using System;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera), typeof(CinemachineBrain))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class CameraControlThirdPerson : MonoBehaviour
{
    [Serializable]
    struct CameraSettings
    {
        public float distance;
        public Vector3 damping;
        public Vector3 targetOffset;
        public float minimumPitch;
        public float maximumPitch;
        public Vector2 deadZoneSize;
        public Vector2 hardLimitSize;
    }

    [Header("Player")]
    [SerializeField] Transform target;

    [Header("Mouse Look")]
    [SerializeField] float lookSensitivity = 0.12f;
    [SerializeField] float initialPitch = 8f;
    [Header("Shared Soft / Hard Lock Camera")]
    [Tooltip("Allowed mouse adjustment above/below soft-lock pitch. Set to zero to lock it completely.")]
    [SerializeField, Range(0f, 5f)] float lockOnVerticalRange = 3f;
    [SerializeField, Range(0f, 1f)] float lockOnVerticalSensitivity = 0.25f;
    [Tooltip("How quickly the camera moves behind the mech when following a locked enemy.")]
    [SerializeField, Min(0.01f)] float hardLockOrbitResponse = 0.12f;
    [Tooltip("Minimum downward orbit angle while locked. Close, low targets use a steeper view to clear the mech.")]
    [SerializeField, Range(5f, 60f)] float hardLockPitch = 20f;
    [Tooltip("Shared height above the player root for both lock modes.")]
    [SerializeField, Min(0f)] float hardLockHeight = 1.5f;
    [Tooltip("Soft-lock screen-center aim is framed this far ahead of the mech, independently of the selected enemy.")]
    [SerializeField, Min(1f)] float softLockAimDistance = 20f;

    [Header("Melee Framing")]
    [SerializeField, Range(0f, 10f)] float meleePitchOffset = 7f;
    [SerializeField, Min(0.1f)] float meleeFramingRange = 6f;
    [SerializeField, Range(0f, 1f)] float meleeShoulderCentering = 0.75f;
    [SerializeField, Range(0f, 1f)] float meleeTargetWeight = 0.65f;
    [SerializeField, Min(0.01f)] float meleeFramingResponse = 0.15f;

    [Header("Follow and Framing")]
    // Keep the serialized name and members so existing scene tuning survives cleanup.
    [SerializeField] CameraSettings normalProfile = new CameraSettings
    {
        distance = 13f,
        damping = new Vector3(0.045f, 0.09f, 0.13f),
        targetOffset = new Vector3(0f, 0.9f, 0f),
        minimumPitch = -18f,
        maximumPitch = 30f,
        deadZoneSize = new Vector2(0.05f, 0.05f),
        hardLimitSize = new Vector2(0.78f, 0.78f)
    };
    [SerializeField] float shoulderHeight = 0.4f;
    [SerializeField] float cameraSide = 0f;
    [SerializeField] Vector2 screenPosition = Vector2.zero;
    [SerializeField] Vector2 rotationDamping = new Vector2(0.1f, 0.1f);
    [SerializeField] Vector3 quickBoostFollowDamping = Vector3.zero;

    [Header("Lateral Framing")]
    [SerializeField] float lateralOffsetDistance = 1.5f;
    [SerializeField] float lateralOffsetSpeed = 12f;
    [SerializeField] float lateralOffsetResponse = 0.25f;

    [Header("Obstacle Avoidance")]
    [SerializeField] bool avoidObstacles = true;
    [Tooltip("Solid structures must have colliders on these layers. Triggers and loose MechPart pickups are ignored.")]
    [SerializeField] LayerMask obstacleLayers = 1;
    [Tooltip("Minimum clearance. Automatically enlarged when needed to protect the camera's near clip plane.")]
    [SerializeField, Range(0.05f, 1f)] float cameraRadius = 0.35f;
    [SerializeField, Min(0f)] float obstacleReturnDamping = 0.25f;

    [Header("Movement Feedback")]
    [SerializeField] bool movementFeedback = true;
    [SerializeField, Range(30f, 100f)] float baseFieldOfView = 60f;
    [SerializeField, Range(0f, 15f)] float quickBoostFovKick = 7f;
    [SerializeField, Range(0f, 20f)] float assaultBoostFovKick = 10f;
    [SerializeField, Range(1f, 1.3f)] float assaultBoostDistanceMultiplier = 1.12f;
    [SerializeField] AnimationCurve quickBoostFovCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.2f, 1f), new Keyframe(1f, 0f));
    float movementFovOffset;
    float assaultFraming;

    CinemachineCamera cinemachineCamera;
    Camera outputCamera;
    GameObject cameraRigObject;
    CinemachineThirdPersonFollow thirdPersonFollow;
    CinemachineRotationComposer rotationComposer;
    Transform cameraTarget;
    Transform meleeFocus;
    Transform softLockFocus;
    MechCombat combatController;
    MechMovementController movementController;
    CharacterController movementCapsule;
    MechTargetLockController targetingController;
    Vector2 lookInput; // this frame's look delta, supplied by MechPlayerInput
    bool wasHardLocked;
    Vector3 previousTargetPosition;
    float currentLateralOffset;
    float cameraYaw;
    float cameraPitch;
    float currentLockHeight;
    float lockPitchAdjustment;
    float meleeFraming;
    bool wasTargeting;
    CinemachineImpulseListener combatImpulseListener;

    // An additive Cinemachine extension; no changes to the tracking rig or lens.
    public CinemachineImpulseListener GetCombatImpulseListener()
    {
        if (cameraRigObject == null) return null;
        if (combatImpulseListener == null)
        {
            combatImpulseListener = cameraRigObject.AddComponent<CinemachineImpulseListener>();
            combatImpulseListener.ChannelMask = 64;
            combatImpulseListener.Gain = 1f;
            combatImpulseListener.UseCameraSpace = true;
            combatImpulseListener.Use2DDistance = false;
            combatImpulseListener.ApplyAfter = CinemachineCore.Stage.Noise;
            combatImpulseListener.SignalCombinationMode = CinemachineImpulseListener.SignalCombinationModes.UseLargest;
            combatImpulseListener.ReactionSettings = default;
        }
        return combatImpulseListener;
    }

    public Transform Target => target;
    public void BindTarget(Transform player)
    {
        target = player;
        if (cameraTarget == null) return; // Before Awake, preserve normal rig initialization.
        cameraTarget.SetParent(player, false);
        movementController = player.GetComponent<MechMovementController>();
        combatController = player.GetComponent<MechCombat>();
        movementCapsule = player.GetComponent<CharacterController>();
        targetingController = player.GetComponent<MechTargetLockController>();
        previousTargetPosition = player.position;
        cameraYaw = player.eulerAngles.y;
        cameraPitch = initialPitch;
        wasHardLocked = wasTargeting = false;
        currentLateralOffset = meleeFraming = 0;
        cinemachineCamera.PreviousStateIsValid = false;
    }

    void Awake()
    {
        if (target == null)
        {
            Debug.LogError("Assign the player root to the third-person camera's Target.", this);
            enabled = false;
            return;
        }

        outputCamera = GetComponent<Camera>();
        cameraRigObject = new GameObject("Mech Cinemachine Camera");
        cinemachineCamera = cameraRigObject.AddComponent<CinemachineCamera>();
        thirdPersonFollow = cameraRigObject.AddComponent<CinemachineThirdPersonFollow>();
        rotationComposer = cameraRigObject.AddComponent<CinemachineRotationComposer>();

        cameraTarget = new GameObject("CameraTarget").transform;
        cameraTarget.SetParent(target, false);
        movementController = target.GetComponent<MechMovementController>();
        combatController = target.GetComponent<MechCombat>();
        meleeFocus = new GameObject("Melee Camera Focus").transform;
        softLockFocus = new GameObject("Soft Lock Aim Focus").transform;
        movementCapsule = target.GetComponent<CharacterController>();
        targetingController = target.GetComponent<MechTargetLockController>();
        cameraYaw = target.eulerAngles.y;
        cameraPitch = initialPitch;
        cinemachineCamera.Follow = cameraTarget;
        cinemachineCamera.LookAt = cameraTarget;
        ApplySettings();
    }

    void OnEnable()
    {
        if (cameraRigObject != null)
            cameraRigObject.SetActive(true);
        if (target != null)
            previousTargetPosition = target.position;
    }

    void OnDisable()
    {
        movementFovOffset = 0f;
        assaultFraming = 0f;
        if (cameraRigObject != null)
            cameraRigObject.SetActive(false);
    }

    void LateUpdate()
    {
        if (target == null || cameraTarget == null)
            return;

        UpdateCameraTargetRotation();
        UpdateMovementFeedback(Time.deltaTime);
        ApplySettings();

        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 velocity = movementController != null && movementController.isActiveAndEnabled
            ? movementController.Velocity
            : (target.position - previousTargetPosition) / deltaTime;
        float lateralSpeed = Vector3.Dot(velocity, cameraTarget.right);
        float desiredOffset = Mathf.Clamp(lateralSpeed / Mathf.Max(lateralOffsetSpeed, 0.01f), -1f, 1f)
            * lateralOffsetDistance;
        currentLateralOffset = Mathf.Lerp(currentLateralOffset, desiredOffset,
            1f - Mathf.Exp(-deltaTime / Mathf.Max(lateralOffsetResponse, 0.01f)));
        thirdPersonFollow.ShoulderOffset = ShoulderOffset();
        previousTargetPosition = target.position;
    }

    void ApplySettings()
    {
        bool targetingActive = targetingController != null && targetingController.isActiveAndEnabled && !targetingController.IsFreeAim;
        bool hardLocked = targetingController != null && targetingController.IsHardLocked;
        cinemachineCamera.LookAt = hardLocked ? targetingController.CurrentTarget.AimTransform : cameraTarget;
        if (targetingActive && !targetingController.IsHardLockMode && softLockFocus != null)
        {
            // Looking at the raised player pivot puts the hidden center ray through
            // the mech and into the ground before it can reach enemies. Aim ahead
            // along the player's manual yaw instead; enemy selection never steers it.
            Vector3 forward = Quaternion.Euler(0f, cameraYaw, 0f) * Vector3.forward;
            float aimHeight = -Mathf.Tan(lockPitchAdjustment * Mathf.Deg2Rad)
                * (softLockAimDistance + normalProfile.distance);
            softLockFocus.position = target.position + forward * softLockAimDistance + Vector3.up * aimHeight;
            cinemachineCamera.LookAt = softLockFocus;
        }
        if (hardLocked && meleeFraming > 0.001f && meleeFocus != null)
        {
            Vector3 enemyFocus = targetingController.CurrentTarget.AimPosition;
            Vector3 playerFocus = target.position + Vector3.up * hardLockHeight;
            Vector3 sharedFocus = Vector3.Lerp(playerFocus, enemyFocus, meleeTargetWeight);
            meleeFocus.position = Vector3.Lerp(enemyFocus, sharedFocus, meleeFraming);
            cinemachineCamera.LookAt = meleeFocus;
        }
        thirdPersonFollow.CameraDistance = normalProfile.distance
            * Mathf.Lerp(1f, assaultBoostDistanceMultiplier, assaultFraming);
        LensSettings lens = cinemachineCamera.Lens;
        lens.FieldOfView = baseFieldOfView + movementFovOffset;
        cinemachineCamera.Lens = lens;
        // A replaced part drops beside the follow pivot. It must not collapse
        // the camera boom, even when a custom structure mask includes all layers.
        int cameraObstacles = obstacleLayers.value;
        int loosePartLayer = LayerMask.NameToLayer(Part.LoosePhysicsLayerName);
        if (loosePartLayer >= 0) cameraObstacles &= ~(1 << loosePartLayer);
        thirdPersonFollow.AvoidObstacles = new CinemachineThirdPersonFollow.ObstacleSettings
        {
            Enabled = avoidObstacles,
            CollisionFilter = cameraObstacles,
            IgnoreTag = "Player",
            CameraRadius = CameraCollisionRadius(lens),
            DampingIntoCollision = 0f,
            DampingFromCollision = obstacleReturnDamping
        };
        thirdPersonFollow.CameraSide = 0.5f;
        thirdPersonFollow.VerticalArmLength = shoulderHeight;
        thirdPersonFollow.Damping = movementController != null && movementController.IsQuickBoosting
            ? quickBoostFollowDamping : normalProfile.damping;
        thirdPersonFollow.ShoulderOffset = ShoulderOffset();
        rotationComposer.TargetOffset = targetingActive ? Vector3.zero : normalProfile.targetOffset;
        // Center on the manual aim focus in soft lock, or the enemy in hard lock.
        // The only visible cursor remains the existing UI cursor.
        rotationComposer.Damping = targetingActive ? Vector2.zero : rotationDamping;
        rotationComposer.CenterOnActivate = true;

        ScreenComposerSettings composition = rotationComposer.Composition;
        composition.ScreenPosition = targetingActive ? Vector2.zero : screenPosition;
        composition.DeadZone = new ScreenComposerSettings.DeadZoneSettings
        {
            Enabled = !targetingActive,
            Size = normalProfile.deadZoneSize
        };
        composition.HardLimits = new ScreenComposerSettings.HardLimitSettings
        {
            Enabled = true,
            Size = normalProfile.hardLimitSize,
            Offset = Vector2.zero
        };
        composition.Validate();
        rotationComposer.Composition = composition;
    }

    float CameraCollisionRadius(LensSettings lens)
    {
        // A clear camera centre is insufficient: the corners of the near plane
        // can still cross a wall, especially with a wide aspect ratio or boost FOV.
        float aspect = outputCamera != null ? outputCamera.aspect : lens.Aspect;
        float halfHeight = Mathf.Max(0.001f, lens.NearClipPlane)
            * Mathf.Tan(Mathf.Clamp(lens.FieldOfView, 1f, 179f) * 0.5f * Mathf.Deg2Rad);
        float nearCorner = Mathf.Sqrt(halfHeight * halfHeight * (1f + aspect * aspect)
            + lens.NearClipPlane * lens.NearClipPlane);
        return Mathf.Max(cameraRadius, nearCorner + 0.02f);
    }

    // Look input arrives from MechPlayerInput each frame (zero while the cursor is free or paused).
    public void SetLookInput(Vector2 delta) => lookInput = delta;

    void UpdateCameraTargetRotation()
    {
        UpdateCameraOrbit(lookInput, Time.deltaTime);
        lookInput = Vector2.zero;
    }

    void UpdateCameraOrbit(Vector2 lookInput, float deltaTime)
    {
        bool targetingActive = targetingController != null && targetingController.isActiveAndEnabled
            && !targetingController.IsFreeAim;
        bool hardLocked = targetingActive && targetingController.IsHardLocked;
        bool hardMode = targetingActive && targetingController.IsHardLockMode;
        MechTargetable enemy = targetingActive ? targetingController.CurrentTarget : null;
        Vector3 direction = enemy != null ? enemy.AimPosition - target.position : Vector3.zero;
        float planarDistance = new Vector2(direction.x, direction.z).magnitude;
        float response = 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.01f, hardLockOrbitResponse));
        Vector3 cameraForward = Quaternion.Euler(0f, cameraYaw, 0f) * Vector3.forward;
        bool closeMelee = targetingActive && enemy != null && combatController != null && combatController.MeleeCameraActive
            && planarDistance <= meleeFramingRange && Mathf.Abs(direction.y) <= 2.5f
            && Vector3.Dot(cameraForward, Vector3.ProjectOnPlane(direction, Vector3.up).normalized) > 0.5f;
        meleeFraming = Mathf.Lerp(meleeFraming, closeMelee ? 1f : 0f,
            1f - Mathf.Exp(-deltaTime / Mathf.Max(0.01f, meleeFramingResponse)));

        if (wasHardLocked && !hardMode)
        {
            cameraYaw = transform.eulerAngles.y;
            if (!targetingActive) cameraPitch = Mathf.DeltaAngle(0f, transform.eulerAngles.x);
        }
        if (hardLocked)
        {
            if (planarDistance > 0.001f)
                cameraYaw = Mathf.LerpAngle(cameraYaw, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, response);
        }
        else
        {
            cameraYaw += lookInput.x * lookSensitivity;
        }
        if (targetingActive)
        {
            if (!wasTargeting) lockPitchAdjustment = 0f;
            if (!hardMode)
                lockPitchAdjustment = Mathf.Clamp(lockPitchAdjustment - lookInput.y * lookSensitivity * lockOnVerticalSensitivity,
                    -lockOnVerticalRange, lockOnVerticalRange);
            float desiredPitch = hardLockPitch + lockPitchAdjustment;
            // Both modes share the same clearance, height, and pitch calculations.
            if (enemy != null && Vector3.Dot(cameraForward, Vector3.ProjectOnPlane(direction, Vector3.up).normalized) > 0.5f)
            {
                float bodyRadius = movementCapsule != null
                    ? Mathf.Max(movementCapsule.bounds.extents.x, movementCapsule.bounds.extents.z) : 1f;
                float clearancePitch = Mathf.Atan2(hardLockHeight - direction.y,
                    Mathf.Max(0.1f, planarDistance - bodyRadius - cameraRadius)) * Mathf.Rad2Deg;
                desiredPitch = Mathf.Max(desiredPitch, clearancePitch);
            }
            desiredPitch = Mathf.Clamp(desiredPitch + meleePitchOffset * meleeFraming, 5f, 80f);
            // Never enter targeting from a low free-aim angle that hides the ground.
            if (!wasTargeting) cameraPitch = Mathf.Max(cameraPitch, hardLockPitch - lockOnVerticalRange);
            cameraPitch = Mathf.LerpAngle(cameraPitch, desiredPitch, response);
            currentLockHeight = Mathf.Lerp(currentLockHeight, hardLockHeight, response);
        }
        else
        {
            cameraPitch = Mathf.Clamp(cameraPitch - lookInput.y * lookSensitivity,
                normalProfile.minimumPitch, normalProfile.maximumPitch);
            currentLockHeight = Mathf.Lerp(currentLockHeight, 0f, response);
        }
        wasHardLocked = hardMode;
        wasTargeting = targetingActive;
        cameraTarget.SetPositionAndRotation(target.position + Vector3.up * currentLockHeight,
            Quaternion.Euler(cameraPitch, cameraYaw, 0f));
    }

    void UpdateMovementFeedback(float deltaTime)
    {
        // Melee framing does not pump FOV or distance as the target gets closer.
        if (meleeFraming > 0.001f) return;
        bool active = movementFeedback && movementController != null && movementController.isActiveAndEnabled;
        float desiredFov = active && movementController.IsQuickBoosting
            ? quickBoostFovCurve.Evaluate(movementController.QuickBoostProgress) * quickBoostFovKick
            : active && movementController.IsAssaultBoosting ? assaultBoostFovKick : 0f;
        movementFovOffset = Mathf.Lerp(movementFovOffset, desiredFov, 1f - Mathf.Exp(-deltaTime / 0.08f));
        float desiredFraming = active && movementController.IsAssaultBoosting ? 1f : 0f;
        assaultFraming = Mathf.Lerp(assaultFraming, desiredFraming, 1f - Mathf.Exp(-deltaTime / 0.2f));
    }

    Vector3 ShoulderOffset() => new Vector3((cameraSide + currentLateralOffset)
        * (1f - meleeFraming * meleeShoulderCentering), shoulderHeight, 0f);

    void OnValidate()
    {
        lookSensitivity = Mathf.Max(0f, lookSensitivity);
        hardLockPitch = Mathf.Clamp(hardLockPitch, 5f, 60f);
        softLockAimDistance = Mathf.Max(1f, softLockAimDistance);
        lockOnVerticalRange = Mathf.Clamp(lockOnVerticalRange, 0f, Mathf.Min(5f, hardLockPitch - 1f));
        lockOnVerticalSensitivity = Mathf.Clamp01(lockOnVerticalSensitivity);
        normalProfile.distance = Mathf.Max(0.1f, normalProfile.distance);
        normalProfile.maximumPitch = Mathf.Max(normalProfile.minimumPitch, normalProfile.maximumPitch);
        normalProfile.damping = Vector3.Max(Vector3.zero, normalProfile.damping);
        quickBoostFollowDamping = Vector3.Max(Vector3.zero, quickBoostFollowDamping);
        rotationDamping = Vector2.Max(Vector2.zero, rotationDamping);
        lateralOffsetSpeed = Mathf.Max(0.01f, lateralOffsetSpeed);
        lateralOffsetResponse = Mathf.Max(0.01f, lateralOffsetResponse);
        if (Application.isPlaying && cinemachineCamera != null)
            ApplySettings();
    }

    void OnDestroy()
    {
        if (cameraRigObject != null)
            Destroy(cameraRigObject);
        if (cameraTarget != null)
            Destroy(cameraTarget.gameObject);
        if (meleeFocus != null)
            Destroy(meleeFocus.gameObject);
        if (softLockFocus != null)
            Destroy(softLockFocus.gameObject);
    }
}
