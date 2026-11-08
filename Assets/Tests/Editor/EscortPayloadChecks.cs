using System;
using System.Linq;
using UnityEngine;

// Escort payload rules: advance with the player, stop when contested, roll back to the last checkpoint at most.
public static class EscortPayloadChecks
{
    sealed class FakeObjective : IMissionObjective { public bool IsComplete { get; set; } }
    sealed class FakeSteps : IMissionObjective, IMissionProgress
    {
        public bool IsComplete => StepsDone >= StepsTotal;
        public int StepsDone { get; set; }
        public int StepsTotal { get; set; }
    }

    public static void Run(Action<bool, string> check)
    {
        var rules = new EscortRules(0f, 100f, new[] { 30f, 60f, 100f });
        rules.Step(1f, false, false, 3f, 1.5f);
        bool idle = rules.State == EscortState.Idle && rules.Distance == 0f;
        rules.Step(5f, true, false, 3f, 1.5f);
        check(idle && rules.State == EscortState.Advancing && Math.Abs(rules.Distance - 15f) < 0.001f,
            "The payload only moves while the player is near it");
        rules.Step(5f, true, true, 3f, 1.5f);
        check(rules.State == EscortState.Contested && Math.Abs(rules.Distance - 15f) < 0.001f, "Player and enemies near: it stops");
        rules.Step(4f, false, true, 3f, 1.5f);
        check(rules.State == EscortState.Retreating && Math.Abs(rules.Distance - 9f) < 0.001f, "Enemies alone push it back");
        rules.Step(100f, false, true, 3f, 1.5f);
        check(rules.Distance == 0f, "It never rolls back past the start");

        int passed = rules.Step(12f, true, false, 3f, 1.5f);
        check(passed == 1 && rules.CheckpointsPassed == 1 && rules.Floor == 30f, "Passing a checkpoint records it");
        rules.Step(100f, false, true, 3f, 1.5f);
        check(rules.Distance == 30f, "It never rolls back past the last checkpoint reached");
        rules.Step(5f, true, false, 3f, 1.5f);
        rules.ReturnToCheckpoint();
        check(rules.Distance == 30f, "Destroyed, it returns to the last checkpoint reached");
        rules.Step(100f, true, false, 3f, 1.5f);
        check(rules.Arrived && rules.Distance == 100f && rules.CheckpointsPassed == 3, "Reaching the end of the route completes the escort");

        // Starting at the first checkpoint: it begins there and never rolls back behind it.
        var fromCheckpoint = new EscortRules(40f, 120f, new[] { 40f, 80f, 120f });
        fromCheckpoint.Step(100f, false, true, 3f, 1.5f);
        check(fromCheckpoint.Distance == 40f && fromCheckpoint.CheckpointCount == 2,
            "The escort starts at the first checkpoint, never rolls back behind it, and counts only the checkpoints after it");

        float greenHeal = EscortPayload.AdvancingHeal(EscortState.Advancing, 1000f, 2f, 1f);
        check(Mathf.Approximately(greenHeal, 20f) && EscortPayload.AdvancingHeal(EscortState.Contested, 1000f, 2f, 1f) == 0f
            && EscortPayload.AdvancingHeal(EscortState.Retreating, 1000f, 2f, 1f) == 0f && EscortPayload.AdvancingHeal(EscortState.Idle, 1000f, 2f, 1f) == 0f,
            "The payload heals only while it moves with no enemies near (green ring), by its % of max health per second");

        var steps = new FakeSteps { StepsDone = 1, StepsTotal = 3 };
        var open = new FakeObjective(); var done = new FakeObjective { IsComplete = true };
        MissionHud.CountObjectives(out int doneBefore, out int totalBefore);
        MissionObjectives.Register(steps); MissionObjectives.Register(open); MissionObjectives.Register(done);
        MissionHud.CountObjectives(out int shownDone, out int shownTotal);
        MissionObjectives.Unregister(steps); MissionObjectives.Unregister(open); MissionObjectives.Unregister(done);
        check(shownDone - doneBefore == 2 && shownTotal - totalBefore == 5,
            "The HUD counts each escort checkpoint as an objective, and other objectives once each");

        var cycle = new WaveCycle(0.5f, 10f, 100);
        cycle.ScaleTotal(1.5f);
        check(cycle.ResourceTotal == 150, "Escort waves can grow: the resource scales for each checkpoint passed");

        var agentObject = new UnityEngine.GameObject("Escort Target Check");
        var payloadBody = new UnityEngine.GameObject("Escort Target Body");
        var fallbackPlayer = new UnityEngine.GameObject("Escort Fallback Player"); fallbackPlayer.tag = "Player";
        try
        {
            var agent = agentObject.AddComponent<EnemyAgent>();
            agent.PreferredTarget = payloadBody.transform;
            agent.RefreshTarget();
            bool preferred = agent.Target == payloadBody.transform;
            payloadBody.SetActive(false);
            agent.RefreshTarget();
            check(preferred && agent.Target != null && agent.Target.CompareTag("Player") && agent.IsPursuingFallbackTarget,
                "Escort enemies go for the payload, and fall back to the player when it is destroyed");
        }
        finally { UnityEngine.Object.DestroyImmediate(agentObject); UnityEngine.Object.DestroyImmediate(payloadBody); UnityEngine.Object.DestroyImmediate(fallbackPlayer); }
    }
}
