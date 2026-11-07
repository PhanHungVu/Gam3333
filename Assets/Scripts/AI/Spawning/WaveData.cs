using UnityEngine;

public enum WaveType { Roaming, KOTH, POI, Escort, Elimination }

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
    [Tooltip("If every enemy this wave spawned is gone (killed or removed) during its Timer Reset, the wait drops to this many seconds. 0 = off (always the full Timer Reset).")]
    [Min(0f)] public float clearedTimerReset;
    [Tooltip("Resource at full; each enemy spawned spends its Spawn Cost from it.")]
    [Min(1)] public int resourceTotal = 50;
    [Tooltip("Objective waves only: pause the roaming wave while this wave runs (KOTH: on). Off keeps roaming enemies coming too (Escort).")]
    public bool pausesRoaming = true;    [Tooltip("How many of this wave's own enemies may be alive at once; at the limit it waits without spending resource. 0 = no limit.")]
    [Min(0)] public int maxConcurrentEnemies;
    [Tooltip("Enemies of this wave further than this (metres) from where the wave's owner says to stay (e.g. the escort payload) are removed, and their cost refunded so fresh ones spawn nearer. 0 = off.")]
    [Min(0f)] public float leashDistance;
    [Tooltip("Enemies this wave can spawn, each with its chance. Enemies it can't afford are skipped.")]
    public WaveEnemy[] enemies = new WaveEnemy[0];
}
