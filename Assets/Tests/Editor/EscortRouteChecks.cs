using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Escort generation: a chain route start -> checkpoints -> end, the end placed far away, one continuous path.
public static class EscortRouteChecks
{
    public static void Run(Action<bool, string> check)
    {
        var terrain = new TerrainSettings();
        var placement = new ChunkPlacementSettings { levelHeight = terrain.levelHeight, terraceLevels = terrain.terraceLevels, lastIsEnd = true };
        var footprints = new List<ChunkFootprint> { new ChunkFootprint(24f, 24f) };
        for (int i = 0; i < 3; i++) footprints.Add(new ChunkFootprint(40f, 40f, true));
        footprints.Add(new ChunkFootprint(24f, 24f));
        var map = TerrainGenerator.Generate(17, terrain);
        var placed = ChunkPlacer.Place(map, 17, footprints, placement);
        ChunkPlacer.Flatten(map, placed, footprints, placement);
        var roads = RoadBuilder.Build(map, placed, footprints, null, new RoadSettings { chain = true, lastIsEnd = true });

        int end = placed.FindIndex(p => p.Footprint == footprints.Count - 1);
        bool sequential = roads.Count == placed.Count - 1 && roads[0].From == 0 && roads[roads.Count - 1].To == end;
        for (int i = 1; i < roads.Count; i++) if (roads[i].From != roads[i - 1].To) sequential = false;
        check(placed.Count == footprints.Count && sequential && roads.Select(r => r.To).Distinct().Count() == roads.Count,
            "A chain route visits every checkpoint once, in order, from the start to the end chunk");

        float endDistance = Vector2.Distance(new Vector2(placed[0].X, placed[0].Z), new Vector2(placed[end].X, placed[end].Z));
        check(endDistance > 150f, "The end chunk is placed far from the start");

        var route = EscortRoute.Build(placed, roads);
        bool joined = route.Complete && Mathf.Approximately(route.X[0], placed[0].X) && Mathf.Approximately(route.Z[0], placed[0].Z)
            && Mathf.Approximately(route.X[route.X.Count - 1], placed[end].X) && Mathf.Approximately(route.Z[route.Z.Count - 1], placed[end].Z);
        bool ordered = route.CheckpointDistances.Count == placed.Count - 1;
        for (int i = 1; i < route.CheckpointDistances.Count; i++) if (route.CheckpointDistances[i] <= route.CheckpointDistances[i - 1]) ordered = false;
        check(joined && ordered && Mathf.Approximately(route.CheckpointDistances[route.CheckpointDistances.Count - 1], route.Length),
            "The escort route runs from the start's centre through each checkpoint to the end's centre");

        route.Sample(route.Length * 0.5f, out float x, out _, out float z, out float dx, out float dz);
        check(Mathf.Abs(dx * dx + dz * dz - 1f) < 0.001f && route.LastCheckpoint(0f) == -1
            && route.LastCheckpoint(route.CheckpointDistances[0] + 1f) == 0 && route.LastCheckpoint(route.Length) == route.CheckpointDistances.Count - 1,
            "Points along the route have a travel direction, and the last checkpoint passed is known (for respawning)");
    }
}
