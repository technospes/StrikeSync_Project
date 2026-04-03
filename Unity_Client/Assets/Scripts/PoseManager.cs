using UnityEngine;
using System.Diagnostics;

/// <summary>
/// PoseManager v14.0
///
/// KEY FIX — UDP socket opens at Awake(), not StartPoseDetection().
///
/// Previously, the socket opened during the countdown coroutine.
/// Python starts sending immediately after model warmup (~3s).
/// With the socket closed, the OS drops every packet silently.
/// Opening at Awake() means port 9001 is ready from frame 1.
///
/// ALSO: forwards calib_score and calib_ready to AvatarController
/// so it can update calibration UI without any extra coupling.
/// </summary>
[DefaultExecutionOrder(-10)]
public class PoseManager : MonoBehaviour
{
    [Tooltip("UDP Receiver listening on port 9001.")]
    public UdpReceiver receiver;

    [Tooltip("AvatarController for Player 0 (left side of camera).")]
    public AvatarController avatarPlayer1;

    [Tooltip("AvatarController for Player 1 (right side of camera).")]
    public AvatarController avatarPlayer2;

    public GameManager gameManager;

    [Header("=== PYTHON LAUNCH MODE ===")]
    [Tooltip("TRUE  = Unity will NOT spawn pose_server.py — you start it manually before pressing Play.\n" +
             "FALSE = Unity spawns pose_server.py automatically after the countdown.\n\n" +
             "Set TRUE during development so you can keep the Python debug window open.\n" +
             "Set FALSE for final build.")]
    public bool externalPythonMode = true;   // ← DEFAULT TRUE: run Python yourself first

    private float _packetTimer = 0f;
    private int _packetCount = 0;
    private float _noPacketWarnTimer = 0f;
    private Process _poseServerProcess;

    // ─────────────────────────────────────────────────────────────────────────
    // Open UDP socket at Awake — before any countdown coroutine, before Start.
    // Port 9001 will be ready from the very first frame of the scene.
    // Python packets sent during model warmup won't be dropped by the OS.
    // ─────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (receiver != null)
        {
            receiver.StartListening();
            UnityEngine.Debug.Log(
                "[PoseManager] UDP socket opened at Awake — port ready before Python starts.");
        }
    }

    void Update()
    {
        string msg;
        int processed = 0;
        while (receiver != null && receiver.messageQueue.TryDequeue(out msg))
        {
            ProcessMessage(msg);
            processed++;
        }

        _packetTimer += Time.deltaTime;
        if (_packetTimer >= 1f)
        {
            if (_packetCount > 0)
                UnityEngine.Debug.Log(
                    $"<color=green>PoseManager: {_packetCount} pkt/s</color>");
            _packetCount = 0;
            _packetTimer -= 1f;
        }

        if (processed == 0)
        {
            _noPacketWarnTimer += Time.deltaTime;
            if (_noPacketWarnTimer >= 5f)
            {
                UnityEngine.Debug.LogWarning(
                    "[PoseManager] No UDP packets for 5 s — is pose_server.py running?");
                _noPacketWarnTimer = 0f;
            }
        }
        else
        {
            _noPacketWarnTimer = 0f;
        }
    }

    private void ProcessMessage(string json)
    {
        //UnityEngine.Debug.Log("🔥 Processing UDP message");
        try
        {
            PoseDataPacket packet = JsonUtility.FromJson<PoseDataPacket>(json);
            if (packet?.players == null) return;
            _packetCount++;

            foreach (var player in packet.players)
            {
                AvatarController ctrl = GetController(player.id);
                if (ctrl == null || player.landmarks == null) continue;

                // SetPlayerID records the ID and sets mirrorInput.
                // It does NOT set canFight — GameManager owns that gate
                // via _p1Controller.canFight = true after the countdown.
                if (ctrl.playerID != player.id) ctrl.SetPlayerID(player.id);

                ctrl.ReceiveKeypoints(player.landmarks);
                ctrl.ReceiveServerMoveX(player.move_x);
                ctrl.ReceiveServerMoveZ(player.move_z);

                if (player.punch_left) ctrl.ReceivePunch("Left");
                if (player.punch_right) ctrl.ReceivePunch("Right");
                if (player.jumped) ctrl.ReceiveJump();

                ctrl.ReceiveCalibration(player.calib_score, player.calib_ready);
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError(
                $"<color=red>[PoseManager] JSON error:</color> {e.Message}\n" +
                $"JSON snippet: {(json.Length > 120 ? json.Substring(0, 120) + "..." : json)}");
        }
    }

    private AvatarController GetController(int id)
    {
        if (id == 0) return avatarPlayer1;
        if (id == 1) return avatarPlayer2;
        return null;
    }

    /// <summary>
    /// Called by GameManager immediately after SpawnPlayers().
    /// Overwrites any inspector-assigned references with the ACTUAL
    /// spawned instances — the only objects that have Animator + HealthSystem.
    /// </summary>
    public void RegisterPlayers(AvatarController p1, AvatarController p2)
    {
        avatarPlayer1 = p1;
        avatarPlayer2 = p2;
        UnityEngine.Debug.Log(
            $"<color=cyan>[PoseManager] Linked to spawned players: {p1?.name}, {p2?.name}</color>");
    }

    public void StartPoseDetection()
    {
        // Socket is already open from Awake — StartListening is idempotent (no-op if running)
        if (receiver != null) receiver.StartListening();

        if (externalPythonMode)
        {
            UnityEngine.Debug.Log(
                "<color=yellow>[PoseManager] External Python mode — skipping auto-launch. " +
                "Start pose_server.py manually BEFORE pressing Play in Unity.</color>");
            return;
        }

        try
        {
            string scriptPath = @"E:\StrikeSync_Project\Python_Server\pose_server.py";
            string pythonPath = @"E:\StrikeSync_Project\Python_Server\venv\Scripts\python.exe";
            _poseServerProcess = new Process();
            _poseServerProcess.StartInfo.FileName = pythonPath;
            _poseServerProcess.StartInfo.Arguments = scriptPath;
            _poseServerProcess.StartInfo.UseShellExecute = false;
            _poseServerProcess.StartInfo.CreateNoWindow = true;
            //_poseServerProcess.Start();
            UnityEngine.Debug.Log("[PoseManager] pose_server.py started.");
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError("[PoseManager] FAILED to start: " + e.Message);
        }
    }

    public void StopPoseDetection()
    {
        if (receiver != null) receiver.StopListening();
        if (_poseServerProcess != null && !_poseServerProcess.HasExited)
        {
            _poseServerProcess.Kill();
            _poseServerProcess.Dispose();
            _poseServerProcess = null;
            UnityEngine.Debug.Log("[PoseManager] pose_server.py stopped.");
        }
    }

    void OnDestroy() => StopPoseDetection();
}