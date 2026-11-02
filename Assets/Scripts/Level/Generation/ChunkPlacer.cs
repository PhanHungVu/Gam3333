using System;
using System.Collections.Generic;

// A chunk's footprint in metres, as the placer sees it: a box (Width by Depth) or a circle
// (Width is the diameter). Every "how far from / inside this chunk" question goes through here.
public readonly struct ChunkFootprint
{
    public readonly float Width, Depth;
    public readonly bool Circle;
    public readonly ChunkInteriorSettings Interior; // circles only: generated slopes, walls and cover inside; null = flat
    public float Radius => Width * 0.5f;

    public ChunkFootprint(float width, float depth, bool circle = false, ChunkInteriorSettings interior = null)
    {
        Width = Math.Max(1f, width);
        Depth = circle ? Width : Math.Max(1f, depth);
        Circle = circle;
        Interior = circle ? interior : null;
    }

    // dx, dz: offset from the chunk's centre. Inside, with the footprint grown by pad metres.
    public bool Inside(float dx, float dz, float pad) => Circle
        ? dx * dx + dz * dz <= (Radius + pad) * (Radius + pad)
        : Math.Abs(dx) <= Width * 0.5f + pad && Math.Abs(dz) <= Depth * 0.5f + pad;

    // Distance from the footprint's edge (0 inside it).
    public float OutsideDistance(float dx, float dz)
    {
        if (Circle) return Math.Max(0f, (float)Math.Sqrt(dx * dx + dz * dz) - Radius);
        float ox = Math.Max(0f, Math.Abs(dx) - Width * 0.5f), oz = Math.Max(0f, Math.Abs(dz) - Depth * 0.5f);
        return (float)Math.Sqrt(ox * ox + oz * oz);
    }

    // Gap between two footprints (negative when they overlap); b sits at (dx, dz) from a.
    public static float Gap(ChunkFootprint a, ChunkFootprint b, float dx, float dz)
    {
        if (a.Circle && b.Circle) return (float)Math.Sqrt(dx * dx + dz * dz) - a.Radius - b.Radius;
        if (a.Circle) return b.OutsideDistance(-dx, -dz) - a.Radius;
        if (b.Circle) return a.OutsideDistance(dx, dz) - b.Radius;
        return Math.Max(Math.Abs(dx) - (a.Width + b.Width) * 0.5f, Math.Abs(dz) - (a.Depth + b.Depth) * 0.5f);
    }
}

// Where one chunk went: its centre, the ground height it sits on, and which footprint it uses.
public sealed class ChunkPlacement
{
    public float X, Z, Height;
    public int Footprint;   // index into the footprints passed to Place
}

public sealed class ChunkPlacementSettings
{
    public float minSpacing = 20f;      // metres of gap between chunk footprints
    public float edgeMargin = 20f;      // metres kept clear at the map border
    public float blendDistance = 24f;   // metres over which a flattened footprint fades into the hills
    public int candidates = 300;        // random spots tried per map
    public float flatnessWeight = 10f;  // how much uneven ground counts against a spot, versus spreading out
    public float levelHeight = 12f;     // flattened chunks snap to a terrace level
    public int terraceLevels = 4;
    public float maxWalkableSlope = 35f;
    public bool lastIsEnd;              // the last footprint is an end chunk: placed as far from the start as it can
}

// Places chunks on a TerrainMap and flattens the ground under them, in plain code (portable).
// The first footprint is the start (placed near the middle on the flattest spot); the rest are
// mission chunks, each put on the best remaining spot: far from chunks already placed and on
// ground that is already fairly flat. Chunks never overlap (with spacing) and stay inside the margin.
public static class ChunkPlacer
{
    public static List<ChunkPlacement> Place(TerrainMap map, int seed, IReadOnlyList<ChunkFootprint> footprints, ChunkPlacementSettings s)
    {
        var placed = new List<ChunkPlacement>();
        var random = new Random(seed ^ 0x2c1b3c6d);
        float minX = map.MinX, maxX = map.MinX + map.Cols * map.CellSize;
        float minZ = map.MinZ, maxZ = map.MinZ + map.Rows * map.CellSize;
        float centreX = (minX + maxX) * 0.5f, centreZ = (minZ + maxZ) * 0.5f;
        float mapHalf = (maxX - minX) * 0.5f;

        for (int f = 0; f < footprints.Count; f++)
        {
            var size = footprints[f];
            float loX = minX + s.edgeMargin + size.Width * 0.5f, hiX = maxX - s.edgeMargin - size.Width * 0.5f;
            float loZ = minZ + s.edgeMargin + size.Depth * 0.5f, hiZ = maxZ - s.edgeMargin - size.Depth * 0.5f;
            if (loX > hiX || loZ > hiZ) continue;
            bool isStart = f == 0;
            bool isEnd = s.lastIsEnd && f == footprints.Count - 1 && f > 0 && placed.Count > 0;
            ChunkPlacement best = null;
            float bestScore = float.NegativeInfinity;
            for (int c = 0; c < Math.Max(1, s.candidates); c++)
            {
                float x = Lerp(loX, hiX, (float)random.NextDouble());
                float z = Lerp(loZ, hiZ, (float)random.NextDouble());
                if (isStart)
                {
                    // The start stays within the middle third of the map.
                    x = Lerp(centreX - mapHalf / 3f, centreX + mapHalf / 3f, (float)random.NextDouble());
                    z = Lerp(centreZ - mapHalf / 3f, centreZ + mapHalf / 3f, (float)random.NextDouble());
                    x = Math.Max(loX, Math.Min(hiX, x)); z = Math.Max(loZ, Math.Min(hiZ, z));
                }
                if (Overlaps(x, z, size, placed, footprints, s.minSpacing)) continue;
                Range(map, x, z, size, out float low, out float high);
                float score = -(high - low) * s.flatnessWeight;
                if (isEnd) score += (float)Math.Sqrt((x - placed[0].X) * (x - placed[0].X) + (z - placed[0].Z) * (z - placed[0].Z));
                else if (!isStart) score += NearestDistance(x, z, placed);
                if (score > bestScore) { bestScore = score; best = new ChunkPlacement { X = x, Z = z, Footprint = f }; }
            }
            if (best == null) continue;
            best.Height = TargetHeight(map, best.X, best.Z, size, s);
            placed.Add(best);
        }
        return placed;
    }

    // Sets each footprint to its height and fades the edge into the surrounding ground, then
    // re-marks walkable cells (the faded edges become ramps up to the chunk).
    public static void Flatten(TerrainMap map, IReadOnlyList<ChunkPlacement> placed, IReadOnlyList<ChunkFootprint> footprints, ChunkPlacementSettings s)
    {
        float blend = Math.Max(0.01f, s.blendDistance);
        foreach (var p in placed)
        {
            var size = footprints[p.Footprint];
            for (int z = 0; z <= map.Rows; z++)
                for (int x = 0; x <= map.Cols; x++)
                {
                    float wx = map.MinX + x * map.CellSize, wz = map.MinZ + z * map.CellSize;
                    float outside = size.OutsideDistance(wx - p.X, wz - p.Z);
                    if (outside >= blend) continue;
                    float t = outside / blend;
                    float keep = t * t * (3f - 2f * t); // 0 at the footprint, 1 at the blend's edge
                    int i = z * (map.Cols + 1) + x;
                    map.Heights[i] = p.Height + (map.Heights[i] - p.Height) * keep;
                }
        }
        for (int row = 0; row < map.Rows; row++)
            for (int col = 0; col < map.Cols; col++)
                map.Walkable[row * map.Cols + col] = TerrainGenerator.IsWalkable(map.Height(col, row), map.Height(col + 1, row),
                    map.Height(col, row + 1), map.Height(col + 1, row + 1), map.CellSize, s.maxWalkableSlope);
    }

    // The terrace level nearest the footprint's average height.
    static float TargetHeight(TerrainMap map, float x, float z, ChunkFootprint size, ChunkPlacementSettings s)
    {
        double sum = 0; int count = 0;
        ForCorners(map, x, z, size, (h) => { sum += h; count++; });
        float average = count > 0 ? (float)(sum / count) : 0f;
        float level = (float)Math.Round(average / Math.Max(0.01f, s.levelHeight));
        level = Math.Max(0f, Math.Min(Math.Max(1, s.terraceLevels) - 1, level));
        return level * s.levelHeight;
    }

    static void Range(TerrainMap map, float x, float z, ChunkFootprint size, out float low, out float high)
    {
        float lo = float.PositiveInfinity, hi = float.NegativeInfinity;
        ForCorners(map, x, z, size, (h) => { if (h < lo) lo = h; if (h > hi) hi = h; });
        low = float.IsPositiveInfinity(lo) ? 0f : lo; high = float.IsNegativeInfinity(hi) ? 0f : hi;
    }

    static void ForCorners(TerrainMap map, float x, float z, ChunkFootprint size, Action<float> visit)
    {
        int x0 = Clamp((int)Math.Ceiling((x - size.Width * 0.5f - map.MinX) / map.CellSize), 0, map.Cols);
        int x1 = Clamp((int)Math.Floor((x + size.Width * 0.5f - map.MinX) / map.CellSize), 0, map.Cols);
        int z0 = Clamp((int)Math.Ceiling((z - size.Depth * 0.5f - map.MinZ) / map.CellSize), 0, map.Rows);
        int z1 = Clamp((int)Math.Floor((z + size.Depth * 0.5f - map.MinZ) / map.CellSize), 0, map.Rows);
        for (int cz = z0; cz <= z1; cz++)
            for (int cx = x0; cx <= x1; cx++)
                if (size.Inside(map.MinX + cx * map.CellSize - x, map.MinZ + cz * map.CellSize - z, 0f)) visit(map.Height(cx, cz));
    }

    public static bool Overlaps(float x, float z, ChunkFootprint size, IReadOnlyList<ChunkPlacement> placed,
        IReadOnlyList<ChunkFootprint> footprints, float spacing)
    {
        foreach (var p in placed)
            if (ChunkFootprint.Gap(footprints[p.Footprint], size, x - p.X, z - p.Z) < spacing) return true;
        return false;
    }

    static float NearestDistance(float x, float z, IReadOnlyList<ChunkPlacement> placed)
    {
        float nearest = float.PositiveInfinity;
        foreach (var p in placed) nearest = Math.Min(nearest, (float)Math.Sqrt((x - p.X) * (x - p.X) + (z - p.Z) * (z - p.Z)));
        return float.IsPositiveInfinity(nearest) ? 0f : nearest;
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;
    static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
}
