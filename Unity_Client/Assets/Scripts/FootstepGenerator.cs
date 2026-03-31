using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class FootstepGenerator : MonoBehaviour
{
    [Header("Audio Components")]
    public AudioSource audioSource;
    [Range(0f, 1f)] public float footstepVolume = 0.35f;

    [Header("Surface Audio Clips")]
    public AudioClip[] grassSounds;
    public AudioClip[] dirtSounds;
    public AudioClip[] stoneSounds;
    public AudioClip[] defaultSounds;

    [Header("Raycast Settings")]
    [Tooltip("Assign your Ground or Environment layer here to ignore players/obstacles.")]
    public LayerMask groundLayer;

    [Header("Movement & Tracking")]
    [Tooltip("Base distance to trigger a step when walking normally.")]
    public float baseStepDistance = 1.5f;
    [Tooltip("Minimum distance to trigger a step when moving fast.")]
    public float minStepDistance = 0.8f;
    [Tooltip("Speed required to reach the minimum step distance.")]
    public float maxSpeedThreshold = 5f;
    [Tooltip("Filters out webcam tracking jitter. Ignores movements smaller than this per frame.")]
    public float minMovementThreshold = 0.02f;

    private AvatarController _controller;
    private Vector3 _lastPosition;
    private float _accumulatedDistance = 0f;

    // Tracks the last played clip to prevent repetition
    private int _lastPlayedIndex = -1;

    void Start()
    {
        _controller = GetComponent<AvatarController>();
        if (audioSource == null) audioSource = GetComponent<AudioSource>();

        _lastPosition = transform.position;
    }

    void Update()
    {
        if (_controller != null && !_controller.enabled)
        {
            _lastPosition = transform.position;
            return;
        }

        Vector3 currentPos = transform.position;
        currentPos.y = 0;
        Vector3 lastPos = _lastPosition;
        lastPos.y = 0;

        float frameDistance = Vector3.Distance(currentPos, lastPos);

        if (frameDistance > minMovementThreshold)
        {
            _accumulatedDistance += frameDistance;

            float currentSpeed = frameDistance / Time.deltaTime;
            float speedRatio = Mathf.Clamp01(currentSpeed / maxSpeedThreshold);
            float dynamicStepDistance = Mathf.Lerp(baseStepDistance, minStepDistance, speedRatio);

            if (_accumulatedDistance >= dynamicStepDistance)
            {
                PlaySurfaceFootstep();
                _accumulatedDistance = 0f;
            }
        }

        _lastPosition = transform.position;
    }

    private void PlaySurfaceFootstep()
    {
        AudioClip[] selectedSounds = defaultSounds;

        // FIX 2: Optimized Raycast with LayerMask
        if (Physics.Raycast(transform.position + (Vector3.up * 0.5f), Vector3.down, out RaycastHit hit, 2f, groundLayer))
        {
            if (hit.collider.CompareTag("Grass") && grassSounds.Length > 0)
                selectedSounds = grassSounds;
            else if (hit.collider.CompareTag("Dirt") && dirtSounds.Length > 0)
                selectedSounds = dirtSounds;
            else if (hit.collider.CompareTag("Stone") && stoneSounds.Length > 0)
                selectedSounds = stoneSounds;
        }

        if (selectedSounds == null || selectedSounds.Length == 0) return;

        // FIX 1: Repeat Sound Prevention
        int index;
        int infiniteLoopFailsafe = 0;
        do
        {
            index = Random.Range(0, selectedSounds.Length);
            infiniteLoopFailsafe++;
        }
        while (index == _lastPlayedIndex && selectedSounds.Length > 1 && infiniteLoopFailsafe < 10);

        _lastPlayedIndex = index;
        AudioClip clip = selectedSounds[index];

        audioSource.pitch = Random.Range(0.85f, 1.15f);
        audioSource.PlayOneShot(clip, footstepVolume);
    }
}