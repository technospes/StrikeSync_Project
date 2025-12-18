using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform player1;
    public Transform player2;

    public float followSpeed = 5f;
    public float minZoom = 10f; // Camera's Z position when close
    public float maxZoom = 25f; // Camera's Z position when far
    public float zoomLimiter = 10f;
    public Vector3 offset; // Use this to center the camera (e.g., Y=2)

    private Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();
    }

    void LateUpdate() // Always use LateUpdate for cameras
    {
        if (player1 == null || player2 == null) return;

        // 1. Find the center point between the players
        Vector3 centerPoint = (player1.position + player2.position) / 2f;

        // 2. Calculate the new camera position
        Vector3 newPosition = centerPoint + offset;

        // 3. Calculate the zoom level based on player distance
        float distance = Vector3.Distance(player1.position, player2.position);
        // This is a simple way to set the camera's Z pos
        float targetZ = Mathf.Lerp(minZoom, maxZoom, distance / zoomLimiter);

        // Apply the new Z position (relative to the players)
        // We use -targetZ because camera looks down negative Z
        newPosition.z = centerPoint.z - targetZ;

        // 4. Smoothly move the camera
        transform.position = Vector3.Lerp(transform.position, newPosition, Time.deltaTime * followSpeed);
    }
}