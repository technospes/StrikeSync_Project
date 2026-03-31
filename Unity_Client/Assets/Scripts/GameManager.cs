using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.SceneManagement;

/// <summary>
/// GameManager — production version.
///
/// Changes vs original:
///   • Ripped out the old Unity Canvas countdown.
///   • Added PlayFightIntroSequence() to trigger React's epic VS screen.
///   • Waits 5.5s for the animation to finish before unlocking the players!
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

    [Header("Player 1 UI (Legacy - Can be hidden)")]
    public Slider player1HealthBar;
    public Image player1Icon;
    public TextMeshProUGUI player1Name;

    [Header("Player 2 UI (Legacy - Can be hidden)")]
    public Slider player2HealthBar;
    public Image player2Icon;
    public TextMeshProUGUI player2Name;

    [Header("Match UI (Legacy - Can be hidden)")]
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
        // 1. Hide the old Unity UI (React is handling this now!)
        if (countdownText != null) countdownText.gameObject.SetActive(false);
        if (winText != null) winText.gameObject.SetActive(false);
        if (rematchButton != null) rematchButton.gameObject.SetActive(false);

        // 2. Subscribe to WS events
        if (UnityWSBridge.Instance != null)
        {
            UnityWSBridge.Instance.OnConnected += OnWSConnected;
            UnityWSBridge.Instance.OnMessage += HandleWSMessage;
        }

        // 3. Spawn the chosen 3D fighters
        SpawnPlayers();

        // 4. Trigger the new React Cinematic!
        StartCoroutine(PlayFightIntroSequence());
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
                OnRematch();
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

        poseManager.avatarPlayer1 = _p1Controller;
        poseManager.avatarPlayer2 = _p2Controller;

        HealthSystem p1Health = player1.GetComponent<HealthSystem>();
        HealthSystem p2Health = player2.GetComponent<HealthSystem>();

        p1Health.healthSlider = player1HealthBar;
        p1Health.healthFillImage = player1HealthBar.transform.Find("Fill Area/Fill")?.GetComponent<Image>();
        p2Health.healthSlider = player2HealthBar;
        p2Health.healthFillImage = player2HealthBar.transform.Find("Fill Area/Fill")?.GetComponent<Image>();

        if (player1Icon != null && p1IconSprite != null) player1Icon.sprite = p1IconSprite;
        if (player2Icon != null && p2IconSprite != null) player2Icon.sprite = p2IconSprite;
        if (player1Name != null) player1Name.text = p1PrefabName;
        if (player2Name != null) player2Name.text = p2PrefabName;

        SendPlayerMeta(p1PrefabName, p2PrefabName);

        p1Health.OnKnockout += () => OnGameOver(_p2Controller);
        p2Health.OnKnockout += () => OnGameOver(_p1Controller);
    }

    // ── Match flow (NEW CINEMATIC SEQUENCE) ───────────────────────────────────
    IEnumerator PlayFightIntroSequence()
    {
        // 1. Lock the players so they can't punch yet
        if (_p1Controller) _p1Controller.canFight = false;
        if (_p2Controller) _p2Controller.canFight = false;

        // 2. Tell React to show the epic "ROUND ONE - FIGHT" screen
        SetState("fight_intro");

        // 3. Wait for the React animation to finish (approx 5.5 seconds)
        yield return new WaitForSeconds(5.5f);

        // 4. Start AI tracking and unlock the players!
        poseManager.StartPoseDetection();
        if (_p1Controller) _p1Controller.canFight = true;
        if (_p2Controller) _p2Controller.canFight = true;

        // 5. Tell React to switch to the minimal fighting HUD
        SetState(GameState.Fighting);
    }

    void OnGameOver(AvatarController winner)
    {
        if (_p1Controller) _p1Controller.canFight = false;
        if (_p2Controller) _p2Controller.canFight = false;

        poseManager.StopPoseDetection();

        if (winText != null)
        {
            winText.text = $"{winner.name} WINS! ANNIHILATION!";
            winText.gameObject.SetActive(true);
        }
        if (rematchButton != null) rematchButton.gameObject.SetActive(true);

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

    private void SendPlayerMeta(string p1Name, string p2Name)
    {
        if (UnityWSBridge.Instance == null) return;

        UnityWSBridge.Instance.Send(WSEventType.StateChange, $"player_meta|{p1Name}|{p2Name}");
    }
}