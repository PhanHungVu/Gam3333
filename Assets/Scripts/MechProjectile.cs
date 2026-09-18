using System;
using System.Collections.Generic;
using UnityEngine;

// One result per resolved impact, emitted only after all gameplay damage is applied.
public readonly struct MechProjectileImpact
{
    public readonly MechWeaponSlot Weapon;
    public readonly Vector3 Position;
    public readonly float Radius;
    public readonly int Hits, Kills;
    public MechProjectileImpact(MechWeaponSlot weapon, Vector3 position, float radius, int hits, int kills)
    { Weapon = weapon; Position = position; Radius = radius; Hits = hits; Kills = kills; }
}

// Straight projectile: direction is captured at launch and never tracks a target.
public class MechProjectile : MonoBehaviour
{
    Transform owner;
    Vector3 direction;
    float speed, damage, radius, remainingDistance;
    float effectiveRange = float.PositiveInfinity, maximumRange, travelled, explosionRadius;
    LayerMask collisionLayers;
    bool live;
    RaycastHit[] hits = new RaycastHit[16];
    Collider[] overlaps = new Collider[16];
    public Vector3 Direction => direction;
    public bool IsLive => live;
    public MechWeaponSlot Weapon { get; set; }
    public event Action<MechProjectileImpact> ImpactResolved;

    public void ConfigureRangeAndExplosion(float effective, float blastRadius, Material material, Color color)
    {
        effectiveRange = Mathf.Max(0f, effective);
        explosionRadius = Mathf.Max(0f, blastRadius);
        // Retain the existing API; visuals now belong to the event-driven feedback layer.
    }

    public static float RangeMultiplier(float distance, float effective, float maximum)
        => distance <= effective ? 1f : maximum <= effective ? 0f
        : Mathf.Clamp01((maximum - distance) / (maximum - effective));

    public void Launch(Transform shooter, Vector3 shotDirection, float shotSpeed, float shotDamage,
        float shotRadius, float range, LayerMask layers, Vector3 muzzle)
    {
        owner = shooter;
        direction = shotDirection.normalized;
        speed = Mathf.Max(0.01f, shotSpeed);
        damage = shotDamage;
        radius = Mathf.Max(0.01f, shotRadius);
        remainingDistance = Mathf.Max(0f, range);
        maximumRange = remainingDistance;
        effectiveRange = Mathf.Min(effectiveRange, maximumRange);
        travelled = 0f;
        collisionLayers = layers;
        live = true;
        // Validate the path from the player's center to the muzzle as well. A muzzle
        // extending through a wall must not spawn a shot on the other side.
        if (!CheckOverlap() && !Sweep(muzzle - transform.position, false)) transform.position = muzzle;
    }

    void FixedUpdate() => Step(Time.fixedDeltaTime);

    void Step(float deltaTime)
    {
        if (!live || deltaTime <= 0f) return;
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
        Impact(closest);
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
        Impact(hits[nearest].collider);
        return true;
    }

    void Impact(Collider collider)
    {
        var health = collider.GetComponentInParent<CombatHealth>();
        Vector3 impactPoint = transform.position;
        float impactDamage = damage * RangeMultiplier(travelled, effectiveRange, maximumRange);
        // Mark spent first: callbacks and compound colliders cannot cause repeat damage.
        Finish();
        var damaged = new HashSet<CombatHealth>();
        int hitCount = 0, killCount = 0;
        if (health != null)
        {
            ApplyImpactDamage(health, impactDamage, ref hitCount, ref killCount);
            damaged.Add(health);
        }
        if (explosionRadius > 0f)
        {
            foreach (var candidate in Physics.OverlapSphere(impactPoint, explosionRadius, collisionLayers, QueryTriggerInteraction.Ignore))
            {
                if (IsOwner(candidate)) continue;
                var nearby = candidate.GetComponentInParent<CombatHealth>();
                if (nearby == null || damaged.Contains(nearby) || BlastBlocked(impactPoint, candidate)) continue;
                damaged.Add(nearby);
                ApplyImpactDamage(nearby, impactDamage, ref hitCount, ref killCount);
            }
        }
        ImpactResolved?.Invoke(new MechProjectileImpact(Weapon, impactPoint, explosionRadius, hitCount, killCount));
        ImpactResolved = null;
    }

    static void ApplyImpactDamage(CombatHealth health, float amount, ref int hits, ref int kills)
    {
        if (!health.ApplyDamage(amount)) return;
        hits++;
        if (health.IsDead) kills++;
    }

    bool BlastBlocked(Vector3 origin, Collider candidate)
    {
        Vector3 offset = candidate.ClosestPoint(origin) - origin;
        if (offset.sqrMagnitude < 0.0001f) return false;
        foreach (var hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude, collisionLayers, QueryTriggerInteraction.Ignore))
            if (!IsOwner(hit.collider) && hit.collider.GetComponentInParent<CombatHealth>() == null) return true;
        return false;
    }

    void Finish()
    {
        live = false;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
