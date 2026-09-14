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

    CinemachineCamera cinemachineCamera;
    GameObject cameraRigObject;
    CinemachineThirdPersonFollow thirdPersonFollow;
    CinemachineRotationComposer rotationComposer;
    Transform cameraTarget;
    MechMovementController movementController;
    InputAction lookAction;
    Vector3 previousTargetPosition;
    float currentLateralOffset;
    float cameraYaw;
    float cameraPitch;

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
        if (cameraRigObject != null)
            cameraRigObject.SetActive(true);
        if (target != null)
            previousTargetPosition = target.position;
    }

    void OnDisable()
    {
        lookAction?.Disable();
        if (cameraRigObject != null)
            cameraRigObject.SetActive(false);
        SetCursorLocked(false);
    }

    void LateUpdate()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetCursorLocked(false);
        else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            SetCursorLocked(true);

        if (target == null || cameraTarget == null)
            return;

        UpdateCameraTargetRotation();
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
        // Gameplay targets never influence camera rotation or framing.
        thirdPersonFollow.CameraDistance = normalProfile.distance;
        thirdPersonFollow.CameraSide = 0.5f;
        thirdPersonFollow.VerticalArmLength = shoulderHeight;
        thirdPersonFollow.Damping = movementController != null && movementController.IsQuickBoosting
            ? quickBoostFollowDamping : normalProfile.damping;
        thirdPersonFollow.ShoulderOffset = new Vector3(cameraSide + currentLateralOffset, shoulderHeight, 0f);
        rotationComposer.TargetOffset = normalProfile.targetOffset;
        rotationComposer.Damping = rotationDamping;
        rotationComposer.CenterOnActivate = true;

        ScreenComposerSettings composition = rotationComposer.Composition;
        composition.ScreenPosition = screenPosition;
        composition.DeadZone = new ScreenComposerSettings.DeadZoneSettings
        {
            Enabled = true,
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
        Vector2 lookInput = lookAction != null ? lookAction.ReadValue<Vector2>()
            : Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        cameraYaw += lookInput.x * lookSensitivity;
        cameraPitch = Mathf.Clamp(cameraPitch - lookInput.y * lookSensitivity,
            normalProfile.minimumPitch, normalProfile.maximumPitch);
        cameraTarget.SetPositionAndRotation(target.position, Quaternion.Euler(cameraPitch, cameraYaw, 0f));
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
