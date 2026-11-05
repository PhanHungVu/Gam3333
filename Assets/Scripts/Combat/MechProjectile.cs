using System;
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
    public readonly float Radius;
    public readonly int Hits, Kills;
    public MechProjectileImpact(Part source, PartSubtype subtype, Vector3 position, float radius, int hits, int kills,
        Vector3 direction = default, Vector3? contactPoint = null, Vector3 normal = default, Transform owner = null)
    {
        Source = source; Subtype = subtype; Position = position; Radius = radius; Hits = hits; Kills = kills;
        Direction = direction;
        ContactPoint = contactPoint ?? position;
        Normal = normal.sqrMagnitude > .0001f ? normal.normalized : direction.sqrMagnitude > .0001f ? -direction.normalized : Vector3.up;
        Owner = owner;
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
        float distance = Mathf.Min(speed * deltaTime, remainingDistance);
        if (CheckOverlap() || Sweep(direction * distance)) return;
        transform.position += direction * distance;
        travelled += distance;
        remainingDistance -= distance;
        if (remainingDistance <= 0f) Finish();
    }

    bool IsOwner(Collider candidate) => owner != null && candidate.transform.IsChildOf(owner);

    bool CheckOverlap()
    {
        int count;
        while ((count = Physics.OverlapSphereNonAlloc(transform.position, radius, overlaps,
            collisionLayers, QueryTriggerInteraction.Ignore)) == overlaps.Length)
            Array.Resize(ref overlaps, overlaps.Length * 2);
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
        // Mark spent first: callbacks and compound colliders cannot cause repeat damage.
        Finish();
        // Teams decide who takes damage (CombatBlast skips the shooter's own team).
        var result = blast.Resolve(impactPoint, explosionRadius, impactDamage, splashDamage, owner, collisionLayers, collider);
        var impact = new MechProjectileImpact(Source, Subtype, impactPoint, explosionRadius,
            result.Hits, result.Kills, direction, contactPoint, normal, owner);
        ImpactResolved?.Invoke(impact);
        CombatVisualEvents.ReportProjectile(impact);
        ImpactResolved = null;
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
