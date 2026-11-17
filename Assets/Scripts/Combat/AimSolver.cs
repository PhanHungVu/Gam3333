using UnityEngine;

// Where a shot is aimed: the locked enemy's aim point, or what the screen centre looks at in free aim.
public static class AimSolver
{
    public static Vector3 AimPoint(MechTargetLockController targeting, Camera camera, Transform owner, float range, LayerMask layers)
        => AimPoint(targeting, camera, owner, range, layers, out _);

    public static Vector3 AimPoint(MechTargetLockController targeting, Camera camera, Transform owner, float range, LayerMask layers, out bool hasContact)
    {
        hasContact = false;
        var enemy = targeting != null && targeting.isActiveAndEnabled && !targeting.IsFreeAim ? targeting.CurrentTarget : null;
        if (enemy != null && enemy.isActiveAndEnabled) { hasContact = true; return enemy.AimPosition; }
        Ray ray = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        Vector3 aimPoint = ray.GetPoint(range);
        float nearest = range;
        foreach (var hit in Physics.RaycastAll(ray, range, layers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(owner) || hit.distance >= nearest) continue;
            nearest = hit.distance;
            hasContact = true;
            aimPoint = hit.point;
        }
        return aimPoint;
    }
}
