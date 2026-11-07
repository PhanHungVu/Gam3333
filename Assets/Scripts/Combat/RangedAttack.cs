using System;
using UnityEngine;

// Fires one projectile from a Projectile or Splash part. Damage, fire rate, magazine, reload, speed,
// size, colour, model and splash all come from the part's stats; the shot uses one round and may start a reload.
// Enemy damage/flight multipliers and optional player cadence stay separate from the shared stats.
public static class RangedAttack
{
    public static MechProjectile Fire(Part part, Transform owner, Vector3 muzzle, Vector3 aimPoint, Vector3 fallbackDirection,
        float range, LayerMask layers, Material fallbackMaterial,
        Action<MechProjectileImpact> onImpact, float damageMultiplier = 1f, float speedMultiplier = 1f, float cadenceMultiplier = 1f)
    {
        var stats = part.Stats;
        Vector3 direction = (aimPoint - muzzle).normalized;
        if (direction.sqrMagnitude < 0.001f) direction = fallbackDirection;
        float cadence = Mathf.Max(.01f, cadenceMultiplier);
        part.Consume(stats.ShotInterval / cadence, true, stats.GetStat(StatType.Reload) / cadence, stats.MagazineSize);
        float size = Mathf.Max(0.01f, stats.GetStat(StatType.ProjectileSize));
        var projectile = MechProjectile.Create(stats.projectilePrefab, stats.partName + " Projectile",
            new Vector3(owner.position.x, muzzle.y, owner.position.z), size, fallbackMaterial, stats.projectileColor);
        projectile.Source = part;
        projectile.Subtype = stats.subtype;
        // Subscribe before Launch: a wall intersecting the muzzle can impact immediately.
        projectile.ImpactResolved += onImpact;
        bool splashes = stats.subtype == PartSubtype.Splash;
        float splash = splashes ? Mathf.Max(0f, stats.GetStat(StatType.SplashRadius)) : 0f;
        float splashDamage = splashes ? Mathf.Max(0f, stats.GetStat(StatType.SplashDamage) * Mathf.Max(0f, damageMultiplier)) : 0f;
        projectile.ConfigureSplash(splash, splashDamage);
        projectile.Launch(owner, direction, stats.GetStat(StatType.ProjectileSpeed) * Mathf.Max(0.01f, speedMultiplier),
            Mathf.Max(0f, stats.GetStat(StatType.Damage) * Mathf.Max(0f, damageMultiplier)),
            size, range, layers, muzzle);
        CombatVisualEvents.ReportShot(projectile, muzzle, owner);
        return projectile;
    }
}
