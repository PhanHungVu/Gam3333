using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Chunk placement: seeded, spread out, never overlapping, inside the margin, on flattened ground.
public static class ChunkPlacementChecks
{
    static float Dist(float dx, float dz) => Mathf.Sqrt(dx * dx + dz * dz);

    public static void Run(Action<bool, string> check)
    {
        var terrain = new TerrainSettings();
        var s = new ChunkPlacementSettings { levelHeight = terrain.levelHeight, terraceLevels = terrain.terraceLevels };
        var footprints = new List<ChunkFootprint> { new ChunkFootprint(24f, 24f) };
        for (int i = 0; i < 4; i++) footprints.Add(new ChunkFootprint(120f, 120f));

        var map = TerrainGenerator.Generate(7, terrain);
        var placed = ChunkPlacer.Place(map, 7, footprints, s);
        var again = ChunkPlacer.Place(TerrainGenerator.Generate(7, terrain), 7, footprints, s);
        bool same = placed.Count == again.Count;
        for (int i = 0; same && i < placed.Count; i++)
            same = placed[i].X == again[i].X && placed[i].Z == again[i].Z && placed[i].Height == again[i].Height;
        check(same, "The same seed places chunks in the same spots");
        check(placed.Count == footprints.Count, "The start and every mission chunk fit on a 500 m map");
        check(placed.Count > 0 && placed[0].Footprint == 0 && Mathf.Abs(placed[0].X) <= 250f / 3f + 1f && Mathf.Abs(placed[0].Z) <= 250f / 3f + 1f,
            "The start chunk goes near the middle of the map");

        bool apart = true, inside = true;
        for (int i = 0; i < placed.Count; i++)
        {
            var size = footprints[placed[i].Footprint];
            var others = placed.GetRange(0, i);
            if (ChunkPlacer.Overlaps(placed[i].X, placed[i].Z, size, others, footprints, s.minSpacing)) apart = false;
            if (Mathf.Abs(placed[i].X) + size.Width * 0.5f > 250f - s.edgeMargin + 0.01f
                || Mathf.Abs(placed[i].Z) + size.Depth * 0.5f > 250f - s.edgeMargin + 0.01f) inside = false;
        }
        check(apart, "Chunks never overlap and keep their spacing");
        check(inside, "Chunks stay inside the map's edge margin");

        ChunkPlacer.Flatten(map, placed, footprints, s);
        bool flat = true, walkable = true, onLevel = true;
        foreach (var p in placed)
        {
            var size = footprints[p.Footprint];
            if (Mathf.Abs(p.Height / s.levelHeight - Mathf.Round(p.Height / s.levelHeight)) > 0.001f) onLevel = false;
            for (int row = 0; row < map.Rows; row++)
                for (int col = 0; col < map.Cols; col++)
                {
                    float x0 = map.MinX + col * map.CellSize, z0 = map.MinZ + row * map.CellSize;
                    bool cellInside = x0 >= p.X - size.Width * 0.5f && x0 + map.CellSize <= p.X + size.Width * 0.5f
                        && z0 >= p.Z - size.Depth * 0.5f && z0 + map.CellSize <= p.Z + size.Depth * 0.5f;
                    if (!cellInside) continue;
                    if (Mathf.Abs(map.Height(col, row) - p.Height) > 0.001f) flat = false;
                    if (!map.IsWalkable(col, row)) walkable = false;
                }
        }
        check(onLevel, "Each chunk sits on a terrace level");
        check(flat && walkable, "Ground under every chunk is flat and walkable after flattening");

        // Circular chunks: placed apart by the gap between circles, flattened as circles, roads meet their edge.
        var circles = new List<ChunkFootprint> { new ChunkFootprint(24f, 24f) };
        for (int i = 0; i < 3; i++) circles.Add(new ChunkFootprint(100f, 100f, true));
        var circleMap = TerrainGenerator.Generate(9, terrain);
        var round = ChunkPlacer.Place(circleMap, 9, circles, s);
        bool circlesApart = round.Count == circles.Count;
        for (int i = 0; i < round.Count; i++)
            for (int j = i + 1; j < round.Count; j++)
                if (ChunkFootprint.Gap(circles[round[i].Footprint], circles[round[j].Footprint],
                        round[j].X - round[i].X, round[j].Z - round[i].Z) < s.minSpacing - 0.01f) circlesApart = false;
        check(circlesApart, "Circular chunks all fit and keep their spacing");

        ChunkPlacer.Flatten(circleMap, round, circles, s);
        bool roundFlat = true;
        foreach (var p in round)
        {
            var size = circles[p.Footprint];
            if (!size.Circle) continue;
            for (int z = 0; z <= circleMap.Rows; z++)
                for (int x = 0; x <= circleMap.Cols; x++)
                {
                    float dx = circleMap.MinX + x * circleMap.CellSize - p.X, dz = circleMap.MinZ + z * circleMap.CellSize - p.Z;
                    if (size.Inside(dx, dz, 0f) && Mathf.Abs(circleMap.Height(x, z) - p.Height) > 0.001f) roundFlat = false;
                }
        }
        check(roundFlat, "Ground inside a circular chunk is flat");

        // Interior: blocks and walls up to Max Height, open hold area, lanes keep the centre reachable.
        var interior = new ChunkInteriorSettings();
        var arena = new List<ChunkFootprint> { new ChunkFootprint(24f, 24f) };
        for (int i = 0; i < 3; i++) arena.Add(new ChunkFootprint(100f, 100f, true, interior));
        TerrainMap Arena(out List<ChunkPlacement> spots, out List<Road> ways)
        {
            var m = TerrainGenerator.Generate(9, terrain);
            spots = ChunkPlacer.Place(m, 9, arena, s);
            ChunkPlacer.Flatten(m, spots, arena, s);
            ways = RoadBuilder.Build(m, spots, arena, null, new RoadSettings());
            ChunkInterior.Build(m, 9, spots, arena, ways, 35f);
            return m;
        }
        var arenaMap = Arena(out var arenaSpots, out var arenaRoads);
        var arenaAgain = Arena(out _, out _);
        check(arenaMap.Heights.SequenceEqual(arenaAgain.Heights), "The same seed generates the same chunk interiors");

        bool inRange = true, holdLight = true, varied = false, reachable = true;
        for (int i = 0; i < arenaSpots.Count; i++)
        {
            var p = arenaSpots[i]; var size = arena[p.Footprint];
            if (!size.Circle) continue;
            for (int z = 0; z <= arenaMap.Rows; z++)
                for (int x = 0; x <= arenaMap.Cols; x++)
                {
                    float dx = arenaMap.MinX + x * arenaMap.CellSize - p.X, dz = arenaMap.MinZ + z * arenaMap.CellSize - p.Z;
                    float r = Mathf.Sqrt(dx * dx + dz * dz);
                    if (r >= size.Radius) continue;
                    float rise = arenaMap.Height(x, z) - p.Height;
                    if (rise < -0.001f || rise > interior.maxHeight + 0.001f) inRange = false;
                    if (r <= size.Radius * interior.holdArea && rise > interior.stepHeight + 0.001f) holdLight = false;
                    if (rise > interior.stepHeight - 0.5f) varied = true;
                }
            if (!ChunkInterior.CentreReachable(arenaMap, p, size, ChunkInterior.Entrances(i, p, arenaRoads))) reachable = false;
        }
        check(inRange && varied, "Chunk interiors rise in steps up to Max Height above the chunk's base");
        check(holdLight, "The hold area in the middle only gets light cover (one step at most)");
        check(reachable, "Every road entrance can walk to the chunk's centre through the generated interior");

        var roundRoads = RoadBuilder.Build(circleMap, round, circles, null, new RoadSettings());
        bool onEdge = true;
        foreach (var r in roundRoads)
        {
            var a = round[r.From]; var b = round[r.To];
            if (circles[a.Footprint].Circle && Mathf.Abs(Dist(r.X[0] - a.X, r.Z[0] - a.Z) - circles[a.Footprint].Radius) > 0.01f) onEdge = false;
            int last = r.X.Count - 1;
            if (circles[b.Footprint].Circle && Mathf.Abs(Dist(r.X[last] - b.X, r.Z[last] - b.Z) - circles[b.Footprint].Radius) > 0.01f) onEdge = false;
        }
        check(roundRoads.Count > 0 && onEdge && RoadBuilder.AllConnected(round.Count, roundRoads),
            "Roads join circular chunks at the point on their edge facing the next chunk");
    }
}
