using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object=UnityEngine.Object;

// Disposable-project Play Mode checks for P3; no gameplay or authoring fixtures are shipped.
public static class QuestCinematicChecks
{
    const string Pending="QuestCinematicChecks.Pending";
    static IEnumerator routine;
    static readonly Stack<IEnumerator> frames=new Stack<IEnumerator>();
    static double deadline;
    static int checks;
    static string captures;
    static Mouse mouse;
    static Vector2 pointer;
    static bool holdPointer;
    static PreMissionFlowController Flow=>Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board=>Flow.StoneBoard;
    static PreMissionStoneMotion Motion=>Board.Motion;
    static PreMissionMissionMiniature Mini=>Motion.Miniature;
    static bool Busy=>Flow.IsPosting||Flow.IsTransitioning;
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.update+=Step;
    public static void RunBatch()
    {
        if(!Application.isBatchMode) throw new Exception("Disposable project only");
        GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline=null;
        PreMissionCinematicSetup.UpgradeAssets(); PreMissionCinematicSetup.UpgradeAssets();
        if(Object.FindObjectsByType<PreMissionMissionMiniature>(FindObjectsInactive.Include).Length!=1) throw new Exception("P3 upgrade duplicated the preview");
        SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Step()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        try
        {
            if(routine==null) { deadline=EditorApplication.timeSinceStartup+300; routine=Run(); frames.Push(routine); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("Cinematic checks timed out");
            if(Advance()) return;
            PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false);
            Debug.Log("CINEMATIC CHECKS COMPLETE: "+checks+"; captures: "+captures); EditorApplication.Exit(0);
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
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); checks++; Debug.Log("CINEMATIC PASS: "+message); }
    static T Field<T>(Object owner,string name) where T:Object=>(T)new SerializedObject(owner).FindProperty(name).objectReferenceValue;
    static IEnumerator Wait(float seconds) { float until=Time.unscaledTime+seconds; while(Time.unscaledTime<until) yield return null; }
    static void MoveMouse(QuestStoneView stone)
    {
        pointer=Board.BoardCamera.WorldToScreenPoint(stone.HitCollider.bounds.center); holdPointer=true;
        FeedPointer(); InputSystem.Update(); mouse.MakeCurrent();
        typeof(PreMissionStoneBoard).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Board,null);
    }
    static void FeedPointer() { if(holdPointer&&mouse!=null&&mouse.added) InputSystem.QueueStateEvent(mouse,new MouseState { position=pointer }); }
    static Rect Project(Bounds bounds)
    {
        Vector2 min=Vector2.one*float.MaxValue,max=Vector2.one*float.MinValue;
        for(int x=-1;x<=1;x+=2) for(int y=-1;y<=1;y+=2) for(int z=-1;z<=1;z+=2)
        {
            Vector2 p=Board.BoardCamera.WorldToViewportPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z)));
            min=Vector2.Min(min,p); max=Vector2.Max(max,p);
        }
        return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
    }
    static Rect Project(RectTransform rect)
    {
        var corners=new Vector3[4]; rect.GetWorldCorners(corners); var canvas=rect.GetComponentInParent<Canvas>().rootCanvas;
        Vector2 min=Vector2.one*float.MaxValue,max=Vector2.one*float.MinValue;
        foreach(var corner in corners)
        {
            var screen=RectTransformUtility.WorldToScreenPoint(canvas.renderMode==RenderMode.ScreenSpaceOverlay?null:canvas.worldCamera,corner);
            var p=new Vector2(screen.x/Board.BoardCamera.pixelWidth,screen.y/Board.BoardCamera.pixelHeight);
            min=Vector2.Min(min,p); max=Vector2.Max(max,p);
        }
        return Rect.MinMaxRect(min.x,min.y,max.x,max.y);
    }
    static void CheckBriefingLayout(string label)
    {
        var paper=Field<RectTransform>(Field<PreMissionPaperPresentation>(Flow,"presentation"),"briefingPaper");
        var panel=Project(paper); var bounds=Motion.Inspected.RockBounds; bounds.Encapsulate(Mini.WorldBounds); var physical=Project(bounds);
        Check(panel.xMin>=0.49f&&panel.xMax<=0.97f&&panel.yMin>=0.06f&&panel.yMax<=0.94f,label+": briefing occupies a readable right-hand panel ("+panel+")");
        Check(physical.xMin>0.015f&&physical.xMax<panel.xMin-0.015f&&physical.yMin>0.07f&&physical.yMax<0.93f,
            label+": selected rock and miniature stay visible together, clear of the information panel");
        Check(Project((RectTransform)Field<Button>(Flow,"acceptButton").transform).width>0.13f
            &&Project((RectTransform)Field<Button>(Flow,"declineButton").transform).width>0.13f,label+": Accept and Decline retain usable hit areas");
    }
    static void Capture(string name,int width=1600,int height=900,bool checkLayout=true)
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
            foreach(var preview in Object.FindObjectsByType<StarterTorsoPreview>()) preview.RenderNow();
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest { destination=target });
            RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply();
            Check(texture.GetPixels32().Count(p=>p.r>220&&p.b>220&&p.g<70)<width*height/200,name+": no missing shaders");
            File.WriteAllBytes(Path.Combine(captures,name+".png"),texture.EncodeToPNG());
            if(checkLayout) CheckBriefingLayout(name);
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
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        foreach(var device in InputSystem.devices.Where(d=>d is Mouse||d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
        mouse=InputSystem.AddDevice<Mouse>(); InputSystem.onBeforeUpdate+=FeedPointer;
        captures=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"P3Captures"); Directory.CreateDirectory(captures);
        Flow.SkipPosting(); yield return null;
        Check(Field<PreMissionPaperPresentation>(Flow,"presentation").TabletopBriefing&&!Mini.IsVisible,"Physical board enables the split briefing without showing an unselected miniature");
        for(int i=0;i<3;i++) { QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return null; yield return null; while(Busy) yield return null; }
        var small=Board.Stones.First(s=>!s.IsMain); var main=Board.Stones.Single(s=>s.IsMain);
        var smallRoot=small.transform.localPosition; var mainRoot=main.transform.localPosition;
        var smallBounds=small.HitCollider.bounds; var mainBounds=main.HitCollider.bounds;
        MoveMouse(small); yield return Wait(0.25f); float smallLift=small.CurrentOffset.y,smallAngle=Mathf.Abs(small.CurrentAngles.x);
        MoveMouse(main); yield return Wait(0.25f);
        Check(smallLift>0.05f&&main.CurrentOffset.y>0&&main.CurrentOffset.y<smallLift*0.6f&&Mathf.Abs(main.CurrentAngles.x)<smallAngle*0.6f,
            "Heavy hover lifts and tilts less than light hover");
        Check(small.transform.localPosition==smallRoot&&main.transform.localPosition==mainRoot&&small.HitCollider.bounds==smallBounds&&main.HitCollider.bounds==mainBounds,
            "Mass-aware hover preserves both layout roots and hit bounds");
        MoveMouse(small); yield return Wait(0.2f); Flow.SelectQuest(small.Offer.Quest);
        bool shortRotation=true;
        while(Motion.InspectionProgress<0.6f)
        {
            shortRotation&=Mathf.Abs(small.CurrentAngles.x)<15&&Mathf.Abs(small.CurrentAngles.z)<15;
            yield return null;
        }
        Check(shortRotation,"Inspection takes the short rotation path from negative hover angles");
        Check(Flow.IsTransitioning&&!Board.InputEnabled&&Mini.IsVisible&&Mini.Progress>0&&!Field<GameObject>(Flow,"detailsView").activeSelf,
            "The miniature emerges on the selected rock during camera approach, before the briefing opens");
        Capture("p3-approach",1600,900,false);
        Flow.SelectQuest(main.Offer.Quest); Flow.AcceptQuest(); Flow.Back();
        Check(Flow.Session.SelectedQuest==small.Offer.Quest,"Competing clicks and navigation cannot replace the active inspection");
        while(Busy) yield return null;
        Check(Mini.Quest==small.Offer.Quest&&Mini.ActiveType==small.Offer.Quest.missionType&&Mini.Progress==1&&small.InscriptionOpacity==0,
            "Correct symbolic objective is fully revealed on the rock with its printed label retracted");
        Check(Mini.GetComponentsInChildren<Collider>(true).Length==0&&Mini.GetComponentsInChildren<Rigidbody>(true).Length==0,
            "Miniature geometry has no physics, gameplay actors or raycast blockers");
        foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1600,900),new Vector2Int(1920,1080),new Vector2Int(1024,768)}) Capture("p3-small-"+size.x,size.x,size.y);

        var selectedQuest=Flow.Session.SelectedQuest; var accept=Field<Button>(Flow,"acceptButton"); var details=Field<GameObject>(Flow,"detailsView").GetComponent<CanvasGroup>();
        Flow.AcceptQuest();
        Check(Flow.IsTransitioning&&!details.interactable&&!details.blocksRaycasts,"Accept acknowledges the click while locking both transition layers immediately");
        while(Busy) yield return null; Flow.NextTorso(); while(Busy) yield return null;
        var torso=Flow.Session.SelectedTorso; var preview=Field<StarterTorsoPreview>(Flow,"torsoPreview");
        Check(preview.HasPreview&&preview.Torso==torso&&Mini.Quest==selectedQuest&&Motion.InspectionProgress==1,
            "Existing torso browsing reuses its model preview while preserving the inspected world setup");
        Capture("p3-torso",1600,900,false);
        var original=Field<MissionStartHook>(Flow,"missionStartHook"); var recording=Flow.gameObject.AddComponent<PreMissionRecordingHook>();
        var fields=new SerializedObject(Flow); fields.FindProperty("missionStartHook").objectReferenceValue=recording; fields.ApplyModifiedPropertiesWithoutUndo();
        Flow.ConfirmTorso(); Flow.ConfirmTorso(); while(Busy) yield return null;
        Check(recording.Calls==1&&recording.Quest==selectedQuest&&recording.Torso==torso&&Flow.Session.Progression.ActiveAttempt==null,
            "Confirmation still submits the exact quest and torso once without beginning gameplay");
        fields.Update(); fields.FindProperty("missionStartHook").objectReferenceValue=original; fields.ApplyModifiedPropertiesWithoutUndo(); Object.Destroy(recording);
        Flow.Back(); while(Busy) yield return null;
        Check(Flow.Session.SelectedQuest==selectedQuest&&Flow.Session.SelectedTorso==torso&&Mini.Quest==selectedQuest&&Mini.Progress==1,"Torso Back retains data, miniature and focused framing");
        Flow.Back(); while(!Motion.IsTransitioning) yield return null; while(Mini.Progress>=0.8f) yield return null;
        Check(small.InscriptionOpacity>0&&!Board.InputEnabled,"Decline retracts the miniature and restores the inscription before board interaction resumes");
        Capture("p3-return",1024,768,false); while(Busy) yield return null;
        Check(!Mini.IsVisible&&Mini.Quest==null&&small.CurrentOffset==Vector3.zero&&small.InscriptionOpacity==1&&small.HitCollider.enabled,
            "Return clears the preview and restores the exact physical rock and inscription");
        yield return Wait(0.25f); Check(small.IsHighlighted,"Stationary-pointer hover works after the new return sequence");

        Flow.SelectQuest(main.Offer.Quest); while(Busy) yield return null;
        Check(main.CurrentOffset.y<smallLift*3&&main.CurrentOffset.y<0.16f&&Mini.ActiveType==QuestMissionType.Retrieval,
            "Main inspection stays restrained and uses its actual Retrieval type rather than inventing a boss preview");
        Capture("p3-main-1600"); Capture("p3-main-1024",1024,768);
        Flow.Back(); while(Busy) yield return null;

        var catalogue=Field<QuestBoardCatalogue>(Flow,"catalogue"); catalogue.smallOfferCount=5; Flow.StartNewProgression(); Flow.SkipPosting(); yield return null;
        var run=Flow.Session.Progression; var ids=string.Join(";",run.Offers.Select(o=>o.Id));
        foreach(var type in (QuestMissionType[])Enum.GetValues(typeof(QuestMissionType)))
        {
            var offer=run.Offers.Single(o=>o.Quest.missionType==type); string authored=EditorJsonUtility.ToJson(offer.Quest);
            Flow.SelectQuest(offer.Quest); while(Busy) yield return null;
            Check(Mini.IsVisible&&Mini.Quest==offer.Quest&&Mini.ActiveType==type&&Mini.PieceCount>0,"Mission miniature matches existing type "+type);
            Check(Field<TMP_Text>(Flow,"detailsTitle").text==offer.Quest.title&&Field<TMP_Text>(Flow,"detailsType").text.Contains(PreMissionFlowController.MissionTypeName(type)),
                "Briefing retains real title/type data for "+type);
            Capture("p3-objective-"+type);
            Flow.Back(); while(Busy) yield return null;
            Check(!Mini.IsVisible&&EditorJsonUtility.ToJson(offer.Quest)==authored&&run.SuccessfulSmallCompletions==0&&string.Join(";",run.Offers.Select(o=>o.Id))==ids,
                "Inspecting/returning "+type+" changes neither authored quest data, offers nor progression");
        }
        catalogue.smallOfferCount=3; Flow.StartNewProgression(); Flow.SkipPosting(); yield return null;
        small=Board.Stones[0]; Flow.SelectQuest(small.Offer.Quest); while(Motion.InspectionProgress<0.6f) yield return null;
        Flow.enabled=false; Flow.enabled=true;
        Check(!Busy&&Flow.Session.View==PreMissionView.Details&&Mini.Quest==small.Offer.Quest&&Mini.Progress==1,"Disabling during approach settles the correct split briefing and full miniature");
        Flow.Back(); while(!Motion.IsTransitioning) yield return null; Flow.enabled=false; Flow.enabled=true;
        Check(!Busy&&Flow.Session.View==PreMissionView.Board&&!Mini.IsVisible&&small.InscriptionOpacity==1,"Disabling during return clears preview geometry and restores the board");
        Flow.SelectQuest(small.Offer.Quest); while(Busy) yield return null; Mini.enabled=false;
        Check(!Board.InputEnabled&&!Flow.IsTransitioning,"Disabled miniature safely locks interaction without leaving a coroutine active");
        Mini.enabled=true; yield return null;
        Check(Mini.IsVisible&&Mini.Progress==1&&Flow.Session.View==PreMissionView.Details,"Re-enabling the miniature restores the current inspection once");
        var surviving=Mini; var world=Board; Object.Destroy(Flow.gameObject); yield return null; yield return Wait(0.2f);
        Check(!surviving.IsVisible&&surviving.Quest==null&&!world.InputEnabled,"UI destruction detaches the miniature and leaves no orphan world interaction");
        InputSystem.onBeforeUpdate-=FeedPointer; InputSystem.RemoveDevice(mouse); Time.timeScale=1;
    }
}
