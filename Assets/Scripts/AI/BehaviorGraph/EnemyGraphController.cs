using Unity.Behavior;
using UnityEngine;

public enum EnemyBehaviorRole
{
    Sword, Mace, Hammer, ThrowingAxe, Spinner, Crossbow, HeavyRanged,
    [InspectorName("Rock Rope - Orbit")] RockRope,
    [InspectorName("Rock Rope - Throw and Pull")] RockRopePull
}

// Adapter only. The graph owns decision order; existing systems own execution.
[RequireComponent(typeof(EnemyAgent), typeof(EnemyAttack), typeof(BehaviorGraphAgent))]
[DisallowMultipleComponent, DefaultExecutionOrder(-100)]
public sealed class EnemyGraphController : MonoBehaviour
{
    [SerializeField] string currentAction;
    [SerializeField] EnemyBehaviorRole role;
    public EnemyBehaviorRole Role => role;
    [Tooltip("Ranged roles back away when the player enters this distance. Zero keeps ordinary melee approach.")]
    [SerializeField, Min(0)] float minimumRange;
    [Header("Ranged firing positions")]
    [SerializeField] bool useRangedPositioning = true;
    [SerializeField, Min(.1f)] float rangedPlanningInterval = .35f;
    [SerializeField, Min(1)] float rangedSpacing = 3;
    EnemyAgent agent;
    EnemyAttack attack;
    BehaviorGraphAgent graph;
    CombatHealth health;
    EnemyGraphAttack specialAttack;
    EnemyCoordinationMember coordination;
    EnemyRangedPositioning rangedPositioning;
    EnemySpecialCoordinator specialCoordinator;
    Transform observedTarget;
    bool detected, graphWasAvailable;
    public EnemyAgent Agent => agent;
    public EnemyAttack Attack => attack;
    public BehaviorGraphAgent Graph => graph;
    public string CurrentAction => currentAction;
    public bool TargetDetected => detected;
    public EnemyGraphAttack SpecialAttack => specialAttack;
    public bool UsesSpecialCoordination => specialCoordinator != null && specialCoordinator.isActiveAndEnabled && specialCoordinator.Supports(this);
    public bool UsesRangedPositioning => useRangedPositioning && minimumRange > 0
        && (role == EnemyBehaviorRole.Crossbow || role == EnemyBehaviorRole.HeavyRanged);
    public bool HasRangedPosition => rangedPositioning != null && rangedPositioning.HasGoal;
    public Vector3 RangedPosition => HasRangedPosition ? rangedPositioning.Goal : transform.position;
    public bool AttackInProgress => specialAttack != null ? specialAttack.IsExecuting : attack.IsAttacking;
    public float CooldownRemaining => specialAttack != null ? specialAttack.CooldownRemaining : attack.CooldownRemaining;
    public bool TargetInReach => specialAttack != null ? specialAttack.TargetInReach : attack.TargetInReach();
    public bool UsesMeleeAttackCoordination => CoordinationMember() != null && coordination.AttackCoordinationEnabled;
    public bool ContinuousMeleePressure
    {
        get
        {
            if (role < EnemyBehaviorRole.Sword || role > EnemyBehaviorRole.ThrowingAxe) return false;
            var member = CoordinationMember();
            return member == null || member.isActiveAndEnabled && member.Coordinator != null
                && member.Coordinator.isActiveAndEnabled && member.Coordinator.ContinuousMeleePressure;
        }
    }
    public bool IsReady => isActiveAndEnabled && agent != null && agent.isActiveAndEnabled
        && attack != null && attack.isActiveAndEnabled && health != null && !health.IsDead
        && agent.ExternalDecisions && attack.ExternalRequests && (specialAttack == null || specialAttack.isActiveAndEnabled);
    void Awake()
    {
        agent = GetComponent<EnemyAgent>(); attack = GetComponent<EnemyAttack>();
        graph = GetComponent<BehaviorGraphAgent>(); health = GetComponent<CombatHealth>();
        specialAttack = GetComponent<EnemyGraphAttack>();
    }
    void OnEnable()
    { detected = graphWasAvailable = false; observedTarget = null; if (specialCoordinator != null) specialCoordinator.Register(this); }
    void OnDisable()
    {
        if (graph != null) graph.End();
        if (coordination != null) coordination.Clear("Graph disabled");
        CancelAttack(); detected = graphWasAvailable = false; currentAction = "Stopped";
        if (specialCoordinator != null) specialCoordinator.Unregister(this);
    }
    void Update()
    {
        bool available = IsReady && graph != null && graph.isActiveAndEnabled && graph.Graph != null;
        if (!available)
        {
            if (graphWasAvailable && graph != null) graph.End();
            CancelAttack(); if (coordination != null) coordination.Clear("Graph unavailable");
            graphWasAvailable = false; currentAction = "Stopped";
            return;
        }
        if (!graphWasAvailable) { graph.Restart(); graphWasAvailable = true; }
        else if (!graph.Graph.IsRunning) { CancelAttack(); if (coordination != null) coordination.Clear("Graph stopped"); currentAction = "Stopped"; }
    }
    public bool SensePlayer()
    {
        if (!IsReady) { if (coordination != null) coordination.Clear("Unavailable"); return detected = false; }
        agent.RefreshTarget(); var target = agent.Target;
        if (target != observedTarget)
        { detected = false; observedTarget = target; if (rangedPositioning != null) rangedPositioning.Clear(); if (specialCoordinator != null) CancelAttack(); if (coordination != null) { CancelAttack(); coordination.Clear("Target changed"); } }
        var targetHealth = target != null ? target.GetComponentInParent<CombatHealth>() : null;
        detected = targetHealth != null && targetHealth.isActiveAndEnabled && !targetHealth.IsDead
            && (agent.IsPursuingFallbackTarget || agent.DistanceToTarget <= (detected ? agent.LoseRadius : agent.DetectRadius));
        if (!detected && coordination != null) { CancelAttack(); coordination.Clear("Target unavailable"); }
        if (!detected && rangedPositioning != null) rangedPositioning.Clear();
        if (!detected && specialCoordinator != null) CancelAttack();
        return detected;
    }
    public void BeginApproach() => agent.Chase.Enter();
    public bool Approach()
    {
        currentAction = "Approach";
        if (UsesRangedPositioning) return ApproachRanged(false);
        if (UsesSpecialCoordination)
        {
            if (agent.DistanceToTarget >= minimumRange && TargetInReach)
            { currentAction = "Ready special attack"; agent.SetPositionGoal(agent.Position, Time.deltaTime); return true; }
            RepositionSpecial(); return false;
        }
        if (minimumRange > 0 && agent.DistanceToTarget < minimumRange)
        { agent.SetMovementIntent(EnemyMovementIntent.Retreat, Time.deltaTime); currentAction = "Reposition"; return false; }
        bool inReach = TargetInReach;
        bool pressure = ContinuousMeleePressure;
        // A reachable target takes priority over approach decoration. No spatial
        // assignment or group permission should delay ordinary pressure attacks.
        if (pressure && agent.DistanceToTarget <= agent.AttackRange + 0.25f && inReach)
        { agent.UseMeleeCrowdClearance(); Hold("In range"); return true; }
        if (TryPosition(out var position))
        {
            if (coordination.IsAttackPosition && Steering.Flat(coordination.DesiredPosition - agent.Position).sqrMagnitude <= 0.81f
                && agent.DistanceToTarget <= agent.AttackRange + 0.25f && inReach)
            { Hold("In range"); return true; }
            currentAction = pressure ? "Close to attack" : coordination.IsAttackPosition ? "Flank to melee position" : "Reposition outside melee";
            agent.SetPositionGoal(position, Time.deltaTime); if (pressure) agent.UseMeleeCrowdClearance(); return false;
        }
        if (coordination != null && coordination.TryGetApproachRecovery(out var recovery))
        { currentAction = "Rejoin flow field"; agent.SetPositionGoal(recovery, Time.deltaTime); if (pressure) agent.UseMeleeCrowdClearance(); return false; }
        if (agent.DistanceToTarget <= agent.AttackRange + 0.25f && inReach) { Hold("In range"); return true; }
        var intent = agent.DistanceToTarget > agent.ArriveRadius || !inReach && agent.DistanceToTarget <= agent.AttackRange + 0.5f
            ? EnemyMovementIntent.Chase : EnemyMovementIntent.Arrive;
        agent.SetMovementIntent(intent, Time.deltaTime);
        if (pressure && (agent.DistanceToTarget <= 8 || coordination == null || coordination.Coordinator.CompactPressureApproach))
            agent.UseMeleeCrowdClearance();
        return false;
    }
    public void Wander()
    { currentAction = "Wander"; agent.SetMovementIntent(EnemyMovementIntent.Wander, Time.deltaTime); }
    public void Hold(string action)
    { currentAction = action; if (agent != null) agent.SetMovementIntent(EnemyMovementIntent.Hold, Time.deltaTime); }
    public bool RequestAttack()
    {
        if (!UsesMeleeAttackCoordination) return (minimumRange <= 0 || agent.DistanceToTarget >= minimumRange) && attack.TryAttack();
        if (attack.IsAttacking) return false;
        bool ready = CooldownRemaining <= 0 && coordination.AtAttackPosition && agent.DistanceToTarget <= agent.AttackRange + 0.25f && TargetInReach;
        if (!coordination.RequestAttackPermission(ready)) return false;
        if (!ready || !attack.TryAttack()) { coordination.ReleaseAttack(); return false; }
        coordination.CommitAttack(); return true;
    }
    public void RepositionWhileWaitingForAttack()
    {
        if (coordination != null && coordination.TryGetWaitingPosition(out var position)) agent.SetPositionGoal(position, Time.deltaTime);
        else Approach();
        currentAction = "Waiting for melee permission / reposition";
    }
    public void CancelQueuedAttackRequest() { if (coordination != null) coordination.CancelQueuedAttack(); }
    public bool Recover()
    {
        Hold("Recover");
        if (specialAttack != null)
        {
            bool complete = specialAttack.Recover(Time.deltaTime);
            if (!complete && UsesSpecialCoordination && specialAttack.RecoveryPoseComplete)
            { RepositionSpecial(); currentAction = "Reposition special during cooldown"; }
            return complete;
        }
        if (!attack.IsAttacking && UsesRangedPositioning)
        { ApproachRanged(true); return attack.CooldownRemaining <= 0; }
        if (!attack.IsAttacking && ContinuousMeleePressure)
        {
            bool inPosition = Approach(); currentAction = inPosition ? "Recover" : "Pursue during cooldown";
            return attack.CooldownRemaining <= 0;
        }
        if (!attack.IsAttacking)
        {
            if (TryPosition(out var position))
            { currentAction = "Reposition during cooldown"; agent.SetPositionGoal(position, Time.deltaTime); }
            else if (coordination != null && coordination.isActiveAndEnabled && coordination.Coordinator != null && coordination.Coordinator.isActiveAndEnabled)
            {
                // Resume navigation if the player leaves the local plan. The
                // attack request node still enforces the unchanged weapon cooldown.
                currentAction = "Resume approach during cooldown"; return true;
            }
        }
        return attack.CooldownRemaining <= 0;
    }
    bool ApproachRanged(bool coolingDown)
    {
        if (rangedPositioning == null) rangedPositioning = new EnemyRangedPositioning(this);
        float distance = agent.DistanceToTarget;
        bool firingDistance = distance >= minimumRange && distance <= agent.AttackRange + .25f;
        bool clear = attack.HasClearRangedPathFrom(agent.Position);
        // Ready shots always win over better spacing. A heavy holds any useful
        // firing position; crossbows spread during cooldown when crowded.
        bool spread = coolingDown && role == EnemyBehaviorRole.Crossbow && rangedPositioning.Crowded(rangedSpacing);
        if (firingDistance && clear && !spread)
        {
            rangedPositioning.Clear(); currentAction = coolingDown ? "Hold firing position / cooldown" : "Aim from firing position";
            agent.HoldFacingTarget(Time.deltaTime); return TargetInReach;
        }
        if (rangedPositioning.TryGetGoal(minimumRange, rangedPlanningInterval, rangedSpacing, out var position))
        {
            currentAction = distance < minimumRange ? "Reposition"
                : spread ? "Spread ranged firing positions" : "Move to clear firing angle";
            agent.SetPositionGoal(position, Time.deltaTime); return false;
        }
        currentAction = distance < minimumRange ? "Reposition" : "Navigate to firing position";
        agent.SetMovementIntent(distance < minimumRange ? EnemyMovementIntent.Retreat : EnemyMovementIntent.Chase, Time.deltaTime);
        return false;
    }
    bool TryPosition(out Vector3 position)
    {
        position = default;
        return CoordinationMember() != null && coordination.TryGetPosition(out position);
    }
    public void BindSpecialCoordinator(EnemySpecialCoordinator owner)
    {
        if (owner != null && !owner.Supports(this)) owner = null;
        if (specialCoordinator == owner) return;
        if (specialCoordinator != null) { CancelAttack(); specialCoordinator.Unregister(this); }
        specialCoordinator = owner;
        if (specialCoordinator != null && isActiveAndEnabled) specialCoordinator.Register(this);
    }
    public bool TryBeginSpecial() => UsesSpecialCoordination ? specialCoordinator.TryBegin(this)
        : specialAttack != null && specialAttack.BeginWindup();
    public void RepositionSpecial()
    {
        if (UsesSpecialCoordination && specialCoordinator.TryPosition(this, out var position))
        { currentAction = "Find special attack angle"; agent.SetPositionGoal(position, Time.deltaTime); return; }
        currentAction = "Navigate to special attack lane";
        agent.SetMovementIntent(agent.DistanceToTarget < minimumRange ? EnemyMovementIntent.Retreat : EnemyMovementIntent.Chase, Time.deltaTime);
    }
    EnemyCoordinationMember CoordinationMember()
    {
        if (role < EnemyBehaviorRole.Sword || role > EnemyBehaviorRole.ThrowingAxe) return null;
        if (coordination == null) TryGetComponent(out coordination);
        return coordination;
    }
    public void CancelAttack()
    {
        if (rangedPositioning != null) rangedPositioning.Clear();
        if (specialCoordinator != null) specialCoordinator.Release(this);
        if (attack != null && attack.IsAttacking) attack.CancelPendingAttack();
        if (specialAttack != null) specialAttack.Cancel();
        if (coordination != null) coordination.ReleaseAttack();
    }
    void OnDrawGizmosSelected()
    {
        if (!HasRangedPosition) return;
        Gizmos.color = Color.green; Gizmos.DrawLine(transform.position, RangedPosition);
        Gizmos.DrawWireSphere(RangedPosition, .35f);
    }
}
