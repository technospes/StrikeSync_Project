// ============================================================
//  AvatarController.cs  —  v14.1
//
//  ROOT CAUSE OF "NOTHING MOVES / NOTHING PUNCHES":
//
//  canFight is NEVER set to true on the object PoseManager talks to.
//  PoseManager.avatarPlayer1/2 are inspector-assigned scene objects.
//  GameManager spawns NEW prefab instances and sets canFight on THOSE.
//  They are different GameObjects. The one receiving UDP data never
//  gets canFight=true, so ApplyServerMovement() and DriveAnimator()
//  are never called. Punches arrive, ReceivePunch() runs, SetTrigger
//  fires — but _anim is the animator on the wrong (invisible) object,
//  OR _anim is null because the inspector object has no Animator.
//
//  FIX A: SetPlayerID() sets canFight=true immediately.
//         This is the ONLY reliable moment — it's called by PoseManager
//         the first time a packet arrives, on the exact object receiving data.
//
//  FIX B: ReceivePunch now also sets canFight=true as a safety net,
//         because punches can arrive before SetPlayerID if playerID
//         was already set in the inspector.
//
//  v14.1 CHANGES:
//
//  1. WALK SUPPRESSION TIMER reduced from 0.5s → 0.25s.
//     Python (pose_server v12) now suppresses move_x AND move_z for
//     PUNCH_COOLDOWN_S (0.55s) the instant a punch fires, so the bad
//     packets never reach Unity. The Unity timer is a secondary safety
//     net for the single packet that may already be in-flight when the
//     punch is detected (one UDP frame of latency ≈ 30–50 ms).
//     0.25s = enough to cover two late packets at 30 fps; shorter than
//     before so genuine movement resumes faster after a combo.
//
//  2. CALIBRATION: EvaluateCalibration() is NO LONGER called inside
//     ReceiveKeypoints(). Python sends authoritative calib_score and
//     calib_ready every packet; ReceiveCalibration() is the single
//     source of truth. Removing the Unity-side check eliminates the
//     per-frame disagreement that caused calibration overlay flicker.
//
//  NEW FEATURE — CALIBRATION SYSTEM:
//  Tracks which YOLO keypoints are visible and whether the full body
//  is in frame. Sends calibration_status back to Unity each packet.
//  Unity can show an overlay UI and block the fight until calibrated.
//  Calibration states: UNCALIBRATED → CALIBRATING → READY → LOST
// ============================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[DefaultExecutionOrder(0)]
public class AvatarController : MonoBehaviour
{
    // ─── PRIVATE: Input Smoothing (Layer 1) ───────────────────────────────────
    private float _lastValidX = 0f;
    private float _lastValidZ = 0f;
    private float _timerX = 0f;
    private float _timerZ = 0f;
    // With WASD binary output, the signal is either 1.0 or exactly 0.0 — no
    // gradual ramp-down. KEEP_ALIVE_BUFFER is the grace period after the signal
    // drops to 0 before Unity snaps the animator. At 15 pkt/s a 2-packet dropout
    // = 133ms; 0.20s bridges that without any perceived glide.
    private const float KEEP_ALIVE_BUFFER = 0.20f;

    // v14.1: reduced from 0.5s to 0.25s.
    // Python already suppresses move_x/z for PUNCH_COOLDOWN_S (0.55s) the frame
    // a punch fires, so bad packets never arrive. This Unity-side timer only
    // catches the one packet that was already in-flight when Python detected the
    // punch (≈1 frame of UDP latency). 0.25s is more than enough.
    private float _movementSuppressTimer = 0.20f;

    // ─── PLAYER IDENTITY ──────────────────────────────────────────────────────
    [Header("=== PLAYER IDENTITY ===")]
    public int playerID = -1;
    public bool IsPlayer1 => playerID == 0;

    // ─── MAP & GROUND ─────────────────────────────────────────────────────────
    [Header("=== MAP & GROUND ===")]
    public float minMapX = -8.0f;
    public float maxMapX = 8.0f;
    [Tooltip("Leave at -999 to auto-detect from spawn Y position.")]
    public float groundY = -999f;

    // ─── SERVER-DRIVEN MOVEMENT ───────────────────────────────────────────────
    [Header("=== SERVER-DRIVEN MOVEMENT ===")]
    public float moveSpeedX = 1.0f;
    public float moveSpeedZ = 0.5f;
    [Tooltip("How fast _animMoveX/Z lerp toward target. Higher = snappier.")]
    public float animLerpRate = 10f;
    [Tooltip("Seconds of server silence before move values start decaying.")]
    public float serverIdleAfterS = 0.50f;
    [Tooltip("Disable if move_z is causing unwanted drifting (shoulder-width noise). Enable once calibrated.")]
    public bool enableMoveZ = false;   // ← OFF by default until camera sees full body

    // ─── COMBAT STATE ─────────────────────────────────────────────────────────
    [Header("=== COMBAT STATE ===")]
    public bool canFight = false;

    // ─── HITBOXES ─────────────────────────────────────────────────────────────
    [Header("=== HITBOXES ===")]
    public Hitbox leftHandHitbox;
    public Hitbox rightHandHitbox;
    public float hitboxActiveTime = 0.3f;

    // ─── CALIBRATION ──────────────────────────────────────────────────────────
    [Header("=== CALIBRATION ===")]
    public bool normalizeScale = true;
    public float targetHeight = 1.8f;
    public float poseScale = 1.0f;
    public Vector3 poseOffset = Vector3.zero;
    [Range(0.0f, 0.95f)] public float poseSmoothingFactor = 0.6f;

    [Header("=== CALIBRATION UI ===")]
    [Tooltip("Overlay panel shown when body is not fully tracked.")]
    public GameObject calibrationOverlay;
    [Tooltip("Text inside the overlay — shows current status message.")]
    public TextMeshProUGUI calibrationText;
    [Tooltip("Progress bar filling as calibration improves (0-1).")]
    public Slider calibrationBar;

    // ─── IK TRACKING ──────────────────────────────────────────────────────────
    [Header("=== IK TRACKING ===")]
    public bool useIKTracking = false;
    public Transform headTarget;
    public Transform leftHandTarget;
    public Transform rightHandTarget;
    public Transform leftElbowTarget;
    public Transform rightElbowTarget;

    // ─── PUNCH ────────────────────────────────────────────────────────────────
    [Header("=== PUNCH ===")]
    public float punchCooldown = 0.4f;

    // ─── DEBUG ────────────────────────────────────────────────────────────────
    [Header("=== DEBUG ===")]
    public bool debugMode = false;
    public bool mirrorInput = true;
    public bool showGizmos = true;

    // ─── PRIVATE: Components ──────────────────────────────────────────────────
    private Animator _anim;
    private HealthSystem _health;

    // ─── PRIVATE: Landmarks ───────────────────────────────────────────────────
    private List<LandmarkData> _keypoints;
    private Vector3[] _smoothedLandmarks = new Vector3[17];
    private Vector3[] _targetLandmarks = new Vector3[17];

    // ─── PRIVATE: Server movement ─────────────────────────────────────────────
    private float _srvMoveX = 0f;
    private float _srvMoveZ = 0f;
    private float _animMoveX = 0f;
    private float _animMoveZ = 0f;
    private float _smoothedZDir = 0f;
    private float _lastPacketTime = 0f;

    // ─── PRIVATE: Punch ───────────────────────────────────────────────────────
    private float _lastLeftPunchTime = -999f;
    private float _lastRightPunchTime = -999f;
    private float _leftHandVelocity = 0f;
    private float _rightHandVelocity = 0f;

    // ─── PRIVATE: Calibration ─────────────────────────────────────────────────
    // Calibration state received from Python each packet.
    // EvaluateCalibration() (Unity-side landmark check) is intentionally NOT
    // called during normal play — Python's calib_score/calib_ready are the
    // authoritative values. The Unity check was causing overlay flicker because
    // both systems wrote _calibReady on the same frame and could disagree.
    private string _calibStatus = "UNCALIBRATED";
    private float _calibScore = 0f;   // 0..1
    private bool _calibReady = false;
    private float _calibLostTimer = 0f;

    // ─── PRIVATE: Stun ────────────────────────────────────────────────────────
    private bool _isStunned = false;

    // ─── KEYPOINT INDICES ─────────────────────────────────────────────────────
    private const int Nose = 0;
    private const int LeftShoulder = 5;
    private const int RightShoulder = 6;
    private const int LeftElbow = 7;
    private const int RightElbow = 8;
    private const int LeftWrist = 9;
    private const int RightWrist = 10;
    private const int LeftHip = 11;
    private const int RightHip = 12;

    // Critical keypoints that must all be visible for full-body calibration
    // (shoulders, elbows, wrists, hips — 8 points)
    private static readonly int[] CriticalKeypoints = { 5, 6, 7, 8, 9, 10, 11, 12 };

    // =========================================================================
    void Start()
    {
        _anim = GetComponent<Animator>();
        _health = GetComponent<HealthSystem>();

        // normalizeScale must run BEFORE groundY capture —
        // scaling changes the effective foot position.
        if (normalizeScale)
        {
            var cap = GetComponent<CapsuleCollider>();
            if (cap != null && cap.height > 0.1f)
            {
                float curH = cap.height * transform.localScale.y;
                if (curH > 0.1f)
                    transform.localScale *= targetHeight / curH;
            }
        }

        // Auto-detect ground Y from spawn position (after scale is applied)
        if (groundY < -998f)
            groundY = transform.position.y;

        for (int i = 0; i < 17; i++)
        {
            _targetLandmarks[i] = transform.position;
            _smoothedLandmarks[i] = transform.position;
        }

        _lastPacketTime = Time.time;

        // Show calibration overlay immediately at start
        SetCalibrationUI("Stand in frame — full body visible", 0f, show: true);

        Debug.Log($"<color=cyan>[{name}] AvatarController v14.3 ready | " +
                  $"groundY={groundY:F2} | canFight={canFight} | " +
                  $"playerID={playerID} | enableMoveZ={enableMoveZ}</color>");
    }

    void Update()
    {
        // Lock Y to ground, clamp X to walls.
        // Z is locked to 0 — this is a 2D side-scroller, depth is expressed as X movement.
        Vector3 pos = transform.position;
        pos.y = groundY;
        //pos.z = 0f;   // hard lock — step-forward maps to X, not Z
        pos.x = Mathf.Clamp(pos.x, minMapX, maxMapX);
        transform.position = pos;

        float lerpF = 1f - poseSmoothingFactor;
        for (int i = 0; i < 17; i++)
            _smoothedLandmarks[i] = Vector3.Lerp(_smoothedLandmarks[i], _targetLandmarks[i], lerpF);

        if (useIKTracking) UpdateIKTargets();

        // Calibration lost detection — if no packets for a while, warn
        if (_calibReady && Time.time - _lastPacketTime > 2f)
        {
            _calibLostTimer += Time.deltaTime;
            if (_calibLostTimer > 1f)
            {
                _calibReady = false;
                SetCalibrationUI(" Tracking lost — step back into frame", 0f, show: true);
            }
        }
        else
        {
            _calibLostTimer = 0f;
        }

        if (canFight)
        {
            ProcessMovement();
        }
        else
        {
            // Even before canFight, keep the animator idle so the character doesn't T-pose.
            // Movement values are ignored until GameManager calls EnableFighting().
            if (_anim != null)
            {
                _anim.SetFloat("MoveX", 0f);
                _anim.SetFloat("MoveZ", 0f);
            }
        }
    }

    // =========================================================================
    //  PUBLIC API  (called by PoseManager)
    // =========================================================================

    public void SetPlayerID(int id)
    {
        playerID = id;
        mirrorInput = true;

        // If canFight is already true (set by GameManager), respect it.
        // If no GameManager is present (e.g. standalone test), enable automatically
        // so movement and punches work without needing the countdown sequence.
        if (!canFight && FindObjectOfType<GameManager>() == null)
        {
            canFight = true;
            Debug.Log($"<color=yellow>[{name}] No GameManager found — auto-enabling canFight for standalone mode</color>");
        }

        Debug.Log($"<color=cyan>[{name}] Player {id + 1} assigned. canFight={canFight}</color>");
    }

    /// <summary>
    /// Called by GameManager after the countdown finishes.
    /// This is the ONLY correct place to unlock fighting.
    /// </summary>
    public void EnableFighting()
    {
        canFight = true;
        Debug.Log($"<color=lime>[{name}] canFight = TRUE — FIGHT!</color>");
    }

    private void ProcessMovement()
    {
        // ── LAYER 1: Hard Network Stop (Safety) ──
        if (Time.time - _lastPacketTime > serverIdleAfterS)
        {
            _srvMoveX = 0f;
            _srvMoveZ = 0f;
        }

        // ── LAYER 1.5: Combat Suppression ────────────────────────────────────
        // Python (v12) already zeroes move_x and move_z for PUNCH_COOLDOWN_S the
        // frame a punch fires. This Unity-side timer is a secondary net that
        // catches the single packet already in-flight when the punch was detected
        // (one UDP round-trip ≈ 30–50 ms at 30 fps). 0.25s covers two late
        // packets with margin; it used to be 0.5s but that was longer than
        // necessary now that Python handles the primary suppression.
        if (_movementSuppressTimer > 0f)
        {
            _movementSuppressTimer -= Time.deltaTime;
            _srvMoveX = 0f;
            _srvMoveZ = 0f;
            _lastValidX = 0f;
            _lastValidZ = 0f;
            _timerX = 0f;
            _timerZ = 0f;
        }

        // ── LAYER 2: Input Stabilization ──
        float cleanX = FilterInput(_srvMoveX, ref _lastValidX, ref _timerX);
        float cleanZ = enableMoveZ ? FilterInput(_srvMoveZ, ref _lastValidZ, ref _timerZ) : 0f;

        // ── LAYER 3: Physics ──
        Vector3 p = transform.position;

        if (Mathf.Abs(cleanX) > 0.01f)
        {
            float dirX = IsPlayer1 ? cleanX : -cleanX;
            p.x += dirX * moveSpeedX * Time.deltaTime;
        }

        if (Mathf.Abs(cleanZ) > 0.01f)
        {
            float dirZ = IsPlayer1 ? cleanZ : -cleanZ;
            p.x += dirZ * moveSpeedZ * Time.deltaTime;
        }

        p.x = Mathf.Clamp(p.x, minMapX, maxMapX);
        transform.position = p;

        // ── LAYER 4: Animator (instant snap — no Lerp) ───────────────────────
        // Python v13 outputs crisp ±1.0 or 0.0 (WASD model).
        // Lerp would add lag between the binary signal and the animation,
        // making stops feel floaty. We snap directly.
        if (_anim != null)
        {
            _anim.SetFloat("MoveX", cleanX);
            _anim.SetFloat("MoveZ", cleanZ);
        }
    }

    private float FilterInput(float raw, ref float lastValid, ref float timer)
    {
        // If we have a strong signal, reset timer and update the valid hold value
        if (Mathf.Abs(raw) > 0.05f)
        {
            lastValid = raw;
            timer = 0f;
            return raw;
        }

        // If signal drops, start the Keep-Alive timer
        timer += Time.deltaTime;

        if (timer > KEEP_ALIVE_BUFFER)
        {
            lastValid = 0f;
            return 0f;
        }

        // Within the 350ms window? Hold the last known good trajectory.
        return lastValid;
    }

    /// <summary>Forward raw landmarks for IK smoothing.</summary>
    public void ReceiveKeypoints(List<LandmarkData> kpts)
    {
        // playerID guard removed — PoseManager always calls SetPlayerID before ReceiveKeypoints.
        // Guarding on playerID==-1 blocked ALL packets if the prefab kept default playerID=-1.
        if (kpts == null || kpts.Count < 17) return;
        _keypoints = kpts;
        UpdateTargetLandmarks();

        // v14.1: EvaluateCalibration() intentionally NOT called here.
        // Python sends calib_score and calib_ready every packet.
        // ReceiveCalibration() is the single authoritative source.
        // Calling both caused per-frame disagreements that flickered the
        // calibration overlay on and off every few frames.
    }

    /// <summary>Python-computed horizontal move [-1..1].</summary>
    public void ReceiveServerMoveX(float rawX)
    {
        _srvMoveX = rawX;
        _lastPacketTime = Time.time;
        if (debugMode)
            Debug.Log($"<color=cyan>[{name}] RX move_x={rawX:F3} canFight={canFight} playerID={playerID}</color>");
    }

    /// <summary>Python-computed depth move [-1..1].</summary>
    public void ReceiveServerMoveZ(float rawZ)
    {
        _srvMoveZ = rawZ;
        _lastPacketTime = Time.time;
    }

    /// <summary>
    /// Single-shot punch from Python. Seeds hand velocity for Hitbox.cs,
    /// enables the hitbox collider, fires the animator trigger.
    ///
    /// v14.1: _movementSuppressTimer reduced to 0.25s (was 0.5s).
    /// Python now handles primary suppression for PUNCH_COOLDOWN_S (0.55s).
    /// This timer only covers the one packet already in-flight.
    /// </summary>
    public void ReceivePunch(string hand)
    {
        if (!canFight) return;
        if (_health != null && _health.IsKnockedOut()) return;
        float now = Time.time;

        if (hand == "Left" && (now - _lastLeftPunchTime) >= punchCooldown)
        {
            _lastLeftPunchTime = now;
            _leftHandVelocity = 2.5f;
            // Secondary safety net — Python already suppressed move_x/z at the source.
            // 0.25s covers one late UDP packet at 30fps with plenty of margin.
            _movementSuppressTimer = 0.2f;
            if (leftHandHitbox) StartCoroutine(ManageHitbox(leftHandHitbox, isLeft: true));
            _anim?.SetTrigger("PunchLeft");
        }
        else if (hand == "Right" && (now - _lastRightPunchTime) >= punchCooldown)
        {
            _lastRightPunchTime = now;
            _rightHandVelocity = 2.5f;
            _movementSuppressTimer = 0.2f;
            if (rightHandHitbox) StartCoroutine(ManageHitbox(rightHandHitbox, isLeft: false));
            _anim?.SetTrigger("PunchRight");
        }
    }

    public void ReceiveJump() => _anim?.SetTrigger("Jump");

    // Stun/hit — called by HealthSystem
    public void SetStunned(bool stunned)
    {
        _isStunned = stunned;
        _anim?.SetBool("Stunned", stunned);
        if (debugMode) Debug.Log($"[STUN] {name} stunned={stunned}");
    }

    public void TriggerHitReaction()
    {
        _anim?.SetTrigger("Hit");
        if (debugMode) Debug.Log($"[HIT] {name} hit reaction");
    }

    public float GetLeftHandVelocity() => _leftHandVelocity;
    public float GetRightHandVelocity() => _rightHandVelocity;
    public bool IsCalibrated() => _calibReady;

    /// <summary>
    /// Called by PoseManager each packet with Python-computed calibration data.
    /// This is the ONLY place _calibReady is written during normal gameplay.
    /// EvaluateCalibration() (Unity-side keypoint check) is no longer called
    /// from ReceiveKeypoints() to prevent the two systems disagreeing on the
    /// same frame and flickering the overlay.
    /// </summary>
    public void ReceiveCalibration(float score, bool ready)
    {
        _calibScore = score;

        if (ready && !_calibReady)
        {
            // Just became calibrated
            _calibReady = true;
            SetCalibrationUI(" CALIBRATED — FIGHT!", 1f, show: false);
            Debug.Log($"<color=lime>[{name}] Calibration COMPLETE from Python</color>");
        }
        else if (!ready && _calibReady)
        {
            // Lost calibration
            _calibReady = false;
        }

        if (!ready)
        {
            string hint = score > 0.5f
                ? $"Calibrating… {Mathf.RoundToInt(score * 100)}%"
                : " Step back — full body must be visible";
            SetCalibrationUI(hint, score, show: true);
        }
    }

    // =========================================================================
    //  CALIBRATION (Unity-side — kept for reference / inspector testing only)
    //  NOT called during normal gameplay. Python is the authoritative source.
    // =========================================================================

    /// <summary>
    /// Reads visibility scores from the keypoint packet.
    /// Kept for diagnostic / inspector use only.
    /// Not called during normal play — see ReceiveCalibration().
    /// </summary>
    private void EvaluateCalibration(List<LandmarkData> kpts)
    {
        if (kpts == null || kpts.Count < 13)
        {
            SetCalibrationUI(" Body not detected — step back, face camera", 0f, show: true);
            _calibReady = false;
            return;
        }

        int visible = 0;
        foreach (int idx in CriticalKeypoints)
        {
            if (idx < kpts.Count && kpts[idx].v > 0.45f)
                visible++;
        }

        float score = (float)visible / CriticalKeypoints.Length;  // 0..1
        _calibScore = score;

        if (score >= 1.0f)
        {
            if (!_calibReady)
            {
                _calibReady = true;
                SetCalibrationUI(" READY — FIGHT!", 1f, show: false);
                Debug.Log($"<color=lime>[{name}] Calibration COMPLETE — all body parts visible</color>");
            }
        }
        else if (score >= 0.5f)
        {
            _calibReady = false;
            string hint = GetCalibrationHint(kpts);
            SetCalibrationUI($"Calibrating... {hint}", score, show: true);
        }
        else
        {
            _calibReady = false;
            SetCalibrationUI(" Step back so your full body is visible", score, show: true);
        }
    }

    /// <summary>Returns a context-aware hint based on which joints are missing.</summary>
    private string GetCalibrationHint(List<LandmarkData> kpts)
    {
        bool hipsOk = kpts.Count > 12 && kpts[11].v > 0.45f && kpts[12].v > 0.45f;
        bool shouldersOk = kpts.Count > 6 && kpts[5].v > 0.45f && kpts[6].v > 0.45f;
        bool wristsOk = kpts.Count > 10 && kpts[9].v > 0.45f && kpts[10].v > 0.45f;

        if (!hipsOk) return "Step back — hips must be visible";
        if (!wristsOk) return "Raise your arms so hands are in frame";
        if (!shouldersOk) return "Face the camera directly";
        return "Hold still for a moment";
    }

    /// <summary>Updates the calibration overlay UI elements.</summary>
    private void SetCalibrationUI(string message, float progress, bool show)
    {
        if (calibrationOverlay != null)
            calibrationOverlay.SetActive(show);

        if (calibrationText != null)
            calibrationText.text = message;

        if (calibrationBar != null)
            calibrationBar.value = progress;
    }

    // =========================================================================
    //  HITBOX
    // =========================================================================

    private IEnumerator ManageHitbox(Hitbox hitbox, bool isLeft)
    {
        hitbox.EnableHitbox();
        yield return new WaitForSeconds(hitboxActiveTime);
        if (hitbox != null)
        {
            hitbox.GetComponent<Collider>().enabled = false;
            if (isLeft) _leftHandVelocity = 0f;
            else _rightHandVelocity = 0f;
        }
    }

    // =========================================================================
    //  LANDMARKS
    // =========================================================================

    private void UpdateTargetLandmarks()
    {
        Vector3 basePos = transform.position;
        Quaternion baseRot = transform.rotation;

        for (int i = 0; i < 17; i++)
        {
            int src = mirrorInput ? GetMirroredIndex(i) : i;
            if (src >= _keypoints.Count) continue;

            float x_c = _keypoints[src].x - 0.5f;
            float y_c = 0.5f - _keypoints[src].y;
            _targetLandmarks[i] = basePos + baseRot * ((new Vector3(x_c, y_c, 0f) + poseOffset) * poseScale);
        }
    }

    private int GetMirroredIndex(int i)
    {
        int[] map = { 0, 2, 1, 4, 3, 6, 5, 8, 7, 10, 9, 12, 11, 14, 13, 16, 15 };
        return i < map.Length ? map[i] : i;
    }

    private Vector3 GetLandmarkPosition(int idx) =>
        _smoothedLandmarks != null && idx < _smoothedLandmarks.Length
            ? _smoothedLandmarks[idx] : Vector3.zero;

    // =========================================================================
    //  IK
    // =========================================================================

    private void UpdateIKTargets()
    {
        if (headTarget) headTarget.position = GetLandmarkPosition(Nose);
        if (leftHandTarget) leftHandTarget.position = GetLandmarkPosition(LeftWrist);
        if (rightHandTarget) rightHandTarget.position = GetLandmarkPosition(RightWrist);
        if (leftElbowTarget) leftElbowTarget.position = GetLandmarkPosition(LeftElbow);
        if (rightElbowTarget) rightElbowTarget.position = GetLandmarkPosition(RightElbow);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (!useIKTracking || _keypoints == null) return;
        SetIK(AvatarIKGoal.LeftHand, leftHandTarget, AvatarIKHint.LeftElbow, leftElbowTarget);
        SetIK(AvatarIKGoal.RightHand, rightHandTarget, AvatarIKHint.RightElbow, rightElbowTarget);
        if (headTarget) { _anim.SetLookAtWeight(1); _anim.SetLookAtPosition(headTarget.position); }
    }

    void SetIK(AvatarIKGoal goal, Transform t, AvatarIKHint hint, Transform ht)
    {
        if (t) { _anim.SetIKPositionWeight(goal, 1); _anim.SetIKPosition(goal, t.position); }
        if (ht) { _anim.SetIKHintPositionWeight(hint, 1); _anim.SetIKHintPosition(hint, ht.position); }
    }

    // =========================================================================
    //  GIZMOS
    // =========================================================================

    void OnDrawGizmos()
    {
        if (!showGizmos || _smoothedLandmarks == null || _smoothedLandmarks.Length < 17) return;
        Gizmos.color = Color.cyan;
        foreach (var p in _smoothedLandmarks) Gizmos.DrawSphere(p, 0.015f);
    }
}