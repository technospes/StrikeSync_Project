using UnityEngine;
using UnityEngine.UI;

public class CharacterIconButton : MonoBehaviour
{
    public CharacterData characterData;
    private CharacterSelectManager manager;

    public void Setup(CharacterData data, CharacterSelectManager manager)
    {
        this.characterData = data;
        this.manager = manager;
        GetComponent<Image>().sprite = data.characterIcon;

        GetComponent<Button>().onClick.AddListener(OnSelect);
    }

    void OnSelect()
    {
        manager.OnCharacterSelect(characterData);
    }
}