using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Actual terrain geometry/reveal and quest drop integration. Disposable project only.
public static class QuestBoardTerrainChecks
{
    const string Pending="QuestBoardTerrainChecks.Pending";
    static readonly Stack<IEnumerator> frames=new Stack<IEnumerator>();
    static readonly HashSet<string> images=new HashSet<string>();
    static readonly Dictionary<string,float> heights=new Dictionary<string,float>();
    static bool started, terrainFirst=true, stillCamera=true, stableRoots=true, sawSubmerged, sawRising;
    static int checks, revealEvents, terrainFrames;
    static double deadline;
    static string captures;
    static PreMissionStoneMotion observed;
    static Vector3 cameraStart;
    static Quaternion rotationStart;
    static float sizeStart;
    static PreMissionFlowController Flow=>Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board=>Flow.StoneBoard;
    static PreMissionStoneMotion Motion=>Board.Motion;
    static PreMissionBoardTerrain Terrain=>Motion.Terrain;
    static bool Busy=>Flow.IsPosting||Flow.IsTransitioning;
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.update+=Step;
    public static void RunBatch()
    {
        if(!Application.isBatchMode) throw new Exception("Disposable project only");
        PreMissionBoardTerrainSetup.UpgradeAssets(); PreMissionBoardTerrainSetup.UpgradeAssets();
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PreMissionStoneBoardSetup.BoardPath);
        if(prefab.GetComponentsInChildren<PreMissionBoardTerrain>(true).Length!=1) throw new Exception("Terrain upgrade duplicated its component");
        GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline=null;
        EditorSceneManager.OpenScene("Assets/Scenes/PreMissionScene.unity");
        SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); checks++; Debug.Log("TERRAIN PASS: "+message); }
    static void Step()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        try
        {
            if(!started) { started=true; deadline=EditorApplication.timeSinceStartup+300; frames.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors(); Observe();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("Terrain checks timed out");
            while(frames.Count>0)
            {
                var current=frames.Peek();
                if(!current.MoveNext()) { (current as IDisposable)?.Dispose(); frames.Pop(); continue; }
                if(current.Current is IEnumerator nested) { frames.Push(nested); continue; }
                return;
            }
            PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false);
            Debug.Log("BOARD TERRAIN CHECKS COMPLETE: "+checks+"; captures: "+captures); EditorApplication.Exit(0);
        }
        catch(Exception exception) { PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false); Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    static IEnumerator Ready() { yield return null; yield return null; while(Busy) yield return null; }
    static void Observe()
    {
        if(Flow==null||Board==null||Motion==null||Terrain==null) return;
        if(observed!=Motion)
        {
            observed=Motion; Terrain.Revealed+=()=>revealEvents++;
            Motion.Impacted+=stone=>terrainFirst&=Terrain.IsRevealed&&!Terrain.IsRevealing;
        }
        foreach(var stone in Board.Stones)
        {
            if(stone.IsReady||!stone.Visual.gameObject.activeSelf) continue;
            heights.TryGetValue(stone.Offer.Id,out float height); heights[stone.Offer.Id]=Mathf.Max(height,stone.CurrentOffset.y);
        }
        if(!Terrain.IsRevealing) return;
        if(terrainFrames==0) { cameraStart=Board.BoardCamera.transform.position; rotationStart=Board.BoardCamera.transform.rotation; sizeStart=Board.BoardCamera.orthographicSize; }
        terrainFrames++;
        stillCamera&=Vector3.Distance(cameraStart,Board.BoardCamera.transform.position)<0.0001f
            &&Quaternion.Angle(rotationStart,Board.BoardCamera.transform.rotation)<0.001f&&Mathf.Abs(sizeStart-Board.BoardCamera.orthographicSize)<0.0001f;
        stableRoots&=Board.Stones.All(s=>!s.IsReady&&!s.HitCollider.enabled&&!s.Visual.gameObject.activeSelf);
        var renderers=Terrain.GetComponentsInChildren<MeshRenderer>();
        if(renderers.Any(r=>r.transform.localPosition.y< -0.5f)) { sawSubmerged=true; Capture("terrain-submerged"); }
        if(Terrain.RevealProgress>0.35f&&Terrain.RevealProgress<0.65f) { sawRising=true; Capture("terrain-rising"); }
    }
    static string Signature()
    {
        return string.Join(";",Terrain.GetComponentsInChildren<MeshFilter>(true).Select(filter=>
            filter.transform.localPosition.ToString("F4")+filter.transform.localRotation.eulerAngles.ToString("F4")
            +string.Join(",",filter.sharedMesh.vertices.Select(v=>v.ToString("F4")))));
    }
    static bool ClearGeometry()
    {
        var exclusions=Board.TerrainExclusions(0.17f);
        return Terrain.RestBounds.All(b=>b.min.x>=-7.2f&&b.max.x<=8.35f&&b.min.z>=-3.92f&&b.max.z<=3.92f
            &&!exclusions.Any(other=>Board.ProjectTerrainBounds(b).Overlaps(other)));
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Time.timeScale=0; Application.runInBackground=true;
        foreach(var device in InputSystem.devices.Where(d=>d is Mouse||d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
        captures=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"TerrainCaptures"); Directory.CreateDirectory(captures);
        yield return Ready();
        Check(Terrain.RidgeCount>=12&&Terrain.RidgeCount<=24,"Seeded tabletop creates a substantial bounded set of faceted mountain ridges");
        Check(sawSubmerged&&sawRising&&terrainFrames>8&&terrainFirst&&stableRoots,"Terrain visibly emerges before any quest rock is shown or dropped");
        Check(stillCamera&&Motion.Feedback.HeavyImpacts==0,"Terrain reveal leaves routine board framing and recoil untouched");
        Check(Motion.Feedback.SmallImpacts==6&&Board.Stones.All(s=>heights.TryGetValue(s.Offer.Id,out float h)&&h>0.65f),"All six side quests use falling arrivals again");
        Check(Terrain.IsRevealed&&!Terrain.IsRevealing&&QuestProgressionRuntime.WasTerrainRevealed(Board.Progression)&&revealEvents==1,"The terrain reveal finishes once and is remembered separately from quest arrivals");
        Check(ClearGeometry()&&Terrain.GetComponentsInChildren<Collider>(true).Length==0,"Ridges stay clear of every reserved quest hit area, main space, track strip and frame; they have no colliders");
        Check(Terrain.GetComponentsInChildren<MeshFilter>().All(f=>f.sharedMesh.vertexCount>60&&f.sharedMesh.normals.Length==f.sharedMesh.vertexCount),"Each terrain group has an actual flat-shaded ridge mesh, not a scaled quest rock");
        Capture("terrain-with-quests");
        var run=Board.Progression; var catalogue=run.Catalogue; string signature=Signature();
        Flow.SelectQuest(Board.Stones[0].Offer.Quest); yield return Ready(); Flow.Back(); yield return Ready();
        Flow.CreateFreshBoard(); yield return Ready();
        Check(signature==Signature()&&revealEvents==1,"Inspection, decline and rebind retain exact terrain and do not replay its reveal");
        SceneManager.LoadScene("PreMissionScene"); yield return Ready();
        Check(Board.Progression==run&&signature==Signature()&&revealEvents==1&&Motion.Feedback.SmallImpacts==0,"Scene return reconstructs the same terrain already raised without replaying stone arrivals");
        for(int i=0;i<3;i++) { Check(QuestStoneBoardChecks.CompleteSmallForTest(Flow),"Tracked side completion "+(i+1)); yield return Ready(); }
        Check(run.BigQuest!=null&&Motion.Feedback.HeavyImpacts==1&&signature!=Signature()&&revealEvents==1
            &&Terrain.BrokenCount==Terrain.PlanBreaks().Count,"Three successes unlock the centered heavy drop and persist its terrain slides/fractures without replaying the reveal");
        Check(Board.Stones.All(s=>Board.StoneAt(Board.BoardCamera.WorldToScreenPoint(s.HitCollider.bounds.center))==s),"All six sides and the main remain independently selectable through the landscape");
        Capture("terrain-main-unlocked");
        var seen=new HashSet<string>(); int minimum=32,maximum=0;
        for(int seed=0;seed<64;seed++)
        {
            QuestProgressionRuntime.StartNewProgression(catalogue,seed); yield return null; yield return null;
            Flow.SkipPosting(); yield return Ready();
            minimum=Math.Min(minimum,Terrain.RidgeCount); maximum=Math.Max(maximum,Terrain.RidgeCount);
            Check(Terrain.RidgeCount>=12&&Terrain.RidgeCount<=24&&ClearGeometry(),"64-seed terrain clearance and density sweep "+seed+" ("+Terrain.RidgeCount+" ridges, clear="+ClearGeometry()+")");
            Check(Terrain.GetComponentsInChildren<MeshRenderer>().Length==Terrain.RidgeCount&&Board.Stones.Count==6,"Terrain regeneration owns exactly one renderer per ridge with six unaffected quests, seed "+seed);
            string shape=Signature(); seen.Add(shape);
            if(seed==0) { Capture("terrain-seed-0"); }
            if(seed==1) { Capture("terrain-seed-1"); }
        }
        Check(seen.Count==64,"Every sampled seed changes the terrain");
        // Isolate the synchronous terrain build from unrelated prefab/TMP and
        // scene-update random use during the multi-frame sweep.
        var fixture=new GameObject("Terrain random isolation",typeof(PreMissionBoardTerrain));
        var isolated=fixture.GetComponent<PreMissionBoardTerrain>();
        var settings=new SerializedObject(isolated);
        settings.FindProperty("rockMaterial").objectReferenceValue=new SerializedObject(Terrain).FindProperty("rockMaterial").objectReferenceValue;
        settings.ApplyModifiedPropertiesWithoutUndo();
        var randomState=UnityEngine.Random.state; isolated.Bind(Board);
        Check(UnityEngine.Random.state.Equals(randomState),"Terrain generation does not consume gameplay Random");
        Object.Destroy(fixture); yield return null;
        Debug.Log("TERRAIN DENSITY RANGE: "+minimum+"–"+maximum);
        QuestProgressionRuntime.StartNewProgression(catalogue,0); yield return null; yield return null; Flow.SkipPosting(); yield return Ready();
        string sameSeed=Signature(); QuestProgressionRuntime.StartNewProgression(catalogue,0); yield return null; yield return null; Flow.SkipPosting(); yield return Ready();
        Check(Signature()==sameSeed,"Reconstructing the same seed produces the exact same ridge geometry and poses");

        QuestProgressionRuntime.StartNewProgression(catalogue,14); yield return null; yield return null;
        while(!Terrain.IsRevealing) yield return null;
        int contacts=Motion.Feedback.SmallImpacts; Flow.SkipPosting(); yield return Ready();
        Check(Terrain.IsRevealed&&!Terrain.IsRevealing&&Terrain.RevealProgress==1&&Motion.Feedback.SmallImpacts==contacts
            &&Board.InputEnabled&&Board.Stones.All(s=>s.IsReady&&s.CurrentOffset==Vector3.zero),"Skip during terrain rise restores the full landscape and all stones without synthetic impacts");

        QuestProgressionRuntime.StartNewProgression(catalogue,18); yield return null; yield return null;
        while(!Terrain.IsRevealing) yield return null;
        Flow.enabled=false; Flow.enabled=true; yield return Ready();
        Check(!Busy&&Board.InputEnabled&&Terrain.IsRevealed&&!Terrain.IsRevealing&&Terrain.GetComponentsInChildren<Collider>(true).Length==0,"Disabling during reveal settles terrain and restores input without blockers");

        QuestProgressionRuntime.StartNewProgression(catalogue,27); yield return null; yield return null;
        while(!Terrain.IsRevealing||Terrain.RevealProgress<0.25f) yield return null;
        SceneManager.LoadScene("PreMissionScene"); yield return Ready();
        Check(Board.Progression.Seed==27&&Terrain.IsRevealed&&Motion.Feedback.SmallImpacts==6,"Leaving mid-rise resumes unfinished terrain before the six ordinary drops on return");

        Motion.Feedback.SetReducedMotion(true); QuestProgressionRuntime.StartNewProgression(catalogue,31); yield return null; yield return null;
        bool reducedStill=true;
        while(Busy)
        {
            if(Terrain.IsRevealing) reducedStill&=Terrain.GetComponentsInChildren<MeshRenderer>().All(r=>Mathf.Abs(r.transform.localPosition.y-0.045f)<0.0001f);
            reducedStill&=Motion.Feedback.CameraOffset==Vector3.zero; yield return null;
        }
        Check(reducedStill&&Terrain.IsRevealed&&Motion.Feedback.SmallDust.particleCount==0&&Motion.Feedback.HeavyDust.particleCount==0,"Reduced motion settles the same landscape without decorative travel, dust or camera shake");
        Check(new SerializedObject(Motion.ProgressTrack).FindProperty("cameraAttention").floatValue==0
            &&new[]{"smallImpact","heavyImpact","handlingClip","progressClip","selectionClip","confirmationClip"}.All(n=>new SerializedObject(Motion.Feedback).FindProperty(n).objectReferenceValue==null)
            &&Board.Progression.ActiveAttempt==null&&SceneManager.GetActiveScene().name=="PreMissionScene","Silent audio hooks, stationary routine framing and the empty gameplay boundary remain intact");
        Motion.Feedback.SetReducedMotion(false); Time.timeScale=1;
    }
    static void Capture(string name)
    {
        if(captures==null||!images.Add(name)) return;
        var camera=Board.BoardCamera; var oldTarget=camera.targetTexture; int mask=camera.cullingMask;
        var canvases=Object.FindObjectsByType<Canvas>().Where(c=>c.isRootCanvas).ToArray();
        var modes=canvases.Select(c=>c.renderMode).ToArray(); var cameras=canvases.Select(c=>c.worldCamera).ToArray(); var distances=canvases.Select(c=>c.planeDistance).ToArray();
        var target=new RenderTexture(1600,900,24); var active=RenderTexture.active; var texture=new Texture2D(1600,900,TextureFormat.RGB24,false);
        try
        {
            camera.targetTexture=target; camera.cullingMask|=(1<<0)|(1<<5);
            foreach(var canvas in canvases) { canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest { destination=target });
            RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,1600,900),0,0); texture.Apply(); File.WriteAllBytes(Path.Combine(captures,name+".png"),texture.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active=active; camera.targetTexture=oldTarget; camera.cullingMask=mask;
            for(int i=0;i<canvases.Length;i++) { canvases[i].renderMode=modes[i]; canvases[i].worldCamera=cameras[i]; canvases[i].planeDistance=distances[i]; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Object.Destroy(target); Object.Destroy(texture);
        }
    }
}
