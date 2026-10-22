using System;
using System.Collections.Generic;
using UnityEngine;

// How many targets one damage event hit and killed.
public readonly struct DamageResult
{
    public readonly int Hits, Kills;
    public DamageResult(int hits, int kills) { Hits = hits; Kills = kills; }
}

// Shared damage rules for every attack: a direct hit plus area damage, or a box (melee).
// Never hits the owner, walls block it, and each target is hit once even with several colliders.
// Presentation never applies damage.
public sealed class CombatBlast : IComparer<Collider>
{
    public sealed class Budget
    {
        public float RemainingDamage;
        public int MaximumTargets;
        public readonly HashSet<CombatHealth> Targets = new HashSet<CombatHealth>();
        public void Reset(float damage, int targets)
        { RemainingDamage = Mathf.Max(0, damage); MaximumTargets = Mathf.Max(0, targets); Targets.Clear(); }
    }

    Collider[] overlaps = new Collider[16];
    RaycastHit[] blockers = new RaycastHit[16];
    readonly HashSet<CombatHealth> damaged = new HashSet<CombatHealth>();
    Vector3 sortOrigin;

    public DamageResult Resolve(Vector3 origin, float radius, float damage,
        Transform owner, LayerMask layers, Collider direct = null, Budget budget = null,
        float innerRadius = -1, float waveHalfHeight = 1.5f, HashSet<CombatHealth> hitHistory = null,
        Action<CombatHealth> onHit = null)
    {
        damaged.Clear();
        int hits = 0, kills = 0;
        if (direct != null && !IsOwner(direct, owner))
            Apply(direct.GetComponentInParent<CombatHealth>(), damage, budget, ref hits, ref kills);
        if (radius > 0)
        {
            int count;
            float queryRadius = hitHistory != null ? Mathf.Sqrt(radius * radius + waveHalfHeight * waveHalfHeight) : radius;
            while ((count = Physics.OverlapSphereNonAlloc(origin, queryRadius, overlaps, layers,
                QueryTriggerInteraction.Ignore)) == overlaps.Length)
                Array.Resize(ref overlaps, overlaps.Length * 2);
            // Delayed nodes may land inside world geometry. A ray starting inside a
            // wall cannot detect that wall; do not let such nodes leak damage through it.
            if (budget != null)
                for (int i = 0; i < count; i++)
                    if (!IsOwner(overlaps[i], owner) && overlaps[i].GetComponentInParent<CombatHealth>() == null
                        && SupportsClosestPoint(overlaps[i])
                        && (overlaps[i].ClosestPoint(origin) - origin).sqrMagnitude < 0.000001f)
                    {
                        Array.Clear(overlaps, 0, count);
                        return new DamageResult(hits, kills);
                    }
            // Budget priority is nearest exposed collider, with a stable tie-breaker.
            if (budget != null) { sortOrigin = origin; Array.Sort(overlaps, 0, count, this); }
            for (int i = 0; i < count; i++)
            {
                Collider candidate = overlaps[i];
                if (IsOwner(candidate, owner)) continue;
                var health = candidate.GetComponentInParent<CombatHealth>();
                if (health == null || damaged.Contains(health) || hitHistory != null && hitHistory.Contains(health)) continue;
                if (hitHistory != null)
                {
                    Bounds bounds = candidate.bounds;
                    if (bounds.min.y > origin.y + waveHalfHeight || bounds.max.y < origin.y - waveHalfHeight) continue;
                    float near = Vector3.ProjectOnPlane(ClosestPoint(candidate, origin) - origin, Vector3.up).magnitude;
                    float far = Vector3.ProjectOnPlane(bounds.center - origin, Vector3.up).magnitude
                        + new Vector2(bounds.extents.x, bounds.extents.z).magnitude;
                    if (near > radius || far < innerRadius) continue;
                }
                if (Blocked(origin, candidate, owner, layers)) continue;
                if (Apply(health, damage, budget, ref hits, ref kills))
                { hitHistory?.Add(health); onHit?.Invoke(health); }
            }
            Array.Clear(overlaps, 0, count);
        }
        damaged.Clear();
        return new DamageResult(hits, kills);
    }

    // Damages every target inside an oriented box (melee). Walls are checked along a line
    // from wallOrigin (the attacker) to each target.
    public DamageResult ResolveBox(Vector3 center, Vector3 halfExtents, Quaternion rotation, float damage,
        Transform owner, LayerMask layers, Vector3 wallOrigin)
    {
        damaged.Clear();
        int hits = 0, kills = 0, count;
        while ((count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlaps, rotation, layers,
            QueryTriggerInteraction.Ignore)) == overlaps.Length)
            Array.Resize(ref overlaps, overlaps.Length * 2);
        for (int i = 0; i < count; i++)
        {
            Collider candidate = overlaps[i];
            if (IsOwner(candidate, owner)) continue;
            var health = candidate.GetComponentInParent<CombatHealth>();
            if (health == null || damaged.Contains(health) || Blocked(wallOrigin, candidate, owner, layers)) continue;
            Apply(health, damage, null, ref hits, ref kills);
        }
        Array.Clear(overlaps, 0, count);
        damaged.Clear();
        return new DamageResult(hits, kills);
    }

    // True when something other than the owner or a damageable target is between origin and candidate.
    public bool LineBlocked(Vector3 origin, Collider candidate, Transform owner, LayerMask layers)
        => Blocked(origin, candidate, owner, layers);

    bool Apply(CombatHealth health, float damage, Budget budget, ref int hits, ref int kills)
    {
        if (health == null || !damaged.Add(health)) return false;
        if (budget != null)
        {
            if (budget.RemainingDamage <= 0 || !budget.Targets.Contains(health)
                && budget.Targets.Count >= budget.MaximumTargets) return false;
            damage = Mathf.Min(damage, budget.RemainingDamage);
        }
        float applied = Mathf.Min(health.CurrentHealth, damage);
        if (!health.ApplyDamage(damage)) return false;
        if (budget != null) { budget.RemainingDamage -= applied; budget.Targets.Add(health); }
        hits++;
        if (health.IsDead) kills++;
        return true;
    }

    bool Blocked(Vector3 origin, Collider candidate, Transform owner, LayerMask layers)
    {
        Vector3 offset = ClosestPoint(candidate, origin) - origin;
        if (offset.sqrMagnitude < 0.0001f) return false;
        int count;
        while ((count = Physics.RaycastNonAlloc(origin, offset.normalized, blockers, offset.magnitude,
            layers, QueryTriggerInteraction.Ignore)) == blockers.Length)
            Array.Resize(ref blockers, blockers.Length * 2);
        bool blocked = false;
        for (int i = 0; i < count; i++)
            blocked |= !IsOwner(blockers[i].collider, owner)
                && blockers[i].collider.GetComponentInParent<CombatHealth>() == null;
        Array.Clear(blockers, 0, count);
        return blocked;
    }

    static bool IsOwner(Collider candidate, Transform owner) => owner != null && candidate.transform.IsChildOf(owner);
    static bool SupportsClosestPoint(Collider candidate) => candidate is BoxCollider || candidate is SphereCollider
        || candidate is CapsuleCollider || candidate is MeshCollider mesh && mesh.convex;
    static Vector3 ClosestPoint(Collider candidate, Vector3 origin) => SupportsClosestPoint(candidate)
        ? candidate.ClosestPoint(origin) : candidate.bounds.ClosestPoint(origin);
    public int Compare(Collider a, Collider b)
    {
        int order = (ClosestPoint(a, sortOrigin) - sortOrigin).sqrMagnitude.CompareTo(
            (ClosestPoint(b, sortOrigin) - sortOrigin).sqrMagnitude);
        return order != 0 ? order : a.GetEntityId().CompareTo(b.GetEntityId());
    }
}
