using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using System.Collections.Generic;

public class CharacterSelectManager : MonoBehaviour
{
    [Header("Character Data")]
    public List<CharacterData> allCharacters;
    public GameObject characterButtonPrefab;

    [Header("Scene References")]
    public TextMeshProUGUI statusText;
    public Button startGameButton;
    public Transform characterGridContent;

    [Header("P1 Preview")]
    public Transform p1Stage;
    private GameObject p1CurrentModel;

    [Header("P2 Preview")]
    public Transform p2Stage;
    private GameObject p2CurrentModel;

    private enum SelectionState { P1_Choosing, P2_Choosing, Both_Selected }
    private SelectionState currentState = SelectionState.P1_Choosing;

    private CharacterData p1Choice;
    private CharacterData p2Choice;

    void Start()
    {
        startGameButton.gameObject.SetActive(false);
        statusText.text = "Player 1: CHOOSE YOUR FIGHTER";
        GenerateCharacterIcons();
    }

    void GenerateCharacterIcons()
    {
        foreach (CharacterData character in allCharacters)
        {
            GameObject newButton = Instantiate(characterButtonPrefab, characterGridContent);
            newButton.GetComponent<CharacterIconButton>().Setup(character, this);
        }
    }

    public void OnCharacterSelect(CharacterData character)
    {
        if (currentState == SelectionState.P1_Choosing)
        {
            p1Choice = character;

            if (p1CurrentModel != null) Destroy(p1CurrentModel);

            // Spawn the model
            p1CurrentModel = Instantiate(character.characterPrefab, p1Stage);

            // --- FIX STARTS HERE ---
            // 1. Disable the AvatarController so it doesn't move/resize itself
            var controller = p1CurrentModel.GetComponent<AvatarController>();
            if (controller != null) controller.enabled = false;

            // 2. Disable Hitboxes so they don't trigger collisions
            var hitboxes = p1CurrentModel.GetComponentsInChildren<Hitbox>();
            foreach (var hb in hitboxes) hb.enabled = false;
            // --- FIX ENDS HERE ---

            p1CurrentModel.transform.localPosition = character.previewOffset;
            p1CurrentModel.transform.localRotation = Quaternion.Euler(0, 180, 0);

            SetLayerRecursively(p1CurrentModel, LayerMask.NameToLayer("P1_Preview"));

            statusText.text = "Player 2: CHOOSE YOUR FIGHTER";
            currentState = SelectionState.P2_Choosing;
        }
        else if (currentState == SelectionState.P2_Choosing)
        {
            p2Choice = character;

            if (p2CurrentModel != null) Destroy(p2CurrentModel);

            // Spawn the model
            p2CurrentModel = Instantiate(character.characterPrefab, p2Stage);

            // --- FIX STARTS HERE ---
            // 1. Disable the AvatarController
            var controller = p2CurrentModel.GetComponent<AvatarController>();
            if (controller != null) controller.enabled = false;

            // 2. Disable Hitboxes
            var hitboxes = p2CurrentModel.GetComponentsInChildren<Hitbox>();
            foreach (var hb in hitboxes) hb.enabled = false;
            // --- FIX ENDS HERE ---

            p2CurrentModel.transform.localPosition = character.previewOffset;
            p2CurrentModel.transform.localRotation = Quaternion.Euler(0, 180, 0);

            SetLayerRecursively(p2CurrentModel, LayerMask.NameToLayer("P2_Preview"));

            statusText.text = "GET READY!";
            currentState = SelectionState.Both_Selected;
            startGameButton.gameObject.SetActive(true);
        }
    }

    void SetLayerRecursively(GameObject obj, int newLayer)
    {
        obj.layer = newLayer;
        foreach (Transform child in obj.transform)
        {
            SetLayerRecursively(child.gameObject, newLayer);
        }
    }

    public void OnStartGame()
    {
        PlayerPrefs.SetString("Player1_PrefabName", p1Choice.characterPrefab.name);
        PlayerPrefs.SetString("Player2_PrefabName", p2Choice.characterPrefab.name);
        PlayerPrefs.SetString("Player1_IconName", p1Choice.characterIcon.name);
        PlayerPrefs.SetString("Player2_IconName", p2Choice.characterIcon.name);
        PlayerPrefs.Save();

        SceneManager.LoadScene("Game_Scene");
    }
}