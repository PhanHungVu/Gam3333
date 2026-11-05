using System;
using Unity.Cinemachine;
using UnityEngine;

// Resolves the existing third-person rig against scenery, without treating actor
// limbs as walls. Collision layers and physical colliders stay owned by gameplay.
[DisallowMultipleComponent, RequireComponent(typeof(CinemachineThirdPersonFollow))]
public sealed class MechCameraObstacles : CinemachineExtension
{
    CinemachineThirdPersonFollow follow;
    Transform ignoredRoot;
    bool avoidanceEnabled;
    float correction;
    RaycastHit[] hits = new RaycastHit[32];
    SphereCollider penetrationProbe;

    protected override void Awake()
    {
        TryGetComponent(out follow);
        base.Awake();
    }

    public void Configure(Transform target, bool avoid)
    {
        if (ignoredRoot != target || avoidanceEnabled != avoid) correction = 0;
        ignoredRoot = target; avoidanceEnabled = avoid;
    }

    protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
    {
        if (stage != CinemachineCore.Stage.Body || vcam != ComponentOwner) return;
        if (follow == null) TryGetComponent(out follow);
        if (follow == null || !follow.IsValid) return;
        follow.CurrentObstacle = null;
        if (deltaTime < 0 || !vcam.PreviousStateIsValid) { correction = 0; deltaTime = -1; }
        var settings = follow.AvoidObstacles;
        if (!avoidanceEnabled || settings.CollisionFilter.value == 0) { correction = 0; return; }

        // Keep both segments of the existing rig: root-to-hand and hand-to-camera.
        // The shoulder segment corrects immediately, as in Third Person Follow.
        follow.GetRigPositions(out var root, out _, out var hand);
        float handCorrection = 0;
        hand = Resolve(root, hand, settings.CameraRadius * 1.05f, -1, ref handCorrection);
        state.RawPosition = Resolve(hand, state.RawPosition, settings.CameraRadius, deltaTime, ref correction);
    }

    Vector3 Resolve(Vector3 start, Vector3 end, float radius, float dt, ref float displacement)
    {
        Vector3 direction = end - start;
        float length = direction.magnitude;
        if (length < Epsilon) return end;
        direction /= length;
        var settings = follow.AvoidObstacles;
        int count;
        while ((count = Physics.SphereCastNonAlloc(start, radius, direction, hits, length,
            settings.CollisionFilter, QueryTriggerInteraction.Ignore)) == hits.Length)
            Array.Resize(ref hits, hits.Length * 2);

        float nearest = float.PositiveInfinity;
        Vector3 contact = end;
        Collider obstacle = null;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i]; var collider = hit.collider;
            if (collider == null || Ignored(collider, settings.IgnoreTag)) continue;
            float distance = hit.distance;
            Vector3 position = hit.point + hit.normal * radius;
            if (distance == 0 && hit.normal == -direction)
            {
                // Sphere casts return an unusable point when the shoulder already
                // overlaps a wall. Resolve that overlap before choosing the hit.
                EnsureProbe(); penetrationProbe.radius = radius;
                if (!Physics.ComputePenetration(penetrationProbe, start, Quaternion.identity,
                    collider, collider.transform.position, collider.transform.rotation, out var push, out float depth)) continue;
                distance = depth - radius;
                position = start + push * depth;
            }
            if (distance >= nearest) continue;
            nearest = distance; contact = position; obstacle = collider;
        }
        Array.Clear(hits, 0, count);
        float desired = obstacle != null ? Vector3.Distance(contact, end) : 0;
        if (obstacle != null) follow.CurrentObstacle = obstacle;
        float damping = desired > displacement ? settings.DampingIntoCollision : settings.DampingFromCollision;
        displacement += dt < 0 ? desired - displacement : Damper.Damp(desired - displacement, damping, dt);
        return end - direction * displacement;
    }

    bool Ignored(Collider collider, string ignoreTag)
    {
        if (collider.GetComponentInParent<CombatRagdoll>() != null) return true;
        if (ignoredRoot != null && collider.transform.IsChildOf(ignoredRoot)) return true;
        if (!string.IsNullOrEmpty(ignoreTag) && collider.CompareTag(ignoreTag)) return true;
        // A health-bearing structure is still scenery. EnemyAgent identifies the
        // moving enemy hierarchy, including default-layer heads and equipped arms.
        return collider.GetComponentInParent<EnemyAgent>() != null
            || collider.GetComponentInParent<MechMovementController>() != null;
    }

    void EnsureProbe()
    {
        if (penetrationProbe != null) return;
        var node = new GameObject("Camera penetration probe") { hideFlags = HideFlags.HideAndDontSave };
        penetrationProbe = node.AddComponent<SphereCollider>();
        penetrationProbe.isTrigger = true;
        // Keep a cooked shape for ComputePenetration without registering a
        // gameplay obstacle or trigger in the physics broad phase.
        var body = node.AddComponent<Rigidbody>();
        body.isKinematic = true; body.useGravity = false; body.detectCollisions = false;
    }

    public override float GetMaxDampTime() => avoidanceEnabled && follow != null
        ? Mathf.Max(follow.AvoidObstacles.DampingIntoCollision, follow.AvoidObstacles.DampingFromCollision) : 0;

    protected override void OnDestroy()
    {
        if (penetrationProbe != null)
        {
            if (Application.isPlaying) Destroy(penetrationProbe.gameObject);
            else DestroyImmediate(penetrationProbe.gameObject);
        }
        base.OnDestroy();
    }
}
