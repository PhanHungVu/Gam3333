using System;
using UnityEngine;

// What one TryAttack call did: whether an attack started, and the projectile if it fired one.
public readonly struct AttackResult
{
    public readonly bool Started;
    public readonly MechProjectile Projectile;
    public AttackResult(bool started, MechProjectile projectile) { Started = started; Projectile = projectile; }
}

// The mech's combat brain. TryAttack(socket) attacks with whatever part is mounted in that socket,
// choosing the attack from the part's subtype: Projectile and Splash parts fire (RangedAttack),
// Melee parts swing (MeleeAttack), and arm parts with no weapon play an animation-only attack.
// Every stat comes from the part's data; ammo and reload live on the part. Presentation listens to
// the events below and never changes gameplay.
[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
[RequireComponent(typeof(EquipManager))]
public class MechCombat : MonoBehaviour
{
    [Header("Aiming")]
    [SerializeField] Camera gameplayCamera;
    [SerializeField] LayerMask hitLayers = ~0;
    [Tooltip("Muzzle used when a part has no muzzle of its own.")]
    [SerializeField] Vector3 muzzleOffset = new Vector3(0.65f, 0.5f, 0.65f);
    [Tooltip("Material for the plain projectile sphere, used when a part has no projectile model.")]
    [SerializeField] Material combatMaterial;
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
    Part lastAttacker;
    internal readonly CombatBlast Blast = new CombatBlast();

    public event Action<MechProjectile> ShotFired;
    public event Action<MechProjectileImpact> ProjectileImpact;
    public event Action<int> MeleePerformed;
    public event Action<Part> WeaponAttackStarted;

    public void BindCamera(Camera camera) => gameplayCamera = camera;
    public MeleeAttack Melee => melee;
    // Multipliers on this mech's weapon damage while something is active (e.g. the torso's Damage Amp).
    public readonly DamageModifiers DamageModifiers = new DamageModifiers();
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
    public bool HasMelee => GetMeleePart(PartCategory.ArmL) != null || GetMeleePart(PartCategory.ArmR) != null;
    internal Part GetMeleePart(PartCategory socket)
    {
        var part = isActiveAndEnabled ? GetPart(socket) : null;
        return part != null && part.Stats.subtype == PartSubtype.Melee ? part : null;
    }
    static bool IsRanged(Part part) => part.Stats.subtype == PartSubtype.Projectile || part.Stats.subtype == PartSubtype.Splash;

    // ---- Attacking ----

    public AttackResult TryAttack(PartCategory socket)
    {
        if (!isActiveAndEnabled || CombatHitStop.BlocksNewActions(transform)) return default;
        var part = GetPart(socket);
        AttackResult result;
        if (part != null && IsRanged(part)) { var shot = Fire(part); result = new AttackResult(shot != null, shot); }
        else if (part != null && part.Stats.subtype == PartSubtype.Melee) result = new AttackResult(melee.TryStart(part, socket), null);
        // No weapon: an arm's animation-only attack.
        else
        {
            bool arm = socket == PartCategory.ArmL || socket == PartCategory.ArmR;
            result = new AttackResult(arm && TryAnimationOnlyAttack(part), null);
        }
        if (result.Started && movement != null) movement.NotifyCombatAction();
        return result;
    }

    public bool TryReload(PartCategory socket)
    {
        var part = isActiveAndEnabled ? GetPart(socket) : null;
        return part != null && IsRanged(part) && part.TryReload();
    }
    public int ReloadWeapons()
    {
        int count = 0;
        foreach (var socket in Sockets) if (TryReload(socket)) count++;
        return count;
    }

    MechProjectile Fire(Part part)
    {
        if (part.ReadyAt > Time.time || part.RemainingAmmo <= 0) return null;
        if (gameplayCamera == null) gameplayCamera = Camera.main;
        if (gameplayCamera == null) return null;
        var stats = part.Stats;
        // Full damage up to Range (golems), where the shot disappears.
        float range = Mathf.Max(0.01f, stats.GetStat(StatType.Range) * MetresPerGolem);
        Vector3 muzzle = MuzzleOf(part);
        Vector3 aimPoint = AimSolver.AimPoint(targeting, gameplayCamera, transform, range, hitLayers);
        var projectile = RangedAttack.Fire(part, transform, muzzle, aimPoint, gameplayCamera.transform.forward,
            range, hitLayers, combatMaterial, ForwardProjectileImpact, DamageModifiers.Total);
        lastAttacker = part;
        ShotFired?.Invoke(projectile);
        WeaponAttackStarted?.Invoke(part);
        return projectile;
    }

    static bool TryAnimationOnlyAttack(Part part)
    {
        if (part == null || part.Stats.subtype != PartSubtype.None) return false;
        var presentation = part.GetComponent<MechWeaponAnimator>();
        return presentation != null && presentation.TryAnimationOnlyAttack();
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
    }

    void OnDisable()
    {
        if (parts != null) parts.EquipmentChanged -= PrepareEquipment;
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
            if (IsRanged(part)) part.InitializeAmmo(Mathf.Max(0, Mathf.RoundToInt(part.Stats.GetStat(StatType.Ammo))), part.Stats.MagazineSize);
        }
        melee.Cancel();
    }

    void LateUpdate()
    {
        if (CombatHitStop.IsFrozen(transform)) return;
        TickCooldowns(Time.deltaTime);
        TickMelee(Time.deltaTime);
    }

    void TickCooldowns(float deltaTime) => melee.TickTimers(deltaTime);
    void TickMelee(float deltaTime) => melee.Tick(deltaTime);

    void OnValidate()
    {
        meleeSize = Vector3.Max(Vector3.one * 0.01f, meleeSize);
        maximumMeleeApproach = Mathf.Max(0f, maximumMeleeApproach);
        meleeApproachDuration = Mathf.Max(0.01f, meleeApproachDuration);
    }
}
