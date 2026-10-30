using UnityEngine;
using UnityEngine.Rendering;

public sealed class EnemyRopeAttack : EnemyGraphAttack
{
    [SerializeField] Transform rock;
    [SerializeField] Material ropeMaterialTemplate;
    [SerializeField] Vector3 handOffset = new Vector3(0.8f, 1.6f, 0.5f);
    [SerializeField, Min(0.2f)] float flightDuration = 0.95f;
    [SerializeField, Min(0)] float arcHeight = 5;
    [SerializeField, Min(0.1f)] float impactRadius = 2.5f;
    [SerializeField, Min(0.1f)] float rockRadius = 0.6f;
    [SerializeField, Min(1)] float pullSpeed = 8;
    [SerializeField, Min(0.1f)] float maximumPullDuration = 2;
    LineRenderer rope;
    Vector3 start, destination, previous, recoverStart, airbornePosition;
    Material ropeMaterial;
    bool impactApplied;
    public Vector3 LandedPosition => destination;
    public int Impacts { get; private set; }
    Vector3 Hand => transform.TransformPoint(handOffset);
    protected override void Awake()
    {
        base.Awake();
        var node = new GameObject("Rock rope"); node.transform.SetParent(transform, false);
        rope = node.AddComponent<LineRenderer>(); rope.positionCount = 2; rope.widthMultiplier = 0.065f;
        rope.useWorldSpace = true; rope.shadowCastingMode = ShadowCastingMode.Off; rope.receiveShadows = false;
        ropeMaterial = ropeMaterialTemplate != null ? new Material(ropeMaterialTemplate)
            : new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
        ropeMaterial.name = "Rope (runtime)"; ropeMaterial.hideFlags = HideFlags.DontSave;
        if (ropeMaterial.HasProperty("_BaseColor")) ropeMaterial.SetColor("_BaseColor", Color.gray);
        rope.sharedMaterial = ropeMaterial; ResetRock();
    }
    float Ground(Vector3 p) => NavTerrain.GroundHeight(p, transform.position.y - 1) + rockRadius;
    public override bool BeginWindup()
    {
        if (rock == null || !base.BeginWindup()) return false;
        destination = committedTarget; destination.y = Ground(destination); impactApplied = false;
        ResetRock(); return true;
    }
    public override bool TickWindup(float dt)
    {
        age += dt; rock.position = Hand + Vector3.up * Mathf.Sin(Mathf.Clamp01(age / windupDuration) * Mathf.PI * 0.5f) * 0.6f;
        marker.Disc(destination, impactRadius, age / windupDuration); DrawRope(); return age >= windupDuration;
    }
    public override void BeginAttack()
    { CurrentStage = Stage.Attack; age = 0; start = previous = airbornePosition = rock.position; }
    public override bool TickAttack(float dt)
    {
        age += dt; float t = Mathf.Clamp01(age / flightDuration);
        var next = Vector3.Lerp(start, destination, t) + Vector3.up * Mathf.Sin(t * Mathf.PI) * arcHeight;
        bool blocked = false; float nearest = float.PositiveInfinity; var travel = next - previous;
        if (travel.sqrMagnitude > 0.0001f)
        {
            foreach (var hit in Physics.SphereCastAll(previous, rockRadius, travel.normalized, travel.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit.transform.IsChildOf(transform) || hit.collider.GetComponentInParent<CombatHealth>() != null
                    || hit.collider.GetComponentInParent<Part>() != null || hit.distance >= nearest) continue;
                nearest = hit.distance; next = hit.point + hit.normal * rockRadius; blocked = true;
            }
        }
        rock.position = airbornePosition = next; previous = next; DrawRope(); marker.Disc(destination, impactRadius, 1);
        if (!blocked && t < 1) return false;
        destination = next; destination.y = Ground(destination); rock.position = airbornePosition = destination;
        if (!impactApplied)
        { blast.Resolve(destination, impactRadius, damage, transform, ~0); impactApplied = true; Impacts++; ImpactSound(); }
        marker.Hide(); return true;
    }
    public override void BeginPull() { CurrentStage = Stage.Pull; age = 0; }
    public override bool TickPull(float dt)
    {
        age += dt; DrawRope(); Vector3 to = Steering.Flat(destination - transform.position);
        if (to.magnitude < 2 || age >= maximumPullDuration || age > dt && agent.LastMotionBlocked)
        { agent.SetMovementIntent(EnemyMovementIntent.Hold, dt); CurrentStage = Stage.Recovery; age = 0; recoverStart = rock.position; return true; }
        agent.SetDirectedMovement(to, pullSpeed); return false;
    }
    public override bool Recover(float dt)
    {
        if (CurrentStage != Stage.Recovery) { CurrentStage = Stage.Recovery; age = 0; recoverStart = rock != null ? rock.position : Hand; }
        if (rock != null) rock.position = Vector3.Lerp(recoverStart, Hand, Mathf.Clamp01((age + dt) / recoveryDuration));
        DrawRope(); bool done = base.Recover(dt); if (done) ResetRock(); return done;
    }
    void ResetRock() { if (rock != null) { rock.gameObject.SetActive(true); rock.position = Hand; } DrawRope(); }
    void LateUpdate()
    {
        // Keep the landed rock fixed while the existing motor pulls the actor;
        // parenting is retained so death/disable cleanup cannot leak an object.
        if (rock != null && (CurrentStage == Stage.Attack || CurrentStage == Stage.Pull)) rock.position = airbornePosition;
        DrawRope();
    }
    void DrawRope() { if (rope == null || rock == null) return; rope.enabled = isActiveAndEnabled; rope.SetPosition(0, Hand); rope.SetPosition(1, rock.position); }
    public override void Cancel() { base.Cancel(); ResetRock(); if (rope != null) rope.enabled = false; }
    void OnDestroy() { if (ropeMaterial != null) Destroy(ropeMaterial); }
}
