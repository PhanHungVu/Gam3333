using System.Collections.Generic;
using UnityEngine;

// Port of flocking.gd. Every function returns a steering FORCE (desired velocity
// minus current velocity, clamped to MaxForce) on the XZ plane. Nothing here
// touches the scene except AvoidObstacles (one physics probe via the agent),
// so the maths ports straight to C++/glm when the custom engine needs it.
public static class Steering
{
    // Wander circle, scaled from the fish values for a ~6 m/s ground enemy.
    const float WanderRadius = 1f;
    const float WanderDistance = 2f;
    const float WanderJitter = 2f; // radians/sec of random drift

    public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

    static Vector3 ToForce(EnemyAgent a, Vector3 desiredVelocity)
        => Vector3.ClampMagnitude(desiredVelocity - a.Velocity, a.MaxForce);

    // Reynolds seek: head for the point at full speed.
    public static Vector3 Seek(EnemyAgent a, Vector3 target)
    {
        Vector3 to = Flat(target - a.Position);
        if (to.sqrMagnitude < 1e-6f) return Vector3.zero;
        return ToForce(a, to.normalized * a.MaxSpeed);
    }

    // Same as Seek, but for when a direction is already known (the flow field case)
    // rather than a single point to head toward.
    public static Vector3 SeekDirection(EnemyAgent a, Vector3 direction)
    {
        if (direction.sqrMagnitude < 1e-6f) return Vector3.zero;
        return ToForce(a, direction.normalized * a.MaxSpeed);
    }

    // Seek that slows down inside slowRadius and stops at stopRadius.
    // (This is the ArriveState TODO from the fish project, now implemented.)
    public static Vector3 Arrive(EnemyAgent a, Vector3 target, float slowRadius, float stopRadius)
    {
        Vector3 to = Flat(target - a.Position);
        float d = to.magnitude;
        if (d < 1e-4f) return ToForce(a, Vector3.zero);
        float t = Mathf.Clamp01((d - stopRadius) / Mathf.Max(0.01f, slowRadius - stopRadius));
        return ToForce(a, (to / d) * (a.MaxSpeed * t));
    }

    public static Vector3 Wander(EnemyAgent a, float dt)
    {
        Vector3 heading = Flat(a.Velocity);
        heading = heading.sqrMagnitude > 1e-4f ? heading.normalized : a.Forward;

        a.WanderAngle += UnityEngine.Random.Range(-1f, 1f) * WanderJitter * dt;
        Vector3 displacement = new Vector3(Mathf.Cos(a.WanderAngle), 0f, Mathf.Sin(a.WanderAngle)) * WanderRadius;
        Vector3 desired = heading * WanderDistance + displacement;
        return ToForce(a, desired.normalized * a.WanderSpeed);
    }

    public static Vector3 Separation(EnemyAgent a, List<EnemyAgent> neighbors)
    {
        Vector3 steer = Vector3.zero;
        int count = 0;
        for (int i = 0; i < neighbors.Count; i++)
        {
            Vector3 away = Flat(a.Position - neighbors[i].Position);
            float d = away.magnitude;
            if (d >= a.SeparationRadius) continue;
            if (d < 1e-3f) { Vector2 r = UnityEngine.Random.insideUnitCircle.normalized; away = new Vector3(r.x, 0f, r.y); d = 1e-3f; } // spawned on top of each other
            steer += (away / d) / d; // direction, stronger when closer
            count++;
        }
        if (count == 0) return Vector3.zero;
        steer /= count;
        return ToForce(a, steer.normalized * a.MaxSpeed);
    }

    public static Vector3 Alignment(EnemyAgent a, List<EnemyAgent> neighbors)
    {
        if (neighbors.Count == 0) return Vector3.zero;
        Vector3 avg = Vector3.zero;
        for (int i = 0; i < neighbors.Count; i++) avg += neighbors[i].Velocity;
        avg /= neighbors.Count;
        if (avg.sqrMagnitude < 1e-6f) return Vector3.zero;
        return ToForce(a, avg.normalized * a.MaxSpeed);
    }

    public static Vector3 Cohesion(EnemyAgent a, List<EnemyAgent> neighbors)
    {
        if (neighbors.Count == 0) return Vector3.zero;
        Vector3 center = Vector3.zero;
        for (int i = 0; i < neighbors.Count; i++) center += neighbors[i].Position;
        center /= neighbors.Count;
        return Seek(a, center);
    }

    // Replaces avoid_walls(). Instead of scanning grid cells, probe ahead along the
    // velocity (the WALL_PROBE idea) and push away from whatever the probe hits.
    // The push is blended with a tangent along the obstacle, on the side that keeps
    // us moving toward the goal, so a head-on hit slides around instead of braking dead.
    public static Vector3 AvoidObstacles(EnemyAgent a)
    {
        Vector3 heading = Flat(a.Velocity);
        if (heading.sqrMagnitude < 0.01f) return Vector3.zero;
        heading.Normalize();

        if (!a.TryProbe(heading, a.ProbeDistance, out RaycastHit hit)) return Vector3.zero;

        Vector3 n = Flat(hit.normal);
        n = n.sqrMagnitude > 1e-4f ? n.normalized : -heading;

        Vector3 tangent = Vector3.Cross(Vector3.up, n);
        if (Vector3.Dot(tangent, a.GoalDirection) < 0f) tangent = -tangent;

        float closeness = 1f - Mathf.Clamp01(hit.distance / a.ProbeDistance); // 0 far .. 1 touching
        return ToForce(a, (n * 0.5f + tangent).normalized * a.MaxSpeed) * closeness;
    }
}