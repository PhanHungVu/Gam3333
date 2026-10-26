using System;
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
            director.Tick(0f); director.Tick(0.5f);
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
        }
        finally
        {
            foreach (var clone in Object.FindObjectsByType<EnemySpawnCost>())
                if (clone.gameObject.name.StartsWith("Spawn Check Enemy")) Object.DestroyImmediate(clone.gameObject);
            if (directorObject != null) Object.DestroyImmediate(directorObject);
            if (pointA != null) Object.DestroyImmediate(pointA);
            if (pointB != null) Object.DestroyImmediate(pointB);
            if (data != null) Object.DestroyImmediate(data);
        }
    }
}
