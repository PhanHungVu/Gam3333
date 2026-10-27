using System;
using System.Collections.Generic;

// A path connector on a chunk, in the chunk's own space: where a road attaches and which way it leaves.
public readonly struct ChunkConnector
{
    public readonly float X, Z, ForwardX, ForwardZ;
    public ChunkConnector(float x, float z, float forwardX, float forwardZ)
    {
        float length = (float)Math.Sqrt(forwardX * forwardX + forwardZ * forwardZ);
        X = x; Z = z;
        ForwardX = length > 0.0001f ? forwardX / length : 0f;
        ForwardZ = length > 0.0001f ? forwardZ / length : 1f;
    }
}

public sealed class RoadSettings
{
    public int extraLinks = 1;         // loops added beyond the minimum network
    public float roadWidth = 12f;      // metres
    public float maxRoadSlope = 20f;   // degrees; roads are evened out to stay under this
    public float roadBlend = 8f;       // metres over which a road's edge fades into the terrain
    public float slopeCost = 4f;       // how strongly the route avoids slopes
    public float cliffCost = 10f;      // carved roads only: how strongly they still avoid crossing cliffs
    public bool carveIfNoWayRound = true; // when no route avoids cliffs, carve a ramp through instead of skipping the road
    public int smoothing = 3;          // cells either side averaged when evening out the road
    public float maxWalkableSlope = 35f;
}

// One road between two placed chunks: its centre line, as world points with heights.
public sealed class Road
{
    public int From, To;                                   // indices into the placements
    public readonly List<float> X = new List<float>(), Z = new List<float>(), Height = new List<float>();
    public readonly List<int> RouteCells = new List<int>(); // the pathfound cells (row * cols + col)
    public bool Carved;                                    // true when no way round existed and it cuts through a cliff
}

// Connects placed chunks with roads, in plain code (portable).
// 1. Links: the shortest network joining every chunk, plus a few extra links for loops.
// 2. Each link runs between the best-facing pair of path connectors, leaving each chunk straight out.
// 3. A* over the terrain grid finds the route: it goes round cliffs and chunks, preferring gentle ground.
//    Only if there is no way round at all (and Carve If No Way Round is on) does it cut through a cliff.
// 4. The road's height is evened out and kept under Max Road Slope, pressed into the terrain across
//    its width and blended at the edges. Cells are re-marked walkable.
public static class RoadBuilder
{
    public static List<Road> Build(TerrainMap map, IReadOnlyList<ChunkPlacement> placed, IReadOnlyList<ChunkFootprint> footprints,
        IReadOnlyList<IReadOnlyList<ChunkConnector>> connectors, RoadSettings s)
    {
        var roads = new List<Road>();
        if (placed.Count < 2) return roads;
        float halfWidth = Math.Max(0.5f, s.roadWidth * 0.5f);
        float leadOut = halfWidth + map.CellSize;
        var blocked = FootprintCells(map, placed, footprints, halfWidth);

        foreach (var link in Links(placed, s.extraLinks))
        {
            var a = placed[link.Item1]; var b = placed[link.Item2];
            PickConnectors(a, b, footprints, connectors, out var ca, out var cb);
            float ax = a.X + ca.X, az = a.Z + ca.Z, bx = b.X + cb.X, bz = b.Z + cb.Z;
            float lax = ax + ca.ForwardX * leadOut, laz = az + ca.ForwardZ * leadOut;
            float lbx = bx + cb.ForwardX * leadOut, lbz = bz + cb.ForwardZ * leadOut;
            int from = Cell(map, lax, laz), to = Cell(map, lbx, lbz);
            var route = FindRoute(map, from, to, blocked, s, false);
            bool carved = false;
            if (route == null && s.carveIfNoWayRound) { route = FindRoute(map, from, to, blocked, s, true); carved = route != null; }
            if (route == null) continue;

            var road = new Road { From = link.Item1, To = link.Item2, Carved = carved };
            road.RouteCells.AddRange(route);
            Add(road, ax, az, a.Height); Add(road, lax, laz, a.Height);
            foreach (int c in route) Add(road, CellX(map, c), CellZ(map, c), CellHeight(map, c));
            Add(road, lbx, lbz, b.Height); Add(road, bx, bz, b.Height);
            EvenOut(road, s);
            roads.Add(road);
        }
        Press(map, roads, placed, footprints, halfWidth, s);
        return roads;
    }

    // True when every placed chunk can be reached from the start (placement 0) by road.
    public static bool AllConnected(int placedCount, IReadOnlyList<Road> roads)
    {
        if (placedCount <= 1) return true;
        var reached = new bool[placedCount]; reached[0] = true;
        int count = 1;
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var road in roads)
            {
                if (road.From >= placedCount || road.To >= placedCount || reached[road.From] == reached[road.To]) continue;
                reached[road.From] = reached[road.To] = true; count++; grew = true;
            }
        }
        return count == placedCount;
    }

    // Minimum spanning tree over chunk centres (Prim's), then the shortest unused links as loops.
    public static List<Tuple<int, int>> Links(IReadOnlyList<ChunkPlacement> placed, int extra)
    {
        int n = placed.Count;
        var links = new List<Tuple<int, int>>();
        var inTree = new bool[n]; inTree[0] = true;
        for (int added = 1; added < n; added++)
        {
            int bestA = -1, bestB = -1; float best = float.PositiveInfinity;
            for (int i = 0; i < n; i++) if (inTree[i])
                for (int j = 0; j < n; j++) if (!inTree[j])
                {
                    float d = Distance(placed[i], placed[j]);
                    if (d < best) { best = d; bestA = i; bestB = j; }
                }
            inTree[bestB] = true;
            links.Add(Tuple.Create(bestA, bestB));
        }
        var spare = new List<Tuple<int, int>>();
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (!links.Exists(l => (l.Item1 == i && l.Item2 == j) || (l.Item1 == j && l.Item2 == i))) spare.Add(Tuple.Create(i, j));
        spare.Sort((p, q) => Distance(placed[p.Item1], placed[p.Item2]).CompareTo(Distance(placed[q.Item1], placed[q.Item2])));
        for (int k = 0; k < Math.Min(Math.Max(0, extra), spare.Count); k++) links.Add(spare[k]);
        return links;
    }

    // The connector pair (one per chunk) whose lead-out points are closest; a chunk with no
    // connectors uses the middle of its edge facing the other chunk.
    static void PickConnectors(ChunkPlacement a, ChunkPlacement b, IReadOnlyList<ChunkFootprint> footprints,
        IReadOnlyList<IReadOnlyList<ChunkConnector>> connectors, out ChunkConnector bestA, out ChunkConnector bestB)
    {
        var listA = ConnectorsOf(a, b, footprints, connectors);
        var listB = ConnectorsOf(b, a, footprints, connectors);
        bestA = listA[0]; bestB = listB[0];
        float best = float.PositiveInfinity;
        foreach (var ca in listA)
            foreach (var cb in listB)
            {
                float ax = a.X + ca.X + ca.ForwardX * 10f, az = a.Z + ca.Z + ca.ForwardZ * 10f;
                float bx = b.X + cb.X + cb.ForwardX * 10f, bz = b.Z + cb.Z + cb.ForwardZ * 10f;
                float d = (ax - bx) * (ax - bx) + (az - bz) * (az - bz);
                if (d < best) { best = d; bestA = ca; bestB = cb; }
            }
    }

    static IReadOnlyList<ChunkConnector> ConnectorsOf(ChunkPlacement self, ChunkPlacement other, IReadOnlyList<ChunkFootprint> footprints,
        IReadOnlyList<IReadOnlyList<ChunkConnector>> connectors)
    {
        var own = connectors != null && self.Footprint < connectors.Count ? connectors[self.Footprint] : null;
        if (own != null && own.Count > 0) return own;
        var size = footprints[self.Footprint];
        float dx = other.X - self.X, dz = other.Z - self.Z;
        if (Math.Abs(dx) * size.Depth >= Math.Abs(dz) * size.Width)
            return new[] { new ChunkConnector(Math.Sign(dx) * size.Width * 0.5f, 0f, Math.Sign(dx), 0f) };
        return new[] { new ChunkConnector(0f, Math.Sign(dz) * size.Depth * 0.5f, 0f, Math.Sign(dz)) };
    }

    // Cells a road may not route through: chunk footprints, widened by half a road.
    static bool[] FootprintCells(TerrainMap map, IReadOnlyList<ChunkPlacement> placed, IReadOnlyList<ChunkFootprint> footprints, float pad)
    {
        var blocked = new bool[map.Cols * map.Rows];
        for (int c = 0; c < blocked.Length; c++)
            foreach (var p in placed)
                if (InsideFootprint(CellX(map, c), CellZ(map, c), p, footprints[p.Footprint], pad)) { blocked[c] = true; break; }
        return blocked;
    }

    public static bool InsideFootprint(float x, float z, ChunkPlacement p, ChunkFootprint size, float pad) =>
        Math.Abs(x - p.X) <= size.Width * 0.5f + pad && Math.Abs(z - p.Z) <= size.Depth * 0.5f + pad;

    // A* over cells, 8 directions; slopes cost more. Footprint cells are impassable (except the start and
    // goal, which sit just outside their chunks). Cliffs are impassable too, unless carving is allowed,
    // when they are only very expensive.
    static List<int> FindRoute(TerrainMap map, int start, int goal, bool[] blocked, RoadSettings s, bool throughCliffs)
    {
        if (start < 0 || goal < 0) return null;
        int count = map.Cols * map.Rows;
        var g = new float[count]; var came = new int[count]; var closed = new bool[count];
        for (int i = 0; i < count; i++) { g[i] = float.PositiveInfinity; came[i] = -1; }
        var open = new MinHeap();
        g[start] = 0f; open.Push(start, Heuristic(map, start, goal));
        float tanMax = (float)Math.Tan(Math.Max(1f, s.maxRoadSlope) * Math.PI / 180.0);
        while (open.Count > 0)
        {
            int current = open.Pop();
            if (current == goal) break;
            if (closed[current]) continue;
            closed[current] = true;
            int cx = current % map.Cols, cz = current / map.Cols;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    int nx = cx + dx, nz = cz + dz;
                    if (nx < 0 || nz < 0 || nx >= map.Cols || nz >= map.Rows) continue;
                    int next = nz * map.Cols + nx;
                    if (closed[next] || (blocked[next] && next != goal)) continue;
                    float step = map.CellSize * (dx != 0 && dz != 0 ? 1.41421356f : 1f);
                    float rise = Math.Abs(CellHeight(map, next) - CellHeight(map, current));
                    float cost = step * (1f + s.slopeCost * (rise / step) / tanMax);
                    if (!map.IsWalkable(nx, nz))
                    {
                        if (!throughCliffs && next != goal) continue;
                        cost *= Math.Max(1f, s.cliffCost);
                    }
                    float total = g[current] + cost;
                    if (total >= g[next]) continue;
                    g[next] = total; came[next] = current;
                    open.Push(next, total + Heuristic(map, next, goal));
                }
        }
        if (came[goal] < 0 && goal != start) return null;
        var path = new List<int>();
        for (int c = goal; c >= 0; c = came[c]) { path.Add(c); if (c == start) break; }
        path.Reverse();
        return path;
    }

    // Averages the route's heights, then keeps every rise under Max Road Slope with the two ends
    // held at their chunks' heights - this is what turns a cliff crossing into a carved ramp.
    static void EvenOut(Road road, RoadSettings s)
    {
        int n = road.Height.Count;
        var h = road.Height.ToArray();
        var smoothed = (float[])h.Clone();
        int w = Math.Max(0, s.smoothing);
        for (int i = 2; i < n - 2; i++)
        {
            float sum = 0f; int k = 0;
            for (int j = Math.Max(2, i - w); j <= Math.Min(n - 3, i + w); j++) { sum += h[j]; k++; }
            smoothed[i] = sum / k;
        }
        float tanMax = (float)Math.Tan(Math.Max(1f, s.maxRoadSlope) * Math.PI / 180.0);
        for (int i = 1; i < n; i++) Limit(road, smoothed, i, i - 1, tanMax);
        for (int i = n - 2; i >= 0; i--) Limit(road, smoothed, i, i + 1, tanMax);
        smoothed[0] = h[0]; smoothed[n - 1] = h[n - 1];
        for (int i = 0; i < n; i++) road.Height[i] = smoothed[i];
    }

    static void Limit(Road road, float[] h, int i, int from, float tanMax)
    {
        float run = (float)Math.Sqrt(Sq(road.X[i] - road.X[from]) + Sq(road.Z[i] - road.Z[from]));
        float rise = run * tanMax;
        h[i] = Math.Max(h[from] - rise, Math.Min(h[from] + rise, h[i]));
    }

    // Sets terrain under each road to the road's height, fades the sides, marks road cells, re-marks walkable.
    static void Press(TerrainMap map, List<Road> roads, IReadOnlyList<ChunkPlacement> placed, IReadOnlyList<ChunkFootprint> footprints,
        float halfWidth, RoadSettings s)
    {
        float blend = Math.Max(0.01f, s.roadBlend);
        for (int z = 0; z <= map.Rows; z++)
            for (int x = 0; x <= map.Cols; x++)
            {
                float wx = map.MinX + x * map.CellSize, wz = map.MinZ + z * map.CellSize;
                if (InAnyFootprint(wx, wz, placed, footprints)) continue;
                if (!Nearest(roads, wx, wz, out float distance, out float roadHeight) || distance >= halfWidth + blend) continue;
                int i = z * (map.Cols + 1) + x;
                if (distance <= halfWidth) { map.Heights[i] = roadHeight; continue; }
                float t = (distance - halfWidth) / blend;
                float keep = t * t * (3f - 2f * t);
                map.Heights[i] = roadHeight + (map.Heights[i] - roadHeight) * keep;
            }
        for (int c = 0; c < map.Cols * map.Rows; c++)
        {
            float cx = CellX(map, c), cz = CellZ(map, c);
            map.Road[c] = !InAnyFootprint(cx, cz, placed, footprints) && Nearest(roads, cx, cz, out float d, out _) && d <= halfWidth;
        }
        for (int row = 0; row < map.Rows; row++)
            for (int col = 0; col < map.Cols; col++)
                map.Walkable[row * map.Cols + col] = TerrainGenerator.IsWalkable(map.Height(col, row), map.Height(col + 1, row),
                    map.Height(col, row + 1), map.Height(col + 1, row + 1), map.CellSize, s.maxWalkableSlope);
    }

    static bool InAnyFootprint(float x, float z, IReadOnlyList<ChunkPlacement> placed, IReadOnlyList<ChunkFootprint> footprints)
    {
        foreach (var p in placed) if (InsideFootprint(x, z, p, footprints[p.Footprint], 0f)) return true;
        return false;
    }

    // Distance to the nearest road centre line, and that road's height there.
    static bool Nearest(List<Road> roads, float x, float z, out float distance, out float height)
    {
        distance = float.PositiveInfinity; height = 0f;
        foreach (var road in roads)
            for (int i = 1; i < road.X.Count; i++)
            {
                float ax = road.X[i - 1], az = road.Z[i - 1], bx = road.X[i], bz = road.Z[i];
                float vx = bx - ax, vz = bz - az, len = vx * vx + vz * vz;
                float t = len > 0f ? Math.Max(0f, Math.Min(1f, ((x - ax) * vx + (z - az) * vz) / len)) : 0f;
                float px = ax + vx * t, pz = az + vz * t;
                float d = (float)Math.Sqrt(Sq(x - px) + Sq(z - pz));
                if (d < distance) { distance = d; height = road.Height[i - 1] + (road.Height[i] - road.Height[i - 1]) * t; }
            }
        return !float.IsPositiveInfinity(distance);
    }

    static void Add(Road road, float x, float z, float height) { road.X.Add(x); road.Z.Add(z); road.Height.Add(height); }
    static int Cell(TerrainMap map, float x, float z)
    {
        int col = (int)Math.Floor((x - map.MinX) / map.CellSize), row = (int)Math.Floor((z - map.MinZ) / map.CellSize);
        if (col < 0 || row < 0 || col >= map.Cols || row >= map.Rows) return -1;
        return row * map.Cols + col;
    }
    static float CellX(TerrainMap map, int c) => map.MinX + (c % map.Cols + 0.5f) * map.CellSize;
    static float CellZ(TerrainMap map, int c) => map.MinZ + (c / map.Cols + 0.5f) * map.CellSize;
    public static float CellHeight(TerrainMap map, int c)
    {
        int col = c % map.Cols, row = c / map.Cols;
        return (map.Height(col, row) + map.Height(col + 1, row) + map.Height(col, row + 1) + map.Height(col + 1, row + 1)) * 0.25f;
    }
    static float Heuristic(TerrainMap map, int a, int b) => (float)Math.Sqrt(Sq(CellX(map, a) - CellX(map, b)) + Sq(CellZ(map, a) - CellZ(map, b)));
    static float Distance(ChunkPlacement a, ChunkPlacement b) => (float)Math.Sqrt(Sq(a.X - b.X) + Sq(a.Z - b.Z));
    static float Sq(float v) => v * v;

    // Small binary heap for A*: lowest priority first.
    sealed class MinHeap
    {
        readonly List<int> items = new List<int>();
        readonly List<float> priorities = new List<float>();
        public int Count => items.Count;
        public void Push(int item, float priority)
        {
            items.Add(item); priorities.Add(priority);
            int i = items.Count - 1;
            while (i > 0) { int parent = (i - 1) / 2; if (priorities[parent] <= priorities[i]) break; Swap(i, parent); i = parent; }
        }
        public int Pop()
        {
            int top = items[0]; int last = items.Count - 1;
            Swap(0, last); items.RemoveAt(last); priorities.RemoveAt(last);
            int i = 0;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, smallest = i;
                if (l < items.Count && priorities[l] < priorities[smallest]) smallest = l;
                if (r < items.Count && priorities[r] < priorities[smallest]) smallest = r;
                if (smallest == i) break;
                Swap(i, smallest); i = smallest;
            }
            return top;
        }
        void Swap(int a, int b) { (items[a], items[b]) = (items[b], items[a]); (priorities[a], priorities[b]) = (priorities[b], priorities[a]); }
    }
}
