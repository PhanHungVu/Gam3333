using UnityEngine;

// Additional tuning only for arm prototypes 5-13. Attacks still run through
// MechCombat, MeleeAttack, RangedAttack, Part and the existing movement controller.
[DisallowMultipleComponent, RequireComponent(typeof(Part))]
public sealed class GreyboxArmWeapon : MonoBehaviour
{
    public enum Weapon { Minigun = 5, Hook, Hammer, Spear, Shotgun, Supersword, Scythe, PunchyArm, MetalBlades }
    public Weapon weapon;
    [Tooltip("Test-scene pickup only: preserve exact authored stats before the first loot roll. Normal prefab instances leave this off.")]
    public bool keepAuthoredStats;
    [Header("Shotgun (damage is shared equally between pellets)")]
    [Min(1)] public int pellets = 6;
    [Tooltip("Independent pellet cone half-angle in degrees, unaffected by head precision or distance tightening.")]
    [Range(0, 30)] public float pelletSpreadAngle = 8;
    [Header("Melee greybox dimensions (world metres)")]
    [Min(.1f)] public float meleeWidth = 2;
    [Min(.1f)] public float meleeHeight = 3;
    [Header("Scythe")]
    [Range(0, 1)] public float lifesteal = .1f;
    [Header("Punchy Arm")]
    [Range(.05f, .49f)] public float punchSpacing = .2f;
    [Header("Hook")]
    [Min(.1f)] public float pullSpeed = 24;
    [Min(.1f)] public float pullStoppingDistance = 2;
    [Min(.1f)] public float pullTimeout = 2;

    void Awake() { if (keepAuthoredStats && Application.isPlaying) GetComponent<Part>().KeepAuthoredStats(); }

    public int ProjectileCount => weapon == Weapon.Shotgun ? Mathf.Clamp(pellets, 1, 32) : 1;
    public float ExtraSpread => weapon == Weapon.Shotgun ? Mathf.Clamp(pelletSpreadAngle, 0, 30) : 0;
    public int MeleeHits => weapon == Weapon.PunchyArm ? 3 : 1;
    public float PunchInterval(Part part) => Mathf.Min(Mathf.Max(.01f, punchSpacing), Mathf.Max(.01f, part.Stats.ShotInterval) * .49f);
    public Vector3 MeleeSize(Part part, float metresPerGolem) => new Vector3(Mathf.Max(.1f, meleeWidth), Mathf.Max(.1f, meleeHeight),
        Mathf.Max(.1f, part.Stats.GetStat(StatType.Range) * metresPerGolem));

    public void OnImpact(MechCombat combat, MechProjectileImpact impact)
    {
        if (weapon != Weapon.Hook || combat == null || !combat.isActiveAndEnabled || impact.HitCollider == null) return;
        var part = GetComponent<Part>();
        if (!part.isActiveAndEnabled || !part.isEquipped || part.EquippedBy == null || part.EquippedBy.transform != combat.transform) return;
        var health = impact.HitCollider.GetComponentInParent<CombatHealth>();
        if (health != null && !CombatTeams.CanDamage(CombatTeams.Of(combat.transform), health.Team)) return;
        var movement = combat.GetComponent<MechMovementController>();
        if (movement != null) movement.BeginHookPull(impact.ContactPoint, pullSpeed, pullStoppingDistance, pullTimeout, part);
    }
}
