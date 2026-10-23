using System;
using UnityEngine;
using UnityEngine.InputSystem;

// What one TryAttack call did: whether an attack started, and the projectile if it fired one.
public readonly struct AttackResult
{
    public readonly bool Started;
    public readonly MechProjectile Projectile;
    public AttackResult(bool started, MechProjectile projectile) { Started = started; Projectile = projectile; }
}

// The mech's combat brain. TryAttack(socket) attacks with whatever part is mounted in that socket,
// choosing the attack from the part's subtype: Projectile and Explosive parts fire (RangedAttack),
// Melee parts swing (MeleeAttack), parts with no weapon use their ability or an animation-only attack.
// Every stat comes from the part's data; ammo and reload live on the part. Presentation listens to
// the events below and never changes gameplay.
[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
[RequireComponent(typeof(EquipManager))]
public class MechCombat : MonoBehaviour
{
    // TEMPORARY: button reading moves to the player input component in its own step.
    [SerializeField] InputActionAsset inputActions;
    [SerializeField] bool readPlayerInput = true;
    public bool ReadPlayerInput { get => readPlayerInput; set => readPlayerInput = value; }
    InputActionAsset runtimeInputActions;
    InputAction rightArmAction, leftArmAction, leftShoulderAction, rightShoulderAction;
    bool inputArmed;

    [Header("Aiming")]
    [SerializeField] Camera gameplayCamera;
    [SerializeField] LayerMask hitLayers = ~0;
    [Tooltip("Muzzle used when a part has no muzzle of its own.")]
    [SerializeField] Vector3 muzzleOffset = new Vector3(0.65f, 0.5f, 0.65f);
    [Tooltip("Material for the plain projectile sphere, used when a part has no projectile model.")]
    [SerializeField] Material combatMaterial;
    [Header("Spiral pattern (parts whose SpiralBarrage stat is on)")]
    [SerializeField] CannonSpiralBarrage.Settings spiralSettings = CannonSpiralBarrage.Settings.Default;
    [Header("Melee (shared hit box; damage and cooldown come from the part)")]
    [SerializeField] Vector3 meleeCenter = new Vector3(0f, 0f, 2.75f);
    [SerializeField] Vector3 meleeSize = new Vector3(5f, 3.5f, 5.5f);
    [SerializeField, Min(0f)] float maximumMeleeApproach = 1f;
    [SerializeField, Range(1f, 60f)] float maximumMeleeAssistAngle = 30f;
    [SerializeField, Min(0.01f)] float meleeApproachDuration = 0.12f;
    [SerializeField, Min(0f)] float meleeCameraHold = 0.45f;

    static readonly PartCategory[] Sockets = (PartCategory[])Enum.GetValues(typeof(PartCategory));
    EquipManager parts;
    MechTargetLockController targeting;
    MechMovementController movement;
    MeleeAttack melee;
    CannonSpiralBarrage barrageRunner;
    Part lastAttacker;
    internal readonly CombatBlast Blast = new CombatBlast();

    public event Action<MechProjectile> ShotFired;
    public event Action<MechProjectileImpact> ProjectileImpact;
    public event Action<int> MeleePerformed;
    public event Action<Part> WeaponAttackStarted;

    public void BindCamera(Camera camera) => gameplayCamera = camera;
    public MeleeAttack Melee => melee;
    public bool MeleeCameraActive => isActiveAndEnabled && melee.CameraTimer > 0f;
    public bool IsMeleeApproaching => melee.Pending;
    public float MeleeExtension => melee.Extension;
    // Muzzle of the part that attacked last (effects play here).
    public Vector3 MuzzlePosition => MuzzleOf(lastAttacker);
    public float MetresPerGolem => parts != null && parts.rulesConfig != null ? parts.rulesConfig.metresPerGolem : 4f;

    // Settings read by MeleeAttack.
    internal LayerMask HitLayers => hitLayers;
    internal Vector3 MeleeCenter => meleeCenter;
    internal Vector3 MeleeSize => meleeSize;
    internal float MaximumMeleeApproach => maximumMeleeApproach;
    internal float MaximumMeleeAssistAngle => maximumMeleeAssistAngle;
    internal float MeleeApproachDuration => meleeApproachDuration;
    internal float MeleeCameraHold => meleeCameraHold;
    internal MechTargetLockController Targeting => targeting;
    internal MechMovementController Movement => movement;
    internal Vector3 MeleeWallOrigin => transform.position + Vector3.up * muzzleOffset.y;
    internal void RaiseAttackStarted(Part part) { lastAttacker = part; WeaponAttackStarted?.Invoke(part); }
    internal void RaiseMeleePerformed(int hits) => MeleePerformed?.Invoke(hits);

    // ---- Socket queries (HUD, tests) ----

    // The part mounted in a socket that can be used, or null.
    public Part GetPart(PartCategory socket)
    {
        var part = parts != null ? parts.GetEquipped(socket) : null;
        return part != null && part.isEquipped && part.isActiveAndEnabled && part.EquippedBy != null && part.Stats != null ? part : null;
    }
    public PartSubtype SubtypeOf(PartCategory socket) { var part = GetPart(socket); return part != null ? part.Stats.subtype : PartSubtype.None; }
    public int AmmoRemaining(PartCategory socket) { var part = GetPart(socket); return part != null && IsRanged(part) ? part.RemainingAmmo : 0; }
    public float ReloadRemaining(PartCategory socket) { var part = GetPart(socket); return part != null ? Mathf.Max(0f, part.ReadyAt - Time.time) : 0f; }
    public MechAbilityDriver GetAbility(PartCategory socket)
    {
        var part = GetPart(socket);
        return part != null && part.Stats.ability != MechPartAbilityKind.None ? part.GetComponent<MechAbilityDriver>() : null;
    }
    public bool HasMelee => GetMeleePart(PartCategory.ArmL) != null || GetMeleePart(PartCategory.ArmR) != null;
    internal Part GetMeleePart(PartCategory socket)
    {
        var part = isActiveAndEnabled ? GetPart(socket) : null;
        return part != null && part.Stats.subtype == PartSubtype.Melee ? part : null;
    }
    static bool IsRanged(Part part) => part.Stats.subtype == PartSubtype.Projectile || part.Stats.subtype == PartSubtype.Explosive;

    // ---- Attacking ----

    public AttackResult TryAttack(PartCategory socket)
    {
        if (!isActiveAndEnabled) return default;
        var part = GetPart(socket);
        if (part != null && IsRanged(part)) { var shot = Fire(part); return new AttackResult(shot != null, shot); }
        if (part != null && part.Stats.subtype == PartSubtype.Melee) return new AttackResult(melee.TryStart(part, socket), null);
        // No weapon: the part's own ability, or an arm's animation-only attack.
        var ability = GetAbility(socket);
        if (ability != null && ability.TryActivate()) return new AttackResult(true, null);
        bool arm = socket == PartCategory.ArmL || socket == PartCategory.ArmR;
        return new AttackResult(arm && TryAnimationOnlyAttack(part), null);
    }

    MechProjectile Fire(Part part)
    {
        if (part.ReadyAt > Time.time || part.RemainingAmmo <= 0) return null;
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        if (gameplayCamera == null) return null;
        var stats = part.Stats;
        float effectiveRange = Mathf.Max(0.01f, stats.GetStat(StatType.Range) * MetresPerGolem);
        float maximumRange = effectiveRange * Mathf.Max(1f, stats.GetStat(StatType.MaxRangeMultiplier));
        Vector3 muzzle = MuzzleOf(part);
        Vector3 aimPoint = AimSolver.AimPoint(targeting, gameplayCamera, transform, maximumRange, hitLayers);
        var projectile = RangedAttack.Fire(part, transform, muzzle, aimPoint, gameplayCamera.transform.forward,
            effectiveRange, maximumRange, hitLayers, combatMaterial, ForwardProjectileImpact,
            stats.GetBool(StatType.SpiralBarrage) ? SpiralRunner() : null, spiralSettings);
        lastAttacker = part;
        ShotFired?.Invoke(projectile);
        WeaponAttackStarted?.Invoke(part);
        return projectile;
    }

    static bool TryAnimationOnlyAttack(Part part)
    {
        if (part == null || part.Stats.subtype != PartSubtype.None || part.Stats.ability != MechPartAbilityKind.None) return false;
        var presentation = part.GetComponent<MechWeaponAnimator>();
        return presentation != null && presentation.TryAnimationOnlyAttack();
    }

    CannonSpiralBarrage SpiralRunner()
    {
        if (barrageRunner == null)
        {
            barrageRunner = GetComponent<CannonSpiralBarrage>();
            if (barrageRunner == null) barrageRunner = gameObject.AddComponent<CannonSpiralBarrage>();
            barrageRunner.ImpactResolved += ForwardProjectileImpact;
        }
        return barrageRunner;
    }

    void ForwardProjectileImpact(MechProjectileImpact impact) => ProjectileImpact?.Invoke(impact);
    Vector3 MuzzleOf(Part part) => part != null && part.muzzle != null ? part.muzzle.position : transform.TransformPoint(muzzleOffset);

    // ---- Lifecycle ----

    void Awake()
    {
        parts = GetComponent<EquipManager>();
        targeting = GetComponent<MechTargetLockController>();
        movement = GetComponent<MechMovementController>();
        melee = new MeleeAttack(this);
        if (gameplayCamera == null) gameplayCamera = Camera.main;
    }

    void OnEnable()
    {
        if (parts != null) { parts.EquipmentChanged += PrepareEquipment; PrepareEquipment(); }
        runtimeInputActions = MechInputActions.Create(inputActions);
        rightArmAction = runtimeInputActions != null ? runtimeInputActions.FindAction("Player/Attack", false) : null;
        leftArmAction = runtimeInputActions != null ? runtimeInputActions.FindAction("Player/Melee", false) : null;
        leftShoulderAction = runtimeInputActions != null ? runtimeInputActions.FindAction("Player/Cannon", false) : null;
        rightShoulderAction = runtimeInputActions != null ? runtimeInputActions.FindAction("Player/Crossbow", false) : null;
        rightArmAction?.Enable();
        leftArmAction?.Enable();
        leftShoulderAction?.Enable();
        rightShoulderAction?.Enable();
    }

    void OnDisable()
    {
        if (parts != null) parts.EquipmentChanged -= PrepareEquipment;
        if (barrageRunner != null) barrageRunner.CancelAll();
        rightArmAction?.Disable();
        leftArmAction?.Disable();
        leftShoulderAction?.Disable();
        rightShoulderAction?.Disable();
        MechInputActions.Release(ref runtimeInputActions);
        rightArmAction = leftArmAction = leftShoulderAction = rightShoulderAction = null;
        inputArmed = false;
        melee.Cancel();
    }

    // Runs whenever equipment changes: newly equipped weapons get their ammo, and a swing in
    // progress is cancelled because its arm may have changed.
    void PrepareEquipment()
    {
        foreach (var socket in Sockets)
        {
            var part = parts.GetEquipped(socket);
            if (part == null || part.Stats == null) continue;
            AbilitySetup.Ensure(part);
            if (IsRanged(part)) part.InitializeAmmo(Mathf.Max(0, Mathf.RoundToInt(part.Stats.GetStat(StatType.Ammo))));
        }
        melee.Cancel();
    }

    void LateUpdate()
    {
        TickCooldowns(Time.deltaTime);
        TickMelee(Time.deltaTime);
        if (readPlayerInput) ProcessPlayerInput(Cursor.lockState == CursorLockMode.Locked);
    }

    void TickCooldowns(float deltaTime) => melee.TickTimers(deltaTime);
    void TickMelee(float deltaTime) => melee.Tick(deltaTime);

    // TEMPORARY: moves to the player input component in its own step.
    // External callers disable ReadPlayerInput first and supply their own cursor-capture policy.
    public void ProcessPlayerInput(bool hasCapturedCursor)
    {
        if (!isActiveAndEnabled) return;
        if (!hasCapturedCursor || Time.timeScale == 0f)
        {
            inputArmed = false;
            return;
        }
        bool rightHeld = rightArmAction != null ? rightArmAction.IsPressed() : Mouse.current != null && Mouse.current.rightButton.isPressed;
        bool right = rightArmAction != null ? rightArmAction.WasPressedThisFrame() : Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        bool leftShoulderHeld = leftShoulderAction != null ? leftShoulderAction.IsPressed() : Keyboard.current != null && Keyboard.current.qKey.isPressed;
        bool rightShoulderHeld = rightShoulderAction != null ? rightShoulderAction.IsPressed() : Keyboard.current != null && Keyboard.current.eKey.isPressed;
        bool leftShoulder = leftShoulderAction != null ? leftShoulderAction.WasPressedThisFrame() : Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
        bool rightShoulder = rightShoulderAction != null ? rightShoulderAction.WasPressedThisFrame() : Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        bool leftHeld = leftArmAction != null ? leftArmAction.IsPressed() : Mouse.current != null && Mouse.current.leftButton.isPressed;
        bool left = leftArmAction != null ? leftArmAction.WasPressedThisFrame() : Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        // A click to recapture the mouse should not also fire a weapon.
        if (!inputArmed) { inputArmed = !rightHeld && !leftHeld && !leftShoulderHeld && !rightShoulderHeld; return; }
        // Holding the swap key with a part selected turns each attack button into
        // "swap the part in that socket" instead of attacking with it.
        if (parts != null && parts.IsSwapHeld)
        {
            if (left) parts.TrySwapSelectedInto(PartCategory.ArmL);
            if (right) parts.TrySwapSelectedInto(PartCategory.ArmR);
            if (leftShoulder) parts.TrySwapSelectedInto(PartCategory.ShoulderL);
            if (rightShoulder) parts.TrySwapSelectedInto(PartCategory.ShoulderR);
            return;
        }
        if (right || left || leftShoulder || rightShoulder) Physics.SyncTransforms();
        if (left) TryAttack(PartCategory.ArmL);                // LMB: left arm
        if (right) TryAttack(PartCategory.ArmR);               // RMB: right arm
        if (leftShoulder) TryAttack(PartCategory.ShoulderL);   // Q: left shoulder
        if (rightShoulder) TryAttack(PartCategory.ShoulderR);  // E: right shoulder
    }

    void OnValidate()
    {
        spiralSettings = spiralSettings.Validated();
        meleeSize = Vector3.Max(Vector3.one * 0.01f, meleeSize);
        maximumMeleeApproach = Mathf.Max(0f, maximumMeleeApproach);
        meleeApproachDuration = Mathf.Max(0.01f, meleeApproachDuration);
    }
}
