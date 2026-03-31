using UnityEngine;

public class Hitbox : MonoBehaviour
{
    public float damageMultiplier = 1.0f;
    public string handType;

    // ── Lazy-initialized refs (safe even if Start() is skipped when disabled) ──
    private AvatarController _ctrl;
    private HealthSystem _myHealth;
    private Collider _col;

    private AvatarController Ctrl
    {
        get
        {
            if (_ctrl == null) _ctrl = GetComponentInParent<AvatarController>(true);
            return _ctrl;
        }
    }

    private HealthSystem MyHealth
    {
        get
        {
            if (_myHealth == null) _myHealth = GetComponentInParent<HealthSystem>(true);
            return _myHealth;
        }
    }

    private Collider Col
    {
        get
        {
            if (_col == null) _col = GetComponent<Collider>();
            return _col;
        }
    }

    // ── Called by AvatarController coroutine to arm this hitbox ───────────────
    public void EnableHitbox()
    {
        if (Col != null) Col.enabled = true;
    }

    void OnTriggerEnter(Collider other)
    {
        // Guard: controller must be found
        if (Ctrl == null) return;

        // Find opponent health — search upward from the hit collider
        HealthSystem opponentHealth = other.GetComponentInParent<HealthSystem>(true);
        if (opponentHealth == null)
            opponentHealth = other.GetComponent<HealthSystem>();

        // Never damage ourselves
        if (opponentHealth == null || opponentHealth == MyHealth) return;

        float vel = handType == "Left" ? Ctrl.GetLeftHandVelocity()
                                       : Ctrl.GetRightHandVelocity();

        // Minimum velocity guard — prevents "walk into fist" false hits
        if (vel > 1.2f)
        {
            Debug.Log($"<color=red>[HIT] {Ctrl.name} {handType} → {other.transform.root.name}  vel={vel:F2}</color>");
            opponentHealth.TakeDamageFromPunch(vel * damageMultiplier, handType, transform.position);

            // Disable self so we only deal one hit per swing
            if (Col != null) Col.enabled = false;
        }
    }
}