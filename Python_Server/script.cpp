using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AvatarController : MonoBehaviour
{
    [Header("=== PLAYER IDENTITY ===")]
    // We lock this. 0 = Player 1 (Left), 1 = Player 2 (Right)
    public int playerID = 0; 
    public bool IsPlayer1 => playerID == 0; 

    [Header("=== MOVEMENT SYSTEM ===")]
    public float depthMovementSpeed = 3.0f;
    public float leanMovementSpeed = 2.5f;
    [Range(0.05f, 0.3f)] public float depthThreshold = 0.12f;
    [Range(0.02f, 0.15f)] public float leanThreshold = 0.05f;
    public float maxLean = 0.2f;

    [Header("=== COMBAT STATE ===")]
    public bool canFight = false;

    [Header("=== HITBOXES ===")]
    public Hitbox leftHandHitbox;
    public Hitbox rightHandHitbox;
    public float hitboxActiveTime = 0.3f;

    [Header("=== CALIBRATION ===")]
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
    public float maxUpwardMovement = 0.15f;
    public float minVisibilityThreshold = 0.25f;
    public float autoResetTimeout = 1.5f;

    [Header("=== DEBUG ===")]
    public bool debugMode = false;
    // We mirror input for P1 (User moves Right -> Avatar moves Right on screen)
    // For P2, we usually don't mirror if they are facing the other way, 
    // BUT with a single webcam, both usually need mirroring.
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
        
        lastLeftHandPos = targetWorldLandmarks[LeftWrist];
        lastRightHandPos = targetWorldLandmarks[RightWrist];

        // Safety Checks
        if (leftHandHitbox == null) Debug.LogWarning($"[{gameObject.name}] Left Hitbox Missing");
        if (rightHandHitbox == null) Debug.LogWarning($"[{gameObject.name}] Right Hitbox Missing");
    }

    // === SETUP FUNCTION CALLED BY POSEMANAGER ===
    public void SetPlayerID(int id)
    {
        playerID = id;
        // P1 (Left Guy) mirrors input so Right Hand = Right Side of Screen
        // P2 (Right Guy) mirrors too because it's a webcam
        mirrorInput = true; 
        
        Debug.Log($"<color=cyan>[{gameObject.name}] Configured as Player {playerID + 1} ({(IsPlayer1 ? "Left" : "Right")})</color>");
    }

    public void ReceiveKeypoints(List<LandmarkData> keypoints)
    {
        if (keypoints == null || keypoints.Count < 17) return;
        this.keypoints = keypoints;
        UpdateTargetLandmarks();
    }

    void Update()
    {
        // Smoothing
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

    // === MOVEMENT LOGIC ===
    private void DetectDepthMovement()
    {
        int lShoulderIdx = mirrorInput ? RightShoulder : LeftShoulder;
        int rShoulderIdx = mirrorInput ? LeftShoulder : RightShoulder;

        if (lShoulderIdx >= keypoints.Count || rShoulderIdx >= keypoints.Count) return;

        float currentWidth = Mathf.Abs(keypoints[lShoulderIdx].x - keypoints[rShoulderIdx].x);

        if (initialShoulderWidth < 0)
        {
            initialShoulderWidth = currentWidth;
            if (debugMode) Debug.Log($"[{gameObject.name}] Calibrated Width: {initialShoulderWidth}");
            return;
        }

        float ratio = currentWidth / initialShoulderWidth;
        float moveDir = 0f;

        // RATIO > 1.1 = Closer = ATTACK
        if (ratio > (1.0f + depthThreshold))
        {
            // If Player 1 (Left), Attack means move Right (+1)
            // If Player 2 (Right), Attack means move Left (-1)
            moveDir = IsPlayer1 ? 1f : -1f;
        }
        // RATIO < 0.9 = Further = RETREAT
        else if (ratio < (1.0f - depthThreshold))
        {
            // If Player 1, Retreat means move Left (-1)
            // If Player 2, Retreat means move Right (+1)
            moveDir = IsPlayer1 ? -1f : 1f;
        }

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
        bool isGuarding = GetRawLandmarkPosition(LeftWrist).y > GetRawLandmarkPosition(LeftHip).y; // Simple guard check

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

    // === PUNCH DETECTION ===
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

        // Left Punch
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

        // Right Punch
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
        Debug.Log($"<color=orange>🥊 {gameObject.name} {hand} PUNCH!</color>");
        
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

    // === UTILS ===
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
        // Simple swap map for 17 points
        int[] map = {0, 2, 1, 4, 3, 6, 5, 8, 7, 10, 9, 12, 11, 14, 13, 16, 15};
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
        if(headTarget) { animator.SetLookAtWeight(1); animator.SetLookAtPosition(headTarget.position); }
    }

    void SetIK(AvatarIKGoal goal, Transform t, AvatarIKHint hint, Transform ht)
    {
        if(t){ animator.SetIKPositionWeight(goal,1); animator.SetIKPosition(goal, t.position); }
        if(ht){ animator.SetIKHintPositionWeight(hint,1); animator.SetIKHintPosition(hint, ht.position); }
    }
}