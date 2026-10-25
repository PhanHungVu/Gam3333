using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Runs in a disposable project: fixture outcomes are data API calls, never shipped cheats.
public static class QuestProgressTrackChecks
{
    const string Pending="QuestProgressTrackChecks.Pending";
    static IEnumerator routine;
    static readonly Stack<IEnumerator> frames=new Stack<IEnumerator>();
    static double deadline;
    static int checks;
    static string captures;
    static PreMissionFlowController Flow=>Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board=>Flow.StoneBoard;
    static PreMissionStoneMotion Motion=>Board.Motion;
    static PreMissionProgressTrack Track=>Motion.ProgressTrack;
    static bool Busy=>Flow.IsPosting||Flow.IsTransitioning;
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.update+=Step;
    public static void RunBatch()
    {
        if(!Application.isBatchMode) throw new Exception("Disposable project only");
        GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline=null;
        PreMissionProgressTrackSetup.UpgradeAssets(); PreMissionProgressTrackSetup.UpgradeAssets();
        if(Object.FindObjectsByType<PreMissionProgressTrack>(FindObjectsInactive.Include).Length!=1) throw new Exception("Repeated P2 upgrade duplicated the track");
        SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Step()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        try
        {
            if(routine==null) { deadline=EditorApplication.timeSinceStartup+300; routine=Run(); frames.Push(routine); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("P2 checks timed out");
            if(Advance()) return;
            PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false);
            Debug.Log("PROGRESS TRACK CHECKS COMPLETE: "+checks+"; captures: "+captures); EditorApplication.Exit(0);
        }
        catch(Exception exception) { PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false); Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    static bool Advance()
    {
        while(frames.Count>0)
        {
            var current=frames.Peek();
            if(!current.MoveNext()) { (current as IDisposable)?.Dispose(); frames.Pop(); continue; }
            if(current.Current is IEnumerator nested) { frames.Push(nested); continue; }
            return true;
        }
        return false;
    }
    static void Check(bool value,string message) { if(!value) throw new Exception(message); checks++; Debug.Log("PROGRESS TRACK PASS: "+message); }
    static T Field<T>(Object target,string name) where T:Object=>(T)new SerializedObject(target).FindProperty(name).objectReferenceValue;
    static IEnumerator Wait(float seconds) { float until=Time.unscaledTime+seconds; while(Time.unscaledTime<until) yield return null; }
    static QuestMissionAttempt Complete(QuestProgressionSession run,PartData torso)
    {
        if(!run.TryBeginMission(run.Offers.First(o=>o.Quest.category==QuestCategory.Side).Quest,torso,out var attempt)
            || !run.ReportMissionResult(attempt.Id,QuestMissionOutcome.Succeeded)) throw new Exception("Unable to complete fixture mission");
        return attempt;
    }
    static void Capture(string name,int width=1600,int height=900)
    {
        var camera=Board.BoardCamera; var oldTarget=camera.targetTexture; int mask=camera.cullingMask;
        var canvases=Object.FindObjectsByType<Canvas>().Where(c=>c.isRootCanvas).ToArray();
        var modes=canvases.Select(c=>c.renderMode).ToArray(); var cameras=canvases.Select(c=>c.worldCamera).ToArray(); var distances=canvases.Select(c=>c.planeDistance).ToArray();
        var target=new RenderTexture(width,height,24); var oldActive=RenderTexture.active; var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target; camera.cullingMask|=(1<<0)|(1<<5); Board.RefreshCameraFrame();
            foreach(var canvas in canvases) { canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest { destination=target });
            RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply();
            Check(texture.GetPixels32().Count(p=>p.r>220&&p.b>220&&p.g<70)<width*height/200,name+": no missing shaders");
            File.WriteAllBytes(Path.Combine(captures,name+".png"),texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=oldActive; camera.targetTexture=oldTarget; camera.cullingMask=mask;
            for(int i=0;i<canvases.Length;i++) { canvases[i].renderMode=modes[i]; canvases[i].worldCamera=cameras[i]; canvases[i].planeDistance=distances[i]; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Object.Destroy(target); Object.Destroy(texture);
        }
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Time.timeScale=0; Application.runInBackground=true;
        captures=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"P2Captures"); Directory.CreateDirectory(captures);
        while(!Track.IsRevealing) yield return null;
        Check(!QuestProgressionRuntime.WasProgressTrackRevealed(Flow.Session.Progression)&&Track.ShownStep==0,"First physical reveal starts without awarding progress or prematurely recording presentation");
        Capture("p2-first-map-reveal");
        while(Busy) yield return null;
        var run=Flow.Session.Progression; var torso=Flow.Session.SelectedTorso;
        Check(Track.IsRevealed&&Track.NodeCount==4&&Track.ShownStep==0&&Track.Marker.localPosition==Track.NodePosition(0),"Fresh three-success track has a start plus three steps and a correctly seated marker");
        Check(Track.GetComponentsInChildren<Collider>(true).Length==0&&Track.GetComponentsInChildren<Rigidbody>(true).Length==0,"Map and marker have no physics or raycast blockers");
        Check(QuestProgressionRuntime.WasProgressTrackRevealed(run)&&QuestProgressionRuntime.LastShownProgressStep(run)==0,"Finished first reveal is remembered within the existing play session");
        Capture("p2-zero-progress");

        Flow.SelectQuest(run.Offers[1].Quest); while(Busy) yield return null; Flow.AcceptQuest(); while(Busy) yield return null;
        Flow.ConfirmTorso(); Flow.ConfirmTorso(); while(Busy) yield return null;
        Check(run.ActiveAttempt==null&&run.SuccessfulSmallCompletions==0&&Track.ShownStep==0&&SceneManager.GetActiveScene().name=="PreMissionScene",
            "Selection, acceptance and empty-hook confirmation cannot move the marker or launch gameplay");
        Flow.Back(); while(Busy) yield return null; Flow.Back(); while(Busy) yield return null;
        foreach(var outcome in new[]{QuestMissionOutcome.Failed,QuestMissionOutcome.Abandoned})
        {
            run.TryBeginMission(run.Offers[0].Quest,torso,out var failed); run.ReportMissionResult(failed.Id,outcome); yield return null; yield return null; while(Busy) yield return null;
            Check(Track.ShownStep==0&&!Track.HasPending&&run.SuccessfulSmallCompletions==0,"No marker advancement for "+outcome);
        }
        var unchanged=Board.Stones.Skip(1).ToDictionary(s=>s.Offer.Id,s=>s.transform.localPosition);
        var sequence=new List<string>(); Track.StepPresented+=step=>sequence.Add("step"+step);
        var completed=Complete(run,torso); yield return null; yield return null;
        while(!Track.IsAdvancing) yield return null;
        yield return Wait(0.12f);
        Check(Track.ShownStep==0&&Track.TargetStep==1&&Track.Marker.localPosition!=Track.NodePosition(0)&&Track.CameraFocus>0,
            "A real successful result visibly moves the marker and directs camera attention before committing the shown step");
        Capture("p2-marker-moving"); while(Busy) yield return null;
        Check(Track.ShownStep==1&&sequence.SequenceEqual(new[]{"step1"})&&QuestProgressionRuntime.LastShownProgressStep(run)==1,
            "Success presents and remembers exactly one marker step");
        Check(!run.ReportMissionResult(completed.Id,QuestMissionOutcome.Succeeded)&&Track.ShownStep==1&&run.SuccessfulSmallCompletions==1,"Duplicate mission result cannot advance progress twice");
        Check(unchanged.All(p=>Board.FindStone(p.Key).transform.localPosition==p.Value),"Marker movement does not relocate unrelated quest offers");
        Capture("p2-one-progress");

        Flow.Back(); yield return null; yield return null;
        Check(SceneManager.GetActiveScene().name=="MainMenuScene"&&QuestProgressionRuntime.LastShownProgressStep(run)==1,"Menu return retains the displayed marker step");
        Complete(run,torso); Complete(run,torso);
        Check(run.SuccessfulSmallCompletions==3&&QuestProgressionRuntime.LastShownProgressStep(run)==1&&run.BigQuest!=null,"Off-board successes unlock through data while visual catch-up remains pending");
        SceneManager.LoadScene("PreMissionScene"); yield return null; yield return null;
        sequence.Clear(); Track.StepPresented+=step=>sequence.Add("step"+step); Motion.Impacted+=stone=>{ if(stone.IsMain) sequence.Add("main"); };
        Check(Track.ShownStep==1&&Track.IsRevealed&&!Track.IsRevealing,"Scene return restores the last presented step without replaying the map reveal");
        while(!Motion.IsAnticipatingMain) yield return null;
        Check(Track.ShownStep==3&&sequence.SequenceEqual(new[]{"step2","step3"})&&Track.CameraFocus>0,"Missed steps catch up in order and hand camera attention directly to the main reveal");
        Capture("p2-final-step-before-main"); while(Busy) yield return null;
        Check(sequence.SequenceEqual(new[]{"step2","step3","main"})&&Track.ShownStep==3&&Track.CameraFocus==0&&Board.Stones.Count==4,"Final marker acknowledgement precedes one main impact and restores the board camera");
        Check(Board.Stones.All(s=>Board.StoneAt(Board.BoardCamera.WorldToScreenPoint(s.HitCollider.bounds.center))==s),"The progress track imposes no main-first or route restriction on selection");
        foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1600,900),new Vector2Int(1920,1080),new Vector2Int(1024,768)}) Capture("p2-unlocked-"+size.x,size.x,size.y);
        Complete(run,torso); yield return null; yield return null; while(Busy) yield return null;
        Check(run.SuccessfulSmallCompletions==4&&Track.ShownStep==3&&Track.NodeCount==4,"Further small successes keep the completed physical track at its threshold");
        Flow.Back(); yield return null; yield return null; SceneManager.LoadScene("PreMissionScene"); yield return null; yield return null; while(Busy) yield return null;
        Check(Track.ShownStep==3&&!Track.HasPending&&Motion.Feedback.HeavyImpacts==0,"Completed track and main cinematic do not replay after another scene return");

        Flow.StartNewProgression(); while(Busy) yield return null;
        run=Flow.Session.Progression;
        Flow.SelectQuest(run.Offers[1].Quest); while(Busy) yield return null; Flow.AcceptQuest(); while(Busy) yield return null;
        var selected=Flow.Session.SelectedQuest; Complete(run,torso); yield return Wait(0.2f);
        Check(Track.ShownStep==0&&!Track.IsAdvancing&&Flow.Session.SelectedQuest==selected&&Flow.Session.View==PreMissionView.Torso,
            "A result received during torso selection waits without moving the map behind the briefing");
        Flow.Back(); while(Busy) yield return null; Flow.Back(); while(Busy) yield return null; yield return null; while(Busy) yield return null;
        Check(Track.ShownStep==1&&Flow.Session.View==PreMissionView.Board,"Returning to the board presents the queued progression step");

        // Exit partway through marker movement: do not consume an unseen destination.
        Flow.StartNewProgression(); while(Busy) yield return null; run=Flow.Session.Progression;
        Complete(run,torso); yield return null; yield return null; while(!Track.IsAdvancing) yield return null;
        yield return Wait(0.10f); Flow.Back(); yield return null; yield return null;
        Check(SceneManager.GetActiveScene().name=="MainMenuScene"&&QuestProgressionRuntime.LastShownProgressStep(run)==0,"Leaving during advancement preserves the last completed presentation step");
        SceneManager.LoadScene("PreMissionScene"); yield return null; yield return null; while(Busy) yield return null;
        Check(Track.ShownStep==1&&Motion.Feedback.SmallImpacts==0,"Pending map advancement resumes even when no stone arrival is pending");

        Flow.StartNewProgression(); yield return null; while(!Track.IsRevealing) yield return null; Flow.SkipPosting();
        Check(!Busy&&Track.IsRevealed&&Track.ShownStep==0&&Track.CameraFocus==0&&Board.Stones.All(s=>s.IsReady),"Skipping the map introduction settles both track and queued stones");
        yield return null;
        for(int i=0;i<3;i++)
        {
            Complete(Flow.Session.Progression,torso); yield return null; yield return null;
            if(i<2) while(Busy) yield return null;
        }
        while(!Track.IsAdvancing) yield return null;
        int heavyBefore=Motion.Feedback.HeavyImpacts; Flow.SkipPosting();
        Check(!Busy&&Track.ShownStep==3&&Track.CameraFocus==0&&Motion.Feedback.HeavyImpacts==heavyBefore
            &&QuestProgressionRuntime.WasPresented(Flow.Session.Progression.BigQuest.Id),"Skipping final advancement completes the map and main presentation without fabricated impacts");
        yield return null;

        Flow.StartNewProgression(); while(Busy) yield return null; Complete(Flow.Session.Progression,torso); yield return null; yield return null;
        while(!Track.IsAdvancing) yield return null;
        Track.enabled=false;
        Check(!Board.InputEnabled&&!Busy&&Track.ShownStep==1&&Track.CameraFocus==0,"Disabling the track cancels its coordinated animation and locks world input safely");
        Track.enabled=true; yield return null;
        Check(Board.InputEnabled&&Track.Marker.localPosition==Track.NodePosition(1)&&!Track.HasPending,"Re-enabling restores one valid settled track without replaying movement");

        var catalogue=Field<QuestBoardCatalogue>(Flow,"catalogue");
        foreach(int requirement in new[]{5,1})
        {
            catalogue.smallCompletionsToUnlockBig=requirement; Flow.StartNewProgression(); while(Busy) yield return null;
            Check(Track.RequiredSteps==requirement&&Track.NodeCount==requirement+1&&Track.ShownStep==0,"Track geometry reads configured requirement "+requirement);
            for(int i=0;i<requirement;i++) { Complete(Flow.Session.Progression,torso); yield return null; yield return null; while(Busy) yield return null; }
            Check(Track.ShownStep==requirement&&Flow.Session.Progression.BigQuest!=null,"Marker and main unlock agree with configured requirement "+requirement);
            Capture("p2-configured-"+requirement);
        }
        catalogue.smallCompletionsToUnlockBig=3; Flow.StartNewProgression(); while(Busy) yield return null;
        Check(Track.NodeCount==4&&Track.ShownStep==0&&QuestProgressionRuntime.LastShownProgressStep(Flow.Session.Progression)==0,"Explicit new run clears presentation watermarks and restores the shipped three-step track");
        Time.timeScale=1;
    }
}
