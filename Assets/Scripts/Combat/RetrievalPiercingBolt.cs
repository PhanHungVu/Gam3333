using System;
using System.Collections.Generic;
using UnityEngine;

// A separate retrieval attack. Normal MechProjectile keeps its first-impact/splash behavior.
public sealed class RetrievalPiercingBolt : MonoBehaviour, ICombatTrailSource
{
    static readonly IComparer<RaycastHit> ByDistance = Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance));
    RaycastHit[] contacts = new RaycastHit[16];
    Collider[] overlaps = new Collider[16];
    readonly HashSet<CombatHealth> history = new HashSet<CombatHealth>();
    Transform owner;
    CombatTeam team;
    LayerMask layers;
    float speed, damage, radius, remaining;
    bool live;
    public Part Source { get; private set; }
    public Vector3 Direction { get; private set; }
    public bool IsLive => live;
    public Transform TrailTransform => this != null ? transform : null;
    public float HitRadius => radius;
    public int HitCount { get; private set; }
    public int KillCount { get; private set; }
    public event Action<MechProjectileImpact> ImpactResolved;
    public event Action<Vector3> Finished;

    // Grounded drops can have offset/buried pivots. Sample beneath their physical pickup point,
    // then launch at combat height rather than adding height to the player's waist or a model pivot.
    public static Vector3 GroundOrigin(Vector3 pickupPoint, Transform owner, float height)
    {
        var found = Physics.RaycastAll(pickupPoint, Vector3.down, 4f, Part.MovementQueryMask & CombatRagdoll.QueryMask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        Vector3 ground = pickupPoint;
        foreach (var hit in found)
        {
            if (hit.normal.y < .5f || hit.distance >= nearest || owner != null && hit.transform.IsChildOf(owner)
                || hit.collider.GetComponentInParent<CombatHealth>() != null || hit.collider.GetComponentInParent<Part>() != null) continue;
            nearest = hit.distance; ground.y = hit.point.y;
        }
        return ground + Vector3.up * Mathf.Max(0, height);
    }
    public static Vector3 PlayerPoint(Transform owner, float height)
    {
        Vector3 point = owner.position;
        var controller = owner.GetComponent<CharacterController>();
        if (controller != null && controller.enabled) point.y = controller.bounds.min.y;
        return point + Vector3.up * Mathf.Max(0, height);
    }
    public static RetrievalPiercingBolt Fire(Part source, Transform owner, Vector3 origin, Vector3 target,
        float damage, LayerMask layers, float metresPerGolem, RetrievalEffectCatalogue.Entry settings,
        Action<RetrievalPiercingBolt> created = null)
    {
        if (source == null || source.Stats == null || owner == null || (target - origin).sqrMagnitude < .0001f) return null;
        var stats = source.Stats;
        float radius = Mathf.Max(.01f, settings.hitRadius);
        var model = MechProjectile.Create(stats.projectilePrefab, "Retrieval Piercing Bolt", origin, radius, null, stats.projectileColor);
        // Reuse the visual prefab, with exactly one movement/damage component driving this instance.
        model.enabled = false;
        var bolt = model.gameObject.AddComponent<RetrievalPiercingBolt>();
        Destroy(model);
        bolt.Source = source; bolt.owner = owner; bolt.team = CombatTeams.Of(owner);
        bolt.layers = layers & Part.MovementQueryMask & CombatRagdoll.QueryMask;
        bolt.Direction = (target - origin).normalized;
        bolt.speed = Mathf.Max(.01f, stats.GetStat(StatType.ProjectileSpeed) * Mathf.Max(.01f, settings.speedMultiplier));
        bolt.remaining = Mathf.Min(Vector3.Distance(origin, target), Mathf.Max(.01f, stats.GetStat(StatType.Range) * metresPerGolem * Mathf.Max(.01f, settings.rangeMultiplier)));
        bolt.damage = damage; bolt.radius = radius;
        bolt.transform.rotation = Quaternion.LookRotation(bolt.Direction);
        bolt.transform.localScale *= Mathf.Max(.01f, settings.boltVisualScale);
        bolt.live = true;
        created?.Invoke(bolt);
        CombatVisualEvents.ReportRetrievalBolt(bolt, origin, owner);
        bolt.CheckOverlap();
        return bolt;
    }
    bool OwnerValid => owner != null && owner.gameObject.activeInHierarchy && !MissionOutcomeController.BlocksGameplay(gameObject.scene);
    void Update() { if (live && !OwnerValid) Stop(); }
    void FixedUpdate() => Step(Time.fixedDeltaTime);
    void Step(float deltaTime)
    {
        if (!live || deltaTime <= 0) return;
        if (!OwnerValid) { Stop(); return; }
        Physics.SyncTransforms();
        if (CheckOverlap()) return;
        float distance = Mathf.Min(speed * deltaTime, remaining);
        Vector3 from = transform.position;
        int count;
        while ((count = Physics.SphereCastNonAlloc(from, radius, Direction, contacts, distance, layers, QueryTriggerInteraction.Ignore)) == contacts.Length)
            Array.Resize(ref contacts, contacts.Length * 2);
        Array.Sort(contacts, 0, count, ByDistance);
        int blocker = -1;
        for (int i = 0; i < count; i++)
            if (!Ignored(contacts[i].collider) && Blocking(contacts[i].collider)) { blocker = i; break; }
        float limit = blocker >= 0 ? contacts[blocker].distance : float.PositiveInfinity;
        for (int i = 0; i < count && live; i++)
        {
            var contact = contacts[i];
            if (contact.distance >= limit - .0001f) break;
            if (Ignored(contact.collider)) continue;
            transform.position = from + Direction * contact.distance;
            Hit(contact.collider, contact.point, contact.normal);
            if (!OwnerValid) Stop();
        }
        if (!live) return;
        if (blocker >= 0)
        {
            var contact = contacts[blocker]; transform.position = from + Direction * contact.distance;
            Impact(contact.point, contact.normal, 0, 0); Stop(); return;
        }
        transform.position = from + Direction * distance;
        remaining = Mathf.Max(0, remaining - distance);
        if (remaining <= .00001f) Stop();
    }
    bool Ignored(Collider collider)
    {
        if (collider == null || owner != null && collider.transform.IsChildOf(owner)) return true;
        var health = collider.GetComponentInParent<CombatHealth>();
        return health != null && (!health.isActiveAndEnabled || health.IsDead);
    }
    bool Blocking(Collider collider)
    {
        var health = collider.GetComponentInParent<CombatHealth>();
        return health == null || !CombatTeams.CanDamage(team, health.Team);
    }
    bool CheckOverlap()
    {
        int count;
        while ((count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlaps, layers, QueryTriggerInteraction.Ignore)) == overlaps.Length)
            Array.Resize(ref overlaps, overlaps.Length * 2);
        // A bolt starting inside solid terrain/friendly bodies must not damage through them.
        for (int i = 0; i < count; i++)
        {
            var candidate = overlaps[i];
            if (Ignored(candidate) || !Blocking(candidate)) continue;
            Impact(candidate.bounds.ClosestPoint(transform.position), -Direction, 0, 0); Stop(); return true;
        }
        for (int i = 0; i < count && live; i++)
        {
            var candidate = overlaps[i]; if (Ignored(candidate)) continue;
            Hit(candidate, candidate.bounds.ClosestPoint(transform.position), -Direction);
            if (!OwnerValid) Stop();
        }
        Array.Clear(overlaps, 0, count);
        return !live;
    }
    void Hit(Collider collider, Vector3 point, Vector3 normal)
    {
        var health = collider.GetComponentInParent<CombatHealth>();
        // Reserve before callbacks: overlapping limbs and re-entrant listeners cannot hit twice.
        if (health == null || !CombatTeams.CanDamage(team, health.Team) || !history.Add(health) || !health.ApplyDamage(damage, transform.position - Direction)) return;
        int kills = health.IsDead ? 1 : 0;
        HitCount++; KillCount += kills;
        CombatVisualEvents.ReportHit(owner, health, CombatHitKind.Projectile);
        Impact(point, normal, 1, kills);
    }
    void Impact(Vector3 point, Vector3 normal, int hits, int kills)
    {
        var result = new MechProjectileImpact(Source, PartSubtype.Projectile, transform.position, 0, hits, kills, Direction, point, normal, owner);
        ImpactResolved?.Invoke(result); CombatVisualEvents.ReportProjectile(result);
    }
    public void Stop()
    {
        if (!live) return;
        live = false; Finished?.Invoke(transform.position); Finished = null; ImpactResolved = null; history.Clear();
        gameObject.SetActive(false); Destroy(gameObject);
    }
    void OnDisable() { if (live) Stop(); }
}
