using System;
using UnityEngine;

// Fires one projectile from a Projectile or Explosive part. Damage, reload, speed, size, colour,
// model and splash all come from the part's stats; the shot uses one round and starts the reload.
public static class RangedAttack
{
    public static MechProjectile Fire(Part part, Transform owner, Vector3 muzzle, Vector3 aimPoint, Vector3 fallbackDirection,
        float effectiveRange, float maximumRange, LayerMask layers, Material fallbackMaterial,
        Action<MechProjectileImpact> onImpact, CannonSpiralBarrage spiral, CannonSpiralBarrage.Settings spiralSettings)
    {
        var stats = part.Stats;
        Vector3 direction = (aimPoint - muzzle).normalized;
        if (direction.sqrMagnitude < 0.001f) direction = fallbackDirection;
        part.Consume(Mathf.Max(0.01f, stats.GetStat(StatType.CooldownReload)), true);
        float size = Mathf.Max(0.01f, stats.GetStat(StatType.ProjectileSize));
        var projectile = MechProjectile.Create(stats.projectilePrefab, stats.partName + " Projectile",
            new Vector3(owner.position.x, muzzle.y, owner.position.z), size, fallbackMaterial, stats.projectileColor);
        projectile.Source = part;
        projectile.Subtype = stats.subtype;
        // Subscribe before Launch: a wall intersecting the muzzle can impact immediately.
        projectile.ImpactResolved += onImpact;
        float splash = stats.subtype == PartSubtype.Explosive ? Mathf.Max(0f, stats.GetStat(StatType.SplashRadius)) : 0f;
        projectile.ConfigureRangeAndExplosion(effectiveRange, splash);
        if (spiral != null) projectile.ConfigureSpiralBarrage(spiral, spiralSettings);
        projectile.Launch(owner, direction, stats.GetStat(StatType.ProjectileSpeed), Mathf.Max(0f, stats.GetStat(StatType.Damage)),
            size, maximumRange, layers, muzzle);
        return projectile;
    }
}
