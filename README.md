# StrikeSync: AI-Powered Motion Combat Interface

Turn your webcam into a real-time motion capture controller for fighting games.

StrikeSync is a low-latency, markerless human–computer interface (HCI) that bridges computer vision and game development. By leveraging the YOLO11 pose estimation model to track full-body movement in real time and streaming data to Unity via UDP, it enables a “play-as-you-fight” experience without the need for VR headsets or mocap suits.

---

## Table of Contents

- [Demo](#demo)
- [Key Features](#key-features)
- [Tech Stack](#tech-stack)
- [System Architecture](#system-architecture)
- [Installation & Setup](#installation--setup)
  - [Python Server](#python-server-setup)
  - [Unity Client](#unity-client-setup)
- [How to Run](#how-to-run)
- [Usage Guide](#usage-guide)
  - [Controls (Body Gestures)](#controls-body-gestures)
  - [Calibration](#calibration)
- [Project Structure](#project-structure)
- [Configuration Details](#configuration-details)
- [Performance & Optimization](#performance--optimization)
- [Known Limitations](#known-limitations)
- [Roadmap](#roadmap)
- [Contribution Guidelines](#contribution-guidelines)
- [License](#license)
- [Author](#author)

---

## Demo

> Add GIFs / screenshots here (for example: `assets/strikesync_punch.gif`, `assets/strikesync_dodge.gif`).

Examples:
- Real-time combat where physical punches and dodges translate directly into in‑game actions.
- Two-player local battles driven entirely by body movement.

---

## Key Features

- **Ultra-Low Latency Architecture**  
  Optimized Python server using `orjson` serialization and UDP broadcasting to keep pose packets lightweight and highly responsive.

- **Advanced Pose Estimation**  
  Powered by YOLO11 pose models, providing high-accuracy skeletal tracking even during rapid, high-intensity combat motions.

- **Physics-Based Combat**  
  - **Velocity Detection**: Punches are triggered by real hand velocity, not just static keyframes.  
  - **Lean-to-Move**: Move your character laterally by physically leaning left or right.  
  - **Depth Navigation**: Step forward/backward in the arena by adjusting stance width.

- **Real-Time Inverse Kinematics**  
  IK retargeting for head, hands, and elbows so the 3D avatar mirrors your stance and strikes.

- **Multiplayer Ready**  
  Native support for up to two players for local 1v1 gameplay on the same machine.

- **Performance Optimized**  
  CUDA acceleration, configurable frame skipping, and float32 precision tuning to maintain 30+ FPS on consumer GPUs.

---

## Tech Stack

**Core AI & Backend**

- Model: YOLO11 (Ultralytics)  
- Framework: PyTorch (CUDA-optimized)  
- Vision: OpenCV  
- Networking: Python sockets (UDP), `orjson`  

**Game Client**

- Engine: Unity 2022.3 LTS or newer  
- Language: C#  
- Systems: Animator IK, Coroutines, custom UDP listener

---

## System Architecture

StrikeSync follows a decoupled server–client architecture to maximize performance and maintainability.

### Pose Server (Python)

- Captures raw video from the webcam (default 640×360 @ 30 FPS).  
- Runs inference using `yolo11n-pose.pt` (or `yolo11s-pose.pt` for higher accuracy).  
- Extracts 17 keypoints (nose, shoulders, elbows, wrists, hips, etc.).  
- Normalizes coordinates and confidence scores, then serializes them with `orjson`.  
- Broadcasts pose packets via UDP to `localhost:9001`.

### Game Client (Unity)

- **UDP Receiver**: Listens on port `9001` on a background thread to avoid blocking the main game loop.  
- **Pose Manager**: Deserializes incoming data and applies interpolation/smoothing.  
- **Avatar Controller**:  
  - Maps 2D landmarks to 3D world space.  
  - Computes velocity for hit detection and movement.  
  - Drives Animator and IK rigs for responsive combat animations.

---

## Installation & Setup

### Prerequisites

- Python 3.8+  
- Unity 2022.3 LTS or newer  
- Webcam  
- NVIDIA GPU (recommended for CUDA acceleration)

### Python Server Setup

Clone the repository

git clone https://github.com/technospes/strikesync-project.git
cd strikesync-project/Python_Server
Install dependencies

pip install torch ultralytics opencv-python orjson numpy

text

> For GPU acceleration, ensure you install a PyTorch build with CUDA support matching your driver and CUDA toolkit version.

### Unity Client Setup

1. Open Unity Hub.  
2. Add the project from the `Unity_Client/` directory.  
3. Let Unity import required packages (URP, TextMeshPro, etc.).  
4. Open `Scenes/Game_Scene.unity`.

---

## How to Run

1. **Start the AI Server**

   From the `Python_Server` directory:

python pose_server.py

text

The console should display a banner such as `YOLO11 Optimized Pose Server` and confirm that the camera stream is active.

2. **Start the Game**

- In the Unity Editor, open `Game_Scene.unity`.  
- Press the Play (▶) button.  
- Stand 2–3 meters from the camera so your full upper body is visible.  
- The avatar should align with your pose and respond to movement.

---

## Usage Guide

### Controls (Body Gestures)

| Action              | Gesture / Movement                                                |
|---------------------|-------------------------------------------------------------------|
| Punch (Left/Right)  | Throw a fast punch; exceeding a velocity threshold triggers hits |
| Move Left / Right   | Lean your upper body left or right                               |
| Move Forward / Back | Widen stance (forward) or narrow stance (backward)               |
| Guard               | Raise both fists above hip level                                 |

### Calibration

- **Lighting**: Use even front lighting; avoid strong backlighting which can reduce detection quality.  
- **Background & Clothing**: Wear clothing that contrasts with the background to improve YOLO keypoint stability.

---

## Project Structure

StrikeSync_Project/
├── Python_Server/
│ ├── pose_server.py # Main entry point for AI tracking
│ ├── yolo11n-pose.pt # Pre-trained YOLO model (Nano)
│ ├── yolo11s-pose.pt # Pre-trained YOLO model (Small)
│ └── script.cpp # (Optional) C++ optimizations
├── Unity_Client/
│ ├── Assets/
│ │ ├── Scenes/ # MainMenu and Game_Scene
│ │ ├── Scripts/
│ │ │ ├── AvatarController.cs # IK and movement logic
│ │ │ ├── UdpReceiver.cs # Networking layer
│ │ │ ├── GameManager.cs # Match state management
│ │ │ └── HealthSystem.cs # Combat and health handling
│ │ ├── Prefabs/ # Character models and UI
│ │ └── Settings/ # Render pipeline and graphics settings
│ └── Packages/ # Unity package dependencies
└── README.md

text

---

## Configuration Details

### Server Configuration (`pose_server.py`)

Tune these variables at the top of the script to match your hardware and performance targets:

- `CAM_INDEX` – Camera device index (default `0`).  
- `INFERENCE_SIZE` – Input resolution for the model (default `256` for speed).  
- `MODEL_CONF` – Confidence threshold (default `0.4`).  
- `MAX_PLAYERS` – Maximum number of tracked players (default `2`).  

### Client Configuration (Unity Inspector)

Select the player avatar in the scene to adjust the `AvatarController` parameters:

- **Sensitivity**  
  - Punch Velocity Threshold – Minimum hand speed required to trigger a hit (default `1.2`).  
  - Lean Threshold – Sensitivity for lateral movement (default `0.08`).  

- **Smoothing**  
  - Pose Smoothing Factor – Higher values yield smoother motion with slightly more latency (default `0.6`).  

- **Networking**  
  - Listen Port – Must match the Python server UDP port (default `9001`).

---

## Performance & Optimization

- **CUDA Acceleration**  
  Automatically enables `torch.backends.cudnn.benchmark` on NVIDIA GPUs to maximize throughput for stable input sizes.

- **Frame Skipping**  
  Configurable `FRAME_SKIP` value decouples camera frame rate from inference, allowing you to trade accuracy granularity for FPS.

- **Memory Management**  
  Strategic `torch.cuda.empty_cache()` calls during warmup reduce the chance of VRAM fragmentation in long sessions.

- **Fast Serialization**  
  Uses `orjson` instead of the standard `json` module to significantly reduce serialization overhead for UDP packets.

---

## Known Limitations

- **Single-Camera Depth Approximation**  
  Z-axis movement is inferred from shoulder width and stance changes, not true 3D depth sensing.  

- **Occlusion Sensitivity**  
  Crossing arms or blocking limbs may momentarily degrade IK quality or hit detection.  

- **Local Networking Only (Current)**  
  UDP communication is configured for localhost. Online play requires additional networking work (port forwarding, relay server, or VPN).

---

## Roadmap

- [ ] Online multiplayer using WebRTC or a relay server for remote 1v1 matches.  
- [ ] Gesture macros for advanced moves (for example, “Hadouken” or combo chains).  
- [ ] Dynamic 3D arenas with background segmentation and virtual staging.  
- [ ] High-performance C++/TensorRT server variant for ultra-low latency.

---

## Contribution Guidelines

Contributions are welcome.

1. Fork the repository.  
2. Create a feature branch:  

git checkout -b feature/NewMove

text

3. Commit your changes.  
4. Push the branch:  

git push origin feature/NewMove

text

5. Open a Pull Request describing your change and test coverage.

---

## License

Distributed under the MIT License. See `LICENSE` for details.

---

## Author

**Technospes**

- GitHub: [https://github.com/technospes](https://github.com/technospes)  
- LinkedIn (Ayush Shukla): [https://www.linkedin.com/in/ayushshukla-ar/](https://www.linkedin.com/in/ayushshukla-ar/)
