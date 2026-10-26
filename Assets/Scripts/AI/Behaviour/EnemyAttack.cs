using UnityEngine;

// AI chooses when to attack; the animation clock determines its hit/release window.
// What each attack does comes from the weapon part's PartData, the same data the player uses:
// its subtype (Melee / Projectile / Explosive), damage, reload, projectile and splash. The three
// tuning multipliers scale those numbers for this enemy type. Enemies never run out of ammo.
[RequireComponent(typeof(EnemyAgent), typeof(CombatHealth))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(110)]
public sealed class EnemyAttack : MonoBehaviour
{
    [SerializeField] MechWeaponAnimator[] weapons = new MechWeaponAnimator[0];
    MechWeaponAnimator activeWeapon;
    int nextWeapon;
    [Tooltip("Fallback shot origin when a weapon part has no muzzle marker.")]
    [SerializeField] Transform muzzle;
    [Header("Enemy tuning (scales the weapon's PartData)")]
    [Tooltip("Multiplies the weapon's Damage.")]
    [SerializeField, Min(0f)] float damageMultiplier = 1f;
    [Tooltip("Multiplies the weapon's Cooldown/Reload - higher means slower attacks.")]
    [SerializeField, Min(0.01f)] float cooldownMultiplier = 1f;
    [Tooltip("Multiplies the weapon's Projectile Speed, so shots can be dodged.")]
    [SerializeField, Min(0.01f)] float projectileSpeedMultiplier = 1f;
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
    CombatHealth self, attackTarget;
    readonly CombatBlast blast = new CombatBlast();
    Quaternion strikeRotation;
    float age, remaining, stationaryAge;
    bool attacking, released;
    Vector3 aim;
    public MechWeaponAnimator Weapon => activeWeapon != null ? activeWeapon : weapons.Length > 0 ? weapons[0] : null;
    public bool OwnsWeapon(MechWeaponAnimator driver) => System.Array.IndexOf(weapons, driver) >= 0;
    public System.Collections.Generic.IReadOnlyList<MechWeaponAnimator> Weapons => weapons;
    public bool NeutralReady => stationaryAge >= 3f && !attacking;
    public bool IsAttacking => attacking;
    public float CooldownRemaining => remaining;
    public int Releases { get; private set; }
    float MetresPerGolem => rulesConfig != null ? rulesConfig.metresPerGolem : 4f;

    static Part PartOf(MechWeaponAnimator weapon) => weapon != null ? weapon.GetComponent<Part>() : null;
    // This enemy's damage and time between attacks with a given weapon: its PartData scaled by the multipliers.
    public float DamageOf(MechWeaponAnimator weapon)
    {
        var part = PartOf(weapon);
        return part != null && part.Stats != null ? Mathf.Max(0f, part.Stats.GetStat(StatType.Damage)) * damageMultiplier : 0f;
    }
    public float CooldownOf(MechWeaponAnimator weapon)
    {
        var part = PartOf(weapon);
        float reload = part != null && part.Stats != null ? part.Stats.GetStat(StatType.CooldownReload) : 0f;
        return Mathf.Max(0.01f, reload * cooldownMultiplier);
    }

    void Awake() { agent = GetComponent<EnemyAgent>(); self = GetComponent<CombatHealth>(); }
    void OnEnable()
    {
        // Child weapon OnEnable can precede this owner during prefab instantiation.
        foreach (var weapon in weapons) if (weapon != null) weapon.RefreshEquipment();
    }
    void OnDisable()
    {
        attacking = released = false; attackTarget = null; stationaryAge = 0f;
        foreach (var weapon in weapons) if (weapon != null) weapon.RefreshEquipment();
    }
    void LateUpdate() => Tick(Time.deltaTime);

    public void Tick(float deltaTime)
    {
        if (!isActiveAndEnabled || self.IsDead || deltaTime <= 0f) return;
        remaining = Mathf.Max(0f, remaining - deltaTime);
        stationaryAge = !attacking && agent.Velocity.sqrMagnitude < 0.0025f ? stationaryAge + deltaTime : 0f;
        if (attacking)
        {
            // A lost/dead target cancels the pending hit. A launched bolt continues travelling.
            if (attackTarget == null || !attackTarget.isActiveAndEnabled || attackTarget.IsDead)
            { attacking = false; attackTarget = null; return; }
            age += deltaTime;
            if (!released && age >= fireDuration * releaseFrame)
            {
                released = true;
                Releases++;
                var part = PartOf(activeWeapon);
                var subtype = part != null && part.Stats != null ? part.Stats.subtype : PartSubtype.None;
                if (subtype == PartSubtype.Melee)
                {
                    if (ClearPath(attackTarget, AimPoint(attackTarget), true)) StrikeMelee(part);
                }
                else if (subtype == PartSubtype.Projectile || subtype == PartSubtype.Explosive) FireRanged(part);
            }
            if (age >= fireDuration) { attacking = false; attackTarget = null; }
            return;
        }
        if (agent.InAttackRange) TryAttack();
    }

    public bool TryAttack()
    {
        if (!isActiveAndEnabled || self.IsDead || attacking || remaining > 0f || agent.Target == null) return false;
        var target = agent.Target.GetComponentInParent<CombatHealth>();
        if (target == null || target == self || target.IsDead || !target.isActiveAndEnabled
            || !CanReach(target.transform)) return false;
        var selected = weapons.Length > 0 ? weapons[nextWeapon % weapons.Length] : null;
        var selectedPart = PartOf(selected);
        if (selectedPart == null || selectedPart.Stats == null) return false;
        // Melee only cares about walls; ranged also won't shoot into the back of another body.
        bool melee = selectedPart.Stats.subtype == PartSubtype.Melee;
        if (!ClearPath(target, AimPoint(target), melee)) return false;
        float cooldown = CooldownOf(selected);
        if (!selected.PlayAttack(cooldown, fireDuration)) return false;
        activeWeapon = selected;
        nextWeapon++;
        attackTarget = target;
        aim = AimPoint(target); // Ranged shots aim once; projectiles never home.
        strikeRotation = transform.rotation; // Melee strikes where the swing began, so side-steps dodge it.
        age = stationaryAge = 0f;
        remaining = cooldown;
        attacking = true; released = false;
        return true;
    }

    bool CanReach(Transform target)
    {
        var offset = target.position - transform.position;
        if (Mathf.Abs(offset.y) > verticalReach) return false;
        offset.y = 0f;
        return offset.magnitude <= agent.AttackRange + 0.5f
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
    void StrikeMelee(Part part)
    {
        Vector3 scale = MeleeAttack.BoxScale(transform.lossyScale, part, MetresPerGolem);
        Vector3 center = transform.position + strikeRotation * Vector3.Scale(meleeCenter, scale);
        blast.ResolveBox(center, Vector3.Scale(meleeSize, scale) * 0.5f, strikeRotation, DamageOf(activeWeapon),
            transform, hitLayers, transform.position + Vector3.up * meleeWallHeight);
    }

    // Same shot as the player's: the part's projectile, size, colour and splash, through RangedAttack.
    void FireRanged(Part part)
    {
        var marker = part.muzzle != null ? part.muzzle : muzzle;
        var position = marker != null ? marker.position : transform.position + Vector3.up * 0.5f + transform.forward;
        var stats = part.Stats;
        float effectiveRange = Mathf.Max(0.01f, stats.GetStat(StatType.Range) * MetresPerGolem);
        float maximumRange = Mathf.Max(effectiveRange * Mathf.Max(1f, stats.GetStat(StatType.MaxRangeMultiplier)),
            agent.AttackRange * 2f);
        RangedAttack.Fire(part, transform, position, aim, transform.forward, effectiveRange, maximumRange,
            hitLayers, projectileMaterial, null, damageMultiplier, projectileSpeedMultiplier);
    }
}
