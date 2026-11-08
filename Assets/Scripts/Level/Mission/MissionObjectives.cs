using System.Collections.Generic;

// Something the mission needs done (a KOTH hold today; other objective types later).
public interface IMissionObjective
{
    bool IsComplete { get; }
}

// An objective that can describe its own progress for the HUD (e.g. the escort's checkpoints, a camp's
// percentage cleared). The HUD shows the first one whose ShowStatus is true, else the objectives count.
public interface IMissionStatus
{
    bool ShowStatus { get; }
    string Status { get; }
}

// An objective made of several steps that each count as an objective on the HUD (e.g. the escort's
// checkpoints: "Objectives 1 / 3"). Display only; the mission still finishes when IsComplete.
public interface IMissionProgress
{
    int StepsDone { get; }
    int StepsTotal { get; }
}

// Every mission objective currently in the scene. Objectives add themselves when enabled and remove
// themselves when disabled, so the mission tracker never needs to know what kinds there are.
public static class MissionObjectives
{
    static readonly List<IMissionObjective> all = new List<IMissionObjective>();
    public static IReadOnlyList<IMissionObjective> All => all;

    public static void Register(IMissionObjective objective) { if (objective != null && !all.Contains(objective)) all.Add(objective); }
    public static void Unregister(IMissionObjective objective) => all.Remove(objective);

    public static int CompleteCount
    {
        get { int n = 0; foreach (var o in all) if (o.IsComplete) n++; return n; }
    }
}
