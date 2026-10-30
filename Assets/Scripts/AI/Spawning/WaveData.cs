using UnityEngine;

public enum WaveType { Roaming, KOTH, POI }

// One enemy a wave can pick, with its chance relative to the others in the list.
[System.Serializable]
public sealed class WaveEnemy
{
    [Tooltip("Enemy prefab. Its EnemySpawnCost decides what it costs from the wave's resource.")]
    public GameObject prefab;
    [Tooltip("Chance of being picked, relative to the other enemies in this wave (like the part drop list).")]
    [Min(0f)] public float weight = 1f;
}

// A wave's settings, the same way PartData holds a part's: one asset per wave type.
// Every Spawn Interval the wave picks one enemy it can still afford and spends its cost from the
// resource. When nothing is affordable the resource is spent: Timer Reset counts down, the resource
// refills, and spawning carries on until whoever started the wave stops it.
[CreateAssetMenu(fileName = "WaveData", menuName = "Mech/Wave")]
public sealed class WaveData : ScriptableObject
{
    public WaveType type;
    [Tooltip("Seconds between one enemy spawning and the next.")]
    [Min(0.01f)] public float spawnInterval = 0.5f;
    [Tooltip("Seconds to wait after the resource runs out before it refills.")]
    [Min(0f)] public float timerReset = 120f;
    [Tooltip("Resource at full; each enemy spawned spends its Spawn Cost from it.")]
    [Min(1)] public int resourceTotal = 50;
    [Tooltip("Optional scene-wide active enemy limit. Zero retains the legacy unlimited behavior. At the limit, spawning waits without spending resource.")]
    [Min(0)] public int maxConcurrentEnemies;
    [Tooltip("Enemies this wave can spawn, each with its chance. Enemies it can't afford are skipped.")]
    public WaveEnemy[] enemies = new WaveEnemy[0];
}
