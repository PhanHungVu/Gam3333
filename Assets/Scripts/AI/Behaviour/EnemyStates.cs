using UnityEngine;

// Port of State.gd / StateMachine.gd / WanderState.gd / SeekState.gd / ArriveState.gd.
// Same contract as the fish: a state's job is to fill agent.BehaviourForce each tick
// (and decide when to hand over to another state). The agent adds separation etc. on top.
// The hierarchical (parent-state / LCA) part is dropped: nothing in the fish used it.

public abstract class EnemyState
{
    protected EnemyAgent agent;
    public void Init(EnemyAgent owner) => agent = owner;
    public virtual void Enter() { }
    public virtual void Exit() { }
    public abstract void Tick(float dt);
}

public sealed class EnemyStateMachine
{
    public EnemyState Current { get; private set; }

    public void Transition(EnemyState next)
    {
        if (next == null || next == Current) return;
        Current?.Exit();
        Current = next;
        Current.Enter();
    }

    public void Tick(float dt) => Current?.Tick(dt);
}

// Idle drifting until the player comes inside the detect radius.
public sealed class WanderState : EnemyState
{
    public override void Enter() { agent.FaceTarget = false; agent.InAttackRange = false; }

    public override void Tick(float dt)
    {
        if (agent.HasTarget && agent.DistanceToTarget < agent.DetectRadius)
        {
            agent.Fsm.Transition(agent.Chase);
            return;
        }
        agent.GoalDirection = Vector3.zero;
        agent.BehaviourForce = Steering.Wander(agent, dt) * agent.WanderWeight;
    }
}

// Full-speed chase toward the player - reads the flow field when one exists
// (Phase 6), so this is the piece that actually solves the corner-stuck problem:
// the direction now comes from a map that already knows the way around a wall,
// not from a straight line toward the player that a wall can block.
public sealed class ChaseState : EnemyState
{
    public override void Enter() { agent.FaceTarget = false; agent.InAttackRange = false; }

    public override void Tick(float dt)
    {
        if (!agent.HasTarget || agent.DistanceToTarget > agent.LoseRadius)
        { agent.Fsm.Transition(agent.Wander); return; }

        if (agent.DistanceToTarget <= agent.ArriveRadius)
        { agent.Fsm.Transition(agent.Arrive); return; }

        Vector3 dir = Steering.Flat(agent.TargetPosition - agent.Position).normalized; // Phase 3 fallback

        var field = FlowField.Instance;
        if (field != null && field.HasDirection)
        {
            Vector3 fieldDir = field.SampleDirection(agent.Position);
            // A zero result means unreachable or off the grid entirely - keep the
            // direct line rather than freezing the enemy in place for that case.
            if (fieldDir != Vector3.zero) dir = fieldDir;
        }

        agent.GoalDirection = dir;
        agent.BehaviourForce = Steering.SeekDirection(agent, dir) * agent.SeekWeight;
    }
}

// Slow down and stop at attack range, facing the player.
// agent.InAttackRange is the hook a future Attack state / animation reads.
public sealed class ArriveState : EnemyState
{
    public override void Enter() { agent.FaceTarget = true; }
    public override void Exit() { agent.InAttackRange = false; }

    public override void Tick(float dt)
    {
        if (!agent.HasTarget || agent.DistanceToTarget > agent.LoseRadius)
        { agent.Fsm.Transition(agent.Wander); return; }

        // 20% hysteresis so an enemy on the boundary doesn't flicker between states.
        if (agent.DistanceToTarget > agent.ArriveRadius * 1.2f)
        { agent.Fsm.Transition(agent.Chase); return; }

        agent.GoalDirection = Steering.Flat(agent.TargetPosition - agent.Position).normalized;
        agent.BehaviourForce = Steering.Arrive(agent, agent.TargetPosition, agent.ArriveRadius, agent.AttackRange) * agent.SeekWeight;
        agent.InAttackRange = agent.DistanceToTarget <= agent.AttackRange + 0.5f;
    }
}