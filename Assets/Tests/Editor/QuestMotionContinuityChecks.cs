using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

// New-plan P1 checks: actual world/UI overlap, stable camera targets and recovery.
public static class QuestMotionContinuityChecks
{
    const string Pending="QuestMotionContinuityChecks.Pending";
    static readonly Stack<IEnumerator> frames=new Stack<IEnumerator>();
    static bool started;
    static int checks;
    static double deadline;
    static Mouse mouse;
    static Vector2 pointer;
    static string captures;
    static readonly HashSet<string> writtenCaptures=new HashSet<string>();
    static PreMissionFlowController Flow=>Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board=>Flow.StoneBoard;
    static PreMissionStoneMotion Motion=>Board.Motion;
    static PreMissionPaperPresentation UI=>Field<PreMissionPaperPresentation>(Flow,"presentation");
    static CanvasGroup Details=>Field<GameObject>(Flow,"detailsView").GetComponent<CanvasGroup>();
    static bool Busy=>Flow.IsPosting||Flow.IsTransitioning;
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.update+=Step;
    public static void RunBatch()
    {
        if(!Application.isBatchMode) throw new Exception("Disposable project only");
        GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline=null;
        EditorSceneManager.OpenScene("Assets/Scenes/PreMissionScene.unity");
        SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static T Field<T>(Object owner,string name) where T:Object=>(T)new SerializedObject(owner).FindProperty(name).objectReferenceValue;
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); checks++; Debug.Log("CONTINUITY PASS: "+message); }
    static bool CanUse(Button button) => button.gameObject.activeInHierarchy&&button.IsInteractable();
    static void Step()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        try
        {
            if(!started) { started=true; deadline=EditorApplication.timeSinceStartup+300; frames.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("Continuity checks timed out");
            while(frames.Count>0)
            {
                var current=frames.Peek();
                if(!current.MoveNext()) { (current as IDisposable)?.Dispose(); frames.Pop(); continue; }
                if(current.Current is IEnumerator nested) { frames.Push(nested); continue; }
                return;
            }
            InputSystem.onBeforeUpdate-=FeedPointer; PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false);
            Debug.Log("MOTION CONTINUITY CHECKS COMPLETE: "+checks+"; captures: "+captures); EditorApplication.Exit(0);
        }
        catch(Exception exception) { InputSystem.onBeforeUpdate-=FeedPointer; PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false); Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    static IEnumerator Wait(float seconds) { float until=Time.unscaledTime+seconds; while(Time.unscaledTime<until) yield return null; }
    static IEnumerator Ready() { yield return null; yield return null; while(Busy) yield return null; }
    static void FeedPointer() { if(mouse!=null&&mouse.added) InputSystem.QueueStateEvent(mouse,new MouseState { position=pointer }); }
    static void Pointer(QuestStoneView stone)
    {
        pointer=stone!=null ? (Vector2)Board.BoardCamera.WorldToScreenPoint(stone.HitCollider.bounds.center) : new Vector2(-100,-100);
        FeedPointer(); InputSystem.Update(); mouse.MakeCurrent();
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Time.timeScale=0; Application.runInBackground=true;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        foreach(var device in InputSystem.devices.Where(d=>d is Mouse||d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
        mouse=InputSystem.AddDevice<Mouse>(); pointer=new Vector2(-100,-100); InputSystem.onBeforeUpdate+=FeedPointer;
        captures=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"ContinuityCaptures"); Directory.CreateDirectory(captures);
        Flow.SkipPosting(); yield return Ready();
        Check(Flow.Session.Progression.SmallOfferCount==6&&Flow.Session.Progression.RequiredSmallCompletions==3
            &&new SerializedObject(Motion.ProgressTrack).FindProperty("cameraAttention").floatValue==0,
            "Six randomized offers, three-success unlock and stationary routine camera are preserved");
        for(int i=0;i<3;i++) { QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return Ready(); }
        Check(Board.Stones.Count==7&&Motion.Feedback.HeavyImpacts==1,"Three real successful outcomes still reveal one centered main among six side rocks");
        Check(new[]{"smallImpact","heavyImpact","handlingClip","progressClip","selectionClip","confirmationClip"}.All(n=>Field<AudioClip>(Motion.Feedback,n)==null),
            "Every audio hook remains unassigned");
        var run=Flow.Session.Progression; var ids=string.Join(";",run.Offers.Select(o=>o.Id));
        foreach(var stone in Board.Stones.ToArray())
        {
            var root=stone.transform.localPosition; var rotation=stone.transform.localRotation;
            var unrelated=Board.Stones.Where(s=>s!=stone).ToDictionary(s=>s.Offer.Id,s=>(s.transform.localPosition,s.transform.localRotation));
            Board.RefreshCameraFrame(); var home=Board.BoardCamera.transform.position; float homeSize=Board.BoardCamera.orthographicSize;
            Pointer(stone); yield return Wait(0.05f);
            var grabOffset=stone.CurrentOffset; var grabAngles=stone.CurrentAngles; var grabScale=stone.CurrentScale;
            Flow.SelectQuest(stone.Offer.Quest);
            Check(Vector3.Distance(stone.CurrentOffset,grabOffset)<0.0001f&&Vector3.Distance(stone.CurrentAngles,grabAngles)<0.001f
                &&Vector3.Distance(stone.CurrentScale,grabScale)<0.0001f,"Click continues from the actual partial-hover pose: "+stone.Offer.Quest.title);
            bool overlap=false, revealOverlap=false, controlsLocked=true, stableTarget=true, targetSeen=false;
            Vector3 target=default; float targetSize=0; int targetFrames=0;
            var groups=UI.GetType().GetField("briefingGroups",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(UI) as CanvasGroup[];
            while(Busy)
            {
                Board.RefreshCameraFrame(); float p=Motion.InspectionProgress;
                controlsLocked&=!CanUse(Field<Button>(Flow,"acceptButton"))&&!CanUse(Field<Button>(Flow,"declineButton"));
                if(Motion.IsTransitioning&&p<1&&Details.gameObject.activeSelf&&Details.alpha>0.01f)
                {
                    overlap=true; revealOverlap|=groups[0].alpha>0;
                    if(stone.Offer.Slot==0) Capture("inspection-overlap");
                }
                if(p>0.15f&&p<0.98f)
                {
                    var inferred=(Board.BoardCamera.transform.position-home*(1-p))/p;
                    float inferredSize=(Board.BoardCamera.orthographicSize-homeSize*(1-p))/p;
                    if(!targetSeen) { targetSeen=true; target=inferred; targetSize=inferredSize; }
                    stableTarget&=Vector3.Distance(target,inferred)<0.003f&&Mathf.Abs(targetSize-inferredSize)<0.003f; targetFrames++;
                }
                yield return null;
            }
            Check(overlap&&revealOverlap,"Camera approach, panel fade and title reveal overlap: "+stone.Offer.Quest.title);
            Check(stableTarget&&targetFrames>2,"Miniature emergence cannot retarget or resize the inspection camera mid-approach: "+stone.Offer.Quest.title);
            Check(controlsLocked&&CanUse(Field<Button>(Flow,"acceptButton"))&&CanUse(Field<Button>(Flow,"declineButton")),
                "Controls unlock once after both world and briefing finish: "+stone.Offer.Quest.title);
            Pointer(null); Flow.Back();
            Check(Motion.IsTransitioning&&UI.IsTransitioning,"Decline starts panel retraction and world return in the same frame: "+stone.Offer.Quest.title);
            bool returnOverlap=false, stableReturn=true;
            while(Busy)
            {
                Board.RefreshCameraFrame(); float p=Motion.InspectionProgress;
                if(p>0.15f&&p<0.98f)
                {
                    var inferred=(Board.BoardCamera.transform.position-home*(1-p))/p;
                    stableReturn&=Vector3.Distance(target,inferred)<0.003f;
                    returnOverlap|=Details.alpha>0.01f&&Details.alpha<0.99f;
                    if(returnOverlap&&stone.Offer.Slot==0) Capture("return-overlap");
                }
                yield return null;
            }
            Check(returnOverlap&&stableReturn,"Panel retraction overlaps a stable camera return as the miniature retracts: "+stone.Offer.Quest.title);
            Check(stone.transform.localPosition==root&&stone.transform.localRotation==rotation&&stone.CurrentOffset==Vector3.zero
                &&stone.CurrentAngles.sqrMagnitude<0.0001f&&stone.CurrentScale==Vector3.one&&stone.HitCollider.enabled&&!Motion.Miniature.IsVisible,
                "Return restores the exact root, visual, hit area and inscription: "+stone.Offer.Quest.title);
            Check(unrelated.All(p=>Board.FindStone(p.Key).transform.localPosition==p.Value.Item1&&Board.FindStone(p.Key).transform.localRotation==p.Value.Item2)
                &&Vector3.Distance(Board.BoardCamera.transform.position,home)<0.001f,"Other rocks and overhead framing do not drift: "+stone.Offer.Quest.title);
        }
        Check(run.SuccessfulSmallCompletions==3&&string.Join(";",run.Offers.Select(o=>o.Id))==ids,"Repeated overlapping interactions do not change offers or award progression");
        var selected=Board.Stones[0]; Flow.SelectQuest(selected.Offer.Quest);
        while(!Details.gameObject.activeSelf || Motion.InspectionProgress>=1) yield return null;
        Flow.enabled=false; Flow.enabled=true; yield return Ready();
        Check(!Busy&&Flow.Session.View==PreMissionView.Details&&Motion.Miniature.Progress==1&&Details.alpha==1,"Disabling during overlapping entry settles both systems into one valid briefing");
        Pointer(null); Flow.Back(); yield return null; Flow.enabled=false; Flow.enabled=true; yield return Ready();
        Check(Board.InputEnabled&&Flow.Session.View==PreMissionView.Board&&!Motion.Miniature.IsVisible,"Disabling during overlapping return settles one valid board");
        yield return Wait(1.2f);
        Check(Board.InputEnabled&&!Busy&&Flow.Session.View==PreMissionView.Board,"Cancelled overlap callbacks cannot reopen an obsolete briefing later");
        Motion.Feedback.SetReducedMotion(true);
        Flow.SelectQuest(selected.Offer.Quest); yield return Ready();
        Check(Details.alpha==1&&selected.CurrentOffset==Vector3.zero&&Motion.Miniature.IsVisible,"Reduced motion preserves the readable briefing without decorative stone movement");
        Flow.AcceptQuest(); yield return Ready(); Flow.NextTorso(); yield return Ready();
        var torso=Flow.Session.SelectedTorso; var quest=Flow.Session.SelectedQuest;
        var original=Field<MissionStartHook>(Flow,"missionStartHook"); var recording=Flow.gameObject.AddComponent<PreMissionRecordingHook>();
        var fields=new SerializedObject(Flow); fields.FindProperty("missionStartHook").objectReferenceValue=recording; fields.ApplyModifiedPropertiesWithoutUndo();
        Flow.ConfirmTorso(); Flow.ConfirmTorso(); yield return Ready();
        Check(recording.Calls==1&&recording.Quest==quest&&recording.Torso==torso&&run.ActiveAttempt==null&&SceneManager.GetActiveScene().name=="PreMissionScene",
            "One exact quest/torso handoff remains exposed without gameplay or scene loading");
        fields.Update(); fields.FindProperty("missionStartHook").objectReferenceValue=original; fields.ApplyModifiedPropertiesWithoutUndo(); Object.Destroy(recording);
        Flow.Back(); yield return Ready(); Flow.Back(); yield return Ready();
        Check(Board.InputEnabled&&!Motion.Miniature.IsVisible&&Board.Stones.All(s=>s.CurrentOffset==Vector3.zero),"Reduced-motion overlapping return restores all selection and exact rests");
        Motion.Feedback.SetReducedMotion(false); InputSystem.onBeforeUpdate-=FeedPointer; InputSystem.RemoveDevice(mouse); Time.timeScale=1;
    }
    static void Capture(string name)
    {
        if(!writtenCaptures.Add(name)) return;
        string path=Path.Combine(captures,name+".png");
        var camera=Board.BoardCamera; var oldTarget=camera.targetTexture; int mask=camera.cullingMask;
        var canvases=Object.FindObjectsByType<Canvas>().Where(c=>c.isRootCanvas).ToArray();
        var modes=canvases.Select(c=>c.renderMode).ToArray(); var cameras=canvases.Select(c=>c.worldCamera).ToArray(); var distances=canvases.Select(c=>c.planeDistance).ToArray();
        var target=new RenderTexture(1600,900,24); var oldActive=RenderTexture.active; var texture=new Texture2D(1600,900,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target; camera.cullingMask|=(1<<0)|(1<<5);
            foreach(var canvas in canvases) { canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest { destination=target });
            RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,1600,900),0,0); texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=oldActive; camera.targetTexture=oldTarget; camera.cullingMask=mask;
            for(int i=0;i<canvases.Length;i++) { canvases[i].renderMode=modes[i]; canvases[i].worldCamera=cameras[i]; canvases[i].planeDistance=distances[i]; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Object.Destroy(target); Object.Destroy(texture);
        }
    }
}
