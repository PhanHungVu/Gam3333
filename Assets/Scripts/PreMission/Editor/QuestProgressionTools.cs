using UnityEditor;
using UnityEngine;
using System.Linq;

// Editor-only development actions. The temporary completion control is retired after P4 review.
public static class QuestProgressionTools
{
    const string Menu = "Tools/Mech/Pre-Mission/Development/";
    static QuestProgressionSession lastCompletedRun;
    static int lastCompletedFrame=-1;
    public static bool CanCompleteSmallQuest(PreMissionFlowController flow)
    {
        if(!Application.isPlaying || flow==null || !flow.isActiveAndEnabled || flow.Session==null
            || !flow.CanSelectBoard || flow.IsPosting || flow.IsTransitioning) return false;
        var session=flow.Session; var run=session.Progression;
        var torso=session.SelectedTorso;
        return run==QuestProgressionRuntime.Current && run.ActiveAttempt==null && torso!=null && torso.partType==PartType.Torso
            && !string.IsNullOrWhiteSpace(torso.partId) && run.Catalogue!=null && run.Catalogue.starterTorsos!=null && run.Catalogue.starterTorsos.Contains(torso)
            && !(run==lastCompletedRun && Time.frameCount==lastCompletedFrame)
            && run.Offers.Any(o=>o.Quest.category==QuestCategory.Side);
    }
    public static bool CompleteSmallQuest(PreMissionFlowController flow)
    {
        if(!CanCompleteSmallQuest(flow)) return false;
        var run=flow.Session.Progression;
        var quest=run.Offers.First(o=>o.Quest.category==QuestCategory.Side).Quest;
        if(!QuestProgressionRuntime.TryBeginMission(quest,flow.Session.SelectedTorso,out var attempt)
            || !QuestProgressionRuntime.ReportMissionResult(attempt.Id,QuestMissionOutcome.Succeeded)) return false;
        lastCompletedRun=run; lastCompletedFrame=Time.frameCount;
        EditorApplication.QueuePlayerLoopUpdate(); return true;
    }
    [MenuItem(Menu + "Reset Quest Progression")]
    static void Reset() => Object.FindAnyObjectByType<PreMissionFlowController>().StartNewProgression();
    [MenuItem(Menu + "Reset Quest Progression", true)]
    static bool CanReset() => Application.isPlaying && Object.FindAnyObjectByType<PreMissionFlowController>() != null;
}
