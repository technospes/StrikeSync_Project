using UnityEngine;

public class Hitbox : MonoBehaviour
{
    private AvatarController myAvatarController;
    private HealthSystem myHealthSystem;
    public float damageMultiplier = 1.0f;
    public string handType;
    private Collider hitboxCollider;

    void Start()
    {
        myAvatarController = GetComponentInParent<AvatarController>();
        myHealthSystem = GetComponentInParent<HealthSystem>();
        hitboxCollider = GetComponent<Collider>();
    }

    void OnTriggerEnter(Collider other)
    {
        GameObject hitObject = other.gameObject;
        HealthSystem opponentHealth = hitObject.GetComponentInParent<HealthSystem>();
        if (opponentHealth == null) opponentHealth = hitObject.GetComponent<HealthSystem>();

        // Strictly ensure we don't hit ourselves
        if (opponentHealth != null && opponentHealth != myHealthSystem)
        {
            float punchVelocity = 0f;
            if (handType == "Left") punchVelocity = myAvatarController.GetLeftHandVelocity();
            else if (handType == "Right") punchVelocity = myAvatarController.GetRightHandVelocity();

            // === FIX: ONLY DAMAGE IF VELOCITY IS HIGH ENOUGH ===
            // This prevents "walking into hands" causing damage.
            // 1.2 is the same threshold used in AvatarController to trigger a punch animation
            if (punchVelocity > 1.2f)
            {
                Debug.Log($"<color=red>HIT! Vel: {punchVelocity:F2}</color>");
                opponentHealth.TakeDamageFromPunch(punchVelocity * damageMultiplier, handType, transform.position);

                if (hitboxCollider != null) hitboxCollider.enabled = false;
            }
        }
    }

    public void EnableHitbox()
    {
        if (hitboxCollider != null) hitboxCollider.enabled = true;
    }
}