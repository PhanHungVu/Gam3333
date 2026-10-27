using System;
using System.Collections.Generic;
using UnityEngine;

// Chunk placement: seeded, spread out, never overlapping, inside the margin, on flattened ground.
public static class ChunkPlacementChecks
{
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
    }
}
