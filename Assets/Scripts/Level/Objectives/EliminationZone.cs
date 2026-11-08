using System;
using System.Collections.Generic;
using UnityEngine;

// Clear the camp, as a plain class (no Unity types): enter to start; the camp's enemies must be killed until
// their spawn costs add up to the Total Resource. Leaving starts a grace countdown (progress kept while it
// runs); if it runs out the camp Fails, shows that for a moment, then resets to the full total. Kills count
// wherever the enemy dies, as long as it came from this camp's wave. Once cleared it stays cleared.
public sealed class EliminationRules
{
    public float GraceSeconds { get; }
    public float FailedSeconds { get; }
    public int Total { get; }
    public int Remaining { get; private set; }
    public float GraceRemaining { get; private set; }
    public HoldZoneState State { get; private set; } = HoldZoneState.Waiting; // Holding = fighting
    public float Cleared => Total > 0 ? 1f - (float)Remaining / Total : 1f;
    float failedRemaining;

    public EliminationRules(int totalResource, float graceSeconds, float failedSeconds)
    {
        Total = Math.Max(1, totalResource); Remaining = Total;
        GraceSeconds = Math.Max(0f, graceSeconds); FailedSeconds = Math.Max(0f, failedSeconds);
        GraceRemaining = GraceSeconds;
    }

    // Returns true when the state changed.
    public bool Tick(float deltaTime, bool inside)
    {
        var before = State;
        switch (State)
        {
            case HoldZoneState.Waiting: if (inside) State = HoldZoneState.Holding; break;
            case HoldZoneState.Holding: if (!inside) { State = HoldZoneState.Warning; GraceRemaining = GraceSeconds; } break;
            case HoldZoneState.Warning:
                if (inside) { State = HoldZoneState.Holding; break; }
                GraceRemaining -= deltaTime;
                if (GraceRemaining <= 0f) { State = HoldZoneState.Failed; failedRemaining = FailedSeconds; }
                break;
            case HoldZoneState.Failed:
                failedRemaining -= deltaTime;
                if (failedRemaining <= 0f) { State = HoldZoneState.Waiting; Remaining = Total; GraceRemaining = GraceSeconds; }
                break;
        }
        return State != before;
    }

    // A camp enemy died. Counts only while the fight is on (fighting or in the grace countdown).
    public bool Kill(int cost)
    {
        if (State != HoldZoneState.Holding && State != HoldZoneState.Warning) return false;
        Remaining = Math.Max(0, Remaining - Math.Max(0, cost));
        if (Remaining == 0) State = HoldZoneState.Complete;
        return true;
    }
}

// The camp in an Elimination chunk. Entering starts the camp's wave from its spawn points (the tents the
// outpost places in the camp; a ring outside the edge if there are none). Each of those enemies that dies
// takes its spawn cost off the Total Resource, wherever it dies; the wave never has more out than what is
// left. Leaving starts the flashing countdown; running out fails the camp and resets it. Cleared camps count
// towards the mission. Put it on a child of the chunk, with a HoldZoneVisual for the outline.
[DisallowMultipleComponent]
public sealed class EliminationZone : MonoBehaviour, IMissionObjective, IMissionStatus, IZoneDisplay, IMissionCountdown
{
    [Tooltip("Spawn cost of camp enemies to kill to clear it (small enemies cost 5, elites 10).")]
    [SerializeField, Min(1)] int totalResource = 500;
    [Tooltip("Seconds to get back inside after leaving before the camp fails and resets.")]
    [SerializeField, Min(0f)] float graceSeconds = 5f;
    [Tooltip("How long the failed state shows before the camp resets.")]
    [SerializeField, Min(0f)] float failedSeconds = 2f;
    [Tooltip("The camp's wave (e.g. WaveData_Elimination): its resource per refill is one wave of the fight.")]
    [SerializeField] WaveData wave;
    [Tooltip("With no spawn points in the camp, enemies appear on a ring this many metres outside the chunk's edge.")]
    [SerializeField, Min(0f)] float spawnDistance = 10f;

    [Header("Debug (read-only)")]
    [SerializeField] HoldZoneState state;
    [SerializeField] int remaining;
    [SerializeField] float cleared;

    EliminationRules rules;
    ChunkDefinition chunk;
    Transform player;
    float nextPlayerSearch;
    SpawnDirector director;
    // The camp enemies alive now, each with the handler that counts its death.
    readonly Dictionary<CombatHealth, Action> tracked = new Dictionary<CombatHealth, Action>();

    public HoldZoneState State => rules != null ? rules.State : HoldZoneState.Waiting;
    public bool IsOpen => true;
    public ChunkDefinition Chunk => chunk;
    public bool IsComplete => State == HoldZoneState.Complete;
    public EliminationRules Rules => rules;
    public bool ShowStatus => State == HoldZoneState.Holding || State == HoldZoneState.Warning || State == HoldZoneState.Failed;
    public bool TryGetCountdown(out float seconds, out bool zeroMeansSuccess)
    {
        seconds = rules != null ? Mathf.Max(0, rules.GraceRemaining) : 0;
        zeroMeansSuccess = false;
        return State == HoldZoneState.Warning;
    }
    public string Status => State switch
    {
        HoldZoneState.Warning => $"Get back to the camp! {rules.GraceRemaining:0.0}s",
        HoldZoneState.Failed => "Camp lost: re-enter to try again",
        _ => $"Clear the camp: {rules.Cleared * 100f:0}%",
    };

    void Awake()
    {
        rules = new EliminationRules(totalResource, graceSeconds, failedSeconds);
        chunk = GetComponentInParent<ChunkDefinition>();
        if (chunk == null) Debug.LogWarning("EliminationZone needs a ChunkDefinition on its chunk root.", this);
        if (wave == null) Debug.LogWarning("EliminationZone has no Wave assigned, so no enemies will come.", this);
    }

    void OnEnable() => MissionObjectives.Register(this);
    void OnDisable() { MissionObjectives.Unregister(this); StopWave(); }

    void Update()
    {
        if (rules.State != HoldZoneState.Complete)
        {
            FindPlayerIfNeeded();
            bool inside = player != null && player.gameObject.activeInHierarchy && chunk != null && chunk.Contains(player.position);
            var previous = rules.State;
            if (rules.Tick(Time.deltaTime, inside)) OnStateChanged(previous, rules.State);
            // The wave never fields more than what is left to kill.
            if (director != null) director.SetBudget(this, rules.Remaining);
        }
        state = rules.State; remaining = rules.Remaining; cleared = rules.Cleared;
    }

    void OnStateChanged(HoldZoneState previous, HoldZoneState current)
    {
        if (previous == HoldZoneState.Waiting && current == HoldZoneState.Holding) StartWave();
        if (current == HoldZoneState.Failed || current == HoldZoneState.Complete) StopWave();
    }

    void StartWave()
    {
        director = SpawnDirector.Instance;
        if (director == null || wave == null) { Debug.LogWarning("EliminationZone: no Spawn Director or no Wave, so no camp enemies come.", this); return; }
        director.Spawned += OnSpawned;
        if (!director.RunWave(wave, CampPoints(), this)) Debug.LogWarning("EliminationZone: another wave is already running.", this);
        director.SetBudget(this, rules.Remaining);
    }

    void StopWave()
    {
        foreach (var pair in tracked) if (pair.Key != null) pair.Key.Died -= pair.Value;
        tracked.Clear();
        if (director == null) return;
        director.Spawned -= OnSpawned;
        director.StopWave(this);
        director = null;
    }

    // The camp's spawn points (the outpost's tents), found when the fight starts, since the outpost is
    // placed after the chunk; a ring outside the edge if there are none.
    List<SpawnPoint> CampPoints()
    {
        var points = new List<SpawnPoint>();
        if (chunk != null)
            foreach (var point in chunk.GetComponentsInChildren<SpawnPoint>(true))
                if (!(point is RoamingSpawner) && !(point is ChunkRingSpawner)) points.Add(point);
        if (points.Count == 0 && chunk != null)
        {
            var ring = new GameObject("Spawn Ring").AddComponent<ChunkRingSpawner>();
            ring.transform.SetParent(chunk.transform, false);
            ring.Configure((chunk.IsCircle ? chunk.RadiusMetres : chunk.SizeMetres.magnitude * 0.5f) + spawnDistance);
            points.Add(ring);
        }
        return points;
    }

    void OnSpawned(UnityEngine.Object owner, GameObject enemy)
    {
        if (owner != this) return;
        var health = enemy.GetComponentInChildren<CombatHealth>();
        if (health == null) return;
        int cost = EnemySpawnCost.Of(enemy);
        Action died = null;
        died = () =>
        {
            health.Died -= died;
            tracked.Remove(health);
            rules.Kill(cost);
            if (rules.State == HoldZoneState.Complete) StopWave();
        };
        health.Died += died;
        tracked[health] = died;
    }

    void FindPlayerIfNeeded()
    {
        if (player != null || Time.unscaledTime < nextPlayerSearch) return;
        nextPlayerSearch = Time.unscaledTime + 0.5f;
        var found = GameObject.FindWithTag("Player");
        if (found != null) player = found.transform;
    }
}
