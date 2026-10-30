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
        Move(dt);
    }

    public void Move(float dt)
    {
        agent.GoalDirection = Vector3.zero;
        agent.BehaviourForce = Steering.Wander(agent, dt) * agent.WanderWeight;
    }
}

// Full-speed chase toward the player. Which way it heads depends on the enemy's
// Pathing mode (an Inspector dropdown on EnemyAgent, readable live in Play mode):
//   FlowField - the shared field, routes around walls (Phase 6)
//   Zigzag    - follows the flow field's own line, weaving left-right as it goes
public sealed class ChaseState : EnemyState
{
    float zigzagTimer;
    float zigzagNoiseSeed; // random per-chase offset into the noise field, so a group doesn't sway in unison
    Vector3 zigzagSmoothedDir; // the steady "line" the sway swings around

    public override void Enter()
    {
        agent.FaceTarget = false;
        agent.InAttackRange = false;
        zigzagTimer = 0f;
        zigzagNoiseSeed = UnityEngine.Random.value * 1000f; // a wide spread so enemies sample unrelated parts of the noise
        zigzagSmoothedDir = Vector3.zero; // sentinel: next ZigzagField call snaps to the first real sample
    }

    public override void Tick(float dt)
    {
        if (!agent.HasTarget || agent.DistanceToTarget > agent.LoseRadius)
        { agent.Fsm.Transition(agent.Wander); return; }

        if (agent.DistanceToTarget <= agent.ArriveRadius)
        { agent.Fsm.Transition(agent.Arrive); return; }

        Move(dt);
    }

    public void Move(float dt)
    {
        Vector3 dir;
        switch (agent.Pathing)
        {
            case PathingMode.Zigzag: dir = ZigzagField(dt); break;
            default: dir = FieldOrDirect(); break;
        }

        // Zigzag only: face the player regardless of which way the swing is
        // currently carrying the enemy, so it reads as sidestepping rather than
        // turning side to side with every swing. Other modes keep facing wherever
        // they're actually moving, as before.
        agent.FaceTarget = agent.Pathing == PathingMode.Zigzag;

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

    // Same aim-point structure as before: project out ahead along a smoothed version
    // of the flow field's direction, then push that point sideways. The difference
    // is what generates the sideways amount - Perlin noise instead of a sine wave.
    // A sine wave repeats identically every cycle, which reads as scripted once you
    // watch it for a few seconds. Perlin noise is just as smooth, but never repeats,
    // so the same visible sway doesn't settle into an obviously mechanical pattern.
    Vector3 ZigzagField(float dt)
    {
        Vector3 rawDir = FieldOrDirect();
        if (rawDir.sqrMagnitude < 1e-6f) return rawDir;

        zigzagSmoothedDir = zigzagSmoothedDir == Vector3.zero
            ? rawDir
            : Vector3.Slerp(zigzagSmoothedDir, rawDir, dt * 2f).normalized; // ~0.5s to mostly catch up to a new direction

        zigzagTimer += dt;
        // PerlinNoise returns roughly [0,1]; remap to [-1,1] so it swings both sides
        // of the line, not just to one side of it.
        float noise = Mathf.PerlinNoise(zigzagNoiseSeed, zigzagTimer * agent.ZigzagNoiseSpeed);
        float lateral = (noise * 2f - 1f) * agent.ZigzagAmplitude;

        Vector3 perpendicular = Vector3.Cross(Vector3.up, zigzagSmoothedDir); // unit length: both inputs unit and perpendicular
        Vector3 aimPoint = agent.Position + zigzagSmoothedDir * agent.ZigzagLookAhead + perpendicular * lateral;

        Vector3 toAim = Steering.Flat(aimPoint - agent.Position);
        return toAim.sqrMagnitude > 1e-6f ? toAim.normalized : zigzagSmoothedDir;
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

        Move(dt);
    }

    public void Move(float dt)
    {
        agent.GoalDirection = Steering.Flat(agent.TargetPosition - agent.Position).normalized;
        agent.BehaviourForce = Steering.Arrive(agent, agent.TargetPosition, agent.ArriveRadius, agent.AttackRange) * agent.SeekWeight;
        agent.InAttackRange = agent.DistanceToTarget <= agent.AttackRange + 0.5f;
    }
}
