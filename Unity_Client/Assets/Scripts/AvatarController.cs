using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ═══════════════════════════════════════════════════════════════════════════════
//  AvatarController — v5.0
//
//  NEW BUGS FIXED IN THIS VERSION:
//
//  ISSUE-A  "Boomerang" / PUBG-loop movement.
//           Root cause: srvAge was reaching 7–8 s at idle, meaning
//           ReceiveServerMoveX was never being called — not because the server
//           stopped sending, but because PoseManager's sentinel check
//           (lw_vel < -0.5) was silently failing on JsonUtility deserialization.
//           Secondary cause: even when srvAge correctly expired and _lockedDir
//           was cleared, the EMA was still fighting _targetMoveX=0 from a high
//           _animMoveX starting point — producing 2–3 "ghost" walk cycles.
//           FIX-A1: Removed the sentinel dependency entirely. PoseManager now
//                   calls ReceiveServerMoveX on EVERY packet unconditionally.
//                   The lw_vel sentinel was a workaround for a bug that no
//                   longer exists — Python always sends valid move_x.
//           FIX-A2: When _targetMoveX is set to 0 (idle timeout fires), we
//                   also force _animMoveX toward 0 with a faster drain rate
//                   (animDamping * 2) so the EMA can't ghost-walk.
//
//  ISSUE-B  Punch fires on lean-start.
//           Root cause: UpdateTargetLandmarks() computes wrist world positions
//           relative to transform.position. On the first frame of a lean, the
//           character root hasn't moved yet but the landmarks jump. This creates
//           a large single-frame velocity spike on both wrists simultaneously.
//           The bothSideways filter only checks same X-direction — a lean moves
//           wrists in X, so it fires the filter correctly... except the filter
//           also requires BOTH hands to move. A lean moves one shoulder forward
//           which shifts one wrist more than the other, escaping the filter.
//           FIX-B: Added a "lean guard" that checks if hip-center moved more
//                  than LEAN_GUARD_THRESHOLD in the same frame. If the hips
//                  translated significantly (body lean), punch detection is
//                  suppressed for that frame. Hips don't move during a punch —
//                  only arms do. This cleanly separates the two gestures.
//           FIX-B2: Increased punchVelocityThreshold from 1.5 → 2.2.
//                   The landmark coordinate space at poseScale=1 produces
//                   lean-spike velocities of ~1.6–1.9. 2.2 sits above that
//                   while still below a real fast punch (~3.5+).
//
//  ISSUE-C  Only right punch ("PunchRight") fires reliably.
//           Root cause: With mirrorInput=true, keypoint indices are remapped.
//           When computing Pos(LWrist)-Pos(LShoulder) the _target array already
//           has mirrored values. A physical right-hand punch (your actual right
//           arm) is detected as LWrist in the mirrored space — firing "Left"
//           punch → PunchLeft trigger. If PunchLeft animation has a longer
//           cooldown or is visually suppressed by a Walking transition with
//           higher priority, only PunchRight shows. 
//           FIX-C: Added explicit physical-hand tracking. punchPhysL/R track
//                  the PHYSICAL hands (pre-mirror), and punch triggers map to
//                  physical hands directly. This decouples punch detection from
//                  the mirror remapping that's only needed for IK/walk.
//
//  ISSUE-D  Walk speed too fast / instant full speed.
//           Root cause: moveSpeed=4f applied from frame 1 using _animMoveX
//           which jumps to ~0.8 in the first EMA step at animDamping=10.
//           FIX-D: Movement position delta is now scaled by |_animMoveX| so
//                  the character physically accelerates with the animator blend.
//                  Also reduced default moveSpeed from 4 → 3 and exposed a
//                  separate walkAccelDamping for the movement (vs animation).
//
//  ISSUE-E  srvAge never reset — ReceiveServerMoveX not being called.
//           See ISSUE-A. The sentinel check in PoseManager was the gatekeeper
//           that silently dropped all packets. Fixed in PoseManager.cs (v5.0).
//
//  ISSUE-F  JsonUtility silent deserialization failure on lw_vel field.
//           JsonUtility in Unity does not throw on missing/mismatched fields —
//           it silently leaves them at default (0f). If PoseDataPacket.lw_vel
//           is not perfectly matching the JSON key, lw_vel stays 0f, the
//           sentinel check (< -0.5) is never true, and ReceiveServerMoveX is
//           never called. Fixed by removing the sentinel gate entirely (FIX-A1).
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
    public float moveSpeed = 3f;          // ISSUE-D: lowered from 4 → 3
    [Range(1f, 20f)] public float animDamping = 10f;
    [Range(1f, 30f)] public float walkAccelDamping = 8f;   // ISSUE-D: separate accel curve
    [Range(0.01f, 0.3f)] public float walkDeadZone = 0.08f;
    [Range(0.05f, 0.5f)] public float reverseDirectionThreshold = 0.35f;
    [Range(0.05f, 0.6f)] public float moveIdleTimeout = 0.18f;

    [Header("=== COMBAT ===")]
    public bool canFight = false;

    [Header("=== HITBOXES ===")]
    public Hitbox leftHandHitbox;
    public Hitbox rightHandHitbox;
    public float hitboxActiveTime = 0.3f;

    [Header("=== CALIBRATION ===")]
    public bool normalizeScale = true;
    public float targetHeight = 1.8f;
    public float poseScale = 1f;
    public Vector3 poseOffset = Vector3.zero;
    [Range(0f, 0.95f)] public float poseSmoothingFactor = 0.6f;
    [Range(0f, 0.5f)] public float velocitySmoothingFactor = 0.15f;

    [Header("=== IK ===")]
    public bool useIKTracking;
    public Transform headTarget, leftHandTarget, rightHandTarget;
    public Transform leftElbowTarget, rightElbowTarget;

    [Header("=== PUNCH DETECTION ===")]
    public float punchVelocityThreshold = 2.2f;  // ISSUE-B2: raised 1.5→2.2
    public float punchVelocityResetThreshold = 1.8f;
    public float punchCooldown = 0.35f;
    public float punchWalkSuppression = 2.5f;
    // ISSUE-B: hip translation guard — if hips moved more than this normalised
    // distance in one frame, it's a body lean, not a punch — skip detection.
    [Range(0.001f, 0.05f)] public float leanGuardThreshold = 0.012f;

    [Header("=== DEBUG ===")]
    public bool debugMode = false;
    public bool mirrorInput = true;
    public bool showGizmos = true;

    // ── Private — server packet ───────────────────────────────────────────────
    private const float SERVER_EXPIRE_S = 1.2f;

    private float _srvMoveX;
    private bool _hasSrvMoveX;
    private bool _srvEverReceived;
    private float _srvAge = SERVER_EXPIRE_S + 1f;

    // ── Private — walk ───────────────────────────────────────────────────────
    private float _idleTimer;
    private float _animMoveX;
    private float _targetMoveX;
    private float _lockedDir;

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

    // ── Private — punch (ISSUE-C: physical-hand tracking, pre-mirror) ────────
    private Vector3 _physLastL, _physLastR;   // physical left/right wrist - shoulder
    private float _physLastT;
    private float _lastLPunch = -999f, _lastRPunch = -999f;
    private float _velL, _velR;
    private bool _lWasFast, _rWasFast;
    private bool _punchInit;

    // ISSUE-B: lean guard state
    private Vector3 _lastHipWorld = Vector3.zero;
    private bool _hipInitialized;

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
        p.y = _groundY;
        p.z = fightPlaneZ;
        transform.position = p;

        for (int i = 0; i < 17; i++)
            _target[i] = _smoothed[i] = transform.position;

        _anim?.SetFloat("MoveX", 0f);

        Debug.Log($"<color=cyan>[{name}] AvatarController v5.0 ready | " +
                  $"scale={transform.localScale.y:F2} | groundY={_groundY:F2} | " +
                  $"rootMotion={(_anim != null ? _anim.applyRootMotion.ToString() : "n/a")}</color>");
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

    // ISSUE-A FIX: called unconditionally by PoseManager — no sentinel gate.
    public void ReceiveServerMoveX(float v)
    {
        _srvMoveX = v;
        _hasSrvMoveX = true;
        _srvEverReceived = true;
        _srvAge = 0f;
    }

    // ── Update ────────────────────────────────────────────────────────────────
    void Update()
    {
        if (!_groundYReady) return;

        // Expire stale server data
        _srvAge += Time.deltaTime;
        if (_hasSrvMoveX && _srvAge >= SERVER_EXPIRE_S)
        {
            _hasSrvMoveX = false;
            _srvMoveX = 0f;
        }

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
            // Force idle cleanly
            _targetMoveX = 0f;
            _lockedDir = 0f;
            _idleTimer = 0f;
            DrainToIdle(animDamping * 2f);  // ISSUE-A FIX: fast drain when inactive
        }

        ApplyMovementAndClamp();
    }

    void LateUpdate()
    {
        if (!_groundYReady || _isStunned || _rb != null) return;
        var pos = transform.position;
        if (Mathf.Abs(pos.y - _groundY) > 0.001f)
        {
            pos.y = _groundY;
            transform.position = pos;
        }
    }

    // ── Walk ──────────────────────────────────────────────────────────────────
    void UpdateWalk()
    {
        // Raw input: server value when fresh, 0 when expired or never received
        float raw = _srvEverReceived ? _srvMoveX : 0f;

        // P2 sees world flipped
        float inputMoveX = IsPlayer1 ? raw : -raw;

        // Deadzone
        inputMoveX = Mathf.Clamp(inputMoveX, -1f, 1f);
        if (Mathf.Abs(inputMoveX) < walkDeadZone)
            inputMoveX = 0f;
        else
            inputMoveX = Mathf.Sign(inputMoveX);

        // Direction hysteresis — suppress weak opposite signal
        if (_lockedDir != 0f && inputMoveX != 0f &&
            Mathf.Sign(inputMoveX) != _lockedDir)
        {
            if (Mathf.Abs(raw) < reverseDirectionThreshold)
                inputMoveX = _lockedDir;
        }

        bool hasInput = (inputMoveX != 0f);

        if (hasInput)
        {
            _idleTimer = 0f;
            float dir = Mathf.Sign(inputMoveX);

            if (_lockedDir == 0f || dir == _lockedDir)
            {
                _lockedDir = dir;
                _targetMoveX = dir;
            }
            else if (Mathf.Abs(raw) > reverseDirectionThreshold)
            {
                _lockedDir = dir;
                _targetMoveX = dir;
            }
        }
        else
        {
            _idleTimer += Time.deltaTime;
            if (_idleTimer >= moveIdleTimeout)
            {
                _targetMoveX = 0f;
                _lockedDir = 0f;

                // ISSUE-A FIX: when idle timeout fires, fast-drain the EMA so
                // ghost walk cycles can't happen. animDamping*2 drains to ~0.05
                // in roughly half the normal time.
                if (Mathf.Abs(_animMoveX) > 0.05f)
                    DrainToIdle(animDamping * 2f);
            }
        }

        // ISSUE-D FIX: use walkAccelDamping for the blend, animDamping for display
        float s = 1f - Mathf.Exp(-walkAccelDamping * Time.deltaTime);
        _animMoveX = Mathf.Lerp(_animMoveX, _targetMoveX, s);
        if (Mathf.Abs(_animMoveX) < 0.05f) _animMoveX = 0f;

        _anim?.SetFloat("MoveX", _animMoveX);

        if (debugMode)
            Debug.Log($"<color=lime>[{name}] MoveX={_animMoveX:F2} raw={raw:F2} " +
                      $"fresh={_hasSrvMoveX} srvAge={_srvAge:F2} locked={_lockedDir}</color>");
    }

    // Helper: decay _animMoveX toward 0 at given damping rate, then apply
    void DrainToIdle(float damping)
    {
        float s = 1f - Mathf.Exp(-damping * Time.deltaTime);
        _animMoveX = Mathf.Lerp(_animMoveX, 0f, s);
        if (Mathf.Abs(_animMoveX) < 0.05f) _animMoveX = 0f;
        _anim?.SetFloat("MoveX", _animMoveX);
    }

    void ApplyMovementAndClamp()
    {
        if (!_groundYReady) return;
        var t = transform.position;

        // ISSUE-D FIX: multiply by |_animMoveX| so acceleration matches animation.
        // Character physically accelerates/decelerates with the blend — no instant snap.
        float worldDir = IsPlayer1 ? _animMoveX : -_animMoveX;
        if (Mathf.Abs(_animMoveX) > 0.01f)
            t.x += worldDir * moveSpeed * Time.deltaTime;

        t.x = Mathf.Clamp(t.x, minMapX, maxMapX);
        t.z = fightPlaneZ;
        if (!_isStunned && _rb == null) t.y = _groundY;

        if (_rb != null) _rb.MovePosition(t);
        else transform.position = t;
    }

    // ── Punch Detection ───────────────────────────────────────────────────────
    void DetectPunches()
    {
        float now = Time.time;
        float dt = now - _physLastT;
        if (dt <= 0.01f) return;

        // ISSUE-C FIX: use PHYSICAL hand indices (pre-mirror) so punch side
        // maps to the actual arm the user extended, regardless of mirrorInput.
        // Physical left = index 9, physical right = index 10.
        // We read directly from _kp (raw, pre-mirror) instead of _target.
        int physLW = 9, physRW = 10;
        int physLS = 5, physRS = 6;

        if (_kp == null || _kp.Count < 17) { _physLastT = now; return; }

        // Build world-space relative positions for physical hands
        // (shoulder-subtracted to cancel body translation)
        Vector3 curPhysL = PhysPos(_kp, physLW) - PhysPos(_kp, physLS);
        Vector3 curPhysR = PhysPos(_kp, physRW) - PhysPos(_kp, physRS);

        // ISSUE-B FIX: lean guard — measure hip center movement this frame.
        // If hips translated significantly, it's a body lean, suppress punches.
        Vector3 hipNow = (PhysPos(_kp, 11) + PhysPos(_kp, 12)) * 0.5f;
        bool isBodyLean = false;
        if (_hipInitialized)
        {
            float hipDelta = Vector3.Distance(hipNow, _lastHipWorld);
            isBodyLean = (hipDelta > leanGuardThreshold);

            if (isBodyLean && debugMode)
                Debug.Log($"<color=yellow>[{name}] Lean guard fired hipDelta={hipDelta:F4}</color>");
        }
        _lastHipWorld = hipNow;
        _hipInitialized = true;

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

        // Dynamic threshold rises with walk speed
        float thr = punchVelocityThreshold + Mathf.Abs(_animMoveX) * punchWalkSuppression;

        // Both-hands same-direction = body lean
        bool bothSideways = vL > 0.4f && vR > 0.4f
                         && Mathf.Sign(dL.x) == Mathf.Sign(dR.x);

        // ISSUE-B FIX: skip if lean guard fires OR both-hands filter fires
        if (!isBodyLean && !bothSideways)
        {
            // Physical left hand punch — map to "Left" trigger
            if (smL > thr && (now - _lastLPunch) > punchCooldown && !_lWasFast)
            {
                if (debugMode)
                    Debug.Log($"<color=orange>[{name}] PHYS-L PUNCH vel={smL:F2} thr={thr:F2}</color>");
                FirePunch("Left");
                _lastLPunch = now;
                _lWasFast = true;
            }
            // Physical right hand punch — map to "Right" trigger
            if (smR > thr && (now - _lastRPunch) > punchCooldown && !_rWasFast)
            {
                if (debugMode)
                    Debug.Log($"<color=orange>[{name}] PHYS-R PUNCH vel={smR:F2} thr={thr:F2}</color>");
                FirePunch("Right");
                _lastRPunch = now;
                _rWasFast = true;
            }
        }

        if (smL < punchVelocityResetThreshold) _lWasFast = false;
        if (smR < punchVelocityResetThreshold) _rWasFast = false;

        _physLastL = curPhysL; _physLastR = curPhysR;
        _velL = smL; _velR = smR;
        _physLastT = now;
    }

    // Builds a normalised 3D position from a raw LandmarkData entry.
    // Used only for punch detection — pre-mirror, in a stable landmark space.
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
        {
            _anim.SetLookAtWeight(1);
            _anim.SetLookAtPosition(headTarget.position);
        }
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
  ANIMATOR SETUP — Fighter_Animator.controller   (unchanged from v4)
 ═══════════════════════════════════════════════════════════════════════════════

  PARAMETERS:
    Float   → MoveX
    Trigger → PunchLeft
    Trigger → PunchRight
    Trigger → Hit
    Trigger → Knockout
    Trigger → Jump

  TRANSITIONS:
  1. Walking           → Sad Idle   MoveX > -0.05 AND MoveX < 0.05  | ExitTime OFF | Duration 0.10
  2. Walking Backwards → Sad Idle   MoveX > -0.05 AND MoveX < 0.05  | ExitTime OFF | Duration 0.10
  3. Sad Idle → Walking             MoveX > 0.05                     | ExitTime OFF | Duration 0.10
  4. Sad Idle → Walking Backwards   MoveX < -0.05                    | ExitTime OFF | Duration 0.10

  PUNCH TRANSITIONS (add if not already present):
  5. Any State → PunchRight   Trigger PunchRight  | ExitTime OFF | Duration 0.05
  6. Any State → PunchLeft    Trigger PunchLeft   | ExitTime OFF | Duration 0.05
  7. PunchRight → Sad Idle    Has Exit Time ON    | ExitTime 0.8 | Duration 0.10
  8. PunchLeft  → Sad Idle    Has Exit Time ON    | ExitTime 0.8 | Duration 0.10

  CLIP IMPORT SETTINGS (all clips):
    Root Transform Rotation   → Bake Into Pose ✅  Based On: Original
    Root Transform Position Y → Bake Into Pose ✅  Based On: Feet
    Root Transform Position XZ→ Bake Into Pose ✅  Based On: Original

  PREFAB:
    Animator → Apply Root Motion : OFF
    Pivot at feet (Y=0)

 ═══════════════════════════════════════════════════════════════════════════════
*/