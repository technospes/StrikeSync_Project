using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using UnityEngine.SceneManagement; // Needed for rematch

public class GameManager : MonoBehaviour
{
    [Header("Spawning")]
    public Transform player1StartPoint;
    public Transform player2StartPoint;
    public BackgroundScroller bgScroller;
    [Header("System References")]
    public PoseManager poseManager; // Your existing PoseManager

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

    private AvatarController p1Controller;
    private AvatarController p2Controller;

    void Start()
    {
        // Hide UI at start
        countdownText.gameObject.SetActive(true);
        winText.gameObject.SetActive(false);
        rematchButton.gameObject.SetActive(false);

        // Spawn Players and start the match
        SpawnPlayers();
        StartCoroutine(StartMatchCountdown());
    }

    void SpawnPlayers()
    {
        // --- 1. GET CHOICES ---
        string p1PrefabName = PlayerPrefs.GetString("Player1_PrefabName", "DefaultPlayer");
        string p2PrefabName = PlayerPrefs.GetString("Player2_PrefabName", "DefaultPlayer");
        string p1IconName = PlayerPrefs.GetString("Player1_IconName");
        string p2IconName = PlayerPrefs.GetString("Player2_IconName");

        // --- 2. LOAD PREFABS (from Assets/Resources folder) ---
        GameObject p1Prefab = Resources.Load<GameObject>(p1PrefabName);
        GameObject p2Prefab = Resources.Load<GameObject>(p2PrefabName);
        Sprite p1IconSprite = Resources.Load<Sprite>(p1IconName);
        Sprite p2IconSprite = Resources.Load<Sprite>(p2IconName);

        // --- 3. SPAWN PLAYERS ---
        GameObject player1 = Instantiate(p1Prefab, player1StartPoint.position, player1StartPoint.rotation);
        GameObject player2 = Instantiate(p2Prefab, player2StartPoint.position, player2StartPoint.rotation);

        player1.name = "Player_1";
        player2.name = "Player_2";

        // --- 4. LINK ALL SCRIPTS (CRITICAL!) ---

        // --- THIS IS THE FIX ---
        // We moved these lines here, AFTER player1 and player2 exist.
        if (bgScroller != null)
        {
            bgScroller.player1 = player1.transform;
            bgScroller.player2 = player2.transform;
        }

        CameraFollow camFollow = Camera.main.GetComponent<CameraFollow>();
        if (camFollow != null)
        {
            camFollow.player1 = player1.transform;
            camFollow.player2 = player2.transform;
        }
        // --- END FIX ---

        p1Controller = player1.GetComponent<AvatarController>();
        p2Controller = player2.GetComponent<AvatarController>();

        // Link PoseManager
        poseManager.avatarPlayer1 = p1Controller;
        poseManager.avatarPlayer2 = p2Controller;

        // Link HealthSystem
        HealthSystem p1Health = player1.GetComponent<HealthSystem>();
        HealthSystem p2Health = player2.GetComponent<HealthSystem>();

        p1Health.healthSlider = player1HealthBar;
        p1Health.healthFillImage = player1HealthBar.transform.Find("Fill Area/Fill").GetComponent<Image>();
        p2Health.healthSlider = player2HealthBar;
        p2Health.healthFillImage = player2HealthBar.transform.Find("Fill Area/Fill").GetComponent<Image>();

        // Link Health Bar UI
        player1Icon.sprite = p1IconSprite;
        player2Icon.sprite = p2IconSprite;
        player1Name.text = p1PrefabName;
        player2Name.text = p2PrefabName;

        // Subscribe to the "Knockout" event
        p1Health.OnKnockout += () => OnGameOver(p2Controller); // If P1 dies, P2 wins
        p2Health.OnKnockout += () => OnGameOver(p1Controller); // If P2 dies, P1 wins
    }

    IEnumerator StartMatchCountdown()
    {
        // --- This is your "Don't start YOLO yet" logic ---

        countdownText.text = "3";
        yield return new WaitForSeconds(1);

        countdownText.text = "2";
        yield return new WaitForSeconds(1);

        countdownText.text = "1";
        yield return new WaitForSeconds(1);

        countdownText.text = "ANNIHILATE!";

        // --- START THE FIGHT ---
        // 1. Start the Python server
        poseManager.StartPoseDetection();

        // 2. Tell avatars they can now fight
        if (p1Controller) p1Controller.canFight = true;
        if (p2Controller) p2Controller.canFight = true;

        yield return new WaitForSeconds(1);
        countdownText.gameObject.SetActive(false);
    }

    // Called by the HealthSystem event when someone's health hits 0
    void OnGameOver(AvatarController winner)
    {
        // Stop the fight
        if (p1Controller) p1Controller.canFight = false;
        if (p2Controller) p2Controller.canFight = false;

        // Stop the Python server
        poseManager.StopPoseDetection();

        // Show UI
        winText.text = $"{winner.name} WINS! ANNIHILATION!";
        winText.gameObject.SetActive(true);
        rematchButton.gameObject.SetActive(true);
    }

    // --- HOOK THIS TO YOUR REMATCH BUTTON'S OnClick() ---
    public void OnRematch()
    {
        // Stop the server (just in case)
        poseManager.StopPoseDetection();
        // Go back to the menu
        SceneManager.LoadScene("MainMenu_Scene");
    }
}