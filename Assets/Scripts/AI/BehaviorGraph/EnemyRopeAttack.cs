using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(100)]
public sealed class EnemyRopeAttack : EnemyGraphAttack
{
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
    LineRenderer rope;
    Vector3 start, destination, previous, recoverStart, airbornePosition;
    Material ropeMaterial;
    bool impactApplied, cancelled;
    float windupLift;
    public Vector3 LandedPosition => destination;
    public int Impacts { get; private set; }
    Vector3 Hand => ropeAnchor != null ? ropeAnchor.position : transform.position + transform.rotation * handOffset;
    Vector3 HeldPosition => Hand + transform.rotation * heldRockOffset;
    Vector3 LandingMarker => destination + Vector3.down * (rockRadius - 0.06f);
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
        ResetRock(); return true;
    }
    public override bool TickWindup(float dt)
    {
        age += dt; windupLift = Mathf.Sin(Mathf.Clamp01(age / windupDuration) * Mathf.PI * 0.5f) * 0.6f;
        rock.position = HeldPosition + Vector3.up * windupLift;
        marker.Disc(LandingMarker, impactRadius, age / windupDuration); DrawRope(); return age >= windupDuration;
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
                if (Ignore(hit.collider) || hit.distance >= nearest) continue;
                nearest = hit.distance;
                // Initial overlaps have no usable contact point (Unity reports zero).
                // Stop where the rock already is instead of treating that as world origin.
                next = hit.distance <= 0.0001f ? previous : hit.point + hit.normal * rockRadius;
                blocked = true;
            }
        }
        rock.position = airbornePosition = next; previous = next; DrawRope(); marker.Disc(LandingMarker, impactRadius, 1);
        if (!blocked && t < 1) return false;
        // A wall contact is already a valid landing. Do not teleport it down to the floor.
        destination = next; rock.position = airbornePosition = destination;
        if (!impactApplied)
        { blast.Resolve(destination, impactRadius, damage, damage, transform, ~0); impactApplied = true; Impacts++; ImpactSound(); }
        marker.Hide(); return true;
    }
    public override void BeginPull() { CurrentStage = Stage.Pull; age = 0; }
    public override bool TickPull(float dt)
    {
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
            if (CurrentStage == Stage.Attack || CurrentStage == Stage.Pull || CurrentStage == Stage.Recovery) rock.position = airbornePosition;
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
        if (CurrentStage == Stage.Pull) sag = 0;
        else if (CurrentStage == Stage.Attack) sag *= 0.35f;
        for (int i = 0; i < rope.positionCount; i++)
        {
            float t = i / (float)(rope.positionCount - 1);
            var point = Vector3.Lerp(hand, end, t) + Vector3.down * (4 * t * (1 - t) * sag);
            point.y = Mathf.Max(point.y, Mathf.Min(hand.y, end.y) - rockRadius * 0.25f);
            rope.SetPosition(i, point);
        }
    }
    public override void Cancel() { base.Cancel(); cancelled = true; ResetRock(); if (rope != null) rope.enabled = false; }
    void OnDestroy() { if (ropeMaterial != null) Destroy(ropeMaterial); }
}
