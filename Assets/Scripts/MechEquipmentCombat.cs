using UnityEngine;

// Optional bridge: the original prototype continues using its built-in loadout without this component.
[RequireComponent(typeof(EquipManager), typeof(MechCombatController))]
[DefaultExecutionOrder(-120)]
public sealed class MechEquipmentCombat : MonoBehaviour
{
    EquipManager equipment;
    MechCombatController combat;
    readonly Part[] ranged = new Part[3];
    readonly MechWeaponStats[] weapons = new MechWeaponStats[3];
    public Part Sword { get; private set; }
    void Awake() { equipment = GetComponent<EquipManager>(); combat = GetComponent<MechCombatController>(); }
    void OnEnable() { equipment.EquipmentChanged += Refresh; Refresh(); }
    void OnDisable() { equipment.EquipmentChanged -= Refresh; }
    public MechAbilityDriver GetAbility(MechWeaponSlot slot)
    {
        var part = equipment.GetEquipped(slot == MechWeaponSlot.ThrowingAxe ? PartCategory.ArmL
            : slot == MechWeaponSlot.Cannon ? PartCategory.ShoulderL : PartCategory.ShoulderR);
        return AbilityOn(part);
    }
    public MechAbilityDriver MeleeAbility
    {
        get
        {
            return AbilityOn(equipment.GetEquipped(PartCategory.ArmR));
        }
    }
    public bool TryMeleeAbility() => MeleeAbility != null && MeleeAbility.TryActivate();
    public Part GetPart(MechWeaponSlot slot) => ranged[(int)slot];
    public bool HasWeapon(MechWeaponSlot slot) => isActiveAndEnabled && Usable(GetPart(slot));
    public bool HasSword => isActiveAndEnabled && Usable(Sword);
    static bool Usable(Part part) => part != null && part.isEquipped && part.isActiveAndEnabled && part.EquippedBy != null;
    static MechAbilityDriver AbilityOn(Part part)
    {
        if (!Usable(part) || part.data == null || part.data.ability == MechPartAbilityKind.None) return null;
        return part.GetComponent<MechAbilityDriver>();
    }
    public MechWeaponStats GetWeapon(MechWeaponSlot slot) => weapons[(int)slot];
    void Refresh()
    {
        Sword = null;
        for (int i = 0; i < ranged.Length; i++) { ranged[i] = null; weapons[i] = null; }
        for (int i = 0; i <= (int)PartCategory.Torso; i++)
        {
            var part = equipment.GetEquipped((PartCategory)i);
            if (part == null || part.data == null) continue;
            var data = part.data;
            EnsureAbility(part);
            if (data.weapon == MechPartWeapon.Sword) { Sword = part; continue; }
            if (data.weapon < MechPartWeapon.ThrowingAxe || data.weapon > MechPartWeapon.Crossbow) continue;
            var slot = (MechWeaponSlot)((int)data.weapon - (int)MechPartWeapon.ThrowingAxe);
            var prototype = combat.GetDefaultWeapon(slot);
            float range = Mathf.Max(0.01f, data.GetStat(StatType.Range) * data.metresPerGolem);
            var stats = new MechWeaponStats(data.partName, Mathf.Max(0, data.GetStat(StatType.Damage)),
                Mathf.Max(0, Mathf.RoundToInt(data.GetStat(StatType.Ammo))), Mathf.Max(0.01f, data.GetStat(StatType.CooldownReload)),
                range, range * Mathf.Max(1, data.maximumRangeMultiplier), prototype.projectileSpeed,
                prototype.projectileRadius, slot == MechWeaponSlot.Cannon ? data.cannonExplosionRadius : 0, prototype.color);
            ranged[(int)slot] = part; weapons[(int)slot] = stats;
            part.InitializeAmmo(stats.ammunition);
        }
        combat.CancelEquippedMelee();
        var feedback = GetComponent<CombatFeedback>();
        if (feedback != null) feedback.BindRecoilVisual(ranged[1] != null ? ranged[1].recoilVisual : null, Vector3.left);
    }

    static void EnsureAbility(Part part)
    {
        if (part.data.ability == MechPartAbilityKind.None) return;
        var required = part.data.ability == MechPartAbilityKind.ResonancePulse ? typeof(ResonancePulseAbility)
            : part.data.ability == MechPartAbilityKind.ZigzagBarrage ? typeof(ZigzagBarrageAbility)
            : typeof(BloomMissileAbility);
        var ability = part.GetComponent<MechPartAbility>();
        if (ability == null || ability.GetType() != required)
        {
            if (ability != null) Object.Destroy(ability);
            ability = (MechPartAbility)part.gameObject.AddComponent(required);
        }
        var driver = part.GetComponent<MechAbilityDriver>();
        if (driver == null) driver = part.gameObject.AddComponent<MechAbilityDriver>();
        driver.SetAbility(ability);
        driver.BindOwner(part.EquippedBy != null ? part.EquippedBy.transform : null);
    }
}
