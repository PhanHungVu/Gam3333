using System.Collections.Generic;
using UnityEngine;

// Port of fish.gd + the driver loop from test_flocking.gd (_physics_process).
// Add to an enemy: it wanders until the player is close, chases, then arrives at attack range.
// Kinematic (moves the transform itself), so it needs no Rigidbody/CharacterController.
// Works on the XZ plane; height follows the ground (see Snap To Ground).
// Demo switch: how ChaseState decides which way to head. Read every frame, so it can
// be flipped live in Play mode. A stopgap until the per-type state classes exist.
public enum PathingMode
{
    FlowField, // routes around walls using the shared flow field (default)
    Zigzag     // follows the flow field's own line, swaying left-right as it goes
}

// Movement intentions only; the existing agent still owns steering and motion.
public enum EnemyMovementIntent { Hold, Wander, Chase, Arrive, Retreat, Directed }

public sealed class EnemyAgent : MonoBehaviour
{
    // Every active enemy. Enable/disable keeps this in sync, so CombatHealth's
    // SetActive(false) on death removes the enemy from the horde automatically.
    public static readonly List<EnemyAgent> All = new List<EnemyAgent>();

    [Header("Target")]
    [Tooltip("Leave empty to find the object tagged Player.")]
    [SerializeField] Transform target;

    [Header("Movement (Properties)")]
    [SerializeField, Min(0.1f)] float maxSpeed = 6f;
    [SerializeField, Min(0.1f)] float maxForce = 15f;      // ~ acceleration in m/s^2
    [SerializeField, Range(0.1f, 1f)] float wanderSpeedFraction = 0.4f;
    [SerializeField] float turnSpeed = 240f;               // degrees/sec

    [Header("Awareness")]
    [SerializeField] float detectRadius = 40f;             // Wander -> Chase
    [SerializeField] float loseRadius = 60f;               // back to Wander (keep > detect)
    [SerializeField] float arriveRadius = 12f;             // Chase -> Arrive, starts slowing
    [SerializeField] float attackRange = 6f;               // stops here

    [Header("Crowd (flocking)")]
    [SerializeField] float perceptionRadius = 10f;
    [SerializeField] float separationRadius = 3.5f;
    [SerializeField] float separationWeight = 1.5f;
    [SerializeField] float alignmentWeight = 0.4f;
    [SerializeField] float cohesionWeight = 0f;            // off: fights "spread out around the player"

    [Header("Behaviour weights")]
    [SerializeField] float seekWeight = 1f;
    [SerializeField] float wanderWeight = 1f;
    [SerializeField] float avoidWeight = 2.2f;

    [Header("Pathing (demo switch)")]
    [SerializeField] PathingMode pathingMode = PathingMode.FlowField;
    [Tooltip("Zigzag only: how far sideways (in units) the aim point can swing off the flow field's line. Bigger = a more obvious sway.")]
    [SerializeField] float zigzagAmplitude = 6f;
    [Tooltip("Zigzag only: how fast the underlying noise changes. Keep this moderate - too fast and velocity can't keep up with the sway (same issue as a fast sine wave would have).")]
    [SerializeField] float zigzagNoiseSpeed = 0.5f;
    [Tooltip("Zigzag only: how far ahead along the line the aim point projects. Smaller = sharper, more visible curves; larger = gentler sweeps.")]
    [SerializeField] float zigzagLookAhead = 6f;

    [Header("Obstacles")]
    [Tooltip("Put enemies on their own layer and exclude it here for cleaner probes.")]
    [SerializeField] LayerMask obstacleMask = ~0;
    [SerializeField] float bodyRadius = 0.6f;
    [SerializeField] float probeHeight = 1f;               // probe sphere centre above the pivot
    [SerializeField] float probeDistance = 3f;
    [SerializeField] float skin = 0.05f;

    [Header("Ground")]
    [SerializeField] bool snapToGround = true;
    [SerializeField] LayerMask groundMask = ~0;
    [SerializeField] float snapRange = 2f;
    [SerializeField] float snapSpeed = 12f;
    [Tooltip("Only used for the off-a-ledge fallback below, not the normal ramp-following case.")]
    [SerializeField] float fallGravity = 25f; // matches the mech's own Gravity magnitude, for a consistent feel
    [Tooltip("Fixed distance from this model's pivot down to its feet - a property of the mesh, not of wherever it spawns. For the current cube enemy this is 1.")]
    [SerializeField] float pivotHeight = 1f;
    [Tooltip("Matches the Plane's Y position (0). Change this if the floor ever moves, or if a level ever has more than one flat height.")]
    [SerializeField] float groundY = 0f;

    [Header("Debug (read-only)")]
    [SerializeField] string debugState;
    [Header("Decision ownership")]
    [Tooltip("Behavior Graph supplies movement intentions. Disables the legacy state machine; missing/stopped graphs hold position.")]
    [SerializeField] bool externalDecisions;
    public bool ExternalDecisions => externalDecisions;
    int intentionFrame = -1;
    EnemyMovementIntent intention;
    Vector3 directedVelocity;
    bool preserveGraphFacing;
    public bool LastMotionBlocked { get; private set; }

    // ---- read by Steering and the states ----
    public Vector3 Position => transform.position;
    public Vector3 Forward => transform.forward;
    public Vector3 Velocity => velocity;
    public float MaxSpeed => maxSpeed;
    public float MaxForce => maxForce;
    public float WanderSpeed => maxSpeed * wanderSpeedFraction;
    public float SeparationRadius => separationRadius;
    public float ProbeDistance => probeDistance;
    public float DetectRadius => detectRadius;
    public float LoseRadius => loseRadius;
    public float ArriveRadius => arriveRadius;
    public float AttackRange => attackRange;
    public float SeekWeight => seekWeight;
    public float WanderWeight => wanderWeight;
    public PathingMode Pathing => pathingMode;
    public float ZigzagAmplitude => zigzagAmplitude;
    public float ZigzagNoiseSpeed => zigzagNoiseSpeed;
    public float ZigzagLookAhead => zigzagLookAhead;

    public bool HasTarget => target != null;
    public Transform Target => target != null && target.gameObject.activeInHierarchy ? target : null;
    public Vector3 TargetPosition => target != null ? target.position : transform.position;
    public float DistanceToTarget { get; private set; } = float.PositiveInfinity;

    // ---- written by the states ----
    public Vector3 BehaviourForce { get; set; }   // == fish.behaviour_force
    public Vector3 GoalDirection { get; set; }    // where the state wants to go (used by obstacle avoidance)
    public bool FaceTarget { get; set; }          // face the player instead of the travel direction
    public bool InAttackRange { get; set; }       // hook for the future attack logic
    public float WanderAngle { get; set; }        // persists across frames so wander drifts smoothly

    public EnemyStateMachine Fsm { get; } = new EnemyStateMachine();
    public WanderState Wander { get; } = new WanderState();
    public ChaseState Chase { get; } = new ChaseState();
    public ArriveState Arrive { get; } = new ArriveState();

    Vector3 velocity;
    bool hasGround;
    float fallSpeed; // accumulated downward speed while off a ledge (fallback case only)
    float nextTargetSearch;
    readonly List<EnemyAgent> neighbors = new List<EnemyAgent>(16);

    void Awake()
    {
        Wander.Init(this); Chase.Init(this); Arrive.Init(this);
    }

    void OnEnable()
    {
        All.Add(this);
        WanderAngle = UnityEngine.Random.value * Mathf.PI * 2f;
        velocity = Vector3.zero;
        intentionFrame = -1;
        if (!externalDecisions) Fsm.Transition(Wander);
    }

    void OnDisable() => All.Remove(this);

    // The ground an enemy starts on: generated terrain's height there if there is terrain, else the fallback.
    public static float SpawnGroundY(Vector3 position, float fallback) => NavTerrain.GroundHeight(position, fallback);

    void Start()
    {
        // Don't trust the Y this spawned at - a spawn point can sit above the real
        // floor (on a pillar, say), and if we treated that as correct we'd float
        // there forever. On generated terrain the ground height comes from the terrain
        // itself; on a flat scene it is the known groundY - no raycast needed either way.
        Vector3 p = transform.position;
        p.y = SpawnGroundY(p, groundY) + pivotHeight;
        transform.position = p;
        hasGround = true; // the ongoing SnapToGround below still raycasts every frame on its own
    }

    void Update()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        ResolveTarget();
        EnemySpatialHash.EnsureBuilt(All);

        // 1. the active state decides where we want to go (sets BehaviourForce)
        if (externalDecisions)
        {
            // No new command this frame means stop, never resume a stale chase.
            if (intentionFrame != Time.frameCount) SetMovementIntent(EnemyMovementIntent.Hold, dt);
            debugState = "Graph: " + intention;
        }
        else
        {
            Fsm.Tick(dt);
            debugState = Fsm.Current?.GetType().Name;
        }

        // Committed charges/pulls still use this motor's collision and ground
        // handling, but do not let flocking bend the telegraphed direction.
        if (externalDecisions && intention == EnemyMovementIntent.Directed)
        {
            velocity = directedVelocity;
            Move(velocity * dt); if (!preserveGraphFacing) Face(dt); SnapToGround(dt); return;
        }

        // 2. crowd + obstacle forces on top, like Flocking.compute_flocking_force
        neighbors.Clear();
        EnemySpatialHash.Query(this, perceptionRadius, neighbors);

        Vector3 force = BehaviourForce;
        force += Steering.Separation(this, neighbors) * separationWeight;
        if (alignmentWeight > 0f) force += Steering.Alignment(this, neighbors) * alignmentWeight;
        if (cohesionWeight > 0f) force += Steering.Cohesion(this, neighbors) * cohesionWeight;
        force += Steering.AvoidObstacles(this) * avoidWeight;

        // 3. integrate
        ApplyForce(Vector3.ClampMagnitude(force, maxForce), dt);
    }

    public void RefreshTarget() => ResolveTarget();

    public void SetMovementIntent(EnemyMovementIntent next, float dt, bool preserveFacing = false)
    {
        if (!externalDecisions) return;
        preserveGraphFacing = preserveFacing;
        intention = next; intentionFrame = Time.frameCount;
        switch (next)
        {
            case EnemyMovementIntent.Wander: Wander.Move(dt); break;
            case EnemyMovementIntent.Chase: Chase.Move(dt); break;
            case EnemyMovementIntent.Arrive: Arrive.Move(dt); break;
            case EnemyMovementIntent.Retreat:
                FaceTarget = true; GoalDirection = Steering.Flat(Position - TargetPosition).normalized;
                BehaviourForce = Steering.SeekDirection(this, GoalDirection) * seekWeight;
                break;
            default:
                FaceTarget = false; GoalDirection = Vector3.zero;
                BehaviourForce = Vector3.ClampMagnitude(-velocity / Mathf.Max(0.001f, dt), maxForce);
                break;
        }
        // The graph requests attacks explicitly, not the legacy range hook.
        InAttackRange = false;
    }

    public void SetDirectedMovement(Vector3 direction, float speed, bool preserveFacing = false)
    {
        if (!externalDecisions) return;
        preserveGraphFacing = preserveFacing;
        intention = EnemyMovementIntent.Directed; intentionFrame = Time.frameCount;
        directedVelocity = Steering.Flat(direction).normalized * Mathf.Max(0, speed);
        GoalDirection = directedVelocity.normalized; FaceTarget = false; InAttackRange = false;
    }

    void ResolveTarget()
    {
        if (target == null || !target.gameObject.activeInHierarchy)
        {
            target = null;
            if (Time.time >= nextTargetSearch)
            {
                nextTargetSearch = Time.time + 0.5f; // don't search every frame
                var player = GameObject.FindWithTag("Player");
                if (player != null) target = player.transform;
            }
        }

        if (target == null) { DistanceToTarget = float.PositiveInfinity; return; }
        Vector3 d = target.position - transform.position;
        d.y = 0f;
        DistanceToTarget = d.magnitude;
    }

    void ApplyForce(Vector3 force, float dt)
    {
        velocity += force * dt;
        velocity.y = 0f;
        velocity = Vector3.ClampMagnitude(velocity, maxSpeed);

        Move(velocity * dt);
        // A committed graph attack keeps the visual swing aligned to the
        // existing attack's snapshotted strike direction.
        if (!externalDecisions || intention != EnemyMovementIntent.Hold && !preserveGraphFacing) Face(dt);
        SnapToGround(dt);
    }

    // Port of fish._move_with_collision: hard collision the steering can't overpower.
    // The fish slid along one grid axis; in 3D we slide along the surface we hit.
    void Move(Vector3 motion)
    {
        LastMotionBlocked = false;
        float dist = motion.magnitude;
        if (dist < 1e-5f) return;

        if (TryProbe(motion / dist, dist + skin, out RaycastHit hit))
        {
            LastMotionBlocked = true;
            Vector3 n = Steering.Flat(hit.normal).normalized;
            motion = Vector3.ProjectOnPlane(motion, n);
            velocity = Vector3.ProjectOnPlane(velocity, n); // stop pushing into the wall

            float slide = motion.magnitude;
            if (slide < 1e-5f || TryProbe(motion / slide, slide + skin, out _)) return; // cornered: hold position
        }
        transform.position += motion;
    }

    // Sphere probe from body height. Ignores things that shouldn't count as obstacles.
    // Note: only the NEAREST hit is examined, so a wall directly behind another enemy
    // is missed for a frame or two. Putting enemies on their own layer avoids that.
    public bool TryProbe(Vector3 dir, float distance, out RaycastHit hit)
    {
        Vector3 origin = transform.position + Vector3.up * probeHeight;
        if (!Physics.SphereCast(origin, bodyRadius, dir, out hit, distance, obstacleMask, QueryTriggerInteraction.Ignore))
            return false;

        if (hit.normal.y > 0.7f) return false;                                    // walkable ground/ramp
        Transform t = hit.collider.transform;
        if (t.IsChildOf(transform)) return false;                                // own body
        if (target != null && t.IsChildOf(target)) return false;                  // the player is the goal, not an obstacle
        if (hit.collider.GetComponentInParent<EnemyAgent>() != null) return false; // crowds are separation's job
        return true;
    }

    void Face(float dt)
    {
        Vector3 dir = (FaceTarget && target != null) ? Steering.Flat(target.position - transform.position) : velocity;
        if (dir.sqrMagnitude < 0.01f) return;
        Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up); // assumes the model faces +Z
        transform.rotation = Quaternion.RotateTowards(transform.rotation, want, turnSpeed * dt);
    }

    void SnapToGround(float dt)
    {
        if (!snapToGround || !hasGround) return;
        Vector3 p = transform.position;

        // Normal case: ground is within snapRange, so ease toward it - this is what
        // makes walking up/down a gentle ramp look smooth instead of stair-stepping.
        if (RaycastGroundIgnoringSelf(p + Vector3.up * 0.5f, 0.5f + snapRange, out RaycastHit hit))
        {
            fallSpeed = 0f; // standing on something within range - not falling
            p.y = Mathf.MoveTowards(p.y, hit.point.y + pivotHeight, snapSpeed * dt);
            transform.position = p;
            return;
        }

        // Fallback: the enemy just walked off a ledge taller than snapRange (e.g. off
        // the ramp platform's edge instead of down the slope). There's no Rigidbody
        // here, so this builds up its own downward speed like simple gravity and
        // clamps to the ground the moment it reaches it - a real fall, not a teleport,
        // without needing physics simulation to do it.
        const float fallbackRange = 50f;
        if (RaycastGroundIgnoringSelf(p + Vector3.up * 0.5f, 0.5f + fallbackRange, out RaycastHit farHit))
        {
            fallSpeed += fallGravity * dt;
            float landingY = farHit.point.y + pivotHeight;
            p.y = Mathf.Max(p.y - fallSpeed * dt, landingY);
            if (p.y <= landingY) fallSpeed = 0f; // landed
            transform.position = p;
        }
    }

    // Plain Physics.Raycast can register a hit against the enemy's OWN colliders if
    // the probe origin isn't safely inside all of them (true for a multi-part model
    // with several separate colliders, unlike the old single convex cube) - or at all,
    // if any part uses a Mesh Collider, which has no "safe from inside" behaviour the
    // way a Box Collider does. RaycastAll + filtering out anything under this enemy's
    // own transform, same ancestry check TryProbe already uses, fixes that properly
    // rather than relying on a layer mask always being set correctly.
    static readonly RaycastHit[] groundHitsBuffer = new RaycastHit[16];

    bool RaycastGroundIgnoringSelf(Vector3 origin, float maxDistance, out RaycastHit result)
    {
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHitsBuffer, maxDistance,
            groundMask, QueryTriggerInteraction.Ignore);

        bool found = false;
        result = default;
        float nearest = float.PositiveInfinity;

        for (int i = 0; i < count; i++)
        {
            RaycastHit h = groundHitsBuffer[i];
            if (h.collider.transform.IsChildOf(transform)) continue; // part of this enemy's own body - not ground
            if (h.distance < nearest) { nearest = h.distance; result = h; found = true; }
        }
        return found;
    }

    // Equivalent of fish.debug_draw_radii: select an enemy to see its ranges.
    void OnDrawGizmosSelected()
    {
        Vector3 p = transform.position;
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(p, detectRadius);
        Gizmos.color = new Color(1f, 0.5f, 0f); Gizmos.DrawWireSphere(p, loseRadius);
        Gizmos.color = Color.cyan; Gizmos.DrawWireSphere(p, arriveRadius);
        Gizmos.color = Color.red; Gizmos.DrawWireSphere(p, attackRange);
        Gizmos.color = new Color(0f, 1f, 0f, 0.6f); Gizmos.DrawWireSphere(p, separationRadius);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(p + Vector3.up * probeHeight + Steering.Flat(velocity).normalized * probeDistance, bodyRadius);
    }
}
