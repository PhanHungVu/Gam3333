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
// Primitive Shoulder specials share admission, but provide their own block/ring/targeted query.
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
    [Tooltip("Crosshair reference half-angle at 0% head Accuracy. Shots use a smaller inner fraction of this circle.")]
    [SerializeField, Range(0f, 45f)] float maximumSpreadAngle = 10f;
    [Tooltip("Maximum shot-spread radius relative to the crosshair circle. Closer aim points tighten it further using the weapon's Range.")]
    [SerializeField, Range(0f, 1f)] float shotSpreadRadiusFraction = .25f;
    [Tooltip("Minigun exception: half the original crosshair-radius spread at maximum range, tightening up close too.")]
    [SerializeField, Range(0f, 1f)] float minigunSpreadRadiusFraction = .5f;
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

    [Header("Melee push on enemies")]
    [SerializeField] bool meleePush = true;
    [SerializeField, Min(0)] float meleeImpulseSpeed = 6.666667f;
    [SerializeField, Min(.01f)] float meleePushSeconds = .9f;
    [SerializeField, Min(0)] float meleeKillImpulseSpeed = 18f;
    [SerializeField, Min(0)] float meleeKillLiftSpeed = 7f;

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
    public float AccuracyPercent => AccuracySpread.Percent(GetPart(PartCategory.Head)?.Stats);
    public float HeadSpreadHalfAngle => AccuracySpread.HalfAngle(AccuracyPercent, maximumSpreadAngle);
    public float SpreadHalfAngle
    {
        get
        {
            float angle = HeadSpreadHalfAngle;
            foreach (var slot in Sockets)
            {
                var part = GetPart(slot);
                if (part != null && IsRanged(part)) angle = Mathf.Max(angle, SpreadAngleOf(part));
            }
            return angle;
        }
    }
    public float SpreadAngleOf(Part part)
    {
        var prototype = part != null ? part.GetComponent<GreyboxArmWeapon>() : null;
        float extra = prototype != null && prototype.isActiveAndEnabled ? prototype.ExtraSpread : 0;
        float head = HeadSpreadHalfAngle;
        return extra > 0 ? Mathf.Min(45, Mathf.Atan(Mathf.Tan(head * Mathf.Deg2Rad) + Mathf.Tan(extra * Mathf.Deg2Rad)) * Mathf.Rad2Deg) : head;
    }
    public float SpreadRadiusPixels(Camera camera) => AccuracySpread.ScreenRadius(camera, SpreadHalfAngle);
    public float RangeMetresOf(Part part) => part != null && part.Stats != null
        ? Mathf.Max(.01f, part.Stats.GetStat(StatType.Range) * MetresPerGolem) : .01f;
    public float ShotRadiusFractionOf(Part part)
    {
        var prototype = part != null ? part.GetComponent<GreyboxArmWeapon>() : null;
        return Mathf.Clamp01(prototype != null && prototype.isActiveAndEnabled && prototype.weapon == GreyboxArmWeapon.Weapon.Minigun
            ? minigunSpreadRadiusFraction : shotSpreadRadiusFraction);
    }
    public float ShotSpreadAngleOf(Part part, Vector3 aimPoint, bool hasAimContact = true)
    {
        // Range is the existing projectile travel limit, also used as the
        // accuracy reference. Empty-space aiming has no nearby contact to tighten.
        float distance = hasAimContact ? AccuracySpread.DistanceFactor(Vector3.Distance(MuzzleOf(part), aimPoint), RangeMetresOf(part)) : 1f;
        var prototype = part != null ? part.GetComponent<GreyboxArmWeapon>() : null;
        bool shotgun = prototype != null && prototype.isActiveAndEnabled && prototype.weapon == GreyboxArmWeapon.Weapon.Shotgun;
        return AccuracySpread.ScaleRadius(shotgun ? HeadSpreadHalfAngle : SpreadAngleOf(part), ShotRadiusFractionOf(part) * distance);
    }

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

    internal bool TryMeleePush(CombatHealth victim, Vector3 incoming, out Vector3 push, out Vector3 killingLaunch)
    {
        push = killingLaunch = Vector3.zero;
        if (!isActiveAndEnabled || !meleePush || victim == null || CombatTeams.Of(transform) != CombatTeam.Player
            || victim.GetComponent<EnemyAgent>() == null) return false;
        var direction = Vector3.ProjectOnPlane(incoming, Vector3.up).normalized;
        if (direction.sqrMagnitude < .0001f) return false;
        push = direction * Mathf.Max(0, meleeImpulseSpeed);
        killingLaunch = direction * Mathf.Max(0, meleeKillImpulseSpeed) + Vector3.up * Mathf.Max(0, meleeKillLiftSpeed);
        return true;
    }
    internal void PushSurvivingEnemy(CombatHealth victim, Vector3 push)
    {
        if (!victim.IsDead && victim.TryGetComponent<EnemyAgent>(out var agent)) agent.ApplyMeleePush(push, meleePushSeconds);
    }

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
    public bool RepeatsWhileHeld(PartCategory socket)
    {
        var part = GetPart(socket); var shoulder = part != null ? part.GetComponent<GreyboxShoulderWeapon>() : null;
        return shoulder != null && shoulder.isActiveAndEnabled && shoulder.weapon == GreyboxShoulderWeapon.Weapon.TeslaCoil;
    }

    // ---- Attacking ----

    public AttackResult TryAttack(PartCategory socket)
    {
        if (!isActiveAndEnabled || CombatHitStop.BlocksNewActions(transform)) return default;
        var part = GetPart(socket);
        AttackResult result;
        var shoulder = part != null ? part.GetComponent<GreyboxShoulderWeapon>() : null;
        if (shoulder != null && shoulder.isActiveAndEnabled && shoulder.HandlesAttack) result = new AttackResult(shoulder.TryAttack(this), null);
        else if (part != null && IsRanged(part)) { var shot = Fire(part); result = new AttackResult(shot != null, shot); }
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
        // Full damage up to Range (golems), where the shot disappears.
        float range = RangeMetresOf(part);
        Vector3 muzzle = MuzzleOf(part);
        Vector3 center = AimSolver.AimPoint(targeting, gameplayCamera, transform, range, hitLayers, out bool hasAimContact);
        var prototype = part.GetComponent<GreyboxArmWeapon>();
        if (prototype != null && !prototype.isActiveAndEnabled) prototype = null;
        var shoulder = part.GetComponent<GreyboxShoulderWeapon>();
        bool salvo = shoulder != null && shoulder.isActiveAndEnabled && shoulder.weapon == GreyboxShoulderWeapon.Weapon.Katyusha;
        int count = salvo ? Mathf.Min(part.RemainingAmmo, part.MagazineRounds) : prototype != null ? prototype.ProjectileCount : 1;
        if (count <= 0) return null;
        MechProjectile projectile = null;
        float shotAngle = ShotSpreadAngleOf(part, center, hasAimContact);
        bool shotgun = prototype != null && prototype.weapon == GreyboxArmWeapon.Weapon.Shotgun;
        Vector3 attackCenter = shotgun || salvo ? AccuracySpread.RandomAimPoint(gameplayCamera, center, shotAngle) : center;
        // One admitted action: Arm multi-projectile attacks split one round's
        // damage as before; a Shoulder salvo spends its magazine at full damage
        // per rocket. Only the first projectile performs the atomic ammo/cooldown update.
        for (int i = 0; i < count; i++)
        {
            Vector3 launch = salvo ? shoulder.SalvoMuzzle(muzzle, attackCenter, i, count) : muzzle;
            Vector3 aimPoint = salvo ? launch + shoulder.SalvoAimPoint(muzzle, attackCenter, i, count) - muzzle : shotgun
                ? muzzle + AccuracySpread.RandomConeDirection(attackCenter - muzzle, prototype.ExtraSpread) * range
                : AccuracySpread.RandomAimPoint(gameplayCamera, center, shotAngle);
            var shot = RangedAttack.Fire(part, transform, launch, aimPoint, gameplayCamera.transform.forward,
                range, hitLayers, combatMaterial, impact =>
                {
                    ForwardProjectileImpact(impact);
                    if (prototype != null) prototype.OnImpact(this, impact);
                }, DamageModifiers.Total / (salvo ? 1 : count), consumeRound: i == 0, ammunitionCost: salvo ? count : 1);
            if (projectile == null) projectile = shot;
            ShotFired?.Invoke(shot);
        }
        lastAttacker = part;
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
