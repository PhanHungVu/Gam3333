using System;

// Mission flow: objectives -> extract opens when all are complete -> extraction completes the mission.
public static class MissionChecks
{
    sealed class FakeObjective : IMissionObjective { public bool IsComplete { get; set; } }

    public static void Run(Action<bool, string> check)
    {
        var rules = new MissionRules();
        rules.Update(3, 2, false);
        bool waiting = rules.State == MissionState.InProgress;
        rules.Update(3, 3, false);
        check(waiting && rules.State == MissionState.ExtractOpen, "The extract opens only once every objective is complete");
        rules.Update(3, 3, false);
        bool open = rules.State == MissionState.ExtractOpen;
        rules.Update(3, 3, true);
        check(open && rules.State == MissionState.Complete, "Holding the extract completes the mission");
        rules.Update(3, 0, false);
        check(rules.State == MissionState.Complete, "A completed mission stays complete");

        var a = new FakeObjective(); var b = new FakeObjective();
        int before = MissionObjectives.All.Count;
        MissionObjectives.Register(a); MissionObjectives.Register(b); MissionObjectives.Register(a);
        b.IsComplete = true;
        bool counted = MissionObjectives.All.Count == before + 2 && MissionObjectives.CompleteCount >= 1;
        MissionObjectives.Unregister(a); MissionObjectives.Unregister(b);
        check(counted && MissionObjectives.All.Count == before, "Objectives register themselves once, and leave when removed");

        var extract = new HoldZoneRules(10f, 5f, 2f);
        extract.Tick(0.1f, true); extract.Tick(10f, true);
        check(extract.State == HoldZoneState.Complete, "The extract uses the same timed hold as KOTH");
    }
}
