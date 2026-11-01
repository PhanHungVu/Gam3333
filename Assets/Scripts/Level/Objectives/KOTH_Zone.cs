using System.Collections.Generic;
using UnityEngine;

// King of the Hill: hold inside the chunk for the timer (HoldZone). When the hold begins the scene's
// SpawnDirector runs the wave on this chunk's spawn points (pausing roaming meanwhile) and stops it on
// failure or completion; enemies already spawned stay until killed. With no spawn points in the chunk,
// a Chunk Ring Spawner is made so enemies come from just outside the edge. Counts as a mission objective.
[DisallowMultipleComponent]
public sealed class KOTH_Zone : HoldZone, IMissionObjective
{
    [Tooltip("Wave the scene's Spawn Director runs while the hold is under way (e.g. WaveData_KOTH).")]
    [SerializeField] WaveData wave;
    [Tooltip("Where this objective's enemies appear. Left empty: any Spawn Points inside this chunk, or if there are none, a ring just outside its edge.")]
    [SerializeField] SpawnPoint[] spawnPoints;
    [Tooltip("With no spawn points, enemies appear on a ring this many metres outside the chunk's edge.")]
    [SerializeField, Min(0f)] float spawnDistance = 10f;

    readonly List<SpawnPoint> points = new List<SpawnPoint>();

    protected override void Awake()
    {
        base.Awake();
        if (spawnPoints != null)
            foreach (var point in spawnPoints)
                if (point != null) points.Add(point);
        if (points.Count == 0 && Chunk != null)
            foreach (var point in Chunk.GetComponentsInChildren<SpawnPoint>(true))
                if (!(point is RoamingSpawner)) points.Add(point);
        if (points.Count == 0 && Chunk != null) points.Add(MakeRing());
        if (points.Count == 0) Debug.LogWarning("KOTH_Zone has no chunk to spawn enemies around.", this);
        if (wave == null) Debug.LogWarning("KOTH_Zone has no Wave assigned, so no enemies will come.", this);
    }

    void OnEnable() => MissionObjectives.Register(this);

    void OnDisable()
    {
        MissionObjectives.Unregister(this);
        if (SpawnDirector.Instance != null) SpawnDirector.Instance.StopWave(this);
    }

    // A ring spawner at the chunk's centre, spawnDistance outside its edge.
    SpawnPoint MakeRing()
    {
        var ring = new GameObject("Spawn Ring").AddComponent<ChunkRingSpawner>();
        ring.transform.SetParent(Chunk.transform, false);
        Vector2 size = Chunk.SizeMetres;
        float edge = Chunk.IsCircle ? Chunk.RadiusMetres : 0.5f * Mathf.Sqrt(size.x * size.x + size.y * size.y);
        ring.Configure(edge + spawnDistance);
        return ring;
    }

    protected override void OnHoldStateChanged(HoldZoneState previous, HoldZoneState current)
    {
        var director = SpawnDirector.Instance;
        if (previous == HoldZoneState.Waiting && current == HoldZoneState.Holding)
        {
            if (director == null) Debug.LogWarning("KOTH_Zone: no Spawn Director in the scene, so no wave runs.", this);
            else if (!director.RunWave(wave, points, this)) Debug.LogWarning("KOTH_Zone: another wave is already running.", this);
        }
        if ((current == HoldZoneState.Failed || current == HoldZoneState.Complete) && director != null)
            director.StopWave(this);
    }
}
