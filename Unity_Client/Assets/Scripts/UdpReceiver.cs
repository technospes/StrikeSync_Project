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
            client = new UdpClient(new IPEndPoint(IPAddress.Any, listenPort));
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
        if (!isListening) return;
        isListening = false;

        // Closing the client forces the blocking client.Receive() to throw a SocketException,
        // which instantly and safely breaks us out of the while loop without using Thread.Abort()
        if (client != null)
        {
            client.Close();
            client = null;
        }

        // Wait a maximum of 1 second for the thread to cleanly exit
        if (listenThread != null && listenThread.IsAlive)
        {
            listenThread.Join(1000);
            listenThread = null;
        }

        UnityEngine.Debug.Log("UDP Receiver shut down safely.");
    }

    private void ListenLoop()
    {
        IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, listenPort);

        while (isListening)
        {
            try
            {
                // Wait for a message (Blocks until data arrives or socket is closed)
                byte[] data = client.Receive(ref remoteEP);
                string json = Encoding.UTF8.GetString(data);

                // Keep queue small but NEVER drop newest packet
                while (messageQueue.Count > 5)
                {
                    messageQueue.TryDequeue(out _); // drop OLD data
                }

                messageQueue.Enqueue(json);
            }
            catch (SocketException)
            {
                // Expected when client.Close() is called. Safely breaks the loop.
                break;
            }
            catch (ObjectDisposedException)
            {
                // Expected if the client is disposed while blocking. Safely breaks the loop.
                break;
            }
            catch (Exception e)
            {
                // General errors
                if (!isListening) break;
                UnityEngine.Debug.LogError($"UDP ListenLoop error: {e.Message}");
            }
        }

        UnityEngine.Debug.Log("UDP Listen thread cleanly exited.");
    }

    // This runs when the object is destroyed (like stopping "Play" mode or closing the .exe)
    void OnDestroy()
    {
        StopListening();
    }
}