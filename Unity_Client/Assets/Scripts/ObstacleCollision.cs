using UnityEngine;
using System.Collections;

public class ObstacleCollision : MonoBehaviour
{
    [Header("Obstacle Settings")]
    [Tooltip("Assign the layer used for your map obstacles (e.g., 'ObstacleLayer')")]
    public LayerMask obstacleLayer;

    [Header("Stun & Knockback")]
    public float stunDuration = 0.3f;
    public float knockbackDistance = 1.0f;

    private AvatarController _controller;
    private Rigidbody _rb;
    private HealthSystem _healthSystem; // Added to prevent the "Zombie" bug
    private bool _isStunned = false;

    void Start()
    {
        _controller = GetComponent<AvatarController>();
        _rb = GetComponent<Rigidbody>();
        _healthSystem = GetComponent<HealthSystem>();
    }

    void OnCollisionEnter(Collision collision)
    {
        // 1. Check if the object hit is in our obstacle layer mask
        if (((1 << collision.gameObject.layer) & obstacleLayer) != 0)
        {
            if (_controller != null && !_isStunned)
            {
                // FIX 1: Safely handle physics edge-cases where contact array is empty
                Vector3 impactPoint;
                if (collision.contactCount > 0)
                {
                    impactPoint = collision.contacts[0].point;
                }
                else
                {
                    impactPoint = collision.transform.position; // Safe fallback
                }

                StartCoroutine(HandleStunAndKnockback(impactPoint));
            }
        }
    }

    private IEnumerator HandleStunAndKnockback(Vector3 impactPoint)
    {
        _isStunned = true;

        // Hard disable the controller so Python/User inputs are completely ignored
        // Also tell AvatarController it's stunned so it stops hard-locking Y in ApplyMovementAndClamp
        _controller.enabled = false;
        _controller.SetStunned(true);

        // Calculate Knockback Direction (Away from impact)
        Vector3 knockbackDir = (transform.position - impactPoint).normalized;
        knockbackDir.y = 0; // Lock to horizontal plane

        // Failsafe: if perfectly overlapping, just push them backward relative to their facing direction
        if (knockbackDir == Vector3.zero)
        {
            knockbackDir = -transform.forward;
        }

        float elapsed = 0f;
        Vector3 startPos = transform.position;

        // FIX 2: Added a slight buffer (0.1f) to the target position to guarantee they clear the collider
        Vector3 targetPos = startPos + (knockbackDir * (knockbackDistance + 0.1f));

        // Kinematic-Safe Slide
        while (elapsed < stunDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / stunDuration;

            // Ease-out cubic curve (snaps fast, eases to a stop)
            float curve = 1f - Mathf.Pow(1f - t, 3f);

            if (_rb != null)
            {
                _rb.MovePosition(Vector3.Lerp(startPos, targetPos, curve));
            }
            else
            {
                transform.position = Vector3.Lerp(startPos, targetPos, curve);
            }

            yield return null;
        }

        // FIX 3: Safety check before re-enabling control!
        // Prevents reviving a player who was killed during the 0.3s stun window.
        bool isKnockedOut = false;
        if (_healthSystem != null)
        {
            // Note: Update this line if your HealthSystem uses a different variable/method (e.g., currentHealth <= 0)
            isKnockedOut = _healthSystem.IsKnockedOut();
        }

        // Only give control back if they are actually still alive/active
        if (_controller != null && !isKnockedOut)
        {
            _controller.SetStunned(false);  // Restore AvatarController's ground-lock
            _controller.enabled = true;
        }

        _isStunned = false;
    }
}