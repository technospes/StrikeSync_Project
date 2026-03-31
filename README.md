# StrikeSync: AI-Powered Motion Combat Interface 🥊

Turn your webcam into a real-time motion capture controller for fighting games.

StrikeSync is a low-latency, markerless human–computer interface (HCI) that bridges computer vision and game development. By leveraging the YOLO11 pose estimation model to track full-body movement in real time and streaming data to Unity via UDP, it enables a “play-as-you-fight” experience without the need for VR headsets or mocap suits. 

**🔥 What's NEW :** StrikeSync now features a fully integrated Web UI and WebSocket Bridge for seamless character and map selection, alongside a rebuilt zero-drift motion tracking engine!

---

## Table of Contents

- [Demo](#demo)
- [Key Features](#key-features)
- [Tech Stack](#tech-stack)
- [System Architecture](#system-architecture)
- [Installation & Setup](#installation--setup)
- [How to Run](#how-to-run)
- [Usage Guide](#usage-guide)
- [Project Structure](#project-structure)
- [Performance & Optimization](#performance--optimization)
- [Roadmap](#roadmap)
- [License & Author](#license--author)

---

## Demo

> 🚀 **v5.0 is LIVE!** Watch the gameplay demo on YouTube [Link coming soon...]

Examples:
- Real-time combat where physical punches and dodges translate directly into in‑game actions.
- Seamless Web UI for on-the-fly character and arena swapping.
- Two-player local battles driven entirely by body movement.

---

## Key Features

- **Ultra-Low Latency Architecture:** Optimized Python server using `orjson` serialization and UDP broadcasting to keep pose packets lightweight and highly responsive.
- **Advanced Pose Estimation (v5.0):** Powered by YOLO11, featuring a new "Zero-Drift" locked calibration system and "Lean-Guard" filtering to perfectly isolate body dodges from rapid punches.
- **Physics-Based Combat:** - **Velocity Detection:** Punches are triggered by true physical hand velocity, mapped accurately regardless of screen mirroring.
  - **Lean-to-Move:** Move laterally by physically leaning left or right, with sub-threshold noise clamping for rock-solid idle states.
- **Web-Driven UI & Bridge:** A dedicated local web dashboard connected via WebSockets allows players to select fighters (Art Clown, LadyHawk, Mutants, etc.) and maps dynamically without touching the Unity editor.
- **Real-Time Inverse Kinematics:** IK retargeting for head, hands, and elbows so the 3D avatar perfectly mirrors your real-world stance.

---

## Tech Stack

**Core AI & Backend**
- Model: YOLO11 (Ultralytics)
- Framework: PyTorch (CUDA-optimized)
- Vision: OpenCV
- Networking: Python sockets (UDP), `orjson`

**Game Client & Bridge**
- Engine: Unity 2022.3 LTS (C#, Animator IK, Coroutines)
- Bridge: Node.js, WebSockets (`ws`)
- UI: HTML5, CSS, Vanilla JS

---

## System Architecture

StrikeSync utilizes a decoupled, three-tier architecture to maximize performance:

1. **Pose Server (Python):** Captures 30+ FPS webcam video, runs YOLO11 inference, applies EMA smoothing, calculates physical displacement/velocity, and broadcasts lightweight JSON packets via UDP to port `9001`.
2. **WebSocket Bridge (Node.js):** Acts as the middleman for the frontend UI, listening for player/map selections and forwarding them to Unity via WebSockets.
3. **Game Client (Unity):** Listens to the UDP stream for movement/combat data and the WS stream for game-state changes. Applies root-motion animations and IK to drive the characters in real-time.

---

## Installation & Setup

### Prerequisites
- Python 3.8+
- Node.js (v16+)
- Unity 2022.3 LTS or newer
- Webcam
- NVIDIA GPU (Highly recommended for CUDA acceleration)

### 1. Python Server Setup
```bash
git clone [https://github.com/technospes/strikesync-project.git](https://github.com/technospes/strikesync-project.git)
cd strikesync-project/Python_Server
pip install torch ultralytics opencv-python orjson numpy
```
> *For GPU acceleration, ensure you install a PyTorch build with CUDA support matching your driver.*

### 2. WebSocket Bridge Setup
```bash
cd ../strikesync-bridge
npm install
```

### 3. Unity Client Setup
1. Open Unity Hub and add the `Unity_Client/` directory.
2. Allow Unity to import required packages.
3. Open `Scenes/Game_Scene.unity`.

---

## How to Run

You need to run the system in three parts for the full experience:

1. **Start the AI Server:**
   ```bash
   cd Python_Server
   python pose_server.py
   ```
2. **Start the WebSocket Bridge & UI:**
   ```bash
   cd strikesync-bridge
   node server.js
   ```
   *(Then open the `strikesync-ui/index.html` file in your browser).*
3. **Start the Game:**
   Hit Play (▶) in the Unity Editor. Use the Web UI to select your character, stand 2–3 meters from the camera, and start fighting!

---

## Usage Guide & Calibration

| Action              | Physical Gesture                                                  |
|---------------------|-------------------------------------------------------------------|
| Punch (Left/Right)  | Throw a fast punch (must exceed the dynamic velocity threshold). |
| Move Left / Right   | Lean your upper body left or right.                               |
| Guard               | Raise both fists above hip level.                                 |

**Calibration Tips:** - The system uses a **Locked Neutral Calibration**. It requires 8 frames of stable warmup when you first start. Stand naturally in the center of your frame when booting the server.
- Ensure your room is well-lit from the front to avoid harsh shadows on your hips/shoulders.

---

## Project Structure

```text
StrikeSync_Project/
├── Python_Server/
│   ├── pose_server.py       # YOLO AI tracking and UDP broadcast
│   └── yolo11*-pose.pt      # Pre-trained models (ignored in git, handled via LFS)
├── strikesync-bridge/
│   ├── server.js            # Node.js WebSocket router
│   └── package.json
├── strikesync-ui/
│   ├── index.html           # Web dashboard for Character/Map selection
│   └── styles.css
└── Unity_Client/
    ├── Assets/
    │   ├── Scenes/          # MainMenu, Fight Arenas
    │   ├── Scripts/         # AvatarController (v5.0), PoseManager, UnityWSBridge
    │   └── Prefabs/         # Character Models & Animations
    └── Packages/
```

---

## Performance & Optimization

- **CUDA Acceleration:** Automatically enables `torch.backends.cudnn.benchmark` to maximize throughput.
- **Adaptive Frame Skipping:** The Python server monitors its own FPS and dynamically adjusts `FRAME_SKIP` and inference size to maintain a smooth 30+ FPS.
- **Sub-Threshold Clamping:** Reduces UDP noise by dropping micro-movements before they ever hit the network layer.

---

## License & Author

Distributed under the MIT License. See `LICENSE` for details.

**Technospes (Ayush Shukla)**
- GitHub: [technospes](https://github.com/technospes)
- LinkedIn: [Ayush Shukla](https://www.linkedin.com/in/ayushshukla-ar/)
```
