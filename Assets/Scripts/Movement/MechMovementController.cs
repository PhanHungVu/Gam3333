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
    RetrievalSpeedBuff retrievalBuff;
    [SerializeField] float sprintSpeed = 12f;
    [Header("Automatic Run")]
    [SerializeField, Min(0f)] float autoRunDelay = 5f;
    [SerializeField] float acceleration = 20f;
    [SerializeField] float deceleration = 32f;
    [SerializeField] float boostAcceleration = 28f;
    [SerializeField] float rotationSharpness = 12f;
    [SerializeField] bool faceCameraDirection = true;

    [Header("Quick Boost")]
    [SerializeField] float quickBoostPeakSpeed = 50f;
    [SerializeField] float quickBoostDuration = 0.25f;
    [SerializeField] float quickBoostRecovery = 0.4f;
    [SerializeField] float quickBoostDistanceMultiplier = 1f;
    [SerializeField] float quickBoostDurationMultiplier = 1f;
    [Tooltip("Normal movement returns over the final fraction of the dash. Its burst direction remains committed.")]
    [SerializeField, Range(.5f, .99f)] float quickBoostExitBlendStart = .8f;
    [SerializeField] AnimationCurve quickBoostCurve = new AnimationCurve(
        new Keyframe(0f, .95f), new Keyframe(.1f, 1f), new Keyframe(.3f, .95f),
        new Keyframe(.5f, .75f), new Keyframe(.75f, .4f), new Keyframe(1f, 0f));

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
    bool hitStopDash, hitStopJump;
    public void ClearHitStopInputBuffer() { hitStopDash=false; hitStopJump=false; }
    Vector3 externalVelocity;
    float externalRemaining, externalDuration;
    public bool IsKnockedBack => externalRemaining > 0;
    public Vector3 ExternalVelocity => externalRemaining > 0 ? externalVelocity * (externalRemaining / externalDuration) : Vector3.zero;

    // A bounded additive velocity, integrated by the same collision-aware CharacterController.Move.
    public bool ApplyKnockback(Vector3 direction, float speed, float duration)
    {
        if (!isActiveAndEnabled || health != null && health.IsDead || !Finite(direction.x) || !Finite(direction.y) || !Finite(direction.z)
            || !Finite(speed) || !Finite(duration) || speed <= 0 || duration <= 0) return false;
        direction = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (direction.sqrMagnitude < .0001f) return false;
        externalVelocity = direction.normalized * Mathf.Min(speed, 40);
        externalRemaining = externalDuration = Mathf.Min(duration, 1);
        NotifyCombatAction(); return true;
    }
    public void ClearExternalMotion() { externalVelocity = Vector3.zero; externalRemaining = 0; }
    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    Vector3 quickBoostDirection;
    float verticalVelocity;
    float quickBoostTimer;
    float quickBoostRecoveryTimer;
    const int BoostIntegrationSteps = 128;
    readonly float[] boostIntegral = new float[BoostIntegrationSteps + 1];
    readonly float[] exitIntegral = new float[BoostIntegrationSteps + 1];
    float quickBoostActiveDuration;
    public Vector3 QuickBoostStartPosition { get; private set; }
    public bool QuickBoostHasGroundContact { get; private set; }
    public Vector3 QuickBoostGroundPoint { get; private set; }
    public Vector3 QuickBoostGroundNormal { get; private set; }
    bool hasGroundSample;
    bool locomotionBoostActive;
    LocomotionModule locomotionModule;
    CombatHealth health;
    float uninterruptedMoveTime;
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
        if (retrievalBuff == null) retrievalBuff = GetComponent<RetrievalSpeedBuff>();
        float bonus = retrievalBuff != null ? retrievalBuff.MovementMultiplier : 1f;
        settings.moveSpeed *= bonus;
        settings.sprintSpeed *= bonus;
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
        ? Mathf.Clamp01(1f - quickBoostTimer / (quickBoostActiveDuration > 0 ? quickBoostActiveDuration : ActiveQuickBoostDuration)) : 0f;
    // Includes the active boost: this is time until another boost may start.
    public float QuickBoostCooldownRemaining => quickBoostRecoveryTimer;
    public bool CanQuickBoost => isActiveAndEnabled && (CurrentModule == null || CurrentModule.CanQuickBoost(IsGrounded))
        && !IsQuickBoosting && quickBoostRecoveryTimer <= 0f;
    public bool IsAssaultBoosting => assaultBoostActive;
    public bool IsAutoRunning { get; private set; }
    public float AutoRunProgress => autoRunDelay > 0 ? Mathf.Clamp01(uninterruptedMoveTime / autoRunDelay) : IsAutoRunning ? 1 : 0;

    // Successful attacks and incoming damage both end the uninterrupted walk.
    public void NotifyCombatAction()
    {
        uninterruptedMoveTime = 0; IsAutoRunning = false;
        if (!IsQuickBoosting && !assaultBoostActive)
            planarVelocity = Vector3.ClampMagnitude(planarVelocity, WalkSpeed);
        if (State == MovementState.GroundBoost) SetState(IsGrounded ? MovementState.Grounded : MovementState.Airborne, 0, AnimationData.AimDirection);
    }
    void OnDamaged(float damage) => NotifyCombatAction();
    void OnEnable()
    {
        health = GetComponent<CombatHealth>();
        if (health != null) health.Damaged += OnDamaged;
    }

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
        Part.ConfigureCharacterCollisions(gameObject);
        targetingController = GetComponent<MechTargetLockController>();
        if (energy == null)
            energy = GetComponent<MechEnergyController>();
        if (gameplayCamera == null)
            gameplayCamera = Camera.main;
        PublishAnimationData(0f, transform.forward);
    }

    void OnDisable()
    {
        if (health != null) health.Damaged -= OnDamaged;
        uninterruptedMoveTime = 0; IsAutoRunning = false;
        bool wasAssaultBoosting = assaultBoostActive;
        assaultBoostActive = false;
        assaultBoostRequiresRelease = false;
        brakingAfterBoost = false;
        bool wasBoosting = IsQuickBoosting;
        quickBoostTimer = 0f;
        quickBoostRecoveryTimer = 0f;
        planarVelocity = Vector3.zero;
        ClearHitStopInputBuffer();
        ClearExternalMotion();
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

        if (CombatHitStop.IsFrozen(transform))
        {
            hitStopDash |= input.quickBoost; hitStopJump |= input.jump; return;
        }
        input.quickBoost |= hitStopDash; input.jump |= hitStopJump; ClearHitStopInputBuffer();

        input.move = Vector2.ClampMagnitude(input.move, 1f);
        MechLocomotionSettings settings = Settings;
        bool wasGrounded = IsGrounded;
        float previousYaw = transform.eulerAngles.y;
        Vector3 movementDirection = GetCameraRelativeDirection(input.move, cameraTransform);
        MoveDirection = movementDirection;
        bool walking = !IsKnockedBack && movementDirection.sqrMagnitude > .001f && CanAssaultBoostHere()
            && !IsQuickBoosting && !assaultBoostActive && !input.quickBoost && !input.assaultBoost;
        bool wasRunning = IsAutoRunning;
        if (!walking || Vector3.ProjectOnPlane(Velocity, Vector3.up).sqrMagnitude < .01f)
        { uninterruptedMoveTime = 0; IsAutoRunning = false; }
        else
        { uninterruptedMoveTime += deltaTime; IsAutoRunning = uninterruptedMoveTime >= autoRunDelay; }
        if (wasRunning && !IsAutoRunning && !IsQuickBoosting && !assaultBoostActive)
            planarVelocity = Vector3.ClampMagnitude(planarVelocity, settings.moveSpeed);
        input.sprint = IsAutoRunning;
        quickBoostRecoveryTimer = Mathf.Max(0f, quickBoostRecoveryTimer - deltaTime);
        if (quickBoostRecoveryTimer < .000001f) quickBoostRecoveryTimer = 0;
        bool boostStarted = input.quickBoost && CanQuickBoost;
        bool wasAssaultBoosting = assaultBoostActive;
        if (boostStarted)
        {
            StartQuickBoost(movementDirection, cameraTransform);
            quickBoostRecoveryTimer = Mathf.Max(0f, quickBoostRecoveryTimer - deltaTime);
        }
        UpdateAssaultBoost(input, movementDirection, cameraTransform, deltaTime);

        if (wasAssaultBoosting && !assaultBoostActive)
            brakingAfterBoost = true;
        if (IsQuickBoosting || assaultBoostActive)
            brakingAfterBoost = false;

        Vector3 planarStep = StepPlanarMotion(input.sprint, movementDirection, settings, deltaTime, out bool boostEnded);
        UpdateFacing(movementDirection, cameraTransform, settings.rotationSharpness, deltaTime);
        verticalVelocity = CurrentModule != null
            ? CurrentModule.VerticalVelocity(input, verticalVelocity, IsGrounded,
                ProbeGround(CurrentModule.GroundProbeDistance), settings, deltaTime)
            : LocomotionModule.GroundVerticalVelocity(input.jump, verticalVelocity, IsGrounded, settings, deltaTime);

        // Integrate the linear decay exactly over this step, including a frame longer than the impulse.
        float impulseStep = Mathf.Min(deltaTime, externalRemaining);
        float impulseWeight = externalDuration > 0 ? (externalRemaining - impulseStep * .5f) / externalDuration : 0;
        Vector3 externalStep = externalVelocity * (impulseStep * impulseWeight);
        externalRemaining = Mathf.Max(0, externalRemaining - deltaTime);
        Vector3 previousPosition = transform.position;
        CollisionFlags collisions = characterController.Move(planarStep + Vector3.up * (verticalVelocity * deltaTime) + externalStep);
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

        locomotionBoostActive = input.sprint && movementDirection.sqrMagnitude > 0.001f && CanAssaultBoostHere();
        SetState(IsQuickBoosting ? MovementState.QuickBoost
            : assaultBoostActive ? MovementState.AssaultBoost
            : !IsGrounded ? MovementState.Airborne
            : input.sprint && movementDirection.sqrMagnitude > 0.001f ? MovementState.GroundBoost
            : MovementState.Grounded, Mathf.DeltaAngle(previousYaw, transform.eulerAngles.y) / deltaTime,
            GetAimDirection(cameraTransform));

        // Publish after movement so effects can read this frame's state and velocity.
        if (boostStarted)
            QuickBoostStarted?.Invoke(quickBoostDirection);
        if (boostEnded)
            QuickBoostEnded?.Invoke();
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
        if (assaultBoostActive)
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

    Vector3 StepPlanarMotion(bool sprinting, Vector3 requested, MechLocomotionSettings settings, float deltaTime, out bool ended)
    {
        ended = false;
        if (!IsQuickBoosting)
        { UpdatePlanarVelocity(sprinting, requested, settings, deltaTime); return planarVelocity * deltaTime; }
        if (quickBoostActiveDuration <= 0) RebuildBoostIntegral();
        float activeStep = Mathf.Min(deltaTime, quickBoostTimer);
        float start = Mathf.Clamp01(1 - quickBoostTimer / quickBoostActiveDuration);
        float finish = Mathf.Clamp01(start + activeStep / quickBoostActiveDuration);
        Vector3 exit = requested * settings.moveSpeed;
        Vector3 step = quickBoostDirection * (quickBoostPeakSpeed * quickBoostDistanceMultiplier * quickBoostActiveDuration
            * (IntegralAt(boostIntegral, finish) - IntegralAt(boostIntegral, start)))
            + exit * (quickBoostActiveDuration * (IntegralAt(exitIntegral, finish) - IntegralAt(exitIntegral, start)));
        float blend = ExitBlend(finish);
        planarVelocity = quickBoostDirection * (quickBoostPeakSpeed * quickBoostDistanceMultiplier * CurveSpeed(finish) * (1 - blend)) + exit * blend;
        quickBoostTimer = Mathf.Max(0, quickBoostTimer - activeStep);
        if (quickBoostTimer <= .000001f)
        {
            quickBoostTimer = 0; ended = true; brakingAfterBoost = true; planarVelocity = exit;
            float remainder = deltaTime - activeStep;
            if (remainder > 0) { UpdatePlanarVelocity(sprinting, requested, settings, remainder); step += planarVelocity * remainder; }
        }
        return step;
    }
    float CurveSpeed(float progress) => Mathf.Clamp01(quickBoostCurve != null ? quickBoostCurve.Evaluate(progress) : 1 - progress);
    float ExitBlend(float progress) => Mathf.SmoothStep(0, 1, Mathf.InverseLerp(quickBoostExitBlendStart, 1, progress));
    void RebuildBoostIntegral()
    {
        quickBoostActiveDuration = ActiveQuickBoostDuration; boostIntegral[0] = exitIntegral[0] = 0;
        float previousBlend = ExitBlend(0), previous = CurveSpeed(0) * (1 - previousBlend);
        for (int i = 1; i <= BoostIntegrationSteps; i++)
        {
            float progress = i / (float)BoostIntegrationSteps, blend = ExitBlend(progress);
            float speed = CurveSpeed(progress) * (1 - blend);
            boostIntegral[i] = boostIntegral[i - 1] + (previous + speed) * (.5f / BoostIntegrationSteps);
            exitIntegral[i] = exitIntegral[i - 1] + (previousBlend + blend) * (.5f / BoostIntegrationSteps);
            previous = speed; previousBlend = blend;
        }
    }
    static float IntegralAt(float[] samples, float progress)
    {
        float position = Mathf.Clamp01(progress) * BoostIntegrationSteps;
        int index = Mathf.Min(BoostIntegrationSteps - 1, Mathf.FloorToInt(position));
        return Mathf.Lerp(samples[index], samples[index + 1], position - index);
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
                distance + bounds.extents.y, Part.MovementQueryMask, QueryTriggerInteraction.Ignore);
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
            assaultBoostDirection = movementDirection.sqrMagnitude > 0.001f
                ? movementDirection.normalized : GetCameraForward(cameraTransform);
            assaultBoostActive = true;
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
        QuickBoostStartPosition = transform.position;
        QuickBoostHasGroundContact = TryGetGroundContact(out var contact);
        QuickBoostGroundPoint = QuickBoostHasGroundContact ? contact.point : transform.position;
        QuickBoostGroundNormal = QuickBoostHasGroundContact ? contact.normal : Vector3.up;
        quickBoostDirection = movementDirection.sqrMagnitude > 0.001f
            ? movementDirection.normalized
            : GetCameraForward(cameraTransform);
        if (CurrentModule != null)
            quickBoostDirection = CurrentModule.QuickBoostDirection(quickBoostDirection,
                Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized);
        RebuildBoostIntegral();
        quickBoostTimer = quickBoostActiveDuration;
        quickBoostRecoveryTimer = quickBoostTimer + Mathf.Max(quickBoostRecovery, 0f);
    }

    public bool TryGetGroundContact(out RaycastHit contact, float maximumDistance = .3f)
        => TryGetGroundContact(transform.position, out contact, maximumDistance);

    // Presentation can sample a position along the resolved movement path between frames.
    public bool TryGetGroundContact(Vector3 rootPosition, out RaycastHit contact, float maximumDistance = .3f)
    {
        contact = default;
        if (characterController == null || !characterController.enabled) return false;
        var bounds = characterController.bounds;
        int count;
        while ((count = Physics.RaycastNonAlloc(bounds.center + (rootPosition - transform.position), Vector3.down, groundHits,
            bounds.extents.y + maximumDistance + characterController.skinWidth, Part.MovementQueryMask, QueryTriggerInteraction.Ignore)) == groundHits.Length)
            Array.Resize(ref groundHits, groundHits.Length * 2);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var hit = groundHits[i];
            if (hit.distance >= nearest || hit.normal.y < .5f || hit.transform.IsChildOf(transform)
                || hit.collider.GetComponentInParent<EnemyAgent>() != null || hit.collider.GetComponentInParent<MechMovementController>() != null) continue;
            nearest = hit.distance; contact = hit;
        }
        return nearest < float.PositiveInfinity;
    }

    float ActiveQuickBoostDuration => Mathf.Max(0.01f, quickBoostDuration * quickBoostDurationMultiplier);

    void OnValidate()
    {
        autoRunDelay = Mathf.Max(0f, autoRunDelay);
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
        quickBoostExitBlendStart = Mathf.Clamp(quickBoostExitBlendStart, .5f, .99f);
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
