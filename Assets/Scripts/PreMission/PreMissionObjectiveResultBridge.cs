using UnityEngine;

// Optional gameplay-side adapter. Attach before the objective can complete and
// begin a tracked mission attempt in the gameplay launch implementation first.
[DisallowMultipleComponent, RequireComponent(typeof(TempLevelObjective))]
public sealed class PreMissionObjectiveResultBridge : MonoBehaviour
{
    TempLevelObjective objective;
    string attemptId;
    bool reported;
    bool triedAutomaticBinding;
    public string AttemptId => attemptId;
    void OnEnable()
    {
        objective = GetComponent<TempLevelObjective>();
        // Re-enabling an old, unbound objective must never adopt a later mission.
        if (!triedAutomaticBinding)
        { triedAutomaticBinding = true; TryBindAttempt(QuestProgressionRuntime.Current?.ActiveAttempt); }
        objective.Completed += OnCompleted;
        if (objective.IsComplete) OnCompleted();
    }
    void OnDisable() { if (objective != null) objective.Completed -= OnCompleted; }
    void OnDestroy() { ReportAbandoned(); }
    public bool TryBindAttempt(QuestMissionAttempt attempt)
    {
        if (attemptId != null || attempt == null || QuestProgressionRuntime.Current?.ActiveAttempt != attempt) return false;
        attemptId = attempt.Id; return true;
    }
    void OnCompleted() => Report(QuestMissionOutcome.Succeeded);
    public bool ReportFailure() => Report(QuestMissionOutcome.Failed);
    public bool ReportAbandoned() => Report(QuestMissionOutcome.Abandoned);
    bool Report(QuestMissionOutcome outcome)
    {
        if (reported || attemptId == null) return false;
        reported = QuestProgressionRuntime.ReportMissionResult(attemptId, outcome);
        return reported;
    }
}
