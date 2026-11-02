using System;
using System.Collections.Generic;

// The escort's path as plain data (portable): from the start chunk's centre, along each road of a chain
// route, through each checkpoint chunk's centre, to the end chunk's centre. Distances are measured along it.
public sealed class EscortRoute
{
    public readonly List<float> X = new List<float>(), Y = new List<float>(), Z = new List<float>();
    public readonly List<float> Distance = new List<float>();             // along the route to each point
    public readonly List<float> CheckpointDistances = new List<float>();  // each chunk after the start, in order; the last is the end
    public float Length => Distance.Count > 0 ? Distance[Distance.Count - 1] : 0f;
    public bool Complete { get; private set; }                          // every chunk joined by its road

    // roads must come from a Chain route (in order, each From the earlier chunk).
    public static EscortRoute Build(IReadOnlyList<ChunkPlacement> placed, IReadOnlyList<Road> roads)
    {
        var route = new EscortRoute();
        if (placed.Count == 0) return route;
        route.Add(placed[0].X, placed[0].Height, placed[0].Z);
        int at = 0, joined = 0;
        foreach (var road in roads)
        {
            if (road.From != at) break;
            for (int i = 0; i < road.X.Count; i++) route.Add(road.X[i], road.Height[i], road.Z[i]);
            var next = placed[road.To];
            route.Add(next.X, next.Height, next.Z);
            route.CheckpointDistances.Add(route.Length);
            at = road.To; joined++;
        }
        route.Complete = joined == placed.Count - 1;
        return route;
    }

    void Add(float x, float y, float z)
    {
        float d = 0f;
        if (X.Count > 0)
        {
            int last = X.Count - 1;
            float dx = x - X[last], dz = z - Z[last];
            d = Distance[last] + (float)Math.Sqrt(dx * dx + dz * dz);
            if (d - Distance[last] < 0.001f) return; // skip repeated points
        }
        X.Add(x); Y.Add(y); Z.Add(z); Distance.Add(d);
    }

    // The point at a distance along the route, and the flat direction of travel there.
    public void Sample(float distance, out float x, out float y, out float z, out float dirX, out float dirZ)
    {
        x = y = z = 0f; dirX = 0f; dirZ = 1f;
        if (X.Count == 0) return;
        if (X.Count == 1) { x = X[0]; y = Y[0]; z = Z[0]; return; }
        distance = Math.Max(0f, Math.Min(Length, distance));
        int i = 1;
        while (i < X.Count - 1 && Distance[i] < distance) i++;
        float span = Math.Max(0.0001f, Distance[i] - Distance[i - 1]);
        float t = Math.Max(0f, Math.Min(1f, (distance - Distance[i - 1]) / span));
        x = X[i - 1] + (X[i] - X[i - 1]) * t;
        y = Y[i - 1] + (Y[i] - Y[i - 1]) * t;
        z = Z[i - 1] + (Z[i] - Z[i - 1]) * t;
        float dx = X[i] - X[i - 1], dz = Z[i] - Z[i - 1], len = (float)Math.Sqrt(dx * dx + dz * dz);
        if (len > 0.0001f) { dirX = dx / len; dirZ = dz / len; }
    }

    // The furthest checkpoint reached at or before this distance (-1 = none yet), i.e. the respawn point.
    public int LastCheckpoint(float distance)
    {
        int last = -1;
        for (int i = 0; i < CheckpointDistances.Count; i++) if (CheckpointDistances[i] <= distance + 0.001f) last = i;
        return last;
    }
}
