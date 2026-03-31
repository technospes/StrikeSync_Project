using UnityEngine;
using System.Diagnostics;
using System.Collections.Concurrent;

/// <summary>
/// PoseManager v5.0
///
/// CRITICAL FIX (ISSUE-E / ISSUE-F):
///
/// v4.0 had a sentinel gate:
///     bool isV31Packet = (player.lw_vel < -0.5f);
///     if (isV31Packet) ctrl.ReceiveServerMoveX(player.move_x);
///
/// This gate was the direct cause of srvAge reaching 7–8 seconds at idle —
/// meaning ReceiveServerMoveX was NEVER being called even while the server
/// was running and sending packets at 37 FPS.
///
/// Root cause: Unity's JsonUtility performs silent deserialization.
/// If the PoseDataPacket struct's field name doesn't exactly match the JSON
/// key, or if there's any schema mismatch, the field stays at its C# default
/// value (0f for float). lw_vel defaulting to 0f means (0f < -0.5f) == false,
/// so ReceiveServerMoveX was never called — every packet was silently dropped
/// at this gate, and srvAge just kept climbing.
///
/// The sentinel was introduced as a v3.1 compatibility shim. pose_server.py
/// is now always v4.0+ which always sends a valid move_x. The sentinel is no
/// longer needed and has been REMOVED entirely.
///
/// FIX: ReceiveServerMoveX is now called unconditionally on every packet
/// that has a valid player entry. No gate, no sentinel check.
/// </summary>
public class PoseManager : MonoBehaviour
{
    [Tooltip("UDP Receiver component listening for pose data.")]
    public UdpReceiver receiver;

    [Tooltip("AvatarController for Player 0 (left side of camera).")]
    public AvatarController avatarPlayer1;

    [Tooltip("AvatarController for Player 1 (right side of camera).")]
    public AvatarController avatarPlayer2;

    public GameManager gameManager;

    private float _packetTimer = 0f;
    private int _packetCount = 0;
    private Process _poseServerProcess;

    // ─────────────────────────────────────────────────────────────────────────
    void Update()
    {
        string msg;
        while (receiver.messageQueue.TryDequeue(out msg))
            ProcessMessage(msg);

        _packetTimer += Time.deltaTime;
        if (_packetTimer > 1f)
        {
            if (_packetCount > 0)
                UnityEngine.Debug.Log(
                    $"<color=green>PoseManager: {_packetCount} packets/sec</color>");
            _packetCount = 0;
            _packetTimer = 0f;
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

                // Always forward keypoints — needed for IK and punch detection
                ctrl.ReceiveKeypoints(player.landmarks);

                // ISSUE-E/F FIX: call unconditionally — no sentinel gate.
                // pose_server v4.0+ always sends a valid move_x.
                // The old lw_vel sentinel was causing JsonUtility silent-default
                // failures that blocked every single packet from reaching
                // ReceiveServerMoveX, causing srvAge to climb to 7–8 seconds.
                ctrl.ReceiveServerMoveX(player.move_x);

                if (player.jumped)
                    ctrl.ReceiveJump();
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError(
                $"<color=red>JSON parse error:</color> {e.Message}\nJSON: {json}");
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
            UnityEngine.Debug.Log("Python Pose Server v5.0 started.");
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError("FAILED to start pose_server.py: " + e.Message);
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
            UnityEngine.Debug.Log("Python Pose Server stopped.");
        }
    }

    void OnDestroy() => StopPoseDetection();
}