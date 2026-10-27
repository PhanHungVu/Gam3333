using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Run only in a disposable copy: creates prototype assets and exercises real world raycasts.
public static class QuestStoneBoardChecks
{
    const string Pending = "QuestStoneBoardChecks.Pending";
    static IEnumerator routine;
    static double deadline;
    static int checks;
    static Mouse mouse;
    static Keyboard keyboard;
    static string captures;
    static PreMissionFlowController Flow => Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board => Flow.StoneBoard;
    static bool Busy => Flow.IsPosting || Flow.IsTransitioning;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Step;
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
        QualitySettings.renderPipeline = null;
        if (GraphicsSettings.defaultRenderPipeline == null) throw new Exception("Copy project Settings/URP assets into the disposable project first.");
        AssetDatabase.ImportAsset("Assets/Models/Golem1Models/Golem1Torso.glb", ImportAssetOptions.ForceUpdate);
        PreMissionStonePolishSetup.UpgradeAssets();
        PreMissionStonePolishSetup.UpgradeAssets();
        if (Object.FindObjectsByType<PreMissionStoneBoard>().Length != 1) throw new Exception("Repeated M2 upgrade created duplicate boards.");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++; Debug.Log("STONE BOARD PASS: " + message);
    }
    public static bool CompleteSmallForTest(PreMissionFlowController flow)
    {
        var run=flow.Session.Progression;
        var quest=run.Offers.First(o=>o.Quest.category==QuestCategory.Side).Quest;
        return run.TryBeginMission(quest,flow.Session.SelectedTorso,out var attempt) && run.ReportMissionResult(attempt.Id,QuestMissionOutcome.Succeeded);
    }
    static T Field<T>(string name) where T : Object => (T)new SerializedObject(Flow).FindProperty(name).objectReferenceValue;
    static void Step()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (routine == null) { deadline = EditorApplication.timeSinceStartup + 240; routine = Run(); }
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("M2 checks timed out");
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (routine.MoveNext()) return;
            PreMissionReliabilityChecks.EndLogAudit();
            SessionState.SetBool(Pending, false);
            Debug.Log("STONE BOARD CHECKS COMPLETE: " + checks + "; captures: " + captures);
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        { PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending, false); Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    static void UpdateBoard() => typeof(PreMissionStoneBoard).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(Board, null);
    static Vector2 Point(QuestStoneView stone) => Board.BoardCamera.WorldToScreenPoint(stone.HitCollider.bounds.center);
    static void MoveMouse(Vector2 point)
    { InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); InputSystem.Update(); mouse.MakeCurrent(); UpdateBoard(); }
    static void ClickStone(QuestStoneView stone)
    {
        var point = Point(stone);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point }.WithButton(MouseButton.Left)); InputSystem.Update(); mouse.MakeCurrent(); UpdateBoard();
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point }); InputSystem.Update();
    }
    static void Key(Key key)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); InputSystem.Update(); keyboard.MakeCurrent(); UpdateBoard();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
    }
    static Rect Project(Bounds bounds)
    {
        Vector2 min = Vector2.one * float.MaxValue, max = Vector2.one * float.MinValue;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
        {
            Vector2 point = Board.BoardCamera.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x,y,z)));
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    static void CheckLayout(string label)
    {
        Physics.SyncTransforms(); Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame();
        var rects = Board.Stones.Select(s => Project(s.HitCollider.bounds)).ToArray();
        Check(rects.All(r => r.xMin > 0.03f && r.xMax < 0.97f && r.yMin > 0.17f && r.yMax < 0.80f), label + ": physical selection areas fit between heading and footer");
        Check(!rects.Where((r,i) => rects.Skip(i+1).Any(other => r.Overlaps(other))).Any(), label + ": main and small projected selection areas do not overlap");
        Check(Board.Stones.All(s => Board.StoneAt(Point(s)) == s), label + ": camera raycasts reach every stone");
        Check(Board.Stones.Where(s => s.Offer.Quest.category == QuestCategory.Main).All(s => s.transform.localPosition.x == 0 && s.transform.localPosition.z == 0), label + ": main rock stays at the center");
        var track=Board.Motion!=null ? Board.Motion.ProgressTrack : null;
        if(track!=null)
        {
            var area=Project(track.WorldBounds);
            Check(area.xMin>0.03f && area.xMax<0.97f && area.yMin>0.17f && area.yMax<0.80f,label+": physical progress track fits the board camera frame");
            Check(rects.All(r=>!r.Overlaps(area)),label+": progress track stays clear of every quest hit area");
            Check(track.GetComponentsInChildren<Collider>(true).Length==0,label+": progress objects cannot intercept quest raycasts");
        }
    }
    static void Capture(string name, int width=1600, int height=900)
    {
        var camera=Board.BoardCamera; var oldTarget=camera.targetTexture; int mask=camera.cullingMask;
        var canvases=Object.FindObjectsByType<Canvas>().Where(c=>c.isRootCanvas).ToArray();
        var modes=canvases.Select(c=>c.renderMode).ToArray(); var cameras=canvases.Select(c=>c.worldCamera).ToArray(); var distances=canvases.Select(c=>c.planeDistance).ToArray();
        var target=new RenderTexture(width,height,24); var oldActive=RenderTexture.active; var image=new Texture2D(width,height,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target; camera.cullingMask|=(1<<0)|(1<<5);
            foreach(var canvas in canvases) { canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame();
            if(Flow.Session.View==PreMissionView.Board) CheckLayout(name);
            foreach(var preview in Object.FindObjectsByType<StarterTorsoPreview>()) preview.RenderNow();
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest { destination=target });
            RenderTexture.active=target; image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
            var pixels=image.GetPixels32();
            Check(pixels.Count(p=>p.r>220 && p.b>220 && p.g<70)<pixels.Length/200, name+": URP capture has no missing-shader pink output");
            Check(pixels.Where((p,i)=>i/width>height*0.83f).Count(p=>p.r>130 && p.g>130 && p.b>110)>100,
                name+": existing UI is visible in the capture as well as the world board");
            File.WriteAllBytes(Path.Combine(captures,name+".png"),image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=oldActive; camera.targetTexture=oldTarget; camera.cullingMask=mask;
            for(int i=0;i<canvases.Length;i++) { canvases[i].renderMode=modes[i]; canvases[i].worldCamera=cameras[i]; canvases[i].planeDistance=distances[i]; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Object.Destroy(image); Object.Destroy(target);
        }
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit();
        Application.runInBackground=true; Time.timeScale=1;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        foreach(var device in InputSystem.devices.Where(d=>d is Mouse || d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
        mouse=InputSystem.AddDevice<Mouse>(); keyboard=InputSystem.AddDevice<Keyboard>();
        captures=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"M2Captures"); Directory.CreateDirectory(captures);
        while(Busy) yield return null;
        var run=Flow.Session.Progression;
        int smallCount=run.SmallOfferCount;
        Check(smallCount==6&&run.RequiredSmallCompletions==3&&Board!=null && Board.Stones.Count==smallCount && Flow.Notices.Count==0 && !Field<Transform>("noticeParent").gameObject.activeSelf,"Shipped scene starts with six small rocks and still requires only three successes");
        Check(run.Offers.Select(o=>o.Quest.questId).Distinct().Count()==6,"All six surrounding offers use distinct quest data");
        Check(Camera.main==Board.BoardCamera && Camera.allCameras.Count(c=>c.enabled)==1 && Object.FindObjectsByType<AudioListener>().Count(a=>a.enabled)==1,"One dedicated overhead camera and audio listener are active");
        Check(Board.Stones.All(s=>s.GetComponent<Rigidbody>()==null && s.Offer.Quest.category==QuestCategory.Side),"M2 starts with small offers and stable collider objects without physics simulation");
        Check(!Board.transform.Find("Impact Inlays").gameObject.activeSelf&&Board.Stones.All(s=>!s.IsMain)
            &&Board.Stones.Any(s=>Board.ProjectTerrainBounds(s.HitCollider.bounds).Overlaps(Board.ImpactExclusions(0)[0])),
            "The center is available to side quests before unlock, with no main marker or forced reservation");
        CheckLayout("fresh"); Capture("m2-fresh");
        var first=Board.Stones[0]; var position=first.transform.position; var colliderBounds=first.HitCollider.bounds;
        MoveMouse(Point(first));
        Check(first.IsHighlighted && first.transform.position==position && first.HitCollider.bounds==colliderBounds,"Pointer hover highlights the rock without moving its collider or layout root");
        Capture("m2-hover");
        var back=Field<Button>("exitButton"); var home=back.transform.position;
        back.transform.position=Point(first); Canvas.ForceUpdateCanvases();
        Check(Board.StoneAt(Point(first))==null && !Board.SelectAt(Point(first)),"UI buttons consume pointer input before world raycasts");
        back.transform.position=home; Canvas.ForceUpdateCanvases();
        MoveMouse(new Vector2(-100,-100)); ClickStone(first);
        Check(Flow.Session.SelectedOfferId==first.Offer.Id && Flow.IsTransitioning && !Board.InputEnabled,"Actual Input System click raycasts into the correct briefing and immediately locks world selection");
        Flow.SelectQuest(run.Offers[1].Quest); Flow.AcceptQuest(); Flow.Back();
        Check(Flow.Session.SelectedOfferId==first.Offer.Id,"Competing actions cannot replace a stone selection during briefing entry");
        while(Busy) yield return null;
        Check(Field<TMP_Text>("detailsTitle").text==first.Offer.Quest.title && Field<TMP_Text>("detailsBody").text==first.Offer.Quest.description,"Existing briefing displays the selected stone's actual quest data");
        Capture("m2-details"); Flow.Back(); while(Busy) yield return null;
        Check(Board.FindStone(first.Offer.Id)==first && first.transform.position==position && run.SuccessfulSmallCompletions==0,"Decline restores the same rock and does not award progression");

        for(int win=1;win<=3;win++)
        {
            var removed=run.Offers.First(o=>o.Quest.category==QuestCategory.Side);
            var unchanged=Board.Stones.Where(s=>s.Offer.Id!=removed.Id).ToDictionary(s=>s,s=>s.transform.position);
            Check(CompleteSmallForTest(Flow),"Test fixture reports one actual small mission success "+win);
            yield return null; yield return null; while(Busy) yield return null;
            Check(run.SuccessfulSmallCompletions==win && unchanged.All(pair=>Board.Stones.Contains(pair.Key)
                && (win==3 ? pair.Key.transform.localPosition==Board.ShockwaveSlotPosition(pair.Key.Offer.Slot,pair.Key.transform.localPosition.y) : pair.Key.transform.position==pair.Value)),
                "Completion preserves unrelated offers; only main impact moves their slots "+win);
            Check(Board.Stones.Count==smallCount+(win==3?1:0)&&((run.BigQuest!=null)==(win==3)),"Six small offers remain while the main unlocks only on the third success "+win);
        }
        CheckLayout("unlocked"); Capture("m2-unlocked");
        var main=Board.Stones.Single(s=>s.Offer.Quest.category==QuestCategory.Main);
        Check(main.RockBounds.size.x>Board.Stones.Where(s=>s!=main).Max(s=>s.RockBounds.size.x)*1.4f,"Main quest uses a visibly larger central rock");
        foreach(var stone in Board.Stones.ToArray())
        {
            Check(Board.SelectAt(Point(stone)),"Main and surrounding small stones remain freely selectable: "+stone.Offer.Quest.questId);
            while(Busy) yield return null;
            Check(Flow.Session.SelectedOfferId==stone.Offer.Id,"Selected offer identity survives the world-to-details handoff");
            Flow.Back(); while(Busy) yield return null;
        }
        var inspect=Board.Stones.First(s=>s.Offer.Quest.category==QuestCategory.Side);
        Flow.SelectQuest(inspect.Offer.Quest);
        Flow.enabled=false; Flow.enabled=true;
        Check(!Flow.IsTransitioning && Flow.Session.View==PreMissionView.Details && !Board.InputEnabled,"Disabling during the stone briefing settles one valid details view");
        CompleteSmallForTest(Flow); yield return null; yield return null; while(Busy) yield return null;
        Check(Flow.Session.View==PreMissionView.Board && Flow.Session.SelectedQuest==null && Board.FindStone(inspect.Offer.Id)==null
            && Board.FindStone(main.Offer.Id)==main,"Completing the inspected small offer safely returns to the board without replacing the main rock");
        MoveMouse(new Vector2(-100,-100)); Key(UnityEngine.InputSystem.Key.RightArrow); Key(UnityEngine.InputSystem.Key.Enter);
        Check(Flow.Session.View==PreMissionView.Details,"Directional keyboard focus and Enter select a physical quest");
        while(Busy) yield return null; Flow.AcceptQuest(); while(Busy) yield return null;
        Flow.NextTorso(); while(Busy) yield return null;
        var torso=Flow.Session.SelectedTorso; var quest=Flow.Session.SelectedQuest;
        Check(torso!=null && Field<TMP_Text>("torsoName").text==torso.partName,"Existing torso browsing remains synchronized with selected data");
        Capture("m2-torso");
        var original=Field<MissionStartHook>("missionStartHook"); var recording=Flow.gameObject.AddComponent<PreMissionRecordingHook>();
        var fields=new SerializedObject(Flow); fields.FindProperty("missionStartHook").objectReferenceValue=recording; fields.ApplyModifiedPropertiesWithoutUndo();
        Flow.ConfirmTorso(); Flow.ConfirmTorso(); while(Busy) yield return null;
        Check(recording.Calls==1 && recording.Quest==quest && recording.Torso==torso && run.ActiveAttempt==null && SceneManager.GetActiveScene().name=="PreMissionScene","Confirmation passes exact choices once to a test hook; UI never begins gameplay or loads a mission");
        fields.Update(); fields.FindProperty("missionStartHook").objectReferenceValue=original; fields.ApplyModifiedPropertiesWithoutUndo(); Object.Destroy(recording);
        Flow.Back(); while(Busy) yield return null; Flow.Back(); while(Busy) yield return null;
        foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(1024,768)}) Capture("m2-unlocked-"+size.x,size.x,size.y);
        var ids=string.Join(";",run.Offers.Select(o=>o.Id)); var placements=Board.Stones.ToDictionary(s=>s.Offer.Id,s=>s.transform.localPosition);
        Flow.enabled=false; Check(!Board.InputEnabled && !Board.SelectAt(Point(main)),"Disabled controller cannot receive physical selection"); Flow.enabled=true;
        Board.enabled=false; Check(!Board.InputEnabled && !Board.SelectAt(Point(main)),"Disabled board cannot receive physical selection"); Board.enabled=true;
        Flow.Back(); yield return null; yield return null;
        Check(SceneManager.GetActiveScene().name=="MainMenuScene" && QuestProgressionRuntime.Current==run,"Board Back returns to the existing main menu while retaining progression");
        SceneManager.LoadScene("PreMissionScene"); yield return null; yield return null; while(Busy) yield return null;
        Check(Flow.Session.Progression==run && string.Join(";",run.Offers.Select(o=>o.Id))==ids && Board.Stones.All(s=>s.transform.localPosition==placements[s.Offer.Id]) && Flow.Session.SelectedTorso==torso,"Scene return preserves exact offers, composition, unlocked main and preferred torso");

        // The shipped catalogue now supplies six real prototype side definitions.
        var catalogue=run.Catalogue;
        var seen=new HashSet<int>();
        for(int seed=0;seen.Count<3 && seed<30;seed++)
        {
            int design=new System.Random(seed).Next(3); if(!seen.Add(design)) continue;
            var fixture=QuestProgressionRuntime.StartNewProgression(catalogue,seed); yield return null; while(Busy) yield return null;
            for(int i=0;i<3;i++) { fixture.TryBeginMission(fixture.Offers[0].Quest,torso,out var attempt); fixture.ReportMissionResult(attempt.Id,QuestMissionOutcome.Succeeded); }
            yield return null; yield return null; while(Busy) yield return null;
            Check(Board.Stones.Count==7 && Board.CompositionIndex==design,"Authored composition supports six surrounding offers plus center, design "+design);
            Capture("m2-six-small-design-"+design);
        }
        // Exercise actual randomized board instances, including the largest supported
        // layout, rather than checking only the three base anchor designs.
        var layouts=new Dictionary<int,HashSet<string>>();
        string firstLayout=null;
        for(int seed=0;seed<64;seed++)
        {
            var fixture=QuestProgressionRuntime.StartNewProgression(catalogue,seed);
            yield return null; yield return null; Flow.SkipPosting(); yield return null; while(Busy) yield return null;
            for(int win=0;win<3;win++)
            {
                fixture.TryBeginMission(fixture.Offers[0].Quest,torso,out var attempt);
                fixture.ReportMissionResult(attempt.Id,QuestMissionOutcome.Succeeded);
            }
            yield return null; yield return null; Flow.SkipPosting(); yield return null; while(Busy) yield return null;
            CheckLayout("random surround seed "+seed);
            var signature=string.Join(";",Board.Stones.Select(s=>s.transform.localPosition.ToString("F3")));
            if(!layouts.TryGetValue(Board.CompositionIndex,out var variations)) layouts[Board.CompositionIndex]=variations=new HashSet<string>();
            Check(variations.Add(signature),"Different seeds vary positions within the same surround design, seed "+seed);
            if(seed==0) firstLayout=signature;
            if(seed<3) Capture("random-surround-"+seed);
            if(seed%21==0)
            {
                var poses=Board.Stones.ToDictionary(s=>s.Offer.Slot,s=>(s.transform.localPosition,s.transform.localRotation));
                Flow.CreateFreshBoard(); yield return null; yield return null; Flow.SkipPosting(); yield return null; while(Busy) yield return null;
                Check(Board.Stones.All(s=>s.transform.localPosition==poses[s.Offer.Slot].Item1&&s.transform.localRotation==poses[s.Offer.Slot].Item2),
                    "Rebinding a run retains randomized slot positions and rotations, seed "+seed);
            }
        }
        QuestProgressionRuntime.StartNewProgression(catalogue,0);
        yield return null; yield return null; Flow.SkipPosting(); yield return null; while(Busy) yield return null;
        for(int win=0;win<3;win++) CompleteSmallForTest(Flow);
        yield return null; yield return null; Flow.SkipPosting(); yield return null; while(Busy) yield return null;
        Check(string.Join(";",Board.Stones.Select(s=>s.transform.localPosition.ToString("F3")))==firstLayout,
            "The same seed reconstructs the exact randomized layout in a new progression instance");
        catalogue.smallOfferCount=5; Flow.StartNewProgression();
        yield return null; yield return null; Flow.SkipPosting(); yield return null; while(Busy) yield return null;
        Check(Board.Stones.Count==5&&Flow.Session.Progression.RequiredSmallCompletions==3,"Five-offer configuration also keeps the three-success requirement");
        for(int win=0;win<3;win++) CompleteSmallForTest(Flow);
        yield return null; yield return null; Flow.SkipPosting(); yield return null; while(Busy) yield return null;
        Check(Board.Stones.Count==6&&Flow.Session.Progression.BigQuest!=null,"Five small offers plus main are available after three successes");
        catalogue.smallOfferCount=smallCount;
        Flow.StartNewProgression(); while(Busy) yield return null;
        Check(Board.Stones.Count==6 && Flow.Session.Progression.BigQuest==null,"Explicit new run restores six small quests and locks the main again");
        InputSystem.RemoveDevice(mouse); InputSystem.RemoveDevice(keyboard);
    }
}
