using Unity.Behavior;
using UnityEngine;

public enum EnemyBehaviorRole { Sword, Mace, Hammer, ThrowingAxe, Spinner, Crossbow, HeavyRanged, RockRope }

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
    EnemyAgent agent;
    EnemyAttack attack;
    BehaviorGraphAgent graph;
    CombatHealth health;
    EnemyGraphAttack specialAttack;
    Transform observedTarget;
    bool detected, graphWasAvailable;
    public EnemyAgent Agent => agent;
    public EnemyAttack Attack => attack;
    public BehaviorGraphAgent Graph => graph;
    public string CurrentAction => currentAction;
    public bool TargetDetected => detected;
    public EnemyGraphAttack SpecialAttack => specialAttack;
    public bool AttackInProgress => specialAttack != null ? specialAttack.IsExecuting : attack.IsAttacking;
    public float CooldownRemaining => specialAttack != null ? specialAttack.CooldownRemaining : attack.CooldownRemaining;
    public bool TargetInReach => specialAttack != null ? specialAttack.TargetInReach : attack.TargetInReach();
    public bool IsReady => isActiveAndEnabled && agent != null && agent.isActiveAndEnabled
        && attack != null && attack.isActiveAndEnabled && health != null && !health.IsDead
        && agent.ExternalDecisions && attack.ExternalRequests && (specialAttack == null || specialAttack.isActiveAndEnabled);
    void Awake()
    {
        agent = GetComponent<EnemyAgent>(); attack = GetComponent<EnemyAttack>();
        graph = GetComponent<BehaviorGraphAgent>(); health = GetComponent<CombatHealth>();
        specialAttack = GetComponent<EnemyGraphAttack>();
    }
    void OnEnable() { detected = graphWasAvailable = false; observedTarget = null; }
    void OnDisable()
    {
        if (graph != null) graph.End();
        CancelAttack(); detected = graphWasAvailable = false; currentAction = "Stopped";
    }
    void Update()
    {
        bool available = IsReady && graph != null && graph.isActiveAndEnabled && graph.Graph != null;
        if (!available)
        {
            if (graphWasAvailable && graph != null) graph.End();
            CancelAttack(); graphWasAvailable = false; currentAction = "Stopped";
            return;
        }
        if (!graphWasAvailable) { graph.Restart(); graphWasAvailable = true; }
        else if (!graph.Graph.IsRunning) { CancelAttack(); currentAction = "Stopped"; }
    }
    public bool SensePlayer()
    {
        if (!IsReady) return detected = false;
        agent.RefreshTarget(); var target = agent.Target;
        if (target != observedTarget) { detected = false; observedTarget = target; }
        var targetHealth = target != null ? target.GetComponentInParent<CombatHealth>() : null;
        detected = targetHealth != null && targetHealth.isActiveAndEnabled && !targetHealth.IsDead
            && agent.DistanceToTarget <= (detected ? agent.LoseRadius : agent.DetectRadius);
        return detected;
    }
    public void BeginApproach() => agent.Chase.Enter();
    public bool Approach()
    {
        currentAction = "Approach";
        if (minimumRange > 0 && agent.DistanceToTarget < minimumRange)
        { agent.SetMovementIntent(EnemyMovementIntent.Retreat, Time.deltaTime); currentAction = "Reposition"; return false; }
        bool inReach = TargetInReach;
        if (agent.DistanceToTarget <= agent.AttackRange + 0.25f && inReach) { Hold("In range"); return true; }
        var intent = agent.DistanceToTarget > agent.ArriveRadius || !inReach && agent.DistanceToTarget <= agent.AttackRange + 0.5f
            ? EnemyMovementIntent.Chase : EnemyMovementIntent.Arrive;
        agent.SetMovementIntent(intent, Time.deltaTime); return false;
    }
    public void Wander()
    { currentAction = "Wander"; agent.SetMovementIntent(EnemyMovementIntent.Wander, Time.deltaTime); }
    public void Hold(string action)
    { currentAction = action; if (agent != null) agent.SetMovementIntent(EnemyMovementIntent.Hold, Time.deltaTime); }
    public bool RequestAttack() => (minimumRange <= 0 || agent.DistanceToTarget >= minimumRange) && attack.TryAttack();
    public bool Recover()
    {
        Hold("Recover");
        if (specialAttack != null) return specialAttack.Recover(Time.deltaTime);
        return attack.CooldownRemaining <= 0;
    }
    public void CancelAttack()
    {
        if (attack != null && attack.IsAttacking) attack.CancelPendingAttack();
        if (specialAttack != null) specialAttack.Cancel();
    }
}
