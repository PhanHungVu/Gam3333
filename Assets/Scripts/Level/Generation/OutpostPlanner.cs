using System;
using System.Collections.Generic;

public enum OutpostSlot { Centre, Flat, WallEdge, High }

// One slot type's rules: how many pieces, and each piece prefab's chance.
public sealed class OutpostSlotSettings
{
    public float[] weights = new float[0];
    public int min, max;
}

// A KOTH chunk's outpost rules as plain data (ChunkOutpost fills it in).
public sealed class OutpostSettings
{
    public OutpostSlotSettings centre = new OutpostSlotSettings { min = 1, max = 1 };
    public OutpostSlotSettings flat = new OutpostSlotSettings { min = 3, max = 6 };
    public OutpostSlotSettings wallEdge = new OutpostSlotSettings { min = 4, max = 8 };
    public OutpostSlotSettings high = new OutpostSlotSettings { min = 1, max = 3 };
    public float spacing = 6f;          // metres between pieces
    public float laneClearance = 2f;    // metres kept clear either side of each entry lane
    public float minScale = 0.9f, maxScale = 1.2f;
}

public sealed class OutpostItem
{
    public float X, Z, Height, Yaw, Scale;
    public OutpostSlot Slot;
    public int Prefab;
}

// Plans the pieces of a KOTH chunk's outpost in plain code (portable), from its generated interior.
// Each walkable cell inside the circle is a wall edge (next to a wall), high (on top of a block) or
// flat; entry lanes (plus clearance) stay clear. A centre piece goes in the middle, then each slot
// type gets a seeded number of pieces on its cells, never closer than Spacing.
public static class OutpostPlanner
{
    public static List<OutpostItem> Plan(TerrainMap map, int seed, ChunkPlacement p, ChunkFootprint size,
        List<Tuple<float, float>> entrances, float laneWidth, float stepHeight, OutpostSettings s)
    {
        var items = new List<OutpostItem>();
        if (!size.Circle) return items;
        var random = new Random(seed);
        var cells = new Dictionary<OutpostSlot, List<int>>
        {
            { OutpostSlot.Flat, new List<int>() }, { OutpostSlot.WallEdge, new List<int>() }, { OutpostSlot.High, new List<int>() },
        };
        float r = size.Radius * 0.9f, clearHalf = laneWidth * 0.5f + Math.Max(0f, s.laneClearance);
        for (int row = 0; row < map.Rows; row++)
            for (int col = 0; col < map.Cols; col++)
            {
                float dx = CellX(map, col) - p.X, dz = CellZ(map, row) - p.Z;
                if (dx * dx + dz * dz > r * r || !map.IsWalkable(col, row)) continue;
                if (InLane(dx, dz, entrances, clearHalf)) continue;
                var slot = NextToWall(map, col, row) ? OutpostSlot.WallEdge
                    : Rise(map, col, row, p.Height) >= stepHeight - 0.25f && Flat(map, col, row) ? OutpostSlot.High
                    : Flat(map, col, row) ? OutpostSlot.Flat : (OutpostSlot)(-1);
                if ((int)slot >= 0) cells[slot].Add(row * map.Cols + col);
            }

        // The centre piece sits in the middle of the hold area.
        int centreCount = Count(s.centre, random);
        if (centreCount > 0 && Total(s.centre) > 0f)
            items.Add(Make(p.X, p.Z, map, OutpostSlot.Centre, s.centre, s, random));

        foreach (var slot in new[] { OutpostSlot.WallEdge, OutpostSlot.High, OutpostSlot.Flat })
        {
            var rules = slot == OutpostSlot.WallEdge ? s.wallEdge : slot == OutpostSlot.High ? s.high : s.flat;
            int want = Count(rules, random);
            if (want <= 0 || Total(rules) <= 0f) continue;
            var pool = cells[slot];
            Shuffle(pool, random);
            foreach (int c in pool)
            {
                if (want <= 0) break;
                float x = CellX(map, c % map.Cols), z = CellZ(map, c / map.Cols);
                if (TooClose(items, x, z, s.spacing)) continue;
                items.Add(Make(x, z, map, slot, rules, s, random));
                want--;
            }
        }
        return items;
    }

    static OutpostItem Make(float x, float z, TerrainMap map, OutpostSlot slot, OutpostSlotSettings rules, OutpostSettings s, Random random) =>
        new OutpostItem
        {
            X = x, Z = z, Height = DecorScatter.HeightAt(map, x, z), Slot = slot,
            Yaw = (float)random.NextDouble() * 360f,
            Scale = s.minScale + (s.maxScale - s.minScale) * (float)random.NextDouble(),
            Prefab = Pick(rules.weights, (float)random.NextDouble()),
        };

    static int Count(OutpostSlotSettings rules, Random random)
    {
        int lo = Math.Max(0, Math.Min(rules.min, rules.max)), hi = Math.Max(0, Math.Max(rules.min, rules.max));
        return random.Next(lo, hi + 1);
    }

    static float Total(OutpostSlotSettings rules) { float t = 0f; foreach (var w in rules.weights) t += Math.Max(0f, w); return t; }

    static int Pick(float[] weights, float roll)
    {
        float total = 0f; foreach (var w in weights) total += Math.Max(0f, w);
        float v = roll * total;
        for (int i = 0; i < weights.Length; i++) { float w = Math.Max(0f, weights[i]); if (v < w) return i; v -= w; }
        return weights.Length - 1;
    }

    public static bool InLane(float dx, float dz, List<Tuple<float, float>> entrances, float halfWidth)
    {
        if (entrances == null) return false;
        foreach (var e in entrances)
        {
            float ex = e.Item1, ez = e.Item2, len = ex * ex + ez * ez;
            float t = len > 0f ? Math.Max(0f, Math.Min(1f, (dx * ex + dz * ez) / len)) : 0f;
            float px = ex * t - dx, pz = ez * t - dz;
            if (px * px + pz * pz <= halfWidth * halfWidth) return true;
        }
        return false;
    }

    public static bool NextToWall(TerrainMap map, int col, int row)
    {
        for (int d = 0; d < 4; d++)
        {
            int c = col + (d == 0 ? 1 : d == 1 ? -1 : 0), r = row + (d == 2 ? 1 : d == 3 ? -1 : 0);
            if (c < 0 || r < 0 || c >= map.Cols || r >= map.Rows || !map.IsWalkable(c, r)) return true;
        }
        return false;
    }

    static bool Flat(TerrainMap map, int col, int row)
    {
        float a = map.Height(col, row), b = map.Height(col + 1, row), c = map.Height(col, row + 1), d = map.Height(col + 1, row + 1);
        return Math.Max(Math.Max(a, b), Math.Max(c, d)) - Math.Min(Math.Min(a, b), Math.Min(c, d)) <= 0.2f;
    }

    public static float Rise(TerrainMap map, int col, int row, float baseHeight) =>
        (map.Height(col, row) + map.Height(col + 1, row) + map.Height(col, row + 1) + map.Height(col + 1, row + 1)) * 0.25f - baseHeight;

    static bool TooClose(List<OutpostItem> items, float x, float z, float spacing)
    {
        foreach (var i in items) if ((i.X - x) * (i.X - x) + (i.Z - z) * (i.Z - z) < spacing * spacing) return true;
        return false;
    }

    static void Shuffle(List<int> list, Random random)
    {
        for (int i = list.Count - 1; i > 0; i--) { int j = random.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
    }

    static float CellX(TerrainMap map, int col) => map.MinX + (col + 0.5f) * map.CellSize;
    static float CellZ(TerrainMap map, int row) => map.MinZ + (row + 0.5f) * map.CellSize;
}
