using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AvatarController : MonoBehaviour
{
    [Header("=== PLAYER IDENTITY ===")]
    public int playerID = -1; // Default to -1 (Unassigned)
    public bool IsPlayer1 => playerID == 0;

    [Header("=== MAP & GROUND ===")]
    public float minMapX = -8.0f;
    public float maxMapX = 8.0f;
    public float groundY = 0.0f; // Force feet to this height

    [Header("=== MOVEMENT SENSITIVITY ===")]
    public float depthMovementSpeed = 2.0f;
    public float leanMovementSpeed = 2.0f;
    [Range(0.05f, 0.4f)] public float depthThreshold = 0.15f;
    [Range(0.02f, 0.2f)] public float leanThreshold = 0.08f;
    public float maxLean = 0.25f;

    [Header("=== COMBAT STATE ===")]
    public bool canFight = false;

    [Header("=== HITBOXES ===")]
    public Hitbox leftHandHitbox;
    public Hitbox rightHandHitbox;
    public float hitboxActiveTime = 0.3f;

    [Header("=== CALIBRATION ===")]
    public bool normalizeScale = true; // UNCHECK THIS if you want characters to have different heights!
    public float targetHeight = 1.8f;  // Only used if normalizeScale is true
    public float poseScale = 1.0f;
    public Vector3 poseOffset = Vector3.zero;
    [Range(0.0f, 0.95f)] public float poseSmoothingFactor = 0.6f;
    [Range(0.0f, 0.5f)] public float velocitySmoothingFactor = 0.2f;

    [Header("=== IK TRACKING ===")]
    public bool useIKTracking = false;
    public Transform headTarget;
    public Transform leftHandTarget;
    public Transform rightHandTarget;
    public Transform leftElbowTarget;
    public Transform rightElbowTarget;

    [Header("=== PUNCH DETECTION ===")]
    public float punchVelocityThreshold = 1.2f;
    public float punchVelocityResetThreshold = 0.5f;
    public float punchCooldown = 0.6f;
    public float maxUpwardMovement = 0.25f;
    public float minVisibilityThreshold = 0.25f;
    public float autoResetTimeout = 1.5f;

    [Header("=== DEBUG ===")]
    public bool debugMode = false;
    public bool mirrorInput = true;
    public bool showGizmos = true;

    // Private State
    private Animator animator;
    private HealthSystem healthSystem;
    private List<LandmarkData> keypoints;
    private float initialShoulderWidth = -1f;
    private float lastHipCenterX = 0f;

    // Landmarks
    private Vector3[] smoothedWorldLandmarks = new Vector3[17];
    private Vector3[] targetWorldLandmarks = new Vector3[17];

    // Punch State
    private Vector3 lastLeftHandPos;
    private Vector3 lastRightHandPos;
    private float lastUpdateTime;
    private float lastLeftPunchTime = -999f;
    private float lastRightPunchTime = -999f;
    private float lastLeftHandVelocity = 0f;
    private float lastRightHandVelocity = 0f;
    private bool leftHandWasFast = false;
    private bool rightHandWasFast = false;
    private float leftMotionStartTime = 0f;
    private float rightMotionStartTime = 0f;

    // Keypoint Map
    private const int Nose = 0;
    private const int LeftShoulder = 5; private const int RightShoulder = 6;
    private const int LeftElbow = 7; private const int RightElbow = 8;
    private const int LeftWrist = 9; private const int RightWrist = 10;
    private const int LeftHip = 11; private const int RightHip = 12;

    void Start()
    {
        animator = GetComponent<Animator>();
        healthSystem = GetComponent<HealthSystem>();
        lastUpdateTime = Time.time;

        for (int i = 0; i < 17; i++)
        {
            targetWorldLandmarks[i] = transform.position;
            smoothedWorldLandmarks[i] = transform.position;
        }

        // === FIXED SCALING LOGIC ===
        if (normalizeScale)
        {
            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            if (capsule != null && capsule.height > 0.1f)
            {
                float currentHeight = capsule.height * transform.localScale.y;
                if (currentHeight > 0.1f)
                {
                    float scaleFactor = targetHeight / currentHeight;
                    transform.localScale = transform.localScale * scaleFactor;
                }
            }
        }

        lastLeftHandPos = targetWorldLandmarks[LeftWrist];
        lastRightHandPos = targetWorldLandmarks[RightWrist];
    }

    public void SetPlayerID(int id)
    {
        playerID = id;
        mirrorInput = true;
    }

    public void ReceiveKeypoints(List<LandmarkData> keypoints)
    {
        if (playerID == -1) return;
        if (keypoints == null || keypoints.Count < 17) return;
        this.keypoints = keypoints;
        UpdateTargetLandmarks();
    }

    void Update()
    {
        // === 1. FORCE POSITION TO GROUND (FIXES FLOATING) ===
        Vector3 currentPos = transform.position;
        currentPos.z = 0;       // Lock 2D plane
        currentPos.y = groundY; // Lock to Ground

        // Clamp X (Invisible Walls)
        currentPos.x = Mathf.Clamp(currentPos.x, minMapX, maxMapX);
        transform.position = currentPos;

        // 2. Smoothing
        float lerpFactor = 1.0f - poseSmoothingFactor;
        for (int i = 0; i < 17; i++)
            smoothedWorldLandmarks[i] = Vector3.Lerp(smoothedWorldLandmarks[i], targetWorldLandmarks[i], lerpFactor);

        if (useIKTracking) UpdateIKTargets();

        if (keypoints != null && keypoints.Count >= 17 && canFight)
        {
            DetectDepthMovement();
            DetectLeanMovement();
            DetectPunches();
        }
    }

    private void DetectDepthMovement()
    {
        int lShoulderIdx = mirrorInput ? RightShoulder : LeftShoulder;
        int rShoulderIdx = mirrorInput ? LeftShoulder : RightShoulder;

        if (lShoulderIdx >= keypoints.Count || rShoulderIdx >= keypoints.Count) return;

        float currentWidth = Mathf.Abs(keypoints[lShoulderIdx].x - keypoints[rShoulderIdx].x);

        if (initialShoulderWidth < 0)
        {
            initialShoulderWidth = currentWidth;
            return;
        }

        float ratio = currentWidth / initialShoulderWidth;
        float moveDir = 0f;

        if (ratio > (1.0f + depthThreshold)) moveDir = IsPlayer1 ? 1f : -1f;
        else if (ratio < (1.0f - depthThreshold)) moveDir = IsPlayer1 ? -1f : 1f;

        if (moveDir != 0)
            transform.Translate(Vector3.right * moveDir * depthMovementSpeed * Time.deltaTime, Space.World);
    }

    private void DetectLeanMovement()
    {
        Vector3 leftHipPos = GetRawLandmarkPosition(LeftHip);
        Vector3 rightHipPos = GetRawLandmarkPosition(RightHip);
        float hipCenterX = (leftHipPos.x + rightHipPos.x) / 2.0f;

        if (lastHipCenterX == 0) { lastHipCenterX = hipCenterX; return; }

        float leanDelta = hipCenterX - lastHipCenterX;
        bool isGuarding = GetRawLandmarkPosition(LeftWrist).y > GetRawLandmarkPosition(LeftHip).y;

        if (isGuarding && !leftHandWasFast && !rightHandWasFast)
        {
            if (Mathf.Abs(leanDelta) > leanThreshold)
            {
                float normalizedLean = Mathf.Clamp(leanDelta, -maxLean, maxLean) / maxLean;
                transform.Translate(Vector3.right * normalizedLean * leanMovementSpeed * Time.deltaTime, Space.World);
            }
        }
        lastHipCenterX = hipCenterX;
    }

    private void DetectPunches()
    {
        float currentTime = Time.time;
        float deltaTime = currentTime - lastUpdateTime;
        if (deltaTime <= 0.01f) return;

        Vector3 curL = GetRawLandmarkPosition(LeftWrist);
        Vector3 curR = GetRawLandmarkPosition(RightWrist);

        float lVel = (curL - lastLeftHandPos).magnitude / deltaTime;
        float rVel = (curR - lastRightHandPos).magnitude / deltaTime;

        float smLVel = Mathf.Lerp(lastLeftHandVelocity, lVel, velocitySmoothingFactor);
        float smRVel = Mathf.Lerp(lastRightHandVelocity, rVel, velocitySmoothingFactor);

        if (smLVel > punchVelocityThreshold && (currentTime - lastLeftPunchTime) > punchCooldown)
        {
            if (!leftHandWasFast)
            {
                TriggerPunch("Left", smLVel);
                lastLeftPunchTime = currentTime;
                leftHandWasFast = true;
            }
        }
        else if (smLVel < punchVelocityResetThreshold) leftHandWasFast = false;

        if (smRVel > punchVelocityThreshold && (currentTime - lastRightPunchTime) > punchCooldown)
        {
            if (!rightHandWasFast)
            {
                TriggerPunch("Right", smRVel);
                lastRightPunchTime = currentTime;
                rightHandWasFast = true;
            }
        }
        else if (smRVel < punchVelocityResetThreshold) rightHandWasFast = false;

        lastLeftHandPos = curL; lastRightHandPos = curR;
        lastLeftHandVelocity = smLVel; lastRightHandVelocity = smRVel;
        lastUpdateTime = currentTime;
    }

    private void TriggerPunch(string hand, float velocity)
    {
        if (healthSystem != null && healthSystem.IsKnockedOut()) return;

        if (hand == "Right" && rightHandHitbox) StartCoroutine(ManageHitbox(rightHandHitbox));
        if (hand == "Left" && leftHandHitbox) StartCoroutine(ManageHitbox(leftHandHitbox));

        animator.SetTrigger(hand == "Right" ? "PunchRight" : "PunchLeft");
    }

    private IEnumerator ManageHitbox(Hitbox hitbox)
    {
        hitbox.EnableHitbox();
        yield return new WaitForSeconds(hitboxActiveTime);
        if (hitbox != null) hitbox.GetComponent<Collider>().enabled = false;
    }

    private void UpdateTargetLandmarks()
    {
        Vector3 basePos = transform.position;
        Quaternion baseRot = transform.rotation;

        for (int i = 0; i < 17; i++)
        {
            int srcIdx = mirrorInput ? GetMirroredIndex(i) : i;
            if (srcIdx >= keypoints.Count) continue;

            float x_c = keypoints[srcIdx].x - 0.5f;
            float y_c = 0.5f - keypoints[srcIdx].y;
            Vector3 localPos = new Vector3(x_c, y_c, 0);
            targetWorldLandmarks[i] = basePos + (baseRot * ((localPos + poseOffset) * poseScale));
        }
    }

    private int GetMirroredIndex(int i)
    {
        int[] map = { 0, 2, 1, 4, 3, 6, 5, 8, 7, 10, 9, 12, 11, 14, 13, 16, 15 };
        return (i < map.Length) ? map[i] : i;
    }

    Vector3 GetRawLandmarkPosition(int index) =>
        (targetWorldLandmarks != null && index < targetWorldLandmarks.Length) ? targetWorldLandmarks[index] : Vector3.zero;

    Vector3 GetLandmarkPosition(int index) =>
        (smoothedWorldLandmarks != null && index < smoothedWorldLandmarks.Length) ? smoothedWorldLandmarks[index] : Vector3.zero;

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
        if (!useIKTracking || keypoints == null) return;
        SetIK(AvatarIKGoal.LeftHand, leftHandTarget, AvatarIKHint.LeftElbow, leftElbowTarget);
        SetIK(AvatarIKGoal.RightHand, rightHandTarget, AvatarIKHint.RightElbow, rightElbowTarget);
        if (headTarget) { animator.SetLookAtWeight(1); animator.SetLookAtPosition(headTarget.position); }
    }

    void SetIK(AvatarIKGoal goal, Transform t, AvatarIKHint hint, Transform ht)
    {
        if (t) { animator.SetIKPositionWeight(goal, 1); animator.SetIKPosition(goal, t.position); }
        if (ht) { animator.SetIKHintPositionWeight(hint, 1); animator.SetIKHintPosition(hint, ht.position); }
    }

    void OnDrawGizmos()
    {
        if (!showGizmos || smoothedWorldLandmarks == null || smoothedWorldLandmarks.Length < 17) return;
        Gizmos.color = Color.cyan;
        foreach (var pos in smoothedWorldLandmarks) Gizmos.DrawSphere(pos, 0.015f);
    }

    public float GetLeftHandVelocity() => lastLeftHandVelocity;
    public float GetRightHandVelocity() => lastRightHandVelocity;
}