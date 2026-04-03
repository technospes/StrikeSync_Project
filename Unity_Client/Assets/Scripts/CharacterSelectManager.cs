// CharacterSelectManager.cs — updated
//
// Flow change:
//   Before: start_game from React → load Game_Scene directly
//   After:  start_game from React → send state_change: map_select (React shows randomizer)
//           map_selected from React → save map, load Game_Scene

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;
using UnityEngine.UI;

public class CharacterSelectManager : MonoBehaviour
{
    [Header("Character Data")]
    public List<CharacterData> allCharacters;

    [Header("P1 Preview Stage")]
    public Transform p1Stage;

    [Header("P2 Preview Stage")]
    public Transform p2Stage;

    [Header("Legacy UI (optional)")]
    public TextMeshProUGUI statusText;
    public Button startGameButton;
    public Transform characterGridContent;
    public GameObject characterButtonPrefab;

    [Header("Transition")]
    [Tooltip("Duration of the scale-out/in when swapping models (seconds).")]
    public float swapFadeTime = 0.18f;

    // ── Private state ──────────────────────────────────────────────────────
    private GameObject _p1Model;
    private GameObject _p2Model;

    private CharacterData _p1Confirmed;
    private CharacterData _p2Confirmed;

    private bool _p1Locked = false;
    private bool _p2Locked = false;

    private Coroutine _p1Swap;
    private Coroutine _p2Swap;

    // ── Unity lifecycle ────────────────────────────────────────────────────
    void Start()
    {
        if (startGameButton != null) startGameButton.gameObject.SetActive(false);

        if (UnityWSBridge.Instance != null)
            UnityWSBridge.Instance.OnMessage += HandleWSMessage;

        StartCoroutine(AnnounceWhenReady());
    }

    void OnDestroy()
    {
        if (UnityWSBridge.Instance != null)
            UnityWSBridge.Instance.OnMessage -= HandleWSMessage;
    }

    // ── Message routing ────────────────────────────────────────────────────
    private void HandleWSMessage(WSMessage msg)
    {
        switch (msg.type)
        {
            case "goto_char_select":
                UnityWSBridge.Instance?.Send(WSEventType.StateChange, GameState.CharSelect);
                break;

            case "select_character":
                HandleLivePreview(msg);
                break;

            case "confirm_character":
                HandleConfirm(msg);
                break;

            case "start_game":
                // Both players confirmed — move to map selection (React handles the slot spin)
                if (_p1Locked && _p2Locked)
                    GoToMapSelect();
                break;

            case "map_selected":
                // React has chosen a map — save it and load the game scene
                HandleMapSelected(msg);
                break;
        }
    }

    // ── Live preview ───────────────────────────────────────────────────────
    private void HandleLivePreview(WSMessage msg)
    {
        CharacterData match = FindCharacter(msg.name);
        if (match == null)
        {
            Debug.LogWarning($"[CharSelect] Preview: '{msg.name}' not found.");
            return;
        }

        int pid = msg.player;

        if (pid == 0 && !_p1Locked)
        {
            if (_p1Swap != null) StopCoroutine(_p1Swap);
            _p1Swap = StartCoroutine(SwapModel(_p1Model, match, p1Stage, false, newModel => _p1Model = newModel));
        }
        else if (pid == 1 && !_p2Locked)
        {
            if (_p2Swap != null) StopCoroutine(_p2Swap);
            _p2Swap = StartCoroutine(SwapModel(_p2Model, match, p2Stage, true, newModel => _p2Model = newModel));
        }
    }

    // ── Confirm ────────────────────────────────────────────────────────────
    private void HandleConfirm(WSMessage msg)
    {
        CharacterData match = FindCharacter(msg.name);
        if (match == null) return;

        if (msg.player == 0 && !_p1Locked)
        {
            _p1Confirmed = match;
            _p1Locked = true;
            UnityWSBridge.Instance?.Send(WSEventType.HitEvent, "p1_confirmed:" + match.characterName);
        }
        else if (msg.player == 1 && !_p2Locked)
        {
            _p2Confirmed = match;
            _p2Locked = true;
            UnityWSBridge.Instance?.Send(WSEventType.HitEvent, "p2_confirmed:" + match.characterName);
        }
    }

    // ── Map select transition ──────────────────────────────────────────────
    //
    // We tell React to switch to the map_select screen. React runs the casino
    // slot spin entirely client-side, then sends map_selected back to Unity.
    private void GoToMapSelect()
    {
        if (_p1Confirmed == null || _p2Confirmed == null) return;

        // Save character choices now — they persist to Game_Scene via PlayerPrefs
        PlayerPrefs.SetString("Player1_PrefabName", _p1Confirmed.characterPrefab.name);
        PlayerPrefs.SetString("Player2_PrefabName", _p2Confirmed.characterPrefab.name);
        PlayerPrefs.SetString("Player1_IconName", _p1Confirmed.characterIcon.name);
        PlayerPrefs.SetString("Player2_IconName", _p2Confirmed.characterIcon.name);
        PlayerPrefs.Save();

        // Tell React to show the map randomizer
        UnityWSBridge.Instance?.Send(WSEventType.StateChange, GameState.MapSelect);
    }

    // ── Map selected (from React) ──────────────────────────────────────────
    //
    // React sends: { type: "map_selected", map: <id>, mapName: "<name>" }
    // Save the choice and load the game scene.
    private void HandleMapSelected(WSMessage msg)
    {
        // Persist map choice so GameManager can read it
        PlayerPrefs.SetInt("SelectedMapIndex", msg.map);
        PlayerPrefs.SetString("SelectedMapName", msg.mapName ?? "");
        PlayerPrefs.Save();

        Debug.Log($"[CharSelect] Map selected: {msg.mapName} (id {msg.map}) — loading Game_Scene.");
        SceneManager.LoadScene("Game_Scene");
    }

    // ── Model swap coroutine ───────────────────────────────────────────────
    private IEnumerator SwapModel(
        GameObject oldModel,
        CharacterData data,
        Transform stage,
        bool flipX,
        System.Action<GameObject> assignSlot)
    {
        // 1. Scale out existing
        if (oldModel != null)
        {
            assignSlot?.Invoke(null);

            float t = 0f;
            Vector3 startScale = oldModel.transform.localScale;
            while (t < swapFadeTime)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / swapFadeTime);
                oldModel.transform.localScale = Vector3.LerpUnclamped(startScale, Vector3.zero, p * p);
                yield return null;
            }
            Destroy(oldModel);
        }

        // 2. Instantiate new model
        GameObject model = Instantiate(data.characterPrefab, stage);
        assignSlot?.Invoke(model);

        model.transform.localPosition = data.previewOffset;
        model.transform.localRotation = Quaternion.Euler(0, flipX ? 0f : 180f, 0);

        var controller = model.GetComponent<AvatarController>();
        if (controller != null) controller.enabled = false;

        foreach (var hb in model.GetComponentsInChildren<Hitbox>())
            hb.enabled = false;

        SetLayerRecursively(model, LayerMask.NameToLayer(flipX ? "P2_Preview" : "P1_Preview"));

        // 3. Spring scale-in
        Vector3 targetScale = model.transform.localScale;
        model.transform.localScale = Vector3.zero;

        float dur = swapFadeTime * 1.6f;
        float t2 = 0f;
        while (t2 < dur)
        {
            t2 += Time.deltaTime;
            float p = Mathf.Clamp01(t2 / dur);
            float sprung = 1f - Mathf.Pow(1f - p, 3f);
            float scl = p < 0.8f
                ? Mathf.Lerp(0f, 1.08f, sprung / 0.95f)
                : Mathf.Lerp(1.08f, 1.0f, (p - 0.8f) / 0.2f);

            model.transform.localScale = targetScale * scl;
            yield return null;
        }
        model.transform.localScale = targetScale;

        // 4. Trigger idle animation
        Animator anim = model.GetComponent<Animator>();
        //if (anim != null) anim.SetTrigger("Idle");
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private CharacterData FindCharacter(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return allCharacters.Find(c =>
            string.Equals(c.characterName, name, System.StringComparison.OrdinalIgnoreCase));
    }

    private void SetLayerRecursively(GameObject obj, int layer)
    {
        obj.layer = layer;
        foreach (Transform child in obj.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private IEnumerator AnnounceWhenReady()
    {
        if (UnityWSBridge.Instance == null) yield break;
        while (!UnityWSBridge.Instance.IsConnected)
            yield return new WaitForSeconds(0.4f);
        UnityWSBridge.Instance.Send(WSEventType.StateChange, GameState.CharSelect);
    }

    // ── Legacy UI support ──────────────────────────────────────────────────
    public void OnCharacterSelect(CharacterData character)
    {
        if (!_p1Locked)
        {
            if (_p1Swap != null) StopCoroutine(_p1Swap);
            _p1Swap = StartCoroutine(SwapModel(_p1Model, character, p1Stage, false, newModel => _p1Model = newModel));
        }
    }
}