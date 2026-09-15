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
    [SerializeField] InputActionAsset inputActions;

    [Header("Mouse Look")]
    [SerializeField] float lookSensitivity = 0.12f;
    [SerializeField] float initialPitch = 8f;
    [SerializeField] bool lockCursorOnPlay = true;
    [Tooltip("How quickly the camera moves behind the mech when following a locked enemy.")]
    [SerializeField, Min(0.01f)] float hardLockOrbitResponse = 0.12f;
    [Tooltip("Minimum downward orbit angle while locked. Close, low targets use a steeper view to clear the mech.")]
    [SerializeField, Range(5f, 60f)] float hardLockPitch = 20f;
    [Tooltip("Extra height above the player root for the hard-lock follow pivot.")]
    [SerializeField, Min(0f)] float hardLockHeight = 1.5f;

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
    [SerializeField] LayerMask obstacleLayers = 1;
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
    GameObject cameraRigObject;
    CinemachineThirdPersonFollow thirdPersonFollow;
    CinemachineRotationComposer rotationComposer;
    Transform cameraTarget;
    MechMovementController movementController;
    CharacterController movementCapsule;
    MechTargetLockController targetingController;
    InputAction lookAction;
    InputAction lockAction;
    bool wasHardLocked;
    Vector3 previousTargetPosition;
    float currentLateralOffset;
    float cameraYaw;
    float cameraPitch;
    float currentLockHeight;

    void Awake()
    {
        if (target == null)
        {
            Debug.LogError("Assign the player root to the third-person camera's Target.", this);
            enabled = false;
            return;
        }

        cameraRigObject = new GameObject("Mech Cinemachine Camera");
        cinemachineCamera = cameraRigObject.AddComponent<CinemachineCamera>();
        thirdPersonFollow = cameraRigObject.AddComponent<CinemachineThirdPersonFollow>();
        rotationComposer = cameraRigObject.AddComponent<CinemachineRotationComposer>();

        cameraTarget = new GameObject("CameraTarget").transform;
        cameraTarget.SetParent(target, false);
        movementController = target.GetComponent<MechMovementController>();
        movementCapsule = target.GetComponent<CharacterController>();
        targetingController = target.GetComponent<MechTargetLockController>();
        cameraYaw = target.eulerAngles.y;
        cameraPitch = initialPitch;
        cinemachineCamera.Follow = cameraTarget;
        cinemachineCamera.LookAt = cameraTarget;
        ApplySettings();
    }

    void Start()
    {
        SetCursorLocked(lockCursorOnPlay);
    }

    void OnEnable()
    {
        lookAction = inputActions != null ? inputActions.FindAction("Player/Look", false) : null;
        lookAction?.Enable();
        lockAction = inputActions != null ? inputActions.FindAction("Player/LockOn", false) : null;
        lockAction?.Enable();
        if (cameraRigObject != null)
            cameraRigObject.SetActive(true);
        if (target != null)
            previousTargetPosition = target.position;
    }

    void OnDisable()
    {
        lookAction?.Disable();
        lockAction?.Disable();
        targetingController?.CancelLockButtonInput();
        movementFovOffset = 0f;
        assaultFraming = 0f;
        if (cameraRigObject != null)
            cameraRigObject.SetActive(false);
        SetCursorLocked(false);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetCursorLocked(false);
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            SetCursorLocked(true);

        if (Cursor.lockState != CursorLockMode.Locked || Time.timeScale == 0f || targetingController == null)
        {
            targetingController?.CancelLockButtonInput();
            return;
        }
        bool pressed = lockAction != null ? lockAction.WasPressedThisFrame()
            : Mouse.current != null && Mouse.current.middleButton.wasPressedThisFrame;
        bool held = lockAction != null ? lockAction.IsPressed()
            : Mouse.current != null && Mouse.current.middleButton.isPressed;
        bool released = lockAction != null ? lockAction.WasReleasedThisFrame()
            : Mouse.current != null && Mouse.current.middleButton.wasReleasedThisFrame;
        targetingController.ProcessLockButton(pressed, held, released, Time.unscaledDeltaTime);
        if (!pressed && !held && !released)
            targetingController.ProcessSwitchInput(Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero,
                Time.unscaledDeltaTime);
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
        thirdPersonFollow.ShoulderOffset = new Vector3(cameraSide + currentLateralOffset, shoulderHeight, 0f);
        previousTargetPosition = target.position;
    }

    void ApplySettings()
    {
        bool hardLocked = targetingController != null && targetingController.IsHardLocked;
        cinemachineCamera.LookAt = hardLocked ? targetingController.CurrentTarget.AimTransform : cameraTarget;
        thirdPersonFollow.CameraDistance = normalProfile.distance
            * Mathf.Lerp(1f, assaultBoostDistanceMultiplier, assaultFraming);
        LensSettings lens = cinemachineCamera.Lens;
        lens.FieldOfView = baseFieldOfView + movementFovOffset;
        cinemachineCamera.Lens = lens;
        thirdPersonFollow.AvoidObstacles = new CinemachineThirdPersonFollow.ObstacleSettings
        {
            Enabled = avoidObstacles,
            CollisionFilter = obstacleLayers,
            IgnoreTag = "Player",
            CameraRadius = cameraRadius,
            DampingIntoCollision = 0f,
            DampingFromCollision = obstacleReturnDamping
        };
        thirdPersonFollow.CameraSide = 0.5f;
        thirdPersonFollow.VerticalArmLength = shoulderHeight;
        thirdPersonFollow.Damping = movementController != null && movementController.IsQuickBoosting
            ? quickBoostFollowDamping : normalProfile.damping;
        thirdPersonFollow.ShoulderOffset = new Vector3(cameraSide + currentLateralOffset, shoulderHeight, 0f);
        rotationComposer.TargetOffset = hardLocked ? Vector3.zero : normalProfile.targetOffset;
        // Center the actual camera on the enemy; the only visible cursor remains the existing UI cursor.
        rotationComposer.Damping = hardLocked ? Vector2.zero : rotationDamping;
        rotationComposer.CenterOnActivate = true;

        ScreenComposerSettings composition = rotationComposer.Composition;
        composition.ScreenPosition = hardLocked ? Vector2.zero : screenPosition;
        composition.DeadZone = new ScreenComposerSettings.DeadZoneSettings
        {
            Enabled = !hardLocked,
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

    void UpdateCameraTargetRotation()
    {
        Vector2 lookInput = Cursor.lockState != CursorLockMode.Locked || Time.timeScale == 0f ? Vector2.zero
            : lookAction != null ? lookAction.ReadValue<Vector2>()
            : Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        UpdateCameraOrbit(lookInput, Time.deltaTime);
    }

    void UpdateCameraOrbit(Vector2 lookInput, float deltaTime)
    {
        bool hardLocked = targetingController != null && targetingController.IsHardLocked;
        if (hardLocked)
        {
            Vector3 direction = targetingController.CurrentTarget.AimPosition - target.position;
            float planarDistance = new Vector2(direction.x, direction.z).magnitude;
            float response = 1f - Mathf.Exp(-deltaTime / Mathf.Max(0.01f, hardLockOrbitResponse));
            if (planarDistance > 0.001f)
                cameraYaw = Mathf.LerpAngle(cameraYaw, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg, response);
            // Orbit above the mech. For close/low enemies, steepen the orbit so the
            // sightline passes over the raised pivot instead of through the mech.
            float bodyRadius = movementCapsule != null
                ? Mathf.Max(movementCapsule.bounds.extents.x, movementCapsule.bounds.extents.z) : 1f;
            // Clear the front of the body, not just the vertical line through its center.
            float clearancePitch = Mathf.Atan2(hardLockHeight - direction.y,
                Mathf.Max(0.1f, planarDistance - bodyRadius - cameraRadius)) * Mathf.Rad2Deg;
            float desiredPitch = Mathf.Clamp(Mathf.Max(hardLockPitch, clearancePitch), 5f, 80f);
            cameraPitch = Mathf.LerpAngle(cameraPitch, desiredPitch, response);
            currentLockHeight = Mathf.Lerp(currentLockHeight, hardLockHeight, response);
        }
        else
        {
            if (wasHardLocked)
            {
                // Resume mouse look from the current view instead of the pre-lock heading.
                cameraYaw = transform.eulerAngles.y;
                cameraPitch = Mathf.DeltaAngle(0f, transform.eulerAngles.x);
            }
            cameraYaw += lookInput.x * lookSensitivity;
            cameraPitch = Mathf.Clamp(cameraPitch - lookInput.y * lookSensitivity,
                normalProfile.minimumPitch, normalProfile.maximumPitch);
            targetingController?.ReportAimInput(lookInput);
            currentLockHeight = Mathf.Lerp(currentLockHeight, 0f,
                1f - Mathf.Exp(-deltaTime / Mathf.Max(0.01f, hardLockOrbitResponse)));
        }
        wasHardLocked = hardLocked;
        cameraTarget.SetPositionAndRotation(target.position + Vector3.up * currentLockHeight,
            Quaternion.Euler(cameraPitch, cameraYaw, 0f));
    }

    void UpdateMovementFeedback(float deltaTime)
    {
        bool active = movementFeedback && movementController != null && movementController.isActiveAndEnabled;
        float desiredFov = active && movementController.IsQuickBoosting
            ? quickBoostFovCurve.Evaluate(movementController.QuickBoostProgress) * quickBoostFovKick
            : active && movementController.IsAssaultBoosting ? assaultBoostFovKick : 0f;
        movementFovOffset = Mathf.Lerp(movementFovOffset, desiredFov, 1f - Mathf.Exp(-deltaTime / 0.08f));
        float desiredFraming = active && movementController.IsAssaultBoosting ? 1f : 0f;
        assaultFraming = Mathf.Lerp(assaultFraming, desiredFraming, 1f - Mathf.Exp(-deltaTime / 0.2f));
    }

    static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void OnValidate()
    {
        lookSensitivity = Mathf.Max(0f, lookSensitivity);
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
    }
}
