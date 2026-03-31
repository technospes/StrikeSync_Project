using UnityEngine;
using System.Collections;

/// <summary>
/// AutoLevelCharacter — Y-grounding only.
/// 
/// ONLY does one thing: shifts the visual model child up/down so the
/// bottom of its feet sit exactly at the root object's Y position (groundY).
/// 
/// Does NOT touch:
///   • transform.localScale  (no resizing)
///   • CapsuleCollider       (no collider reshaping)
///   • Any other children    (AudioSource, Hitboxes, etc.)
/// </summary>
public class AutoLevelCharacter : MonoBehaviour
{
    [Header("Target Visuals")]
    [Tooltip("Drag the child object containing the actual 3D model/mesh here. " +
             "If left blank, the first child is used automatically.")]
    public Transform visualModel;

    [Header("Ground Offset Fine-Tune")]
    [Tooltip("Extra nudge in world units applied on top of the auto-calculation. " +
             "Positive = lift the character up, Negative = push down. Default 0.")]
    public float extraYOffset = 0f;

    void Start()
    {
        // Auto-assign the first child if none was dragged in
        if (visualModel == null && transform.childCount > 0)
            visualModel = transform.GetChild(0);

        StartCoroutine(GroundNextFrame());
    }

    private IEnumerator GroundNextFrame()
    {
        // Wait one frame so AvatarController's scale normalization runs first
        yield return new WaitForEndOfFrame();

        if (visualModel == null) yield break;

        // ── Step 1: Temporarily zero rotation so bounds are axis-aligned ──
        Quaternion originalRot = transform.rotation;
        transform.rotation = Quaternion.identity;

        // ── Step 2: Collect all renderers inside the visual model only ─────
        Renderer[] renderers = visualModel.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
        {
            transform.rotation = originalRot;
            yield break;
        }

        // ── Step 3: Measure the lowest point of the mesh ───────────────────
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer r in renderers)
        {
            if (r is ParticleSystemRenderer) continue; // skip FX
            bounds.Encapsulate(r.bounds);
        }

        float lowestPoint = bounds.min.y;   // bottom of feet in world space
        float rootY = transform.position.y; // where we WANT the feet to be

        // How much do we need to shift the visual up so feet touch rootY?
        float offsetNeeded = rootY - lowestPoint;

        // ── Step 4: ONLY move the visual model's local Y — nothing else ────
        visualModel.localPosition += new Vector3(0f, offsetNeeded + extraYOffset, 0f);

        // ── Step 5: Restore original rotation ─────────────────────────────
        transform.rotation = originalRot;
    }
}