using System.Collections.Generic;
using System;
using UnityEngine;

public enum EnemyMeleeStyle { ContinuousPressure, ReservedPositions }

// Shares local positions and basic-melee permissions, never movement commands.
// Distant or obstructed actors fall back to the existing player-directed flow field.
[DisallowMultipleComponent, DefaultExecutionOrder(-150)]
public sealed partial class EnemyHordeCoordinator : MonoBehaviour
{
    [SerializeField] NavGrid grid;
    [Header("Melee behavior")]
    [SerializeField] EnemyMeleeStyle meleeStyle = EnemyMeleeStyle.ContinuousPressure;
    [Header("Melee positions")]
    [SerializeField, Range(2, 8)] int attackPositions = 4;
    [SerializeField, Range(4, 16)] int waitingPositions = 8;
    [SerializeField, Min(3)] float waitingRadius = 5.2f;
    [SerializeField, Min(5)] float localRadius = 10;
    [SerializeField, Min(0.1f)] float planningInterval = 0.2f;
    [SerializeField, Min(1)] float positionLease = 4;
    [SerializeField, Min(0.5f)] float innerReuseDelay = 2;
    [SerializeField, Min(1)] float stalledAfter = 2.5f;
    [SerializeField] bool drawGizmos = true;
    [Header("Reserved-position comparison only")]
    [SerializeField] bool useAttackPermissions;
    [SerializeField, Range(1, 8)] int maximumMeleeAttacks = 2;
    [Tooltip("Minimum separation between granted melee starts against the same target.")]
    [SerializeField, Min(0)] float meleeStartSpacing = 0.18f;
    [SerializeField, Min(0.1f)] float unstartedGrantLifetime = 0.5f;
    [Tooltip("Watchdog, extended if necessary to cover the weapon's authored attack duration.")]
    [SerializeField, Min(1)] float maximumReservationHold = 4;
    readonly EnemyMeleeAttackPool meleeAttacks = new EnemyMeleeAttackPool();
    static readonly Func<EnemyCoordinationMember, bool> attackExecuting = AttackExecuting;
    static readonly Action<EnemyCoordinationMember> cancelReservedAttack = CancelReservedAttack;
    bool appliedAttackPermissions;
    EnemyMeleeStyle appliedStyle;
    Func<EnemyCoordinationMember, Transform, bool> validateAttackMember;
    readonly List<EnemyCoordinationMember> members = new List<EnemyCoordinationMember>();
    readonly List<EnemyCoordinationMember> pending = new List<EnemyCoordinationMember>();
    readonly RaycastHit[] hits = new RaycastHit[32];
    float planAt;
    bool dirty;
    public int RegisteredCount => members.Count;
    public EnemyMeleeStyle MeleeStyle
    {
        get => meleeStyle;
        set { if (meleeStyle == value) return; meleeStyle = value; ApplyAttackMode(); }
    }
    public bool ContinuousMeleePressure => meleeStyle == EnemyMeleeStyle.ContinuousPressure;
    public bool UseAttackPermissions
    {
        get => useAttackPermissions && !ContinuousMeleePressure;
        set { if (useAttackPermissions == value && appliedAttackPermissions == UseAttackPermissions) return; useAttackPermissions = value; ApplyAttackMode(); }
    }
    public int MaximumMeleeAttacks { get => maximumMeleeAttacks; set => maximumMeleeAttacks = Mathf.Clamp(value, 1, 8); }
    public int ActiveMeleeReservations => meleeAttacks.ActiveCount;
    public int CommittedMeleeAttacks => meleeAttacks.CommittedCount;
    public int PendingMeleeRequests => meleeAttacks.PendingCount;
    public bool IsInnerSlot(int slot) => slot >= 0 && slot < attackPositions;

    void Awake() { if (grid == null) grid = FindAnyObjectByType<NavGrid>(); validateAttackMember = ValidAttackMember; }
    void OnEnable() => ApplyAttackMode();
    void OnDisable()
    {
        meleeAttacks.Clear(CancelReservedAttack);
        foreach (var member in members) if (member != null) member.Clear("Coordinator disabled"); dirty = true;
    }
    void Update()
    {
        if (appliedAttackPermissions != UseAttackPermissions || appliedStyle != meleeStyle) ApplyAttackMode();
        if (UseAttackPermissions) meleeAttacks.Tick(Time.time, maximumMeleeAttacks, meleeStartSpacing, unstartedGrantLifetime,
            validateAttackMember, attackExecuting, cancelReservedAttack);
    }
    void ApplyAttackMode()
    {
        bool styleChanged = appliedStyle != meleeStyle;
        meleeAttacks.Clear(CancelReservedAttack); appliedAttackPermissions = UseAttackPermissions; appliedStyle = meleeStyle;
        if (styleChanged) foreach (var member in members) if (member != null) member.Clear("Melee style changed");
        // Adopting an ongoing M1/baseline fight must not leave uncounted swings.
        if (UseAttackPermissions && isActiveAndEnabled)
            foreach (var member in members) if (member != null && member.IsBasicMelee) CancelReservedAttack(member);
    }
    static bool AttackExecuting(EnemyCoordinationMember member) => member != null && member.Controller != null && member.Controller.Attack.IsAttacking;
    static void CancelReservedAttack(EnemyCoordinationMember member)
    { if (member != null && member.Controller != null && member.Controller.Attack != null) member.Controller.Attack.CancelPendingAttack(); }
    bool ValidAttackMember(EnemyCoordinationMember member, Transform target)
    {
        if (member == null || member.Coordinator != this || !member.Eligible || member.Agent.Target != target) return false;
        var health = target != null ? target.GetComponentInParent<CombatHealth>() : null;
        return health != null && health.isActiveAndEnabled && !health.IsDead;
    }
    public bool HasAttackPermission(EnemyCoordinationMember member) => meleeAttacks.HasPermission(member);
    public bool HasCommittedAttack(EnemyCoordinationMember member) => meleeAttacks.IsCommitted(member);
    public int MeleeReservationsForTarget(Transform target) => meleeAttacks.CountForTarget(target);
    public bool RequestAttackPermission(EnemyCoordinationMember member, bool ready)
    {
        if (!UseAttackPermissions || !isActiveAndEnabled || member == null || !members.Contains(member) || !ValidAttackMember(member, member.Agent.Target)) return false;
        bool granted = meleeAttacks.Request(member, member.Agent.Target, Time.time, ready);
        if (!granted) member.DescribeAttack(ready ? "Waiting for melee permission" : "Repositioning; queued");
        return granted;
    }
    public void CommitAttack(EnemyCoordinationMember member)
    {
        meleeAttacks.Commit(member, Time.time, Mathf.Max(maximumReservationHold, member.Controller.Attack.AttackDuration + 0.5f));
        member.DescribeAttack("Committed melee attack");
    }
    public void ReleaseAttack(EnemyCoordinationMember member) => meleeAttacks.Release(member, true, CancelReservedAttack);
    public void CancelQueuedAttack(EnemyCoordinationMember member) => meleeAttacks.CancelQueued(member);
    public bool TryGetWaitingPosition(EnemyCoordinationMember member, out Vector3 position)
    {
        if (!TryGetPosition(member, out position)) return false;
        if (!member.IsAttackPosition) return true;
        Vector3 radial = Steering.Flat(member.DesiredPosition - member.Agent.TargetPosition).normalized;
        Vector3 tangent = Vector3.Cross(Vector3.up, radial);
        Vector3 candidate = member.DesiredPosition + tangent * (Mathf.Sin(Time.time * 1.8f + member.PositionIndex * 1.7f) * 0.3f);
        if (GroundPoint(member.Agent, candidate, out var grounded) && ClearSegment(member.Agent, member.Agent.Position, grounded)) position = grounded;
        return true;
    }
    void OnValidate()
    {
        attackPositions = Mathf.Clamp(attackPositions, 2, 8); waitingPositions = Mathf.Clamp(waitingPositions, 4, 16);
        waitingRadius = Mathf.Max(3, waitingRadius); localRadius = Mathf.Max(waitingRadius + 2, localRadius);
        planningInterval = Mathf.Max(0.1f, planningInterval); positionLease = Mathf.Max(1, positionLease);
        stalledAfter = Mathf.Max(1, stalledAfter); dirty = true;
        maximumMeleeAttacks = Mathf.Clamp(maximumMeleeAttacks, 1, 8); meleeStartSpacing = Mathf.Max(0, meleeStartSpacing);
        unstartedGrantLifetime = Mathf.Max(0.1f, unstartedGrantLifetime); maximumReservationHold = Mathf.Max(1, maximumReservationHold);
    }
    internal void Register(EnemyCoordinationMember member) { if (!members.Contains(member)) members.Add(member); dirty = true; }
    internal void Unregister(EnemyCoordinationMember member)
    { meleeAttacks.Release(member, true, CancelReservedAttack); members.Remove(member); member.Clear("Released"); dirty = true; }
    bool Eligible(EnemyCoordinationMember member)
    {
        if (member == null || !member.Eligible || member.Agent.DistanceToTarget > localRadius) return false;
        var health = member.Agent.Target.GetComponentInParent<CombatHealth>();
        return health != null && health.isActiveAndEnabled && !health.IsDead;
    }
    public bool TryGetPosition(EnemyCoordinationMember member, out Vector3 position)
    {
        position = default;
        if (!isActiveAndEnabled || !Eligible(member) || !members.Contains(member))
        { if (member != null) member.Clear("Outside local positioning"); return false; }
        if (ContinuousMeleePressure) return TryPressurePosition(member, out position);
        if (member.HasPosition && member.AssignedTarget != member.Agent.Target) { Vacate(member, "Target changed"); dirty = true; }
        if (dirty || Time.time >= planAt) Plan();
        if (!member.HasPosition || member.AssignedTarget != member.Agent.Target) return false;
        // Geometry is validated at the planning rate, not for every graph tick.
        // A dash/boost out of the local plan immediately restores ordinary chase.
        if (Steering.Flat(member.Agent.TargetPosition - member.PlannedTargetPosition).sqrMagnitude > 4)
        { Vacate(member, "Target moved; ordinary approach"); dirty = true; return false; }
        position = member.SteeringPosition; return true;
    }
    public bool TryGetApproachRecovery(EnemyCoordinationMember member, out Vector3 position)
    {
        position = default;
        var field = FlowField.Instance;
        if (!isActiveAndEnabled || !member.Eligible || grid == null || !grid.Baked || field == null || !field.HasDirection) return false;
        // Conservative grid padding can mark a physically clear wall edge as
        // blocked. Its flow direction is zero, so direct chase can pin an actor
        // against the wall. Step back onto a nearby reachable cell, then let the
        // existing field route it. This is bounded local recovery, not a new path.
        grid.WorldToCell(member.Agent.Position, out int col, out int row);
        if (!grid.IsBlocked(col, row))
        {
            if (member.Agent.DistanceToTarget > localRadius) return false;
            // With no usable local slot, follow the existing field precisely.
            // The optional zigzag demo path can otherwise keep carrying an actor
            // back into conservative padding while it is trying to get around it.
            Vector3 direction = field.SampleDirection(member.Agent.Position);
            if (direction != Vector3.zero)
            {
                Vector3 next = grid.CellToWorldCenter(col + Mathf.RoundToInt(direction.x), row + Mathf.RoundToInt(direction.z));
                if (GroundPoint(member.Agent, next, out var grounded) && ClearSegment(member.Agent, member.Agent.Position, grounded))
                { position = grounded; member.Describe("Following flow field around blocked local route"); return true; }
            }
        }
        float nearest = float.PositiveInfinity;
        for (int z = -2; z <= 2; z++) for (int x = -2; x <= 2; x++)
        {
            if (grid.IsBlocked(col + x, row + z)) continue;
            Vector3 candidate = grid.CellToWorldCenter(col + x, row + z);
            float distance = Steering.Flat(candidate - member.Agent.Position).sqrMagnitude;
            if (distance >= nearest || field.SampleDirection(candidate) == Vector3.zero) continue;
            if (!GroundPoint(member.Agent, candidate, out var grounded) || !ClearSegment(member.Agent, member.Agent.Position, grounded, true)) continue;
            nearest = distance; position = grounded;
        }
        if (float.IsPositiveInfinity(nearest)) return false;
        member.Describe("Rejoining reachable flow-field cell"); return true;
    }
    void Vacate(EnemyCoordinationMember member, string reason)
    {
        if (IsInnerSlot(member.Slot)) member.InnerEligibleAt = Time.time + innerReuseDelay;
        member.WaitingSince = Time.time; member.Clear(reason);
    }
    void Plan()
    {
        dirty = false; planAt = Time.time + planningInterval; pending.Clear();
        for (int i = members.Count - 1; i >= 0; i--)
        {
            var member = members[i];
            if (member == null) { members.RemoveAt(i); continue; }
            if (!Eligible(member)) { if (member.HasPosition) Vacate(member, "Ordinary approach"); continue; }
            if (member.HasPosition && member.AssignedTarget != member.Agent.Target) Vacate(member, "Target changed");
            // Never change a committed swing's position. Graph attack nodes hold it.
            if (member.HasPosition && member.Controller.AttackInProgress) { member.ProgressAt = Time.time; continue; }
            if (member.HasPosition)
            {
                bool valid = BuildGoal(member, member.Slot, out var desired, out var goal);
                float distance = Steering.Flat(desired - member.Agent.Position).magnitude;
                if (valid && (distance < 0.5f || distance < member.LastDistance - 0.1f
                    || Steering.Flat(desired - member.PreviousDestination).sqrMagnitude > 1)) member.ProgressAt = Time.time;
                if (!valid || Time.time >= member.LeaseUntil || Time.time - member.ProgressAt > stalledAfter)
                    Vacate(member, valid ? "Reassigning position" : "Blocked position");
                else
                {
                    member.LastDistance = distance; member.PreviousDestination = member.DesiredPosition = desired;
                    member.SteeringPosition = goal; member.PlannedTargetPosition = member.Agent.TargetPosition;
                }
            }
            if (!member.HasPosition) pending.Add(member);
        }
        // Older waiting actors get first choice. Positions are not attack tokens.
        pending.Sort((a, b) => a.WaitingSince.CompareTo(b.WaitingSince));
        foreach (var member in pending) Assign(member);
    }
    void Assign(EnemyCoordinationMember member)
    {
        int count = attackPositions + waitingPositions;
        // Try cheap angular/distance scoring first, then validate the best choices.
        // No per-enemy paths or flow-field rebuilds are introduced.
        int tried = 0;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            int best = -1; float score = float.PositiveInfinity;
            for (int slot = 0; slot < count; slot++)
            {
                if ((tried & 1 << slot) != 0 || IsInnerSlot(slot) && Time.time < member.InnerEligibleAt || Occupied(member, slot)) continue;
                float candidate = Steering.Flat(SlotPoint(member, slot) - member.Agent.Position).sqrMagnitude;
                if (!IsInnerSlot(slot)) candidate += localRadius * localRadius;
                if (candidate < score) { best = slot; score = candidate; }
            }
            if (best < 0) break;
            tried |= 1 << best;
            if (!BuildGoal(member, best, out var desired, out var goal)) continue;
            member.Slot = best; member.AssignedTarget = member.Agent.Target;
            member.DesiredPosition = member.PreviousDestination = desired; member.SteeringPosition = goal;
            member.PlannedTargetPosition = member.Agent.TargetPosition;
            member.LeaseUntil = Time.time + positionLease; member.ProgressAt = Time.time;
            member.LastDistance = Steering.Flat(desired - member.Agent.Position).magnitude;
            member.Describe(IsInnerSlot(best) ? "Melee position" : "Outer position"); return;
        }
        member.Clear("No usable position; flow-field approach");
    }
    bool Occupied(EnemyCoordinationMember requester, int slot)
    {
        foreach (var member in members)
            if (member != null && member != requester && member.HasPosition && member.AssignedTarget == requester.Agent.Target && member.Slot == slot) return true;
        return false;
    }
    Vector3 SlotPoint(EnemyCoordinationMember member, int slot)
    {
        bool inner = IsInnerSlot(slot); int index = inner ? slot : slot - attackPositions;
        // Fixed world directions avoid rotating the formation when the player turns.
        float angle = (index + 0.5f) * Mathf.PI * 2 / (inner ? attackPositions : waitingPositions) + Mathf.Sin(slot * 7.13f) * 0.07f;
        float radius = inner ? Mathf.Max(member.Agent.BodyRadius * 2 + 0.25f, member.Agent.AttackRange - 0.12f)
            : waitingRadius + Mathf.Sin(slot * 3.7f) * 0.25f;
        return member.Agent.TargetPosition + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius;
    }
    bool BuildGoal(EnemyCoordinationMember member, int slot, out Vector3 desired, out Vector3 goal)
    {
        desired = goal = default; var agent = member.Agent;
        if (!GroundPoint(agent, SlotPoint(member, slot), out desired)) return false;
        Vector3 centre = agent.TargetPosition, from = Steering.Flat(agent.Position - centre), to = Steering.Flat(desired - centre);
        float angle = Vector3.SignedAngle(from, to, Vector3.up);
        Vector3 candidate = desired;
        if (from.sqrMagnitude > 0.01f && Mathf.Abs(angle) > 35)
        {
            // Follow short arcs instead of cutting through the player to a far slot.
            Vector3 radial = Quaternion.AngleAxis(Mathf.Clamp(angle, -28, 28), Vector3.up) * from.normalized;
            candidate = centre + radial * Mathf.Max(from.magnitude, waitingRadius - 0.5f);
        }
        if (!GroundPoint(agent, candidate, out goal)) return false;
        return ClearSegment(agent, agent.Position, goal);
    }
    bool GroundPoint(EnemyAgent agent, Vector3 candidate, out Vector3 point)
    {
        point = default;
        if (grid == null || !grid.Baked || grid.IsBlockedWorld(candidate) || NavTerrain.IsWall(candidate)) return false;
        float feet = agent.Position.y - agent.PivotHeight;
        int count = Physics.RaycastNonAlloc(new Vector3(candidate.x, feet + 3, candidate.z), Vector3.down, hits, 6, CombatRagdoll.QueryMask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity; bool found = false;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider.GetComponentInParent<CombatHealth>() != null || hit.normal.y < 0.7f || Mathf.Abs(hit.point.y - feet) > 1.5f) continue;
            if (hit.distance >= nearest) continue;
            nearest = hit.distance; point = hit.point + Vector3.up * agent.PivotHeight; found = true;
        }
        return found && ClearSegment(agent, point, point);
    }
    bool ClearSegment(EnemyAgent agent, Vector3 from, Vector3 to, bool allowBlockedStart = false)
    {
        Vector3 delta = to - from; float length = delta.magnitude;
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(0.2f, grid.CellSize * 0.5f)));
        for (int i = 0; i <= steps; i++)
        {
            var sample = Vector3.Lerp(from, to, i / (float)steps);
            if (NavTerrain.IsWall(sample)) return false;
            if (grid.IsBlockedWorld(sample)) { if (!allowBlockedStart) return false; }
            else allowBlockedStart = false;
        }
        // SphereCast does not report an initial overlap. Explicitly test the goal.
        Vector3 offset = Vector3.up * agent.ProbeHeight;
        int count = Physics.SphereCastNonAlloc(from + offset, agent.BodyRadius, length > 0.001f ? delta / length : Vector3.up,
            hits, Mathf.Max(0.01f, length), CombatRagdoll.QueryMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (Blocks(hits[i].collider)) return false;
        int overlapCount = Physics.OverlapSphereNonAlloc(to + offset, agent.BodyRadius, overlaps, CombatRagdoll.QueryMask, QueryTriggerInteraction.Ignore);
        if (overlapCount == overlaps.Length || count == hits.Length) return false;
        for (int i = 0; i < overlapCount; i++) if (Blocks(overlaps[i])) return false;
        return true;
    }
    readonly Collider[] overlaps = new Collider[32];
    static bool Blocks(Collider collider) => collider != null && collider.GetComponentInParent<CombatHealth>() == null;
    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        foreach (var member in members)
        {
            if (member == null || !member.HasPosition || member.Agent == null) continue;
            Gizmos.color = member.HasCommittedAttack ? Color.red : member.HasAttackPermission ? Color.yellow
                : member.IsAttackPosition ? new Color(1, 0.6f, 0.15f) : Color.cyan;
            Gizmos.DrawLine(member.Agent.Position, member.SteeringPosition); Gizmos.DrawWireSphere(member.DesiredPosition, 0.3f);
#if UNITY_EDITOR
            UnityEditor.Handles.Label(member.DesiredPosition + Vector3.up * 0.5f, member.PositioningStatus + " / " + member.AttackPermissionStatus);
#endif
        }
    }
}
