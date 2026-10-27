using UnityEngine;

// Which side a CombatHealth is on. An attack never damages its own team, so enemies can't hurt
// each other and the player can't hurt themselves. Neutral (test dummies, props) damages and is
// damaged by everyone.
public enum CombatTeam { Neutral, Player, Enemy }

public static class CombatTeams
{
    public static bool CanDamage(CombatTeam attacker, CombatTeam target)
        => attacker == CombatTeam.Neutral || target == CombatTeam.Neutral || attacker != target;

    // The team of whoever owns this transform (the attacker's root), Neutral if it has no health.
    public static CombatTeam Of(Transform owner)
    {
        var health = owner != null ? owner.GetComponentInParent<CombatHealth>() : null;
        return health != null ? health.Team : CombatTeam.Neutral;
    }
}
