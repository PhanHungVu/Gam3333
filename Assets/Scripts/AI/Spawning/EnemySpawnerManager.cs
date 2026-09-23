using UnityEngine;

// New for the horde - nothing in the fish project spawned fish over time, they
// were all placed once. This is Phase 7: the last piece nothing else in
// Scripts/AI/ depends on, or is depended on by - it only ever creates an
// EnemyAgent and hands it off; everything after that (chasing, pathing, boids)
// is already built and doesn't know or care that this script exists.
//
// Fixed cadence: a new wave starts every Wave Interval seconds, on a clock that
// doesn't care how long the previous wave took to finish spawning - not "wait
// until the wave is cleared". Within a wave, enemies trickle in one at a time,
// Spawn Stagger seconds apart, rather than all appearing on top of each other.
public sealed class EnemySpawnerManager : MonoBehaviour
{
    [Header("Enemy")]
    [Tooltip("Drag a prefab here - make one by dragging your tuned Enemy from the Hierarchy into the Project window.")]
    [SerializeField] GameObject enemyPrefab;

    [Header("Spawn points")]
    [Tooltip("Empty GameObjects placed by hand around the level. One is picked at random for each enemy.")]
    [SerializeField] Transform[] spawnPoints;

    [Header("Waves")]
    [Tooltip("Time between the START of each wave - fixed cadence, not tied to how long spawning or the fight takes.")]
    [SerializeField] float waveInterval = 30f;
    [SerializeField] int enemiesPerWave = 10;
    [Tooltip("Delay between each enemy within a wave, so 10 don't appear in the same instant.")]
    [SerializeField] float spawnStagger = 0.5f;
    [SerializeField] bool spawnFirstWaveImmediately = true;

    [Header("Debug (read-only)")]
    [SerializeField] int currentWave;
    [SerializeField] int spawnedThisWave;
    [SerializeField] int totalSpawned;

    float waveStartTime;
    float nextWaveTime;
    float nextSpawnTime;
    bool waveInProgress;

    void Start()
    {
        if (enemyPrefab == null)
            Debug.LogWarning("[EnemySpawnerManager] No Enemy Prefab assigned - nothing will spawn.", this);

        nextWaveTime = spawnFirstWaveImmediately ? Time.time : Time.time + waveInterval;
    }

    void Update()
    {
        if (!waveInProgress && Time.time >= nextWaveTime)
            BeginWave();

        if (waveInProgress && Time.time >= nextSpawnTime && spawnedThisWave < enemiesPerWave)
        {
            SpawnOne();
            spawnedThisWave++;
            nextSpawnTime = Time.time + spawnStagger;

            if (spawnedThisWave >= enemiesPerWave)
            {
                waveInProgress = false;
                // Fixed cadence from when THIS wave started, not from when it
                // finished spawning - so a slow stagger never compounds into
                // later and later waves.
                nextWaveTime = waveStartTime + waveInterval;
            }
        }
    }

    void BeginWave()
    {
        currentWave++;
        waveStartTime = Time.time;
        waveInProgress = true;
        spawnedThisWave = 0;
        nextSpawnTime = Time.time; // first enemy of the wave spawns immediately, no initial delay
    }

    bool warnedAllNull;

    void SpawnOne()
    {
        if (enemyPrefab == null) return;

        // No points assigned - the spawner's own position IS the spawn point.
        // Points are only needed if you want enemies coming from more than
        // one place; nothing requires filling that array in at all.
        Transform point = PickRandomAssignedPoint();
        if (point == null) point = transform;

        Instantiate(enemyPrefab, point.position, point.rotation);
        totalSpawned++;
    }

    Transform PickRandomAssignedPoint()
    {
        if (spawnPoints == null || spawnPoints.Length == 0) return null;

        int start = Random.Range(0, spawnPoints.Length);
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            Transform p = spawnPoints[(start + i) % spawnPoints.Length];
            if (p != null) return p;
        }
        return null;
    }

    void OnDrawGizmos()
    {
        if (spawnPoints == null) return;
        Gizmos.color = new Color(0.95f, 0.55f, 0.15f, 0.9f);
        foreach (var p in spawnPoints)
        {
            if (p == null) continue;
            Gizmos.DrawWireSphere(p.position, 1f);
            Gizmos.DrawLine(p.position, p.position + Vector3.up * 3f);
        }
    }
}