using System.Collections.Generic;
using UnityEngine;

// One weapon this enemy carries, with its own tuning. The multipliers scale the weapon part's
// PartData (the same data the player uses), so the part stays the source of every number.
[System.Serializable]
public sealed class EnemyWeapon
{
    public MechWeaponAnimator weapon;
    [Tooltip("Multiplies the part's Damage.")]
    [Min(0f)] public float damageMultiplier = 1f;
    [Tooltip("Multiplies the part's Cooldown/Reload - higher means slower attacks.")]
    [Min(0.01f)] public float cooldownMultiplier = 1f;
    [Tooltip("Multiplies the part's Projectile Speed, so shots can be dodged.")]
    [Min(0.01f)] public float projectileSpeedMultiplier = 1f;
}

// AI chooses when to attack; the animation clock determines its hit/release window.
// What each attack does comes from the weapon part's PartData: its subtype (Melee / Projectile /
// Explosive), damage, reload, projectile and splash, scaled by that weapon's multipliers.
// Enemies never run out of ammo. Each weapon only attacks when the target is within its reach
// (melee: its hit box; ranged: as far as its shot still does damage).
// Taking turns (default): one weapon attacks at a time, then the enemy waits that weapon's cooldown.
// Independent Weapons: every weapon runs on its own timer and fires whenever it is ready.
[RequireComponent(typeof(EnemyAgent), typeof(CombatHealth))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(110)]
public sealed class EnemyAttack : MonoBehaviour
{
    // One attack in progress (or cooling down). Taking turns uses a single lane; independent
    // weapons use one lane per weapon.
    sealed class Lane
    {
        public EnemyWeapon entry;
        public CombatHealth target;
        public Vector3 aim;
        public Quaternion strikeRotation;
        public float age, remaining;
        public bool attacking, released;
        public void Clear() { attacking = released = false; target = null; }
    }

    [SerializeField] EnemyWeapon[] weapons = new EnemyWeapon[0];
    [Tooltip("Off: weapons take turns, one attack then that weapon's cooldown. On: every weapon has its own timer and fires whenever ready and in reach, even while walking in.")]
    [SerializeField] bool independentWeapons;
    MechWeaponAnimator activeWeapon;
    int nextWeapon;
    [Tooltip("Fallback shot origin when a weapon part has no muzzle marker.")]
    [SerializeField] Transform muzzle;
    [Tooltip("Gives metres per golem for weapon Range. Left empty, 4 metres per golem is used, as in MechCombat.")]
    [SerializeField] MechRulesConfig rulesConfig;
    [Header("Attack timing")]
    [SerializeField, Min(0.01f)] float fireDuration = 0.7f;
    [SerializeField, Range(0f, 1f)] float releaseFrame = 0.6f;
    [SerializeField, Range(0f, 180f)] float facingAngle = 70f;
    [SerializeField, Min(0f)] float verticalReach = 2.5f;
    [Header("Melee box")]
    [Tooltip("Same shape as the player's melee box; its depth scales with the weapon's Range.")]
    [SerializeField] Vector3 meleeCenter = new Vector3(0f, 0f, 2.75f);
    [SerializeField] Vector3 meleeSize = new Vector3(5f, 3.5f, 5.5f);
    [Tooltip("Height above the enemy's root that walls are checked from.")]
    [SerializeField, Min(0f)] float meleeWallHeight = 0.5f;
    [Header("Shots")]
    [Tooltip("Used only for weapon parts without their own projectile model.")]
    [SerializeField] Material projectileMaterial;
    [SerializeField] LayerMask hitLayers = ~0;
    EnemyAgent agent;
    CombatHealth self;
    readonly CombatBlast blast = new CombatBlast();
    readonly Lane turnLane = new Lane();
    Lane[] weaponLanes = new Lane[0];
    MechWeaponAnimator[] animators = new MechWeaponAnimator[0];
    float stationaryAge;

    public MechWeaponAnimator Weapon => activeWeapon != null ? activeWeapon : animators.Length > 0 ? animators[0] : null;
    public bool OwnsWeapon(MechWeaponAnimator driver) => System.Array.IndexOf(Animators(), driver) >= 0;
    public IReadOnlyList<MechWeaponAnimator> Weapons => Animators();
    public bool NeutralReady => stationaryAge >= 3f && !IsAttacking;
    public bool IsAttacking
    {
        get
        {
            if (!independentWeapons) return turnLane.attacking;
            foreach (var lane in weaponLanes) if (lane.attacking) return true;
            return false;
        }
    }
    public float CooldownRemaining
    {
        get
        {
            if (!independentWeapons) return turnLane.remaining;
            float least = float.PositiveInfinity;
            foreach (var lane in weaponLanes) least = Mathf.Min(least, lane.remaining);
            return float.IsPositiveInfinity(least) ? 0f : least;
        }
    }
    public int Releases { get; private set; }
    float MetresPerGolem => rulesConfig != null ? rulesConfig.metresPerGolem : 4f;

    static Part PartOf(MechWeaponAnimator weapon) => weapon != null ? weapon.GetComponent<Part>() : null;
    static bool HasStats(Part part) => part != null && part.Stats != null;

    // Weapon animators, built from the entries (kept fresh in the editor, where entries change).
    MechWeaponAnimator[] Animators()
    {
        if (animators.Length != weapons.Length || !Application.isPlaying)
        {
            animators = new MechWeaponAnimator[weapons.Length];
            for (int i = 0; i < weapons.Length; i++) animators[i] = weapons[i] != null ? weapons[i].weapon : null;
        }
        return animators;
    }

    EnemyWeapon EntryOf(MechWeaponAnimator weapon)
    {
        foreach (var entry in weapons) if (entry != null && entry.weapon == weapon) return entry;
        return null;
    }

    // This enemy's damage and time between attacks with a given weapon: its PartData scaled by that weapon's multipliers.
    public float DamageOf(MechWeaponAnimator weapon)
    {
        var entry = EntryOf(weapon); var part = PartOf(weapon);
        return entry != null && HasStats(part) ? Mathf.Max(0f, part.Stats.GetStat(StatType.Damage)) * entry.damageMultiplier : 0f;
    }
    public float CooldownOf(MechWeaponAnimator weapon)
    {
        var entry = EntryOf(weapon); var part = PartOf(weapon);
        float reload = HasStats(part) ? part.Stats.GetStat(StatType.CooldownReload) : 0f;
        return Mathf.Max(0.01f, reload * (entry != null ? entry.cooldownMultiplier : 1f));
    }

    // How far a weapon can hit from: the front of its melee box, or as far as its shot still deals damage.
    public float ReachOf(MechWeaponAnimator weapon)
    {
        var part = PartOf(weapon);
        if (!HasStats(part)) return 0f;
        var stats = part.Stats;
        if (stats.subtype == PartSubtype.Melee)
            return (meleeCenter.z + meleeSize.z * 0.5f) * MeleeAttack.BoxScale(transform.lossyScale, part, MetresPerGolem).z;
        return stats.GetStat(StatType.Range) * MetresPerGolem * Mathf.Max(1f, stats.GetStat(StatType.MaxRangeMultiplier));
    }

    void Awake()
    {
        agent = GetComponent<EnemyAgent>(); self = GetComponent<CombatHealth>();
        EnsureLanes();
        Animators();
    }
    void OnEnable()
    {
        // Child weapon OnEnable can precede this owner during prefab instantiation.
        foreach (var weapon in Animators()) if (weapon != null) weapon.RefreshEquipment();
    }
    void OnDisable()
    {
        turnLane.Clear();
        foreach (var lane in weaponLanes) lane.Clear();
        stationaryAge = 0f;
        foreach (var weapon in Animators()) if (weapon != null) weapon.RefreshEquipment();
    }
    // One lane per weapon for independent firing, matching the Weapons list.
    void EnsureLanes()
    {
        if (weaponLanes.Length == weapons.Length) return;
        weaponLanes = new Lane[weapons.Length];
        for (int i = 0; i < weapons.Length; i++) weaponLanes[i] = new Lane();
    }

    // A fresh entry added in the Inspector starts with every dial at 1 rather than 0.
    void OnValidate()
    {
        foreach (var entry in weapons)
            if (entry != null && entry.damageMultiplier == 0f && entry.cooldownMultiplier == 0f && entry.projectileSpeedMultiplier == 0f)
                entry.damageMultiplier = entry.cooldownMultiplier = entry.projectileSpeedMultiplier = 1f;
    }
    void LateUpdate() => Tick(Time.deltaTime);

    public void Tick(float deltaTime)
    {
        if (!isActiveAndEnabled || self.IsDead || deltaTime <= 0f) return;
        stationaryAge = !IsAttacking && agent.Velocity.sqrMagnitude < 0.0025f ? stationaryAge + deltaTime : 0f;
        if (!independentWeapons)
        {
            Advance(turnLane, deltaTime);
            if (!turnLane.attacking && agent.InAttackRange) TryAttack();
            return;
        }
        EnsureLanes();
        foreach (var lane in weaponLanes) Advance(lane, deltaTime);
        TryAttack();
    }

    // Counts down a lane's cooldown and plays out its attack: the hit lands at the release frame.
    void Advance(Lane lane, float deltaTime)
    {
        lane.remaining = Mathf.Max(0f, lane.remaining - deltaTime);
        if (!lane.attacking) return;
        // A lost/dead target cancels the pending hit. A launched shot continues travelling.
        if (lane.target == null || !lane.target.isActiveAndEnabled || lane.target.IsDead) { lane.Clear(); return; }
        lane.age += deltaTime;
        if (!lane.released && lane.age >= fireDuration * releaseFrame)
        {
            lane.released = true;
            Releases++;
            var part = PartOf(lane.entry.weapon);
            var subtype = HasStats(part) ? part.Stats.subtype : PartSubtype.None;
            if (subtype == PartSubtype.Melee)
            {
                if (ClearPath(lane.target, AimPoint(lane.target), true)) StrikeMelee(part, lane);
            }
            else if (subtype == PartSubtype.Projectile || subtype == PartSubtype.Explosive) FireRanged(part, lane);
        }
        if (lane.age >= fireDuration) lane.Clear();
    }

    // Taking turns: starts the next weapon in order that can reach the target. Independent: starts
    // every ready weapon that can. Returns whether any attack started.
    public bool TryAttack()
    {
        if (!isActiveAndEnabled || self.IsDead || agent.Target == null) return false;
        var target = agent.Target.GetComponentInParent<CombatHealth>();
        if (target == null || target == self || target.IsDead || !target.isActiveAndEnabled) return false;

        if (!independentWeapons)
        {
            if (turnLane.attacking || turnLane.remaining > 0f) return false;
            for (int step = 0; step < weapons.Length; step++)
            {
                int index = (nextWeapon + step) % weapons.Length;
                // Never further than the enemy's own Attack Range when taking turns.
                if (!TryStart(turnLane, weapons[index], target, agent.AttackRange)) continue;
                nextWeapon = index + 1;
                return true;
            }
            return false;
        }

        EnsureLanes();
        bool started = false;
        for (int i = 0; i < weapons.Length; i++)
        {
            var lane = weaponLanes[i];
            if (!lane.attacking && lane.remaining <= 0f && TryStart(lane, weapons[i], target, float.PositiveInfinity)) started = true;
        }
        return started;
    }

    bool TryStart(Lane lane, EnemyWeapon entry, CombatHealth target, float rangeCap)
    {
        var weapon = entry != null ? entry.weapon : null;
        var part = PartOf(weapon);
        if (!HasStats(part) || !CanReach(target.transform, Mathf.Min(ReachOf(weapon), rangeCap))) return false;
        // Melee only cares about walls; ranged also won't shoot into the back of another body.
        if (!ClearPath(target, AimPoint(target), part.Stats.subtype == PartSubtype.Melee)) return false;
        float cooldown = CooldownOf(weapon);
        if (!weapon.PlayAttack(cooldown, fireDuration)) return false;
        activeWeapon = weapon;
        lane.entry = entry;
        lane.target = target;
        lane.aim = AimPoint(target); // Ranged shots aim once; projectiles never home.
        lane.strikeRotation = transform.rotation; // Melee strikes where the swing began, so side-steps dodge it.
        lane.age = stationaryAge = 0f;
        lane.remaining = cooldown;
        lane.attacking = true; lane.released = false;
        return true;
    }

    bool CanReach(Transform target, float reach)
    {
        var offset = target.position - transform.position;
        if (Mathf.Abs(offset.y) > verticalReach) return false;
        offset.y = 0f;
        return offset.magnitude <= reach + 0.5f
            && (offset.sqrMagnitude < 0.001f || Vector3.Angle(transform.forward, offset) <= facingAngle);
    }

    static Vector3 AimPoint(CombatHealth health)
    {
        var collider = health.GetComponent<Collider>();
        return collider != null ? collider.bounds.center : health.transform.position + Vector3.up * 0.5f;
    }
    // Line from this enemy to the target's centre. With wallsOnly, bodies with health (other enemies)
    // don't count as blocking - only walls and props do. Checked to the centre, not the target's
    // nearest collider, so a part poking through a thin wall can't be hit through it.
    bool ClearPath(CombatHealth target, Vector3 point, bool wallsOnly)
    {
        var origin = transform.position + Vector3.up * meleeWallHeight;
        var offset = point - origin;
        foreach (var hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude, hitLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(target.transform)) continue;
            if (wallsOnly && hit.collider.GetComponentInParent<CombatHealth>() != null) continue;
            return false;
        }
        return true;
    }
    // Same hit as the player's melee: a box in front, depth scaled by the part's Range, resolved by
    // CombatBlast. Walls block it (see ClearPath); other bodies don't, and teams stop it hurting other enemies.
    void StrikeMelee(Part part, Lane lane)
    {
        Vector3 scale = MeleeAttack.BoxScale(transform.lossyScale, part, MetresPerGolem);
        Vector3 center = transform.position + lane.strikeRotation * Vector3.Scale(meleeCenter, scale);
        blast.ResolveBox(center, Vector3.Scale(meleeSize, scale) * 0.5f, lane.strikeRotation, DamageOf(lane.entry.weapon),
            transform, hitLayers, transform.position + Vector3.up * meleeWallHeight);
    }

    // Same shot as the player's: the part's projectile, size, colour and splash, through RangedAttack.
    void FireRanged(Part part, Lane lane)
    {
        var marker = part.muzzle != null ? part.muzzle : muzzle;
        var position = marker != null ? marker.position : transform.position + Vector3.up * 0.5f + transform.forward;
        var stats = part.Stats;
        float effectiveRange = Mathf.Max(0.01f, stats.GetStat(StatType.Range) * MetresPerGolem);
        float maximumRange = Mathf.Max(effectiveRange * Mathf.Max(1f, stats.GetStat(StatType.MaxRangeMultiplier)),
            agent.AttackRange * 2f);
        RangedAttack.Fire(part, transform, position, lane.aim, transform.forward, effectiveRange, maximumRange,
            hitLayers, projectileMaterial, null, lane.entry.damageMultiplier, lane.entry.projectileSpeedMultiplier);
    }
}
