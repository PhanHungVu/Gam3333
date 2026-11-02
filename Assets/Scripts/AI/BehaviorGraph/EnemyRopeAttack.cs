using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(100)]
public sealed class EnemyRopeAttack : EnemyGraphAttack
{
    public enum RopeBehavior { ThrowAndPull, Orbit }
    [SerializeField] RopeBehavior behavior;
    [SerializeField] Transform rock;
    [Tooltip("Attachment on the visible rig. The root offset is only a fallback for older prefabs.")]
    [SerializeField] Transform ropeAnchor;
    [SerializeField] Material ropeMaterialTemplate;
    [SerializeField] Vector3 handOffset = new Vector3(0.8f, 1.6f, 0.5f);
    [SerializeField] Vector3 heldRockOffset = new Vector3(0.6f, -0.25f, 0.5f);
    [SerializeField, Min(0.2f)] float flightDuration = 0.95f;
    [SerializeField, Min(0)] float arcHeight = 5;
    [SerializeField, Min(0.1f)] float impactRadius = 2.5f;
    [SerializeField, Min(0.1f)] float rockRadius = 0.6f;
    [SerializeField, Min(1)] float pullSpeed = 8;
    [SerializeField, Min(0.1f)] float maximumPullDuration = 2;
    [Header("Orbiting rock")]
    [Tooltip("How long the thrown rock rests on the floor before lifting into its orbit. Does not affect the throw-and-pull variant.")]
    [SerializeField, Min(0)] float orbitGroundPauseDuration = 1.2f;
    [SerializeField, Min(0.01f)] float orbitLiftDuration = 0.35f;
    [SerializeField, Min(1)] float orbitRadius = 6;
    [SerializeField, Min(1)] float orbitDegreesPerSecond = 360;
    [SerializeField, Min(0.1f)] float orbitDuration = 3;
    [SerializeField, Min(0.6f)] float orbitHeight = 1.1f;
    [SerializeField, Min(0.01f)] float orbitAccelerationDuration = 0.4f;
    [SerializeField, Min(1)] float orbitBodyTurnSpeed = 720;
    [Tooltip("Charge toward the live player during the spin, stopping this far away. Keeps the existing navigation and body collision.")]
    [SerializeField, Min(0.5f)] float orbitChaseStopDistance = 2.5f;
    [SerializeField, Min(0)] float orbitChargeSpeed = 9;
    [SerializeField, Min(0.1f)] float orbitChargeAcceleration = 30;
    LineRenderer rope;
    Vector3 start, destination, previous, recoverStart, airbornePosition;
    Material ropeMaterial;
    bool impactApplied, cancelled;
    float windupLift;
    float orbitAngle, orbitVerticalOffset;
    readonly HashSet<CombatHealth> orbitVictims = new HashSet<CombatHealth>();
    public RopeBehavior Behavior => behavior;
    public float OrbitRadius => orbitRadius;
    public int OrbitsStarted { get; private set; }
    public int OrbitContacts { get; private set; }
    public override bool TargetInReach => behavior == RopeBehavior.Orbit
        ? agent.Target != null && agent.DistanceToTarget <= agent.AttackRange + 0.5f : base.TargetInReach;
    public Vector3 LandedPosition => destination;
    public int Impacts { get; private set; }
    Vector3 Hand => ropeAnchor != null ? ropeAnchor.position : transform.position + transform.rotation * handOffset;
    Vector3 HeldPosition => Hand + transform.rotation * heldRockOffset;
    Vector3 LandingMarker => destination + Vector3.down * (rockRadius - 0.06f);
    Vector3 OrbitCenter => transform.position + Vector3.up * orbitVerticalOffset;
    Vector3 OrbitPoint(float angle) => OrbitCenter + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * orbitRadius;
    void ShowWarning(float progress)
    {
        if (behavior == RopeBehavior.Orbit) marker.Disc(OrbitCenter + Vector3.down * (orbitHeight - 0.06f), orbitRadius, progress);
        else marker.Disc(LandingMarker, impactRadius, progress);
    }
    protected override void Awake()
    {
        base.Awake();
        var node = new GameObject("Rock rope"); node.transform.SetParent(transform, false);
        rope = node.AddComponent<LineRenderer>(); rope.positionCount = 12; rope.widthMultiplier = 0.065f;
        rope.useWorldSpace = true; rope.shadowCastingMode = ShadowCastingMode.Off; rope.receiveShadows = false;
        ropeMaterial = ropeMaterialTemplate != null ? new Material(ropeMaterialTemplate)
            : new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        ropeMaterial.name = "Rope (runtime)"; ropeMaterial.hideFlags = HideFlags.DontSave;
        if (ropeMaterial.HasProperty("_BaseColor")) ropeMaterial.SetColor("_BaseColor", Color.gray);
        rope.sharedMaterial = ropeMaterial; ResetRock();
    }
    float Ground(Vector3 p)
    {
        // Sample the destination's actual floor, not the thrower's current height.
        var origin = p; origin.y = Mathf.Max(p.y, transform.position.y) + rockRadius;
        float nearest = float.PositiveInfinity;
        float height = NavTerrain.GroundHeight(p, transform.position.y - 1);
        foreach (var hit in Physics.RaycastAll(origin, Vector3.down, 100, ~0, QueryTriggerInteraction.Ignore))
        {
            if (Ignore(hit.collider) || hit.normal.y < 0.5f || hit.distance >= nearest) continue;
            nearest = hit.distance; height = hit.point.y;
        }
        return height + rockRadius;
    }
    bool Ignore(Collider collider) => collider.transform.IsChildOf(transform)
        || collider.GetComponentInParent<CombatHealth>() != null || collider.GetComponentInParent<Part>() != null;
    public override bool BeginWindup()
    {
        if (rock == null || !base.BeginWindup()) return false;
        destination = committedTarget; destination.y = Ground(destination); impactApplied = cancelled = false;
        orbitVictims.Clear();
        if (behavior == RopeBehavior.Orbit)
        {
            var direction = Steering.Flat(committedTarget - transform.position).normalized;
            if (direction.sqrMagnitude < 0.001f) direction = transform.forward;
            orbitAngle = Mathf.Atan2(direction.z, direction.x);
            orbitVerticalOffset = Ground(transform.position) - rockRadius + orbitHeight - transform.position.y;
            destination = OrbitPoint(orbitAngle); destination.y = Ground(destination);
        }
        ResetRock(); return true;
    }
    public override bool TickWindup(float dt)
    {
        if (behavior == RopeBehavior.Orbit) AlignOrbitMount(OrbitPoint(orbitAngle), dt);
        age += dt; windupLift = Mathf.Sin(Mathf.Clamp01(age / windupDuration) * Mathf.PI * 0.5f) * 0.6f;
        rock.position = HeldPosition + Vector3.up * windupLift;
        ShowWarning(age / windupDuration); DrawRope(); return age >= windupDuration;
    }
    public override void BeginAttack()
    { CurrentStage = Stage.Attack; age = 0; start = previous = airbornePosition = rock.position; }
    public override bool TickAttack(float dt)
    {
        if (CurrentStage == Stage.Recovery || CurrentStage == Stage.Idle) return true;
        if (behavior == RopeBehavior.Orbit && CurrentStage == Stage.Grounded) return TickGrounded(dt);
        if (behavior == RopeBehavior.Orbit && CurrentStage == Stage.OrbitLift) return TickOrbitLift(dt);
        if (behavior == RopeBehavior.Orbit && CurrentStage == Stage.Orbit) return TickOrbit(dt);
        age += dt; float t = Mathf.Clamp01(age / flightDuration);
        var next = Vector3.Lerp(start, destination, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * arcHeight;
        // Only the preserved throw-and-pull rock is intercepted by scenery.
        bool blocked = behavior != RopeBehavior.Orbit && SweepRock(previous, ref next);
        if (behavior == RopeBehavior.Orbit) ResolveOrbitContact(previous, next);
        rock.position = airbornePosition = next; previous = next; DrawRope(); ShowWarning(1);
        if (!blocked && t < 1) return false;
        if (behavior == RopeBehavior.Orbit)
        {
            destination = next; CurrentStage = Stage.Grounded; age = 0; return false;
        }
        destination = next;
        // A wall contact is already a valid landing. Do not teleport it down to the floor.
        if (!impactApplied)
        { blast.Resolve(destination, impactRadius, damage, damage, transform, ~0); impactApplied = true; Impacts++; ImpactSound(); }
        marker.Hide(); return true;
    }
    bool TickGrounded(float dt)
    {
        age += Mathf.Max(0, dt); rock.position = airbornePosition; DrawRope(); ShowWarning(1);
        // The landed rock stays fixed and harmless throughout this readable pause.
        if (age < orbitGroundPauseDuration) return false;
        start = airbornePosition; CurrentStage = Stage.OrbitLift; age = 0; return false;
    }
    bool TickOrbitLift(float dt)
    {
        age += Mathf.Max(0, dt); float t = Mathf.Clamp01(age / Mathf.Max(0.01f, orbitLiftDuration));
        var next = Vector3.Lerp(start, OrbitPoint(orbitAngle), Mathf.SmoothStep(0, 1, t));
        ResolveOrbitContact(airbornePosition, next);
        rock.position = airbornePosition = next; DrawRope(); ShowWarning(1);
        if (t < 1) return false;
        CurrentStage = Stage.Orbit; age = 0; OrbitsStarted++; return false;
    }
    bool SweepRock(Vector3 from, ref Vector3 next)
    {
        bool blocked = false; float nearest = float.PositiveInfinity; var travel = next - from;
        if (travel.sqrMagnitude > 0.0001f)
        {
            foreach (var hit in Physics.SphereCastAll(from, rockRadius, travel.normalized, travel.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (Ignore(hit.collider) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                // Initial overlaps have no usable contact point (Unity reports zero).
                // Stop where the rock already is instead of treating that as world origin.
                next = hit.distance <= 0.0001f ? from : hit.point + hit.normal * rockRadius;
                blocked = true;
            }
        }
        return blocked;
    }
    void ResolveOrbitContact(Vector3 from, Vector3 to)
    {
        var travel = to - from;
        var rotation = travel.sqrMagnitude > 0.00001f ? Quaternion.LookRotation(travel.normalized) : Quaternion.identity;
        var result = blast.ResolveBox((from + to) * 0.5f, new Vector3(rockRadius, rockRadius, travel.magnitude * 0.5f + rockRadius),
            rotation, damage, transform, ~0, from, orbitVictims);
        OrbitContacts += result.Hits;
        if (result.Hits > 0) ImpactSound();
    }
    bool TickOrbit(float dt)
    {
        ChargeDuringOrbit(dt);
        // Subdivide the circular path: a long frame must not turn the sweep into
        // a chord cutting through the safe center or skipping targets.
        float remaining = Mathf.Min(Mathf.Max(0, dt), Mathf.Max(0, orbitDuration - age));
        float maximumStep = 8 / Mathf.Max(1, orbitDegreesPerSecond);
        while (remaining > 0.00001f)
        {
            float step = Mathf.Min(remaining, maximumStep);
            age += step; remaining -= step;
            float speed = Mathf.Lerp(0.4f, 1, Mathf.Clamp01(age / orbitAccelerationDuration));
            orbitAngle += orbitDegreesPerSecond * Mathf.Deg2Rad * step * speed;
            var next = OrbitPoint(orbitAngle);
            ResolveOrbitContact(airbornePosition, next);
            rock.position = airbornePosition = next;
        }
        DrawRope(); ShowWarning(1);
        if (age < orbitDuration - 0.00001f) return false;
        agent.SetMovementIntent(EnemyMovementIntent.Hold, dt);
        BeginRecovery(); return true;
    }
    void ChargeDuringOrbit(float dt)
    {
        if (agent.Target == null) return;
        float gap = Steering.Flat(agent.TargetPosition - transform.position).magnitude - orbitChaseStopDistance;
        if (gap <= 0)
        { agent.SetDirectedMovement(Vector3.zero, 0, preserveFacing: true); return; }
        // Reuse Chase's flow-field direction, then accelerate through the same
        // collision/ground motor as the other special charges and pulls.
        agent.SetMovementIntent(EnemyMovementIntent.Chase, dt, preserveFacing: true);
        float acceleration = Mathf.Max(.1f, orbitChargeAcceleration);
        float speed = Mathf.Min(orbitChargeSpeed, Mathf.Sqrt(2 * acceleration * gap));
        var velocity = Vector3.MoveTowards(agent.Velocity, agent.GoalDirection.normalized * speed, acceleration * Mathf.Max(0, dt));
        velocity = Vector3.ClampMagnitude(velocity, gap / Mathf.Max(.001f, dt));
        agent.SetDirectedMovement(velocity, velocity.magnitude, preserveFacing: true);
    }
    void AlignOrbitMount(Vector3 point, float dt)
    {
        var radial = Steering.Flat(point - transform.position);
        var mount = Steering.Flat(Hand - transform.position);
        if (radial.sqrMagnitude < 0.001f || mount.sqrMagnitude < 0.001f) return;
        // Keep the visible rope mount on the rock's side, including while walking.
        var wanted = Quaternion.AngleAxis(Vector3.SignedAngle(mount, radial, Vector3.up), Vector3.up) * transform.rotation;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, orbitBodyTurnSpeed * Mathf.Max(0, dt));
    }
    void BeginRecovery() { CurrentStage = Stage.Recovery; age = 0; recoverStart = airbornePosition; marker.Hide(); }
    public override void BeginPull() { if (behavior != RopeBehavior.Orbit) { CurrentStage = Stage.Pull; age = 0; } }
    public override bool TickPull(float dt)
    {
        if (behavior == RopeBehavior.Orbit) return true;
        age += dt; DrawRope(); Vector3 to = Steering.Flat(destination - transform.position);
        if (to.magnitude <= 2.01f || age >= maximumPullDuration || age > dt && agent.LastMotionBlocked)
        { agent.SetMovementIntent(EnemyMovementIntent.Hold, dt); CurrentStage = Stage.Recovery; age = 0; recoverStart = rock.position; return true; }
        agent.SetDirectedMovement(to, Mathf.Min(pullSpeed, Mathf.Max(0, to.magnitude - 2) / Mathf.Max(0.001f, dt))); return false;
    }
    public override bool Recover(float dt)
    {
        if (CurrentStage != Stage.Recovery) { CurrentStage = Stage.Recovery; age = 0; recoverStart = rock != null ? rock.position : HeldPosition; }
        if (rock != null) rock.position = airbornePosition = Vector3.Lerp(recoverStart, HeldPosition, Mathf.SmoothStep(0, 1, (age + dt) / recoveryDuration));
        DrawRope(); bool done = base.Recover(dt); if (done) ResetRock(); return done;
    }
    void ResetRock() { windupLift = 0; if (rock != null) { rock.gameObject.SetActive(true); rock.position = HeldPosition; } DrawRope(); }
    void LateUpdate()
    {
        // Keep the landed rock fixed while the existing motor pulls the actor;
        // parenting is retained so death/disable cleanup cannot leak an object.
        if (rock != null)
        {
            // The rig can finish its turn/pose blend after the graph's recovery tick.
            // Re-evaluate the moving attachment here, especially once fully reeled in.
            if (CurrentStage == Stage.Orbit)
            {
                // The shared motor may move after the graph ticks. Follow its
                // final position so the moving enemy remains the orbit's center.
                var next = OrbitPoint(orbitAngle);
                if (Time.deltaTime > 0 && (next - airbornePosition).sqrMagnitude > .000001f)
                    ResolveOrbitContact(airbornePosition, next);
                AlignOrbitMount(next, Time.deltaTime);
                rock.position = airbornePosition = next;
                ShowWarning(1);
            }
            else if (CurrentStage == Stage.Recovery) rock.position = airbornePosition = Vector3.Lerp(recoverStart, HeldPosition, Mathf.SmoothStep(0, 1, age / recoveryDuration));
            else if (CurrentStage == Stage.Attack || CurrentStage == Stage.Pull
                || CurrentStage == Stage.Grounded || CurrentStage == Stage.OrbitLift) rock.position = airbornePosition;
            else rock.position = HeldPosition + Vector3.up * windupLift;
        }
        DrawRope();
    }
    void DrawRope()
    {
        if (rope == null || rock == null) return;
        rope.enabled = isActiveAndEnabled && !cancelled && rock.gameObject.activeInHierarchy;
        var hand = Hand; var end = rock.position;
        // Attach to the near surface of the centered rock, with slack until the pull tensions it.
        var towardHand = hand - end;
        end += towardHand.normalized * Mathf.Min(rockRadius * 0.75f, towardHand.magnitude * 0.5f);
        float sag = Mathf.Min(0.35f, Vector3.Distance(hand, end) * 0.12f);
        if (CurrentStage == Stage.Pull || CurrentStage == Stage.Orbit || CurrentStage == Stage.OrbitLift) sag = 0;
        else if (CurrentStage == Stage.Attack) sag *= 0.35f;
        for (int i = 0; i < rope.positionCount; i++)
        {
            float t = i / (float)(rope.positionCount - 1);
            var point = Vector3.Lerp(hand, end, t) + Vector3.down * (4 * t * (1 - t) * sag);
            point.y = Mathf.Max(point.y, Mathf.Min(hand.y, end.y) - rockRadius * 0.25f);
            rope.SetPosition(i, point);
        }
    }
    public override void Cancel() { base.Cancel(); cancelled = true; orbitVictims.Clear(); ResetRock(); if (rope != null) rope.enabled = false; }
    void OnDestroy() { if (ropeMaterial != null) Destroy(ropeMaterial); }
}
