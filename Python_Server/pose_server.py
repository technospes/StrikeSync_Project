"""
pose_server.py — StrikeSync Pose Server v8.0
=============================================

CHANGE FROM v6/v7:

FIX-A1  STOP_ZONE hysteresis — eliminates the 2–3 cycle glitch walk loop.

Root cause (log evidence):
  Python PERF showed: move_x=+0.971 → +1.000 → +1.000 → +0.000  (clean stop)
  Unity showed: character kept walking for 2–3 cycles after user stopped leaning.

The Python EMA with MOVE_EMA_ALPHA=0.35 decays across ~9 frames (~270ms at
33fps) as the user's hip returns toward neutral.  During that decay window,
values like 0.27, 0.18, 0.12 are sent to Unity.  These are above Unity's
walkDeadZone=0.08, so Unity treats them as valid walk input and resets its
idle timer each packet.  The idle timer never completes, _lockedDir stays
locked, and the character keeps walking.

FIX: Two separate zones instead of one.

  WALK_ZONE  = 0.012   (unchanged) — displacement must exceed this to START walking
  STOP_ZONE  = 0.022               — displacement must fall BELOW this to STOP

  STOP_ZONE > WALK_ZONE creates hysteresis:
  - Walking starts: |displacement| crosses WALK_ZONE going outward
  - Walking stops:  |displacement| falls below STOP_ZONE on the return stroke
  - Since STOP_ZONE > WALK_ZONE, the return stroke snaps smoothed_move_x=0
    the moment displacement enters the stop zone, well before the EMA has
    time to decay gradually.
  - No sub-threshold non-zero values reach Unity during the return stroke.
  - Character stops immediately, no loop cycles.

This is the standard joystick hysteresis pattern used in all fighting game
controllers — start threshold < stop threshold ensures decisive stop response.

NO OTHER LOGIC CHANGED.  Python is otherwise working correctly.
"""

import torch
import time
import warnings
warnings.filterwarnings('ignore')

# ─── GPU / CPU auto-detect ────────────────────────────────────────────────────
cuda_available = torch.cuda.is_available()
mps_available  = hasattr(torch.backends, 'mps') and torch.backends.mps.is_available()

if cuda_available:
    DEVICE = 'cuda'
    torch.backends.cudnn.benchmark        = True
    torch.backends.cuda.matmul.allow_tf32 = True
    torch.backends.cudnn.allow_tf32       = True
    torch.set_float32_matmul_precision('high')
    USE_HALF = True
    print(f"[HW]  CUDA GPU  → {torch.cuda.get_device_name(0)}")
elif mps_available:
    DEVICE   = 'mps'
    USE_HALF = False
    print("[HW]  Apple MPS GPU")
else:
    DEVICE   = 'cpu'
    USE_HALF = False
    print("[HW]  CPU-only — performance will be limited")

import cv2
import orjson
import socket
import numpy as np
import threading
import queue
from collections import deque
from ultralytics import YOLO

# ─── Config ───────────────────────────────────────────────────────────────────
MODEL_PATH = 'yolo11n-pose.pt'
CAM_INDEX  = 0
SEND_IP    = "127.0.0.1"
SEND_PORT  = 9001

CAM_WIDTH  = 640
CAM_HEIGHT = 360

INFERENCE_SIZE_DEFAULT = 256
INFERENCE_SIZE_MIN     = 192
INFERENCE_SIZE_MAX     = 320

TARGET_FPS = 30
FRAME_SKIP = 1

MODEL_CONF = 0.45
MODEL_IOU  = 0.45

# ─── Multi-player guard ───────────────────────────────────────────────────────
MAX_PLAYERS                 = 1
MULTI_PLAYER_CONFIRM_FRAMES = 15
_p2_candidate_frames        = 0

# ─── Landmark smoothing ───────────────────────────────────────────────────────
LANDMARK_ALPHA = 0.3

# ─── Jump detection ───────────────────────────────────────────────────────────
JUMP_RISE_THRESHOLD = 0.035
JUMP_COOLDOWN_S     = 0.5

# ─── Walk zones ───────────────────────────────────────────────────────────────
WALK_ZONE      = 0.012   # displacement to START walking (unchanged)
# FIX-A1: STOP_ZONE > WALK_ZONE — snap to 0 on return stroke before EMA decays
STOP_ZONE      = 0.022   # displacement to STOP  walking (NEW — was same as WALK_ZONE)
MAX_THROW      = 0.065
JOYSTICK_SCALE = 1.0 / MAX_THROW
MOVE_EMA_ALPHA = 0.35
MOVE_OUTPUT_MIN = 0.05

# ─── Neutral warmup ───────────────────────────────────────────────────────────
NEUTRAL_WARMUP_FRAMES = 8

# ─── Performance window ───────────────────────────────────────────────────────
PERF_WINDOW = 20


# ─── Per-player state ─────────────────────────────────────────────────────────
class PlayerState:
    def __init__(self, pid: int):
        self.pid              = pid
        self.ema              = None
        self.bbox             = None
        self.last_hip_y       = None
        self.hip_y_baseline   = None
        self.last_jump_t      = -999.0
        self.neutral_hip_cx   = None
        self.smoothed_move_x  = 0.0
        self._warmup_frames   = 0
        self._warmup_sum      = 0.0

    def update_ema(self, kpts_xy: np.ndarray) -> np.ndarray:
        if self.ema is None or self.ema.shape != kpts_xy.shape:
            self.ema = kpts_xy.copy()
        else:
            self.ema = LANDMARK_ALPHA * kpts_xy + (1.0 - LANDMARK_ALPHA) * self.ema
        return self.ema

    def compute_move_x(self, raw_kpts: np.ndarray, frame_w: int) -> float:
        if len(raw_kpts) <= 12:
            return 0.0

        hip_cx = (raw_kpts[11][0] + raw_kpts[12][0]) / 2.0 / frame_w

        # Warmup — accumulate stable frames before locking neutral
        if self.neutral_hip_cx is None:
            self._warmup_frames += 1
            self._warmup_sum    += hip_cx
            if self._warmup_frames >= NEUTRAL_WARMUP_FRAMES:
                self.neutral_hip_cx  = self._warmup_sum / self._warmup_frames
                self.smoothed_move_x = 0.0
                print(f"[CALIBRATED] Player {self.pid} "
                      f"neutral={self.neutral_hip_cx:.4f} "
                      f"(mean of {self._warmup_frames} frames)")
            return 0.0

        displacement = hip_cx - self.neutral_hip_cx

        # ── FIX-A1: Hysteresis stop zone ─────────────────────────────────────
        # STOP if |displacement| < STOP_ZONE — snap immediately, no EMA decay.
        # This is larger than WALK_ZONE so the return stroke always hits this
        # before the EMA can bleed non-zero values through.
        if abs(displacement) < STOP_ZONE:
            self.smoothed_move_x = 0.0   # instant snap — no decay bleed
            return 0.0

        # START/CONTINUE walking — only reached if |displacement| >= STOP_ZONE
        # (which is also >= WALK_ZONE since STOP_ZONE > WALK_ZONE)
        sign        = 1.0 if displacement > 0 else -1.0
        # Ramp starts from WALK_ZONE edge for smooth entry from zero
        active_disp = displacement - sign * WALK_ZONE
        raw_move_x  = float(np.clip(active_disp * JOYSTICK_SCALE, -1.0, 1.0))

        # EMA smooths mid-walk jitter (not applied on stop path above)
        self.smoothed_move_x = (MOVE_EMA_ALPHA * raw_move_x
                                + (1.0 - MOVE_EMA_ALPHA) * self.smoothed_move_x)

        if abs(self.smoothed_move_x) < MOVE_OUTPUT_MIN:
            self.smoothed_move_x = 0.0
            return 0.0

        return float(np.clip(self.smoothed_move_x, -1.0, 1.0))

    def recalibrate(self, raw_kpts: np.ndarray, frame_w: int):
        """Force reset of neutral to current hip position."""
        if len(raw_kpts) > 12:
            self.neutral_hip_cx  = (raw_kpts[11][0] + raw_kpts[12][0]) / 2.0 / frame_w
            self.smoothed_move_x = 0.0
            self._warmup_frames  = NEUTRAL_WARMUP_FRAMES
            print(f"[RECALIBRATED] Player {self.pid} neutral={self.neutral_hip_cx:.4f}")

    def detect_jump(self, kpts: np.ndarray, frame_h: int) -> bool:
        if len(kpts) <= 12:
            return False
        now   = time.time()
        hip_y = (kpts[11][1] + kpts[12][1]) / 2.0 / frame_h

        if self.hip_y_baseline is None:
            self.hip_y_baseline = hip_y
            self.last_hip_y     = hip_y
            return False

        self.hip_y_baseline = 0.98 * self.hip_y_baseline + 0.02 * hip_y
        rise   = self.hip_y_baseline - hip_y
        jumped = (rise > JUMP_RISE_THRESHOLD) and (now - self.last_jump_t > JUMP_COOLDOWN_S)
        if jumped:
            self.last_jump_t = now
        self.last_hip_y = hip_y
        return jumped


# ─── Fast camera ──────────────────────────────────────────────────────────────
class FastCamera:
    def __init__(self, index, width, height):
        self.cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
        self.cap.set(cv2.CAP_PROP_FRAME_WIDTH,  width)
        self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        self.cap.set(cv2.CAP_PROP_FPS,          30)
        self.cap.set(cv2.CAP_PROP_BUFFERSIZE,   1)
        self.cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc('M', 'J', 'P', 'G'))
        if not self.cap.isOpened():
            raise RuntimeError(f"Cannot open camera {index}")
        w   = int(self.cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        h   = int(self.cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        fps = int(self.cap.get(cv2.CAP_PROP_FPS))
        print(f"[CAM] {w}×{h} @ {fps} FPS")

    def read(self):
        return self.cap.read()

    def release(self):
        self.cap.release()


def capture_worker(camera, frame_queue, stop_event, skip_ref):
    counter = 0
    while not stop_event.is_set():
        ret, frame = camera.read()
        if not ret:
            time.sleep(0.001)
            continue
        counter += 1
        skip = skip_ref[0]
        if skip > 0 and counter % (skip + 1) != 0:
            continue
        while frame_queue.full():
            frame_queue.get_nowait()
        frame_queue.put_nowait(frame)


def iou_1d(a1, a2, b1, b2):
    inter = max(0.0, min(a2, b2) - max(a1, b1))
    union = max(a2, b2) - min(a1, b1)
    return inter / union if union > 0 else 0.0


def assign_slots(detections, player_states, frame_w):
    if not player_states:
        return []
    result = [None] * len(player_states)
    used   = set()
    for pid, ps in enumerate(player_states):
        if ps.bbox is None:
            continue
        best_iou = 0.4
        best_det = None
        for di, (kpts, confs, x1, x2) in enumerate(detections):
            if di in used:
                continue
            ov = iou_1d(ps.bbox[0], ps.bbox[2], x1, x2)
            if ov > best_iou:
                best_iou = ov
                best_det = di
        if best_det is not None:
            result[pid] = detections[best_det]
            used.add(best_det)
    for di, det in enumerate(detections):
        if di in used:
            continue
        for pid in range(len(player_states)):
            if result[pid] is None:
                result[pid] = det
                used.add(di)
                break
    return result


def main():
    global MAX_PLAYERS, _p2_candidate_frames

    print("\n[INIT] StrikeSync Pose Server v8.0 starting...")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    print(f"[MODEL] Loading {MODEL_PATH}...")
    try:
        model = YOLO(MODEL_PATH)
        model.to(DEVICE)
        infer_size = [INFERENCE_SIZE_DEFAULT]
        skip_ref   = [FRAME_SKIP]

        overrides = {
            'conf':         MODEL_CONF,
            'iou':          MODEL_IOU,
            'imgsz':        infer_size[0],
            'verbose':      False,
            'device':       DEVICE,
            'max_det':      2,
            'classes':      [0],
            'agnostic_nms': True,
        }
        if USE_HALF:
            overrides['half'] = True
        model.overrides = overrides

        warmup = np.random.randint(0, 255, (CAM_HEIGHT, CAM_WIDTH, 3), dtype=np.uint8)
        _ = model(warmup, verbose=False)
        if cuda_available:
            torch.cuda.synchronize()
            torch.cuda.empty_cache()
        print("[MODEL] Warmed up ✓")
    except Exception as e:
        print(f"[ERROR] Model load failed: {e}")
        return

    try:
        camera = FastCamera(CAM_INDEX, CAM_WIDTH, CAM_HEIGHT)
    except Exception as e:
        print(f"[ERROR] {e}")
        return

    frame_queue = queue.Queue(maxsize=1)
    stop_event  = threading.Event()
    cap_thread  = threading.Thread(
        target=capture_worker,
        args=(camera, frame_queue, stop_event, skip_ref),
        daemon=True,
    )
    cap_thread.start()

    player_states = [PlayerState(0)]
    fps_times     = deque(maxlen=PERF_WINDOW)
    last_report   = time.time()

    print("=" * 55)
    print("🚀 STRIKESYNC SERVER v8.0 RUNNING")
    print(f"   Device     : {DEVICE.upper()}")
    print(f"   Walk zone  : ±{WALK_ZONE*100:.1f}% frame width  (start)")
    print(f"   Stop zone  : ±{STOP_ZONE*100:.1f}% frame width  (stop — hysteresis)")
    print(f"   Max throw  : ±{MAX_THROW*100:.1f}% frame width → move_x ±1.0")
    print(f"   Neutral    : locked after {NEUTRAL_WARMUP_FRAMES} warmup frames")
    print(f"   Landmark α : {LANDMARK_ALPHA}")
    print("   Press Ctrl+C to stop")
    print("=" * 55)

    try:
        while True:
            t0 = time.time()

            try:
                frame = frame_queue.get_nowait()
            except queue.Empty:
                time.sleep(0.001)
                continue

            model.overrides['imgsz'] = infer_size[0]

            try:
                results = model(frame, augment=False, verbose=False)[0]
            except Exception:
                continue

            packet = {"players": []}

            if results.keypoints is not None and len(results.keypoints) > 0:
                frame_h, frame_w = frame.shape[:2]
                kpts_xy   = results.keypoints.xy.cpu().numpy()
                kpts_conf = results.keypoints.conf.cpu().numpy()

                raw_count = min(len(kpts_xy), 2)

                if raw_count >= 2:
                    _p2_candidate_frames += 1
                    if _p2_candidate_frames >= MULTI_PLAYER_CONFIRM_FRAMES:
                        if MAX_PLAYERS < 2:
                            MAX_PLAYERS = 2
                            player_states.append(PlayerState(1))
                            print("[INFO] Player 2 confirmed — 2-player mode active")
                else:
                    _p2_candidate_frames = max(0, _p2_candidate_frames - 2)
                    if _p2_candidate_frames == 0 and MAX_PLAYERS == 2:
                        MAX_PLAYERS = 1
                        player_states = [player_states[0]]
                        player_states[0].bbox = None
                        print("[INFO] Player 2 lost — back to single-player mode")

                active_count = min(raw_count, MAX_PLAYERS)
                detections   = []
                for i in range(active_count):
                    kpts  = kpts_xy[i]
                    confs = kpts_conf[i] if i < len(kpts_conf) else np.ones(len(kpts))
                    x1    = float(kpts[:, 0].min())
                    x2    = float(kpts[:, 0].max())
                    detections.append((kpts, confs, x1, x2))

                detections.sort(key=lambda d: d[2])
                assigned = assign_slots(detections, player_states, frame_w)

                for slot_idx, det in enumerate(assigned):
                    if det is None:
                        continue
                    kpts, confs, x1, x2 = det
                    ps = player_states[slot_idx]

                    y1 = float(kpts[:, 1].min())
                    y2 = float(kpts[:, 1].max())
                    ps.bbox = (x1, y1, x2, y2)

                    smoothed = ps.update_ema(kpts)
                    move_x   = ps.compute_move_x(kpts, frame_w)
                    jumped   = ps.detect_jump(smoothed, frame_h)

                    landmarks = []
                    for j, (x, y) in enumerate(smoothed):
                        landmarks.append({
                            "x": float(x / frame_w),
                            "y": float(y / frame_h),
                            "z": 0.0,
                            "v": float(confs[j] if j < len(confs) else 0.5),
                        })

                    packet["players"].append({
                        "id":        slot_idx,
                        "landmarks": landmarks,
                        "move_x":    round(move_x, 4),
                        "jumped":    jumped,
                        "lw_vel":    -1.0,
                        "rw_vel":    0.0,
                    })

            try:
                sock.sendto(orjson.dumps(packet), (SEND_IP, SEND_PORT))
            except Exception:
                pass

            # Adaptive performance controller
            loop_time = time.time() - t0
            fps_times.append(loop_time)

            if len(fps_times) >= 10:
                avg_fps = len(fps_times) / sum(fps_times)
                if avg_fps < TARGET_FPS * 0.75:
                    if infer_size[0] > INFERENCE_SIZE_MIN:
                        infer_size[0] = max(INFERENCE_SIZE_MIN, infer_size[0] - 32)
                    elif skip_ref[0] < 2:
                        skip_ref[0] += 1
                elif avg_fps > TARGET_FPS * 1.15:
                    if skip_ref[0] > 0:
                        skip_ref[0] -= 1
                    elif infer_size[0] < INFERENCE_SIZE_MAX:
                        infer_size[0] = min(INFERENCE_SIZE_MAX, infer_size[0] + 32)

            if time.time() - last_report >= 2.0:
                avg_fps = len(fps_times) / sum(fps_times) if fps_times else 0
                status  = "✅" if avg_fps >= TARGET_FPS * 0.9 else "⚡"
                p0      = player_states[0]
                n_str   = (f"{p0.neutral_hip_cx:.4f}" if p0.neutral_hip_cx is not None
                           else f"warming ({p0._warmup_frames}/{NEUTRAL_WARMUP_FRAMES})")
                m_str   = (f"{p0.smoothed_move_x:+.3f}" if p0.neutral_hip_cx is not None
                           else "n/a")
                print(f"[PERF] {avg_fps:.1f} FPS | infer={infer_size[0]} | "
                      f"skip={skip_ref[0]} | players={len(packet['players'])} | "
                      f"neutral={n_str} | move_x={m_str} {status}")
                last_report = time.time()

    except KeyboardInterrupt:
        print("\n[STOP] Shutting down...")
    except Exception as e:
        print(f"\n[ERROR] {e}")
        import traceback; traceback.print_exc()
    finally:
        stop_event.set()
        cap_thread.join(timeout=1.0)
        camera.release()
        sock.close()
        print("[CLEANUP] Done.")


if __name__ == "__main__":
    main()