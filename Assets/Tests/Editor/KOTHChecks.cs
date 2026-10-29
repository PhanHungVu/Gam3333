using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// King of the Hill: hold inside the chunk for the timer, 5 s grace after leaving, reset on failure;
// enemies come from a ring just outside the chunk's edge.
public static class KOTHChecks
{
    public static void Run(Action<bool, string> check)
    {
        var rules = new KOTH_ZoneRules(10f, 5f, 2f);
        rules.Tick(0.1f, false);
        bool waits = rules.State == KOTH_ZoneState.Waiting;
        rules.Tick(0.1f, true); rules.Tick(4f, true);
        check(waits && rules.State == KOTH_ZoneState.Holding && Mathf.Approximately(rules.Remaining, 6f),
            "The KOTH timer starts when the player enters and counts while they hold");
        rules.Tick(3f, false); rules.Tick(3f, false);
        bool paused = rules.State == KOTH_ZoneState.Warning && Mathf.Approximately(rules.Remaining, 6f);
        rules.Tick(0.1f, true);
        check(paused && rules.State == KOTH_ZoneState.Holding, "Leaving pauses the timer for the grace period, and returning resumes it");
        rules.Tick(0.1f, false); rules.Tick(5f, false);
        bool failed = rules.State == KOTH_ZoneState.Failed;
        rules.Tick(2f, false);
        check(failed && rules.State == KOTH_ZoneState.Waiting && Mathf.Approximately(rules.Remaining, 10f),
            "Staying out past the grace period fails the hold and resets it to the start");
        rules.Tick(0.1f, true); rules.Tick(10f, true); rules.Tick(1f, false);
        check(rules.State == KOTH_ZoneState.Complete, "Holding for the full time completes KOTH, and it stays complete");

        var site = new Vector3(94000f, 0f, 0f);
        GameObject gridObject = null;
        try
        {
            gridObject = new GameObject("KOTH Check Grid");
            var grid = gridObject.AddComponent<NavGrid>();
            var settings = new SerializedObject(grid);
            settings.FindProperty("origin").vector3Value = site;
            settings.FindProperty("width").floatValue = 120f;
            settings.FindProperty("depth").floatValue = 120f;
            settings.FindProperty("drawOpenCells").boolValue = false;
            settings.FindProperty("drawBlockedCells").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            grid.Bake();
            bool onRing = true, facing = true;
            for (int i = 0; i < 20; i++)
            {
                if (!SpawnRing.TryPick(site, 50f, 12, grid, out var position, out var rotation)) { onRing = false; break; }
                if (Mathf.Abs(Vector3.Distance(position, site) - 50f) > 0.01f) onRing = false;
                if (Vector3.Angle(rotation * Vector3.forward, site - position) > 1f) facing = false;
            }
            check(onRing && facing, "KOTH enemies spawn on a ring outside the chunk, facing its centre");
            check(!SpawnRing.TryPick(site, 200f, 12, grid, out _, out _), "The spawn ring never places enemies off the NavGrid");
        }
        finally { if (gridObject != null) Object.DestroyImmediate(gridObject); }
    }
}
