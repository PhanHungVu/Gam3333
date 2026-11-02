using System;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[DisallowMultipleComponent]
public class MechMovementController : MonoBehaviour
{
    public enum MovementState { Grounded, GroundBoost, Airborne, QuickBoost, AssaultBoost }

    [SerializeField] Camera gameplayCamera;
    [SerializeField] MechEnergyController energy;
    [Header("Ground Locomotion")]
    [Tooltip("Walking speed used only when the equipped legs have no Speed stat (legs' Speed sets it otherwise).")]
    [SerializeField] float moveSpeed = 6f;
    [Tooltip("Read-only: the walking speed right now, after legs and part Speed modifiers.")]
    [SerializeField] float currentWalkSpeed;
    MechStats stats;
    [SerializeField] float sprintSpeed = 12f;
    [SerializeField] float acceleration = 20f;
    [SerializeField] float deceleration = 32f;
    [SerializeField] float boostAcceleration = 28f;
    [SerializeField] float rotationSharpness = 12f;
    [SerializeField] bool faceCameraDirection = true;

    [Header("Quick Boost")]
    [SerializeField] float quickBoostPeakSpeed = 24f;
    [SerializeField] float quickBoostDuration = 0.22f;
    [SerializeField] float quickBoostRecovery = 0.35f;
    [SerializeField] float quickBoostDistanceMultiplier = 1.75f;
    [SerializeField] float quickBoostDurationMultiplier = 1.35f;
    [SerializeField] AnimationCurve quickBoostCurve = new AnimationCurve(
        new Keyframe(0f, 0.65f),
        new Keyframe(0.12f, 1f),
        new Keyframe(0.55f, 0.75f),
        new Keyframe(1f, 0.1f));

    [Header("Ground Assault Boost")]
    [SerializeField, Min(0f)] float assaultBoostSpeed = 28f;
    [SerializeField, Min(0f)] float assaultBoostAcceleration = 45f;
    [SerializeField, Min(0f)] float assaultBoostTurnRate = 90f;
    [SerializeField, Min(0f)] float assaultBoostEnergyPerSecond = 18f;
    [SerializeField, Min(0f)] float assaultBoostMinimumEnergy = 10f;
    [SerializeField, Min(0f)] float quickBoostEnergyCost = 20f;

    [Header("Boost Stopping")]
    [Tooltip("How quickly dash/boost momentum settles to the requested walking velocity after the boost ends.")]
    [SerializeField, Min(0f)] float boostStopDeceleration = 64f;

    [Header("Ground Contact and Existing Jump")]
    [SerializeField] float gravity = -25f;
    [SerializeField] float jumpHeight = 1.5f;

    CharacterController characterController;
    MechTargetLockController targetingController;
    Vector3 assaultBoostDirection;
    bool assaultBoostActive;
    bool assaultBoostRequiresRelease;
    bool brakingAfterBoost;
    Vector3 planarVelocity;
    Vector3 quickBoostDirection;
    float verticalVelocity;
    float quickBoostTimer;
    float quickBoostRecoveryTimer;
    bool hasGroundSample;
    bool locomotionBoostActive;
    LocomotionModule locomotionModule;
    RaycastHit[] groundHits = new RaycastHit[16];

    public LocomotionModule CurrentModule => locomotionModule != null && locomotionModule.isActiveAndEnabled
        ? locomotionModule : null;
    public LocomotionType LocomotionType => CurrentModule != null ? CurrentModule.Type : LocomotionType.Biped;
    public LocomotionAnimationData AnimationData { get; private set; }
    public event Action<LocomotionAnimationData> AnimationDataUpdated;

    // Attachment changes never reset velocity, input instances, energy, or boost timers.
    public void SetLocomotionModule(LocomotionModule module)
    {
        locomotionModule = module;
        PublishAnimationData(AnimationData.TurnRate, AnimationData.AimDirection);
    }

    MechLocomotionSettings Settings
    {
        get
        {
            var defaults = new MechLocomotionSettings
            {
                moveSpeed = moveSpeed, sprintSpeed = sprintSpeed, acceleration = acceleration,
                deceleration = deceleration, boostAcceleration = boostAcceleration,
                rotationSharpness = rotationSharpness, gravity = gravity, jumpHeight = jumpHeight
            };
            var configured = CurrentModule != null ? CurrentModule.Configure(defaults) : defaults;
            return ApplyPartSpeed(configured);
        }
    }

    // Walking speed from the equipped parts (MechStats): the legs' Speed in m/s, or this Move Speed
    // when the legs have none, times every other part's Speed percentage. Sprint scales the same way.
    MechLocomotionSettings ApplyPartSpeed(MechLocomotionSettings settings)
    {
        if (stats == null) stats = GetComponent<MechStats>();
        if (stats != null)
        {
            float baseSpeed = stats.BaseSpeed > 0f ? stats.BaseSpeed : settings.moveSpeed;
            float target = baseSpeed * stats.SpeedMultiplier;
            float scale = settings.moveSpeed > 0f ? target / settings.moveSpeed : 1f;
            settings.moveSpeed = target;
            settings.sprintSpeed *= scale;
        }
        currentWalkSpeed = settings.moveSpeed;
        return settings;
    }

    // The walking speed the mech moves at right now, after legs and part modifiers.
    public float WalkSpeed => Settings.moveSpeed;

    public MovementState State { get; private set; } = MovementState.Airborne;
    public Vector3 Velocity { get; private set; }
    public Vector3 MoveDirection { get; private set; }
    public Vector3 FacingDirection { get; private set; }
    public bool IsGrounded => characterController != null && characterController.isGrounded;
    public bool IsQuickBoosting => quickBoostTimer > 0f;
    public float QuickBoostProgress => IsQuickBoosting
        ? Mathf.Clamp01(1f - quickBoostTimer / ActiveQuickBoostDuration) : 0f;
    // Includes the active boost: this is time until another boost may start.
    public float QuickBoostCooldownRemaining => quickBoostRecoveryTimer;
    public bool CanQuickBoost => isActiveAndEnabled && (CurrentModule != null ? CurrentModule.CanQuickBoost(IsGrounded) : IsGrounded)
        && !IsQuickBoosting && quickBoostRecoveryTimer <= 0f
        && (energy == null || energy.CanSpend(quickBoostEnergyCost));
    public bool IsAssaultBoosting => assaultBoostActive;

    public event Action<MovementState, MovementState> StateChanged;
    public event Action<Vector3> QuickBoostStarted;
    // Fired for both completion and cancellation when this controller is disabled.
    public event Action QuickBoostEnded;
    // Positive downward speed immediately before ground contact, in world units/second.
    public event Action<float> Landed;
    public event Action AssaultBoostStarted;
    public event Action AssaultBoostEnded;

    public void BindCamera(Camera camera) => gameplayCamera = camera;

    void Awake()
    {
        characterController = GetComponent<CharacterController>();
        targetingController = GetComponent<MechTargetLockController>();
        if (energy == null)
            energy = GetComponent<MechEnergyController>();
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;
        PublishAnimationData(0f, transform.forward);
    }

    void OnDisable()
    {
        bool wasAssaultBoosting = assaultBoostActive;
        assaultBoostActive = false;
        assaultBoostRequiresRelease = false;
        brakingAfterBoost = false;
        bool wasBoosting = IsQuickBoosting;
        quickBoostTimer = 0f;
        quickBoostRecoveryTimer = 0f;
        planarVelocity = Vector3.zero;
        verticalVelocity = 0f;
        Velocity = MoveDirection = FacingDirection = Vector3.zero;
        hasGroundSample = false;
        locomotionBoostActive = false;
        SetState(IsGrounded ? MovementState.Grounded : MovementState.Airborne, 0f, AnimationData.AimDirection);
        if (wasBoosting)
            QuickBoostEnded?.Invoke();
        if (wasAssaultBoosting)
            AssaultBoostEnded?.Invoke();
    }

    // Called each frame by MechPlayerInput with the player's input, using this mech's gameplay camera.
    public void Tick(MechLocomotionInput input, float deltaTime)
    {
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;
        Tick(input, gameplayCamera != null ? gameplayCamera.transform : null, deltaTime);
    }

    // Public command boundary also supports AI/replay/test callers without device input.
    public void Tick(MechLocomotionInput input, Transform cameraTransform, float deltaTime)
    {
        if (deltaTime <= 0f || !isActiveAndEnabled || !characterController.enabled)
            return;

        // Retain the command field for existing replay/AI callers, but sprint
        // no longer changes speed or state. Dash is a separate pressed action.
        input.sprint = false;
        input.move = Vector2.ClampMagnitude(input.move, 1f);
        MechLocomotionSettings settings = Settings;
        bool wasGrounded = IsGrounded;
        float previousYaw = transform.eulerAngles.y;
        Vector3 movementDirection = GetCameraRelativeDirection(input.move, cameraTransform);
        MoveDirection = movementDirection;
        bool boostEnded = UpdateQuickBoostTimers(deltaTime);
        bool boostStarted = input.quickBoost && CanQuickBoost;
        bool wasAssaultBoosting = assaultBoostActive;
        if (boostStarted)
        {
            energy?.TrySpend(quickBoostEnergyCost);
            StartQuickBoost(movementDirection, cameraTransform);
        }
        UpdateAssaultBoost(input, movementDirection, cameraTransform, deltaTime);

        if (boostEnded || wasAssaultBoosting && !assaultBoostActive)
            brakingAfterBoost = true;
        if (IsQuickBoosting || assaultBoostActive)
            brakingAfterBoost = false;

        UpdatePlanarVelocity(input.sprint, movementDirection, settings, deltaTime);
        UpdateFacing(movementDirection, cameraTransform, settings.rotationSharpness, deltaTime);
        verticalVelocity = CurrentModule != null
            ? CurrentModule.VerticalVelocity(input, verticalVelocity, IsGrounded,
                ProbeGround(CurrentModule.GroundProbeDistance), settings, deltaTime)
            : LocomotionModule.GroundVerticalVelocity(input.jump, verticalVelocity, IsGrounded, settings, deltaTime);

        Vector3 velocity = planarVelocity + Vector3.up * verticalVelocity;
        Vector3 previousPosition = transform.position;
        CollisionFlags collisions = characterController.Move(velocity * deltaTime);
        Velocity = (transform.position - previousPosition) / deltaTime;
        float landingSpeed = Mathf.Max(0f, -verticalVelocity);
        if ((collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
            verticalVelocity = 0f;
        if (assaultBoostActive && (!CanAssaultBoostHere()
            || ((collisions & CollisionFlags.Sides) != 0
                && Vector3.ProjectOnPlane(Velocity, Vector3.up).magnitude < 1f)))
        {
            assaultBoostActive = false;
            assaultBoostRequiresRelease = true;
            brakingAfterBoost = true;
        }
        energy?.TickRecovery(deltaTime, !IsQuickBoosting && !assaultBoostActive);

        locomotionBoostActive = input.sprint && movementDirection.sqrMagnitude > 0.001f && CanAssaultBoostHere();
        SetState(IsQuickBoosting ? MovementState.QuickBoost
            : assaultBoostActive ? MovementState.AssaultBoost
            : !IsGrounded ? MovementState.Airborne
            : input.sprint && movementDirection.sqrMagnitude > 0.001f ? MovementState.GroundBoost
            : MovementState.Grounded, Mathf.DeltaAngle(previousYaw, transform.eulerAngles.y) / deltaTime,
            GetAimDirection(cameraTransform));

        // Publish after movement so effects can read this frame's state and velocity.
        if (boostEnded)
            QuickBoostEnded?.Invoke();
        if (boostStarted)
            QuickBoostStarted?.Invoke(quickBoostDirection);
        if (wasAssaultBoosting && !assaultBoostActive)
            AssaultBoostEnded?.Invoke();
        else if (!wasAssaultBoosting && assaultBoostActive)
            AssaultBoostStarted?.Invoke();
        if (hasGroundSample && !wasGrounded && IsGrounded)
            Landed?.Invoke(landingSpeed);
        hasGroundSample = true;
    }

    void UpdatePlanarVelocity(bool sprinting, Vector3 movementDirection, MechLocomotionSettings settings, float deltaTime)
    {
        if (IsQuickBoosting)
        {
            planarVelocity = quickBoostDirection * (quickBoostCurve.Evaluate(QuickBoostProgress)
                * quickBoostPeakSpeed * quickBoostDistanceMultiplier);
        }
        else if (assaultBoostActive)
        {
            planarVelocity = Vector3.MoveTowards(planarVelocity, assaultBoostDirection * assaultBoostSpeed,
                assaultBoostAcceleration * deltaTime);
        }
        else
        {
            if (brakingAfterBoost)
            {
                // Recover movement control promptly even when walking or changing
                // direction after a dash. Normal module tuning resumes at the target.
                settings.acceleration = Mathf.Max(settings.acceleration, boostStopDeceleration);
                settings.deceleration = Mathf.Max(settings.deceleration, boostStopDeceleration);
            }
            planarVelocity = CurrentModule != null
                ? CurrentModule.PlanarVelocity(planarVelocity, movementDirection, sprinting, settings, deltaTime)
                : LocomotionModule.GroundPlanarVelocity(planarVelocity, movementDirection, sprinting, settings, deltaTime);
            if ((planarVelocity - movementDirection * settings.moveSpeed).sqrMagnitude < 0.0001f)
                brakingAfterBoost = false;
        }
    }

    void UpdateFacing(Vector3 movementDirection, Transform cameraTransform, float turnSharpness, float deltaTime)
    {
        Vector3 facingDirection = IsQuickBoosting
            ? quickBoostDirection
            : assaultBoostActive ? assaultBoostDirection
            : GetDesiredFacingDirection(movementDirection, cameraTransform);
        // Hard lock changes facing only. Input axes, acceleration, and the captured
        // Quick Boost direction remain entirely under the locomotion code above.
        if (targetingController != null && targetingController.IsHardLocked)
            facingDirection = Vector3.ProjectOnPlane(
                targetingController.CurrentTarget.AimPosition - transform.position, Vector3.up).normalized;
        FacingDirection = facingDirection;
        if (facingDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation,
                1f - Mathf.Exp(-turnSharpness * deltaTime));
        }
    }

    float ProbeGround(float distance)
    {
        if (distance <= 0f) return float.PositiveInfinity;
        Bounds bounds = characterController.bounds;
        int count;
        while (true)
        {
            count = Physics.RaycastNonAlloc(bounds.center, Vector3.down, groundHits,
                distance + bounds.extents.y, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count < groundHits.Length) break;
            Array.Resize(ref groundHits, groundHits.Length * 2);
        }
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = groundHits[i];
            if (hit.transform.IsChildOf(transform) || hit.normal.y < 0.5f) continue;
            nearest = Mathf.Min(nearest, Mathf.Max(0f, hit.distance - bounds.extents.y));
        }
        return nearest;
    }

    bool CanAssaultBoostHere() => CurrentModule != null ? CurrentModule.CanAssaultBoost(IsGrounded) : IsGrounded;

    void UpdateAssaultBoost(MechLocomotionInput input, Vector3 movementDirection,
        Transform cameraTransform, float deltaTime)
    {
        if (!input.assaultBoost)
            assaultBoostRequiresRelease = false;
        if (!input.assaultBoost || !CanAssaultBoostHere()
            || input.jump && (CurrentModule == null || CurrentModule.JumpCancelsAssaultBoost) || IsQuickBoosting)
        {
            if (assaultBoostActive && input.assaultBoost)
                assaultBoostRequiresRelease = true;
            assaultBoostActive = false;
            return;
        }
        if (assaultBoostRequiresRelease)
            return;
        if (!assaultBoostActive)
        {
            if (energy != null && !energy.CanSpend(assaultBoostMinimumEnergy))
            {
                assaultBoostRequiresRelease = true;
                return;
            }
            assaultBoostDirection = movementDirection.sqrMagnitude > 0.001f
                ? movementDirection.normalized : GetCameraForward(cameraTransform);
            assaultBoostActive = true;
        }
        if (energy != null && !energy.TrySpend(assaultBoostEnergyPerSecond * deltaTime))
        {
            assaultBoostActive = false;
            assaultBoostRequiresRelease = true;
            return;
        }
        Vector3 steering = movementDirection.sqrMagnitude > 0.001f
            ? movementDirection.normalized : GetCameraForward(cameraTransform);
        assaultBoostDirection = Vector3.RotateTowards(assaultBoostDirection, steering,
            assaultBoostTurnRate * Mathf.Deg2Rad * deltaTime, 0f).normalized;
    }

    void SetState(MovementState nextState, float turnRate, Vector3 aimDirection)
    {
        MovementState previousState = State;
        State = nextState;
        PublishAnimationData(turnRate, aimDirection);
        if (previousState != nextState) StateChanged?.Invoke(previousState, nextState);
    }

    void StartQuickBoost(Vector3 movementDirection, Transform cameraTransform)
    {
        quickBoostDirection = movementDirection.sqrMagnitude > 0.001f
            ? movementDirection.normalized
            : GetCameraForward(cameraTransform);
        if (CurrentModule != null)
            quickBoostDirection = CurrentModule.QuickBoostDirection(quickBoostDirection,
                Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized);
        quickBoostTimer = Mathf.Max(ActiveQuickBoostDuration, 0.01f);
        quickBoostRecoveryTimer = quickBoostTimer + Mathf.Max(quickBoostRecovery, 0f);
    }

    bool UpdateQuickBoostTimers(float deltaTime)
    {
        bool wasBoosting = IsQuickBoosting;
        quickBoostTimer = Mathf.Max(0f, quickBoostTimer - deltaTime);
        quickBoostRecoveryTimer = Mathf.Max(0f, quickBoostRecoveryTimer - deltaTime);
        return wasBoosting && !IsQuickBoosting;
    }

    float ActiveQuickBoostDuration => Mathf.Max(0.01f, quickBoostDuration * quickBoostDurationMultiplier);

    void OnValidate()
    {
        moveSpeed = Mathf.Max(0f, moveSpeed);
        sprintSpeed = Mathf.Max(0f, sprintSpeed);
        acceleration = Mathf.Max(0f, acceleration);
        deceleration = Mathf.Max(0f, deceleration);
        boostStopDeceleration = Mathf.Max(0f, boostStopDeceleration);
        boostAcceleration = Mathf.Max(0f, boostAcceleration);
        rotationSharpness = Mathf.Max(0f, rotationSharpness);
        gravity = Mathf.Min(0f, gravity);
        jumpHeight = Mathf.Max(0f, jumpHeight);
        assaultBoostSpeed = Mathf.Max(0f, assaultBoostSpeed);
        assaultBoostAcceleration = Mathf.Max(0f, assaultBoostAcceleration);
        assaultBoostTurnRate = Mathf.Max(0f, assaultBoostTurnRate);
        assaultBoostEnergyPerSecond = Mathf.Max(0f, assaultBoostEnergyPerSecond);
        assaultBoostMinimumEnergy = Mathf.Max(0f, assaultBoostMinimumEnergy);
        quickBoostEnergyCost = Mathf.Max(0f, quickBoostEnergyCost);
        quickBoostPeakSpeed = Mathf.Max(0f, quickBoostPeakSpeed);
        quickBoostDuration = Mathf.Max(0.01f, quickBoostDuration);
        quickBoostRecovery = Mathf.Max(0f, quickBoostRecovery);
        quickBoostDistanceMultiplier = Mathf.Max(1f, quickBoostDistanceMultiplier);
        quickBoostDurationMultiplier = Mathf.Max(1f, quickBoostDurationMultiplier);
    }

    Vector3 GetDesiredFacingDirection(Vector3 movementDirection, Transform cameraTransform)
    {
        if (movementDirection.sqrMagnitude < 0.001f)
            return Vector3.zero;

        return faceCameraDirection
            ? GetCameraForward(cameraTransform)
            : movementDirection;
    }

    static Vector3 GetCameraRelativeDirection(Vector2 input, Transform cameraTransform)
    {
        if (cameraTransform == null)
            return Vector3.ClampMagnitude(new Vector3(input.x, 0f, input.y), 1f);

        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
        return Vector3.ClampMagnitude(right * input.x + forward * input.y, 1f);
    }

    Vector3 GetCameraForward(Transform cameraTransform)
    {
        if (cameraTransform == null)
            return Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;

        Vector3 forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
        return forward.sqrMagnitude > 0.001f ? forward : transform.forward;
    }

    Vector3 GetAimDirection(Transform cameraTransform)
    {
        if (targetingController != null && !targetingController.IsFreeAim && targetingController.CurrentTarget != null)
            return (targetingController.CurrentTarget.AimPosition - transform.position).normalized;
        return cameraTransform != null ? cameraTransform.forward : transform.forward;
    }

    void PublishAnimationData(float turnRate, Vector3 aimDirection)
    {
        // Ground contact includes a small downward stick velocity. Hide that only in animation data.
        Vector3 velocity = Velocity;
        if (IsGrounded && velocity.y < 0f) velocity.y = 0f;
        if (velocity.sqrMagnitude < 0.0001f) velocity = Vector3.zero;
        AnimationData = new LocomotionAnimationData(LocomotionType, State, velocity, transform.rotation,
            IsGrounded, IsQuickBoosting || IsAssaultBoosting || locomotionBoostActive,
            turnRate, aimDirection);
        AnimationDataUpdated?.Invoke(AnimationData);
    }
}
