using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// Elimination: clear the camp by killing its enemies' spawn cost; leaving fails and resets after the countdown.
public static class EliminationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var rules = new EliminationRules(500, 5f, 2f);
        bool ignoredBefore = !rules.Kill(5) && rules.Remaining == 500;
        rules.Tick(0.1f, true);
        rules.Kill(5); rules.Kill(10);
        check(ignoredBefore && rules.State == HoldZoneState.Holding && rules.Remaining == 485 && Mathf.Approximately(rules.Cleared, 0.03f),
            "Entering starts the fight, and only kills during it take their cost off the total");
        rules.Tick(0.1f, false); rules.Kill(5); rules.Tick(2f, false);
        bool countdown = rules.State == HoldZoneState.Warning && rules.Remaining == 480;
        rules.Tick(0.1f, true);
        check(countdown && rules.State == HoldZoneState.Holding, "Leaving starts the countdown, kills still count, and returning resumes the fight");
        rules.Tick(0.1f, false); rules.Tick(5f, false);
        bool failed = rules.State == HoldZoneState.Failed;
        rules.Tick(2f, false);
        check(failed && rules.State == HoldZoneState.Waiting && rules.Remaining == 500, "Running out the countdown fails the camp and resets it to the full total");
        rules.Tick(0.1f, true);
        for (int i = 0; i < 100; i++) rules.Kill(5);
        check(rules.State == HoldZoneState.Complete && rules.Cleared == 1f && !rules.Kill(5), "Killing the full total clears the camp, and it stays cleared");

        var roster = new WaveRoster<object>();
        roster.Add(new object(), 5); roster.Add(new object(), 10);
        check(roster.TotalCost == 15, "A wave knows how much spawn cost it has out, so a camp never fields more than is left");

        var terrain = new TerrainSettings();
        var placement = new ChunkPlacementSettings { levelHeight = terrain.levelHeight, terraceLevels = terrain.terraceLevels };
        var interior = new ChunkInteriorSettings { holdArea = 0.5f, holdCover = 0f };
        var footprints = new List<ChunkFootprint> { new ChunkFootprint(24f, 24f), new ChunkFootprint(120f, 120f, true, interior) };
        var map = TerrainGenerator.Generate(19, terrain);
        var placed = ChunkPlacer.Place(map, 19, footprints, placement);
        ChunkPlacer.Flatten(map, placed, footprints, placement);
        var roads = RoadBuilder.Build(map, placed, footprints, null, new RoadSettings());
        ChunkInterior.Build(map, 19, placed, footprints, roads, 35f);
        int index = placed.FindIndex(p => p.Footprint == 1);
        var camp = placed[index];
        var settings = new OutpostSettings
        {
            centre = new OutpostSlotSettings(), flat = new OutpostSlotSettings(), wallEdge = new OutpostSlotSettings(), high = new OutpostSlotSettings(),
            camp = new OutpostSlotSettings { weights = new[] { 1f }, min = 3, max = 5 },
        };
        var items = OutpostPlanner.Plan(map, 3, camp, footprints[1], ChunkInterior.Entrances(index, camp, roads),
            interior.laneWidth, interior.stepHeight, settings, interior.holdArea);
        float holdRadius = footprints[1].Radius * interior.holdArea;
        check(items.Count >= 3 && items.Count <= 5 && items.All(i => i.Slot == OutpostSlot.Camp
            && (i.X - camp.X) * (i.X - camp.X) + (i.Z - camp.Z) * (i.Z - camp.Z) <= holdRadius * holdRadius + 0.01f),
            "Camp pieces (the tents) go only on the flat camp in the middle");
    }
}
