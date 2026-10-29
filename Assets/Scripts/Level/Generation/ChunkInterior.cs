using System;
using System.Collections.Generic;

// One circular chunk's interior rules as plain data (ChunkDefinition fills it in).
public sealed class ChunkInteriorSettings
{
    public float featureSize = 20f;   // metres across a typical block or dip
    public float stepHeight = 3f;     // metres per step: one step = cover, stacked steps = walls
    public float maxHeight = 6f;      // metres: the tallest the interior rises above the chunk's base
    public float sharpness = 0.85f;   // 0 = smooth slopes, 1 = sheer steps
    public float holdArea = 0.3f;     // fraction of the radius kept as the open hold area in the middle
    public float holdCover = 0.5f;    // 0..1: how much light cover the hold area gets (one step at most)
    public float laneWidth = 8f;      // metres: lanes from each road entrance to the centre stay at base level
    public float edgeFade = 0.15f;    // fraction of the radius over which the interior fades back to base at the edge
}

// Generates the inside of circular chunks in plain code (portable): terraced noise gives blocks,
// slopes and walls (steps steeper than Max Walkable Slope are walls); the middle is an open hold
// area with only light cover; lanes from every road entrance to the centre stay at base level, so
// the centre can always be reached; the edge fades back to base so roads join smoothly.
public static class ChunkInterior
{
    public static void Build(TerrainMap map, int seed, IReadOnlyList<ChunkPlacement> placed, IReadOnlyList<ChunkFootprint> footprints,
        IReadOnlyList<Road> roads, float maxWalkableSlope)
    {
        for (int index = 0; index < placed.Count; index++)
        {
            var p = placed[index];
            var size = footprints[p.Footprint];
            var s = size.Interior;
            if (!size.Circle || s == null) continue;
            var noise = new SeededNoise(seed ^ (0x5f356495 * (index + 1)));
            var entrances = Entrances(index, p, roads);
            float r = size.Radius, step = Math.Max(0.1f, s.stepHeight);
            int steps = Math.Max(1, (int)Math.Floor(Math.Max(step, s.maxHeight) / step));
            float scale = 1f / Math.Max(1f, s.featureSize);

            for (int z = 0; z <= map.Rows; z++)
                for (int x = 0; x <= map.Cols; x++)
                {
                    float dx = map.MinX + x * map.CellSize - p.X, dz = map.MinZ + z * map.CellSize - p.Z;
                    float along = (float)Math.Sqrt(dx * dx + dz * dz) / r;
                    if (along >= 1f) continue;
                    float raw = noise.Layered(dx * scale + 500f, dz * scale + 500f, 2, 0.5f, 2f);
                    raw = Clamp01((raw - 0.5f) * 2.2f + 0.5f);
                    float offset;
                    if (along <= s.holdArea)
                        // Light cover in the hold area: at most one step, and only on the noise's peaks.
                        offset = TerrainGenerator.Terrace(raw * Clamp01(s.holdCover), 2, step, s.sharpness);
                    else
                        offset = TerrainGenerator.Terrace(raw, steps + 1, step, s.sharpness);
                    float fadeStart = 1f - Clamp01(s.edgeFade);
                    float fade = along <= fadeStart ? 1f : 1f - SmoothStep((along - fadeStart) / Math.Max(0.0001f, 1f - fadeStart));
                    if (CornerOfLaneCell(dx, dz, map.CellSize, entrances, s.laneWidth * 0.5f)) offset = 0f;
                    map.Heights[z * (map.Cols + 1) + x] = p.Height + offset * fade;
                }
        }
        for (int row = 0; row < map.Rows; row++)
            for (int col = 0; col < map.Cols; col++)
                map.Walkable[row * map.Cols + col] = TerrainGenerator.IsWalkable(map.Height(col, row), map.Height(col + 1, row),
                    map.Height(col, row + 1), map.Height(col + 1, row + 1), map.CellSize, maxWalkableSlope);
    }

    // Where roads meet this chunk's edge, as offsets from its centre.
    public static List<Tuple<float, float>> Entrances(int index, ChunkPlacement p, IReadOnlyList<Road> roads)
    {
        var list = new List<Tuple<float, float>>();
        if (roads == null) return list;
        foreach (var road in roads)
        {
            int last = road.X.Count - 1;
            if (road.From == index) list.Add(Tuple.Create(road.X[0] - p.X, road.Z[0] - p.Z));
            if (road.To == index) list.Add(Tuple.Create(road.X[last] - p.X, road.Z[last] - p.Z));
        }
        return list;
    }

    // A corner belongs to the lane when any of the four cells around it has its centre in the lane, so
    // every lane cell is fully at base level and the lane stays a connected walkable path at any angle.
    static bool CornerOfLaneCell(float dx, float dz, float cell, List<Tuple<float, float>> entrances, float halfWidth)
    {
        float h = cell * 0.5f;
        return NearLane(dx - h, dz - h, entrances, halfWidth) || NearLane(dx + h, dz - h, entrances, halfWidth)
            || NearLane(dx - h, dz + h, entrances, halfWidth) || NearLane(dx + h, dz + h, entrances, halfWidth);
    }

    // Within half a lane of the line from an entrance to the centre.
    static bool NearLane(float dx, float dz, List<Tuple<float, float>> entrances, float halfWidth)
    {
        if (dx * dx + dz * dz <= halfWidth * halfWidth) return true;
        foreach (var e in entrances)
        {
            float ex = e.Item1, ez = e.Item2, len = ex * ex + ez * ez;
            float t = len > 0f ? Math.Max(0f, Math.Min(1f, (dx * ex + dz * ez) / len)) : 0f;
            float px = ex * t - dx, pz = ez * t - dz;
            if (px * px + pz * pz <= halfWidth * halfWidth) return true;
        }
        return false;
    }

    // True when every entrance can walk to the centre over walkable cells.
    public static bool CentreReachable(TerrainMap map, ChunkPlacement p, ChunkFootprint size, List<Tuple<float, float>> entrances)
    {
        int Cell(float x, float z)
        {
            int c = (int)Math.Floor((x - map.MinX) / map.CellSize), r = (int)Math.Floor((z - map.MinZ) / map.CellSize);
            return c < 0 || r < 0 || c >= map.Cols || r >= map.Rows ? -1 : r * map.Cols + c;
        }
        int centre = Cell(p.X, p.Z);
        if (centre < 0) return false;
        var seen = new bool[map.Cols * map.Rows];
        var queue = new Queue<int>(); queue.Enqueue(centre); seen[centre] = true;
        while (queue.Count > 0)
        {
            int c = queue.Dequeue(), cx = c % map.Cols, cz = c / map.Cols;
            for (int d = 0; d < 4; d++)
            {
                int nx = cx + (d == 0 ? 1 : d == 1 ? -1 : 0), nz = cz + (d == 2 ? 1 : d == 3 ? -1 : 0);
                if (nx < 0 || nz < 0 || nx >= map.Cols || nz >= map.Rows) continue;
                int n = nz * map.Cols + nx;
                float wx = map.MinX + (nx + 0.5f) * map.CellSize - p.X, wz = map.MinZ + (nz + 0.5f) * map.CellSize - p.Z;
                if (seen[n] || !map.Walkable[n] || !size.Inside(wx, wz, map.CellSize)) continue;
                seen[n] = true; queue.Enqueue(n);
            }
        }
        foreach (var e in entrances)
        {
            // The entrance cell sits on the edge; step one cell inwards so it lies inside the circle.
            float len = (float)Math.Sqrt(e.Item1 * e.Item1 + e.Item2 * e.Item2);
            float inward = len > 0f ? Math.Max(0f, len - map.CellSize) / len : 0f;
            int cell = Cell(p.X + e.Item1 * inward, p.Z + e.Item2 * inward);
            if (cell < 0 || !seen[cell]) return false;
        }
        return true;
    }

    static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
    static float SmoothStep(float t) { t = Clamp01(t); return t * t * (3f - 2f * t); }
}
