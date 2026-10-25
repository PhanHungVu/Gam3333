using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

// Exercises the same Editor-only action used by the restored Inspector button.
public static class QuestInspectorChecks
{
    const string Pending="QuestInspectorChecks.Pending", Count="QuestInspectorChecks.Count";
    static readonly Stack<IEnumerator> frames=new Stack<IEnumerator>();
    static double deadline;
    static int checks;
    static bool started;
    static PreMissionFlowController Flow=>Object.FindAnyObjectByType<PreMissionFlowController>();
    static bool Busy=>Flow.IsPosting||Flow.IsTransitioning;
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.update+=Step;
    public static void RunBatch()
    {
        if(!Application.isBatchMode) throw new Exception("Disposable project only");
        EditorSceneManager.OpenScene("Assets/Scenes/PreMissionScene.unity");
        Check(!QuestProgressionTools.CanCompleteSmallQuest(Flow)&&!QuestProgressionTools.CompleteSmallQuest(Flow),"Edit Mode rejects completion without mutating data");
        var inspector=Editor.CreateEditor(Flow);
        Check(inspector is PreMissionFlowControllerEditor,"Unity selects the restored custom Inspector"); Object.DestroyImmediate(inspector);
        SessionState.SetInt(Count,checks); SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); checks++; Debug.Log("QUEST INSPECTOR PASS: "+message); }
    static void Step()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        try
        {
            if(!started) { started=true; checks=SessionState.GetInt(Count,0); deadline=EditorApplication.timeSinceStartup+150; frames.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("Inspector checks timed out");
            while(frames.Count>0)
            {
                var current=frames.Peek();
                if(!current.MoveNext()) { (current as IDisposable)?.Dispose(); frames.Pop(); continue; }
                if(current.Current is IEnumerator nested) { frames.Push(nested); continue; }
                return;
            }
            PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false);
            Debug.Log("QUEST INSPECTOR CHECKS COMPLETE: "+checks); EditorApplication.Exit(0);
        }
        catch(Exception exception) { PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false); Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Time.timeScale=0; Application.runInBackground=true;
        Check(!QuestProgressionTools.CanCompleteSmallQuest(Flow),"Button is disabled while the board introduction is posting");
        while(Busy) yield return null;
        var run=Flow.Session.Progression; int buttons=Flow.GetComponentsInChildren<Button>(true).Length;
        Check(QuestProgressionTools.CanCompleteSmallQuest(Flow),"Settled board enables the Inspector action");
        Flow.enabled=false;
        Check(!QuestProgressionTools.CompleteSmallQuest(Flow)&&run.SuccessfulSmallCompletions==0,"Disabled controller rejects the action");
        Flow.enabled=true; yield return null;
        Flow.SelectQuest(run.Offers[0].Quest);
        Check(!QuestProgressionTools.CompleteSmallQuest(Flow),"Inspection transition rejects completion");
        while(Busy) yield return null;
        Check(!QuestProgressionTools.CompleteSmallQuest(Flow),"Quest Details requires returning to the board first");
        Flow.Back(); while(Busy) yield return null;
        run.TryBeginMission(run.Offers[0].Quest,Flow.Session.SelectedTorso,out var active);
        Check(!QuestProgressionTools.CanCompleteSmallQuest(Flow)&&!QuestProgressionTools.CompleteSmallQuest(Flow)
            &&run.ActiveAttempt==active&&run.SuccessfulSmallCompletions==0,"Active gameplay attempt is left untouched");
        run.ReportMissionResult(active.Id,QuestMissionOutcome.Abandoned); yield return null; yield return null; while(Busy) yield return null;
        var fields=new SerializedObject(Flow); var original=(MissionStartHook)fields.FindProperty("missionStartHook").objectReferenceValue;
        var recording=Flow.gameObject.AddComponent<PreMissionRecordingHook>(); fields.FindProperty("missionStartHook").objectReferenceValue=recording; fields.ApplyModifiedPropertiesWithoutUndo();
        for(int win=1;win<=3;win++)
        {
            var unchanged=Flow.StoneBoard.Stones.Skip(1).ToDictionary(s=>s.Offer.Id,s=>s.transform.localPosition);
            Check(QuestProgressionTools.CompleteSmallQuest(Flow)&&run.SuccessfulSmallCompletions==win&&run.ActiveAttempt==null,"Inspector action completes exactly one tracked small quest, success "+win);
            Check(!QuestProgressionTools.CompleteSmallQuest(Flow)&&run.SuccessfulSmallCompletions==win,"Same-frame double click is rejected, success "+win);
            yield return null; yield return null;
            Check(!QuestProgressionTools.CanCompleteSmallQuest(Flow),"Arrival/marker animation locks the next test click, success "+win);
            while(Busy) yield return null;
            Check(Flow.StoneBoard.Motion.ProgressTrack.ShownStep==win&&unchanged.All(p=>Flow.StoneBoard.FindStone(p.Key).transform.localPosition==p.Value),
                "Real board presentation advances while unrelated rocks remain fixed, success "+win);
        }
        Check(run.BigQuest!=null&&Flow.StoneBoard.Stones.Count==4&&Flow.StoneBoard.Motion.Feedback.HeavyImpacts==1,
            "Three Inspector completions trigger one central main-rock reveal");
        Check(Flow.StoneBoard.Stones.All(s=>Flow.StoneBoard.StoneAt(Flow.StoneBoard.BoardCamera.WorldToScreenPoint(s.HitCollider.bounds.center))==s),
            "Main and small quests remain freely selectable");
        Check(recording.Calls==0&&SceneManager.GetActiveScene().name=="PreMissionScene"&&Flow.GetComponentsInChildren<Button>(true).Length==buttons,
            "Testing never calls gameplay launch, loads a mission or adds an in-game button");
        fields.Update(); fields.FindProperty("missionStartHook").objectReferenceValue=original; fields.ApplyModifiedPropertiesWithoutUndo(); Object.Destroy(recording);
        Time.timeScale=1;
    }
}
