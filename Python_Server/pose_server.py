"""
pose_server.py — StrikeSync v13.0
===================================
ARCHITECTURE: All punch detection computed in Python. Unity receives only
final decisions and executes animations.

PUNCH DETECTION PIPELINE (v11 — 3 critical fixes over v10):

  1. FRAME HISTORY BUFFER
     Rolling 6-frame deque per hand stores wrist+shoulder positions + timestamps.

  2. TEMPORAL VELOCITY (3-frame average + peak)
     Peak velocity used to catch fast punches spanning < 2 frames.

  3. INTENT BUFFER
     Arms intent at 80% of PUNCH_VEL_THRESHOLD. A follow-through within
     INTENT_WINDOW_S confirms the punch.

  4. DIRECTIONAL FILTERING — FIXED IN v11, TUNED IN v12
     Dynamic arm-axis dot product (shoulder→wrist direction at motion start).
     FORWARD_DOT_THRESHOLD = 0.35 — real punches show dot 0.56–0.84; 0.35
     rejects pure sideways noise (dot < 0.25) without killing borderline punches.

  5. DISTANCE THRESHOLD
     MIN_PUNCH_DISTANCE = 0.09. Rejects short micro-movements (dist≈0.04–0.07
     were causing false fires in v10).

  6. TWO-STAGE DETECTION
     Stage 1 — ACCELERATION: peak velocity ≥ PUNCH_VEL_THRESHOLD (0.60).
     Stage 2 — EXTENSION: distance ≥ MIN_PUNCH_DISTANCE AND direction correct.

  7. RETRACTION LOCK — NEW IN v11
     After a punch fires, the hand MUST decelerate below RETRACTION_VEL (0.30)
     before another punch can be armed. This is the primary fix for the
     rapid-fire false-positive chain seen in v10 logs (consecutive punches
     on the same continuous motion arc).

  8. COOLDOWN
     PUNCH_COOLDOWN_S = 0.55 per hand.

  9. CLEAN OUTPUT — single-shot booleans.

 10. ANTI-JITTER DEBOUNCE
     PUNCH_CONFIRM_FRAMES = 2 consecutive frames required. Was 3 in v11, lowered
     to compensate for retraction-lock history-clear losing one context frame.

 11. WALK/DEPTH SUPPRESSION ON PUNCH — NEW IN v12
     When a punch fires, move_x AND move_z are forced to 0.0 for exactly
     PUNCH_COOLDOWN_S seconds. This prevents the hip rotation caused by throwing
     a punch from generating a spurious walk signal that arrives in Unity
     alongside (or just after) the punch packet.
     Both compute_move_x() and compute_move_z() check _punch_suppress_until
     before doing any velocity math — if the timestamp is in the future they
     return 0.0 immediately, and also decay the smoothed EMA so there is no
     "burst" of stored velocity when suppression lifts.

TUNING PARAMETERS:
  PUNCH_VEL_THRESHOLD   — min peak velocity (above noise floor ~0.45)
  RETRACTION_VEL        — hand must slow below this after firing (retraction lock)
  MIN_PUNCH_DISTANCE    — min wrist travel distance (normalised)
  FORWARD_DOT_THRESHOLD — how aligned with the push axis (0.55 = strict)
  PUNCH_COOLDOWN_S      — min gap between confirmed punches per hand
                          ALSO used as the walk/depth suppression window on punch
  PUNCH_CONFIRM_FRAMES  — debounce frames required
  HIP_LEAN_THRESHOLD    — hip X-delta that counts as a body lean
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

# ─── Network ──────────────────────────────────────────────────────────────────
MODEL_PATH = 'yolo11n-pose.pt'
CAM_INDEX  = 0
SEND_IP    = "127.0.0.1"
SEND_PORT  = 9001

CAM_WIDTH  = 640
CAM_HEIGHT = 360

INFERENCE_SIZE_DEFAULT = 256
INFERENCE_SIZE_MIN     = 192
INFERENCE_SIZE_MAX     = 320

TARGET_FPS = 20
FRAME_SKIP = 0

MODEL_CONF = 0.45
MODEL_IOU  = 0.45

MAX_PLAYERS = 1

# ─── Landmark smoothing ───────────────────────────────────────────────────────
LANDMARK_ALPHA = 0.3     # EMA alpha for raw keypoint positions

# ─── Jump detection ───────────────────────────────────────────────────────────
JUMP_RISE_THRESHOLD = 0.035
JUMP_COOLDOWN_S     = 0.5

# ─── Walk (v12 — velocity-based, replaces hip-displacement joystick) ─────────
# v11 and earlier used hip X-displacement from a calibrated neutral position.
# This felt like leaning a joystick — unnatural and slow to respond.
#
# v12 uses hip X-VELOCITY: how fast the hips are moving left/right per second.
# Standing still = velocity ≈ 0 → no walk signal.
# Taking a real step = hips accelerate briefly → positive velocity spike → walk.
# Leaning slowly without stepping = low velocity → below threshold → no walk.
#
# This matches how real walking works: it's the movement, not the position.
#
HIP_VEL_WALK_THRESHOLD = 0.035   # normalised hip X-vel needed to start walking
                                  # (hip moves > 1.8% frame width per frame at 30fps)
                                  # real step: ~0.03–0.08 | idle sway: < 0.010
HIP_VEL_EMA_ALPHA      = 0.50    # EMA on hip velocity for smoothing
                                  # higher = more responsive, lower = smoother
HIP_VEL_SCALE          = 18.0    # maps velocity to -1..1 joystick range
                                  # at vel=0.08 (brisk step): 0.08 * 12 = 0.96 ≈ full walk
MOVE_OUTPUT_MIN        = 0.05    # dead-zone on output (unchanged)

NEUTRAL_WARMUP_FRAMES = 8
PERF_WINDOW = 20

# ─── Depth / move_z (Velocity-based) ──────────────────────────────────────────
# v13: Z-movement now uses shoulder-width VELOCITY, matching X-movement.
SW_VEL_WALK_THRESHOLD = 0.006   # Normalised shoulder width velocity needed to walk
SW_VEL_EMA_ALPHA      = 0.40    # Smoothing for Z-velocity
SW_VEL_SCALE          = 25.0    # Maps velocity to -1..1 joystick range

# ─── Punch detection ──────────────────────────────────────────────────────────
# v11 tuning — all values justified in module docstring.

HISTORY_FRAMES       = 6     # rolling frame buffer depth per hand
PUNCH_CONFIRM_FRAMES = 2     # v12: was 3. Retraction lock clears history on reset,
                             # costing one context frame. 2 confirms is still robust
                             # at 30+ fps (66ms window) while compensating for the loss.

# Stage 1 — acceleration gate
# Noise floor on a typical webcam boxing session is ~0.28–0.45 norm/s.
# Setting threshold above the noise floor at 0.60 means only deliberate throws pass.
# Fast punches in logs hit 1.8–6.9; slow intentional punches ~0.8–1.5.
PUNCH_VEL_THRESHOLD = 0.60   # was 0.28 (v10 original) / 0.55 (v10 patched)

# Intent buffer — arms at 80% so a slow build-up primes the detector
INTENT_THRESHOLD  = PUNCH_VEL_THRESHOLD * 0.50   # = 0.48
INTENT_WINDOW_S   = 0.10     # tight window — prevents stale intent causing false fires

# Stage 2 — extension gate
# v10 was 0.04 (26 px at 640w) — too short, micro-movements passed.
# 0.09 = ~58 px at 640w: requires a committed arm extension.
MIN_PUNCH_DISTANCE  = 0.09

# Directional filter — v11 REWRITE (see _compute_displacement for implementation)
# The broken [0,-1] global "hand rises = punch" logic is replaced with a
# per-punch dynamic axis: direction from shoulder to wrist at motion start.
# This threshold now means: wrist must continue extending along that initial axis.
# 0.55 = ~56° cone — tight enough to reject sideways drift, loose enough for hooks.
FORWARD_DOT_THRESHOLD = 0.35   # v12: lowered from 0.55. Real punches in logs show dot 0.56–0.84;
                               # with history-clear-on-reset losing 1 context frame, 0.55 was
                               # too tight. 0.35 still rejects pure sideways noise (dot < 0.3).

# Retraction lock — NEW v11
# After punch fires, hand MUST decelerate below this before arming again.
# Stops consecutive false-positive chains from a single continuous arm motion.
RETRACTION_VEL = 0.30        # normalised units/sec; below this = hand is "reset"

# Lean handling
HIP_LEAN_THRESHOLD   = 0.025  # normalised X hip-delta that triggers lean flush
HIP_LEAN_EMA_ALPHA   = 0.45   # EMA on hip-delta for sustained lean detection
HIP_LEAN_EMA_THRESH  = 0.014  # sustained lean EMA threshold

# Cooldown
PUNCH_COOLDOWN_S = 0.55       # per-hand, seconds.
                               # ALSO doubles as the walk/depth suppression window —
                               # move_x and move_z are zeroed for this duration after
                               # any punch fires. Single source of truth; tune once.

# ─── Debug / Calibration window ───────────────────────────────────────────────
SHOW_DEBUG_WINDOW   = True    # Set False to hide the OpenCV preview window
DEBUG_WINDOW_NAME   = "StrikeSync — Camera Debug (press Q to hide)"
DEBUG_WINDOW_SCALE  = 1.0     # Scale factor for the debug window (0.5 = half size)

# Critical keypoints that must all have visibility > threshold for calibration
# Shoulders(5,6), Elbows(7,8), Wrists(9,10), Hips(11,12) = 8 points
CALIB_KEYPOINTS     = [5, 6, 7, 8, 9, 10, 11, 12]
CALIB_VIS_THRESHOLD = 0.45    # per-keypoint confidence needed
CALIB_HOLD_FRAMES   = 15      # must be stable for this many consecutive frames

# ─── YOLO 17-point keypoint indices ──────────────────────────────────────────
KP_NOSE       = 0
KP_LSHOULDER  = 5
KP_RSHOULDER  = 6
KP_LELBOW     = 7
KP_RELBOW     = 8
KP_LWRIST     = 9
KP_RWRIST     = 10
KP_LHIP       = 11
KP_RHIP       = 12


# ─────────────────────────────────────────────────────────────────────────────
# HAND STATE — frame history + all punch detection logic for one hand
# ─────────────────────────────────────────────────────────────────────────────
class HandState:
    """
    Maintains a rolling buffer of (wrist_pos, shoulder_pos, timestamp) and
    implements the full two-stage punch detection pipeline for a single hand.
    All positions are normalised (0–1) by frame dimensions.
    """
    def __init__(self, side: str):
        self.side   = side    # "left" or "right"
        # Rolling buffer: each entry is (wrist_xy, shoulder_xy, timestamp)
        self.history: deque = deque(maxlen=HISTORY_FRAMES)
        # Intent buffer
        self.intent_time: float = -999.0
        # Cooldown
        self.last_punch_t: float = -999.0
        # Debounce: counts consecutive frames where the punch condition is met
        self._confirm_frames: int = 0
        # One-shot: True only the frame the punch is emitted, then False
        self.punched_this_frame: bool = False
        # v11 — Retraction lock: True after a punch fires until hand decelerates
        self.waiting_for_reset: bool = False

    def push(self, wrist_xy: np.ndarray, shoulder_xy: np.ndarray, ts: float):
        """Add a new frame entry."""
        self.history.append((wrist_xy.copy(), shoulder_xy.copy(), ts))

    def flush(self):
        """Called when lean detected — clear motion history to prevent false punch."""
        self.history.clear()
        self.intent_time = -999.0
        self._confirm_frames = 0
        self.waiting_for_reset = False   # lean-flush cancels retraction lock too

    def _relative_pos(self, entry) -> np.ndarray:
        """Wrist position relative to shoulder (shoulder-space). Cancels body translation."""
        wrist, shoulder, _ = entry
        return wrist - shoulder

    def _compute_velocities(self) -> tuple:
        """
        Returns (instant_vel, avg_vel_3fr, peak_vel_3fr).
        All in normalised-units/second.
        Returns (0, 0, 0) if history is too short.
        """
        h = list(self.history)
        if len(h) < 2:
            return 0.0, 0.0, 0.0

        # Instantaneous: last two frames
        p_cur  = self._relative_pos(h[-1])
        p_prev = self._relative_pos(h[-2])
        dt_inst = h[-1][2] - h[-2][2]
        if dt_inst <= 0:
            dt_inst = 1e-3
        inst_vel = float(np.linalg.norm(p_cur - p_prev) / dt_inst)

        # 3-frame window velocities
        window = h[-min(4, len(h)):]   # up to last 4 entries = 3 intervals
        vels = []
        for i in range(1, len(window)):
            dp  = self._relative_pos(window[i]) - self._relative_pos(window[i-1])
            dtt = window[i][2] - window[i-1][2]
            if dtt > 0:
                vels.append(float(np.linalg.norm(dp) / dtt))

        avg_vel  = float(np.mean(vels))  if vels else inst_vel
        peak_vel = float(max(vels))      if vels else inst_vel

        return inst_vel, avg_vel, peak_vel

    def _compute_displacement(self) -> tuple:
        """
        Returns (total_distance, forward_dot).

        total_distance: L2 distance wrist has travelled (shoulder-relative) from
                        the oldest history frame to the newest.

        forward_dot: how aligned the net displacement is with the PUNCH AXIS —
                     defined as the direction from shoulder → wrist at the START
                     of the history window (i.e. the arm's natural extension axis).

        WHY THIS REPLACES [0, -1]:
          v10 used a global "hand rises = punch" vector [0, -1].  This is wrong
          for a front-facing camera: a jab travels HORIZONTALLY away from the
          shoulder, not upward.  A side-on hook travels upward, but that's not
          the dominant motion we care about.  Using the initial shoulder→wrist
          direction as the forward axis makes the filter body-relative and correct
          for any arm angle.  It also means the filter works regardless of whether
          the player is left- or right-handed or leans.
        """
        h = list(self.history)
        if len(h) < 2:
            return 0.0, 0.0

        # Shoulder-relative wrist positions
        p_start = self._relative_pos(h[0])   # wrist_start - shoulder_start
        p_end   = self._relative_pos(h[-1])  # wrist_end   - shoulder_end

        net_disp   = p_end - p_start
        total_dist = float(np.linalg.norm(net_disp))

        # Dynamic forward axis: shoulder → wrist direction at the start of the window.
        # This is the arm's natural extension direction; a punch extends along it.
        # p_start IS (wrist - shoulder) in shoulder-space, so its direction IS the axis.
        axis_len = float(np.linalg.norm(p_start))
        if axis_len < 1e-4 or total_dist < 1e-6:
            # Degenerate: arm at shoulder or no movement — no valid direction
            return total_dist, 0.0

        forward_vec = p_start / axis_len   # unit vector along the arm at motion start

        dot = float(np.dot(net_disp / total_dist, forward_vec))
        # dot > 0  → wrist moved further along/away from shoulder  (extension = punch)
        # dot < 0  → wrist moved back toward shoulder              (retraction / noise)

        return total_dist, dot

    def update(self, wrist_xy: np.ndarray, shoulder_xy: np.ndarray,
               ts: float, is_leaning: bool) -> bool:
        """
        Full pipeline update.  Returns True if a punch should fire THIS frame (single-shot).
        """
        self.punched_this_frame = False

        # Push new frame first so velocity computation has current data
        self.push(wrist_xy, shoulder_xy, ts)

        # ── v11: RETRACTION LOCK — must decelerate before arming again ─────────
        # After a punch fires, block all new punches until the hand slows down.
        # This is the primary fix for chains of false-positives on a single arm arc.
        if self.waiting_for_reset:
            if len(self.history) >= 2:
                _, _, peak_vel = self._compute_velocities()
                if peak_vel < RETRACTION_VEL:
                    self.waiting_for_reset = False
                    # Also clear history so the next punch starts from a clean slate
                    self.history.clear()
                    print(f"[RESET][{self.side}] hand decelerated → ready")
            return False  # block regardless until reset confirmed

        # If body is leaning and wrist isn't clearly extending forward, flush and skip.
        if is_leaning:
            dist, dot = self._compute_displacement()
            # Only block if clearly NOT a punch
            if dot < 0.2:
                return False

        # Need at least 2 frames for velocity
        if len(self.history) < 2:
            return False

        # ── Stage 1: ACCELERATION (velocity check) ────────────────────────────
        inst_vel, avg_vel, peak_vel = self._compute_velocities()

        # Intent buffer: arm intent if velocity is near threshold
        if peak_vel >= INTENT_THRESHOLD:
            self.intent_time = ts

        stage1_ok = peak_vel >= PUNCH_VEL_THRESHOLD
        # if stage1_ok:
        #     print(f"[DEBUG][{self.side}] vel={peak_vel:.3f}")

        # Grace window: if intent was recently armed and avg is still high
        if not stage1_ok and (ts - self.intent_time) < INTENT_WINDOW_S:
            if avg_vel >= INTENT_THRESHOLD:
                stage1_ok = True

        if not stage1_ok:
            self._confirm_frames = 0
            return False

        # ── Cooldown gate (checked BEFORE confirm frames accumulate) ───────────
        # Prevents confirm frames from ticking up during cooldown, then firing
        # the instant it expires.
        if (ts - self.last_punch_t) < PUNCH_COOLDOWN_S:
            self._confirm_frames = 0
            return False

        # ── Stage 2: EXTENSION (distance + direction) ─────────────────────────
        total_dist, dot = self._compute_displacement()
        stage2_dist = total_dist >= MIN_PUNCH_DISTANCE
        stage2_dir  = dot >= FORWARD_DOT_THRESHOLD

        # if stage2_dist:
        # #     print(f"[DEBUG][{self.side}] dist={total_dist:.3f}")
        # if stage2_dir:
        #     print(f"[DEBUG][{self.side}] dot={dot:.3f}")

        if not (stage2_dist and stage2_dir):
            self._confirm_frames = 0
            return False

        # ── Both stages pass — run debounce ───────────────────────────────────
        self._confirm_frames += 1
        if self._confirm_frames < PUNCH_CONFIRM_FRAMES:
            return False

        # ── FIRE ──────────────────────────────────────────────────────────────
        self.last_punch_t = ts
        self._confirm_frames = 0
        self.intent_time = -999.0
        self.history.clear()
        self.punched_this_frame = True
        self.waiting_for_reset = True   # v11: arm retraction lock immediately
        return True


# ─────────────────────────────────────────────────────────────────────────────
# PLAYER STATE — movement + punch for one player
# ─────────────────────────────────────────────────────────────────────────────
class PlayerState:
    def __init__(self, pid: int):
        self.pid             = pid
        self.ema             = None
        self.bbox            = None
        self.last_hip_y      = None
        self.hip_y_baseline  = None
        self.last_jump_t     = -999.0

        # ── v13: POSITION-BASED "WASD" WALK ──────────────────────────────────
        # Replaces velocity-based detection entirely.
        # neutral_hip_cx: where the hips sit when standing still.
        #   Set on the first frame, then slowly auto-centres inside the deadzone.
        #   Separate from the old recalibrate() neutral (kept for compat).
        # neutral_sw: same concept for shoulder-width (Z axis).
        # Both are None until the first valid keypoint frame.
        self.neutral_hip_cx  = None    # used by compute_move_x (WASD)
        self.neutral_sw      = None    # used by compute_move_z (WASD)
        self._active_walk_x = 0.0
        self._active_walk_z = 0.0
        # Debounce: require N consecutive frames outside the deadzone before
        # committing to a direction. Prevents single-frame keypoint noise from
        # triggering a step. 2 frames at 30fps = 66 ms — imperceptible to player.
        self._walk_x_confirm = 0
        self._walk_z_confirm = 0
        self._walk_x_sign    = 0      # last committed direction (-1, 0, 1)
        self._walk_z_sign    = 0
        self.WALK_CONFIRM_FRAMES = 2  # tune: lower = more responsive, higher = quieter

        # Lean tracking (for punch detection)
        self._last_hip_cx       = None
        self._hip_delta_ema     = 0.0
        self._is_leaning        = False

        # Punch detectors — one per hand
        self.hand_left  = HandState("left")
        self.hand_right = HandState("right")

        # ── v12: WALK / DEPTH SUPPRESSION ON PUNCH ────────────────────────────
        # When a punch fires, both move_x and move_z are zeroed for PUNCH_COOLDOWN_S
        # seconds. Throwing a punch rotates the hips slightly, which the velocity-
        # based walk detector would otherwise interpret as a step. This timestamp
        # gate is the authoritative suppression — it lives in Python so the walk
        # signal is killed BEFORE the packet is sent, not after Unity receives it.
        # Single field, used by both compute_move_x() and compute_move_z().
        self._punch_suppress_until: float = 0.0

        # ── Depth / move_z ────────────────────────────────────────────────────
        # _last_sw kept only for the lean-detection system (update_lean reads sw).
        # Actual walk output now comes from neutral_sw in compute_move_z.
        self._last_sw               = None

    # ── EMA smoothing for IK landmarks ───────────────────────────────────────
    def update_ema(self, kpts_xy: np.ndarray) -> np.ndarray:
        if self.ema is None or self.ema.shape != kpts_xy.shape:
            self.ema = kpts_xy.copy()
        else:
            self.ema = LANDMARK_ALPHA * kpts_xy + (1.0 - LANDMARK_ALPHA) * self.ema
        return self.ema

    # ── Called by detect_punches() when a punch fires ─────────────────────────
    def _arm_punch_suppress(self):
        """
        Starts the walk/depth suppression window.
        Called exactly once per confirmed punch, using PUNCH_COOLDOWN_S as the
        window duration — same constant that governs the per-hand cooldown, so
        both systems are in sync with a single tuning knob.
        v13: also resets the WASD confirm counters so a punch can never
        carry a stale directional vote into the post-suppression window.
        """
        self._punch_suppress_until = time.time() + PUNCH_COOLDOWN_S
        # Reset WASD debounce state so no direction is "pre-voted" after suppression
        self._walk_x_confirm = 0
        self._walk_z_confirm = 0
        self._walk_x_sign    = 0
        self._walk_z_sign    = 0

    # ── Depth movement — WASD Hysteresis (v13.1) ───────────────────────────
    def compute_move_z(self, raw_kpts: np.ndarray, frame_w: int) -> float:
        if time.time() < self._punch_suppress_until:
            self._active_walk_z = 0.0
            return 0.0

        if len(raw_kpts) <= 6:
            return 0.0

        sw = abs(raw_kpts[KP_LSHOULDER][0] - raw_kpts[KP_RSHOULDER][0]) / frame_w

        if getattr(self, 'neutral_sw', None) is None:
            self.neutral_sw = sw
            return 0.0

        if self.neutral_sw < 1e-4:
            return 0.0

        delta_z = (sw - self.neutral_sw) / self.neutral_sw

        # HYSTERESIS: Require big movement to start, but deep return to stop
        if self._active_walk_z == 0.0:
            # We are stopped. Require a large step to start walking.
            if delta_z > 0.14:
                self._active_walk_z = 1.0
            elif delta_z < -0.12:
                self._active_walk_z = -1.0
            elif abs(delta_z) < 0.06:
                # Slowly auto-center only when safely in the deep deadzone
                self.neutral_sw = 0.90 * self.neutral_sw + 0.10 * sw
        else:
            # We are walking. Don't stop until they clearly return near center.
            if self._active_walk_z == 1.0 and delta_z < 0.06:
                self._active_walk_z = 0.0
            elif self._active_walk_z == -1.0 and delta_z > -0.06:
                self._active_walk_z = 0.0

        return self._active_walk_z

    # ── Horizontal movement — WASD Hysteresis (v13.1) ─────────────────────
    def compute_move_x(self, raw_kpts: np.ndarray, frame_w: int) -> float:
        if time.time() < self._punch_suppress_until:
            self._active_walk_x = 0.0
            return 0.0

        if len(raw_kpts) <= 12:
            return 0.0

        hip_cx = (raw_kpts[11][0] + raw_kpts[12][0]) / 2.0 / frame_w

        if getattr(self, 'neutral_hip_cx', None) is None:
            self.neutral_hip_cx = hip_cx
            return 0.0

        delta_x = hip_cx - self.neutral_hip_cx

        # HYSTERESIS: Require big movement to start, but deep return to stop
        if self._active_walk_x == 0.0:
            # We are stopped. Require a firm 8% lean/step to trigger walk.
            if delta_x > 0.08:
                self._active_walk_x = 1.0
            elif delta_x < -0.08:
                self._active_walk_x = -1.0
            elif abs(delta_x) < 0.04:
                # Slowly auto-center only when safely in the deep deadzone
                self.neutral_hip_cx = 0.90 * self.neutral_hip_cx + 0.10 * hip_cx
        else:
            # We are walking. Ignore noise. Don't stop until they cross the 4% line.
            if self._active_walk_x == 1.0 and delta_x < 0.04:
                self._active_walk_x = 0.0
            elif self._active_walk_x == -1.0 and delta_x > -0.04:
                self._active_walk_x = 0.0

        return self._active_walk_x

    # ── Lean detection ────────────────────────────────────────────────────────
    def update_lean(self, raw_kpts: np.ndarray, frame_w: int):
        """
        Updates self._is_leaning based on hip X movement.
        Uses both instantaneous delta and an EMA for sustained leans.
        Must be called BEFORE detect_punches each frame.
        """
        if len(raw_kpts) <= 12:
            self._is_leaning = False
            return

        hip_cx = (raw_kpts[11][0] + raw_kpts[12][0]) / 2.0 / frame_w

        if self._last_hip_cx is None:
            self._last_hip_cx  = hip_cx
            self._is_leaning   = False
            return

        delta = abs(hip_cx - self._last_hip_cx)
        self._last_hip_cx = hip_cx

        # EMA of hip delta for sustained lean
        self._hip_delta_ema = (HIP_LEAN_EMA_ALPHA * delta
                               + (1.0 - HIP_LEAN_EMA_ALPHA) * self._hip_delta_ema)

        instant_lean   = delta > HIP_LEAN_THRESHOLD
        sustained_lean = self._hip_delta_ema > HIP_LEAN_EMA_THRESH

        self._is_leaning = instant_lean or sustained_lean

        # If leaning without a punch-like forward extension, flush both hands
        if self._is_leaning:
            # Hand states will check for forward extension internally —
            # flushing is done inside HandState.update() conditionally.
            pass

    # ── Punch detection ───────────────────────────────────────────────────────
    def detect_punches(self, raw_kpts: np.ndarray, frame_w: int, frame_h: int,
                       ts: float) -> tuple:
        """
        Returns (punch_left: bool, punch_right: bool).
        Each bool is True for exactly ONE frame when a punch is confirmed.
        Uses raw (pre-EMA) keypoints so velocity spikes aren't smoothed away.

        v12: On any confirmed punch, calls _arm_punch_suppress() to freeze
        move_x and move_z output for PUNCH_COOLDOWN_S seconds.
        """
        if len(raw_kpts) < 17:
            return False, False

        # Normalise keypoints
        lw  = raw_kpts[KP_LWRIST]   / np.array([frame_w, frame_h], dtype=float)
        rw  = raw_kpts[KP_RWRIST]   / np.array([frame_w, frame_h], dtype=float)
        ls  = raw_kpts[KP_LSHOULDER]/ np.array([frame_w, frame_h], dtype=float)
        rs  = raw_kpts[KP_RSHOULDER]/ np.array([frame_w, frame_h], dtype=float)

        punch_l = self.hand_left.update(lw, ls, ts, self._is_leaning)
        punch_r = self.hand_right.update(rw, rs, ts, self._is_leaning)

        # ── v12: suppress walk on punch ───────────────────────────────────────
        if punch_l or punch_r:
            self._arm_punch_suppress()

        return punch_l, punch_r

    # ── Jump detection ────────────────────────────────────────────────────────
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

    def recalibrate(self, raw_kpts: np.ndarray, frame_w: int):
        if len(raw_kpts) > 12:
            self.neutral_hip_cx  = (raw_kpts[11][0] + raw_kpts[12][0]) / 2.0 / frame_w
            self.neutral_sw      = abs(raw_kpts[KP_LSHOULDER][0] - raw_kpts[KP_RSHOULDER][0]) / frame_w
            self._active_walk_x  = 0.0
            self._active_walk_z  = 0.0
            print(f"[RECALIBRATED] Player {self.pid} neutral_x={self.neutral_hip_cx:.4f}")


# ─── Fast camera ──────────────────────────────────────────────────────────────
class FastCamera:
    def __init__(self, index, width, height):
        self.cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
        self.cap.set(cv2.CAP_PROP_FRAME_WIDTH,  width)
        self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        self.cap.set(cv2.CAP_PROP_FPS,          30)
        self.cap.set(cv2.CAP_PROP_BUFFERSIZE,   1)
        self.cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc('M','J','P','G'))
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


# ─────────────────────────────────────────────────────────────────────────────
# CALIBRATION EVALUATOR
# ─────────────────────────────────────────────────────────────────────────────
def evaluate_calibration(kpts, confs, frame_w, frame_h, calib_hold_counter):
    """
    Checks whether all critical body joints are visible with sufficient confidence.

    Returns:
        score        (float 0..1) — fraction of critical joints that are visible
        is_ready     (bool)       — True when all joints visible for CALIB_HOLD_FRAMES
        hold_counter (int)        — updated consecutive-good-frame counter
        hint         (str)        — human-readable instruction for the user
    """
    if kpts is None or len(kpts) < 13:
        return 0.0, False, 0, "⚠ Body not detected — step back and face the camera"

    n_visible = 0
    missing_groups = []

    # Check each critical keypoint
    shoulder_ok = (confs[5] > CALIB_VIS_THRESHOLD and confs[6] > CALIB_VIS_THRESHOLD)
    elbow_ok    = (confs[7] > CALIB_VIS_THRESHOLD and confs[8] > CALIB_VIS_THRESHOLD)
    wrist_ok    = (confs[9] > CALIB_VIS_THRESHOLD and confs[10] > CALIB_VIS_THRESHOLD)
    hip_ok      = (confs[11] > CALIB_VIS_THRESHOLD and confs[12] > CALIB_VIS_THRESHOLD)

    for idx in CALIB_KEYPOINTS:
        if idx < len(confs) and confs[idx] > CALIB_VIS_THRESHOLD:
            n_visible += 1

    score = n_visible / len(CALIB_KEYPOINTS)

    if not hip_ok:
        missing_groups.append("hips")
    if not wrist_ok:
        missing_groups.append("wrists/hands")
    if not shoulder_ok:
        missing_groups.append("shoulders")
    if not elbow_ok:
        missing_groups.append("elbows")

    if score >= 1.0:
        calib_hold_counter += 1
        is_ready = calib_hold_counter >= CALIB_HOLD_FRAMES
        hint = "✅ READY — hold still" if not is_ready else "✅ CALIBRATED — FIGHT!"
    else:
        calib_hold_counter = 0
        is_ready = False
        if missing_groups:
            hint = f"Step back — {', '.join(missing_groups)} not visible"
        else:
            hint = "Hold still for a moment"

    return score, is_ready, calib_hold_counter, hint


# ─────────────────────────────────────────────────────────────────────────────
# DEBUG DRAW — annotates the camera frame with skeleton, status, and calibration
# ─────────────────────────────────────────────────────────────────────────────

# YOLO 17-point skeleton connections
_SKELETON_PAIRS = [
    (5, 7), (7, 9),    # left arm
    (6, 8), (8, 10),   # right arm
    (5, 6),            # shoulders
    (5, 11), (6, 12),  # torso sides
    (11, 12),          # hips
    (11, 13), (13, 15),# left leg
    (12, 14), (14, 16),# right leg
]

def draw_debug_frame(frame, kpts, confs, calib_score, calib_ready, calib_hint,
                     move_x, move_z, avg_fps, infer_size, punch_l, punch_r,
                     suppressed: bool = False):
    """
    Draws full debug overlay on the camera frame:
      - Skeleton with colour-coded joint visibility
      - Calibration bar and status text
      - FPS, inference size, move_x/z values
      - Punch flash when a punch fires
      - v12: "WALK SUPPRESSED" indicator during punch window
    """
    out = frame.copy()
    h, w = out.shape[:2]

    # ── Skeleton ──────────────────────────────────────────────────────────────
    if kpts is not None and len(kpts) >= 13:
        # Draw bones
        for (a, b) in _SKELETON_PAIRS:
            if a < len(kpts) and b < len(kpts):
                ax, ay = int(kpts[a][0]), int(kpts[a][1])
                bx, by = int(kpts[b][0]), int(kpts[b][1])
                if ax > 0 and ay > 0 and bx > 0 and by > 0:
                    ca = confs[a] if a < len(confs) else 0
                    cb = confs[b] if b < len(confs) else 0
                    conf_avg = (ca + cb) / 2.0
                    # Green = high confidence, Yellow = medium, Red = low
                    if conf_avg > 0.6:
                        color = (0, 220, 0)
                    elif conf_avg > 0.35:
                        color = (0, 200, 200)
                    else:
                        color = (0, 60, 220)
                    cv2.line(out, (ax, ay), (bx, by), color, 2)

        # Draw joints — filled circle, colour = visibility
        for idx, (x, y) in enumerate(kpts):
            if x <= 0 and y <= 0:
                continue
            conf = confs[idx] if idx < len(confs) else 0
            is_critical = idx in CALIB_KEYPOINTS
            if conf > CALIB_VIS_THRESHOLD:
                dot_color = (0, 255, 0) if is_critical else (255, 255, 255)
            else:
                dot_color = (0, 0, 255) if is_critical else (120, 120, 120)
            radius = 6 if is_critical else 4
            cv2.circle(out, (int(x), int(y)), radius, dot_color, -1)

    # ── Calibration bar ───────────────────────────────────────────────────────
    bar_x, bar_y, bar_w, bar_h = 10, 10, 200, 22
    cv2.rectangle(out, (bar_x, bar_y), (bar_x + bar_w, bar_y + bar_h), (50, 50, 50), -1)
    filled = int(bar_w * calib_score)
    bar_color = (0, 220, 0) if calib_ready else ((0, 200, 200) if calib_score > 0.5 else (0, 60, 220))
    cv2.rectangle(out, (bar_x, bar_y), (bar_x + filled, bar_y + bar_h), bar_color, -1)
    cv2.rectangle(out, (bar_x, bar_y), (bar_x + bar_w, bar_y + bar_h), (200, 200, 200), 1)
    cv2.putText(out, f"Calibration: {int(calib_score * 100)}%",
                (bar_x, bar_y + bar_h + 16), cv2.FONT_HERSHEY_SIMPLEX, 0.5, (220, 220, 220), 1)

    # ── Status hint ───────────────────────────────────────────────────────────
    hint_color = (0, 255, 80) if calib_ready else (0, 200, 255)
    cv2.putText(out, calib_hint, (10, bar_y + bar_h + 38),
                cv2.FONT_HERSHEY_SIMPLEX, 0.52, hint_color, 1, cv2.LINE_AA)

    # ── Perf stats ────────────────────────────────────────────────────────────
    stats = f"FPS:{avg_fps:.0f}  infer:{infer_size}  X:{move_x:+.3f}  Z:{move_z:+.3f}"
    cv2.putText(out, stats, (10, h - 10),
                cv2.FONT_HERSHEY_SIMPLEX, 0.45, (180, 180, 180), 1, cv2.LINE_AA)

    # ── v12: Walk suppression indicator ──────────────────────────────────────
    if suppressed:
        cv2.putText(out, "WALK LOCKED", (10, h - 28),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.48, (0, 140, 255), 1, cv2.LINE_AA)

    # ── Punch flash ───────────────────────────────────────────────────────────
    if punch_l:
        cv2.putText(out, "LEFT PUNCH!", (w // 2 - 80, h // 2),
                    cv2.FONT_HERSHEY_DUPLEX, 1.2, (0, 100, 255), 3, cv2.LINE_AA)
    if punch_r:
        cv2.putText(out, "RIGHT PUNCH!", (w // 2 - 90, h // 2 + 50),
                    cv2.FONT_HERSHEY_DUPLEX, 1.2, (255, 100, 0), 3, cv2.LINE_AA)

    # ── Legend (bottom-right) ─────────────────────────────────────────────────
    legend = [
        ("Green dot = visible critical joint", (0, 200, 0)),
        ("Blue dot  = low-conf critical joint", (0, 60, 220)),
        ("White dot = non-critical joint",      (200, 200, 200)),
        ("Press Q to close this window",        (150, 150, 150)),
    ]
    for i, (txt, col) in enumerate(legend):
        cv2.putText(out, txt, (w - 310, h - 10 - i * 18),
                    cv2.FONT_HERSHEY_SIMPLEX, 0.38, col, 1, cv2.LINE_AA)

    return out


# ─── Main ─────────────────────────────────────────────────────────────────────
def main():
    global SHOW_DEBUG_WINDOW
    global MAX_PLAYERS

    print("\n[INIT] StrikeSync Pose Server v13.0 starting...")
    print("[ARCH] Python-side punch detection ACTIVE")

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

    # ── Calibration state ─────────────────────────────────────────────────────
    calib_hold_counter = 0
    calib_ready        = False
    calib_score        = 0.0
    calib_hint         = "Stand in frame — full body visible"
    _last_punch_l      = False
    _last_punch_r      = False
    _punch_flash_timer = 0.0
    PUNCH_FLASH_S      = 0.3   # how long punch flash stays on screen

    if SHOW_DEBUG_WINDOW:
        cv2.namedWindow(DEBUG_WINDOW_NAME, cv2.WINDOW_NORMAL)
        print(f"[DEBUG] Camera window open: '{DEBUG_WINDOW_NAME}'")

    print("=" * 60)
    print("🚀 STRIKESYNC SERVER v13.0 — Python Punch Detection")
    print(f"   Device          : {DEVICE.upper()}")
    print(f"   Punch vel thr   : {PUNCH_VEL_THRESHOLD:.3f} norm/s")
    print(f"   Retraction vel  : {RETRACTION_VEL:.3f} norm/s  (reset gate)")
    print(f"   Intent thr      : {INTENT_THRESHOLD:.3f} norm/s  window={INTENT_WINDOW_S}s")
    print(f"   Min distance    : {MIN_PUNCH_DISTANCE:.3f} norm")
    print(f"   Forward dot thr : {FORWARD_DOT_THRESHOLD:.2f}  (dynamic arm-axis)")
    print(f"   Cooldown        : {PUNCH_COOLDOWN_S}s per hand")
    print(f"   Walk suppress   : {PUNCH_COOLDOWN_S}s on punch (move_x + move_z zeroed)")
    print(f"   Confirm frames  : {PUNCH_CONFIRM_FRAMES}")
    print(f"   Walk mode       : velocity-based (thr={HIP_VEL_WALK_THRESHOLD:.3f} norm/frame)")
    print("   Press Ctrl+C to stop")
    print("=" * 60)

    try:
        while True:
            t0 = time.time()

            try:
                frame = frame_queue.get_nowait()
            except queue.Empty:
                # No sleep — busy-wait keeps latency minimal on a dedicated process
                continue

            model.overrides['imgsz'] = infer_size[0]

            try:
                results = model(frame, augment=False, verbose=False)[0]
            except Exception:
                continue

            # Timestamp AFTER inference — 'now' represents the actual moment
            # keypoints are valid, giving punch velocity the freshest possible dt.
            now = time.time()

            packet  = {"players": []}
            move_x  = 0.0
            move_z  = 0.0
            punch_l = False
            punch_r = False
            assigned = []   # keep in scope for debug window access below

            if results.keypoints is not None and len(results.keypoints) > 0:
                frame_h, frame_w = frame.shape[:2]
                kpts_xy   = results.keypoints.xy.cpu().numpy()
                kpts_conf = results.keypoints.conf.cpu().numpy()

                raw_count    = min(len(kpts_xy), MAX_PLAYERS)
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

                    # EMA for IK landmarks
                    smoothed = ps.update_ema(kpts)

                    # ── PUNCH DETECTION (Python-side) ─────────────────────────
                    # Step 1: update lean state FIRST (uses raw kpts for accuracy)
                    ps.update_lean(kpts, frame_w)

                    # Step 2: detect punches using RAW keypoints (not EMA-smoothed)
                    # Raw kpts preserve velocity spikes; EMA would blur them away.
                    # v12: _arm_punch_suppress() is called inside detect_punches()
                    #      immediately when a punch fires — BEFORE compute_move_x/z.
                    punch_l, punch_r = ps.detect_punches(kpts, frame_w, frame_h, now)

                    if punch_l:
                        print(f"[PUNCH] P{slot_idx} LEFT  "
                              f"vel_thr={PUNCH_VEL_THRESHOLD:.2f} [v12]")
                        _last_punch_l = True
                        _punch_flash_timer = PUNCH_FLASH_S
                    if punch_r:
                        print(f"[PUNCH] P{slot_idx} RIGHT "
                              f"vel_thr={PUNCH_VEL_THRESHOLD:.2f} [v12]")
                        _last_punch_r = True
                        _punch_flash_timer = PUNCH_FLASH_S

                    # Movement — computed AFTER punch detection so suppression
                    # is already armed when compute_move_x/z check the timestamp.
                    # Movement — computed AFTER punch detection
                    move_x = ps.compute_move_x(kpts, frame_w)
                    move_z = ps.compute_move_z(kpts, frame_w)

                    # 🔥 THE PRE-PUNCH FLINCH FIX (YOU MISSED THIS!) 🔥
                    # If arms are winding up (intent), mute the legs BEFORE sending to Unity!
                    # This prevents the hip-twist from triggering a walk animation.
                    intent_l = (now - ps.hand_left.intent_time) < INTENT_WINDOW_S
                    intent_r = (now - ps.hand_right.intent_time) < INTENT_WINDOW_S

                    if intent_l or intent_r:
                        move_x = 0.0
                        move_z = 0.0
                        ps.smoothed_move_x = 0.0
                        ps.smoothed_move_z = 0.0
                        ps._last_emitted_sign = 0
                        ps._z_last_emitted_sign = 0

                    # Jump
                    jumped = ps.detect_jump(smoothed, frame_h)

                    # ── Calibration evaluation ────────────────────────────────
                    calib_score, calib_ready, calib_hold_counter, calib_hint = \
                        evaluate_calibration(kpts, confs, frame_w, frame_h, calib_hold_counter)

                    # Build landmark list for IK (smoothed, as before)
                    # Pre-allocated for speed — avoids repeated list.append() overhead
                    n_kpts = len(smoothed)
                    landmarks = [None] * n_kpts
                    inv_w = 1.0 / frame_w
                    inv_h = 1.0 / frame_h
                    for j, (x, y) in enumerate(smoothed):
                        landmarks[j] = {
                            "x": x * inv_w,
                            "y": y * inv_h,
                            "z": 0.0,
                            "v": float(confs[j] if j < n_kpts else 0.5),
                        }

                    packet["players"].append({
                        "id":           slot_idx,
                        "landmarks":    landmarks,
                        "move_x":       round(move_x, 4),
                        "move_z":       round(move_z, 4),
                        "jumped":       jumped,
                        "punch_left":   punch_l,
                        "punch_right":  punch_r,
                        "lw_vel":      -1.0,   # sentinel: -1 = v3 packet with valid move fields
                        "rw_vel":       0.0,
                        "calib_score":  round(float(calib_score), 3),
                        "calib_ready":  bool(calib_ready),
                    })

            # ── SEND immediately — before any perf accounting ────────────────
            try:
                sock.sendto(orjson.dumps(packet, option=orjson.OPT_SERIALIZE_NUMPY), (SEND_IP, SEND_PORT))
            except Exception as e:
                print(f"[NETWORK ERROR] Failed to send: {e}")

            # ── Debug window ─────────────────────────────────────────────────
            if SHOW_DEBUG_WINDOW:
                # Decay punch flash timer
                _punch_flash_timer = max(0.0, _punch_flash_timer - (time.time() - t0))
                show_pl = _last_punch_l and _punch_flash_timer > 0
                show_pr = _last_punch_r and _punch_flash_timer > 0
                if _punch_flash_timer <= 0:
                    _last_punch_l = False
                    _last_punch_r = False

                avg_fps_disp = len(fps_times) / sum(fps_times) if fps_times else 0

                # Get kpts/confs for display (use last detected player)
                disp_kpts  = None
                disp_confs = None
                is_suppressed = False
                if packet["players"]:
                    ps0 = player_states[0]
                    if ps0.ema is not None:
                        disp_kpts  = ps0.ema
                        if len(assigned) > 0 and assigned[0] is not None:
                            disp_confs = assigned[0][1]
                    is_suppressed = time.time() < ps0._punch_suppress_until

                disp_frame = draw_debug_frame(
                    frame, disp_kpts, disp_confs,
                    calib_score, calib_ready, calib_hint,
                    move_x if packet["players"] else 0.0,
                    move_z if packet["players"] else 0.0,
                    avg_fps_disp, infer_size[0],
                    show_pl, show_pr,
                    suppressed=is_suppressed,
                )

                if DEBUG_WINDOW_SCALE != 1.0:
                    dh, dw = disp_frame.shape[:2]
                    disp_frame = cv2.resize(disp_frame,
                        (int(dw * DEBUG_WINDOW_SCALE), int(dh * DEBUG_WINDOW_SCALE)))

                cv2.imshow(DEBUG_WINDOW_NAME, disp_frame)
                key = cv2.waitKey(1) & 0xFF
                if key == ord('q') or key == ord('Q'):
                    cv2.destroyAllWindows()
                    SHOW_DEBUG_WINDOW = False
                    print("[DEBUG] Window closed by user (Q pressed)")

            # loop_time: full wall time from frame-grab to send
            loop_time = time.time() - t0
            fps_times.append(loop_time)

            if len(fps_times) >= 10:
                avg_fps = len(fps_times) / sum(fps_times)
                if avg_fps < TARGET_FPS * 0.75:
                    if infer_size[0] > INFERENCE_SIZE_MIN:
                        infer_size[0] = max(INFERENCE_SIZE_MIN, infer_size[0] - 32)
                elif avg_fps > TARGET_FPS * 1.15:
                    if skip_ref[0] > 0:
                        skip_ref[0] -= 1
                    elif infer_size[0] < INFERENCE_SIZE_MAX:
                        infer_size[0] = min(INFERENCE_SIZE_MAX, infer_size[0] + 32)

            if time.time() - last_report >= 2.0:
                avg_fps = len(fps_times) / sum(fps_times) if fps_times else 0
                status  = "✅" if avg_fps >= TARGET_FPS * 0.9 else "⚡"
                p0      = player_states[0]
                m_str   = f"{move_x:+.3f}"
                z_str   = f"{move_z:+.3f}"
                lean_str = "LEAN" if p0._is_leaning else "still"
                supp_str = " [WALK-LOCK]" if time.time() < p0._punch_suppress_until else ""
                print(f"[PERF] {avg_fps:.1f} FPS | infer={infer_size[0]} | "
                      f"players={len(packet['players'])} | "
                      f"move_x={m_str} move_z={z_str} | body={lean_str} {status}{supp_str}")
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
        cv2.destroyAllWindows()
        sock.close()
        print("[CLEANUP] Done.")


if __name__ == "__main__":
    main()