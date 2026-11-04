using UnityEngine;

// Local firing-position suggestions only. The graph owns when to move or fire;
// EnemyAgent and the existing grid/flow field still own navigation and collision.
public sealed class EnemyRangedPositioning
{
    readonly EnemyGraphController controller;
    readonly EnemyAgent agent;
    readonly EnemyLocalPositionQuery navigation;
    static readonly float[] angles = { 0, 20, -20, 40, -40, 65, -65, 90, -90 };
    Transform target;
    Vector3 plannedTarget;
    float planAt;
    public bool HasGoal { get; private set; }
    public Vector3 Goal { get; private set; }

    public EnemyRangedPositioning(EnemyGraphController owner)
    { controller = owner; agent = owner.Agent; navigation = new EnemyLocalPositionQuery(agent); }

    public void Clear() { HasGoal = false; target = null; planAt = 0; }

    public bool Crowded(float spacing)
    {
        foreach (var other in EnemyAgent.All)
            if (RangedPeer(other) && Steering.Flat(other.Position - agent.Position).sqrMagnitude < spacing * spacing) return true;
        return false;
    }

    bool RangedPeer(EnemyAgent other) => other != null && other != agent && other.Target == agent.Target
        && other.TryGetComponent<EnemyGraphController>(out var brain) && brain.UsesRangedPositioning && brain.IsReady;

    public bool TryGetGoal(float minimum, float interval, float spacing, out Vector3 position)
    {
        position = default;
        if (agent.Target == null) { Clear(); return false; }
        if (target == agent.Target && Time.time < planAt && Steering.Flat(plannedTarget - agent.TargetPosition).sqrMagnitude < 1)
        { position = Goal; return HasGoal; }
        target = agent.Target; plannedTarget = agent.TargetPosition; planAt = Time.time + interval;
        if (!navigation.Available) { HasGoal = false; return false; }

        float maximum = agent.AttackRange - .25f;
        float near = Mathf.Min(minimum + .65f, maximum);
        float preferred = Mathf.Lerp(near, maximum, .55f);
        Vector3 radial = Steering.Flat(agent.Position - plannedTarget).normalized;
        if (radial.sqrMagnitude < .001f) radial = -agent.Forward;
        float bestScore = float.PositiveInfinity;
        Vector3 best = default;
        // Soft crowd costs spread shooters without reserving slots or withholding a shot.
        for (int ring = 0; ring < 3; ring++)
        {
            float radius = ring == 0 ? preferred : ring == 1 ? maximum : near;
            foreach (float angle in angles)
            {
                Vector3 candidate = plannedTarget + Quaternion.AngleAxis(angle, Vector3.up) * radial * radius;
                if (!navigation.TryGoal(candidate, out var goal)
                    || !controller.Attack.HasClearRangedPathFrom(goal)) continue;
                float score = Steering.Flat(goal - agent.Position).magnitude + Mathf.Abs(radius - preferred) * .4f;
                foreach (var other in EnemyAgent.All)
                {
                    if (!RangedPeer(other)) continue;
                    score += Mathf.Max(0, spacing - Steering.Flat(other.Position - goal).magnitude) * 4;
                    var brain = other.GetComponent<EnemyGraphController>();
                    if (brain.HasRangedPosition) score += Mathf.Max(0, spacing - Steering.Flat(brain.RangedPosition - goal).magnitude) * 2;
                }
                if (HasGoal && Steering.Flat(goal - Goal).sqrMagnitude < .5f) score -= .8f;
                if (score >= bestScore) continue;
                bestScore = score; best = goal;
            }
        }
        HasGoal = bestScore < float.PositiveInfinity;
        if (HasGoal) Goal = position = best;
        return HasGoal;
    }

}
