using UnityEngine;

public readonly struct MechAbilityContext
{
    public readonly Transform Owner;
    public readonly Vector3 Origin, Direction;
    public readonly MechTargetable Target;
    public readonly MechWeaponStats WeaponStats;
    public readonly LayerMask HitLayers;
    public readonly CombatFeedback Feedback;
    public MechAbilityContext(Transform owner, Vector3 origin, Vector3 direction, MechTargetable target = null,
        MechWeaponStats weaponStats = null, LayerMask? hitLayers = null, CombatFeedback feedback = null)
    {
        Owner = owner; Origin = origin; Direction = direction; Target = target; WeaponStats = weaponStats;
        HitLayers = hitLayers ?? (LayerMask)~0; Feedback = feedback;
    }
}

public readonly struct MechAbilityResult
{
    public readonly Vector3 Position;
    public readonly float Radius;
    public readonly int Hits, Kills;
    public readonly bool Complete;
    public MechAbilityResult(Vector3 position, float radius, int hits, int kills, bool complete = false)
    { Position = position; Radius = radius; Hits = hits; Kills = kills; Complete = complete; }
}
