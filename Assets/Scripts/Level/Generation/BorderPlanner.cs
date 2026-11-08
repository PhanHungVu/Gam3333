using System;
using System.Collections.Generic;

// The map border's rules as plain data (SubLevelGenSettings fills them in).
public sealed class BorderSettings
{
    public float width = 16f;           // metres of trees inward from each map edge
    public float spacing = 4f;          // metres between trees (smaller = denser)
    public float[] weights = { 1f };    // chance of each tree prefab, relative to the others
    public float minScale = 1.6f, maxScale = 2.2f;
}

// Plans a thick band of trees all round the map's edge, in plain code (portable). Trees sit on a jittered
// grid one Spacing apart, only within Width of an edge, never on road cells; seeded, so a seed always gives
// the same border. Everything else on the map (chunks, roads, decor) is placed exactly as without it.
public static class BorderPlanner
{
    public static List<DecorItem> Plan(TerrainMap map, int seed, BorderSettings s)
    {
        var items = new List<DecorItem>();
        if (s == null || s.width <= 0f || s.spacing <= 0f || s.weights == null || s.weights.Length == 0) return items;
        var random = new Random(seed ^ 0x2545f491);
        float width = map.Cols * map.CellSize, depth = map.Rows * map.CellSize;
        float total = 0f; foreach (float w in s.weights) total += Math.Max(0f, w);
        if (total <= 0f) return items;
        float jitter = s.spacing * 0.35f;
        for (float gz = s.spacing * 0.5f; gz < depth; gz += s.spacing)
        for (float gx = s.spacing * 0.5f; gx < width; gx += s.spacing)
        {
            float lx = gx + (float)(random.NextDouble() * 2 - 1) * jitter;
            float lz = gz + (float)(random.NextDouble() * 2 - 1) * jitter;
            double yaw = random.NextDouble(), scale = random.NextDouble(), pick = random.NextDouble();
            if (lx < 0f || lz < 0f || lx >= width || lz >= depth) continue;
            float edge = Math.Min(Math.Min(lx, width - lx), Math.Min(lz, depth - lz));
            if (edge > s.width) continue;
            int col = Math.Min(map.Cols - 1, (int)(lx / map.CellSize)), row = Math.Min(map.Rows - 1, (int)(lz / map.CellSize));
            if (map.IsRoad(col, row)) continue;
            float x = map.MinX + lx, z = map.MinZ + lz;
            int prefab = 0; float acc = 0f, target = (float)pick * total;
            for (int i = 0; i < s.weights.Length; i++) { acc += Math.Max(0f, s.weights[i]); if (target <= acc) { prefab = i; break; } }
            items.Add(new DecorItem
            {
                X = x, Z = z, Height = DecorScatter.HeightAt(map, x, z), Yaw = (float)(yaw * 360.0),
                Scale = s.minScale + (float)scale * Math.Max(0f, s.maxScale - s.minScale), Prefab = prefab,
            });
        }
        return items;
    }

    // True when enemy pathing and spawning should treat a point as a wall: inside the border band and not on
    // a road (a road crossing the band stays open).
    public static bool IsBorderWall(TerrainMap map, float x, float z, float bandWidth)
    {
        if (bandWidth <= 0f || !InBand(map, x, z, bandWidth)) return false;
        int col = (int)((x - map.MinX) / map.CellSize), row = (int)((z - map.MinZ) / map.CellSize);
        if (col < 0 || row < 0 || col >= map.Cols || row >= map.Rows) return true;
        return !map.IsRoad(col, row);
    }

    // True when a point is inside the border band (for checks and tools).
    public static bool InBand(TerrainMap map, float x, float z, float bandWidth)
    {
        float lx = x - map.MinX, lz = z - map.MinZ, w = map.Cols * map.CellSize, d = map.Rows * map.CellSize;
        return Math.Min(Math.Min(lx, w - lx), Math.Min(lz, d - lz)) <= bandWidth;
    }
}
