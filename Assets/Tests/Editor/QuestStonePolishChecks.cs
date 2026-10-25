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
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// M3/M4 behavior checks. Only run in a disposable project, never the working scene.
public static class QuestStonePolishChecks
{
    const string Pending="QuestStonePolishChecks.Pending";
    const string PhysicalityKey="QuestStonePolishChecks.Physicality";
    static IEnumerator routine;
    static readonly Stack<IEnumerator> frames=new Stack<IEnumerator>();
    static double deadline;
    static int checks;
    static string captures;
    static Mouse mouse;
    static PreMissionStoneMotion observed;
    static bool hitch, smallCapture;
    static float smallShake, heavyShake;
    static int smallParticles, heavyParticles;
    static int anticipationFrames;
    static float anticipationFocus, recoilTravel, postContactRise;
    static bool firstMainVerified;
    static readonly Dictionary<string,Quaternion> restRotations=new Dictionary<string,Quaternion>();
    static bool Physicality=>SessionState.GetBool(PhysicalityKey,false);
    static readonly HashSet<string> impacts=new HashSet<string>();
    static readonly Dictionary<string,Drop> drops=new Dictionary<string,Drop>();
    sealed class Drop { public Vector3 root; public int frames; public float maxY; public readonly List<Vector2> trajectory=new List<Vector2>(); }
    static PreMissionFlowController Flow=>Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board=>Flow.StoneBoard;
    static PreMissionStoneMotion Motion=>Board.Motion;
    static QuestBoardImpactFeedback Fx=>Motion.Feedback;
    static bool Busy=>Flow.IsPosting||Flow.IsTransitioning;
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.update+=Step;
    public static void RunBatch()=>Prepare(false);
    public static void RunPhysicalityBatch()=>Prepare(true);
    static void Prepare(bool physicality)
    {
        if(!Application.isBatchMode) throw new Exception("Disposable project only");
        GraphicsSettings.defaultRenderPipeline=UnityEditor.AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline=null;
        SessionState.SetBool(PhysicalityKey,physicality);
        if (physicality) { PreMissionStonePhysicalitySetup.UpgradeAssets(); PreMissionStonePhysicalitySetup.UpgradeAssets(); }
        else PreMissionStonePolishSetup.UpgradeAssets();
        SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool ok,string message)
    { if(!ok) throw new Exception(message); checks++; Debug.Log("STONE POLISH PASS: "+message); }
    static void Step()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        try
        {
            if(routine==null) { deadline=EditorApplication.timeSinceStartup+220; routine=Run(); frames.Push(routine); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors(); Observe();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("Stone polish checks timed out");
            if(Advance()) return;
            PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false);
            Debug.Log("STONE POLISH CHECKS COMPLETE: "+checks+"; captures: "+captures); EditorApplication.Exit(0);
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
    static void Observe()
    {
        if(Flow==null||Board==null||Motion==null) return;
        if(observed!=Motion)
        {
            observed=Motion;
            observed.Impacted+=stone=>
            {
                if(!impacts.Add(stone.Offer.Id)) throw new Exception("Duplicate impact for an already presented offer");
                if(stone.IsMain) heavyParticles=Fx.HeavyDust.particleCount; else smallParticles=Mathf.Max(smallParticles,Fx.SmallDust.particleCount);
                if(Physicality&&stone.IsMain&&!firstMainVerified)
                {
                    var neighbours=Board.Stones.Where(s=>!s.IsMain).OrderBy(s=>Vector3.Distance(s.transform.position,stone.transform.position)).ToArray();
                    Check(neighbours.All(s=>s.IsReacting&&s.IsReady&&!s.IsMoving&&s.HitCollider.enabled),"P1: nearby stone visuals react at contact while their hit areas stay ready and fixed");
                    float near=Quaternion.Angle(restRotations[neighbours.First().Offer.Id],neighbours.First().Visual.localRotation);
                    float far=Quaternion.Angle(restRotations[neighbours.Last().Offer.Id],neighbours.Last().Visual.localRotation);
                    Check(near>far*1.2f&&far>0.05f,"P1: impact tilt decreases with distance from the main stone");
                    Check(Fx.ImpactChips!=null&&Fx.ImpactChips.particleCount>0&&Fx.CameraOffset.magnitude>0,
                        "P1: chips, dust, camera impulse and secondary reactions start together at contact");
                    Capture("p1-heavy-contact");
                }
            };
        }
        if(Physicality&&!firstMainVerified)
        {
            foreach(var stone in Board.Stones.Where(s=>s.IsReady&&!s.IsMoving&&!s.IsReacting)) restRotations[stone.Offer.Id]=stone.Visual.localRotation;
            if(Motion.IsAnticipatingMain)
            {
                anticipationFrames++; anticipationFocus=Mathf.Max(anticipationFocus,Motion.ArrivalFocus);
                if(Fx.HeavyImpacts!=0||Board.Stones.Any(s=>s.IsMain&&s.Visual.gameObject.activeSelf))
                    throw new Exception("P1: heavy impact/stone appeared before anticipation finished");
            }
            if(Fx.BoardSurface!=null) recoilTravel=Mathf.Max(recoilTravel,Fx.BoardSurface.localPosition.magnitude);
            foreach(var stone in Board.Stones.Where(s=>s.IsMain&&QuestProgressionRuntime.WasPresented(s.Offer.Id)))
                postContactRise=Mathf.Max(postContactRise,stone.CurrentOffset.y);
        }
        if(Fx.IsShaking)
        {
            if(Fx.HeavyImpacts>0) heavyShake=Mathf.Max(heavyShake,Fx.CameraOffset.magnitude); else smallShake=Mathf.Max(smallShake,Fx.CameraOffset.magnitude);
        }
        foreach(var stone in Board.Stones.Where(s=>!s.IsReady&&s.Visual.gameObject.activeSelf))
        {
            if(!drops.TryGetValue(stone.Offer.Id,out var drop)) drops[stone.Offer.Id]=drop=new Drop { root=stone.transform.position };
            if(stone.transform.position!=drop.root||stone.HitCollider.enabled) throw new Exception("Arrival moved a layout root or enabled its collider before settling");
            drop.frames++; drop.maxY=Mathf.Max(drop.maxY,stone.CurrentOffset.y);
            if(stone.CurrentOffset.y>0.12f) drop.trajectory.Add(new Vector2(Time.unscaledTime,stone.CurrentOffset.y));
            if(!hitch) { hitch=true; System.Threading.Thread.Sleep(240); }
            if(!smallCapture&&!stone.IsMain&&stone.CurrentOffset.y>0.2f&&stone.CurrentOffset.y<0.7f&&captures!=null)
            { smallCapture=true; Capture("m3-small-drop"); }
        }
    }
    static T Field<T>(Object target,string name) where T:Object => (T)new SerializedObject(target).FindProperty(name).objectReferenceValue;
    static IEnumerator Wait(float seconds) { float until=Time.unscaledTime+seconds; while(Time.unscaledTime<until) yield return null; }
    static void MoveMouse(Vector2 point)
    {
        InputSystem.QueueStateEvent(mouse,new MouseState { position=point }); InputSystem.Update(); mouse.MakeCurrent();
        typeof(PreMissionStoneBoard).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(Board,null);
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
            foreach(var preview in Object.FindObjectsByType<StarterTorsoPreview>()) preview.RenderNow();
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest { destination=target });
            RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply();
            var pixels=texture.GetPixels32(); Check(pixels.Count(p=>p.r>220&&p.b>220&&p.g<70)<pixels.Length/200,name+": no missing-shader output");
            File.WriteAllBytes(Path.Combine(captures,name+".png"),texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=oldActive; camera.targetTexture=oldTarget; camera.cullingMask=mask; Board.RefreshCameraFrame();
            for(int i=0;i<canvases.Length;i++) { canvases[i].renderMode=modes[i]; canvases[i].worldCamera=cameras[i]; canvases[i].planeDistance=distances[i]; }
            Canvas.ForceUpdateCanvases(); Object.Destroy(target); Object.Destroy(texture);
        }
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Time.timeScale=0; Application.runInBackground=true;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        foreach(var device in InputSystem.devices.Where(d=>d is Mouse||d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
        mouse=InputSystem.AddDevice<Mouse>();
        captures=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"M34Captures"); Directory.CreateDirectory(captures);
        while(Busy) yield return null;
        Check(hitch&&drops.Count==3&&drops.Values.All(d=>d.frames>=3&&d.maxY>0.65f),"All three initial stones visibly drop despite a startup hitch and timeScale zero");
        Check(Fx.SmallImpacts==3&&Fx.HeavyImpacts==0&&Board.Stones.All(s=>s.IsReady&&!s.IsMoving&&s.CurrentOffset==Vector3.zero),"Initial light impacts settle once with stable selectable roots");
        Check(Fx.SmallDust.main.useUnscaledTime&&Fx.HeavyDust.main.useUnscaledTime&&smallParticles>0,"Dust is actually emitted and uses unscaled time");
        var smallClip=Field<AudioClip>(Fx,"smallImpact"); var heavyClip=Field<AudioClip>(Fx,"heavyImpact");
        Check(smallClip!=null&&heavyClip!=null&&heavyClip.length>smallClip.length*2,"Distinct imported short/light and longer/deeper impact clips are assigned");
        Capture("m3-small-settled");
        if(Physicality)
        {
            var profiles=Board.Stones.Select(Motion.GetArrivalProfile).ToArray();
            Check(profiles.Select(p=>p.Style).Distinct().Count()==3&&drops.Values.Select(d=>Mathf.RoundToInt(d.maxY*100)).Distinct().Count()>1,
                "P1: the first three real arrivals distribute light drop, tumble and tilt/settle with varied heights");
            Check(profiles.All(p=>p.FallDuration+p.SettleDuration>=0.3f&&p.FallDuration+p.SettleDuration<=0.55f),"P1: small arrival variations remain within the short repeatable timing range");
            Check(Fx.BoardSurface!=null&&Fx.ImpactChips!=null&&Fx.BoardSurface.GetComponentsInChildren<Collider>().Length==0
                &&Board.GetComponentsInChildren<ParticleSystem>(true).Length==3,"P1: repeated upgrade owns one visual surface and three reusable effect systems, with no board physics");
        }
        var run=Flow.Session.Progression;
        for(int win=1;win<=3;win++)
        {
            var unchanged=Board.Stones.Skip(1).ToDictionary(s=>s,s=>s.transform.position);
            QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return null; yield return null;
            if(win==3)
            {
                if(Physicality)
                {
                    while(!Motion.IsAnticipatingMain) yield return null;
                    while(Motion.ArrivalFocus<0.5f) yield return null;
                    Capture("p1-main-anticipation");
                }
                while(!Board.Stones.Any(s=>s.IsMain&&s.Visual.gameObject.activeSelf)) yield return null;
                Capture("m3-main-high-drop");
                while(Fx.HeavyImpacts==0) yield return null;
                yield return null; Capture("m3-main-impact");
            }
            while(Busy) yield return null;
            Check(unchanged.All(pair=>Board.Stones.Contains(pair.Key)&&pair.Key.transform.position==pair.Value),"Arrival/recoil never displaces unrelated rocks, success "+win);
        }
        var main=Board.Stones.Single(s=>s.IsMain); var mainDrop=drops[main.Offer.Id];
        Check(mainDrop.maxY>5f&&mainDrop.frames>10&&Fx.HeavyImpacts==1&&heavyParticles>smallParticles,"Main arrives higher, settles longer and emits the stronger impact exactly once");
        var speeds=mainDrop.trajectory.Zip(mainDrop.trajectory.Skip(1),(a,b)=>(a.y-b.y)/Mathf.Max(0.001f,b.x-a.x)).ToArray();
        Check(speeds.Length>=5&&speeds.Skip(speeds.Length/2).Average()>speeds.Take(speeds.Length/2).Average()*1.2f,"Main rock visibly accelerates toward contact");
        Check(heavyShake>smallShake*3&&Fx.CameraOffset==Vector3.zero&&!Fx.IsShaking,"Heavy contact jolts the camera more than light contact, then restores the base pose");
        Check(run.SuccessfulSmallCompletions==3&&Board.Stones.Count==4&&Board.Stones.All(s=>Board.StoneAt(Board.BoardCamera.WorldToScreenPoint(s.HitCollider.bounds.center))==s),"Main and small quests remain selectable after impact");
        if(Physicality)
        {
            Check(anticipationFrames>=3&&anticipationFocus>0.9f&&Motion.ArrivalFocus==0,"P1: a visible anticipation cue precedes the main fall, then returns fully to board framing");
            Check(recoilTravel>0.01f&&Fx.BoardSurface.localPosition==Vector3.zero&&Quaternion.Angle(Fx.BoardSurface.localRotation,Quaternion.identity)<0.001f,
                "P1: the board visibly recoils then restores its exact authored pose");
            Check(postContactRise<0.001f&&Board.Stones.All(s=>!s.IsReacting),"P1: heavy landing has no upward spring and all secondary reactions finish");
            Check(Board.Stones.Where(s=>!s.IsMain).All(s=>Quaternion.Angle(s.Visual.localRotation,restRotations[s.Offer.Id])<0.001f),
                "P1: every nearby stone returns to its exact pre-impact rotation");
            firstMainVerified=true;
        }
        Capture("m3-unlocked-rest");

        Board.RefreshCameraFrame(); var camera=Board.BoardCamera; var homePosition=camera.transform.position; var homeRotation=camera.transform.rotation; float homeSize=camera.orthographicSize;
        var rock=Board.Stones.First(s=>!s.IsMain); var root=rock.transform.position;
        Vector2 pointer=camera.WorldToScreenPoint(rock.HitCollider.bounds.center); MoveMouse(pointer); yield return Wait(0.18f);
        Check(rock.IsHighlighted&&rock.CurrentOffset.y>0.04f&&rock.transform.position==root,"Hover eases into a restrained lift without moving the hit root");
        Flow.SelectQuest(rock.Offer.Quest);
        while(Motion.InspectionProgress<0.25f) yield return null;
        Check(Flow.IsTransitioning&&camera.orthographicSize<homeSize&&Vector3.Distance(camera.transform.position,homePosition)>0.1f
            &&!Field<GameObject>(Flow,"detailsView").activeSelf,"Camera approaches the physical rock before revealing the information panel");
        Capture("m4-approach");
        Flow.Back(); Flow.AcceptQuest(); Flow.ConfirmTorso(); Flow.SelectQuest(main.Offer.Quest);
        Check(Flow.Session.SelectedOfferId==rock.Offer.Id,"Rapid competing actions cannot replace or skip an inspection");
        while(Busy) yield return null;
        Check(Motion.InspectionProgress==1&&rock.CurrentOffset.y>0.2f&&!rock.HitCollider.enabled&&Field<TMP_Text>(Flow,"detailsTitle").text==rock.Offer.Quest.title,
            "Inspection holds the lifted stone and correct briefing with world selection disabled");
        Capture("m4-focused-briefing");
        Flow.AcceptQuest(); while(Busy) yield return null; Flow.NextTorso(); while(Busy) yield return null;
        var torso=Flow.Session.SelectedTorso; var quest=Flow.Session.SelectedQuest;
        Capture("m4-torso");
        var original=Field<MissionStartHook>(Flow,"missionStartHook"); var recording=Flow.gameObject.AddComponent<PreMissionRecordingHook>();
        var fields=new SerializedObject(Flow); fields.FindProperty("missionStartHook").objectReferenceValue=recording; fields.ApplyModifiedPropertiesWithoutUndo();
        Flow.ConfirmTorso(); Flow.ConfirmTorso(); while(Busy) yield return null;
        Check(recording.Calls==1&&recording.Quest==quest&&recording.Torso==torso&&run.ActiveAttempt==null&&SceneManager.GetActiveScene().name=="PreMissionScene",
            "Polished confirmation submits exact mission/torso data once without beginning gameplay or loading a mission");
        fields.Update(); fields.FindProperty("missionStartHook").objectReferenceValue=original; fields.ApplyModifiedPropertiesWithoutUndo(); Object.Destroy(recording);
        Flow.Back(); while(Busy) yield return null;
        Check(Motion.InspectionProgress==1&&Flow.Session.SelectedTorso==torso,"Torso Back preserves the inspected quest, preferred torso and camera focus");
        Flow.Back(); while(!Motion.IsTransitioning) yield return null;
        while(Motion.InspectionProgress>0.6f) yield return null;
        Capture("m4-return-4x3",1024,768);
        while(Busy) yield return null; Board.RefreshCameraFrame();
        Check(rock.transform.position==root&&rock.CurrentOffset==Vector3.zero&&rock.HitCollider.enabled&&Motion.Inspected==null
            &&Vector3.Distance(camera.transform.position,homePosition)<0.001f&&Quaternion.Angle(camera.transform.rotation,homeRotation)<0.001f&&Mathf.Abs(camera.orthographicSize-homeSize)<0.001f,
            "Decline restores the exact resting rock, collider, rotation and camera frame after a resize/refit");
        yield return Wait(0.18f); Check(rock.IsHighlighted,"Stationary-pointer hover recovers after returning from inspection");
        foreach(var size in new[]{new Vector2Int(1280,720),new Vector2Int(1920,1080),new Vector2Int(1024,768)}) Capture("m4-rest-"+size.x,size.x,size.y);

        Flow.SelectQuest(main.Offer.Quest); while(Motion.InspectionProgress<0.2f) yield return null;
        Flow.enabled=false; Flow.enabled=true;
        Check(!Busy&&Flow.Session.View==PreMissionView.Details&&Motion.InspectionProgress==1,"Disabling mid-approach settles the target details view and focused camera");
        Flow.Back(); while(!Motion.IsTransitioning) yield return null; Flow.enabled=false; Flow.enabled=true;
        Check(!Busy&&Flow.Session.View==PreMissionView.Board&&Motion.InspectionProgress==0&&Board.Stones.All(s=>s.IsReady&&!s.IsMoving),"Disabling mid-return restores the board without an orphan lift or transition");

        Flow.StartNewProgression(); yield return null;
        int impactCount=Fx.SmallImpacts+Fx.HeavyImpacts; Flow.SkipPosting(); Flow.SelectQuest(Flow.Session.Offers[0]);
        Check(!Busy&&Flow.Session.View==PreMissionView.Board&&Board.Stones.All(s=>s.IsReady&&!s.IsMoving)&&Fx.SmallImpacts+Fx.HeavyImpacts==impactCount&&!Fx.IsShaking,
            "Skipping the entry settles pending drops without effects or same-frame quest selection");
        yield return null;
        Flow.StartNewProgression(); yield return null; while(!Motion.IsPosting) yield return null;
        Motion.enabled=false;
        Check(!Board.InputEnabled&&!Fx.IsShaking&&Fx.SmallDust.particleCount==0&&Fx.HeavyDust.particleCount==0,"Disabling the motion component cancels arrivals, particles, shake and input");
        Motion.enabled=true; yield return null;
        Check(Board.InputEnabled&&Board.Stones.All(s=>s.IsReady&&!s.IsMoving),"Re-enabling restores one settled board without restarting arrivals");

        // Leave before the new main impacts; it must still get its first arrival on return.
        Flow.StartNewProgression(); while(Busy) yield return null;
        for(int i=0;i<2;i++) { QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return null; yield return null; while(Busy) yield return null; }
        QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return null; yield return null;
        while(!Board.Stones.Any(s=>s.IsMain&&s.Visual.gameObject.activeSelf)) yield return null;
        string mainId=Flow.Session.Progression.BigQuest.Id; var persisted=Flow.Session.Progression;
        Check(!QuestProgressionRuntime.WasPresented(mainId),"Unseen main arrival is not marked presented before physical contact");
        Flow.Back(); yield return null; yield return null;
        Check(SceneManager.GetActiveScene().name=="MainMenuScene"&&QuestProgressionRuntime.Current==persisted,"Leaving during a drop keeps progression without stray scene work");
        SceneManager.LoadScene("PreMissionScene"); yield return null; yield return null;
        while(Fx.HeavyImpacts==0) yield return null;
        Check(Fx.HeavyImpacts==1&&QuestProgressionRuntime.WasPresented(mainId),"Returning plays the first heavy impact and records presentation at contact");
        Flow.Back(); yield return null; yield return null;
        SceneManager.LoadScene("PreMissionScene"); yield return null; yield return null; while(Busy) yield return null;
        Check(Fx.HeavyImpacts==0&&Flow.Session.Progression==persisted&&Board.Stones.Count==4,"A main already seen at contact never replays its heavy unlock on later scene returns");
        Check(typeof(PreMissionFlowControllerEditor).IsSubclassOf(typeof(Editor))
            &&typeof(QuestProgressionTools).Assembly!=typeof(PreMissionFlowController).Assembly
            &&!typeof(PreMissionFlowController).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.DeclaredOnly).Any(m=>m.Name=="CompleteSmallQuest"),
            "Restored temporary completion control is Editor-only and adds no runtime completion action");

        if(Physicality)
        {
            for(int cycle=0;cycle<2;cycle++)
            {
                Flow.StartNewProgression(); while(Busy) yield return null;
                for(int win=0;win<3;win++)
                {
                    QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return null; yield return null;
                    if(win<2) while(Busy) yield return null;
                }
                if(cycle==0)
                {
                    while(!Motion.IsAnticipatingMain||Motion.ArrivalFocus<0.4f) yield return null;
                    int before=Fx.HeavyImpacts; Flow.SkipPosting();
                    Check(!Busy&&!Motion.IsAnticipatingMain&&Motion.ArrivalFocus==0&&Fx.HeavyImpacts==before
                        &&QuestProgressionRuntime.WasPresented(Flow.Session.Progression.BigQuest.Id),"P1: skipping anticipation safely completes presentation without a fabricated heavy impact");
                }
                else
                {
                    while(!Board.Stones.Any(s=>s.IsReacting)) yield return null;
                    Flow.enabled=false;
                    Check(!Fx.IsRecoiling&&Fx.ImpactChips.particleCount==0&&Fx.BoardSurface.localPosition==Vector3.zero
                        &&Board.Stones.All(s=>!s.IsReacting&&s.CurrentOffset==Vector3.zero),"P1: disabling during secondary recoil clears all reactions, chips and board motion");
                    Flow.enabled=true;
                }
                yield return null;
                Check(Board.InputEnabled&&Motion.ArrivalFocus==0&&Board.Stones.All(s=>s.IsReady&&!s.IsMoving&&s.HitCollider.enabled),
                    "P1: skip/recovery leaves every quest selectable with no camera focus or collider residue, cycle "+cycle);
            }
        }

        var remainingBoard=Board; var remainingMotion=Motion;
        Flow.SelectQuest(Board.Stones[0].Offer.Quest); while(Motion.InspectionProgress<0.2f) yield return null;
        Object.Destroy(Flow.gameObject); yield return null; yield return Wait(1.1f);
        Check(!remainingMotion.IsTransitioning&&!remainingMotion.IsPosting&&remainingMotion.InspectionProgress==0&&!remainingBoard.InputEnabled,
            "Destroying the UI cancels camera/stone callbacks and leaves no active world interaction");
        InputSystem.RemoveDevice(mouse); Time.timeScale=1;
    }
}
