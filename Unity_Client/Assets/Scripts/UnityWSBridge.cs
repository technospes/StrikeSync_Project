using System;
using System.Threading.Tasks;
using UnityEngine;
using NativeWebSocket;

/// <summary>
/// Singleton WebSocket bridge between Unity and the React UI via Node.js relay.
///
/// PROTOCOL: All messages are FLAT JSON — no nested "value" objects.
///
/// Updated in this version:
///   • Send(WSEventType.StateChange, ...) uses "state" field (not "value")
///   • SendCountdown() uses "countdown" field (not "value")
///   • SendGameOver() uses "winner" field (not "value")
///   • SendHealthUpdate() unchanged (was already correct flat shape)
///   • Legacy Send(type, string value) is kept so existing call sites compile,
///     but it maps the string to the correct semantic field automatically.
/// </summary>
public class UnityWSBridge : MonoBehaviour
{
    // ─── Singleton ────────────────────────────────────────────────────────────
    public static UnityWSBridge Instance { get; private set; }

    // ─── Inspector ────────────────────────────────────────────────────────────
    [Header("Connection")]
    public string serverURL = "ws://localhost:8080/unity";
    [Tooltip("Seconds between reconnect attempts.")]
    public float reconnectDelay = 2f;
    [Tooltip("Seconds between heartbeat pings.")]
    public float heartbeatInterval = 5f;

    [Header("Debug")]
    public bool logMessages = false;

    // ─── Events ───────────────────────────────────────────────────────────────
    public event Action<WSMessage> OnMessage;
    public event Action OnConnected;
    public event Action OnDisconnected;

    // ─── State ────────────────────────────────────────────────────────────────
    private WebSocket _ws;
    private bool _isReconnecting;
    private float _heartbeatTimer;
    private bool _appQuitting;

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────
    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    async void Start() => await Connect();

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        _ws?.DispatchMessageQueue();
#endif
        if (_ws != null && _ws.State == WebSocketState.Open)
        {
            _heartbeatTimer += Time.deltaTime;
            if (_heartbeatTimer >= heartbeatInterval)
            {
                _heartbeatTimer = 0f;
                SendPing();
            }
        }
    }

    void OnApplicationQuit() { _appQuitting = true; _ = CloseGracefully(); }
    void OnDestroy() { if (!_appQuitting) _ = CloseGracefully(); }

    // ─── Connection ───────────────────────────────────────────────────────────
    private async Task Connect()
    {
        if (_appQuitting) return;
        _ws = new WebSocket(serverURL);

        _ws.OnOpen += () =>
        {
            _isReconnecting = false;
            _heartbeatTimer = 0f;
            Debug.Log("<color=cyan>[WS] Connected to bridge.</color>");
            OnConnected?.Invoke();
        };

        _ws.OnError += (err) => Debug.LogWarning($"[WS] Error: {err}");

        _ws.OnClose += (code) =>
        {
            Debug.Log($"[WS] Closed (code {code}). Reconnecting in {reconnectDelay}s.");
            OnDisconnected?.Invoke();
            if (!_appQuitting) ScheduleReconnect();
        };

        _ws.OnMessage += (bytes) =>
        {
            string raw = System.Text.Encoding.UTF8.GetString(bytes);
            if (logMessages) Debug.Log($"[WS] ← {raw}");
            ParseAndDispatch(raw);
        };

        try { await _ws.Connect(); }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WS] Connect failed: {ex.Message}. Retrying in {reconnectDelay}s.");
            if (!_appQuitting) ScheduleReconnect();
        }
    }

    private async void ScheduleReconnect()
    {
        if (_isReconnecting || _appQuitting) return;
        _isReconnecting = true;
        await Task.Delay(TimeSpan.FromSeconds(reconnectDelay));
        if (!_appQuitting)
        {
            Debug.Log("[WS] Attempting reconnect...");
            await Connect();
        }
    }

    private async Task CloseGracefully()
    {
        _isReconnecting = false;
        if (_ws != null && _ws.State == WebSocketState.Open)
            await _ws.Close();
    }

    // ─── Sending ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Generic send — maps the WSEventType to the correct semantic JSON field.
    ///
    ///   StateChange  → { "type": "state_change",  "state":     value }
    ///   Countdown    → { "type": "countdown",      "countdown": value }
    ///   GameOver     → { "type": "game_over",      "winner":    value }
    ///   HitEvent     → { "type": "hit_event",      "state":     value }
    ///   Everything else → { "type": "...",          "state":     value }
    ///                      (using "state" as the safe default)
    /// </summary>
    public void Send(WSEventType type, string value)
    {
        if (!IsConnected)
        {
            if (logMessages) Debug.LogWarning($"[WS] Tried to send '{type}' but socket is not open.");
            return;
        }

        object payload;

        switch (type)
        {
            case WSEventType.StateChange:
                payload = new { type = type.ToWireString(), state = value };
                break;

            case WSEventType.Countdown:
                payload = new { type = type.ToWireString(), countdown = value };
                break;

            case WSEventType.GameOver:
                payload = new { type = type.ToWireString(), winner = value };
                break;

            default:
                // For HitEvent, Ping, and other string-value events,
                // keep "value" so any existing React handlers still work.
                payload = new { type = type.ToWireString(), value };
                break;
        }

        string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
        if (logMessages) Debug.Log($"[WS] → {json}");
        _ws.SendText(json);
    }

    /// <summary>
    /// Sends a typed state transition — preferred over Send(StateChange, string).
    /// Produces: { "type": "state_change", "state": "<stateName>" }
    /// </summary>
    public void SendStateSnapshot(string stateName) =>
        Send(WSEventType.StateChange, stateName);

    /// <summary>
    /// Sends a typed countdown tick.
    /// Produces: { "type": "countdown", "countdown": "<value>" }
    /// </summary>
    public void SendCountdown(string value) =>
        Send(WSEventType.Countdown, value);

    /// <summary>
    /// Sends health update — always been flat, unchanged.
    /// Produces: { "type": "health_update", "player": N, "hp": X, "pct": Y }
    /// </summary>
    public void SendHealthUpdate(int playerID, float currentHP, float maxHP)
    {
        if (!IsConnected) return;

        float pct = maxHP > 0 ? currentHP / maxHP : 0f;
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            type = WSEventType.HealthUpdate.ToWireString(),
            player = playerID,
            hp = Mathf.Round(currentHP * 10f) / 10f,
            pct = (float)Math.Round(pct, 3),
        });

        if (logMessages) Debug.Log($"[WS] → {json}");
        _ws.SendText(json);
    }

    private void SendPing()
    {
        if (!IsConnected) return;
        string json = Newtonsoft.Json.JsonConvert.SerializeObject(
            new { type = WSEventType.Ping.ToWireString() }
        );
        _ws.SendText(json);
    }

    // ─── Receiving ────────────────────────────────────────────────────────────
    private void ParseAndDispatch(string raw)
    {
        try
        {
            WSMessage msg = Newtonsoft.Json.JsonConvert.DeserializeObject<WSMessage>(raw);
            if (msg == null || string.IsNullOrEmpty(msg.type))
            {
                Debug.LogWarning($"[WS] No 'type' field: {raw}");
                return;
            }
            OnMessage?.Invoke(msg);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WS] Parse failed: {ex.Message}\nRaw: {raw}");
        }
    }

    // ─── Status ───────────────────────────────────────────────────────────────
    public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;
}