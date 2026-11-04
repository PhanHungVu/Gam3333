using System.Linq;
using System.Text;
using UnityEngine;

public sealed partial class EnemyBehaviorPlaytest
{
    public string CombatReviewReport { get; private set; } = "End captures a combat snapshot. Home compares compact pursuit with the earlier wide approach spacing.";
    [ContextMenu("M6/Toggle Compact Melee Pursuit")]
    public void TogglePressureMovement()
    {
        if (!Application.isPlaying || coordinator == null) return;
        coordinator.MeleeStyle = EnemyMeleeStyle.ContinuousPressure; coordinator.UseAttackPermissions = false;
        coordinator.CompactPressureApproach = !coordinator.CompactPressureApproach;
        Debug.Log("Melee movement: " + (coordinator.CompactPressureApproach ? "compact pursuit" : "earlier wide approach spacing")
            + ". F11 restarts the selected preset for comparison; End records combat state.", this);
    }
    [ContextMenu("M6/Capture Combat Snapshot")]
    public void CaptureCombatReview()
    {
        if (!Application.isPlaying) return;
        var actors = EnemyAgent.All.Where(a => a != null && a.gameObject.scene == gameObject.scene)
            .Select(a => a.GetComponent<EnemyGraphController>()).Where(a => a != null).ToArray();
        var report = new StringBuilder("Combat snapshot: ").Append(actors.Length).Append(" actors; melee movement=")
            .Append(coordinator != null && coordinator.CompactPressureApproach ? "compact pursuit" : "wide approach spacing").AppendLine();
        foreach (var group in actors.GroupBy(a => a.Role).OrderBy(g => g.Key))
        {
            int released = group.Sum(a => a.Attack.Releases);
            report.Append(group.Key).Append(": ").Append(group.Count()).Append(" actors; ")
                .Append(group.Count(a => a.Attack.Releases > 0 || a.SpecialAttack is EnemySpinnerAttack spin && spin.Contacts > 0
                    || a.SpecialAttack is EnemyRopeAttack rope && (rope.OrbitsStarted > 0 || rope.Impacts > 0)))
                .Append(" contributed; releases=").Append(released)
                .Append("; attacking=").Append(group.Count(a => a.AttackInProgress))
                .Append("; cooldown=").Append(group.Count(a => !a.AttackInProgress && a.CooldownRemaining > 0)).AppendLine();
        }
        if (coordinator != null)
            report.Append("Pressure planning: ").Append(coordinator.PressurePlans).Append(" plans; mean ")
                .Append((coordinator.PressurePlanningMilliseconds / System.Math.Max(1, coordinator.PressurePlans)).ToString("F3"))
                .Append(" ms/plan; ordinary attack reservations=").Append(coordinator.ActiveMeleeReservations);
        CombatReviewReport = report.ToString(); Debug.Log(CombatReviewReport, this);
    }
}
