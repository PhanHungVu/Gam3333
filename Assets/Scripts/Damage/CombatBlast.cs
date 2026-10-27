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
public sealed class CombatBlast
{
    Collider[] overlaps = new Collider[16];
    RaycastHit[] blockers = new RaycastHit[16];
    readonly HashSet<CombatHealth> damaged = new HashSet<CombatHealth>();

    public DamageResult Resolve(Vector3 origin, float radius, float damage,
        Transform owner, LayerMask layers, Collider direct = null)
    {
        damaged.Clear();
        int hits = 0, kills = 0;
        CombatTeam attacker = CombatTeams.Of(owner);
        if (direct != null && !IsOwner(direct, owner))
            Apply(direct.GetComponentInParent<CombatHealth>(), damage, attacker, ref hits, ref kills);
        if (radius > 0)
        {
            int count;
            while ((count = Physics.OverlapSphereNonAlloc(origin, radius, overlaps, layers,
                QueryTriggerInteraction.Ignore)) == overlaps.Length)
                Array.Resize(ref overlaps, overlaps.Length * 2);
            for (int i = 0; i < count; i++)
            {
                Collider candidate = overlaps[i];
                if (IsOwner(candidate, owner)) continue;
                var health = candidate.GetComponentInParent<CombatHealth>();
                if (health == null || damaged.Contains(health)) continue;
                if (Blocked(origin, candidate, owner, layers)) continue;
                Apply(health, damage, attacker, ref hits, ref kills);
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
        CombatTeam attacker = CombatTeams.Of(owner);
        while ((count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlaps, rotation, layers,
            QueryTriggerInteraction.Ignore)) == overlaps.Length)
            Array.Resize(ref overlaps, overlaps.Length * 2);
        for (int i = 0; i < count; i++)
        {
            Collider candidate = overlaps[i];
            if (IsOwner(candidate, owner)) continue;
            var health = candidate.GetComponentInParent<CombatHealth>();
            if (health == null || damaged.Contains(health) || Blocked(wallOrigin, candidate, owner, layers)) continue;
            Apply(health, damage, attacker, ref hits, ref kills);
        }
        Array.Clear(overlaps, 0, count);
        damaged.Clear();
        return new DamageResult(hits, kills);
    }

    // True when something other than the owner or a damageable target is between origin and candidate.
    public bool LineBlocked(Vector3 origin, Collider candidate, Transform owner, LayerMask layers)
        => Blocked(origin, candidate, owner, layers);

    // Same-team targets are skipped: no friendly fire.
    bool Apply(CombatHealth health, float damage, CombatTeam attacker, ref int hits, ref int kills)
    {
        if (health == null || !CombatTeams.CanDamage(attacker, health.Team) || !damaged.Add(health)) return false;
        if (!health.ApplyDamage(damage)) return false;
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
}
