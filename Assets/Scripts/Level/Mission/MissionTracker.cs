using System;
using UnityEngine;

public enum MissionState { InProgress, ExtractOpen, Complete }

// The mission's flow as a plain class (no Unity types), so it ports to the custom engine as-is:
// objectives in progress -> all complete, extract opens -> extraction held -> mission complete.
public sealed class MissionRules
{
    public MissionState State { get; private set; } = MissionState.InProgress;

    // Advances with the current counts. Returns true when the state changed.
    public bool Update(int total, int complete, bool extracted)
    {
        var next = State;
        if (State == MissionState.InProgress && complete >= total) next = MissionState.ExtractOpen;
        else if (State == MissionState.ExtractOpen && extracted) next = MissionState.Complete;
        bool changed = next != State;
        State = next;
        return changed;
    }
}

// Runs a sub-level's mission: counts the objectives in the scene (MissionObjectives), opens the extract
// (ExtractZone, on the start chunk) once all are complete, and finishes the mission when the extraction
// is held. On completion the game freezes for now; listen to MissionCompleted to hand over to another
// screen later. Lives in the SubLevel prefab.
[DisallowMultipleComponent]
public sealed class MissionTracker : MonoBehaviour
{
    [Tooltip("Freeze the game when the mission completes (until a results / next-scene flow exists).")]
    [SerializeField] bool freezeOnComplete = true;

    [Header("Debug (read-only)")]
    [SerializeField] MissionState state;
    [SerializeField] int objectivesComplete;
    [SerializeField] int objectivesTotal;

    readonly MissionRules rules = new MissionRules();
    ExtractZone extract;
    bool counted;
    bool frozeTime;

    public MissionState State => rules.State;
    public int ObjectivesComplete => MissionObjectives.CompleteCount;
    public int ObjectivesTotal => MissionObjectives.All.Count;
    public ExtractZone Extract => extract != null ? extract : extract = FindAnyObjectByType<ExtractZone>();

    public event Action<MissionState> StateChanged;
    public event Action MissionCompleted;

    void Update()
    {
        objectivesTotal = ObjectivesTotal;
        objectivesComplete = ObjectivesComplete;
        // Wait a frame for the generated objectives to register before judging "all complete".
        if (!counted) { counted = true; return; }
        if (rules.Update(objectivesTotal, objectivesComplete, Extract != null && Extract.IsComplete))
        {
            if (rules.State == MissionState.ExtractOpen)
            {
                if (Extract != null) Extract.Open();
                else Debug.LogWarning("MissionTracker: no ExtractZone in the scene (add one to Chunk_Start).", this);
            }
            if (rules.State == MissionState.Complete)
            {
                MissionCompleted?.Invoke();
                var outcome = MissionOutcomeController.Find(gameObject.scene);
                if (outcome != null) outcome.ReportSuccess();
                else if (freezeOnComplete) { frozeTime = true; Time.timeScale = 0f; }
            }
            StateChanged?.Invoke(rules.State);
        }
        state = rules.State;
    }

    // Never leave the editor or the next scene frozen.
    void OnDestroy() { if (frozeTime) Time.timeScale = 1f; }
}
