using UnityEngine;

// Shared local goal validation. Navigation and movement remain on NavGrid/EnemyAgent.
public sealed class EnemyLocalPositionQuery
{
    readonly EnemyAgent agent;
    readonly RaycastHit[] hits = new RaycastHit[32];
    readonly Collider[] overlaps = new Collider[32];
    NavGrid grid;
    public EnemyLocalPositionQuery(EnemyAgent owner) => agent = owner;
    public bool Available
    {
        get { if (grid == null) grid = Object.FindAnyObjectByType<NavGrid>(); return grid != null && grid.Baked; }
    }
    public bool TryGoal(Vector3 candidate, out Vector3 point)
    {
        point = default;
        if (!Available || grid.IsBlockedWorld(candidate) || NavTerrain.IsWall(candidate)) return false;
        float feet = agent.Position.y - agent.PivotHeight;
        int count = Physics.RaycastNonAlloc(new Vector3(candidate.x, feet + 3, candidate.z), Vector3.down,
            hits, 6, CombatRagdoll.QueryMask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false;
        float nearest = float.PositiveInfinity; bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider.GetComponentInParent<CombatHealth>() != null || hit.normal.y < .7f
                || Mathf.Abs(hit.point.y - feet) > 1.5f || hit.distance >= nearest) continue;
            nearest = hit.distance; point = hit.point + Vector3.up * agent.PivotHeight; found = true;
        }
        return found && ClearSegment(agent.Position, point);
    }
    bool ClearSegment(Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from; float length = delta.magnitude;
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(.2f, grid.CellSize * .5f)));
        bool leavingBlockedStart = grid.IsBlockedWorld(from);
        for (int i = 0; i <= steps; i++)
        {
            var sample = Vector3.Lerp(from, to, i / (float)steps);
            if (NavTerrain.IsWall(sample)) return false;
            if (grid.IsBlockedWorld(sample)) { if (!leavingBlockedStart) return false; }
            else leavingBlockedStart = false;
        }
        Vector3 offset = Vector3.up * agent.ProbeHeight;
        int count = Physics.SphereCastNonAlloc(from + offset, agent.BodyRadius,
            length > .001f ? delta / length : Vector3.up, hits, Mathf.Max(.01f, length), CombatRagdoll.QueryMask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) return false;
        for (int i = 0; i < count; i++) if (Blocks(hits[i].collider)) return false;
        count = Physics.OverlapSphereNonAlloc(to + offset, agent.BodyRadius, overlaps, CombatRagdoll.QueryMask, QueryTriggerInteraction.Ignore);
        if (count == overlaps.Length) return false;
        for (int i = 0; i < count; i++) if (Blocks(overlaps[i])) return false;
        return true;
    }
    static bool Blocks(Collider collider) => collider != null && collider.GetComponentInParent<CombatHealth>() == null;
}
