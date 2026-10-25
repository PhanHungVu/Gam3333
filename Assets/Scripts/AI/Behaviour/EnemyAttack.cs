using UnityEngine;

// AI chooses when to attack; the animation clock determines its hit/release window.
[RequireComponent(typeof(EnemyAgent), typeof(CombatHealth))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(110)]
public sealed class EnemyAttack : MonoBehaviour
{
    public enum AttackKind { Melee, Crossbow }
    [SerializeField] AttackKind kind;
    [SerializeField] MechWeaponAnimator[] weapons = new MechWeaponAnimator[0];
    MechWeaponAnimator activeWeapon;
    int nextWeapon;
    [SerializeField] Transform muzzle;
    [SerializeField, Min(0f)] float damage = 20f;
    [SerializeField, Min(0.01f)] float cooldown = 1.8f;
    [SerializeField, Min(0.01f)] float fireDuration = 0.7f;
    [SerializeField, Range(0f, 1f)] float releaseFrame = 0.6f;
    [SerializeField, Range(0f, 180f)] float facingAngle = 70f;
    [SerializeField, Min(0f)] float verticalReach = 2.5f;
    [SerializeField, Min(0.1f)] float projectileSpeed = 18f;
    [SerializeField] Material projectileMaterial;
    [SerializeField] GameObject projectilePrefab;
    [SerializeField] LayerMask hitLayers = ~0;
    EnemyAgent agent;
    CombatHealth self, attackTarget;
    float age, remaining, stationaryAge;
    bool attacking, released;
    Vector3 aim;
    public MechWeaponAnimator Weapon => activeWeapon != null ? activeWeapon : weapons.Length > 0 ? weapons[0] : null;
    public bool OwnsWeapon(MechWeaponAnimator driver) => System.Array.IndexOf(weapons, driver) >= 0;
    public bool NeutralReady => stationaryAge >= 3f && !attacking;
    public bool IsAttacking => attacking;
    public float CooldownRemaining => remaining;
    public int Releases { get; private set; }

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
                if (kind == AttackKind.Melee)
                {
                    if (CanReach(attackTarget.transform) && ClearPath(attackTarget, AimPoint(attackTarget)))
                        attackTarget.ApplyDamage(damage);
                }
                else LaunchBolt();
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
            || !CanReach(target.transform) || !ClearPath(target, AimPoint(target))) return false;
        var selected = weapons.Length > 0 ? weapons[nextWeapon % weapons.Length] : null;
        if (selected != null && !selected.PlayAttack(cooldown, fireDuration)) return false;
        activeWeapon = selected;
        nextWeapon++;
        attackTarget = target;
        aim = AimPoint(target); // Crossbow aims once; the projectile never homes.
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
    bool ClearPath(CombatHealth target, Vector3 point)
    {
        var origin = transform.position + Vector3.up * 0.5f;
        var offset = point - origin;
        foreach (var hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude, hitLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(target.transform)) return false;
        return true;
    }
    void LaunchBolt()
    {
        var marker = activeWeapon != null ? activeWeapon.GetComponent<Part>().muzzle : muzzle;
        var position = marker != null ? marker.position : transform.position + Vector3.up * 0.5f + transform.forward;
        var direction = (aim - position).normalized;
        if (direction.sqrMagnitude < 0.001f) direction = transform.forward;
        var projectile = MechProjectile.Create(projectilePrefab, "Enemy Crossbow Bolt",
            new Vector3(transform.position.x, position.y, transform.position.z), 0.12f, projectileMaterial, new Color(1f, 0.3f, 0.15f));
        projectile.Subtype = PartSubtype.Projectile;
        projectile.Launch(transform, direction, projectileSpeed, damage, 0.12f, Mathf.Max(25f, agent.AttackRange * 2f), hitLayers, position);
    }
}
