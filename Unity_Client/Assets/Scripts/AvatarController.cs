using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  AvatarController — v8.2
//  v8.1: gap/Sad_idle fallback, lean guard using smoothed landmarks + sustained velocity.
//  v8.2: UpdateWalk rewrite — lerp can no longer un-zero a committed stop.
//        Stop path now returns before the lerp executes (see UpdateWalk comments).
//
// ─────────────────────────────────────────────────────────────────────────────
//  FIX-A  Glitchy walk loop (2–3 cycles before stopping)
//
//  Root cause (log evidence):
//    Python PERF: move_x=+0.971 → +1.000 → ... → +0.000 (clean stop in Python)
//    Unity log  : _lockedDir stays ±1, character keeps walking for 2–3 cycles
//
//  The Python EMA decays the move_x value across ~9 frames (~270ms at 33fps)
//  as the user returns toward neutral.  During that decay the value passes
//  through e.g. 0.27, 0.18, 0.12 — values above Unity's walkDeadZone=0.08.
//  Each such packet resets _idleTimer=0 in UpdateWalk, preventing the idle
//  timeout from ever completing.  _lockedDir stays set, _targetMoveX stays ±1,
//  character keeps walking.
//
//  FIX: Two-part fix.
//
//  (A1) Python side — STOP_ZONE hysteresis (see pose_server.py v8.0):
//       Walk STARTS when |displacement| >= WALK_ZONE (0.012).
//       Walk STOPS  when |displacement| <  STOP_ZONE (0.022).
//       STOP_ZONE > WALK_ZONE means the return stroke snaps smoothed_move_x=0
//       immediately when displacement falls back toward neutral.  No EMA decay
//       bleed, no sub-threshold non-zero values reaching Unity.
//
//  (A2) Unity side — moveIdleTimeout reduced 0.18s → 0.10s.
//       With the Python fix, stop signals arrive clean and fast.  A 100ms
//       timeout means the character stops within one walk-cycle of the
//       signal arriving.  The short timeout is safe because the Python
//       STOP_ZONE prevents false stops mid-lean.
//
// ─────────────────────────────────────────────────────────────────────────────
//  FIX-B  Lean still occasionally triggers punch
//
//  Two causes remain after v7 fixes:
//
//  (B1) leanGuardThreshold=0.08 misses slow leans.
//       The guard measures frame-to-frame hip delta.  A slow sustained lean
//       at constant velocity produces per-frame deltas of ~0.005–0.02, which
//       never exceed 0.08, so the guard never fires.  Yet arm velocity still
//       spikes because wrist landmarks jump when shoulders rotate.
//       FIX: Lower leanGuardThreshold 0.08 → 0.035.  This catches slow leans.
//       Also add a 3-frame sustained-lean window: if lean guard fired in ANY
//       of the last LEAN_GUARD_FRAMES frames, punch detection is suppressed.
//       This prevents a single non-lean frame from escaping the guard window.
//
//  (B2) Punch should never fire while the character is walking.
//       A fighting game character does not punch mid-stride.  If |_animMoveX|
//       exceeds WALK_PUNCH_SUPPRESS_THRESHOLD, all punch detection is skipped.
//       This is separate from punchWalkSuppression (which raises the threshold)
//       — this is a hard gate: if walking, no punches at all.
//       Threshold = 0.3 so very slow shuffling still allows punches but
//       a clear walk stride blocks them completely.
//
// ─────────────────────────────────────────────────────────────────────────────
//  FIX-C  _punchLockTimer goes negative (cosmetic but incorrect)
//
//  The timer was decremented past zero.  Clamped with Mathf.Max(0, ...).
//
// ─────────────────────────────────────────────────────────────────────────────
//  FIX-D  Player_2 srvAge climbing to 17–39s in single-player mode
//
//  This is expected and correct behaviour: Python only sends id=0 packets
//  when one person is detected.  Player_2 (id=1) never receives a packet
//  so its srvAge climbs indefinitely.  However if Player_2 AvatarController
//  has canFight=true and debugMode=true, it logs every frame — creating noise.
//  Added: suppress all per-frame debug logs when srvAge > SERVER_EXPIRE_S AND
//  _srvEverReceived is false (player was never activated this session).
//  This keeps the console clean in single-player mode without disabling debug
//  for the active player.
//
// ═══════════════════════════════════════════════════════════════════════════════

public class AvatarController : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────
    [Header("=== PLAYER IDENTITY ===")]
    public int playerID = -1;
    public bool IsPlayer1 => playerID == 0;

    [Header("=== MAP & GROUND ===")]
    public float minMapX = -8f;
    public float maxMapX = 8f;
    public float groundY = -999f;
    public float fightPlaneZ = 0f;

    [Header("=== MOVEMENT ===")]
    public float moveSpeed = 3f;
    [Range(1f, 20f)] public float animDamping = 10f;
    [Range(1f, 30f)] public float walkAccelDamping = 8f;
    [Range(0.01f, 0.3f)] public float walkDeadZone = 0.08f;
    [Range(0.05f, 0.5f)] public float reverseDirectionThreshold = 0.35f;
    // FIX-A2: reduced from 0.18 → 0.10 for snappier stop response
    [Range(0.05f, 0.6f)] public float moveIdleTimeout = 0.10f;

    [Tooltip("Flip if lean-right makes character walk backward.")]
    public bool invertMoveX = false;

    [Header("=== COMBAT ===")]
    public bool canFight = false;

    [Header("=== HITBOXES ===")]
    public Hitbox leftHandHitbox;
    public Hitbox rightHandHitbox;
    public float hitboxActiveTime = 0.45f;

    [Header("=== CALIBRATION ===")]
    public bool normalizeScale = true;
    public float targetHeight = 1.8f;
    public float poseScale = 1f;
    public Vector3 poseOffset = Vector3.zero;
    [Range(0f, 0.95f)] public float poseSmoothingFactor = 0.6f;

    [Header("=== IK ===")]
    public bool useIKTracking;
    public Transform headTarget, leftHandTarget, rightHandTarget;
    public Transform leftElbowTarget, rightElbowTarget;

    [Header("=== PUNCH DETECTION ===")]
    public float punchVelocityThreshold = 1.6f;
    [Tooltip("Auto-set to threshold × 0.55 in Start(). Do not edit.")]
    public float punchVelocityResetThreshold = 0.88f;
    public float punchCooldown = 0.35f;
    public float punchWalkSuppression = 1.2f;
    // FIX-B1: lowered from 0.08 → 0.035 to catch slow leans
    [Range(0.001f, 0.1f)] public float leanGuardThreshold = 0.035f;
    // FIX-B1: consecutive-frame lean window — blocks punch for N frames after lean
    [Range(1, 8)] public int leanGuardFrames = 4;
    // FIX-B2: hard gate — no punches while walking above this speed
    [Range(0.1f, 1.0f)] public float walkPunchSuppressThresh = 0.30f;
    [Range(0.05f, 0.5f)] public float velocitySmoothingFactor = 0.18f;
    [Range(0f, 1f)] public float punchWalkLockDuration = 0.45f;

    [Header("=== DEBUG ===")]
    public bool debugMode = false;
    public bool mirrorInput = true;
    public bool showGizmos = true;

    // ── Private — server ─────────────────────────────────────────────────────
    private const float SERVER_EXPIRE_S = 2.5f;
    // FIX-GAP: if no packet for this long, treat as zero input (→ Sad_idle)
    // This is shorter than SERVER_EXPIRE_S so the character snaps to idle quickly
    // instead of staying in the last animated state for up to 2.5s.
    private const float SERVER_IDLE_AFTER_S = 0.5f;
    private const float SERVER_WARN_S = 5f;

    private float _srvMoveX;
    private bool _hasSrvMoveX;
    private bool _srvEverReceived;
    private float _srvAge = SERVER_EXPIRE_S + 1f;
    private float _srvWarnTimer = 0f;
    private bool _srvFirstPacket = true;

    // ── Private — walk ───────────────────────────────────────────────────────
    private float _idleTimer;
    private float _animMoveX;
    private float _targetMoveX;
    private float _lockedDir;
    private float _punchLockTimer = 0f;

    // ── Private — pose / IK ──────────────────────────────────────────────────
    private Animator _anim;
    private HealthSystem _health;
    private Rigidbody _rb;
    private bool _isStunned;
    private float _groundY;
    private bool _groundYReady;
    private List<LandmarkData> _kp;
    private Vector3[] _smoothed = new Vector3[17];
    private Vector3[] _target = new Vector3[17];

    // ── Private — punch ───────────────────────────────────────────────────────
    private Vector3 _physLastL, _physLastR;
    private float _physLastT;
    private float _lastLPunch = -999f, _lastRPunch = -999f;
    private float _velL, _velR;
    private bool _lWasFast, _rWasFast;
    private bool _punchInit;

    // lean guard state
    private Vector3 _lastHipWorld = Vector3.zero;
    private bool _hipInitialized = false;
    // FIX-B1: rolling lean frame counter
    private int _leanFramesRemaining = 0;
    // FIX-LEAN: sustained lean velocity accumulator — catches slow leans that
    // never produce a single large delta but steadily shift the hip position
    private float _hipVelocityEMA = 0f;
    private const float HIP_VEL_ALPHA = 0.4f;
    private const float HIP_VEL_SUSTAINED_THRESH = 0.012f; // EMA velocity triggering guard

    // keypoint indices — YOLO 17-point
    const int Nose = 0, LShoulder = 5, RShoulder = 6,
              LElbow = 7, RElbow = 8, LWrist = 9, RWrist = 10,
              LHip = 11, RHip = 12;

    // ═════════════════════════════════════════════════════════════════════════
    void Start()
    {
        _anim = GetComponent<Animator>();
        _health = GetComponent<HealthSystem>();
        _rb = GetComponent<Rigidbody>();
        _physLastT = Time.time;

        if (_anim != null) _anim.applyRootMotion = false;

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

        _groundY = (groundY > -998f) ? groundY : transform.position.y;
        _groundYReady = true;

        var p = transform.position;
        p.y = _groundY; p.z = fightPlaneZ;
        transform.position = p;

        for (int i = 0; i < 17; i++)
            _target[i] = _smoothed[i] = transform.position;

        _anim?.SetFloat("MoveX", 0f);

        // Always enforce correct reset threshold
        punchVelocityResetThreshold = punchVelocityThreshold * 0.55f;

        Debug.Log($"<color=cyan>[{name}] AvatarController v8.0 | " +
                  $"playerID={playerID} | invertMoveX={invertMoveX} | " +
                  $"scale={transform.localScale.y:F2} | groundY={_groundY:F2}</color>");
    }

    // ── Public API ────────────────────────────────────────────────────────────
    public void SetPlayerID(int id) { playerID = id; mirrorInput = true; }
    public void ReceiveJump() { _anim?.SetTrigger("Jump"); }
    public void TriggerHitReaction() { _anim?.SetTrigger("Hit"); }
    public void SetStunned(bool s) { _isStunned = s; }

    public void ReceiveKeypoints(List<LandmarkData> kp)
    {
        if (playerID == -1 || kp == null || kp.Count < 17) return;
        _kp = kp;
        UpdateTargetLandmarks();
    }

    public void ReceiveServerMoveX(float v)
    {
        if (_srvFirstPacket)
        {
            _srvFirstPacket = false;
            Debug.Log($"<color=cyan>[{name}] First packet: move_x={v:F3} | " +
                      $"If direction wrong → enable invertMoveX in Inspector</color>");
        }
        _srvMoveX = v;
        _hasSrvMoveX = true;
        _srvEverReceived = true;
        _srvAge = 0f;
        _srvWarnTimer = 0f;
    }

    // ── Update ────────────────────────────────────────────────────────────────
    void Update()
    {
        if (!_groundYReady) return;

        // Startup diagnostic — only log for players that ever receive data
        if (!_srvEverReceived)
        {
            _srvWarnTimer += Time.deltaTime;
            if (_srvWarnTimer > SERVER_WARN_S)
            {
                // FIX-D: suppress for inactive players (Player 2 in solo mode)
                // Only warn if this player is supposed to be active
                if (canFight)
                    Debug.LogWarning($"[{name}] No packet in {_srvWarnTimer:F0}s. " +
                        "Check: PoseManager saved+compiled | pose_server running | " +
                        "port 9001 | canFight=true | playerID assigned");
                _srvWarnTimer = 0f;
            }
        }

        _srvAge += Time.deltaTime;
        if (_hasSrvMoveX && _srvAge >= SERVER_EXPIRE_S)
        {
            _hasSrvMoveX = false;
            _srvMoveX = 0f;
        }

        // FIX-C: clamp punch lock timer to zero (no negative values)
        _punchLockTimer = Mathf.Max(0f, _punchLockTimer - Time.deltaTime);

        // FIX-B1: decay lean guard frame counter
        if (_leanFramesRemaining > 0)
            _leanFramesRemaining--;

        // Smooth landmarks
        float lf = 1f - poseSmoothingFactor;
        for (int i = 0; i < 17; i++)
            _smoothed[i] = Vector3.Lerp(_smoothed[i], _target[i], lf);

        if (useIKTracking) UpdateIKTargets();

        if (_kp != null && _kp.Count >= 17 && canFight && !_isStunned)
        {
            UpdateWalk();
            DetectPunches();
        }
        else
        {
            _targetMoveX = 0f;
            _lockedDir = 0f;
            _idleTimer = 0f;
            _punchLockTimer = 0f;
            _leanFramesRemaining = 0;

            // INSTANT RESET
            _animMoveX = 0f;
            _anim?.SetFloat("MoveX", 0f);
        }

        ApplyMovementAndClamp();
    }

    void LateUpdate()
    {
        if (!_groundYReady || _isStunned) return;
        var pos = transform.position;
        if (Mathf.Abs(pos.y - _groundY) > 0.001f)
        { pos.y = _groundY; transform.position = pos; }
    }

    // ── Walk ──────────────────────────────────────────────────────────────────
    //
    //  v8.2 STOP LOGIC REWRITE
    //  ────────────────────────
    //  Previous bug: the lerp  _animMoveX = Lerp(_animMoveX, _targetMoveX, s)
    //  ran unconditionally AFTER the instant-stop block.  Even when the idle
    //  timeout fired and set _animMoveX=0, the lerp immediately re-blended it
    //  toward the (still non-zero) _targetMoveX for that one extra frame,
    //  producing a tiny non-zero MoveX → Animator re-entered walk for 1–2 frames.
    //
    //  New design — two separate concerns:
    //
    //  1. STOP CONFIRM DEBOUNCE (_stopDebounceTimer, replaces _idleTimer)
    //     Purpose: tolerate brief 1-frame zero packets mid-walk (network noise).
    //     Duration: moveIdleTimeout (default 0.10s).
    //     Behaviour: while debouncing, hold _lockedDir and _targetMoveX unchanged.
    //     When debounce expires: commit the stop — zero EVERYTHING including
    //     _animMoveX — then RETURN immediately before the lerp can run.
    //
    //  2. LERP (walk acceleration only)
    //     Only runs when there IS active input (_targetMoveX != 0).
    //     Never runs in the same frame as a committed stop.
    //     This means the lerp can never un-zero a stop decision.
    //
    void UpdateWalk()
    {
        // ── 1. Read raw server value ──────────────────────────────────────────
        float raw = (_srvEverReceived && _srvAge < SERVER_IDLE_AFTER_S) ? _srvMoveX : 0f;
        if (invertMoveX) raw = -raw;
        if (_punchLockTimer > 0f) raw = 0f;

        float inputMoveX = IsPlayer1 ? raw : -raw;
        inputMoveX = Mathf.Clamp(inputMoveX, -1f, 1f);

        if (Mathf.Abs(inputMoveX) < walkDeadZone)
            inputMoveX = 0f;
        else
            inputMoveX = Mathf.Sign(inputMoveX);

        // Direction hysteresis — suppress weak opposite signal
        if (_lockedDir != 0f && inputMoveX != 0f &&
            Mathf.Sign(inputMoveX) != _lockedDir &&
            Mathf.Abs(raw) < reverseDirectionThreshold)
        {
            inputMoveX = _lockedDir;
        }

        bool hasInput = (inputMoveX != 0f);

        // ── 2. State machine ──────────────────────────────────────────────────
        if (hasInput)
        {
            // Active input — reset debounce, update direction lock & target
            _idleTimer = 0f;
            float dir = Mathf.Sign(inputMoveX);

            if (_lockedDir == 0f || dir == _lockedDir)
            { _lockedDir = dir; _targetMoveX = dir; }
            else if (Mathf.Abs(raw) > reverseDirectionThreshold)
            { _lockedDir = dir; _targetMoveX = dir; }

            // ── 3a. ACCELERATION lerp (only when actively walking) ───────────
            float s = 1f - Mathf.Exp(-walkAccelDamping * Time.deltaTime);
            _animMoveX = Mathf.Lerp(_animMoveX, _targetMoveX, s);
            if (Mathf.Abs(_animMoveX) < 0.05f) _animMoveX = 0f;
            _anim?.SetFloat("MoveX", _animMoveX);
        }
        else
        {
            // No input — run stop-confirm debounce
            _idleTimer += Time.deltaTime;

            if (_idleTimer >= moveIdleTimeout)
            {
                // ── 3b. COMMITTED STOP — zero everything, skip lerp entirely ──
                _targetMoveX = 0f;
                _lockedDir = 0f;
                _animMoveX = 0f;
                _anim?.SetFloat("MoveX", 0f);

                if (debugMode && _srvEverReceived)
                    Debug.Log($"<color=lime>[{name}] MoveX=0.00 raw={raw:F2} " +
                              $"srvAge={_srvAge:F2} locked=0 [STOP]</color>");
                return;
            }
            else
            {
                // ── 3c. DEBOUNCE WINDOW — drain toward zero smoothly ──────────
                // 🔥 FIXED: drain to 0, not to _targetMoveX
                float s = 1f - Mathf.Exp(-walkAccelDamping * Time.deltaTime);
                _animMoveX = Mathf.Lerp(_animMoveX, 0f, s);  // ← THIS IS THE FIX
                if (Mathf.Abs(_animMoveX) < 0.05f) _animMoveX = 0f;
                _anim?.SetFloat("MoveX", _animMoveX);
            }
        }

        if (debugMode && _srvEverReceived)
            Debug.Log($"<color=lime>[{name}] MoveX={_animMoveX:F2} raw={raw:F2} " +
                      $"srvAge={_srvAge:F2} locked={_lockedDir} " +
                      $"punchLock={_punchLockTimer:F2} leanFr={_leanFramesRemaining}</color>");
    }

    void ApplyMovementAndClamp()
    {
        if (!_groundYReady) return;
        var t = transform.position;

        float worldDir = IsPlayer1 ? _animMoveX : -_animMoveX;
        if (Mathf.Abs(_animMoveX) > 0.01f)
            t.x += worldDir * moveSpeed * Time.deltaTime;

        t.x = Mathf.Clamp(t.x, minMapX, maxMapX);
        t.z = fightPlaneZ;
        t.y = _groundY;

        if (_rb != null) _rb.MovePosition(t);
        else transform.position = t;
    }

    // ── Punch Detection ───────────────────────────────────────────────────────
    void DetectPunches()
    {
        float now = Time.time;
        float dt = now - _physLastT;
        if (dt <= 0.01f) return;

        if (_kp == null || _kp.Count < 17) { _physLastT = now; return; }

        // FIX-B2: hard gate — no punches while clearly walking
        if (Mathf.Abs(_animMoveX) >= walkPunchSuppressThresh)
        {
            // Still advance timestamps so we don't get stale deltas
            _physLastL = PhysPos(_kp, 9) - PhysPos(_kp, 5);
            _physLastR = PhysPos(_kp, 10) - PhysPos(_kp, 6);
            _velL = 0f;
            _velR = 0f;
            _physLastT = now;
            return;
        }

        // Physical hand positions — pre-mirror, shoulder-relative
        Vector3 curPhysL = PhysPos(_kp, 9) - PhysPos(_kp, 5);
        Vector3 curPhysR = PhysPos(_kp, 10) - PhysPos(_kp, 6);

        // ── Lean guard ────────────────────────────────────────────────────────
        // FIX-LEAN: Use SMOOTHED hip position (_smoothed array) instead of raw
        // keypoints. Raw kp jitter was causing false guard triggers during normal
        // arm movement, AND failing to catch slow leans because per-frame raw
        // deltas are too small. Smoothed landmarks are stable enough that even
        // a slow lean produces a consistent non-zero per-frame delta.
        Vector3 hipNow = (_smoothed[LHip] + _smoothed[RHip]) * 0.5f;

        if (_hipInitialized)
        {
            float hipDelta = Vector3.Distance(hipNow, _lastHipWorld);

            // EMA of per-frame hip velocity (sustained lean detection)
            _hipVelocityEMA = HIP_VEL_ALPHA * hipDelta + (1f - HIP_VEL_ALPHA) * _hipVelocityEMA;

            bool instantLean = hipDelta > leanGuardThreshold;
            bool sustainedLean = _hipVelocityEMA > HIP_VEL_SUSTAINED_THRESH;

            // FIX-B1: lower threshold catches slow leans
            if (instantLean || sustainedLean)
            {
                if (debugMode)
                    Debug.Log($"<color=yellow>[{name}] Lean hipDelta={hipDelta:F4} " +
                              $"velEMA={_hipVelocityEMA:F4} " +
                              $"→ guard {leanGuardFrames}fr</color>");

                // Set rolling window — block punches for N frames
                _leanFramesRemaining = leanGuardFrames;

                // Clear EMA history so no residual bleeds into the next frame
                _velL = 0f;
                _velR = 0f;
                _lWasFast = false;
                _rWasFast = false;
                _physLastL = curPhysL;
                _physLastR = curPhysR;
                _physLastT = now;
                _lastHipWorld = hipNow;
                _hipInitialized = true;
                return;
            }
        }

        _lastHipWorld = hipNow;
        _hipInitialized = true;

        // FIX-B1: sustained lean window — if lean guard fired recently, skip
        if (_leanFramesRemaining > 0)
        {
            _physLastL = curPhysL;
            _physLastR = curPhysR;
            _velL = Mathf.Lerp(_velL, 0f, 0.3f); // gentle decay during guard
            _velR = Mathf.Lerp(_velR, 0f, 0.3f);
            _physLastT = now;
            return;
        }

        // Decay hip velocity EMA when not in a lean (body is still)
        _hipVelocityEMA = Mathf.Lerp(_hipVelocityEMA, 0f, 0.2f);

        if (!_punchInit)
        {
            _physLastL = curPhysL; _physLastR = curPhysR;
            _punchInit = true; _physLastT = now;
            return;
        }

        Vector3 dL = curPhysL - _physLastL;
        Vector3 dR = curPhysR - _physLastR;
        float vL = dL.magnitude / dt;
        float vR = dR.magnitude / dt;

        float smL = Mathf.Lerp(_velL, vL, velocitySmoothingFactor);
        float smR = Mathf.Lerp(_velR, vR, velocitySmoothingFactor);

        // Dynamic threshold — rises with walk speed
        float thr = punchVelocityThreshold + Mathf.Abs(_animMoveX) * punchWalkSuppression;

        // Both hands moving same horizontal direction = body lean
        bool bothSideways = vL > 0.8f && vR > 0.8f
                         && Mathf.Sign(dL.x) == Mathf.Sign(dR.x)
                         && Mathf.Abs(dL.x) > Mathf.Abs(dL.y);

        if (!bothSideways)
        {
            if (smL > thr && (now - _lastLPunch) > punchCooldown && !_lWasFast)
            {
                if (debugMode)
                    Debug.Log($"<color=orange>[{name}] L-PUNCH vel={smL:F2} thr={thr:F2}</color>");
                FirePunch("Left");
                _lastLPunch = now;
                _lWasFast = true;
                _punchLockTimer = punchWalkLockDuration;
            }
            if (smR > thr && (now - _lastRPunch) > punchCooldown && !_rWasFast)
            {
                if (debugMode)
                    Debug.Log($"<color=orange>[{name}] R-PUNCH vel={smR:F2} thr={thr:F2}</color>");
                FirePunch("Right");
                _lastRPunch = now;
                _rWasFast = true;
                _punchLockTimer = punchWalkLockDuration;
            }
        }

        if (smL < punchVelocityResetThreshold) _lWasFast = false;
        if (smR < punchVelocityResetThreshold) _rWasFast = false;

        _physLastL = curPhysL; _physLastR = curPhysR;
        _velL = smL; _velR = smR;
        _physLastT = now;
    }

    Vector3 PhysPos(List<LandmarkData> kp, int idx)
    {
        if (idx >= kp.Count) return Vector3.zero;
        return new Vector3(kp[idx].x, kp[idx].y, 0f);
    }

    void FirePunch(string hand)
    {
        if (_health != null && _health.IsKnockedOut()) return;
        if (_anim == null) return;
        if (hand == "Left" && leftHandHitbox != null) StartCoroutine(ArmHitbox(leftHandHitbox));
        if (hand == "Right" && rightHandHitbox != null) StartCoroutine(ArmHitbox(rightHandHitbox));
        _anim.SetTrigger(hand == "Right" ? "PunchRight" : "PunchLeft");
    }

    IEnumerator ArmHitbox(Hitbox hb)
    {
        hb.EnableHitbox();
        yield return new WaitForSeconds(hitboxActiveTime);
        var col = hb?.GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    // ── Landmark helpers ──────────────────────────────────────────────────────
    void UpdateTargetLandmarks()
    {
        var bp = transform.position;
        var br = transform.rotation;
        for (int i = 0; i < 17; i++)
        {
            int s = mirrorInput ? Mirror(i) : i;
            if (s >= _kp.Count) continue;
            _target[i] = bp + br * ((new Vector3(_kp[s].x - 0.5f, 0.5f - _kp[s].y, 0)
                                     + poseOffset) * poseScale);
        }
    }

    int Mirror(int i)
    {
        int[] m = { 0, 2, 1, 4, 3, 6, 5, 8, 7, 10, 9, 12, 11, 14, 13, 16, 15 };
        return i < m.Length ? m[i] : i;
    }

    Vector3 Pos(int i) => (_target != null && i < _target.Length) ? _target[i] : Vector3.zero;
    Vector3 SPos(int i) => (_smoothed != null && i < _smoothed.Length) ? _smoothed[i] : Vector3.zero;

    // ── IK ────────────────────────────────────────────────────────────────────
    void UpdateIKTargets()
    {
        if (headTarget) headTarget.position = SPos(Nose);
        if (leftHandTarget) leftHandTarget.position = SPos(LWrist);
        if (rightHandTarget) rightHandTarget.position = SPos(RWrist);
        if (leftElbowTarget) leftElbowTarget.position = SPos(LElbow);
        if (rightElbowTarget) rightElbowTarget.position = SPos(RElbow);
    }

    void OnAnimatorIK(int _)
    {
        if (!useIKTracking || _kp == null) return;
        IKGoal(AvatarIKGoal.LeftHand, leftHandTarget, AvatarIKHint.LeftElbow, leftElbowTarget);
        IKGoal(AvatarIKGoal.RightHand, rightHandTarget, AvatarIKHint.RightElbow, rightElbowTarget);
        if (headTarget)
        { _anim.SetLookAtWeight(1); _anim.SetLookAtPosition(headTarget.position); }
    }

    void IKGoal(AvatarIKGoal g, Transform t, AvatarIKHint h, Transform ht)
    {
        if (t) { _anim.SetIKPositionWeight(g, 1); _anim.SetIKPosition(g, t.position); }
        if (ht) { _anim.SetIKHintPositionWeight(h, 1); _anim.SetIKHintPosition(h, ht.position); }
    }

    void OnDrawGizmos()
    {
        if (!showGizmos || _smoothed == null) return;
        Gizmos.color = Color.cyan;
        foreach (var p in _smoothed) Gizmos.DrawSphere(p, 0.015f);
    }

    public float GetLeftHandVelocity() => _velL;
    public float GetRightHandVelocity() => _velR;
}

/*
 ═══════════════════════════════════════════════════════════════════════════════
  ANIMATOR SETUP  (no changes from v7)
 ═══════════════════════════════════════════════════════════════════════════════

  PARAMETERS:
    Float   → MoveX
    Trigger → PunchLeft / PunchRight / Hit / Knockout / Jump

  TRANSITIONS:
  1. Sad Idle → Walking             MoveX > 0.05              ExitTime OFF  Duration 0.10
  2. Sad Idle → Walking Backwards   MoveX < -0.05             ExitTime OFF  Duration 0.10
  3. Walking → Sad Idle             MoveX > -0.05 AND < 0.05  ExitTime OFF  Duration 0.10
  4. Walking Backwards → Sad Idle   MoveX > -0.05 AND < 0.05  ExitTime OFF  Duration 0.10
  5. AnyState → PunchRight   Trigger PunchRight  ExitTime OFF  Duration 0.05
                                Interruption Source: Current State  Can Transition To Self: OFF
  6. AnyState → PunchLeft    Trigger PunchLeft   ExitTime OFF  Duration 0.05
                                Interruption Source: Current State  Can Transition To Self: OFF
  7. PunchRight → Sad Idle   Exit Time ON  ExitTime 0.80  Duration 0.10
  8. PunchLeft  → Sad Idle   Exit Time ON  ExitTime 0.80  Duration 0.10

  ALL CLIPS:
    Root Transform Rotation    Bake Into Pose ✅  Based On: Original
    Root Transform Position Y  Bake Into Pose ✅  Based On: Feet
    Root Transform Position XZ Bake Into Pose ✅  Based On: Original

  LeftPunch + RightPunch: Loop Time OFF, Loop Pose OFF
  Prefab: Apply Root Motion OFF, pivot at feet

 ═══════════════════════════════════════════════════════════════════════════════
*/