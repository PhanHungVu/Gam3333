using System.Collections.Generic;
using UnityEngine;

// Port of SpatialGrid.gd. Buckets enemies by XZ grid cell so neighbour lookups
// don't compare every enemy against every other enemy (O(n^2) -> roughly O(n)).
// Rebuilt at most once per frame, by whichever enemy ticks first.
public static class EnemySpatialHash
{
    const float CellSize = 8f;
    static readonly Dictionary<long, List<EnemyAgent>> cells = new Dictionary<long, List<EnemyAgent>>();
    static int builtFrame = -1;

    static int Cell(float v) => Mathf.FloorToInt(v / CellSize);
    static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

    public static void EnsureBuilt(List<EnemyAgent> all)
    {
        if (builtFrame == Time.frameCount) return;
        builtFrame = Time.frameCount;

        // Keep the lists (and their capacity) between frames so this doesn't allocate.
        foreach (var list in cells.Values) list.Clear();

        for (int i = 0; i < all.Count; i++)
        {
            Vector3 p = all[i].Position;
            long key = Key(Cell(p.x), Cell(p.z));
            if (!cells.TryGetValue(key, out var bucket))
                cells[key] = bucket = new List<EnemyAgent>();
            bucket.Add(all[i]);
        }
    }

    public static void Query(EnemyAgent agent, float radius, List<EnemyAgent> results)
    {
        Vector3 p = agent.Position;
        int cx = Cell(p.x), cz = Cell(p.z);
        int reach = Mathf.CeilToInt(radius / CellSize);
        float radiusSqr = radius * radius;

        for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                if (!cells.TryGetValue(Key(cx + dx, cz + dz), out var bucket)) continue;
                for (int i = 0; i < bucket.Count; i++)
                {
                    var other = bucket[i];
                    if (other == agent) continue;
                    Vector3 d = other.Position - p;
                    d.y = 0f;
                    if (d.sqrMagnitude < radiusSqr) results.Add(other);
                }
            }
    }
}