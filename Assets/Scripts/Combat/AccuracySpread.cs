using UnityEngine;

// Shared projection for the crosshair reference circle and smaller actual shot circle.
// Offsets lie on the camera-facing plane through the intended aim point, so a shot
// aimed anywhere inside the inner circle also lands there at that depth (before collisions).
public static class AccuracySpread
{
    public static float Percent(PartData head)
    {
        if (head != null && head.stats != null)
            foreach (var entry in head.stats)
                if (entry.type == StatType.Accuracy)
                    return Finite(entry.value) ? Mathf.Clamp(entry.value, 0f, 100f) : 100f;
        // Legacy/test loadouts without an Accuracy entry retain their centered aim.
        return 100f;
    }

    public static float HalfAngle(float accuracy, float maximumAngle)
        => Finite(accuracy) && Finite(maximumAngle)
            ? (1f - Mathf.Clamp01(accuracy / 100f)) * Mathf.Clamp(maximumAngle, 0f, 45f) : 0f;

    static float Slope(float angle) => Finite(angle) ? Mathf.Tan(Mathf.Clamp(angle, 0f, 45f) * Mathf.Deg2Rad) : 0f;

    // Scale projected radius, rather than approximating it by multiplying degrees.
    public static float ScaleRadius(float angle, float fraction)
        => Finite(fraction) ? Mathf.Atan(Slope(angle) * Mathf.Clamp01(fraction)) * Mathf.Rad2Deg : 0f;

    public static float DistanceFactor(float distance, float referenceRange)
        => Finite(distance) && Finite(referenceRange) && referenceRange > 0 ? Mathf.Clamp01(distance / referenceRange) : 1f;

    public static float ScreenRadius(Camera camera, float angle)
        => camera != null ? Mathf.Abs(camera.pixelHeight * .5f * camera.projectionMatrix.m11) * Slope(angle) : 0f;

    public static Vector3 AimPoint(Camera camera, Vector3 center, float angle, float direction01, float distance01)
    {
        if (camera == null || angle <= 0f || !Finite(angle)) return center;
        float depth = camera.WorldToScreenPoint(center).z;
        if (depth <= 0f) return center;
        float radius = (camera.orthographic ? 1f : depth) * Slope(angle) * Mathf.Clamp01(distance01);
        float bearing = direction01 * Mathf.PI * 2f;
        return center + (camera.transform.right * Mathf.Cos(bearing) + camera.transform.up * Mathf.Sin(bearing)) * radius;
    }

    public static Vector3 RandomAimPoint(Camera camera, Vector3 center, float angle)
        => angle > 0f ? AimPoint(camera, center, angle, Random.value, Random.value) : center;

    // A physical cone around one already-resolved attack direction. Shotgun head
    // error is sampled once for that direction, not once per pellet.
    public static Vector3 ConeDirection(Vector3 axis, float halfAngle, float bearing01, float radius01)
    {
        axis = axis.sqrMagnitude > .0001f ? axis.normalized : Vector3.forward;
        if (!Finite(halfAngle) || halfAngle <= 0) return axis;
        Vector3 right = Vector3.Cross(axis, Mathf.Abs(axis.y) > .99f ? Vector3.right : Vector3.up).normalized;
        Vector3 up = Vector3.Cross(right, axis).normalized;
        float angle = Mathf.Clamp(halfAngle, 0, 45) * Mathf.Clamp01(radius01) * Mathf.Deg2Rad;
        float bearing = bearing01 * Mathf.PI * 2;
        return axis * Mathf.Cos(angle) + (right * Mathf.Cos(bearing) + up * Mathf.Sin(bearing)) * Mathf.Sin(angle);
    }

    public static Vector3 RandomConeDirection(Vector3 axis, float halfAngle)
        => halfAngle > 0 ? ConeDirection(axis, halfAngle, Random.value, Random.value) : axis.normalized;

    static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
}
