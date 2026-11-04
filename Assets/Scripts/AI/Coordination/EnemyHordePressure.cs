using Unity.Profiling;
using UnityEngine;

public sealed partial class EnemyHordeCoordinator
{
    [Header("M6 pressure approach")]
    [Tooltip("Use body-sized crowd clearance throughout pursuit, including passing ranged enemies outside the local melee radius.")]
    [SerializeField] bool compactPressureApproach = true;
    static readonly ProfilerMarker pressureMarker = new ProfilerMarker("Lithomachia.AI.PressurePosition");
    public bool CompactPressureApproach { get => compactPressureApproach; set => compactPressureApproach = value; }
    public int PressurePlans { get; private set; }
    public double PressurePlanningMilliseconds { get; private set; }
    public void ResetPressureDiagnostics() { PressurePlans = 0; PressurePlanningMilliseconds = 0; }
    bool TryPressurePosition(EnemyCoordinationMember member, out Vector3 position)
    {
        position = default;
        if (member.HasPressureGoal && member.AssignedTarget == member.Agent.Target && Time.time < member.PressurePlanAt
            && Steering.Flat(member.Agent.TargetPosition - member.PlannedTargetPosition).sqrMagnitude < 1)
        { position = member.SteeringPosition; return true; }
        long began = System.Diagnostics.Stopwatch.GetTimestamp();
        using (pressureMarker.Auto())
        {
            try
            {
                var agent = member.Agent;
                Vector3 radial = Steering.Flat(agent.Position - agent.TargetPosition).normalized;
                if (radial == Vector3.zero) radial = -agent.Forward;
                Vector3 tangent = Vector3.Cross(Vector3.up, radial);
                Vector3 candidate = agent.TargetPosition + radial * Mathf.Max(agent.BodyRadius + .4f, agent.AttackRange - .12f)
                    + tangent * member.ApproachBias;
                if (!GroundPoint(agent, candidate, out var goal) || !ClearSegment(agent, agent.Position, goal))
                { member.Clear("Follow navigation to target"); return false; }
                member.HasPressureGoal = true; member.Slot = -1; member.AssignedTarget = agent.Target;
                member.DesiredPosition = member.SteeringPosition = position = goal;
                member.PlannedTargetPosition = agent.TargetPosition; member.PressurePlanAt = Time.time + planningInterval;
                member.Describe("Closing on target"); member.DescribeAttack("Attack whenever weapon is ready"); return true;
            }
            finally
            {
                PressurePlans++;
                PressurePlanningMilliseconds += (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }
        }
    }
}
