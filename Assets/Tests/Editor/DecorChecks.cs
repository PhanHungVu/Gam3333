using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Decor scatter: seeded, spaced out, in woods with clearings, never on roads, chunks or cliffs.
public static class DecorChecks
{
    public static void Run(Action<bool, string> check)
    {
        var terrain = new TerrainSettings();
        var placement = new ChunkPlacementSettings { levelHeight = terrain.levelHeight, terraceLevels = terrain.terraceLevels };
        var footprints = new List<ChunkFootprint> { new ChunkFootprint(24f, 24f) };
        for (int i = 0; i < 3; i++) footprints.Add(new ChunkFootprint(80f, 80f));
        var map = TerrainGenerator.Generate(11, terrain);
        var placed = ChunkPlacer.Place(map, 11, footprints, placement);
        ChunkPlacer.Flatten(map, placed, footprints, placement);
        RoadBuilder.Build(map, placed, footprints, null, new RoadSettings());

        var trees = new DecorGroupSettings { weights = new[] { 0.7f, 0.3f } };
        var first = DecorScatter.Scatter(map, 5, trees, placed, footprints, true);
        var again = DecorScatter.Scatter(map, 5, trees, placed, footprints, true);
        check(first.Count > 0 && first.Count == again.Count && first.Zip(again, (a, b) => a.X == b.X && a.Z == b.Z && a.Prefab == b.Prefab).All(x => x),
            "The same seed scatters the same decor");
        check(first.Count >= 100 && first.Count <= 1200, "Default tree settings give a few hundred trees on a 500 m map");

        bool spaced = true;
        for (int i = 0; i < first.Count && spaced; i++)
            for (int j = i + 1; j < first.Count; j++)
                if ((first[i].X - first[j].X) * (first[i].X - first[j].X) + (first[i].Z - first[j].Z) * (first[i].Z - first[j].Z)
                    < trees.spacing * trees.spacing - 0.001f) { spaced = false; break; }
        check(spaced, "No two decor items are closer than their spacing");

        bool clear = true;
        foreach (var item in first)
        {
            int col = Mathf.FloorToInt((item.X - map.MinX) / map.CellSize), row = Mathf.FloorToInt((item.Z - map.MinZ) / map.CellSize);
            if (!map.IsWalkable(col, row) || map.IsRoad(col, row)) clear = false;
            foreach (var p in placed) if (RoadBuilder.InsideFootprint(item.X, item.Z, p, footprints[p.Footprint], 0f)) clear = false;
        }
        check(clear, "Decor never lands on a cliff, a road or a chunk");
        check(first.All(item => Mathf.Abs(item.Height - DecorScatter.HeightAt(map, item.X, item.Z)) < 0.001f), "Decor sits on the ground");

        var even = DecorScatter.Scatter(map, 5, new DecorGroupSettings { weights = new[] { 1f }, clusterStrength = 0f }, placed, footprints, true);
        check(first.Count < even.Count, "Woods and clearings leave open ground compared with an even spread");
    }
}
