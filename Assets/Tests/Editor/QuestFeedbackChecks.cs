using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object=UnityEngine.Object;

// Integration checks run only in an isolated copy; never mutate live authoring fixtures.
public static class QuestFeedbackChecks
{
    const string Pending="QuestFeedbackChecks.Pending", Count="QuestFeedbackChecks.Count";
    static readonly Stack<IEnumerator> frames=new Stack<IEnumerator>();
    static bool started;
    static int checks;
    static double deadline;
    static Mouse mouse;
    static Vector2 pointer;
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
        PreMissionFeedbackSetup.UpgradeAssets();
        string first=File.ReadAllText(PreMissionStoneBoardSetup.BoardPath);
        PreMissionFeedbackSetup.UpgradeAssets();
        Check(first==File.ReadAllText(PreMissionStoneBoardSetup.BoardPath),"Repeated P4 upgrade is idempotent and preserves authored settings");
        EditorSceneManager.OpenScene("Assets/Scenes/PreMissionScene.unity");
        SessionState.SetInt(Count,checks); SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); checks++; Debug.Log("P4 PASS: "+message); }
    static T Field<T>(Object owner,string name) where T:Object=>(T)new SerializedObject(owner).FindProperty(name).objectReferenceValue;
    static void Step()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        try
        {
            if(!started) { started=true; checks=SessionState.GetInt(Count,0); deadline=EditorApplication.timeSinceStartup+240; frames.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("P4 checks timed out");
            while(frames.Count>0)
            {
                var current=frames.Peek();
                if(!current.MoveNext()) { (current as IDisposable)?.Dispose(); frames.Pop(); continue; }
                if(current.Current is IEnumerator nested) { frames.Push(nested); continue; }
                return;
            }
            InputSystem.onBeforeUpdate-=FeedPointer;
            PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false);
            Debug.Log("P4 FEEDBACK CHECKS COMPLETE: "+checks); EditorApplication.Exit(0);
        }
        catch(Exception exception) { InputSystem.onBeforeUpdate-=FeedPointer; PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false); Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    static IEnumerator Wait(float seconds) { float until=Time.unscaledTime+seconds; while(Time.unscaledTime<until) yield return null; }
    static void FeedPointer() { if(mouse!=null&&mouse.added) InputSystem.QueueStateEvent(mouse,new MouseState { position=pointer }); }
    static void Pointer(QuestStoneView stone)
    {
        pointer=stone!=null ? (Vector2)Board.BoardCamera.WorldToScreenPoint(stone.HitCollider.bounds.center) : new Vector2(-100,-100);
        FeedPointer(); InputSystem.Update(); mouse.MakeCurrent();
    }
    static IEnumerator Ready() { yield return null; yield return null; while(Busy) yield return null; }
    static void Stable(string label)
    {
        Pointer(null); Board.ClearHighlights();
        Check(Board.Stones.All(s=>s.IsReady&&!s.IsMoving&&!s.IsReacting&&s.CurrentOffset==Vector3.zero&&s.CurrentAngles.sqrMagnitude<0.0001f&&s.HitCollider.enabled),label+": exact visual rests and colliders");
        Check(Board.InputEnabled&&!Fx.IsShaking&&!Fx.IsRecoiling&&Motion.Inspected==null&&!Motion.Miniature.IsVisible,label+": all input restored, effects and inspection cleared");
        Check(Board.Stones.All(s=>Board.StoneAt(Board.BoardCamera.WorldToScreenPoint(s.HitCollider.bounds.center))==s),label+": every available quest raycasts independently");
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Time.timeScale=0; Application.runInBackground=true;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        foreach(var device in InputSystem.devices.Where(d=>d is Mouse||d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
        mouse=InputSystem.AddDevice<Mouse>(); pointer=new Vector2(-100,-100); InputSystem.onBeforeUpdate+=FeedPointer;
        yield return Ready();
        var impact=Field<AudioSource>(Fx,"impactAudio"); var interaction=Field<AudioSource>(Fx,"interactionAudio");
        Check(impact!=interaction&&Board.GetComponentsInChildren<AudioSource>(true).Length==2,"Two reusable sources separate impact tails from interaction cues");
        Check(Board.GetComponentsInChildren<ParticleSystem>(true).Length==3&&Fx.GetComponentsInChildren<Rigidbody>(true).Length==0,"Existing three reusable particle systems retained without physical debris");
        var roles=new[]{"smallImpact","heavyImpact","handlingClip","progressClip","selectionClip","confirmationClip"};
        Check(roles.All(n=>Field<AudioClip>(Fx,n)==null),"All six shipped audio hooks remain empty after repeated setup");
        Fx.Play(Vector3.zero,false); Fx.Play(Vector3.zero,true);
        foreach(var cue in (QuestBoardImpactFeedback.Cue[])Enum.GetValues(typeof(QuestBoardImpactFeedback.Cue))) Fx.PlayCue(cue);
        Check(!impact.isPlaying&&!interaction.isPlaying,"Unconnected audio hooks safely stay silent for impacts and every interaction");
        Fx.Cancel();
        // A silent in-memory fixture proves future external clips can connect; no sound asset is saved.
        var connectedClip=AudioClip.Create("Silent connection check",2205,1,22050,false);
        var audioFields=new SerializedObject(Fx);
        foreach(var role in roles) audioFields.FindProperty(role).objectReferenceValue=connectedClip;
        audioFields.ApplyModifiedPropertiesWithoutUndo();
        var randomBefore=UnityEngine.Random.state;
        Fx.Play(Vector3.zero,false); float pitch=impact.pitch; Fx.Play(Vector3.zero,false);
        Check(impact.pitch!=pitch&&impact.pitch>=0.9f&&impact.pitch<=1.1f&&UnityEngine.Random.state.Equals(randomBefore),"Repeated small contacts vary pitch without consuming gameplay random state");
        Fx.Cancel(); Fx.Play(Vector3.zero,true); float heavyPitch=impact.pitch;
        Vector3 impulse=Fx.CameraOffset; Fx.Play(Vector3.zero,false);
        Check(Fx.IsRecoiling&&Fx.CameraOffset==impulse&&impact.pitch==heavyPitch,"Light overlap cannot restart, dilute or repitch a heavy response");
        Fx.SetCameraShakeScale(0);
        Check(!Fx.IsShaking&&Fx.CameraOffset==Vector3.zero&&Fx.CameraRoll==0&&Fx.IsRecoiling,"Zero shake removes only the camera impulse, retaining physical board response");
        Fx.SetEffectsVolume(0); Fx.PlayCue(QuestBoardImpactFeedback.Cue.Accept);
        Check(impact.volume==0&&interaction.volume==0,"Effects mute immediately silences both active sources");
        Fx.SetParticleScale(0); Fx.Play(Vector3.zero,true);
        Check(Fx.SmallDust.particleCount+Fx.HeavyDust.particleCount+Fx.ImpactChips.particleCount==0,"Zero particle amount clears active effects and suppresses later emissions");
        Fx.SetEffectsVolume(1); Fx.SetParticleScale(1); Fx.SetCameraShakeScale(1); Fx.Cancel();
        Fx.SetCameraShakeScale(0.5f); Fx.SetCameraShakeScale(1);
        Check(Fx.CameraOffset==Vector3.zero&&Fx.CameraRoll==0,"Changing shake gain while idle cannot resurrect a cancelled impulse");
        var home=Board.BoardCamera.transform.position; var rotation=Board.BoardCamera.transform.rotation;
        Fx.SetReducedMotion(true); Flow.StartNewProgression();
        bool reducedStable=true;
        while(Busy)
        {
            reducedStable&=Fx.CameraOffset==Vector3.zero&&!Fx.IsRecoiling&&Board.Stones.All(s=>s.CurrentOffset==Vector3.zero&&s.CurrentAngles.sqrMagnitude<0.0001f);
            yield return null;
        }
        Check(reducedStable&&Vector3.Distance(home,Board.BoardCamera.transform.position)<0.001f&&Quaternion.Angle(rotation,Board.BoardCamera.transform.rotation)<0.001f,
            "Reduced entry suppresses drops, recoil and camera excursions while paused");
        Stable("Reduced entry");
        var stone=Board.Stones[0]; Pointer(stone); yield return Wait(0.25f);
        Check(stone.IsHighlighted&&stone.CurrentOffset==Vector3.zero&&stone.CurrentAngles.sqrMagnitude<0.0001f&&stone.CurrentScale==Vector3.one,"Reduced hover preserves highlight/ring with no lift, tilt or scale pulse");
        int cues=Fx.InteractionCount; Flow.SelectQuest(stone.Offer.Quest); Flow.SelectQuest(Board.Stones[1].Offer.Quest); Flow.Back();
        Check(Fx.InteractionCount==cues+1&&Fx.LastCue==QuestBoardImpactFeedback.Cue.Pickup,"Repeated selection and Escape during approach acknowledge one pickup");
        yield return Ready();
        Check(Motion.Miniature.Progress==1&&Motion.Miniature.Quest==stone.Offer.Quest&&stone.CurrentOffset==Vector3.zero,"Reduced inspection keeps the real objective and stable rock readable");
        Capture("p4-reduced-details",1024,768);
        Flow.AcceptQuest(); Flow.AcceptQuest(); yield return Ready();
        Check(Fx.LastCue==QuestBoardImpactFeedback.Cue.Accept&&Flow.Session.View==PreMissionView.Torso,"Accept produces one deliberate cue and reaches torso selection");
        var torso=Flow.Session.SelectedTorso; cues=Fx.InteractionCount;
        Flow.NextTorso(); Flow.NextTorso(); Flow.PreviousTorso(); yield return Ready();
        Check(Fx.InteractionCount==cues+1&&Fx.LastCue==QuestBoardImpactFeedback.Cue.Torso&&Flow.Session.SelectedTorso!=torso,"Rapid torso inputs perform one data change and one cue");
        var original=Field<MissionStartHook>(Flow,"missionStartHook"); var recording=Flow.gameObject.AddComponent<PreMissionRecordingHook>();
        var fields=new SerializedObject(Flow); fields.FindProperty("missionStartHook").objectReferenceValue=recording; fields.ApplyModifiedPropertiesWithoutUndo();
        var selected=Flow.Session.SelectedQuest; torso=Flow.Session.SelectedTorso; cues=Fx.InteractionCount;
        Flow.ConfirmTorso(); Flow.ConfirmTorso(); yield return Ready();
        Check(Fx.InteractionCount==cues+1&&Fx.LastCue==QuestBoardImpactFeedback.Cue.Confirm&&recording.Calls==1&&recording.Quest==selected&&recording.Torso==torso,
            "Repeated confirmation emits one cue and submits exact quest/torso once");
        Check(Flow.Session.Progression.ActiveAttempt==null&&SceneManager.GetActiveScene().name=="PreMissionScene","P4 never starts gameplay or loads a gameplay scene");
        fields.Update(); fields.FindProperty("missionStartHook").objectReferenceValue=original; fields.ApplyModifiedPropertiesWithoutUndo(); Object.Destroy(recording);
        Flow.Back(); yield return Ready(); Flow.Back(); yield return Ready();
        Check(Fx.LastCue==QuestBoardImpactFeedback.Cue.Return,"Stone return acknowledges actual contact without replaying an arrival");
        Stable("Reduced return");
        var run=Flow.Session.Progression; var unchanged=Board.Stones.Skip(1).ToDictionary(s=>s.Offer.Id,s=>s.transform.localPosition);
        int heavy=Fx.HeavyImpacts; int progress=Fx.InteractionCount;
        for(int win=1;win<=3;win++)
        {
            Pointer(null); Check(QuestProgressionTools.CompleteSmallQuest(Flow),"Inspector completion remains usable, win "+win); yield return Ready();
            Check(run.SuccessfulSmallCompletions==win&&Motion.ProgressTrack.ShownStep==win&&Motion.ProgressTrack.CameraFocus==0,"Reduced progress agrees with successful result, win "+win);
        }
        Check(Fx.InteractionCount==progress+3&&run.BigQuest!=null&&Fx.HeavyImpacts==heavy+1,"Three actual successes create three marker cues and one main contact");
        Check(Fx.HeavyDust.particleCount+Fx.SmallDust.particleCount+Fx.ImpactChips.particleCount==0&&unchanged.All(p=>Board.FindStone(p.Key).transform.localPosition==p.Value),
            "Reduced main reveal emits no debris and preserves unrelated slots");
        Stable("Reduced main reveal"); Capture("p4-reduced-board",1600,900);
        // Change preference in the middle of real heavy feedback and object motion.
        Fx.SetReducedMotion(false); Flow.StartNewProgression(); yield return Ready();
        for(int i=0;i<2;i++) { QuestProgressionTools.CompleteSmallQuest(Flow); yield return Ready(); }
        QuestProgressionTools.CompleteSmallQuest(Flow); yield return null; yield return null;
        while(!Fx.IsRecoiling) yield return null;
        Fx.SetReducedMotion(true); yield return null;
        Check(!Fx.IsShaking&&!Fx.IsRecoiling&&Fx.CameraOffset==Vector3.zero&&Fx.HeavyDust.particleCount==0,"Enabling reduced motion during heavy impact clears active camera, board and particles");
        yield return Ready(); Stable("Changed preference during impact");
        Fx.SetReducedMotion(false); stone=Board.Stones[0]; Flow.SelectQuest(stone.Offer.Quest); yield return Wait(0.2f);
        Fx.SetReducedMotion(true); Flow.enabled=false; Flow.enabled=true; yield return Ready();
        Check(Flow.Session.View==PreMissionView.Details&&Motion.Miniature.Progress==1&&!Busy,"Interruption during approach settles the target briefing without stale callbacks");
        Flow.Back(); yield return Ready(); Stable("Interrupted approach return");
        Fx.SetReducedMotion(false); Flow.StartNewProgression(); yield return null; Flow.SkipPosting(); yield return null;
        for(int i=0;i<2;i++) { QuestProgressionTools.CompleteSmallQuest(Flow); yield return Ready(); }
        QuestProgressionTools.CompleteSmallQuest(Flow); yield return null; yield return null;
        while(!Motion.IsAnticipatingMain) yield return null;
        heavy=Fx.HeavyImpacts; cues=Fx.InteractionCount; Flow.SkipPosting(); yield return null;
        Check(Fx.HeavyImpacts==heavy&&Fx.InteractionCount==cues&&QuestProgressionRuntime.WasPresented(Flow.Session.Progression.BigQuest.Id),"Skipping the main anticipation neither fabricates sound/contact nor leaves it unpresented");
        Stable("Skipped main");
        var beforeRun=Flow.Session.Progression; var ids=string.Join(";",beforeRun.Offers.Select(o=>o.Id)); heavy=Fx.HeavyImpacts;
        SceneManager.LoadScene("PreMissionScene"); yield return Ready();
        Check(Flow.Session.Progression==beforeRun&&string.Join(";",beforeRun.Offers.Select(o=>o.Id))==ids&&Fx.HeavyImpacts==0,
            "Scene return retains offers/progress without replaying the main contact or its sound");
        Stable("Scene return");
        Fx.Play(Vector3.zero,true); var survivingFx=Fx; var survivingBoard=Board;
        Object.Destroy(Flow.gameObject); yield return null; yield return null;
        Check(!survivingFx.IsShaking&&!survivingFx.IsRecoiling&&!survivingBoard.InputEnabled
            &&!Field<AudioSource>(survivingFx,"impactAudio").isPlaying&&!Field<AudioSource>(survivingFx,"interactionAudio").isPlaying
            &&survivingFx.HeavyDust.particleCount+survivingFx.ImpactChips.particleCount==0,"UI unload cancels sound, particles, motion and orphan board input");
        Object.Destroy(connectedClip);
        InputSystem.RemoveDevice(mouse); Time.timeScale=1;
    }
    static void Capture(string name,int width,int height)
    {
        var camera=Board.BoardCamera; var oldTarget=camera.targetTexture; int mask=camera.cullingMask;
        var canvases=Object.FindObjectsByType<Canvas>().Where(c=>c.isRootCanvas).ToArray();
        var modes=canvases.Select(c=>c.renderMode).ToArray(); var cameras=canvases.Select(c=>c.worldCamera).ToArray(); var distances=canvases.Select(c=>c.planeDistance).ToArray();
        var target=new RenderTexture(width,height,24); var oldActive=RenderTexture.active; var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target; camera.cullingMask|=(1<<0)|(1<<5);
            foreach(var canvas in canvases) { canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest { destination=target });
            RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply();
            string directory=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"P4Captures"); Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory,name+".png"),texture.EncodeToPNG());
            Check(texture.GetPixels32().Count(p=>p.r>220&&p.b>220&&p.g<70)<width*height/200,name+": rendered without missing shaders");
        }
        finally
        {
            RenderTexture.active=oldActive; camera.targetTexture=oldTarget; camera.cullingMask=mask;
            for(int i=0;i<canvases.Length;i++) { canvases[i].renderMode=modes[i]; canvases[i].worldCamera=cameras[i]; canvases[i].planeDistance=distances[i]; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Object.Destroy(target); Object.Destroy(texture);
        }
    }
}
