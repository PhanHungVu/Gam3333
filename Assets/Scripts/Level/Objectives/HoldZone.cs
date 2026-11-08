using System;
using UnityEngine;
using UnityEngine.Serialization;

public enum HoldZoneState { Waiting, Holding, Warning, Failed, Complete }

// What a zone outline needs to know (HoldZoneVisual): its state, whether it's open, and its chunk.
// Hold zones (KOTH, extract) and the elimination camp provide it.
public interface IZoneDisplay
{
    HoldZoneState State { get; }
    bool IsOpen { get; }
    ChunkDefinition Chunk { get; }
}

// Hold rules (King of the Hill, extraction) as a plain class (no Unity types), so they port to the custom engine as-is.
// Waiting -> player enters -> Holding (timer counts). Leaving starts a grace period (Warning) with
// the timer paused; re-entering resumes it. If the grace runs out the hold Fails, shows that for a
// moment, then resets to Waiting with the timer back at zero. Once complete it stays complete.
public sealed class HoldZoneRules
{
    public float HoldSeconds { get; }
    public float GraceSeconds { get; }
    public float FailedSeconds { get; }
    public HoldZoneState State { get; private set; } = HoldZoneState.Waiting;
    public float Elapsed { get; private set; }
    public float Remaining => Math.Max(0f, HoldSeconds - Elapsed);
    public float GraceRemaining { get; private set; }
    float failedAge;

    public HoldZoneRules(float holdSeconds, float graceSeconds, float failedSeconds)
    {
        HoldSeconds = Math.Max(0.1f, holdSeconds);
        GraceSeconds = Math.Max(0f, graceSeconds);
        FailedSeconds = Math.Max(0f, failedSeconds);
    }

    // Advances one frame. Returns true when the state changed.
    public bool Tick(float deltaTime, bool playerInside)
    {
        deltaTime = Math.Max(0f, deltaTime);
        HoldZoneState next = State;
        switch (State)
        {
            case HoldZoneState.Waiting:
                if (playerInside) { Elapsed = 0f; next = HoldZoneState.Holding; }
                break;
            case HoldZoneState.Holding:
                if (!playerInside) { GraceRemaining = GraceSeconds; next = HoldZoneState.Warning; break; }
                Elapsed = Math.Min(HoldSeconds, Elapsed + deltaTime);
                if (Elapsed >= HoldSeconds) next = HoldZoneState.Complete;
                break;
            case HoldZoneState.Warning:
                if (playerInside) { next = HoldZoneState.Holding; break; }
                GraceRemaining = Math.Max(0f, GraceRemaining - deltaTime);
                if (GraceRemaining <= 0f) { failedAge = 0f; next = HoldZoneState.Failed; }
                break;
            case HoldZoneState.Failed:
                failedAge += deltaTime;
                if (failedAge >= FailedSeconds) { Elapsed = 0f; next = HoldZoneState.Waiting; }
                break;
        }
        bool changed = next != State;
        State = next;
        return changed;
    }
}

// A zone the player must stand inside for a set time: the chunk's footprint (box or circle, from
// ChunkDefinition), timed by HoldZoneRules. KOTH_Zone and ExtractZone build on it. A closed zone (IsOpen
// false) does nothing until opened. Put it on a child of the chunk. Visuals listen to State.
public abstract class HoldZone : MonoBehaviour, IZoneDisplay
{
    [Tooltip("Seconds to hold inside the chunk.")]
    [FormerlySerializedAs("surviveSeconds")]
    [SerializeField, Min(0.1f)] protected float holdSeconds = 60f;
    [Tooltip("Seconds the player has to get back inside after leaving, with the timer paused.")]
    [SerializeField, Min(0f)] protected float graceSeconds = 5f;
    [Tooltip("How long the failed state shows before the hold resets.")]
    [SerializeField, Min(0f)] protected float failedSeconds = 2f;

    [Header("Debug (read-only)")]
    [SerializeField] HoldZoneState state;
    [SerializeField] float timeRemaining;
    [SerializeField] float graceRemaining;

    HoldZoneRules rules;
    ChunkDefinition chunk;
    Transform player;
    float nextPlayerSearch;

    public HoldZoneState State => rules != null ? rules.State : HoldZoneState.Waiting;
    public float TimeRemaining => rules != null ? rules.Remaining : holdSeconds;
    public float GraceRemaining => rules != null ? rules.GraceRemaining : graceSeconds;
    public bool IsComplete => State == HoldZoneState.Complete;
    public ChunkDefinition Chunk => chunk;
    // Whether the zone is active. Closed zones neither time nor show.
    public virtual bool IsOpen => true;

    public event Action<HoldZoneState> StateChanged;
    public event Action Completed;

    protected virtual void Awake()
    {
        rules = new HoldZoneRules(holdSeconds, graceSeconds, failedSeconds);
        chunk = GetComponentInParent<ChunkDefinition>();
        if (chunk == null) Debug.LogWarning(GetType().Name + " needs a ChunkDefinition on its chunk root.", this);
    }

    protected virtual void Update()
    {
        if (Time.deltaTime <= 0f) return;
        if (IsOpen && rules.State != HoldZoneState.Complete)
        {
            FindPlayerIfNeeded();
            bool inside = player != null && player.gameObject.activeInHierarchy && chunk != null && chunk.Contains(player.position);
            HoldZoneState previous = rules.State;
            if (rules.Tick(Time.deltaTime, inside))
            {
                OnHoldStateChanged(previous, rules.State);
                StateChanged?.Invoke(rules.State);
                if (rules.State == HoldZoneState.Complete) Completed?.Invoke();
            }
        }
        state = rules.State;
        timeRemaining = rules.Remaining;
        graceRemaining = rules.GraceRemaining;
    }

    // For zones that react to the hold starting, failing or completing (e.g. KOTH runs its wave).
    protected virtual void OnHoldStateChanged(HoldZoneState previous, HoldZoneState current) { }

    void FindPlayerIfNeeded()
    {
        if (player != null || Time.unscaledTime < nextPlayerSearch) return;
        nextPlayerSearch = Time.unscaledTime + 0.5f;
        var found = GameObject.FindWithTag("Player");
        if (found != null) player = found.transform;
    }
}
