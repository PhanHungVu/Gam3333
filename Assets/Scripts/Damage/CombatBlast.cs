using System;
using System.Collections.Generic;
using UnityEngine;

// How many targets one damage event hit and killed.
public readonly struct DamageResult
{
    public readonly int Hits, Kills;
    public DamageResult(int hits, int kills) { Hits = hits; Kills = kills; }
}

// Shared damage rules: direct/splash hits, melee boxes and filled circular areas.
// Never hits the owner, walls block it, and each target is hit once even with several colliders.
// Presentation never applies damage.
public sealed class CombatBlast
{
    Collider[] overlaps = new Collider[16];
    RaycastHit[] blockers = new RaycastHit[16];
    readonly HashSet<CombatHealth> damaged = new HashSet<CombatHealth>();

    // A hit at origin. The target struck directly takes damage, plus splashDamage when there is a radius
    // (rewarding direct hits); everyone else within radius takes splashDamage. Walls block the splash.
    public DamageResult Resolve(Vector3 origin, float radius, float damage, float splashDamage,
        Transform owner, LayerMask layers, Collider direct = null, Vector3? directSourcePosition = null)
    {
        layers &= CombatRagdoll.QueryMask;
        damaged.Clear();
        int hits = 0, kills = 0;
        CombatTeam attacker = CombatTeams.Of(owner);
        if (direct != null && !IsOwner(direct, owner))
            Apply(direct.GetComponentInParent<CombatHealth>(), damage + (radius > 0f ? splashDamage : 0f), attacker, ref hits, ref kills,
                owner, radius>0 ? CombatHitKind.Blast : CombatHitKind.Projectile,
                radius > 0 ? origin : directSourcePosition ?? (owner != null ? owner.position : (Vector3?)null));
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
                Apply(health, splashDamage, attacker, ref hits, ref kills, owner, CombatHitKind.Blast, origin);
            }
            Array.Clear(overlaps, 0, count);
        }
        damaged.Clear();
        return new DamageResult(hits, kills);
    }

    // Damages every target inside an oriented box (melee). Walls are checked along a line
    // from wallOrigin (the attacker) to each target.
    public DamageResult ResolveBox(Vector3 center, Vector3 halfExtents, Quaternion rotation, float damage,
        Transform owner, LayerMask layers, Vector3 wallOrigin, ISet<CombatHealth> hitHistory = null, Part source = null,
        Action<CombatHealth> acceptedHit = null)
    {
        layers &= CombatRagdoll.QueryMask;
        damaged.Clear();
        int hits = 0, kills = 0, count;
        CombatTeam attacker = CombatTeams.Of(owner);
        var playerMelee = source != null && source.Stats != null && source.Stats.subtype == PartSubtype.Melee && owner != null
            ? owner.GetComponent<MechCombat>() : null;
        while ((count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlaps, rotation, layers,
            QueryTriggerInteraction.Ignore)) == overlaps.Length)
            Array.Resize(ref overlaps, overlaps.Length * 2);
        for (int i = 0; i < count; i++)
        {
            Collider candidate = overlaps[i];
            if (IsOwner(candidate, owner)) continue;
            var health = candidate.GetComponentInParent<CombatHealth>();
            if (health == null || damaged.Contains(health) || hitHistory != null && hitHistory.Contains(health)
                || Blocked(wallOrigin, candidate, owner, layers)) continue;
            var point = ClosestPoint(candidate, wallOrigin);
            var normal = wallOrigin - point;
            Vector3 push = default, launch = default;
            bool hasPush = playerMelee != null && playerMelee.TryMeleePush(health, rotation * Vector3.forward, out push, out launch);
            var death = hasPush ? health.GetComponent<CombatDeathRagdoll>() : null;
            // Death callbacks run inside ApplyDamage. Scope the upright launch to this hit only,
            // including rejected damage, rather than leaving a flag for an unrelated later death.
            if (death != null) death.BeginMeleeLaunch(launch);
            bool applied;
            try { applied = Apply(health, damage, attacker, ref hits, ref kills, owner, source!=null ? CombatHitKind.Melee : CombatHitKind.Blast, wallOrigin); }
            finally { if (death != null) death.EndMeleeLaunch(); }
            if (applied)
            {
                if (hasPush) playerMelee.PushSurvivingEnemy(health, push);
                hitHistory?.Add(health);
                acceptedHit?.Invoke(health);
                if (source != null) CombatVisualEvents.ReportMelee(source, owner, point, normal.sqrMagnitude > .0001f ? normal.normalized : Vector3.up);
            }
        }
        Array.Clear(overlaps, 0, count);
        damaged.Clear();
        return new DamageResult(hits, kills);
    }

    // A filled horizontal circle with a bounded height, for ground-area attacks.
    // The box is only a broad phase; its corners must not extend the circular warning.
    public DamageResult ResolveCylinder(Vector3 center, float radius, float halfHeight, float damage,
        Transform owner, LayerMask layers, ISet<CombatHealth> hitHistory = null, Action<CombatHealth> acceptedHit = null)
    {
        layers &= CombatRagdoll.QueryMask;
        damaged.Clear();
        int hits = 0, kills = 0, count;
        CombatTeam attacker = CombatTeams.Of(owner);
        while ((count = Physics.OverlapBoxNonAlloc(center, new Vector3(radius, halfHeight, radius), overlaps,
            Quaternion.identity, layers, QueryTriggerInteraction.Ignore)) == overlaps.Length)
            Array.Resize(ref overlaps, overlaps.Length * 2);
        for (int i = 0; i < count; i++)
        {
            Collider candidate = overlaps[i];
            if (IsOwner(candidate, owner)) continue;
            Vector3 offset = ClosestPoint(candidate, center) - center;
            if (offset.x * offset.x + offset.z * offset.z > radius * radius) continue;
            var health = candidate.GetComponentInParent<CombatHealth>();
            if (health == null || damaged.Contains(health) || hitHistory != null && hitHistory.Contains(health)
                || Blocked(center, candidate, owner, layers)) continue;
            if (Apply(health, damage, attacker, ref hits, ref kills, owner, CombatHitKind.Blast, center)) { hitHistory?.Add(health); acceptedHit?.Invoke(health); }
        }
        Array.Clear(overlaps, 0, count);
        damaged.Clear();
        return new DamageResult(hits, kills);
    }

    // True when something other than the owner or a damageable target is between origin and candidate.
    public bool LineBlocked(Vector3 origin, Collider candidate, Transform owner, LayerMask layers)
        => Blocked(origin, candidate, owner, layers);

    // Same-team targets are skipped: no friendly fire.
    bool Apply(CombatHealth health, float damage, CombatTeam attacker, ref int hits, ref int kills, Transform owner, CombatHitKind kind, Vector3? sourcePosition)
    {
        if (health == null || !CombatTeams.CanDamage(attacker, health.Team) || !damaged.Add(health)) return false;
        if (!health.ApplyDamage(damage, sourcePosition)) return false;
        hits++;
        if (health.IsDead) kills++;
        CombatVisualEvents.ReportHit(owner,health,kind);
        return true;
    }

    bool Blocked(Vector3 origin, Collider candidate, Transform owner, LayerMask layers)
    {
        layers &= CombatRagdoll.QueryMask;
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
