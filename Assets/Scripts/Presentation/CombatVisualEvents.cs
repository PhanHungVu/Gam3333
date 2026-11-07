using System;
using UnityEngine;

public enum CombatHitKind { Projectile, Melee, Blast }

// Notifications from accepted gameplay actions. Subscribers only present them.
public static class CombatVisualEvents
{
    public static event Action<MechProjectile, Vector3, Transform> ShotReleased;
    public static event Action<MechProjectileImpact> ProjectileImpact;
    public static event Action<Part, Transform, Vector3, Vector3> MeleeImpact;
    public static event Action<Transform, CombatHealth, CombatHitKind> HitConfirmed;
    public static event Action<Part, Transform, Vector3, float, DamageResult> RetrievalBlast;
    public static event Action<MechProjectile, Vector3, Transform> RetrievalShotReleased;
    public static event Action<RetrievalPiercingBolt, Vector3, Transform> RetrievalBoltReleased;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { ShotReleased = null; ProjectileImpact = null; MeleeImpact = null; HitConfirmed = null; RetrievalBlast = null; RetrievalShotReleased = null; RetrievalBoltReleased = null; }
    public static void ReportShot(MechProjectile shot, Vector3 muzzle, Transform owner) => ShotReleased?.Invoke(shot, muzzle, owner);
    public static void ReportProjectile(MechProjectileImpact impact) => ProjectileImpact?.Invoke(impact);
    public static void ReportMelee(Part source, Transform owner, Vector3 point, Vector3 normal) => MeleeImpact?.Invoke(source, owner, point, normal);
    public static void ReportHit(Transform owner, CombatHealth victim, CombatHitKind kind) => HitConfirmed?.Invoke(owner, victim, kind);
    public static void ReportRetrievalBlast(Part source, Transform owner, Vector3 position, float radius, DamageResult result)
        => RetrievalBlast?.Invoke(source, owner, position, radius, result);
    public static void ReportRetrievalShot(MechProjectile shot, Vector3 muzzle, Transform owner) => RetrievalShotReleased?.Invoke(shot, muzzle, owner);
    public static void ReportRetrievalBolt(RetrievalPiercingBolt bolt, Vector3 origin, Transform owner) => RetrievalBoltReleased?.Invoke(bolt, origin, owner);
}
