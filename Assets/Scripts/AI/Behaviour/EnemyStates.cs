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

// Full-speed chase toward the player. Which way it heads depends on the enemy's
// Pathing mode (an Inspector dropdown on EnemyAgent, readable live in Play mode):
//   FlowField - the shared field, routes around walls (Phase 6)
//   Direct    - straight at the player, reactive avoidance only
//   Flank     - swing out to one side of the player, then close in
public sealed class ChaseState : EnemyState
{
    const float FlankTimeout = 8f; // give up the swing if it can't be reached (e.g. blocked by a wall)

    bool flankPlanned;
    bool flankReached;
    float flankTimer;
    Vector3 flankOffset; // fixed offset from the player, chosen once per chase

    public override void Enter()
    {
        agent.FaceTarget = false;
        agent.InAttackRange = false;
        flankPlanned = false;
        flankReached = false;
        flankTimer = 0f;
    }

    public override void Tick(float dt)
    {
        if (!agent.HasTarget || agent.DistanceToTarget > agent.LoseRadius)
        { agent.Fsm.Transition(agent.Wander); return; }

        if (agent.DistanceToTarget <= agent.ArriveRadius)
        { agent.Fsm.Transition(agent.Arrive); return; }

        Vector3 dir;
        switch (agent.Pathing)
        {
            case PathingMode.Direct: dir = DirectToPlayer(); break;
            case PathingMode.Flank: dir = FlankThenField(dt); break;
            default: dir = FieldOrDirect(); break;
        }

        agent.GoalDirection = dir;
        agent.BehaviourForce = Steering.SeekDirection(agent, dir) * agent.SeekWeight;
    }

    Vector3 DirectToPlayer()
        => Steering.Flat(agent.TargetPosition - agent.Position).normalized;

    // The shared flow field when it has an answer for this cell; otherwise the direct
    // line, so an unreachable or off-grid cell never freezes the enemy in place.
    Vector3 FieldOrDirect()
    {
        var field = FlowField.Instance;
        if (field != null && field.HasDirection)
        {
            Vector3 fieldDir = field.SampleDirection(agent.Position);
            if (fieldDir != Vector3.zero) return fieldDir;
        }
        return DirectToPlayer();
    }

    // Leg 1: head for a point that's diagonally behind the player - both to one side
    // AND behind their facing, so the path reads as a loop rather than a straight
    // lateral cut. Leg 2 (once there, or after the timeout): close in toward a point
    // directly behind the player, so the final approach still comes from behind,
    // instead of beelining at the player from whatever angle leg 1 ended on.
    // Both legs are straight lines to an offset point - the flow field only ever
    // points at the PLAYER'S own cell, not an arbitrary point near them, so neither
    // leg gets obstacle-routing the way a direct chase does. A wall in the way can
    // block either leg; that's a real limitation of aiming at an offset point at all.
    Vector3 FlankThenField(float dt)
    {
        Vector3 behind = -agent.TargetForward; // opposite the player's facing

        if (!flankPlanned)
        {
            float side = UnityEngine.Random.value < 0.5f ? 1f : -1f; // a group splits left and right
            Vector3 sideways = Vector3.Cross(Vector3.up, behind) * side;
            flankOffset = (behind + sideways).normalized * agent.FlankRadius;
            flankPlanned = true;
        }

        if (!flankReached)
        {
            flankTimer += dt;
            Vector3 toPoint = Steering.Flat(agent.TargetPosition + flankOffset - agent.Position);
            if (toPoint.magnitude <= agent.FlankReachDistance || flankTimer >= FlankTimeout)
                flankReached = true;
            else
                return toPoint.normalized;
        }

        // Closing in: aim at a point just behind the player, not the player directly,
        // so the enemy keeps approaching from behind rather than cutting back toward
        // whichever angle is shortest. Once within ArriveRadius of the PLAYER (checked
        // above, every Tick), the state machine hands off to Arrive as normal.
        Vector3 closeOffset = behind * Mathf.Max(agent.ArriveRadius * 0.8f, agent.FlankReachDistance);
        Vector3 toBehind = Steering.Flat(agent.TargetPosition + closeOffset - agent.Position);
        return toBehind.sqrMagnitude > 1e-4f ? toBehind.normalized : DirectToPlayer();
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