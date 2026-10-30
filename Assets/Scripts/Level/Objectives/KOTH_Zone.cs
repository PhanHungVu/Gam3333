using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public enum KOTH_ZoneState { Waiting, Holding, Warning, Failed, Complete }

// King of the Hill rules as a plain class (no Unity types), so they port to the custom engine as-is.
// Waiting -> player enters -> Holding (timer counts). Leaving starts a grace period (Warning) with
// the timer paused; re-entering resumes it. If the grace runs out the mission Fails, shows that for a
// moment, then resets to Waiting with the timer back at zero. Once complete it stays complete.
public sealed class KOTH_ZoneRules
{
    public float HoldSeconds { get; }
    public float GraceSeconds { get; }
    public float FailedSeconds { get; }
    public KOTH_ZoneState State { get; private set; } = KOTH_ZoneState.Waiting;
    public float Elapsed { get; private set; }
    public float Remaining => Math.Max(0f, HoldSeconds - Elapsed);
    public float GraceRemaining { get; private set; }
    float failedAge;

    public KOTH_ZoneRules(float holdSeconds, float graceSeconds, float failedSeconds)
    {
        HoldSeconds = Math.Max(0.1f, holdSeconds);
        GraceSeconds = Math.Max(0f, graceSeconds);
        FailedSeconds = Math.Max(0f, failedSeconds);
    }

    // Advances one frame. Returns true when the state changed.
    public bool Tick(float deltaTime, bool playerInside)
    {
        deltaTime = Math.Max(0f, deltaTime);
        KOTH_ZoneState next = State;
        switch (State)
        {
            case KOTH_ZoneState.Waiting:
                if (playerInside) { Elapsed = 0f; next = KOTH_ZoneState.Holding; }
                break;
            case KOTH_ZoneState.Holding:
                if (!playerInside) { GraceRemaining = GraceSeconds; next = KOTH_ZoneState.Warning; break; }
                Elapsed = Math.Min(HoldSeconds, Elapsed + deltaTime);
                if (Elapsed >= HoldSeconds) next = KOTH_ZoneState.Complete;
                break;
            case KOTH_ZoneState.Warning:
                if (playerInside) { next = KOTH_ZoneState.Holding; break; }
                GraceRemaining = Math.Max(0f, GraceRemaining - deltaTime);
                if (GraceRemaining <= 0f) { failedAge = 0f; next = KOTH_ZoneState.Failed; }
                break;
            case KOTH_ZoneState.Failed:
                failedAge += deltaTime;
                if (failedAge >= FailedSeconds) { Elapsed = 0f; next = KOTH_ZoneState.Waiting; }
                break;
        }
        bool changed = next != State;
        State = next;
        return changed;
    }
}

// Measures whether the player is inside the chunk's footprint (box or circle, from ChunkDefinition)
// and feeds it to the rules. When the hold begins the scene's SpawnDirector runs the wave on this
// chunk's spawn points (pausing roaming meanwhile) and stops it on failure or completion; enemies
// already spawned stay until killed. With no spawn points in the chunk, a Chunk Ring Spawner is made so
// enemies come from just outside the edge. Put it on a child of the chunk. Visuals listen to State.
[DisallowMultipleComponent]
public sealed class KOTH_Zone : MonoBehaviour
{
    [Tooltip("Seconds to hold inside the chunk.")]
    [FormerlySerializedAs("surviveSeconds")]
    [SerializeField, Min(0.1f)] float holdSeconds = 60f;
    [Tooltip("Seconds the player has to get back inside after leaving, with the timer paused.")]
    [SerializeField, Min(0f)] float graceSeconds = 5f;
    [Tooltip("How long the failed state shows before the mission resets.")]
    [SerializeField, Min(0f)] float failedSeconds = 2f;
    [Tooltip("Wave the scene's Spawn Director runs while the hold is under way (e.g. WaveData_KOTH).")]
    [SerializeField] WaveData wave;
    [Tooltip("Where this objective's enemies appear. Left empty: any Spawn Points inside this chunk, or if there are none, a ring just outside its edge.")]
    [SerializeField] SpawnPoint[] spawnPoints;
    [Tooltip("With no spawn points, enemies appear on a ring this many metres outside the chunk's edge.")]
    [SerializeField, Min(0f)] float spawnDistance = 10f;

    [Header("Debug (read-only)")]
    [SerializeField] KOTH_ZoneState state;
    [SerializeField] float timeRemaining;
    [SerializeField] float graceRemaining;

    KOTH_ZoneRules rules;
    ChunkDefinition chunk;
    readonly List<SpawnPoint> points = new List<SpawnPoint>();
    Transform player;
    float nextPlayerSearch;

    public KOTH_ZoneState State => rules != null ? rules.State : KOTH_ZoneState.Waiting;
    public float TimeRemaining => rules != null ? rules.Remaining : holdSeconds;
    public float GraceRemaining => rules != null ? rules.GraceRemaining : graceSeconds;
    public bool IsComplete => State == KOTH_ZoneState.Complete;
    public ChunkDefinition Chunk => chunk;

    public event Action<KOTH_ZoneState> StateChanged;
    public event Action Completed;

    void Awake()
    {
        rules = new KOTH_ZoneRules(holdSeconds, graceSeconds, failedSeconds);
        chunk = GetComponentInParent<ChunkDefinition>();
        if (chunk == null) Debug.LogWarning("KOTH_Zone needs a ChunkDefinition on its chunk root.", this);
        if (spawnPoints != null)
            foreach (var point in spawnPoints)
                if (point != null) points.Add(point);
        if (points.Count == 0 && chunk != null)
            foreach (var point in chunk.GetComponentsInChildren<SpawnPoint>(true))
                if (!(point is RoamingSpawner)) points.Add(point);
        if (points.Count == 0 && chunk != null) points.Add(MakeRing());
        if (points.Count == 0) Debug.LogWarning("KOTH_Zone has no chunk to spawn enemies around.", this);
        if (wave == null) Debug.LogWarning("KOTH_Zone has no Wave assigned, so no enemies will come.", this);
    }

    // A ring spawner at the chunk's centre, spawnDistance outside its edge.
    SpawnPoint MakeRing()
    {
        var ring = new GameObject("Spawn Ring").AddComponent<ChunkRingSpawner>();
        ring.transform.SetParent(chunk.transform, false);
        Vector2 size = chunk.SizeMetres;
        float edge = chunk.IsCircle ? chunk.RadiusMetres : 0.5f * Mathf.Sqrt(size.x * size.x + size.y * size.y);
        ring.Configure(edge + spawnDistance);
        return ring;
    }

    void OnDisable()
    {
        if (SpawnDirector.Instance != null) SpawnDirector.Instance.StopWave(this);
    }

    void Update()
    {
        if (rules.State != KOTH_ZoneState.Complete)
        {
            FindPlayerIfNeeded();
            bool inside = player != null && player.gameObject.activeInHierarchy && InsideChunk(player.position);
            KOTH_ZoneState previous = rules.State;
            if (rules.Tick(Time.deltaTime, inside)) OnStateChanged(previous, rules.State);
        }
        state = rules.State;
        timeRemaining = rules.Remaining;
        graceRemaining = rules.GraceRemaining;
    }

    void OnStateChanged(KOTH_ZoneState previous, KOTH_ZoneState current)
    {
        var director = SpawnDirector.Instance;
        if (previous == KOTH_ZoneState.Waiting && current == KOTH_ZoneState.Holding)
        {
            if (director == null) Debug.LogWarning("KOTH_Zone: no Spawn Director in the scene, so no wave runs.", this);
            else if (!director.RunWave(wave, points, this)) Debug.LogWarning("KOTH_Zone: another wave is already running.", this);
        }
        if ((current == KOTH_ZoneState.Failed || current == KOTH_ZoneState.Complete) && director != null)
            director.StopWave(this);
        StateChanged?.Invoke(current);
        if (current == KOTH_ZoneState.Complete) Completed?.Invoke();
    }

    void FindPlayerIfNeeded()
    {
        if (player != null || Time.unscaledTime < nextPlayerSearch) return;
        nextPlayerSearch = Time.unscaledTime + 0.5f;
        var found = GameObject.FindWithTag("Player");
        if (found != null) player = found.transform;
    }

    // Inside the chunk's footprint (box or circle); the rule lives in ChunkDefinition.Contains.
    bool InsideChunk(Vector3 position) => chunk != null && chunk.Contains(position);
}
