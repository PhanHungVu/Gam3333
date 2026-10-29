using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// KOTH outposts: seeded, centre piece in the middle, pieces on the right kind of ground, lanes clear.
public static class OutpostChecks
{
    public static void Run(Action<bool, string> check)
    {
        var terrain = new TerrainSettings();
        var placement = new ChunkPlacementSettings { levelHeight = terrain.levelHeight, terraceLevels = terrain.terraceLevels };
        var interior = new ChunkInteriorSettings();
        var footprints = new List<ChunkFootprint> { new ChunkFootprint(24f, 24f) };
        for (int i = 0; i < 2; i++) footprints.Add(new ChunkFootprint(100f, 100f, true, interior));
        var map = TerrainGenerator.Generate(13, terrain);
        var placed = ChunkPlacer.Place(map, 13, footprints, placement);
        ChunkPlacer.Flatten(map, placed, footprints, placement);
        var roads = RoadBuilder.Build(map, placed, footprints, null, new RoadSettings());
        ChunkInterior.Build(map, 13, placed, footprints, roads, 35f);

        int index = placed.FindIndex(p => footprints[p.Footprint].Circle);
        var spot = placed[index]; var size = footprints[spot.Footprint];
        var entrances = ChunkInterior.Entrances(index, spot, roads);
        var settings = new OutpostSettings
        {
            centre = new OutpostSlotSettings { weights = new[] { 1f }, min = 1, max = 1 },
            flat = new OutpostSlotSettings { weights = new[] { 1f, 1f }, min = 3, max = 6 },
            wallEdge = new OutpostSlotSettings { weights = new[] { 1f }, min = 4, max = 8 },
            high = new OutpostSlotSettings { weights = new[] { 1f }, min = 1, max = 3 },
        };
        var first = OutpostPlanner.Plan(map, 21, spot, size, entrances, interior.laneWidth, interior.stepHeight, settings);
        var again = OutpostPlanner.Plan(map, 21, spot, size, entrances, interior.laneWidth, interior.stepHeight, settings);
        check(first.Count > 1 && first.Count == again.Count && first.Zip(again, (a, b) => a.X == b.X && a.Z == b.Z && a.Slot == b.Slot).All(x => x),
            "The same seed plans the same outpost");
        check(first[0].Slot == OutpostSlot.Centre && Mathf.Approximately(first[0].X, spot.X) && Mathf.Approximately(first[0].Z, spot.Z),
            "The centre piece sits in the middle of the hold area");

        float clearHalf = interior.laneWidth * 0.5f + settings.laneClearance;
        bool lanesClear = true, inside = true, rightGround = true, spaced = true;
        foreach (var item in first.Where(i => i.Slot != OutpostSlot.Centre))
        {
            float dx = item.X - spot.X, dz = item.Z - spot.Z;
            if (OutpostPlanner.InLane(dx, dz, entrances, clearHalf)) lanesClear = false;
            if (dx * dx + dz * dz > size.Radius * size.Radius) inside = false;
            int col = Mathf.FloorToInt((item.X - map.MinX) / map.CellSize), row = Mathf.FloorToInt((item.Z - map.MinZ) / map.CellSize);
            if (item.Slot == OutpostSlot.WallEdge && !OutpostPlanner.NextToWall(map, col, row)) rightGround = false;
            if (item.Slot == OutpostSlot.High && OutpostPlanner.Rise(map, col, row, spot.Height) < interior.stepHeight - 0.25f) rightGround = false;
            if (!map.IsWalkable(col, row)) rightGround = false;
        }
        for (int i = 0; i < first.Count; i++)
            for (int j = i + 1; j < first.Count; j++)
                if ((first[i].X - first[j].X) * (first[i].X - first[j].X) + (first[i].Z - first[j].Z) * (first[i].Z - first[j].Z)
                    < settings.spacing * settings.spacing - 0.001f) spaced = false;
        check(lanesClear, "Outpost pieces keep the entry lanes clear");
        check(inside && rightGround, "Pieces sit inside the chunk on their kind of ground (wall edges, raised blocks, open flat)");
        check(spaced, "Outpost pieces keep their spacing");
    }
}
