using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using System.Collections.Concurrent;

public class UdpReceiver : MonoBehaviour
{
    [Tooltip("The port to listen on. Must match the Python server's SEND_PORT.")]
    public int listenPort = 9001;

    private UdpClient client;
    private Thread listenThread;

    // A thread-safe queue to store messages
    [HideInInspector]
    public ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();

    // A flag to control the thread
    private volatile bool isListening = false;

    // We no longer start automatically
    void Start()
    {
        // Does nothing. Waits for GameManager to call StartListening.
    }

    public void StartListening()
    {
        if (isListening) return; // Already running

        try
        {
            client = new UdpClient(listenPort);
            isListening = true; // Set the flag

            listenThread = new Thread(new ThreadStart(ListenLoop));
            listenThread.IsBackground = true;
            listenThread.Start();

            UnityEngine.Debug.Log($"<color=green>UDP Receiver started on port {listenPort}.</color>");
        }
        catch (Exception e)
        {
            UnityEngine.Debug.LogError($"<color=red>Failed to start UDP Receiver: {e.Message}</color>");
        }
    }

    public void StopListening()
    {
        isListening = false; // Signal the thread to stop

        // Abort the thread
        if (listenThread != null && listenThread.IsAlive)
        {
            listenThread.Abort();
            listenThread = null;
        }

        // Close the client
        if (client != null)
        {
            client.Close();
            client = null;
        }

        UnityEngine.Debug.Log("UDP Receiver shut down.");
    }

    private void ListenLoop()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, listenPort);
        try
        {
            // Use the volatile bool to control the loop
            while (isListening)
            {
                // Wait for a message
                byte[] data = client.Receive(ref remoteEP);
                string json = Encoding.UTF8.GetString(data);

                // Add the message to our thread-safe queue
                messageQueue.Enqueue(json);
            }
        }
        catch (ThreadAbortException)
        {
            // This is expected when we call Abort()
            UnityEngine.Debug.Log("UDP Listen thread aborted.");
        }
        catch (SocketException)
        {
            // This is expected when the client is closed
            UnityEngine.Debug.Log("UDP socket closed.");
        }
        catch (Exception e)
        {
            if (isListening) // Only log if it wasn't a planned stop
            {
                UnityEngine.Debug.LogError($"UDP ListenLoop error: {e.Message}");
            }
        }
    }
    // This runs when the object is destroyed (like stopping "Play" mode)
    void OnDestroy()
    {
        StopListening();
    }
}