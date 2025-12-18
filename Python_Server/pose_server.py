import torch
import time
import warnings
warnings.filterwarnings('ignore')

print("=" * 60)
print("🚀 YOLO11 ULTIMATE OPTIMIZED POSE SERVER")
print("=" * 60)

cuda_available = torch.cuda.is_available()
if cuda_available:
    print(f"GPU: {torch.cuda.get_device_name(0)}")
    # MAXIMUM PERFORMANCE OPTIMIZATIONS
    torch.backends.cudnn.benchmark = True
    torch.backends.cuda.matmul.allow_tf32 = True
    torch.backends.cudnn.allow_tf32 = True
    torch.set_float32_matmul_precision('high')
else:
    print("[ERROR] NO CUDA!")

print("=" * 60)

import cv2
import orjson
import socket
import numpy as np
import threading
import queue
from collections import deque
from ultralytics import YOLO
#-----------------------------------Config-----------------------------#
MODEL_PATH = 'yolo11n-pose.pt'
CAM_INDEX = 0
SEND_IP = "127.0.0.1"
SEND_PORT = 9001
MAX_PLAYERS = 2
CAM_WIDTH = 640      # Good for 2 players
CAM_HEIGHT = 360     # 16:9 aspect ratio - FASTER than 480p
INFERENCE_SIZE = 256 # Optimal for speed while maintaining accuracy
FRAME_SKIP = 1       # Process every other frame
MODEL_CONF = 0.4     # Balanced confidence
MODEL_IOU = 0.5      # Good overlap handling

PERF_WINDOW = 10
class FastCamera:
    def __init__(self, index, width, height):
        self.cap = cv2.VideoCapture(index, cv2.CAP_DSHOW)
        self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        self.cap.set(cv2.CAP_PROP_FPS, 30)
        self.cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)
        self.cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc('M','J','P','G'))
        
        if not self.cap.isOpened():
            raise RuntimeError(f"Cannot open camera {index}")
        
        actual_w = int(self.cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        actual_h = int(self.cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        actual_fps = int(self.cap.get(cv2.CAP_PROP_FPS))
        print(f"[CAM] {actual_w}x{actual_h} @ {actual_fps}FPS")
    
    def read(self):
        return self.cap.read()
    
    def release(self):
        self.cap.release()

def capture_worker(camera, frame_queue, stop_event, skip):
    """ULTRA-FAST CAPTURE - MINIMAL OVERHEAD"""
    counter = 0
    while not stop_event.is_set():
        ret, frame = camera.read()
        if not ret:
            time.sleep(0.001)
            continue
        
        counter += 1
        if skip > 0 and counter % (skip + 1) != 0:
            continue
        while frame_queue.full():
            frame_queue.get_nowait()
            
        frame_queue.put_nowait(frame)

def main():
    print("\n[INIT] Starting ULTIMATE OPTIMIZED Server...")
    print(f"[CONFIG] Camera: {CAM_WIDTH}x{CAM_HEIGHT} (16:9 - Optimal)")
    print(f"[CONFIG] Inference: {INFERENCE_SIZE}")
    print(f"[CONFIG] Frame skip: {FRAME_SKIP}")
    print(f"[CONFIG] Target: 20+ FPS GUARANTEED\n")

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    print(f"[MODEL] Loading {MODEL_PATH}...")
    try:
        model = YOLO(MODEL_PATH)
        
        if cuda_available:
            model.to('cuda')
            model.overrides = {
                'conf': MODEL_CONF,
                'iou': MODEL_IOU, 
                'imgsz': INFERENCE_SIZE,
                'verbose': False,
                'device': 0,
                'max_det': MAX_PLAYERS,
                'classes': [0],
                'agnostic_nms': True,
                'half': True,
            }
            print("[MODEL] Quick warmup...")
            warmup_frame = np.random.randint(0, 255, (360, 640, 3), dtype=np.uint8)
            _ = model(warmup_frame)
            torch.cuda.synchronize()
            torch.cuda.empty_cache()
            
            mem_mb = torch.cuda.memory_allocated() / 1024**2
            print(f"[MODEL] Ready! VRAM: {mem_mb:.0f}MB | Ultimate Optimizations\n")
        else:
            print("[WARN] Running on CPU\n")
            
    except Exception as e:
        print(f"[ERROR] Model load failed: {e}")
        return
    try:
        camera = FastCamera(CAM_INDEX, CAM_WIDTH, CAM_HEIGHT)
    except Exception as e:
        print(f"[ERROR] Camera failed: {e}")
        return
    frame_queue = queue.Queue(maxsize=1)
    stop_event = threading.Event()
    cap_thread = threading.Thread(
        target=capture_worker,
        args=(camera, frame_queue, stop_event, FRAME_SKIP),
        daemon=True
    )
    cap_thread.start()
    fps_times = deque(maxlen=PERF_WINDOW)
    last_report = time.time()
    frame_count = 0
    
    print("=" * 50)
    print("🚀 ULTIMATE SERVER RUNNING")
    print("=" * 50)
    print("MAXIMUM OPTIMIZATIONS ENABLED")
    print("Press Ctrl+C to stop\n")
    
    try:
        while True:
            loop_start = time.time()
            try:
                frame = frame_queue.get_nowait()
            except queue.Empty:
                time.sleep(0.002)
                continue
            try:
                results = model(frame, augment=False, verbose=False)[0]
            except Exception as e:
                continue
            packet = {"players": []}
            
            if results.keypoints is not None and len(results.keypoints) > 0:
                frame_h, frame_w = frame.shape[:2]
                kpts_xy = results.keypoints.xy.cpu().numpy()
                kpts_conf = results.keypoints.conf.cpu().numpy()
                players_data = []
                for i, kpts in enumerate(kpts_xy):
                    if i >= MAX_PLAYERS:
                        break
                    if len(kpts) > 12:
                        hip_x = (kpts[11][0] + kpts[12][0]) / 2
                    else:
                        hip_x = kpts[5][0]
                    
                    players_data.append((hip_x, kpts, kpts_conf[i] if i < len(kpts_conf) else np.ones(len(kpts))))
                players_data.sort(key=lambda x: x[0])
                for slot_idx, (_, kpts, confs) in enumerate(players_data[:MAX_PLAYERS]):
                    landmarks = []
                    for j, (x, y) in enumerate(kpts):
                        landmarks.append({
                            "x": float(x / frame_w),
                            "y": float(y / frame_h),
                            "z": 0.0,
                            "v": float(confs[j] if j < len(confs) else 0.5)
                        })
                    
                    packet["players"].append({
                        "id": slot_idx,
                        "landmarks": landmarks
                    })
            try:
                sock.sendto(orjson.dumps(packet), (SEND_IP, SEND_PORT))
            except:
                pass
            loop_time = time.time() - loop_start
            fps_times.append(loop_time)
            frame_count += 1
            if time.time() - last_report >= 2.0:
                avg_fps = len(fps_times) / sum(fps_times) if fps_times else 0
                
                status = "✅ TARGET ACHIEVED" if avg_fps >= 20 else "⚡ OPTIMIZING"
                print(f"[PERF] FPS: {avg_fps:.1f} | Players: {len(packet['players'])} | {status}")
                
                if avg_fps < 15:
                    print("   → Try: Close other applications")
                    print("   → Ensure good lighting")
                
                last_report = time.time()
                frame_count = 0
    
    except KeyboardInterrupt:
        print("\n[STOP] Shutting down...")
    except Exception as e:
        print(f"\n[ERROR] {e}")
    finally:
        stop_event.set()
        cap_thread.join(timeout=1.0)
        camera.release()
        sock.close()
        print("[CLEANUP] Complete")

if __name__ == "__main__":
    main()