using System;
using System.Collections.Generic;
using UnityEngine;

// One result per resolved impact, emitted only after all gameplay damage is applied.
public readonly struct MechProjectileImpact
{
    // The part that fired (null for enemy shots) and how it attacks.
    public readonly Part Source;
    public readonly PartSubtype Subtype;
    public readonly Vector3 Position;
    public readonly Vector3 Direction;
    public readonly Vector3 ContactPoint, Normal;
    public readonly Transform Owner;
    public readonly Collider HitCollider;
    public readonly float Radius;
    public readonly int Hits, Kills;
    public MechProjectileImpact(Part source, PartSubtype subtype, Vector3 position, float radius, int hits, int kills,
        Vector3 direction = default, Vector3? contactPoint = null, Vector3 normal = default, Transform owner = null, Collider hitCollider = null)
    {
        Source = source; Subtype = subtype; Position = position; Radius = radius; Hits = hits; Kills = kills;
        Direction = direction;
        ContactPoint = contactPoint ?? position;
        Normal = normal.sqrMagnitude > .0001f ? normal.normalized : direction.sqrMagnitude > .0001f ? -direction.normalized : Vector3.up;
        Owner = owner;
        HitCollider = hitCollider;
    }
}

// Straight projectile: direction is captured at launch and never tracks a target.
public class MechProjectile : MonoBehaviour
{
    [SerializeField] Transform visual;
    [SerializeField] Vector3 angularVelocity;
    Transform owner;
    Vector3 direction;
    float speed, damage, radius, remainingDistance;
    float travelled, explosionRadius, splashDamage;
    LayerMask collisionLayers;
    bool live;
    bool piercing;
    int maximumPiercedTargets;
    HashSet<CombatHealth> piercedTargets;
    bool arcing;
    Vector3 arcTarget, arcVelocity;
    float arcGravity, arcMinimumFlight, arcRemaining;
    RaycastHit[] hits = new RaycastHit[16];
    Collider[] overlaps = new Collider[16];
    readonly CombatBlast blast = new CombatBlast();
    public Vector3 Direction => direction;
    public bool IsLive => live;
    public CombatTeam OwnerTeam { get; private set; }
    // The part that fired this shot (null for enemy shots) and how it attacks.
    public Part Source { get; set; }
    public PartSubtype Subtype { get; set; }
    // Optional enemy shot filter. Other bodies still block the shot, without friendly damage.
    public event Action<MechProjectileImpact> ImpactResolved;
    public event Action<Vector3> Finished;

    public static MechProjectile Create(GameObject prefab, string shotName, Vector3 position,
        float shotRadius, Material fallbackMaterial, Color fallbackColor)
    {
        if (prefab != null)
        {
            var model = Instantiate(prefab, position, Quaternion.identity);
            model.name = shotName;
            return model.GetComponent<MechProjectile>() ?? model.AddComponent<MechProjectile>();
        }
        // Legacy prototypes and the cannon retain their existing visuals.
        var shot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shot.name = shotName; shot.transform.position = position;
        var collider = shot.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
        shot.transform.localScale = Vector3.one * (shotRadius * 2f);
        var renderer = shot.GetComponent<Renderer>();
        if (fallbackMaterial != null) renderer.sharedMaterial = fallbackMaterial;
        var tint = new MaterialPropertyBlock(); tint.SetColor("_Color", fallbackColor); renderer.SetPropertyBlock(tint);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return shot.AddComponent<MechProjectile>();
    }

    // A Splash weapon's radius and the damage everything inside it takes (the direct hit takes both).
    public void ConfigureSplash(float blastRadius, float splash)
    {
        explosionRadius = Mathf.Max(0f, blastRadius);
        splashDamage = Mathf.Max(0f, splash);
    }

    public void ConfigurePiercing(bool enabled, int maximumTargets = 0)
    {
        piercing = enabled;
        maximumPiercedTargets = Mathf.Max(0, maximumTargets);
        if (!enabled) return;
        if (piercedTargets == null) piercedTargets = new HashSet<CombatHealth>();
        piercedTargets.Clear();
        // Piercing is direct-only, including if a stale splash field exists on an item.
        explosionRadius = splashDamage = 0;
    }

    // Optional artillery mode on the existing projectile, preserving the same
    // swept collision, splash, teams, feedback and finish lifecycle.
    public void ConfigureArc(Vector3 target, float gravity = 25, float minimumFlight = .55f)
    { arcing = true; arcTarget = target; arcGravity = Mathf.Max(.1f, gravity); arcMinimumFlight = Mathf.Max(.05f, minimumFlight); }

    public void Launch(Transform shooter, Vector3 shotDirection, float shotSpeed, float shotDamage,
        float shotRadius, float range, LayerMask layers, Vector3 muzzle)
    {
        owner = shooter;
        OwnerTeam = CombatTeams.Of(shooter);
        direction = shotDirection.normalized;
        if (direction.sqrMagnitude > 0f) transform.rotation = Quaternion.LookRotation(direction);
        speed = Mathf.Max(0.01f, shotSpeed);
        damage = shotDamage;
        radius = Mathf.Max(0.01f, shotRadius);
        remainingDistance = Mathf.Max(0f, range);
        travelled = 0f;
        collisionLayers = layers & CombatRagdoll.QueryMask;
        if (arcing)
        {
            Vector3 delta = arcTarget - muzzle, planar = Vector3.ProjectOnPlane(delta, Vector3.up);
            // Mortar Range is horizontal acquisition/reach, not the longer arc's path length.
            if (planar.magnitude > range) { planar = planar.normalized * range; arcTarget = muzzle + planar + Vector3.up * delta.y; }
            arcRemaining = Mathf.Max(arcMinimumFlight, planar.magnitude / speed);
            arcVelocity = planar / arcRemaining + Vector3.up * ((arcTarget.y - muzzle.y) / arcRemaining + .5f * arcGravity * arcRemaining);
            direction = arcVelocity.normalized; transform.rotation = Quaternion.LookRotation(direction);
        }
        live = true;
        // Validate the path from the player's center to the muzzle as well. A muzzle
        // extending through a wall must not spawn a shot on the other side.
        if (!CheckOverlap() && !Sweep(muzzle - transform.position, false)) transform.position = muzzle;
    }

    void FixedUpdate() => Step(Time.fixedDeltaTime);

    void Step(float deltaTime)
    {
        if (!live || deltaTime <= 0f) return;
        if (visual != null) visual.Rotate(angularVelocity * deltaTime, Space.Self);
        if (arcing) { StepArc(deltaTime); return; }
        float distance = Mathf.Min(speed * deltaTime, remainingDistance);
        if (CheckOverlap() || Sweep(direction * distance)) return;
        transform.position += direction * distance;
        travelled += distance;
        remainingDistance -= distance;
        if (remainingDistance <= 0f) Finish();
    }

    void StepArc(float deltaTime)
    {
        while (live && deltaTime > 0 && arcRemaining > 0)
        {
            float dt = Mathf.Min(deltaTime, 1f / 60, arcRemaining);
            Vector3 acceleration = Vector3.down * arcGravity;
            Vector3 displacement = arcVelocity * dt + .5f * acceleration * dt * dt;
            if (CheckOverlap() || Sweep(displacement)) return;
            transform.position += displacement; arcVelocity += acceleration * dt;
            direction = arcVelocity.normalized; transform.rotation = Quaternion.LookRotation(direction);
            deltaTime -= dt; arcRemaining = Mathf.Max(0, arcRemaining - dt);
        }
        // Arrival is an area detonation if the shell did not strike a body or
        // world collider first; normal straight projectiles keep range-expiry behavior.
        if (live && arcRemaining <= 0) Impact(null, transform.position, Vector3.up);
    }

    bool IsOwner(Collider candidate) => owner != null && candidate.transform.IsChildOf(owner);

    bool IgnorePiercingContact(Collider candidate)
        => candidate == null || IsOwner(candidate) || candidate.GetComponentInParent<CombatHealth>(true) is CombatHealth health && piercedTargets.Contains(health);

    bool CanPierce(Collider candidate)
    {
        // A preceding contact may kill/deactivate a compound root during this
        // same sweep. Resolve inactive parents too, so its other colliders still
        // map to the already-hit root instead of becoming scenery blockers.
        var health = candidate.GetComponentInParent<CombatHealth>(true);
        return health != null && health.isActiveAndEnabled && !health.IsDead
            && health.Team != CombatTeam.Neutral && CombatTeams.CanDamage(OwnerTeam, health.Team);
    }

    bool CheckOverlap()
    {
        int count;
        while ((count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlaps,
            collisionLayers, QueryTriggerInteraction.Ignore)) == overlaps.Length)
            Array.Resize(ref overlaps, overlaps.Length * 2);
        if (piercing)
        {
            // A muzzle inside scenery must not shoot through it. Give simultaneous
            // blocking overlaps priority before resolving damageable targets.
            for (int pass = 0; pass < 2 && live; pass++)
                for (int i = 0; i < count && live; i++)
                {
                    var candidate = overlaps[i];
                    if (IgnorePiercingContact(candidate) || CanPierce(candidate) != (pass == 1)) continue;
                    Vector3 piercingPoint = candidate is MeshCollider piercingMesh && !piercingMesh.convex
                        ? candidate.bounds.ClosestPoint(transform.position) : candidate.ClosestPoint(transform.position);
                    Vector3 piercingNormal = transform.position - piercingPoint;
                    Impact(candidate, piercingPoint, piercingNormal.sqrMagnitude > .0001f ? piercingNormal.normalized : -direction);
                }
            Array.Clear(overlaps, 0, count);
            return !live;
        }
        Collider closest = null;
        float closestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (IsOwner(overlaps[i])) continue;
            float distance = (overlaps[i].ClosestPoint(transform.position) - transform.position).sqrMagnitude;
            if (distance < closestDistance) { closestDistance = distance; closest = overlaps[i]; }
        }
        Array.Clear(overlaps, 0, count);
        if (closest == null) return false;
        Vector3 point = closest is MeshCollider mesh && !mesh.convex ? closest.bounds.ClosestPoint(transform.position) : closest.ClosestPoint(transform.position);
        var normal = transform.position - point;
        Impact(closest, point, normal.sqrMagnitude > .0001f ? normal.normalized : -direction);
        return true;
    }

    bool Sweep(Vector3 displacement, bool countTravel = true)
    {
        float distance = displacement.magnitude;
        if (distance <= 0f) return false;
        int count;
        while ((count = Physics.SphereCastNonAlloc(transform.position, radius, displacement / distance,
            hits, distance, collisionLayers, QueryTriggerInteraction.Ignore)) == hits.Length)
            Array.Resize(ref hits, hits.Length * 2);
        if (piercing)
        {
            // NonAlloc casts are unordered. Process near-to-far so a wall stops
            // the bolt before any enemy behind it, even within one fast step.
            for (int i = 1; i < count; i++)
            {
                var next = hits[i]; int j = i - 1;
                while (j >= 0 && hits[j].distance > next.distance) { hits[j + 1] = hits[j]; j--; }
                hits[j + 1] = next;
            }
            Vector3 start = transform.position;
            for (int i = 0; i < count; i++)
            {
                if (IgnorePiercingContact(hits[i].collider)) continue;
                transform.position = start + displacement.normalized * hits[i].distance;
                Impact(hits[i].collider, hits[i].point, hits[i].normal);
                if (!live)
                {
                    if (countTravel) travelled += hits[i].distance;
                    Array.Clear(hits, 0, count);
                    return true;
                }
            }
            transform.position = start; // caller advances/counts the full clear step once
            Array.Clear(hits, 0, count);
            return false;
        }
        int nearest = -1;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            if (IsOwner(hits[i].collider) || hits[i].distance >= nearestDistance) continue;
            nearest = i;
            nearestDistance = hits[i].distance;
        }
        if (nearest < 0) return false;
        transform.position += displacement.normalized * nearestDistance;
        if (countTravel) travelled += nearestDistance;
        Impact(hits[nearest].collider, hits[nearest].point, hits[nearest].normal);
        return true;
    }

    void Impact(Collider collider, Vector3 contactPoint, Vector3 normal)
    {
        if (!live) return;
        Vector3 impactPoint = transform.position;
        float impactDamage = damage;
        bool passesThrough = piercing && CanPierce(collider);
        if (passesThrough)
        {
            // Remember the health root before callbacks; compound colliders and
            // later frames can never apply this projectile's damage twice.
            if (!piercedTargets.Add(collider.GetComponentInParent<CombatHealth>(true))) return;
        }
        if (!passesThrough || maximumPiercedTargets > 0 && piercedTargets.Count >= maximumPiercedTargets) Finish();
        // Teams decide who takes damage (CombatBlast skips the shooter's own team).
        // The incoming trajectory remains meaningful even if the shooter has moved or died.
        var result = blast.Resolve(impactPoint, explosionRadius, impactDamage, splashDamage, owner, collisionLayers, collider,
            impactPoint - direction);
        var impact = new MechProjectileImpact(Source, Subtype, impactPoint, explosionRadius,
            result.Hits, result.Kills, direction, contactPoint, normal, owner, collider);
        ImpactResolved?.Invoke(impact);
        CombatVisualEvents.ReportProjectile(impact);
        if (!live) ImpactResolved = null;
    }

    void Finish()
    {
        live = false;
        Finished?.Invoke(transform.position); Finished = null;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
    void OnDisable()
    {
        if (!live) return;
        live = false; Finished?.Invoke(transform.position); Finished = null;
        ImpactResolved = null;
    }
}
