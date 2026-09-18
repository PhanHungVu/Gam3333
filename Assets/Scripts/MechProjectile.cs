using System;
using System.Collections.Generic;
using UnityEngine;

// Straight projectile: direction is captured at launch and never tracks a target.
public class MechProjectile : MonoBehaviour
{
    Transform owner;
    Vector3 direction;
    float speed, damage, radius, remainingDistance;
    float effectiveRange = float.PositiveInfinity, maximumRange, travelled, explosionRadius;
    Material impactMaterial;
    Color impactColor;
    LayerMask collisionLayers;
    bool live;
    RaycastHit[] hits = new RaycastHit[16];
    Collider[] overlaps = new Collider[16];
    public Vector3 Direction => direction;
    public bool IsLive => live;

    public void ConfigureRangeAndExplosion(float effective, float blastRadius, Material material, Color color)
    {
        effectiveRange = Mathf.Max(0f, effective);
        explosionRadius = Mathf.Max(0f, blastRadius);
        impactMaterial = material;
        impactColor = color;
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
        if (health != null) { health.ApplyDamage(impactDamage); damaged.Add(health); }
        if (explosionRadius <= 0f) return;
        foreach (var candidate in Physics.OverlapSphere(impactPoint, explosionRadius, collisionLayers, QueryTriggerInteraction.Ignore))
        {
            if (IsOwner(candidate)) continue;
            var nearby = candidate.GetComponentInParent<CombatHealth>();
            if (nearby == null || damaged.Contains(nearby) || BlastBlocked(impactPoint, candidate)) continue;
            damaged.Add(nearby);
            nearby.ApplyDamage(impactDamage);
        }
        ShowExplosion(impactPoint);
    }

    bool BlastBlocked(Vector3 origin, Collider candidate)
    {
        Vector3 offset = candidate.ClosestPoint(origin) - origin;
        if (offset.sqrMagnitude < 0.0001f) return false;
        foreach (var hit in Physics.RaycastAll(origin, offset.normalized, offset.magnitude, collisionLayers, QueryTriggerInteraction.Ignore))
            if (!IsOwner(hit.collider) && hit.collider.GetComponentInParent<CombatHealth>() == null) return true;
        return false;
    }

    void ShowExplosion(Vector3 point)
    {
        if (impactMaterial == null) return;
        var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        flash.name = "Cannon Blast";
        flash.GetComponent<Collider>().enabled = false;
        Destroy(flash.GetComponent<Collider>());
        flash.transform.position = point;
        flash.transform.localScale = Vector3.one * explosionRadius * 2f;
        var renderer = flash.GetComponent<Renderer>();
        renderer.sharedMaterial = impactMaterial;
        var tint = new MaterialPropertyBlock();
        impactColor.a = 0.18f;
        tint.SetColor("_Color", impactColor);
        renderer.SetPropertyBlock(tint);
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Destroy(flash, 0.18f);
    }

    void Finish()
    {
        live = false;
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
