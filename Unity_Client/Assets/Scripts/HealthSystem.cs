using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class HealthSystem : MonoBehaviour
{
    [Header("Health Settings")]
    public float maxHealth = 100f;
    public bool enableRegen = true; // Toggle this OFF for hardcore mode
    public float healthRegenRate = 5f;
    public float regenDelay = 5f; // Increased default to 5 seconds

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

    [Header("Audio/Visual Feedback")]
    public AudioClip hitSound;
    public AudioClip knockoutSound;
    public ParticleSystem hitEffect;
    public ParticleSystem knockoutEffect;

    private float currentHealth;
    private float lastHitTime;
    private bool isKnockedOut = false;
    private Animator animator;
    private AvatarController avatarController;
    private Coroutine regenCoroutine;
    private Coroutine stunCoroutine;

    public System.Action<float> OnDamageTaken;
    public System.Action OnKnockout;
    public System.Action OnRecovered;

    void Start()
    {
        currentHealth = maxHealth;
        animator = GetComponent<Animator>();
        avatarController = GetComponent<AvatarController>();
        InitializeUI();
        regenCoroutine = StartCoroutine(HealthRegeneration());
    }

    void InitializeUI()
    {
        if (healthSlider != null) { healthSlider.maxValue = maxHealth; healthSlider.value = maxHealth; }
        if (healthFillImage != null) healthFillImage.color = fullHealthColor;
        if (knockoutText != null) knockoutText.SetActive(false);
    }

    public void TakeDamage(float damageAmount, string punchType = "normal", Vector3 hitDirection = default)
    {
        if (isKnockedOut) return;

        float actualDamage = punchType == "strong" ? strongPunchDamage : punchDamage;
        currentHealth -= actualDamage;
        lastHitTime = Time.time; // Reset regen timer

        UpdateHealthUI();
        PlayHitEffects(hitDirection);
        OnDamageTaken?.Invoke(actualDamage);

        if (stunCoroutine != null) StopCoroutine(stunCoroutine);
        stunCoroutine = StartCoroutine(PunchStunEffect());

        if (currentHealth <= 0)
        {
            currentHealth = 0;
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

    private void UpdateHealthUI()
    {
        if (healthSlider != null) healthSlider.value = currentHealth;
        if (healthFillImage != null)
        {
            float healthPercent = currentHealth / maxHealth;
            healthFillImage.color = Color.Lerp(lowHealthColor, fullHealthColor, healthPercent);
        }
    }

    private void PlayHitEffects(Vector3 hitDirection)
    {
        if (hitSound != null) AudioSource.PlayClipAtPoint(hitSound, transform.position);
        if (hitEffect != null) { hitEffect.transform.position = transform.position + hitDirection * 0.5f; hitEffect.Play(); }
    }

    private IEnumerator PunchStunEffect()
    {
        if (avatarController != null) avatarController.enabled = false;
        yield return new WaitForSeconds(punchStunDuration);
        if (avatarController != null && !isKnockedOut) avatarController.enabled = true;
    }

    private void Knockout()
    {
        isKnockedOut = true;
        if (animator != null) animator.SetTrigger("Knockout");
        if (avatarController != null) avatarController.enabled = false;
        if (knockoutSound != null) AudioSource.PlayClipAtPoint(knockoutSound, transform.position);
        if (knockoutEffect != null) knockoutEffect.Play();
        if (knockoutText != null) knockoutText.SetActive(true);
        OnKnockout?.Invoke();
        StartCoroutine(RecoveryProcess());
    }

    private IEnumerator RecoveryProcess()
    {
        yield return new WaitForSeconds(knockoutRecoveryTime);
        Recover();
    }

    public void Recover()
    {
        isKnockedOut = false;
        currentHealth = maxHealth * 0.3f;
        UpdateHealthUI();
        if (avatarController != null) avatarController.enabled = true;
        if (knockoutText != null) knockoutText.SetActive(false);
        OnRecovered?.Invoke();
    }

    private IEnumerator HealthRegeneration()
    {
        while (true)
        {
            yield return new WaitForSeconds(1f);

            if (enableRegen && !isKnockedOut &&
                Time.time - lastHitTime > regenDelay &&
                currentHealth < maxHealth)
            {
                currentHealth = Mathf.Min(currentHealth + healthRegenRate, maxHealth);
                UpdateHealthUI();
            }
        }
    }

    public bool IsKnockedOut() => isKnockedOut;
    public float GetHealthPercentage() => currentHealth / maxHealth;
    public float GetCurrentHealth() => currentHealth;

    void OnDestroy()
    {
        if (regenCoroutine != null) StopCoroutine(regenCoroutine);
        if (stunCoroutine != null) StopCoroutine(stunCoroutine);
    }
}