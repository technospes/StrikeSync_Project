using UnityEngine;

// This creates a new "Create" menu item in Unity
[CreateAssetMenu(fileName = "New Character", menuName = "StrikeSync/Character Data")]
public class CharacterData : ScriptableObject
{
    public string characterName;
    public Sprite characterIcon; // Your "passport photo" icon

    // The 3D model to spawn in the menu and in the game
    public GameObject characterPrefab;
    public Vector3 previewOffset;
}