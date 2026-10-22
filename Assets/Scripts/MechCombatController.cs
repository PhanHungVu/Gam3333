using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
public class MechCombatController : MonoBehaviour
{
    [SerializeField] InputActionAsset inputActions;
    [SerializeField] bool readPlayerInput = true;
    public bool ReadPlayerInput { get => readPlayerInput; set => readPlayerInput = value; }
    InputActionAsset runtimeInputActions;
    [SerializeField] Camera gameplayCamera;
    [SerializeField] Material combatMaterial;
    [SerializeField] LayerMask hitLayers = ~0;
    [Header("Ranged Weapons (ranges in metres, one-round magazines)")]
    [SerializeField] Vector3 muzzleOffset = new Vector3(0.65f, 0.5f, 0.65f);
    [SerializeField] MechWeaponStats throwingAxe = new MechWeaponStats("Throwing axe", 50, 30, 0.5f, 12, 18, 100, 0.18f, 0, new Color(0.3f, 0.9f, 1));
    [SerializeField] MechWeaponStats cannon = new MechWeaponStats("Cannon", 100, 20, 3, 20, 30, 100, 0.3f, 6, new Color(1, 0.5f, 0.15f));
    [SerializeField] MechWeaponStats crossbow = new MechWeaponStats("Large crossbow", 30, 50, 1, 40, 60, 160, 0.07f, 0, new Color(1, 0.9f, 0.3f));
    [SerializeField] MechWeaponStats armCrossbow = new MechWeaponStats("Arm crossbow", 100, 30, 2, 20, 30, 160, 0.07f, 0, new Color(1, 0.9f, 0.3f));
    [Header("Projectile Models")]
    [SerializeField] GameObject crossbowProjectilePrefab;
    [SerializeField] GameObject throwingAxeProjectilePrefab;
    [Header("Optional Cannon Pattern")]
    [Tooltip("Snapshot at firing. Off retains the normal single cannon blast.")]
    [SerializeField] bool spiralBarrage;
    [SerializeField] CannonSpiralBarrage.Settings spiralSettings = CannonSpiralBarrage.Settings.Default;
    CannonSpiralBarrage barrageRunner;
    [Header("Melee Weapon")]
    [SerializeField] Vector3 meleeCenter = new Vector3(0f, 0f, 2.75f);
    [SerializeField] Vector3 meleeSize = new Vector3(5f, 3.5f, 5.5f);
    [SerializeField, Min(0f)] float meleeDamage = 100f;
    [SerializeField, Min(0.01f)] float meleeCooldown = 0.7f;
    [SerializeField] bool showMeleeBox = true;
    [Header("Melee Reach Assist")]
    [SerializeField, Min(0f)] float maximumMeleeApproach = 1f;
    [SerializeField, Range(1f, 60f)] float maximumMeleeAssistAngle = 30f;
    [SerializeField, Min(0.01f)] float meleeApproachDuration = 0.12f;
    [SerializeField, Min(0f)] float meleeCameraHold = 0.45f;
    MechTargetLockController targeting;
    InputAction fireAction, meleeAction, cannonAction, crossbowAction;
    bool inputArmed;
    float meleeTimer, flashTimer;
    readonly float[] reloadTimers = new float[4];
    readonly int[] remainingAmmo = new int[4];
    MechEquipmentCombat equipment;
    EquipManager parts;
    float strikeDamage;
    public void BindCamera(Camera camera) => gameplayCamera = camera;
    public bool HasWeapon(MechWeaponType slot) => equipment == null || equipment.HasWeapon(slot);
    public bool HasSword => equipment == null || equipment.HasSword;
    public bool HasMelee => equipment == null || equipment.HasMelee;
    public MechWeaponStats GetWeapon(MechWeaponType slot) => equipment != null && equipment.GetWeapon(slot) != null
        ? equipment.GetWeapon(slot) : GetDefaultWeapon(slot);
    public MechWeaponStats GetDefaultWeapon(MechWeaponType slot) => slot == MechWeaponType.Cannon ? cannon
        : slot == MechWeaponType.Crossbow ? crossbow : slot == MechWeaponType.ArmCrossbow ? armCrossbow : throwingAxe;
    public int AmmoRemaining(MechWeaponType slot) => equipment != null ? (HasWeapon(slot) ? equipment.GetPart(slot).RemainingAmmo : 0) : remainingAmmo[(int)slot];
    public float ReloadRemaining(MechWeaponType slot) => equipment != null ? (HasWeapon(slot) ? Mathf.Max(0, equipment.GetPart(slot).ReadyAt - Time.time) : 0) : reloadTimers[(int)slot];
    readonly CombatBlast blast = new CombatBlast();
    LineRenderer meleeOutline;
    LineRenderer groundOutline;
    CharacterController movementCapsule;
    MechMovementController movement;
    bool meleePending;
    Part activeMeleePart;
    PartCategory activeMeleeCategory;
    float approachTime, approachDistance, meleeExtension, meleeCameraTimer;
    Quaternion strikeRotation;
    MechTargetable assistedTarget;
    public bool MeleeCameraActive => isActiveAndEnabled && meleeCameraTimer > 0f;
    public bool IsMeleeApproaching => meleePending;
    public float MeleeExtension => meleeExtension;
    static readonly int[] BoxCorners = { 0, 1, 3, 2, 0, 4, 5, 1, 5, 7, 3, 7, 6, 2, 6, 4 };
    static readonly int[] GroundCorners = { 0, 1, 5, 4, 0 };
    public event Action<MechProjectile> ShotFired;
    public event Action<MechProjectileImpact> ProjectileImpact;
    public event Action<int> MeleePerformed;
    // Presentation starts on admission, independently of impact/damage resolution.
    public event Action<Part> WeaponAttackStarted;
    MechWeaponType lastFiredSlot = MechWeaponType.Cannon;
    public Vector3 MuzzlePosition => GetMuzzlePosition(lastFiredSlot);
    public Vector3 GetMuzzlePosition(MechWeaponType slot)
    {
        var part = equipment != null ? equipment.GetPart(slot) : null;
        return part != null && part.muzzle != null ? part.muzzle.position : transform.TransformPoint(muzzleOffset);
    }

    void Awake()
    {
        equipment = GetComponent<MechEquipmentCombat>();
        parts = GetComponent<EquipManager>();
        targeting = GetComponent<MechTargetLockController>();
        movementCapsule = GetComponent<CharacterController>();
        movement = GetComponent<MechMovementController>();
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        for (int i = 0; i < remainingAmmo.Length; i++) remainingAmmo[i] = GetWeapon((MechWeaponType)i).ammunition;
        if (combatMaterial != null) CreateMeleeOutline();
    }

    void OnEnable()
    {
        runtimeInputActions = MechInputActions.Create(inputActions);
        fireAction = runtimeInputActions != null ? runtimeInputActions.FindAction("Player/Attack", false) : null;
        meleeAction = runtimeInputActions != null ? runtimeInputActions.FindAction("Player/Melee", false) : null;
        cannonAction = runtimeInputActions != null ? runtimeInputActions.FindAction("Player/Cannon", false) : null;
        crossbowAction = runtimeInputActions != null ? runtimeInputActions.FindAction("Player/Crossbow", false) : null;
        fireAction?.Enable();
        meleeAction?.Enable();
        cannonAction?.Enable();
        crossbowAction?.Enable();
    }

    void OnDisable()
    {
        if (barrageRunner != null) barrageRunner.CancelAll();
        fireAction?.Disable();
        meleeAction?.Disable();
        cannonAction?.Disable();
        crossbowAction?.Disable();
        MechInputActions.Release(ref runtimeInputActions);
        fireAction = meleeAction = cannonAction = crossbowAction = null;
        inputArmed = false;
        meleePending = false;
        meleeExtension = meleeCameraTimer = flashTimer = 0f;
        if (meleeOutline != null) meleeOutline.enabled = false;
        if (groundOutline != null) groundOutline.enabled = false;
    }

    void LateUpdate()
    {
        TickCooldowns(Time.deltaTime);
        TickMelee(Time.deltaTime);
        UpdateMeleeOutline();
        if (readPlayerInput) ProcessPlayerInput(Cursor.lockState == CursorLockMode.Locked);
    }

    // Explicit input boundary, like the movement motor's Tick. External callers
    // disable ReadPlayerInput first and supply their own cursor-capture policy.
    public void ProcessPlayerInput(bool hasCapturedCursor)
    {
        if (!isActiveAndEnabled) return;
        if (!hasCapturedCursor || Time.timeScale == 0f)
        {
            inputArmed = false;
            return;
        }
        bool fireHeld = fireAction != null ? fireAction.IsPressed() : Mouse.current != null && Mouse.current.rightButton.isPressed;
        bool fire = fireAction != null ? fireAction.WasPressedThisFrame() : Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        bool cannonHeld = cannonAction != null ? cannonAction.IsPressed() : Keyboard.current != null && Keyboard.current.qKey.isPressed;
        bool crossbowHeld = crossbowAction != null ? crossbowAction.IsPressed() : Keyboard.current != null && Keyboard.current.eKey.isPressed;
        bool fireCannon = cannonAction != null ? cannonAction.WasPressedThisFrame() : Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
        bool fireCrossbow = crossbowAction != null ? crossbowAction.WasPressedThisFrame() : Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
        bool meleeHeld = meleeAction != null ? meleeAction.IsPressed() : Mouse.current != null && Mouse.current.leftButton.isPressed;
        bool melee = meleeAction != null ? meleeAction.WasPressedThisFrame() : Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        // A click to recapture the mouse should not also fire a weapon.
        if (!inputArmed) { inputArmed = !fireHeld && !meleeHeld && !cannonHeld && !crossbowHeld; return; }
        // Holding the swap key with a part selected turns each attack button into
        // "swap the part in that socket" instead of firing it.
        if (parts != null && parts.IsSwapHeld)
        {
            if (melee) parts.TrySwapSelectedInto(PartCategory.ArmL);
            if (fire) parts.TrySwapSelectedInto(PartCategory.ArmR);
            if (fireCannon) parts.TrySwapSelectedInto(PartCategory.ShoulderL);
            if (fireCrossbow) parts.TrySwapSelectedInto(PartCategory.ShoulderR);
            return;
        }
        if (fire || melee || fireCannon || fireCrossbow) Physics.SyncTransforms();
        // Each button fires whatever is mounted in its socket.
        if (melee) TryMelee();                              // LMB: left arm
        if (fire) TryFire();                                // RMB: right arm
        if (fireCannon) TryFire(PartCategory.ShoulderL);    // Q: left shoulder
        if (fireCrossbow) TryFire(PartCategory.ShoulderR);  // E: right shoulder
    }

    void TickCooldowns(float deltaTime)
    {
        for (int i = 0; i < reloadTimers.Length; i++) reloadTimers[i] = Mathf.Max(0f, reloadTimers[i] - deltaTime);
        meleeTimer = Mathf.Max(0f, meleeTimer - deltaTime);
        flashTimer = Mathf.Max(0f, flashTimer - deltaTime);
        meleeCameraTimer = Mathf.Max(0f, meleeCameraTimer - deltaTime);
        if (!meleePending && flashTimer <= 0f) meleeExtension = 0f;
    }

    // RMB: the right arm.
    public MechProjectile TryFire() => TryFire(PartCategory.ArmR);

    // Fire whatever is mounted in this socket. Shoulders use their ability or ranged weapon;
    // arms fire a ranged weapon, otherwise melee, their own ability, or an animation-only attack.
    public MechProjectile TryFire(PartCategory mount)
    {
        if (equipment == null) return LegacyFire(mount);
        var part = equipment.GetEquippedPart(mount);
        bool arm = mount == PartCategory.ArmL || mount == PartCategory.ArmR;
        if (!arm && isActiveAndEnabled && equipment.TryAbility(mount)) return null;
        if (TryGetRangedWeapon(part, out var weapon)) return TryFire(weapon, part);
        if (arm) TryMelee(mount);
        return null;
    }

    // Which ranged weapon a part is (projectile, default stats); None for melee/ability parts.
    static bool TryGetRangedWeapon(Part part, out MechWeaponType weapon)
    {
        weapon = default;
        if (part == null || part.data == null) return false;
        var type = part.data.weapon;
        if (type < MechPartWeapon.ThrowingAxe || type > MechPartWeapon.ArmCrossbow) return false;
        weapon = (MechWeaponType)((int)type - (int)MechPartWeapon.ThrowingAxe);
        return true;
    }

    // Prototype fixtures without an EquipManager keep the original fixed loadout.
    public static MechWeaponType LegacyWeaponFor(PartCategory mount) => mount == PartCategory.ShoulderL ? MechWeaponType.Cannon
        : mount == PartCategory.ShoulderR ? MechWeaponType.Crossbow : MechWeaponType.ThrowingAxe;
    MechProjectile LegacyFire(PartCategory mount)
    {
        if (mount == PartCategory.ArmL) { TryMelee(mount); return null; }
        return TryFire(LegacyWeaponFor(mount), null);
    }

    MechProjectile TryFire(MechWeaponType slot, Part part)
    {
        int index = (int)slot;
        if (!isActiveAndEnabled || index < 0 || index >= remainingAmmo.Length) return null;
        bool usable = part != null ? equipment != null && equipment.isActiveAndEnabled
            && part.isEquipped && part.isActiveAndEnabled && part.EquippedBy != null : HasWeapon(slot);
        if (!usable)
        {
            if (slot == MechWeaponType.ThrowingAxe) TryAnimationOnlyAttack(PartCategory.ArmR);
            return null;
        }
        if (part != null && (part.ReadyAt > Time.time || part.RemainingAmmo <= 0)
            || part == null && (ReloadRemaining(slot) > 0f || AmmoRemaining(slot) <= 0)) return null;
        var weapon = equipment != null && part != null ? equipment.GetWeapon(part) : GetWeapon(slot);
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        if (gameplayCamera == null) return null;
        lastFiredSlot = slot;
        Vector3 muzzle = part != null && part.muzzle != null ? part.muzzle.position : GetMuzzlePosition(slot);
        Vector3 aimPoint = GetAimPoint(weapon.maximumRange);
        Vector3 direction = (aimPoint - muzzle).normalized;
        if (direction.sqrMagnitude < 0.001f) direction = gameplayCamera.transform.forward;
        if (equipment != null) part.Consume(weapon.reloadSeconds, true);
        else { reloadTimers[index] = weapon.reloadSeconds; remainingAmmo[index]--; }
        var prefab = part != null && part.data != null && part.data.projectilePrefab != null ? part.data.projectilePrefab
            : slot == MechWeaponType.ThrowingAxe ? throwingAxeProjectilePrefab
            : slot == MechWeaponType.Crossbow || slot == MechWeaponType.ArmCrossbow ? crossbowProjectilePrefab : null;
        var projectile = MechProjectile.Create(prefab, weapon.displayName + " Projectile",
            new Vector3(transform.position.x, muzzle.y, transform.position.z), weapon.projectileRadius, combatMaterial, weapon.color);
        projectile.Weapon = slot;
        // Subscribe before Launch: a wall intersecting the muzzle can impact immediately.
        projectile.ImpactResolved += ForwardProjectileImpact;
        projectile.ConfigureRangeAndExplosion(weapon.effectiveRange, weapon.explosionRadius, combatMaterial, weapon.color);
        if (slot == MechWeaponType.Cannon && spiralBarrage)
        {
            if (barrageRunner == null)
            {
                barrageRunner = GetComponent<CannonSpiralBarrage>();
                if (barrageRunner == null) barrageRunner = gameObject.AddComponent<CannonSpiralBarrage>();
                barrageRunner.ImpactResolved += ForwardProjectileImpact;
            }
            projectile.ConfigureSpiralBarrage(barrageRunner, spiralSettings);
        }
        projectile.Launch(transform, direction, weapon.projectileSpeed, weapon.damage, weapon.projectileRadius,
            weapon.maximumRange, hitLayers, muzzle);
        ShotFired?.Invoke(projectile);
        if (equipment != null) WeaponAttackStarted?.Invoke(part);
        return projectile;
    }

    void ForwardProjectileImpact(MechProjectileImpact impact) => ProjectileImpact?.Invoke(impact);

    Vector3 GetAimPoint(float projectileRange)
    {
        var enemy = targeting != null && targeting.isActiveAndEnabled && !targeting.IsFreeAim ? targeting.CurrentTarget : null;
        if (enemy != null && enemy.isActiveAndEnabled) return enemy.AimPosition;
        Ray ray = gameplayCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 aimPoint = ray.GetPoint(projectileRange);
        float nearest = projectileRange;
        foreach (var hit in Physics.RaycastAll(ray, projectileRange, hitLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
            nearest = hit.distance;
            aimPoint = hit.point;
        }
        return aimPoint;
    }

    public bool TryMelee() => TryMelee(PartCategory.ArmL);

    bool TryMelee(PartCategory category)
    {
        var arm = equipment != null ? equipment.GetEquippedPart(category) : null;
        if (arm != null && arm.data != null && (arm.data.weapon == MechPartWeapon.ArmCrossbow || arm.data.weapon == MechPartWeapon.ThrowingAxe))
            return TryFire(arm.data.weapon == MechPartWeapon.ArmCrossbow ? MechWeaponType.ArmCrossbow : MechWeaponType.ThrowingAxe, arm) != null;
        var meleePart = equipment != null ? equipment.GetMeleePart(category) : null;
        if (isActiveAndEnabled && meleePart == null && equipment != null)
            return equipment.TryAbility(category) || TryAnimationOnlyAttack(category);
        if (!isActiveAndEnabled || equipment != null && meleePart == null || equipment == null && meleeTimer > 0f || meleePending
            || meleePart != null && meleePart.ReadyAt > Time.time) return false;
        activeMeleePart = meleePart;
        activeMeleeCategory = category;
        strikeDamage = meleePart != null ? Mathf.Max(0f, meleePart.data.GetStat(StatType.Damage)) : meleeDamage;
        meleeTimer = meleePart != null ? Mathf.Max(0.01f, meleePart.data.GetStat(StatType.CooldownReload)) : meleeCooldown;
        if (meleePart != null) meleePart.Consume(meleeTimer, false);
        if (meleePart != null) WeaponAttackStarted?.Invoke(meleePart);
        meleeCameraTimer = meleeCameraHold;
        strikeRotation = transform.rotation;
        meleeExtension = 0f;
        approachDistance = FindMeleeApproach();
        if (approachDistance > 0.01f)
        {
            meleePending = true;
            approachTime = 0f;
            UpdateMeleeOutline();
        }
        else ResolveMelee();
        return true;
    }

    bool TryAnimationOnlyAttack(PartCategory category)
    {
        var part = equipment != null ? equipment.GetEquippedPart(category) : null;
        if (part == null || part.data == null || part.data.weapon != MechPartWeapon.None
            || part.data.ability != MechPartAbilityKind.None) return false;
        var presentation = part.GetComponent<MechWeaponAnimator>();
        return presentation != null && presentation.TryAnimationOnlyAttack();
    }

    // Reach assistance translates only the attack box. It never steers or teleports
    // the movement root, and never changes target or direction after admission.
    float FindMeleeApproach()
    {
        assistedTarget = targeting != null && !targeting.IsFreeAim ? targeting.CurrentTarget : null;
        if (assistedTarget == null || !assistedTarget.isActiveAndEnabled || maximumMeleeApproach <= 0f
            || movement != null && (movement.IsQuickBoosting || !movement.IsGrounded)) return 0f;
        Vector3 direction = Vector3.ProjectOnPlane(assistedTarget.AimPosition - transform.position, Vector3.up);
        float angle = Vector3.Angle(transform.forward, direction);
        if (angle >= maximumMeleeAssistAngle) return 0f;
        float budget = maximumMeleeApproach * (1f - angle / maximumMeleeAssistAngle);
        Vector3 halfSize = Vector3.Scale(meleeSize, AbsScale()) * 0.5f;
        Vector3 center = MeleeBoxCenter(0f);
        Vector3 front = center + strikeRotation * Vector3.forward * halfSize.z;
        float closestGap = float.MaxValue;
        foreach (var candidate in assistedTarget.GetComponentsInChildren<Collider>())
        {
            if (!candidate.enabled || candidate.isTrigger || !candidate.gameObject.activeInHierarchy
                || (hitLayers.value & (1 << candidate.gameObject.layer)) == 0) continue;
            var health = candidate.GetComponentInParent<CombatHealth>();
            if (health == null || health.IsDead || !health.isActiveAndEnabled
                || blast.LineBlocked(MeleeWallOrigin(), candidate, transform, hitLayers)) continue;
            Vector3 local = Quaternion.Inverse(strikeRotation) * (candidate.ClosestPoint(front) - center);
            if (Mathf.Abs(local.x) > halfSize.x || Mathf.Abs(local.y) > halfSize.y) continue;
            closestGap = Mathf.Min(closestGap, local.z - halfSize.z);
        }
        if (closestGap <= 0f || closestGap + 0.03f > budget) return 0f;
        float extension = closestGap + 0.03f;
        return LimitMeleeApproach(extension) >= extension - 0.001f ? extension : 0f;
    }

    float LimitMeleeApproach(float distance)
    {
        Vector3 halfSize = Vector3.Scale(meleeSize, AbsScale()) * 0.5f;
        foreach (var hit in Physics.BoxCastAll(MeleeBoxCenter(0f), halfSize, strikeRotation * Vector3.forward,
            strikeRotation, distance, hitLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform) && hit.collider.GetComponentInParent<CombatHealth>() == null)
                distance = Mathf.Min(distance, Mathf.Max(0f, hit.distance - 0.03f));
        return distance;
    }

    void TickMelee(float deltaTime)
    {
        if (!meleePending || deltaTime <= 0f) return;
        if (assistedTarget == null || !assistedTarget.isActiveAndEnabled
            || !HasMelee || equipment != null && equipment.GetMeleePart(activeMeleeCategory) != activeMeleePart
            || movement != null && (movement.IsQuickBoosting || !movement.IsGrounded))
        {
            meleePending = false;
            meleeExtension = 0f;
            MeleePerformed?.Invoke(0);
            return;
        }
        approachTime += deltaTime;
        Physics.SyncTransforms();
        meleeExtension = LimitMeleeApproach(approachDistance * Mathf.Clamp01(approachTime / meleeApproachDuration));
        if (approachTime >= meleeApproachDuration) ResolveMelee();
    }

    Vector3 MeleeBoxCenter(float extension) => transform.position
        + strikeRotation * (Vector3.Scale(meleeCenter, AbsScale()) + Vector3.forward * extension);

    void ResolveMelee()
    {
        flashTimer = 0.15f;
        Vector3 center = MeleeBoxCenter(meleeExtension);
        Vector3 halfSize = Vector3.Scale(meleeSize, AbsScale()) * 0.5f;
        var result = blast.ResolveBox(center, halfSize, strikeRotation, strikeDamage, transform, hitLayers, MeleeWallOrigin());
        meleePending = false;
        MeleePerformed?.Invoke(result.Hits);
        UpdateMeleeOutline();
    }

    // Melee wall checks run from the mech's chest height to each target.
    Vector3 MeleeWallOrigin() => transform.position + Vector3.up * muzzleOffset.y;

    Vector3 AbsScale()
    {
        var scale = transform.lossyScale;
        var part = equipment != null ? (activeMeleePart != null && (meleeTimer > 0f || meleePending || flashTimer > 0f) ? activeMeleePart
            : equipment.GetMeleePart(PartCategory.ArmL) ?? equipment.GetMeleePart(PartCategory.ArmR)) : null;
        float reachScale = part != null ? Mathf.Max(0.01f, part.data.GetStat(StatType.Range) * part.data.metresPerGolem / 4f) : 1f;
        return new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z) * reachScale);
    }

    void CreateMeleeOutline()
    {
        var outline = new GameObject("Melee Hitbox Preview");
        outline.transform.SetParent(transform, false);
        meleeOutline = outline.AddComponent<LineRenderer>();
        meleeOutline.useWorldSpace = true;
        meleeOutline.sharedMaterial = combatMaterial;
        meleeOutline.startWidth = meleeOutline.endWidth = 0.05f;
        meleeOutline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meleeOutline.receiveShadows = false;
        var ground = new GameObject("Melee Ground Reach");
        ground.transform.SetParent(transform, false);
        groundOutline = ground.AddComponent<LineRenderer>();
        groundOutline.sharedMaterial = combatMaterial;
        groundOutline.useWorldSpace = true;
        groundOutline.positionCount = 5;
        groundOutline.startWidth = groundOutline.endWidth = 0.055f;
        groundOutline.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        groundOutline.receiveShadows = false;
        UpdateMeleeOutline();
    }

    void UpdateMeleeOutline()
    {
        if (meleeOutline == null) return;
        if (!meleePending && flashTimer <= 0f) strikeRotation = transform.rotation;
        meleeOutline.enabled = showMeleeBox && HasMelee;
        groundOutline.enabled = showMeleeBox && HasMelee;
        // Walk every edge of the box, retracing only the edges needed to connect them.
        meleeOutline.positionCount = BoxCorners.Length;
        for (int i = 0; i < BoxCorners.Length; i++)
        {
            int c = BoxCorners[i];
            Vector3 corner = new Vector3((c & 1) == 0 ? -0.5f : 0.5f,
                (c & 2) == 0 ? -0.5f : 0.5f, (c & 4) == 0 ? -0.5f : 0.5f);
            meleeOutline.SetPosition(i, MeleeBoxCenter(meleeExtension) + strikeRotation * Vector3.Scale(corner, Vector3.Scale(meleeSize, AbsScale())));
        }
        meleeOutline.startColor = meleeOutline.endColor = flashTimer > 0f
            ? new Color(1f, 0.9f, 0.25f) : meleePending ? new Color(1f, 0.55f, 0.15f)
            : new Color(0.2f, 0.8f, 1f, 0.7f);
        groundOutline.startColor = groundOutline.endColor = meleeOutline.startColor;
        float groundY = movementCapsule != null ? movementCapsule.bounds.min.y + 0.04f : transform.position.y - 1.46f;
        for (int i = 0; i < GroundCorners.Length; i++)
        {
            int c = GroundCorners[i];
            Vector3 point = MeleeBoxCenter(meleeExtension) + strikeRotation * Vector3.Scale(
                new Vector3((c & 1) == 0 ? -0.5f : 0.5f, 0f, (c & 4) == 0 ? -0.5f : 0.5f), Vector3.Scale(meleeSize, AbsScale()));
            point.y = groundY;
            groundOutline.SetPosition(i, point);
        }
    }

    public void CancelEquippedMelee()
    {
        activeMeleePart = null;
        meleePending = false;
        meleeExtension = meleeCameraTimer = flashTimer = 0;
    }

    void OnValidate()
    {
        spiralSettings = spiralSettings.Validated();
        meleeSize = Vector3.Max(Vector3.one * 0.01f, meleeSize);
        meleeCooldown = Mathf.Max(0.01f, meleeCooldown);
        maximumMeleeApproach = Mathf.Max(0f, maximumMeleeApproach);
        meleeApproachDuration = Mathf.Max(0.01f, meleeApproachDuration);
    }
}
