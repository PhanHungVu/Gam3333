using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// P4 live preferences, allocation sampling and disposable Windows build entry.
public static class PreMissionReleaseChecks
{
    const string Pending="PreMissionReleaseChecks.Pending";
    static readonly Stack<IEnumerator> frames=new Stack<IEnumerator>();
    static bool started; static int checks; static double deadline;
    static readonly List<double> layoutMs=new List<double>(),terrainMs=new List<double>();
    static Recorder layoutRecorder,terrainRecorder; static int sampledFrame=-1;
    static PreMissionFlowController Flow=>Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board=>Flow.StoneBoard;
    static PreMissionStoneMotion Motion=>Board.Motion;
    static QuestBoardImpactFeedback Fx=>Motion.Feedback;
    static bool Busy=>Flow.IsPosting||Flow.IsTransitioning;
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.update+=Step;
    public static void RunBatch()
    {
        if(!Application.isBatchMode) throw new Exception("Disposable project only");
        GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline=null;
        EditorSceneManager.OpenScene("Assets/Scenes/PreMissionScene.unity"); SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); checks++; Debug.Log("RELEASE PASS: "+message); }
    static void Step()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        try
        {
            if(!started) { started=true; deadline=EditorApplication.timeSinceStartup+300; frames.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors(); Sample();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("Release checks timed out");
            while(frames.Count>0)
            {
                var current=frames.Peek(); if(!current.MoveNext()) { (current as IDisposable)?.Dispose(); frames.Pop(); continue; }
                if(current.Current is IEnumerator nested) { frames.Push(nested); continue; } return;
            }
            PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false);
            Debug.Log("PRE-MISSION RELEASE CHECKS COMPLETE: "+checks); EditorApplication.Exit(0);
        }
        catch(Exception exception) { PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false); Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    static void Sample()
    {
        if(sampledFrame==Time.frameCount) return; sampledFrame=Time.frameCount;
        if(layoutRecorder!=null&&layoutRecorder.sampleBlockCount>0) layoutMs.Add(layoutRecorder.elapsedNanoseconds/1000000.0);
        if(terrainRecorder!=null&&terrainRecorder.sampleBlockCount>0) terrainMs.Add(terrainRecorder.elapsedNanoseconds/1000000.0);
    }
    static IEnumerator Frames(int count)
    { int target=Time.frameCount+count; while(Time.frameCount<target) yield return null; }
    static IEnumerator Ready()
    { yield return Frames(2); while(Busy||Board.Progression!=QuestProgressionRuntime.Current||Board.Stones.Count!=Board.Progression.Offers.Count) yield return null; }
    static bool Attached(QuestStoneView s) => Vector3.Distance(Motion.Miniature.transform.position,s.SurfacePoint+s.SurfaceRotation*Vector3.up*0.015f)<0.001f
        &&Quaternion.Angle(Motion.Miniature.transform.rotation,s.SurfaceRotation)<0.02f;
    static bool AllHit()=>Board.InputEnabled&&Board.Stones.All(s=>Board.StoneAt(Board.BoardCamera.WorldToScreenPoint(s.HitCollider.bounds.center))==s);
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground=true; Time.timeScale=0;
        foreach(var device in InputSystem.devices.Where(d=>d is Mouse||d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
        yield return Ready();
        layoutRecorder=Recorder.Get("PreMission.Layout"); terrainRecorder=Recorder.Get("PreMission.TerrainBuild");
        layoutRecorder.enabled=terrainRecorder.enabled=true;
        Check(ReferenceEquals(Board.Stones,Board.Stones),"The read-only stone view is reused rather than allocated on each query");
        var stone=Board.Stones[0]; var point=(Vector2)Board.BoardCamera.WorldToScreenPoint(stone.HitCollider.bounds.center);
        for(int i=0;i<50;i++) Board.StoneAt(point);
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<500;i++) Board.StoneAt(point);
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        Debug.Log("RAYCAST ALLOCATION SAMPLE: "+bytes+" bytes / 500 warmed queries");
        Check(bytes<=256,"Warmed pointer selection avoids per-query managed allocation");
        var run=Board.Progression; var catalogue=run.Catalogue; var root=stone.transform.localPosition;
        Flow.SelectQuest(stone.Offer.Quest); yield return Ready();
        var lifted=stone.CurrentOffset; Check(lifted.y>0.2f&&Attached(stone),"Normal held inspection is lifted and attached");
        Fx.SetReducedMotion(true); yield return Frames(2);
        Check(stone.CurrentOffset==Vector3.zero&&stone.CurrentScale==Vector3.one&&stone.CurrentAngles.sqrMagnitude<0.0001f&&Attached(stone)&&Motion.Miniature.Progress==1,
            "Changing reduced motion during a held inspection updates slab and miniature together");
        Fx.SetReducedMotion(false); yield return Frames(2);
        Check(Vector3.Distance(stone.CurrentOffset,lifted)<0.001f&&Attached(stone)&&stone.transform.localPosition==root,
            "Restoring motion reapplies the held pose without moving the quest slot");
        Flow.Back(); yield return Ready(); Flow.SelectQuest(stone.Offer.Quest);
        while(Motion.InspectionProgress<0.2f) yield return null;
        Fx.SetReducedMotion(true); yield return Frames(2); float p=Motion.InspectionProgress;
        Fx.SetReducedMotion(false); bool forward=true;
        while(Busy) { forward&=Motion.InspectionProgress>=p-0.0001f; p=Motion.InspectionProgress; yield return null; }
        Check(forward&&Motion.InspectionProgress==1&&Attached(stone),"Rapid preference changes cannot rewind an inspection already simplified to its final pose");
        Flow.Back(); yield return Frames(2); Fx.SetReducedMotion(true); yield return Frames(2); Fx.SetReducedMotion(false); yield return Ready();
        Check(AllHit()&&!Motion.Miniature.IsVisible&&stone.transform.localPosition==root,"Preference changes during return restore the exact board and all input");
        Flow.SelectQuest(stone.Offer.Quest); yield return Ready(); Flow.AcceptQuest(); yield return Frames(1);
        Fx.SetReducedMotion(true); yield return Ready(); var selected=Flow.Session.SelectedQuest;
        Flow.NextTorso(); yield return Frames(1); Fx.SetReducedMotion(false); yield return Ready();
        Check(Flow.Session.SelectedQuest==selected&&Flow.Session.SelectedTorso!=null&&Attached(stone),"Live preferences preserve mission/torso selections through forward and browse transitions");
        Flow.Back(); yield return Ready(); Flow.Back(); yield return Ready();
        Fx.Play(Vector3.zero,true); Fx.SetReducedMotion(true); Fx.SetReducedMotion(false);
        Check(!Fx.IsShaking&&!Fx.IsRecoiling&&Fx.CameraOffset==Vector3.zero,"Re-enabling motion cannot resurrect an impulse cancelled by reduced motion"); Fx.Cancel();
        QuestProgressionRuntime.StartNewProgression(catalogue,61); yield return Ready();
        for(int i=0;i<2;i++) { QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return Ready(); }
        QuestStoneBoardChecks.CompleteSmallForTest(Flow); while(!Motion.IsClearingMainArea) yield return null;
        Fx.SetReducedMotion(true); yield return Frames(2);
        var landing=Board.Stones.Where(s=>!s.IsMain).ToDictionary(s=>s.Offer.Slot,s=>s.transform.localPosition);
        Fx.SetReducedMotion(false); bool stayed=true;
        while(Motion.IsClearingMainArea) { stayed&=Board.Stones.Where(s=>!s.IsMain).All(s=>s.transform.localPosition==landing[s.Offer.Slot]); yield return null; }
        Check(stayed,"A simplified center-clearance movement never travels backward when motion is restored");
        while(!Motion.Shockwave.IsActive||Motion.Shockwave.Radius<2) yield return null;
        Fx.SetReducedMotion(true); yield return Frames(2);
        var final=Board.Stones.Where(s=>!s.IsMain).ToDictionary(s=>s.Offer.Slot,s=>s.transform.localPosition);
        Fx.SetReducedMotion(false); stayed=true;
        while(Busy) { stayed&=!Motion.Shockwave.IsVisible&&Board.Stones.Where(s=>!s.IsMain).All(s=>s.transform.localPosition==final[s.Offer.Slot]); yield return null; }
        Check(stayed&&AllHit(),"A suppressed shockwave stays settled and selectable after motion is restored");
        Check(Board.Progression.SuccessfulSmallCompletions==3&&Board.Stones.Count==7,"Preference changes never fabricate progression or remove available quests");
        for(int seed=80;seed<88;seed++)
        {
            QuestProgressionRuntime.StartNewProgression(catalogue,seed); yield return Frames(2); Flow.SkipPosting(); yield return Ready();
            Check(AllHit(),"Profiled seeded rebuild retains all independent selection areas, seed "+seed);
        }
        if(layoutMs.Count>0) Debug.Log("LAYOUT PROFILE MS: n="+layoutMs.Count+", mean="+layoutMs.Average().ToString("F3")+", max="+layoutMs.Max().ToString("F3"));
        if(terrainMs.Count>0) Debug.Log("TERRAIN PROFILE MS: n="+terrainMs.Count+", mean="+terrainMs.Average().ToString("F3")+", max="+terrainMs.Max().ToString("F3"));
        Check(layoutMs.Count>0&&terrainMs.Count>0,"Actual generation profiler samples were collected");
        layoutRecorder.enabled=terrainRecorder.enabled=false;
        QuestProgressionRuntime.StartNewProgression(catalogue,89); yield return Frames(2);
        while(!Motion.Terrain.IsRevealing||Motion.Terrain.RevealProgress<0.15f) yield return null;
        Fx.SetReducedMotion(true); yield return Frames(2);
        Check(Motion.Terrain.IsRevealed&&!Motion.Terrain.IsRevealing&&Motion.Terrain.RevealProgress==1,
            "Reducing motion during terrain emergence promptly resolves the complete raised landscape");
        Fx.SetReducedMotion(false); bool raised=true;
        while(Busy) { raised&=Motion.Terrain.IsRevealed&&Motion.Terrain.RevealProgress==1; yield return null; }
        Check(raised&&AllHit(),"Restoring motion never replays terrain emergence or blocks the remaining drops");
        Check(SceneManager.GetActiveScene().name=="PreMissionScene"&&Board.Progression.ActiveAttempt==null,"Final integration remains inside the pre-mission handoff boundary");
        Time.timeScale=1;
    }
    public static void BuildSmokePlayer()
    {
        string root=Directory.GetParent(Application.dataPath).FullName;
        if(!Application.isBatchMode||!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))
            throw new Exception("Build smoke checks only in a disposable temporary project");
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
        PlayerSettings.companyName="LocalPreMissionChecks"; PlayerSettings.productName="PreMissionP4Smoke";
        string output=Path.Combine(root,"P4PlayerSmoke","PreMissionSmoke.exe"); Directory.CreateDirectory(Path.GetDirectoryName(output));
        var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes=new[]{"Assets/Scenes/PreMissionScene.unity","Assets/Scenes/MainMenuScene.unity"},
            locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.Development,
            extraScriptingDefines=new[]{"PREMISSION_SMOKE_CHECKS"}
        });
        Debug.Log("PRE-MISSION PLAYER BUILD: "+report.summary.result+", seconds="+report.summary.totalTime.TotalSeconds+", bytes="+report.summary.totalSize);
        if(report.summary.result!=BuildResult.Succeeded) throw new Exception("Pre-mission smoke player build failed");
    }
}
