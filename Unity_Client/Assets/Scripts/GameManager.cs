using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.SceneManagement;

/// <summary>
/// GameManager — production version.
///
/// Changes vs original:
///   • All WS events use WSEventType enum (no magic strings).
///   • Subscribes to UnityWSBridge.OnConnected to push a state snapshot
///     the moment the bridge connects, so React is never out of sync.
///   • Subscribes to UnityWSBridge.OnMessage to handle rematch/main_menu
///     commands from React.
///   • Properly unsubscribes in OnDestroy.
/// </summary>
public class GameManager : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────
    [Header("Spawning")]
    public Transform player1StartPoint;
    public Transform player2StartPoint;
    public BackgroundScroller bgScroller;

    [Header("System References")]
    public PoseManager poseManager;

    [Header("Player 1 UI")]
    public Slider player1HealthBar;
    public Image player1Icon;
    public TextMeshProUGUI player1Name;

    [Header("Player 2 UI")]
    public Slider player2HealthBar;
    public Image player2Icon;
    public TextMeshProUGUI player2Name;

    [Header("Match UI")]
    public TextMeshProUGUI countdownText;
    public TextMeshProUGUI winText;
    public Button rematchButton;

    // ── Private state ─────────────────────────────────────────────────────────
    private AvatarController _p1Controller;
    private AvatarController _p2Controller;
    private string _currentGameState = GameState.Menu;

    // ── Unity lifecycle ───────────────────────────────────────────────────────
    void Start()
    {
        countdownText.gameObject.SetActive(true);
        winText.gameObject.SetActive(false);
        rematchButton.gameObject.SetActive(false);

        // Subscribe to WS events
        if (UnityWSBridge.Instance != null)
        {
            UnityWSBridge.Instance.OnConnected += OnWSConnected;
            UnityWSBridge.Instance.OnMessage += HandleWSMessage;
        }

        SpawnPlayers();
        StartCoroutine(StartMatchCountdown());
    }

    void OnDestroy()
    {
        if (UnityWSBridge.Instance != null)
        {
            UnityWSBridge.Instance.OnConnected -= OnWSConnected;
            UnityWSBridge.Instance.OnMessage -= HandleWSMessage;
        }
    }

    // ── WS handlers ───────────────────────────────────────────────────────────
    private void OnWSConnected()
    {
        // Push current state immediately so React is never stale after a
        // reconnect (e.g. if the bridge was restarted mid-game).
        SendState(_currentGameState);
    }

    private void HandleWSMessage(WSMessage msg)
    {
        switch (msg.EventType)
        {
            case WSEventType.Rematch:
                OnRematch();
                break;
            case WSEventType.MainMenu:
                OnRematch(); // Same logic: stop everything and go to menu
                break;
        }
    }

    // ── Spawning ──────────────────────────────────────────────────────────────
    void SpawnPlayers()
    {
        string p1PrefabName = PlayerPrefs.GetString("Player1_PrefabName", "DefaultPlayer");
        string p2PrefabName = PlayerPrefs.GetString("Player2_PrefabName", "DefaultPlayer");
        string p1IconName = PlayerPrefs.GetString("Player1_IconName");
        string p2IconName = PlayerPrefs.GetString("Player2_IconName");

        GameObject p1Prefab = Resources.Load<GameObject>(p1PrefabName);
        GameObject p2Prefab = Resources.Load<GameObject>(p2PrefabName);
        Sprite p1IconSprite = Resources.Load<Sprite>(p1IconName);
        Sprite p2IconSprite = Resources.Load<Sprite>(p2IconName);

        if (p1Prefab == null || p2Prefab == null)
        {
            Debug.LogError("[GameManager] Could not load player prefabs from Resources.");
            return;
        }

        GameObject player1 = Instantiate(p1Prefab, player1StartPoint.position, player1StartPoint.rotation);
        GameObject player2 = Instantiate(p2Prefab, player2StartPoint.position, player2StartPoint.rotation);
        player1.name = "Player_1";
        player2.name = "Player_2";

        // Link camera + background scroller
        if (bgScroller != null)
        {
            bgScroller.player1 = player1.transform;
            bgScroller.player2 = player2.transform;
        }
        CameraFollow camFollow = Camera.main?.GetComponent<CameraFollow>();
        if (camFollow != null)
        {
            camFollow.player1 = player1.transform;
            camFollow.player2 = player2.transform;
        }

        _p1Controller = player1.GetComponent<AvatarController>();
        _p2Controller = player2.GetComponent<AvatarController>();

        // Link PoseManager to the ACTUAL spawned instances (not inspector refs)
        poseManager.RegisterPlayers(_p1Controller, _p2Controller);

        // Link HealthSystem + UI
        HealthSystem p1Health = player1.GetComponent<HealthSystem>();
        HealthSystem p2Health = player2.GetComponent<HealthSystem>();

        p1Health.healthSlider = player1HealthBar;
        p1Health.healthFillImage = player1HealthBar.transform.Find("Fill Area/Fill").GetComponent<Image>();
        p2Health.healthSlider = player2HealthBar;
        p2Health.healthFillImage = player2HealthBar.transform.Find("Fill Area/Fill").GetComponent<Image>();

        if (p1IconSprite != null) player1Icon.sprite = p1IconSprite;
        if (p2IconSprite != null) player2Icon.sprite = p2IconSprite;
        player1Name.text = p1PrefabName;
        player2Name.text = p2PrefabName;

        // Send player metadata to React (names for HUD labels)
        SendPlayerMeta(p1PrefabName, p2PrefabName);

        // Knockout events
        p1Health.OnKnockout += () => OnGameOver(_p2Controller);
        p2Health.OnKnockout += () => OnGameOver(_p1Controller);
    }

    // ── Match flow ────────────────────────────────────────────────────────────
    IEnumerator StartMatchCountdown()
    {
        SetState(GameState.Countdown);

        countdownText.text = "3"; SendCountdown("3"); yield return new WaitForSeconds(1);
        countdownText.text = "2"; SendCountdown("2"); yield return new WaitForSeconds(1);
        countdownText.text = "1"; SendCountdown("1"); yield return new WaitForSeconds(1);

        countdownText.text = "ANNIHILATE!"; SendCountdown("FIGHT");

        poseManager.StartPoseDetection();
        if (_p1Controller) _p1Controller.EnableFighting();
        if (_p2Controller) _p2Controller.EnableFighting();

        SetState(GameState.Fighting);

        yield return new WaitForSeconds(1);
        countdownText.gameObject.SetActive(false);
    }

    void OnGameOver(AvatarController winner)
    {
        if (_p1Controller) _p1Controller.canFight = false;
        if (_p2Controller) _p2Controller.canFight = false;

        poseManager.StopPoseDetection();

        winText.text = $"{winner.name} WINS! ANNIHILATION!";
        winText.gameObject.SetActive(true);
        rematchButton.gameObject.SetActive(true);

        string winnerKey = winner.playerID == 0 ? "player1" : "player2";
        UnityWSBridge.Instance?.Send(WSEventType.GameOver, winnerKey);
        SetState(GameState.GameOver);
    }

    public void OnRematch()
    {
        poseManager.StopPoseDetection();
        SceneManager.LoadScene("MainMenu_Scene");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private void SetState(string state)
    {
        _currentGameState = state;
        SendState(state);
    }

    private void SendState(string state) =>
        UnityWSBridge.Instance?.Send(WSEventType.StateChange, state);

    private void SendCountdown(string value) =>
        UnityWSBridge.Instance?.Send(WSEventType.Countdown, value);

    private void SendPlayerMeta(string p1Name, string p2Name)
    {
        if (UnityWSBridge.Instance == null) return;

        string json = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            type = WSEventType.StateChange.ToWireString(),
            subtype = "player_meta",
            p1Name,
            p2Name
        });
        // Send as raw string via the bridge — React side reads subtype:"player_meta"
        // to populate the HUD name labels.
        UnityWSBridge.Instance.Send(WSEventType.StateChange, $"player_meta|{p1Name}|{p2Name}");
    }
}