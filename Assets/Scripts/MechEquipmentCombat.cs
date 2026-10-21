using UnityEngine;

// Optional bridge: the original prototype continues using its built-in loadout without this component.
[RequireComponent(typeof(EquipManager), typeof(MechCombatController))]
[DefaultExecutionOrder(-120)]
public sealed class MechEquipmentCombat : MonoBehaviour
{
    EquipManager equipment;
    MechCombatController combat;
    MechMovementController movement;
    [SerializeField, Min(0f)] float weaponIdleDelay = 3f;
    float weaponIdleTime;
    public MechWeaponAnimator IdleWeapon { get; private set; }
    readonly Part[] ranged = new Part[4];
    readonly MechWeaponStats[] weapons = new MechWeaponStats[4];
    readonly System.Collections.Generic.Dictionary<Part, MechWeaponStats> partWeapons = new System.Collections.Generic.Dictionary<Part, MechWeaponStats>();
    public Part Sword { get; private set; }
    void Awake() { equipment = GetComponent<EquipManager>(); combat = GetComponent<MechCombatController>(); movement = GetComponent<MechMovementController>(); }
    void OnEnable() { equipment.EquipmentChanged += Refresh; Refresh(); }
    void OnDisable() { equipment.EquipmentChanged -= Refresh; ResetWeaponIdle(); }
    void LateUpdate()
    {
        var state = movement != null ? movement.AnimationData : default;
        bool stationary = movement != null && movement.isActiveAndEnabled && state.IsGrounded
            && !state.IsBoosting && state.Speed < 0.05f && Mathf.Abs(state.VerticalSpeed) < 0.05f;
        TickWeaponIdle(Time.deltaTime, stationary);
    }

    public void ResetWeaponIdle() { weaponIdleTime = 0f; IdleWeapon = null; }

    // One owner arbitrates all its weapons; a part never starts an idle by itself.
    public void TickWeaponIdle(float deltaTime, bool stationary)
    {
        if (!isActiveAndEnabled || !stationary) { ResetWeaponIdle(); return; }
        for (int i = 0; i <= (int)PartCategory.Torso; i++)
        {
            var part = equipment.GetEquipped((PartCategory)i);
            var driver = part != null ? part.GetComponent<MechWeaponAnimator>() : null;
            if (driver != null && driver.CurrentPose != MechWeaponAnimator.PoseState.Neutral)
            { ResetWeaponIdle(); return; }
        }
        weaponIdleTime += Mathf.Max(0f, deltaTime);
        if (weaponIdleTime < weaponIdleDelay || IdleWeapon != null) return;
        int candidates = 0;
        for (int i = 0; i <= (int)PartCategory.Torso; i++)
        {
            var part = equipment.GetEquipped((PartCategory)i);
            var driver = part != null ? part.GetComponent<MechWeaponAnimator>() : null;
            if (driver == null || !driver.CanAnimate) continue;
            // Reservoir sampling chooses uniformly without allocating a list.
            if (Random.Range(0, ++candidates) == 0) IdleWeapon = driver;
        }
    }
    // The ability on whatever is mounted in this socket, if any.
    public MechAbilityDriver GetAbility(PartCategory mount) => AbilityOn(equipment.GetEquipped(mount));
    public bool TryAbility(PartCategory mount)
    {
        var ability = GetAbility(mount);
        return ability != null && ability.TryActivate();
    }
    public Part GetPart(MechWeaponType slot) => ranged[(int)slot];
    public Part GetEquippedPart(PartCategory category) => equipment.GetEquipped(category);
    public bool HasWeapon(MechWeaponType slot) => isActiveAndEnabled && Usable(GetPart(slot));
    public bool HasSword => isActiveAndEnabled && Usable(Sword);
    public bool HasMelee => GetMeleePart(PartCategory.ArmL) != null || GetMeleePart(PartCategory.ArmR) != null;
    public Part GetMeleePart(PartCategory category)
    {
        var part = equipment != null ? equipment.GetEquipped(category) : null;
        return isActiveAndEnabled && Usable(part) && part.data != null
            && (part.data.weapon == MechPartWeapon.Sword || part.data.weapon == MechPartWeapon.Mace) ? part : null;
    }
    static bool Usable(Part part) => part != null && part.isEquipped && part.isActiveAndEnabled && part.EquippedBy != null;
    static MechAbilityDriver AbilityOn(Part part)
    {
        if (!Usable(part) || part.data == null || part.data.ability == MechPartAbilityKind.None) return null;
        return part.GetComponent<MechAbilityDriver>();
    }
    public MechWeaponStats GetWeapon(MechWeaponType slot) => weapons[(int)slot];
    public MechWeaponStats GetWeapon(Part part) => part != null && partWeapons.TryGetValue(part, out var stats) ? stats : null;
    void Refresh()
    {
        ResetWeaponIdle();
        Sword = null;
        partWeapons.Clear();
        for (int i = 0; i < ranged.Length; i++) { ranged[i] = null; weapons[i] = null; }
        for (int i = 0; i <= (int)PartCategory.Torso; i++)
        {
            var part = equipment.GetEquipped((PartCategory)i);
            if (part == null || part.data == null) continue;
            var data = part.data;
            EnsureAbility(part);
            if (data.weapon == MechPartWeapon.Sword) { Sword = part; continue; }
            if (data.weapon < MechPartWeapon.ThrowingAxe || data.weapon > MechPartWeapon.ArmCrossbow) continue;
            var slot = (MechWeaponType)((int)data.weapon - (int)MechPartWeapon.ThrowingAxe);
            var prototype = combat.GetDefaultWeapon(slot);
            float range = Mathf.Max(0.01f, data.GetStat(StatType.Range) * data.metresPerGolem);
            var stats = new MechWeaponStats(data.partName, Mathf.Max(0, data.GetStat(StatType.Damage)),
                Mathf.Max(0, Mathf.RoundToInt(data.GetStat(StatType.Ammo))), Mathf.Max(0.01f, data.GetStat(StatType.CooldownReload)),
                range, range * Mathf.Max(1, data.maximumRangeMultiplier), prototype.projectileSpeed,
                prototype.projectileRadius, slot == MechWeaponType.Cannon ? data.cannonExplosionRadius : 0, prototype.color);
            ranged[(int)slot] = part; weapons[(int)slot] = stats;
            partWeapons[part] = stats;
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
