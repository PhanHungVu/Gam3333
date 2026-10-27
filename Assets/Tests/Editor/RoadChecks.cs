using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Roads: seeded, joining every chunk, starting at path connectors, never through chunks, walkable.
public static class RoadChecks
{
    public static void Run(Action<bool, string> check)
    {
        var terrain = new TerrainSettings();
        var placement = new ChunkPlacementSettings { levelHeight = terrain.levelHeight, terraceLevels = terrain.terraceLevels };
        var road = new RoadSettings();
        var footprints = new List<ChunkFootprint> { new ChunkFootprint(24f, 24f) };
        for (int i = 0; i < 3; i++) footprints.Add(new ChunkFootprint(80f, 80f));
        var startConnectors = new List<ChunkConnector> { new ChunkConnector(0f, 12f, 0f, 1f), new ChunkConnector(12f, 0f, 1f, 0f),
            new ChunkConnector(0f, -12f, 0f, -1f), new ChunkConnector(-12f, 0f, -1f, 0f) };
        var missionConnectors = new List<ChunkConnector> { new ChunkConnector(0f, 40f, 0f, 1f), new ChunkConnector(-40f, 0f, -1f, 0f) };
        var connectors = new List<IReadOnlyList<ChunkConnector>> { startConnectors, missionConnectors, missionConnectors, missionConnectors };

        TerrainMap Map(out List<ChunkPlacement> placed, out List<Road> roads)
        {
            var map = TerrainGenerator.Generate(7, terrain);
            placed = ChunkPlacer.Place(map, 7, footprints, placement);
            ChunkPlacer.Flatten(map, placed, footprints, placement);
            roads = RoadBuilder.Build(map, placed, footprints, connectors, road);
            return map;
        }
        var first = Map(out var placedA, out var roadsA);
        var second = Map(out _, out var roadsB);
        check(roadsA.Count == roadsB.Count && first.Heights.SequenceEqual(second.Heights) && first.Road.SequenceEqual(second.Road),
            "The same seed builds the same roads");

        var links = RoadBuilder.Links(placedA, road.extraLinks);
        check(links.Count == placedA.Count - 1 + road.extraLinks, "Roads form the shortest network plus the extra loop links");
        var joined = new HashSet<int> { 0 };
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var r in roadsA)
                if (joined.Contains(r.From) != joined.Contains(r.To)) { joined.Add(r.From); joined.Add(r.To); grew = true; }
        }
        check(roadsA.Count == links.Count && joined.Count == placedA.Count, "Every chunk is joined to the start by road");

        bool atConnectors = roadsA.All(r =>
            IsConnector(placedA[r.From], r.X[0], r.Z[0], connectors[placedA[r.From].Footprint])
            && IsConnector(placedA[r.To], r.X[r.X.Count - 1], r.Z[r.Z.Count - 1], connectors[placedA[r.To].Footprint]));
        check(atConnectors, "Roads start and end at path connectors");

        bool clearOfChunks = true; int roadCells = 0, walkableRoad = 0;
        for (int c = 0; c < first.Cols * first.Rows; c++)
        {
            if (!first.Road[c]) continue;
            roadCells++;
            if (first.Walkable[c]) walkableRoad++;
            float x = first.MinX + (c % first.Cols + 0.5f) * first.CellSize, z = first.MinZ + (c / first.Cols + 0.5f) * first.CellSize;
            foreach (var p in placedA) if (RoadBuilder.InsideFootprint(x, z, p, footprints[p.Footprint], 0f)) clearOfChunks = false;
        }
        check(roadCells > 0 && clearOfChunks, "Roads never run through a chunk");
        check(roadCells > 0 && walkableRoad >= roadCells * 0.95f, "Road cells are walkable (cliffs on the route become ramps)");
    }

    static bool IsConnector(ChunkPlacement p, float x, float z, IReadOnlyList<ChunkConnector> list) =>
        list.Any(c => Mathf.Abs(p.X + c.X - x) < 0.01f && Mathf.Abs(p.Z + c.Z - z) < 0.01f);
}
