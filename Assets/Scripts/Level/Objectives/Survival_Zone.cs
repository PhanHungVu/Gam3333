using System;
using System.Collections.Generic;
using UnityEngine;

public enum Survival_ZoneState { Waiting, Surviving, Warning, Failed, Complete }

// Survival rules as a plain class (no Unity types), so they port to the custom engine as-is.
// Waiting -> player enters -> Surviving (timer counts). Leaving starts a grace period (Warning) with
// the timer paused; re-entering resumes it. If the grace runs out the mission Fails, shows that for a
// moment, then resets to Waiting with the timer back at zero. Once complete it stays complete.
public sealed class Survival_ZoneRules
{
    public float SurviveSeconds { get; }
    public float GraceSeconds { get; }
    public float FailedSeconds { get; }
    public Survival_ZoneState State { get; private set; } = Survival_ZoneState.Waiting;
    public float Elapsed { get; private set; }
    public float Remaining => Math.Max(0f, SurviveSeconds - Elapsed);
    public float GraceRemaining { get; private set; }
    float failedAge;

    public Survival_ZoneRules(float surviveSeconds, float graceSeconds, float failedSeconds)
    {
        SurviveSeconds = Math.Max(0.1f, surviveSeconds);
        GraceSeconds = Math.Max(0f, graceSeconds);
        FailedSeconds = Math.Max(0f, failedSeconds);
    }

    // Advances one frame. Returns true when the state changed.
    public bool Tick(float deltaTime, bool playerInside)
    {
        deltaTime = Math.Max(0f, deltaTime);
        Survival_ZoneState next = State;
        switch (State)
        {
            case Survival_ZoneState.Waiting:
                if (playerInside) { Elapsed = 0f; next = Survival_ZoneState.Surviving; }
                break;
            case Survival_ZoneState.Surviving:
                if (!playerInside) { GraceRemaining = GraceSeconds; next = Survival_ZoneState.Warning; break; }
                Elapsed = Math.Min(SurviveSeconds, Elapsed + deltaTime);
                if (Elapsed >= SurviveSeconds) next = Survival_ZoneState.Complete;
                break;
            case Survival_ZoneState.Warning:
                if (playerInside) { next = Survival_ZoneState.Surviving; break; }
                GraceRemaining = Math.Max(0f, GraceRemaining - deltaTime);
                if (GraceRemaining <= 0f) { failedAge = 0f; next = Survival_ZoneState.Failed; }
                break;
            case Survival_ZoneState.Failed:
                failedAge += deltaTime;
                if (failedAge >= FailedSeconds) { Elapsed = 0f; next = Survival_ZoneState.Waiting; }
                break;
        }
        bool changed = next != State;
        State = next;
        return changed;
    }
}

// Measures whether the player is inside the chunk's footprint (from ChunkDefinition) and feeds it to
// the rules. Starts the chunk's own spawners when survival begins and stops them on failure or completion;
// enemies already spawned stay until killed. Put it on a child of the chunk. The waves are run by
// the scene's SpawnDirector on this chunk's spawn points, which pauses roaming meanwhile. Visuals listen to State.
[DisallowMultipleComponent]
public sealed class Survival_Zone : MonoBehaviour
{
    [Tooltip("Seconds to survive inside the chunk.")]
    [SerializeField, Min(0.1f)] float surviveSeconds = 60f;
    [Tooltip("Seconds the player has to get back inside after leaving, with the timer paused.")]
    [SerializeField, Min(0f)] float graceSeconds = 5f;
    [Tooltip("How long the failed state shows before the mission resets.")]
    [SerializeField, Min(0f)] float failedSeconds = 2f;
    [Tooltip("Wave the scene's Spawn Director runs while survival is under way (e.g. WaveData_Survival).")]
    [SerializeField] WaveData wave;
    [Tooltip("Where this objective's enemies appear. Left empty, every Spawn Point inside this chunk is used.")]
    [SerializeField] SpawnPoint[] spawnPoints;

    [Header("Debug (read-only)")]
    [SerializeField] Survival_ZoneState state;
    [SerializeField] float timeRemaining;
    [SerializeField] float graceRemaining;

    Survival_ZoneRules rules;
    ChunkDefinition chunk;
    readonly List<SpawnPoint> points = new List<SpawnPoint>();
    Transform player;
    float nextPlayerSearch;

    public Survival_ZoneState State => rules != null ? rules.State : Survival_ZoneState.Waiting;
    public float TimeRemaining => rules != null ? rules.Remaining : surviveSeconds;
    public float GraceRemaining => rules != null ? rules.GraceRemaining : graceSeconds;
    public bool IsComplete => State == Survival_ZoneState.Complete;
    public ChunkDefinition Chunk => chunk;

    public event Action<Survival_ZoneState> StateChanged;
    public event Action Completed;

    void Awake()
    {
        rules = new Survival_ZoneRules(surviveSeconds, graceSeconds, failedSeconds);
        chunk = GetComponentInParent<ChunkDefinition>();
        if (chunk == null) Debug.LogWarning("Survival_Zone needs a ChunkDefinition on its chunk root.", this);
        if (spawnPoints != null)
            foreach (var point in spawnPoints)
                if (point != null) points.Add(point);
        if (points.Count == 0 && chunk != null)
            foreach (var point in chunk.GetComponentsInChildren<SpawnPoint>(true))
                if (!(point is RoamingSpawner)) points.Add(point);
        if (points.Count == 0) Debug.LogWarning("Survival_Zone found no Spawn Point in its chunk.", this);
        if (wave == null) Debug.LogWarning("Survival_Zone has no Wave assigned, so no enemies will come.", this);
    }

    void OnDisable()
    {
        if (SpawnDirector.Instance != null) SpawnDirector.Instance.StopWave(this);
    }

    void Update()
    {
        if (rules.State != Survival_ZoneState.Complete)
        {
            FindPlayerIfNeeded();
            bool inside = player != null && player.gameObject.activeInHierarchy && InsideChunk(player.position);
            Survival_ZoneState previous = rules.State;
            if (rules.Tick(Time.deltaTime, inside)) OnStateChanged(previous, rules.State);
        }
        state = rules.State;
        timeRemaining = rules.Remaining;
        graceRemaining = rules.GraceRemaining;
    }

    void OnStateChanged(Survival_ZoneState previous, Survival_ZoneState current)
    {
        var director = SpawnDirector.Instance;
        if (previous == Survival_ZoneState.Waiting && current == Survival_ZoneState.Surviving)
        {
            if (director == null) Debug.LogWarning("Survival_Zone: no Spawn Director in the scene, so no wave runs.", this);
            else if (!director.RunWave(wave, points, this)) Debug.LogWarning("Survival_Zone: another wave is already running.", this);
        }
        if ((current == Survival_ZoneState.Failed || current == Survival_ZoneState.Complete) && director != null)
            director.StopWave(this);
        StateChanged?.Invoke(current);
        if (current == Survival_ZoneState.Complete) Completed?.Invoke();
    }

    void FindPlayerIfNeeded()
    {
        if (player != null || Time.unscaledTime < nextPlayerSearch) return;
        nextPlayerSearch = Time.unscaledTime + 0.5f;
        var found = GameObject.FindWithTag("Player");
        if (found != null) player = found.transform;
    }

    // Inside the chunk's footprint box, in the chunk's own orientation. Height is ignored (flat map).
    bool InsideChunk(Vector3 position)
    {
        if (chunk == null) return false;
        Vector3 local = chunk.transform.InverseTransformPoint(position);
        Vector2 half = chunk.SizeMetres * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.y;
    }
}
