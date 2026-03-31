using UnityEngine;
using System.Diagnostics;

/// <summary>
/// PoseManager v7.0
///
/// CRITICAL FIX: Script Execution Order
///
/// The remaining srvAge expiry bug (fresh flipping to false at srvAge=1.19→1.40)
/// was caused by Unity's default script execution order:
///
///   Frame N:
///     1. AvatarController.Update() runs FIRST
///        → adds deltaTime to _srvAge
///        → if _srvAge >= SERVER_EXPIRE_S (was 1.2): clears _hasSrvMoveX, zeros _srvMoveX
///        → UpdateWalk() sees raw=0, fresh=false
///
///     2. PoseManager.Update() runs SECOND (default order)
///        → drains UDP queue
///        → calls ReceiveServerMoveX(move_x) → resets _srvAge=0
///        → but AvatarController already used expired data this frame
///
/// This means any frame where the packet arrived AFTER AvatarController ran
/// would see stale/expired data for that entire frame.  At 35fps with 29ms
/// inter-packet gaps and 16ms Unity frames, this race occurred regularly.
///
/// FIX: [DefaultExecutionOrder(-10)] on PoseManager ensures it runs BEFORE
/// AvatarController (which has default order 0).  Now the sequence is:
///
///   Frame N:
///     1. PoseManager.Update() runs FIRST (order -10)
///        → drains UDP queue, calls ReceiveServerMoveX → _srvAge reset to 0
///
///     2. AvatarController.Update() runs SECOND (order 0)
///        → _srvAge is 0 (or small), _hasSrvMoveX is true
///        → UpdateWalk() sees fresh=true, raw=correct_value  ✅
///
/// Combined with SERVER_EXPIRE_S raised to 2.5s in AvatarController,
/// the srvAge expiry can no longer be accidentally triggered during normal
/// operation regardless of UDP timing variance.
/// </summary>
[DefaultExecutionOrder(-10)]  // ← CRITICAL: must run before AvatarController (order 0)
public class PoseManager : MonoBehaviour
{
    [Tooltip("UDP Receiver listening on port 9001.")]
    public UdpReceiver receiver;

    [Tooltip("AvatarController for Player 0 (left side of camera).")]
    public AvatarController avatarPlayer1;

    [Tooltip("AvatarController for Player 1 (right side of camera).")]
    public AvatarController avatarPlayer2;

    public GameManager gameManager;

    private float _packetTimer = 0f;
    private int _packetCount = 0;
    private float _noPacketWarnTimer = 0f;
    private Process _poseServerProcess;

    // ─────────────────────────────────────────────────────────────────────────
    void Update()
    {
        // Drain ALL queued packets before AvatarController sees this frame.
        // [DefaultExecutionOrder(-10)] guarantees we run first.
        string msg;
        int processed = 0;
        while (receiver != null && receiver.messageQueue.TryDequeue(out msg))
        {
            ProcessMessage(msg);
            processed++;
        }

        // Packet rate counter
        _packetTimer += Time.deltaTime;
        if (_packetTimer >= 1f)
        {
            if (_packetCount > 0)
                UnityEngine.Debug.Log(
                    $"<color=green>PoseManager v7: {_packetCount} pkt/s</color>");
            _packetCount = 0;
            _packetTimer -= 1f;
        }

        // Warn if no packets arriving at all
        if (processed == 0)
        {
            _noPacketWarnTimer += Time.deltaTime;
            if (_noPacketWarnTimer >= 5f)
            {
                UnityEngine.Debug.LogWarning(
                    "[PoseManager] No UDP packets for 5 s — " +
                    "is pose_server.py running? Is port 9001 open?");
                _noPacketWarnTimer = 0f;
            }
        }
        else
        {
            _noPacketWarnTimer = 0f;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    private void ProcessMessage(string json)
    {
        try
        {
            PoseDataPacket packet = JsonUtility.FromJson<PoseDataPacket>(json);
            if (packet?.players == null) return;
            _packetCount++;

            foreach (var player in packet.players)
            {
                AvatarController ctrl = GetController(player.id);
                if (ctrl == null || player.landmarks == null) continue;

                if (ctrl.playerID != player.id) ctrl.SetPlayerID(player.id);

                // Always forward keypoints (IK + punch)
                ctrl.ReceiveKeypoints(player.landmarks);

                // Unconditional — pose_server v5+ always sends valid move_x.
                // No sentinel gate (lw_vel check was causing silent drops).
                ctrl.ReceiveServerMoveX(player.move_x);

                if (player.jumped)
                    ctrl.ReceiveJump();
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError(
                $"<color=red>[PoseManager] JSON error:</color> {e.Message}\n" +
                $"JSON: {(json.Length > 100 ? json.Substring(0, 100) + "..." : json)}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    private AvatarController GetController(int id)
    {
        if (id == 0) return avatarPlayer1;
        if (id == 1) return avatarPlayer2;
        return null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    public void StartPoseDetection()
    {
        if (receiver != null) receiver.StartListening();
        try
        {
            string scriptPath = @"E:\StrikeSync_Project\Python_Server\pose_server.py";
            string pythonPath = @"E:\StrikeSync_Project\Python_Server\venv\Scripts\python.exe";
            _poseServerProcess = new Process();
            _poseServerProcess.StartInfo.FileName = pythonPath;
            _poseServerProcess.StartInfo.Arguments = scriptPath;
            _poseServerProcess.StartInfo.UseShellExecute = false;
            _poseServerProcess.StartInfo.CreateNoWindow = true;
            _poseServerProcess.Start();
            UnityEngine.Debug.Log("[PoseManager] pose_server.py v8 started.");
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