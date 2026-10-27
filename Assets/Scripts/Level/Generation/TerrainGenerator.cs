using System;

// The settings the terrain generator needs, as plain data (SubLevelGenSettings fills it in).
public sealed class TerrainSettings
{
    public float mapSize = 500f;          // metres, square, centred on the origin
    public float cellSize = 4f;           // metres (one golem)
    public float hillSize = 160f;         // metres across a typical hill
    public int noiseLayers = 4;
    public float layerStrength = 0.5f;    // how much each smaller layer adds (persistence)
    public float layerDetail = 2f;        // how much smaller each layer is (lacunarity)
    public float heightSpread = 2.2f;     // stretches the noise so it reaches every level (it bunches in the middle)
    public int terraceLevels = 4;
    public float levelHeight = 6f;        // metres between levels
    public float terraceSharpness = 0.85f; // 0 = smooth hills, 1 = sharp steps
    public float maxWalkableSlope = 35f;  // degrees; steeper cells are walls (cliffs)
    public float cliffCoverage = 0.5f;    // roughly how much of the map is terraced with cliffs; the rest is smooth hills
    public float variationSize = 250f;    // metres across a cliffy or smooth region
    public float regionBlend = 0.1f;      // width of the change from cliffy to smooth (wider = gradual)
    public float smoothAreaTerracing = 0f; // how much terracing smooth regions keep (0 = none, 1 = as cliffy)
}

// The generated terrain as plain data: a height per grid corner and walkable/wall per cell.
public sealed class TerrainMap
{
    public int Cols { get; }
    public int Rows { get; }
    public float CellSize { get; }
    public float MinX { get; }   // world X of the grid's left edge
    public float MinZ { get; }   // world Z of the grid's bottom edge
    public float[] Heights { get; }   // (Cols + 1) * (Rows + 1) corners
    public bool[] Walkable { get; }   // Cols * Rows cells
    public bool[] Road { get; }       // Cols * Rows cells, set by RoadBuilder

    public TerrainMap(int cols, int rows, float cellSize, float minX, float minZ)
    {
        Cols = cols; Rows = rows; CellSize = cellSize; MinX = minX; MinZ = minZ;
        Heights = new float[(cols + 1) * (rows + 1)];
        Walkable = new bool[cols * rows];
        Road = new bool[cols * rows];
    }

    public float Height(int cornerX, int cornerZ) => Heights[cornerZ * (Cols + 1) + cornerX];
    public bool IsWalkable(int col, int row) => Walkable[row * Cols + col];
    public bool IsRoad(int col, int row) => Road[row * Cols + col];
    public int WalkableCount { get { int n = 0; foreach (var w in Walkable) if (w) n++; return n; } }
}

// Seeded terrain in plain code (no Unity), so it ports to the custom engine.
// 1. Layered Perlin noise gives rolling ground, stretched by Height Spread so it reaches every
// level (layered noise rarely strays far from the middle).  2. Heights are rounded towards a few terrace
// levels; where the noise rises gently the step between levels becomes a wide ramp, where it rises
// sharply it stays a cliff.  3. A second, much larger noise decides where the map is terraced with
// cliffs and where it stays as smooth rolling hills (Cliff Coverage), blending between the two over
// Region Blend; smooth regions can keep some soft terracing (Smooth Area Terracing) so they match.
// 4. Any cell steeper than Max Walkable Slope is marked a wall.
public static class TerrainGenerator
{
    public static TerrainMap Generate(int seed, TerrainSettings s)
    {
        float cell = Math.Max(0.5f, s.cellSize);
        int count = Math.Max(1, (int)Math.Round(s.mapSize / cell));
        float half = count * cell * 0.5f;
        var map = new TerrainMap(count, count, cell, -half, -half);

        var noise = new SeededNoise(seed);
        var offsets = new Random(seed ^ 0x5bd1e995);
        float offsetX = (float)offsets.NextDouble() * 1000f, offsetZ = (float)offsets.NextDouble() * 1000f;
        float scale = 1f / Math.Max(1f, s.hillSize);
        var regions = new SeededNoise(seed ^ 0x1b873593);
        float regionScale = 1f / Math.Max(1f, s.variationSize);
        float top = Math.Max(1, s.terraceLevels - 1) * s.levelHeight;
        float threshold = 1f - Math.Max(0f, Math.Min(1f, s.cliffCoverage));

        for (int z = 0; z <= count; z++)
            for (int x = 0; x <= count; x++)
            {
                float wx = map.MinX + x * cell, wz = map.MinZ + z * cell;
                float raw = noise.Layered(wx * scale + offsetX, wz * scale + offsetZ, s.noiseLayers, s.layerStrength, s.layerDetail);
                raw = Math.Max(0f, Math.Min(1f, (raw - 0.5f) * Math.Max(0.1f, s.heightSpread) + 0.5f));
                float terraced = Terrace(raw, s.terraceLevels, s.levelHeight, s.terraceSharpness);
                float region = regions.Layered(wx * regionScale + offsetZ, wz * regionScale + offsetX, 2, 0.5f, 2f);
                region = Math.Max(0f, Math.Min(1f, (region - 0.5f) * 2.2f + 0.5f));
                float blendHalf = Math.Max(0.01f, s.regionBlend);
                float cliffy = SmoothStep(threshold - blendHalf, threshold + blendHalf, region); // 1 = terraced, 0 = smooth
                float floor = Math.Max(0f, Math.Min(1f, s.smoothAreaTerracing));
                float terracing = floor + (1f - floor) * cliffy;
                float smooth = raw * top;
                map.Heights[z * (count + 1) + x] = smooth + (terraced - smooth) * terracing;
            }

        for (int row = 0; row < count; row++)
            for (int col = 0; col < count; col++)
                map.Walkable[row * count + col] = IsWalkable(map.Height(col, row), map.Height(col + 1, row),
                    map.Height(col, row + 1), map.Height(col + 1, row + 1), cell, s.maxWalkableSlope);
        return map;
    }

    // 0..1 noise to a height on (or between) terrace levels. Levels sit at 0, levelHeight, 2x...
    public static float Terrace(float raw01, int levels, float levelHeight, float sharpness)
    {
        int steps = Math.Max(1, levels - 1);
        float t = Math.Max(0f, Math.Min(1f, raw01)) * steps;
        int level = Math.Min(steps - 1, (int)Math.Floor(t));
        float within = t - level;                      // 0..1 between this level and the next
        float band = Math.Max(0.02f, 1f - Math.Max(0f, Math.Min(1f, sharpness)));
        float a = 0.5f - band * 0.5f, b = 0.5f + band * 0.5f;
        float k = Math.Max(0f, Math.Min(1f, (within - a) / (b - a)));
        float shaped = k * k * (3f - 2f * k);          // smooth step across the band
        return (level + shaped) * levelHeight;
    }

    static float SmoothStep(float a, float b, float v)
    {
        float k = Math.Max(0f, Math.Min(1f, (v - a) / Math.Max(0.0001f, b - a)));
        return k * k * (3f - 2f * k);
    }

    // A cell is walkable when the rise across it is no steeper than the limit.
    public static bool IsWalkable(float h00, float h10, float h01, float h11, float cellSize, float maxSlopeDegrees)
    {
        float high = Math.Max(Math.Max(h00, h10), Math.Max(h01, h11));
        float low = Math.Min(Math.Min(h00, h10), Math.Min(h01, h11));
        double slope = Math.Atan2(high - low, cellSize) * 180.0 / Math.PI;
        return slope <= maxSlopeDegrees;
    }
}
