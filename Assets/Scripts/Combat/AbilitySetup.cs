using UnityEngine;

// TEMPORARY: gives an ability part the matching ability component and driver when it is equipped.
// Moves to the abilities system in its own step.
public static class AbilitySetup
{
    public static void Ensure(Part part)
    {
        if (part.Stats.ability == MechPartAbilityKind.None) return;
        var required = part.Stats.ability == MechPartAbilityKind.ResonancePulse ? typeof(ResonancePulseAbility)
            : part.Stats.ability == MechPartAbilityKind.ZigzagBarrage ? typeof(ZigzagBarrageAbility)
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
