using System.Collections.Generic;
using UnityEngine;

// The single spawn manager for a scene. Runs one wave at a time on the spawn points it is given:
// every Spawn Interval it picks an affordable enemy from the wave's list by its chance, spawns it
// at one of the wave's points and spends its cost; when nothing is affordable it waits Timer Reset,
// refills and carries on. Other scripts start and stop waves (e.g. Survival_Zone runs the Survival
// wave on its chunk's spawn points). Enemies already spawned are never removed. Put one in each scene.
[DisallowMultipleComponent]
public sealed class SpawnDirector : MonoBehaviour
{
    [Header("Debug (read-only)")]
    [SerializeField] string currentWave;
    [SerializeField] int resourceLeft;
    [SerializeField] float resetRemaining;
    [SerializeField] int totalSpawned;

    sealed class ActiveWave
    {
        public WaveData data;
        public WaveCycle cycle;
        public SpawnPoint[] points;
        public Object owner;
        public int[] costs;
        public float[] weights;
    }

    static SpawnDirector instance;
    ActiveWave wave;

    // The scene's director (found on first use if no one has asked yet).
    public static SpawnDirector Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<SpawnDirector>();
            return instance;
        }
    }

    public WaveData CurrentWave => wave != null ? wave.data : null;
    public bool IsRunning(Object owner) => wave != null && wave.owner == owner;
    public int ResourceLeft => wave != null ? wave.cycle.Resource : 0;
    public int TotalSpawned => totalSpawned;

    void OnEnable() { if (instance == null) instance = this; }
    void OnDisable() { if (instance == this) instance = null; }

    // Starts a wave on these spawn points. Only one wave runs at a time: returns false if another
    // owner's wave is already running. Calling again with the same owner restarts its wave.
    public bool RunWave(WaveData data, IReadOnlyList<SpawnPoint> points, Object owner)
    {
        if (data == null || points == null || points.Count == 0)
        {
            Debug.LogWarning("SpawnDirector: a wave needs Wave Data and at least one spawn point.", this);
            return false;
        }
        if (wave != null && wave.owner != owner) return false;

        var entries = data.enemies ?? new WaveEnemy[0];
        var costs = new int[entries.Length];
        var weights = new float[entries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            costs[i] = entries[i] != null ? EnemySpawnCost.Of(entries[i].prefab) : 0;
            weights[i] = entries[i] != null ? entries[i].weight : 0f;
            if (entries[i] != null && entries[i].prefab != null && costs[i] == 0)
                Debug.LogWarning($"SpawnDirector: {entries[i].prefab.name} has no EnemySpawnCost, so wave {data.name} never spawns it.", data);
        }
        var copy = new SpawnPoint[points.Count];
        for (int i = 0; i < points.Count; i++) copy[i] = points[i];

        wave = new ActiveWave
        {
            data = data, owner = owner, points = copy, costs = costs, weights = weights,
            cycle = new WaveCycle(data.spawnInterval, data.timerReset, data.resourceTotal),
        };
        return true;
    }

    // Stops the owner's wave. Enemies it already spawned stay until killed.
    public void StopWave(Object owner)
    {
        if (wave != null && wave.owner == owner) wave = null;
    }

    void Update() => Tick(Time.deltaTime);

    public void Tick(float deltaTime)
    {
        if (wave != null && wave.cycle.Tick(deltaTime)) TrySpawn(wave);
        currentWave = wave != null ? wave.data.name : "";
        resourceLeft = wave != null ? wave.cycle.Resource : 0;
        resetRemaining = wave != null && wave.cycle.Refilling ? wave.cycle.ResetRemaining : 0f;
    }

    void TrySpawn(ActiveWave active)
    {
        int pick = WaveCycle.Pick(active.costs, active.weights, active.cycle.Resource, Random.value);
        if (pick < 0) { active.cycle.OutOfResource(); return; }
        if (!TryPickPose(active.points, out var position, out var rotation)) { active.cycle.Skipped(); return; }
        Instantiate(active.data.enemies[pick].prefab, position, rotation);
        active.cycle.Spent(active.costs[pick]);
        totalSpawned++;
    }

    // A random usable point from the wave's list (destroyed or blocked points are skipped).
    static bool TryPickPose(SpawnPoint[] points, out Vector3 position, out Quaternion rotation)
    {
        int start = Random.Range(0, points.Length);
        for (int step = 0; step < points.Length; step++)
        {
            var point = points[(start + step) % points.Length];
            if (point != null && point.isActiveAndEnabled && point.TryGetSpawnPose(out position, out rotation)) return true;
        }
        position = default; rotation = Quaternion.identity;
        return false;
    }
}
