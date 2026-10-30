using System.Collections.Generic;
using UnityEngine;

// Zoo floor switches enable the two group objects. One existing SpawnDirector
// handles the filtered roster, spawn timing, resource and population limit.
[RequireComponent(typeof(SpawnDirector)), DisallowMultipleComponent, DefaultExecutionOrder(-50)]
public sealed class ZooEnemyWaves : MonoBehaviour
{
    [SerializeField] WaveData roster;
    [SerializeField] GameObject smallEnemies, eliteEnemies;
    [SerializeField] SpawnPoint smallSpawnPoint, eliteSpawnPoint;
    [Header("Zoo showcase pacing")]
    [SerializeField, Min(0.1f)] float spawnInterval = 2;
    [SerializeField, Min(1)] int maximumAlive = 8;
    [SerializeField, Min(1)] int resourcePerCycle = 50;
    [SerializeField, Min(0)] float resetDelay = 15;
    SpawnDirector director;
    WaveData activeWave;
    int activeGroups = -1;

    void Awake() => director = GetComponent<SpawnDirector>();
    void OnEnable() => activeGroups = -1;
    void Update()
    {
        int groups = (smallEnemies != null && smallEnemies.activeInHierarchy ? 1 : 0)
            | (eliteEnemies != null && eliteEnemies.activeInHierarchy ? 2 : 0);
        if (groups == activeGroups) return;
        StopWave();
        if (groups == 0 || roster == null || director == null || !director.isActiveAndEnabled)
        { activeGroups = groups; return; }

        var entries = new List<WaveEnemy>(); var points = new List<SpawnPoint>();
        if ((groups & 1) != 0 && smallSpawnPoint != null) points.Add(smallSpawnPoint);
        if ((groups & 2) != 0 && eliteSpawnPoint != null) points.Add(eliteSpawnPoint);
        foreach (var entry in roster.enemies)
        {
            if (entry == null || entry.prefab == null) continue;
            var enemy = entry.prefab.GetComponent<EnemyGraphController>();
            if (enemy == null) continue;
            bool small = enemy.Role <= EnemyBehaviorRole.Crossbow;
            if ((groups & (small ? 1 : 2)) != 0) entries.Add(new WaveEnemy { prefab = entry.prefab, weight = entry.weight });
        }
        if (entries.Count == 0 || points.Count == 0) { activeGroups = groups; return; }
        activeWave = Instantiate(roster);
        activeWave.name = "Zoo Enemy Showcase";
        activeWave.enemies = entries.ToArray(); activeWave.spawnInterval = spawnInterval;
        activeWave.maxConcurrentEnemies = maximumAlive; activeWave.resourceTotal = resourcePerCycle; activeWave.timerReset = resetDelay;
        // Another objective's director ownership is respected. Retry when free.
        if (director.RunWave(activeWave, points, this)) activeGroups = groups;
        else StopWave();
    }
    void StopWave()
    {
        if (director != null) director.StopWave(this);
        if (activeWave != null) Destroy(activeWave);
        activeWave = null;
    }
    void OnDisable() { StopWave(); activeGroups = -1; }
}
