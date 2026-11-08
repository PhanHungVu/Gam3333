using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Spawn director: waves spend a resource on affordable enemies picked by chance, refill after
// Timer Reset, and only one wave runs at a time.
public static class SpawnDirectorChecks
{
    public static void Run(Action<bool, string> check)
    {
        // The wave clock and the pick, as plain logic.
        var costs = new[] { 5, 10 };
        var even = new[] { 1f, 1f };
        check(WaveCycle.Pick(costs, even, 5, 0.99f) == 0 && WaveCycle.Pick(costs, even, 5, 0f) == 0,
            "A wave only picks enemies its resource can still afford");
        check(WaveCycle.Pick(costs, even, 4, 0.5f) == -1, "A wave with too little resource picks nothing");
        var same = new[] { 5, 5 };
        var ninety = new[] { 9f, 1f };
        check(WaveCycle.Pick(same, ninety, 50, 0.85f) == 0 && WaveCycle.Pick(same, ninety, 50, 0.95f) == 1,
            "Enemy chances follow their weights");

        var cycle = new WaveCycle(0.5f, 30f, 15);
        bool first = cycle.Tick(0f);
        cycle.Spent(5);
        bool early = cycle.Tick(0.4f), onTime = cycle.Tick(0.1f);
        check(first && cycle.Resource == 10 && !early && onTime, "A wave spawns one enemy per Spawn Interval and spends its cost");
        cycle.OutOfResource();
        bool waiting = cycle.Tick(29.9f), refilled = cycle.Tick(0.2f);
        check(!waiting && refilled && cycle.Resource == 15 && !cycle.Refilling,
            "A spent wave waits Timer Reset, then refills and spawns again");

        // The director, with a stand-in enemy and two spawn points.
        GameObject template = null, directorObject = null, pointA = null, pointB = null;
        GameObject roamDirectorObject = null, standIn = null, roamObject = null, gridObject = null;
        WaveData data = null;
        try
        {
            template = new GameObject("Spawn Check Enemy");
            template.transform.position = new Vector3(91000f, 0f, 0f);
            template.AddComponent<EnemySpawnCost>(); // default cost 5
            data = ScriptableObject.CreateInstance<WaveData>();
            data.name = "Spawn Check Wave";
            data.spawnInterval = 0.5f; data.timerReset = 10f; data.resourceTotal = 10;
            data.enemies = new[] { new WaveEnemy { prefab = template, weight = 1f } };
            directorObject = new GameObject("Spawn Check Director");
            var director = directorObject.AddComponent<SpawnDirector>();
            pointA = new GameObject("Spawn Check Point A"); pointA.transform.position = new Vector3(91010f, 0f, 0f);
            pointB = new GameObject("Spawn Check Point B"); pointB.transform.position = new Vector3(91020f, 0f, 0f);
            var points = new[] { pointA.AddComponent<SpawnPoint>(), pointB.AddComponent<SpawnPoint>() };
            var owner = directorObject;

            check(director.RunWave(data, points, owner) && director.IsRunning(owner), "An outside script can start a wave on its spawn points");
            check(!director.RunWave(data, points, pointA), "Only one wave runs at a time");
            director.Tick(0f);
            check(director.TotalSpawned == 0, "Zero elapsed time cannot spawn an enemy while gameplay is paused");
            director.Tick(0.01f); director.Tick(0.5f);
            check(director.TotalSpawned == 2 && director.ResourceLeft == 0, "The wave spawns until its resource is spent");
            director.Tick(0.5f); director.Tick(0.5f);
            check(director.TotalSpawned == 2, "Nothing spawns while the wave waits to refill");
            int clonesAtPoints = 0;
            foreach (var clone in Object.FindObjectsByType<EnemySpawnCost>())
                if (clone.gameObject != template && (clone.transform.position == pointA.transform.position || clone.transform.position == pointB.transform.position))
                    clonesAtPoints++;
            check(clonesAtPoints == 2, "Enemies appear at the wave's spawn points");
            director.StopWave(owner);
            director.Tick(20f); director.Tick(0.5f);
            check(!director.IsRunning(owner) && director.TotalSpawned == 2, "Stopping a wave stops its spawning");

            // Roaming: a stand-in player on its own open NavGrid, with a roaming spawner and director.
            var site = new Vector3(92000f, 0f, 0f);
            standIn = new GameObject("Spawn Check Player"); standIn.transform.position = site;
            gridObject = new GameObject("Spawn Check Grid");
            var grid = gridObject.AddComponent<NavGrid>();
            var gridSettings = new SerializedObject(grid);
            gridSettings.FindProperty("origin").vector3Value = site;
            gridSettings.FindProperty("width").floatValue = 200f;
            gridSettings.FindProperty("depth").floatValue = 200f;
            gridSettings.FindProperty("drawOpenCells").boolValue = false;
            gridSettings.FindProperty("drawBlockedCells").boolValue = false;
            gridSettings.ApplyModifiedPropertiesWithoutUndo();
            grid.Bake();
            roamObject = new GameObject("Spawn Check Roaming");
            var roamer = roamObject.AddComponent<RoamingSpawner>();
            var roamSettings = new SerializedObject(roamer);
            roamSettings.FindProperty("radius").floatValue = 20f;
            roamSettings.FindProperty("target").objectReferenceValue = standIn.transform;
            roamSettings.FindProperty("grid").objectReferenceValue = grid;
            roamSettings.ApplyModifiedPropertiesWithoutUndo();
            roamDirectorObject = new GameObject("Spawn Check Roaming Director");
            var roamDirector = roamDirectorObject.AddComponent<SpawnDirector>();
            var directorSettings = new SerializedObject(roamDirector);
            directorSettings.FindProperty("roamingWave").objectReferenceValue = data;
            directorSettings.FindProperty("roamingSpawner").objectReferenceValue = roamer;
            directorSettings.ApplyModifiedPropertiesWithoutUndo();

            data.resourceTotal = 50; // room for several roaming spawns
            roamDirector.Tick(0f);
            check(roamDirector.TotalSpawned == 0, "Paused roaming does not create an initial enemy");
            roamDirector.Tick(0.01f);
            GameObject roamed = null;
            foreach (var clone in Object.FindObjectsByType<EnemySpawnCost>())
                if (clone.gameObject != template && clone.transform.position.x > site.x - 100f) roamed = clone.gameObject;
            float fromPlayer = roamed != null ? Vector3.Distance(roamed.transform.position, site) : 0f;
            check(roamDirector.TotalSpawned == 1 && Mathf.Abs(fromPlayer - 20f) < 0.01f,
                "Roaming spawns enemies on a circle Radius away from the player");
            check(roamDirector.RunWave(data, new[] { points[0] }, owner), "An objective wave can start while roaming");
            roamDirector.Tick(0.01f);
            check(roamDirector.RoamingPaused && roamDirector.IsRunning(owner) && roamDirector.TotalSpawned == 2,
                "An objective wave pauses roaming and spawns at its own points");
            roamDirector.StopWave(owner);
            RoamingBlockers.Register(standIn, position => true);
            roamDirector.Tick(0.5f);
            check(roamDirector.RoamingPaused && roamDirector.TotalSpawned == 2, "Roaming pauses while the player is inside an objective chunk");
            RoamingBlockers.Unregister(standIn);
            roamDirector.Tick(0.5f);
            check(!roamDirector.RoamingPaused && roamDirector.TotalSpawned == 3, "Roaming resumes where it left off when the player leaves");
            standIn.transform.position = site + new Vector3(150f, 0f, 0f);
            roamDirector.Tick(0.5f); roamDirector.Tick(0.5f);
            check(roamDirector.TotalSpawned == 3, "Roaming never spawns off the NavGrid");
        }
        finally
        {
            foreach (var clone in Object.FindObjectsByType<EnemySpawnCost>())
                if (clone.gameObject.name.StartsWith("Spawn Check Enemy")) Object.DestroyImmediate(clone.gameObject);
            if (directorObject != null) Object.DestroyImmediate(directorObject);
            if (pointA != null) Object.DestroyImmediate(pointA);
            if (pointB != null) Object.DestroyImmediate(pointB);
            if (data != null) Object.DestroyImmediate(data);
            if (standIn != null) RoamingBlockers.Unregister(standIn);
            foreach (var go in new[] { roamDirectorObject, standIn, roamObject, gridObject })
                if (go != null) Object.DestroyImmediate(go);
        }
    }
}
