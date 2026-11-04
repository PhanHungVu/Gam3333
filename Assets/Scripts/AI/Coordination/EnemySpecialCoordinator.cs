using System.Collections.Generic;
using UnityEngine;

// Opt-in arena coordination for existing specials. Ordinary melee/ranged never
// enter this admission system; no weapon execution or cooldown is owned here.
[DisallowMultipleComponent]
public sealed class EnemySpecialCoordinator : MonoBehaviour
{
    [SerializeField, Range(1, 4)] int maximumSpinnerAttacks = 1;
    [SerializeField, Range(1, 3)] int maximumOrbitAttacks = 1;
    [SerializeField, Min(0)] float specialStartSpacing = .55f;
    [SerializeField, Min(8)] float localRadius = 24;
    [SerializeField, Min(.1f)] float planningInterval = .35f;
    [SerializeField] bool drawGizmos = true;
    sealed class Member
    {
        public EnemyGraphController Brain;
        public EnemyLocalPositionQuery Navigation;
        public Transform Target, StartedTarget;
        public long Ticket = -1;
        public float RequestedAt, StartedAt = float.NegativeInfinity, PlanAt;
        public bool HasGoal;
        public Vector3 Goal, PlannedTarget;
    }
    readonly Dictionary<EnemyGraphController, Member> members = new Dictionary<EnemyGraphController, Member>();
    readonly List<EnemyGraphController> stale = new List<EnemyGraphController>();
    static readonly float[] angles = { 0, 25, -25, 50, -50, 80, -80 };
    long nextTicket;
    public int PendingAttacks { get { int n = 0; foreach (var m in members.Values) if (m.Ticket >= 0) n++; return n; } }
    public int ActiveSpinnerAttacks => ActiveCount(false);
    public int ActiveOrbitAttacks => ActiveCount(true);
    public int MaximumSpinnerAttacks => maximumSpinnerAttacks;
    public int MaximumOrbitAttacks => maximumOrbitAttacks;
    public float StartSpacing => specialStartSpacing;
    public bool Supports(EnemyGraphController brain) => brain != null
        && (brain.Role == EnemyBehaviorRole.Spinner || brain.Role == EnemyBehaviorRole.RockRope || brain.Role == EnemyBehaviorRole.RockRopePull);
    public void Register(EnemyGraphController brain)
    {
        if (Supports(brain) && !members.ContainsKey(brain))
            members.Add(brain, new Member { Brain = brain, Navigation = new EnemyLocalPositionQuery(brain.Agent) });
    }
    public void Unregister(EnemyGraphController brain) { if (!ReferenceEquals(brain, null)) members.Remove(brain); }
    public void Release(EnemyGraphController brain)
    {
        if (brain != null && members.TryGetValue(brain, out var member))
        { member.Ticket = -1; member.HasGoal = false; member.PlanAt = 0; }
    }
    static bool Live(EnemyGraphController brain) => brain != null && brain.IsReady && brain.Graph != null
        && brain.Graph.isActiveAndEnabled && brain.Graph.Graph != null && brain.Graph.Graph.IsRunning;
    static bool Orbit(EnemyGraphController brain) => brain.Role == EnemyBehaviorRole.RockRope;
    bool Near(Member a, Member b) => a.Brain.Agent.Target == b.Brain.Agent.Target
        && Steering.Flat(a.Brain.transform.position - b.Brain.transform.position).sqrMagnitude <= localRadius * localRadius;
    int ActiveCount(bool orbit)
    {
        int count = 0;
        foreach (var m in members.Values)
            if (Live(m.Brain) && m.Brain.SpecialAttack.IsExecuting
                && (orbit ? Orbit(m.Brain) : m.Brain.Role == EnemyBehaviorRole.Spinner)) count++;
        return count;
    }
    bool CanAdmit(Member member)
    {
        int spinners = 0, orbits = 0;
        foreach (var other in members.Values)
        {
            if (other == member || !Live(other.Brain) || !Near(member, other)) continue;
            if (other.StartedTarget == member.Brain.Agent.Target && Time.time - other.StartedAt < specialStartSpacing) return false;
            if (!other.Brain.SpecialAttack.IsExecuting) continue;
            if (other.Brain.Role == EnemyBehaviorRole.Spinner) spinners++;
            if (Orbit(other.Brain)) orbits++;
        }
        return member.Brain.Role == EnemyBehaviorRole.Spinner ? spinners < maximumSpinnerAttacks
            : !Orbit(member.Brain) || orbits < maximumOrbitAttacks;
    }
    bool Ready(Member member) => Live(member.Brain) && member.Brain.TargetDetected
        && member.Brain.Agent.Target != null && !member.Brain.SpecialAttack.IsExecuting
        && member.Brain.CooldownRemaining <= 0 && member.Brain.TargetInReach;
    public bool TryBegin(EnemyGraphController brain)
    {
        if (!isActiveAndEnabled || !members.TryGetValue(brain, out var member)) return false;
        if (!Ready(member)) { member.Ticket = -1; return false; }
        if (member.Target != brain.Agent.Target)
        { member.Target = brain.Agent.Target; member.Ticket = -1; member.HasGoal = false; member.StartedAt = float.NegativeInfinity; }
        if (member.Ticket < 0) member.Ticket = nextTicket++;
        member.RequestedAt = Time.time;
        if (!CanAdmit(member)) return false;
        foreach (var other in members.Values)
            if (other != member && other.Ticket >= 0 && other.Ticket < member.Ticket && Time.time - other.RequestedAt <= .6f
                && Near(member, other) && Ready(other) && CanAdmit(other)) return false;
        if (!brain.SpecialAttack.BeginWindup()) { member.Ticket = -1; return false; }
        member.StartedAt = Time.time; member.StartedTarget = brain.Agent.Target;
        member.Ticket = -1; member.HasGoal = false; return true;
    }
    public bool TryPosition(EnemyGraphController brain, out Vector3 position)
    {
        position = default;
        if (!isActiveAndEnabled || !members.TryGetValue(brain, out var member) || brain.Agent.Target == null) return false;
        var agent = brain.Agent;
        if (member.HasGoal && member.Target == agent.Target && Time.time < member.PlanAt
            && Steering.Flat(member.PlannedTarget - agent.TargetPosition).sqrMagnitude < 1)
        { position = member.Goal; return true; }
        member.Target = agent.Target; member.PlannedTarget = agent.TargetPosition; member.PlanAt = Time.time + planningInterval;
        Vector3 radial = Steering.Flat(agent.Position - agent.TargetPosition).normalized;
        if (radial.sqrMagnitude < .001f) radial = -agent.Forward;
        float radius = brain.Role == EnemyBehaviorRole.Spinner ? agent.AttackRange - .8f
            : Orbit(brain) ? agent.AttackRange - .5f : agent.AttackRange * .8f;
        float best = float.PositiveInfinity; Vector3 chosen = default;
        foreach (float angle in angles)
        {
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * radial;
            if (!member.Navigation.TryGoal(agent.TargetPosition + direction * radius, out var goal)) continue;
            if (brain.SpecialAttack is EnemySpinnerAttack spinner && !spinner.CanChargeFrom(goal)) continue;
            if (brain.Role == EnemyBehaviorRole.RockRopePull && !brain.Attack.HasClearRangedPathFrom(goal)) continue;
            float score = Steering.Flat(goal - agent.Position).magnitude;
            foreach (var other in EnemyAgent.All)
            {
                if (other == agent || other.Target != agent.Target) continue;
                float distance = Steering.Flat(other.Position - agent.TargetPosition).magnitude;
                if (distance > localRadius) continue;
                Vector3 theirSide = Steering.Flat(other.Position - agent.TargetPosition).normalized;
                // Prefer a different direction from the close melee pack, while
                // keeping this a local flank rather than a trip around the player.
                if (other.TryGetComponent<EnemyGraphController>(out var peer) && peer.Role <= EnemyBehaviorRole.ThrowingAxe && distance < 8)
                    score += Mathf.Max(0, Vector3.Dot(direction, theirSide) - .25f) * 4;
                score += Mathf.Max(0, 3 - Steering.Flat(goal - other.Position).magnitude) * 3;
            }
            foreach (var other in members.Values)
                if (other != member && other.HasGoal && other.Target == agent.Target)
                    score += Mathf.Max(0, 3 - Steering.Flat(goal - other.Goal).magnitude) * 2;
            if (member.HasGoal && Steering.Flat(member.Goal - goal).sqrMagnitude < .5f) score -= .75f;
            if (score >= best) continue;
            best = score; chosen = goal;
        }
        member.HasGoal = best < float.PositiveInfinity;
        if (member.HasGoal) member.Goal = position = chosen;
        return member.HasGoal;
    }
    void Update()
    {
        stale.Clear();
        foreach (var member in members.Values)
        {
            if (member.Brain == null) { stale.Add(member.Brain); continue; }
            if (!Live(member.Brain) || member.Brain.Agent.Target == null || member.Target != null && member.Target != member.Brain.Agent.Target)
            { member.Brain.CancelAttack(); member.Ticket = -1; member.HasGoal = false; }
            if (Time.time - member.RequestedAt > .6f) member.Ticket = -1;
        }
        foreach (var brain in stale) members.Remove(brain);
    }
    void OnDisable()
    {
        foreach (var member in members.Values)
            if (member.Brain != null) member.Brain.CancelAttack();
    }
    void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = Color.cyan;
        foreach (var member in members.Values)
            if (member.HasGoal && member.Brain != null)
            { Gizmos.DrawLine(member.Brain.transform.position, member.Goal); Gizmos.DrawWireSphere(member.Goal, .4f); }
    }
}
