using UnityEngine;

public sealed class ZigzagBarrageAbility : MechPartAbility
{
    [Header("Zigzag Barrage")]
    [SerializeField, Range(1, 24)] int count = 8;
    [SerializeField, Range(0.2f, 10)] float spacing = 2.5f;
    [Tooltip("Full distance between the left and right lanes.")]
    [SerializeField, Range(0, 12)] float laneWidth = 3;
    [SerializeField, Range(0.08f, 3)] float delay = 0.2f;
    [SerializeField, Range(0.1f, 10)] float blastRadius = 2;
    [Header("Terrain Placement")]
    [SerializeField] LayerMask terrainLayers = 1;
    [SerializeField, Range(0.1f, 5)] float probeHeight = 1.5f;
    [SerializeField, Range(0.1f, 10)] float maximumDrop = 4;
    [SerializeField, Range(0, 1)] float minimumGroundNormal = 0.65f;
    int next, nodes;
    float step, lane, interval, radius, due, probe, drop, normal;
    LayerMask groundLayers;
    Vector3 forward, previous;
    public static Vector3 NodePosition(Vector3 origin, Vector3 forward, int index, float spacing, float laneWidth)
    {
        forward = Vector3.ProjectOnPlane(forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        return origin + forward * ((index + 1) * spacing)
            + Vector3.Cross(Vector3.up, forward) * ((index % 2 == 0 ? -1 : 1) * laneWidth * 0.5f);
    }

    protected override void Begin()
    {
        nodes = Mathf.Min(Mathf.Clamp(count, 1, 24), DetonationLimit);
        step = Mathf.Clamp(spacing, 0.2f, 10); lane = Mathf.Clamp(laneWidth, 0, 12);
        interval = Mathf.Clamp(delay, 0.08f, 3); radius = Mathf.Clamp(blastRadius, 0.1f, 10);
        probe = Mathf.Clamp(probeHeight, 0.1f, 5); drop = Mathf.Clamp(maximumDrop, 0.1f, 10);
        normal = Mathf.Clamp01(minimumGroundNormal); groundLayers = terrainLayers;
        forward = Vector3.ProjectOnPlane(AimDirection, Vector3.up).normalized;
        previous = Context.Origin;
        next = 0; due = interval;
    }

    protected override void Advance(float deltaTime)
    {
        if (Age < due) return;
        Vector3 candidate = NodePosition(Context.Origin, forward, next, step, lane);
        if (!Queries.Ground(candidate, probe, drop, normal, Context.Owner, groundLayers, out Vector3 point)
            || Queries.Sweep(previous, point - previous, 0.05f, Context.Owner, Context.HitLayers,
                out _, out _, true)
            || Queries.Sweep(Context.Origin, point - Context.Origin, 0.05f, Context.Owner, Context.HitLayers,
                out _, out _, true))
        { Finish(); return; } // Stop the chain at a wall, blocked lane, steep surface or missing ground.
        previous = point;
        next++; due = Age + interval;
        Detonate(point, radius);
        if (next >= nodes) Finish();
    }
    protected override void Release() { }
}
