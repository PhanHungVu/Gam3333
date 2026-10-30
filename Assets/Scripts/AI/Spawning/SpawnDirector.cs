using System.Collections.Generic;
using UnityEngine;

// The single spawn manager for a scene. Runs one wave at a time on the spawn points it is given:
// every Spawn Interval it picks an affordable enemy from the wave's list by its chance, spawns it
// at one of the wave's points and spends its cost; when nothing is affordable it waits Timer Reset,
// refills and carries on. Other scripts start and stop waves (e.g. KOTH_Zone runs the KOTH
// wave on its chunk's spawn points). Enemies already spawned are never removed. Put one in each scene.
// Roaming: with a Roaming Wave and a Roaming Spawner it runs that wave on its own whenever no other
// wave is running and the player isn't inside an area that pauses roaming (objective chunks). A paused
// roaming wave keeps its place in its cycle and carries on when it resumes.
[DisallowMultipleComponent]
public sealed class SpawnDirector : MonoBehaviour
{
    [Header("Roaming")]
    [Tooltip("Wave run around the player whenever no other wave is running. Left empty, there is no roaming.")]
    [SerializeField] WaveData roamingWave;
    [Tooltip("Left empty, the scene's Roaming Spawner is used.")]
    [SerializeField] RoamingSpawner roamingSpawner;

    [Header("Debug (read-only)")]
    [SerializeField] string currentWave;
    [SerializeField] bool roamingPaused;
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
    ActiveWave objective;  // started by another script; pauses roaming while it runs
    ActiveWave roaming;

    // The scene's director (found on first use if no one has asked yet).
    public static SpawnDirector Instance
    {
        get
        {
            if (instance == null) instance = FindAnyObjectByType<SpawnDirector>();
            return instance;
        }
    }

    ActiveWave Current => objective ?? (RoamingActive() ? roaming : null);
    public WaveData CurrentWave => Current != null ? Current.data : null;
    public bool IsRunning(Object owner) => objective != null && objective.owner == owner;
    public int ResourceLeft => Current != null ? Current.cycle.Resource : 0;
    public bool RoamingPaused => roamingPaused;
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
        if (objective != null && objective.owner != owner) return false;
        objective = Build(data, points, owner);
        return true;
    }

    // Stops the owner's wave. Enemies it already spawned stay until killed.
    public void StopWave(Object owner)
    {
        if (objective != null && objective.owner == owner) objective = null;
    }

    ActiveWave Build(WaveData data, IReadOnlyList<SpawnPoint> points, Object owner)
    {
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

        return new ActiveWave
        {
            data = data, owner = owner, points = copy, costs = costs, weights = weights,
            cycle = new WaveCycle(data.spawnInterval, data.timerReset, data.resourceTotal),
        };
    }

    // Roaming runs only when no other wave is, and not while the player is in a pausing area.
    bool RoamingActive()
    {
        if (objective != null || roamingWave == null) return false;
        if (roamingSpawner == null) roamingSpawner = FindAnyObjectByType<RoamingSpawner>();
        if (roamingSpawner == null) return false;
        var player = roamingSpawner.Target;
        if (player == null || RoamingBlockers.Contains(player.position)) return false;
        if (roaming == null || roaming.data != roamingWave) roaming = Build(roamingWave, new SpawnPoint[] { roamingSpawner }, this);
        return true;
    }

    void Update() => Tick(Time.deltaTime);

    public void Tick(float deltaTime)
    {
        bool roamingNow = RoamingActive();
        var active = objective ?? (roamingNow ? roaming : null);
        if (active != null && active.cycle.Tick(deltaTime)) TrySpawn(active);
        roamingPaused = roamingWave != null && !roamingNow;
        currentWave = active != null ? active.data.name : "";
        resourceLeft = active != null ? active.cycle.Resource : 0;
        resetRemaining = active != null && active.cycle.Refilling ? active.cycle.ResetRemaining : 0f;
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
