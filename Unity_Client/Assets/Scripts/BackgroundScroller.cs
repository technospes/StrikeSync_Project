using UnityEngine;

public class BackgroundScroller : MonoBehaviour
{
    public Transform player1;
    public Transform player2;
    public float scrollSpeed = 0.05f;
    public float maxScroll = 0.2f; // How far to scroll (0.0 to 0.5)

    private Material backgroundMaterial;
    private float initialCameraX;

    void Start()
    {
        backgroundMaterial = GetComponent<Renderer>().material;
        initialCameraX = Camera.main.transform.position.x;
    }

    void Update()
    {
        if (player1 == null || player2 == null) return;

        // Get the midpoint X of the players
        float playersMidpointX = (player1.position.x + player2.position.x) / 2f;

        // Calculate how far the midpoint has moved from the center
        float xOffset = (playersMidpointX - initialCameraX) * scrollSpeed;

        // Clamp the scroll amount so we don't see the edge
        xOffset = Mathf.Clamp(xOffset, -maxScroll, maxScroll);

        // Apply the offset to the texture
        backgroundMaterial.SetTextureOffset("_MainTex", new Vector2(xOffset, 0));
    }
}