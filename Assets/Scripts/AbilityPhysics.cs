using System;
using UnityEngine;

// Reusable non-allocating queries for placement, moving bomblets and collision-limited knockback.
public sealed class AbilityPhysics
{
    RaycastHit[] hits = new RaycastHit[16];
    Collider[] overlaps = new Collider[16];
    static bool Ignore(Collider c, Transform owner, bool worldOnly) => c.transform.IsChildOf(owner)
        || worldOnly && c.GetComponentInParent<CombatHealth>() != null;

    public bool Sweep(Vector3 origin, Vector3 displacement, float radius, Transform owner, LayerMask layers,
        out Vector3 point, out Collider collider, bool worldOnly = false)
    {
        point = origin; collider = null;
        int count;
        while ((count = Physics.OverlapSphereNonAlloc(origin, radius, overlaps, layers,
            QueryTriggerInteraction.Ignore)) == overlaps.Length) Array.Resize(ref overlaps, overlaps.Length * 2);
        for (int i = 0; i < count; i++)
            if (!Ignore(overlaps[i], owner, worldOnly)) { collider = overlaps[i]; break; }
        Array.Clear(overlaps, 0, count);
        if (collider != null) return true;
        float distance = displacement.magnitude;
        if (distance < 0.00001f) return false;
        while ((count = Physics.SphereCastNonAlloc(origin, radius, displacement / distance, hits, distance, layers,
            QueryTriggerInteraction.Ignore)) == hits.Length) Array.Resize(ref hits, hits.Length * 2);
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
            if (!Ignore(hits[i].collider, owner, worldOnly) && hits[i].distance < nearest)
            { nearest = hits[i].distance; collider = hits[i].collider; }
        Array.Clear(hits, 0, count);
        if (collider == null) { point = origin + displacement; return false; }
        point = origin + displacement.normalized * Mathf.Max(0, nearest - 0.01f);
        return true;
    }

    public bool Ground(Vector3 candidate, float probeHeight, float drop, float minimumUp,
        Transform owner, LayerMask layers, out Vector3 point)
    {
        point = candidate;
        Vector3 top = candidate + Vector3.up * probeHeight;
        int count;
        while ((count = Physics.RaycastNonAlloc(top, Vector3.down, hits, probeHeight + drop, layers,
            QueryTriggerInteraction.Ignore)) == hits.Length) Array.Resize(ref hits, hits.Length * 2);
        float nearest = float.MaxValue;
        bool found = false;
        for (int i = 0; i < count; i++)
            if (!Ignore(hits[i].collider, owner, true) && hits[i].distance < nearest)
            {
                nearest = hits[i].distance;
                point = hits[i].point + Vector3.up * 0.12f;
                found = hits[i].normal.y >= minimumUp;
            }
        Array.Clear(hits, 0, count);
        return found;
    }

    public void Knockback(CombatHealth health, Vector3 origin, float distance, LayerMask layers)
    {
        if (health == null || health.IsDead || distance <= 0) return;
        var collider = health.GetComponentInChildren<Collider>();
        if (collider == null) return;
        Vector3 direction = Vector3.ProjectOnPlane(health.transform.position - origin, Vector3.up).normalized;
        Bounds bounds = collider.bounds;
        int count;
        while ((count = Physics.BoxCastNonAlloc(bounds.center, bounds.extents * 0.95f, direction, hits,
            Quaternion.identity, distance, layers, QueryTriggerInteraction.Ignore)) == hits.Length)
            Array.Resize(ref hits, hits.Length * 2);
        for (int i = 0; i < count; i++)
            if (!hits[i].transform.IsChildOf(health.transform)) distance = Mathf.Min(distance, Mathf.Max(0, hits[i].distance - 0.02f));
        Array.Clear(hits, 0, count);
        Vector3 displacement = direction * distance;
        if (health.TryGetComponent<CharacterController>(out var controller)) controller.Move(displacement);
        else if (health.TryGetComponent<Rigidbody>(out var body)) body.position += displacement;
        else health.transform.position += displacement;
    }
}
