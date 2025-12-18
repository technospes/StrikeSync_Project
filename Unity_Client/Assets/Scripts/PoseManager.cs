using UnityEngine;
using System.Diagnostics; // Required for Process
using System.Collections.Generic; // Required for List
using System.Collections.Concurrent; // For the queue

public class PoseManager : MonoBehaviour
{
    [Tooltip("The UDP Receiver component that is listening for data.")]
    public UdpReceiver receiver;
    [Tooltip("The AvatarController for Player 0 (left).")]
    public AvatarController avatarPlayer1;
    [Tooltip("The AvatarController for Player 1 (right).")]
    public AvatarController avatarPlayer2;

    // Reference to the GameManager to show UI
    public GameManager gameManager;

    private float packetTimer = 0f;
    private int packetCount = 0;
    private Process poseServerProcess;

    void Update()
    {
        string msg;
        // Process all messages in the queue this frame
        while (receiver.messageQueue.TryDequeue(out msg))
        {
            ProcessMessage(msg);
        }

        packetTimer += Time.deltaTime;
        if (packetTimer > 1.0f) // Log this info once per second
        {
            if (packetCount > 0)
            {
                // Use UnityEngine.Debug to be specific
                UnityEngine.Debug.Log($"<color=green>PoseManager: Received {packetCount} packets in the last second.</color>");
            }
            packetCount = 0;
            packetTimer = 0f;
        }
    }

    private void ProcessMessage(string json)
    {
        try
        {
            PoseDataPacket packet = JsonUtility.FromJson<PoseDataPacket>(json);
            packetCount++;

            // Inside PoseManager.cs -> ProcessMessage function

            if (packet != null && packet.players != null)
            {
                foreach (var player in packet.players)
                {
                    if (player.id == 0)
                    {
                        if (avatarPlayer1 != null && player.landmarks != null)
                        {
                            // 1. TELL THE AVATAR IT IS PLAYER 1 (ID 0)
                            if (avatarPlayer1.playerID != 0) avatarPlayer1.SetPlayerID(0);

                            avatarPlayer1.ReceiveKeypoints(player.landmarks);
                        }
                    }
                    else if (player.id == 1)
                    {
                        if (avatarPlayer2 != null && player.landmarks != null)
                        {
                            // 2. TELL THE AVATAR IT IS PLAYER 2 (ID 1)
                            if (avatarPlayer2.playerID != 1) avatarPlayer2.SetPlayerID(1);

                            avatarPlayer2.ReceiveKeypoints(player.landmarks);
                        }
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            // Use UnityEngine.Debug to be specific
            UnityEngine.Debug.LogError($"<color=red>Error parsing JSON:</color> {e.Message}\nJSON: {json}");
        }
    }

    // --- These are the functions GameManager calls ---

    public void StartPoseDetection()
    {
        // Start the UDP listener
        if (receiver != null)
            receiver.StartListening(); // This is the new function

        // Start the Python script
        try
        {
            string scriptPath = @"E:\StrikeSync_Project\Python_Server\pose_server.py";
            string pythonPath = @"E:\StrikeSync_Project\Python_Server\venv\Scripts\python.exe";

            poseServerProcess = new Process();
            poseServerProcess.StartInfo.FileName = pythonPath;
            poseServerProcess.StartInfo.Arguments = scriptPath;
            poseServerProcess.StartInfo.UseShellExecute = false;
            poseServerProcess.StartInfo.CreateNoWindow = true;
            poseServerProcess.Start();
            // Use UnityEngine.Debug to be specific
            UnityEngine.Debug.Log("Python Pose Server started!");
        }
        catch (System.Exception e)
        {
            // Use UnityEngine.Debug to be specific
            UnityEngine.Debug.LogError("FAILED to start pose_server.py: " + e.Message);
        }
    }

    public void StopPoseDetection()
    {
        // Stop the UDP listener
        if (receiver != null)
            receiver.StopListening(); // This is the new function

        // Kill the Python script
        if (poseServerProcess != null && !poseServerProcess.HasExited)
        {
            poseServerProcess.Kill();
            poseServerProcess.Dispose();
            poseServerProcess = null;
            // Use UnityEngine.Debug to be specific
            UnityEngine.Debug.Log("Python Pose Server stopped.");
        }
    }
    void OnDestroy()
    {
        StopPoseDetection();
    }
}