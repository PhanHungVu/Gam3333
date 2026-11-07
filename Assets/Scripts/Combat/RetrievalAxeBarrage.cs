using System;
using UnityEngine;

// Temporary combat projectiles only; never clones equipment or consumes a weapon's ammunition/cooldown.
public static class RetrievalAxeBarrage
{
    public static int Fire(Part source, Transform owner, float damage, LayerMask layers, float metresPerGolem,
        RetrievalEffectCatalogue.Entry settings, Action<MechProjectile, Vector3> created = null)
    {
        if (source == null || source.Stats == null || owner == null) return 0;
        var stats = source.Stats;
        int count = Mathf.Clamp(settings.projectileCount, 1, 32), fired = 0;
        // Retrieval-only forgiveness for the sparse body/head hitboxes; normal weapon shots retain their stat radius.
        float size = Mathf.Max(.01f, Mathf.Max(stats.GetStat(StatType.ProjectileSize), settings.hitRadius));
        float speed = Mathf.Max(.01f, stats.GetStat(StatType.ProjectileSpeed) * Mathf.Max(.01f, settings.speedMultiplier));
        float range = Mathf.Max(.01f, stats.GetStat(StatType.Range) * metresPerGolem * Mathf.Max(.01f, settings.rangeMultiplier));
        // The Specialist pivot is at the waist, not the feet. Root + height fires over small enemies.
        var controller = owner.GetComponent<CharacterController>();
        Vector3 feet = owner.position;
        if (controller != null && controller.enabled) feet.y = controller.bounds.min.y;
        Vector3 center = feet + Vector3.up * Mathf.Max(0, settings.spawnHeight);
        Vector3 forward = Vector3.ProjectOnPlane(owner.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < .001f) forward = Vector3.forward;
        for (int i = 0; i < count && owner != null; i++)
        {
            Vector3 direction = Quaternion.AngleAxis(360f * i / count, Vector3.up) * forward;
            Vector3 muzzle = center + direction * Mathf.Max(0, settings.spawnRadius);
            var shot = MechProjectile.Create(stats.projectilePrefab, "Retrieval Axe " + (i + 1), center, size, null, stats.projectileColor);
            shot.Source = source; shot.Subtype = PartSubtype.Projectile;
            shot.ConfigureSplash(0, 0);
            // Subscribe before launch; the standard center-to-muzzle sweep may hit a nearby wall immediately.
            created?.Invoke(shot, direction);
            shot.Launch(owner, direction, speed, damage, size, range, layers, muzzle);
            CombatVisualEvents.ReportRetrievalShot(shot, muzzle, owner);
            fired++;
        }
        return fired;
    }
}
