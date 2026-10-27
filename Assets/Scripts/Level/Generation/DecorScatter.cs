using System;
using System.Collections.Generic;

// One decor group's rules as plain data (SubLevelGenSettings fills it in).
public sealed class DecorGroupSettings
{
    public float[] weights = { 1f };   // chance of each prefab, relative to the others
    public float spacing = 6f;         // metres: no two items closer than this
    public float density = 0.15f;      // 0..1 chance an available spot gets an item, before clustering
    public float clusterStrength = 0.85f; // 0 = even spread, 1 = only in woods (clearings stay empty)
    public float clusterSize = 120f;   // metres across a wood or clearing
    public float roadClearance = 4f;   // metres kept clear beside roads
    public float chunkClearance = 6f;  // metres kept clear around chunks
    public float startClearance = 30f; // metres kept clear around the start chunk
    public float maxSlope = 30f;       // degrees; nothing on ground steeper than this (or on cliffs)
    public float minScale = 0.8f, maxScale = 1.3f;
}

// One placed item: where, on what height, turned and sized how, and which prefab.
public sealed class DecorItem
{
    public float X, Z, Height, Yaw, Scale;
    public int Prefab;
}

// Scatters decor (trees, later rocks and grass) in plain code (portable). Candidate spots sit on a
// jittered grid one Spacing apart, and never closer than Spacing to each other. A large-scale noise
// forms woods and clearings, and each spot is kept with a chance of Density x that mask. Spots on
// cliffs, steep ground, roads (plus clearance) or chunks (plus clearance, more round the start) are skipped.
public static class DecorScatter
{
    public static List<DecorItem> Scatter(TerrainMap map, int seed, DecorGroupSettings g,
        IReadOnlyList<ChunkPlacement> placed, IReadOnlyList<ChunkFootprint> footprints, bool firstIsStart)
    {
        var items = new List<DecorItem>();
        var random = new Random(seed);
        var woods = new SeededNoise(seed ^ 0x3c6ef372);
        float spacing = Math.Max(0.5f, g.spacing);
        float minX = map.MinX, minZ = map.MinZ, size = map.Cols * map.CellSize;
        int grid = Math.Max(1, (int)Math.Floor(size / spacing));
        var taken = new DecorItem[grid * grid];
        float weightTotal = 0f;
        foreach (var w in g.weights) weightTotal += Math.Max(0f, w);
        if (weightTotal <= 0f) return items;
        float tanSlope = (float)Math.Tan(Math.Max(0f, Math.Min(89f, g.maxSlope)) * Math.PI / 180.0);

        for (int gz = 0; gz < grid; gz++)
            for (int gx = 0; gx < grid; gx++)
            {
                // Draw every number up front so each spot's outcome depends only on the seed.
                float jx = (float)random.NextDouble(), jz = (float)random.NextDouble(), roll = (float)random.NextDouble();
                float pick = (float)random.NextDouble(), yaw = (float)random.NextDouble(), scale = (float)random.NextDouble();
                float x = minX + (gx + jx) * spacing, z = minZ + (gz + jz) * spacing;

                float wood = woods.Layered(x / Math.Max(1f, g.clusterSize), z / Math.Max(1f, g.clusterSize), 2, 0.5f, 2f);
                wood = SmoothStep(0.42f, 0.58f, wood);
                float chance = Math.Max(0f, g.density) * (1f - g.clusterStrength + g.clusterStrength * wood);
                if (roll >= chance) continue;
                if (!Usable(map, x, z, tanSlope, g.roadClearance)) continue;
                if (NearChunk(x, z, placed, footprints, g, firstIsStart)) continue;
                if (TooClose(taken, grid, gx, gz, x, z, spacing)) continue;

                var item = new DecorItem
                {
                    X = x, Z = z, Height = HeightAt(map, x, z),
                    Yaw = yaw * 360f, Scale = g.minScale + (g.maxScale - g.minScale) * scale, Prefab = Pick(g.weights, weightTotal, pick),
                };
                taken[gz * grid + gx] = item;
                items.Add(item);
            }
        return items;
    }

    // Walkable, gentle enough, and no road cell within the clearance.
    static bool Usable(TerrainMap map, float x, float z, float tanSlope, float roadClearance)
    {
        int col = (int)Math.Floor((x - map.MinX) / map.CellSize), row = (int)Math.Floor((z - map.MinZ) / map.CellSize);
        if (col < 0 || row < 0 || col >= map.Cols || row >= map.Rows || !map.IsWalkable(col, row)) return false;
        float h00 = map.Height(col, row), h10 = map.Height(col + 1, row), h01 = map.Height(col, row + 1), h11 = map.Height(col + 1, row + 1);
        float rise = Math.Max(Math.Max(h00, h10), Math.Max(h01, h11)) - Math.Min(Math.Min(h00, h10), Math.Min(h01, h11));
        if (rise > tanSlope * map.CellSize) return false;
        int reach = (int)Math.Ceiling(Math.Max(0f, roadClearance) / map.CellSize);
        for (int dz = -reach; dz <= reach; dz++)
            for (int dx = -reach; dx <= reach; dx++)
            {
                int c = col + dx, r = row + dz;
                if (c >= 0 && r >= 0 && c < map.Cols && r < map.Rows && map.IsRoad(c, r)) return false;
            }
        return true;
    }

    static bool NearChunk(float x, float z, IReadOnlyList<ChunkPlacement> placed, IReadOnlyList<ChunkFootprint> footprints,
        DecorGroupSettings g, bool firstIsStart)
    {
        foreach (var p in placed)
        {
            float pad = firstIsStart && p.Footprint == 0 ? g.startClearance : g.chunkClearance;
            if (RoadBuilder.InsideFootprint(x, z, p, footprints[p.Footprint], Math.Max(0f, pad))) return true;
        }
        return false;
    }

    static bool TooClose(DecorItem[] taken, int grid, int gx, int gz, float x, float z, float spacing)
    {
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int cx = gx + dx, cz = gz + dz;
                if (cx < 0 || cz < 0 || cx >= grid || cz >= grid) continue;
                var other = taken[cz * grid + cx];
                if (other != null && (other.X - x) * (other.X - x) + (other.Z - z) * (other.Z - z) < spacing * spacing) return true;
            }
        return false;
    }

    // Ground height at a point, blended from the four corners of its cell.
    public static float HeightAt(TerrainMap map, float x, float z)
    {
        float fx = (x - map.MinX) / map.CellSize, fz = (z - map.MinZ) / map.CellSize;
        int col = Math.Max(0, Math.Min(map.Cols - 1, (int)Math.Floor(fx))), row = Math.Max(0, Math.Min(map.Rows - 1, (int)Math.Floor(fz)));
        float tx = Math.Max(0f, Math.Min(1f, fx - col)), tz = Math.Max(0f, Math.Min(1f, fz - row));
        float a = map.Height(col, row) + (map.Height(col + 1, row) - map.Height(col, row)) * tx;
        float b = map.Height(col, row + 1) + (map.Height(col + 1, row + 1) - map.Height(col, row + 1)) * tx;
        return a + (b - a) * tz;
    }

    static int Pick(float[] weights, float total, float roll)
    {
        float r = roll * total;
        for (int i = 0; i < weights.Length; i++) { float w = Math.Max(0f, weights[i]); if (r < w) return i; r -= w; }
        return weights.Length - 1;
    }

    static float SmoothStep(float a, float b, float v)
    {
        float k = Math.Max(0f, Math.Min(1f, (v - a) / (b - a)));
        return k * k * (3f - 2f * k);
    }
}
