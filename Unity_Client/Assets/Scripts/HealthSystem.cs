using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// HealthSystem — production version.
///
/// Changes vs original:
///   • BroadcastHealth() is throttled (max once per 50 ms) to prevent
///     message flooding when multiple hits land in the same frame.
///   • Uses Newtonsoft.Json + UnityWSBridge.SendHealthUpdate() for correct
///     serialisation of the structured payload.
///   • Calls SendHealthUpdate() on Recover() so React always stays in sync.
/// </summary>
public class HealthSystem : MonoBehaviour
{
    [Header("Health Settings")]
    public float maxHealth = 100f;
    public bool enableRegen = true;
    public float healthRegenRate = 5f;
    public float regenDelay = 5f;

    [Header("UI Elements")]
    public Slider healthSlider;
    public Image healthFillImage;
    public Color fullHealthColor = Color.green;
    public Color lowHealthColor = Color.red;
    public GameObject knockoutText;

    [Header("Combat Settings")]
    public float punchDamage = 10f;
    public float strongPunchDamage = 20f;
    public float punchStunDuration = 0.3f;
    public float knockoutRecoveryTime = 5f;

    [Header("Audio / Visual Feedback")]
    public AudioClip hitSound;
    public AudioClip knockoutSound;
    public ParticleSystem hitEffect;
    public ParticleSystem knockoutEffect;

    // ── Private state ────────────────────────────────────────────────────────
    private float _currentHealth;
    private float _lastHitTime;
    private bool _isKnockedOut;
    private Animator _animator;
    private AvatarController _avatarController;
    private Coroutine _regenCoroutine;
    private Coroutine _stunCoroutine;

    // Throttle: send health update at most once every 50 ms.
    private float _lastBroadcastTime = -1f;
    private const float BroadcastMinInterval = 0.05f;

    // ── Events ────────────────────────────────────────────────────────────────
    public System.Action<float> OnDamageTaken;
    public System.Action OnKnockout;
    public System.Action OnRecovered;

    // ── Unity lifecycle ───────────────────────────────────────────────────────
    void Start()
    {
        _currentHealth = maxHealth;
        _animator = GetComponent<Animator>();
        _avatarController = GetComponent<AvatarController>();

        InitializeUI();
        _regenCoroutine = StartCoroutine(HealthRegeneration());

        // Send initial health so React HUD is correct from frame 1.
        BroadcastHealth(force: true);
    }

    void InitializeUI()
    {
        if (healthSlider != null) { healthSlider.maxValue = maxHealth; healthSlider.value = maxHealth; }
        if (healthFillImage != null) healthFillImage.color = fullHealthColor;
        if (knockoutText != null) knockoutText.SetActive(false);
    }

    // ── Public API ────────────────────────────────────────────────────────────
    public void TakeDamage(float damageAmount, string punchType = "normal", Vector3 hitDirection = default)
    {
        if (_isKnockedOut) return;

        float actualDamage = punchType == "strong" ? strongPunchDamage : punchDamage;
        _currentHealth -= actualDamage;
        _lastHitTime = Time.time;

        UpdateHealthUI();
        PlayHitEffects(hitDirection);
        OnDamageTaken?.Invoke(actualDamage);
        BroadcastHealth();

        // Trigger the "Hit" animator parameter on the character being struck
        _avatarController?.TriggerHitReaction();

        if (_stunCoroutine != null) StopCoroutine(_stunCoroutine);
        _stunCoroutine = StartCoroutine(PunchStunEffect());

        if (_currentHealth <= 0)
        {
            _currentHealth = 0;
            Knockout();
        }
    }

    public void TakeDamageFromPunch(float velocity, string hand, Vector3 hitPosition)
    {
        float damageMultiplier = Mathf.Clamp(velocity / 2f, 0.5f, 2f);
        string punchType = velocity > 3f ? "strong" : "normal";
        Vector3 hitDirection = (hitPosition - transform.position).normalized;
        TakeDamage(punchDamage * damageMultiplier, punchType, hitDirection);
    }

    public void Recover()
    {
        _isKnockedOut = false;
        _currentHealth = maxHealth * 0.3f;

        UpdateHealthUI();
        BroadcastHealth(force: true);

        if (_avatarController != null)
        {
            _avatarController.SetStunned(false);  // Restore ground-lock after recovery
            _avatarController.enabled = true;
        }
        if (knockoutText != null) knockoutText.SetActive(false);
        OnRecovered?.Invoke();
    }

    public bool IsKnockedOut() => _isKnockedOut;
    public float GetHealthPercentage() => _currentHealth / maxHealth;
    public float GetCurrentHealth() => _currentHealth;

    // ── Private helpers ───────────────────────────────────────────────────────
    private void UpdateHealthUI()
    {
        if (healthSlider != null) healthSlider.value = _currentHealth;
        if (healthFillImage != null)
        {
            float pct = _currentHealth / maxHealth;
            healthFillImage.color = Color.Lerp(lowHealthColor, fullHealthColor, pct);
        }
    }

    private void PlayHitEffects(Vector3 hitDirection)
    {
        if (hitSound != null) AudioSource.PlayClipAtPoint(hitSound, transform.position);
        if (hitEffect != null)
        {
            hitEffect.transform.position = transform.position + hitDirection * 0.5f;
            hitEffect.Play();
        }
    }

    private IEnumerator PunchStunEffect()
    {
        if (_avatarController != null)
        {
            _avatarController.enabled = false;
            _avatarController.SetStunned(true);   // Unlocks Y so knockback arc isn't crushed
        }
        yield return new WaitForSeconds(punchStunDuration);
        if (_avatarController != null && !_isKnockedOut)
        {
            _avatarController.SetStunned(false);  // Restore ground-lock
            _avatarController.enabled = true;
        }
    }

    private void Knockout()
    {
        _isKnockedOut = true;

        if (_animator != null) _animator.SetTrigger("Knockout");
        if (_avatarController != null)
        {
            _avatarController.SetStunned(true);   // Free Y so knockout fall/ragdoll plays correctly
            _avatarController.enabled = false;
        }
        if (knockoutSound != null) AudioSource.PlayClipAtPoint(knockoutSound, transform.position);
        if (knockoutEffect != null) knockoutEffect.Play();
        if (knockoutText != null) knockoutText.SetActive(true);

        BroadcastHealth(force: true);
        OnKnockout?.Invoke();
        StartCoroutine(RecoveryProcess());
    }

    private IEnumerator RecoveryProcess()
    {
        yield return new WaitForSeconds(knockoutRecoveryTime);
        Recover();
    }

    private IEnumerator HealthRegeneration()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);

            if (enableRegen && !_isKnockedOut
                && Time.time - _lastHitTime > regenDelay
                && _currentHealth < maxHealth)
            {
                _currentHealth = Mathf.Min(_currentHealth + healthRegenRate, maxHealth);
                UpdateHealthUI();
                BroadcastHealth();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BroadcastHealth
    //
    // Throttled at BroadcastMinInterval (50 ms) to prevent message flooding.
    // Pass force:true to bypass the throttle (used on Knockout / Recover /
    // Start where a guaranteed immediate sync is critical).
    // ─────────────────────────────────────────────────────────────────────────
    private void BroadcastHealth(bool force = false)
    {
        if (UnityWSBridge.Instance == null || !UnityWSBridge.Instance.IsConnected)
            return;

        float now = Time.time;
        if (!force && (now - _lastBroadcastTime < BroadcastMinInterval))
            return;

        _lastBroadcastTime = now;

        AvatarController ac = GetComponent<AvatarController>();
        int pid = ac != null ? ac.playerID : -1;

        UnityWSBridge.Instance.SendHealthUpdate(pid, _currentHealth, maxHealth);
    }

    void OnDestroy()
    {
        if (_regenCoroutine != null) StopCoroutine(_regenCoroutine);
        if (_stunCoroutine != null) StopCoroutine(_stunCoroutine);
    }
}