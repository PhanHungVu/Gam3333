using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// New-plan P2 real rendering, contact timing, propagation and recovery. Disposable project only.
public static class QuestWorldReactionsChecks
{
    const string Pending="QuestWorldReactionsChecks.Pending";
    static readonly Stack<IEnumerator> frames=new Stack<IEnumerator>();
    static readonly HashSet<string> images=new HashSet<string>(), shadowOffers=new HashSet<string>();
    static readonly Dictionary<Transform,(Vector3 pos,Quaternion rot)> terrainRest=new Dictionary<Transform,(Vector3,Quaternion)>();
    static readonly Dictionary<QuestStoneView,(Vector3 root,Quaternion rot)> stoneRest=new Dictionary<QuestStoneView,(Vector3,Quaternion)>();
    static readonly Dictionary<Transform,(float distance,float onset,float max,float lift)> terrainTraces=new Dictionary<Transform,(float,float,float,float)>();
    static readonly Dictionary<QuestStoneView,(float distance,float onset,float max,float lift)> stoneTraces=new Dictionary<QuestStoneView,(float,float,float,float)>();
    static bool started, traceWave, contactClean=true, posesStable=true, pixelVerified, shadowTightens=true;
    static int checks, mainContacts;
    static double deadline;
    static float contactTime, previousShadowHeight, previousShadowOpacity, previousShadowWidth;
    static string captures, shadowId;
    static PreMissionStoneMotion observed;
    static PlayerLoopSystem originalLoop;
    static bool observationInstalled;
    static Exception observationError;
    static PreMissionFlowController Flow=>Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board=>Flow.StoneBoard;
    static PreMissionStoneMotion Motion=>Board.Motion;
    static PreMissionContactShadow Shadow=>Motion.ContactShadow;
    static PreMissionBoardTerrain Terrain=>Motion.Terrain;
    static bool Busy=>Flow.IsPosting||Flow.IsTransitioning;
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.update+=Step;
    public static void RunBatch()
    {
        if(!Application.isBatchMode) throw new Exception("Disposable project only");
        PreMissionWorldReactionsSetup.UpgradeAssets(); PreMissionWorldReactionsSetup.UpgradeAssets();
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PreMissionStoneBoardSetup.BoardPath);
        if(prefab.GetComponentsInChildren<PreMissionContactShadow>(true).Length!=1) throw new Exception("Contact shadow setup duplicated its renderer/controller");
        GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline=null;
        EditorSceneManager.OpenScene("Assets/Scenes/PreMissionScene.unity");
        SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); checks++; Debug.Log("WORLD REACTIONS PASS: "+message); }
    static void Step()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        try
        {
            if(!started) { started=true; deadline=EditorApplication.timeSinceStartup+300; InstallObservation(); frames.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if(observationError!=null) throw observationError;
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("World reaction checks timed out");
            while(frames.Count>0)
            {
                var current=frames.Peek();
                if(!current.MoveNext()) { (current as IDisposable)?.Dispose(); frames.Pop(); continue; }
                if(current.Current is IEnumerator nested) { frames.Push(nested); continue; }
                return;
            }
            RestoreObservation(); PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false);
            Debug.Log("WORLD REACTIONS CHECKS COMPLETE: "+checks+"; captures: "+captures); EditorApplication.Exit(0);
        }
        catch(Exception exception) { RestoreObservation(); PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false); Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    static void InstallObservation()
    {
        originalLoop=PlayerLoop.GetCurrentPlayerLoop();
        PlayerLoopSystem Copy(PlayerLoopSystem system)
        {
            if(system.subSystemList!=null) system.subSystemList=system.subSystemList.Select(Copy).ToArray();
            return system;
        }
        var loop=Copy(originalLoop);
        bool Insert(ref PlayerLoopSystem system)
        {
            if(system.type==typeof(UnityEngine.PlayerLoop.PreLateUpdate))
            {
                var children=system.subSystemList.ToList();
                children.Add(new PlayerLoopSystem { type=typeof(QuestWorldReactionsChecks),updateDelegate=ObserveFrame });
                system.subSystemList=children.ToArray(); return true;
            }
            if(system.subSystemList!=null) for(int i=0;i<system.subSystemList.Length;i++)
                if(Insert(ref system.subSystemList[i])) return true;
            return false;
        }
        if(!Insert(ref loop)) throw new Exception("Player LateUpdate observation point missing");
        PlayerLoop.SetPlayerLoop(loop); observationInstalled=true;
    }
    static void ObserveFrame()
    {
        // Sample every player frame, after the actual motion/camera updates.
        // Editor update ticks can miss the short first kick under startup load.
        try { Observe(); } catch(Exception exception) { observationError=exception; }
    }
    static void RestoreObservation() { if(observationInstalled) { PlayerLoop.SetPlayerLoop(originalLoop); observationInstalled=false; } }
    static IEnumerator Ready()
    {
        // Editor update ticks need not correspond to player frames at a capped FPS.
        int frame=Time.frameCount+2;
        while(Time.frameCount<frame) yield return null;
        while(Busy || Board.Progression!=QuestProgressionRuntime.Current
            || Board.Stones.Count!=Board.Progression.Offers.Count
            || Board.Stones.Any(s=>Board.Progression.FindOffer(s.Offer.Id)==null)) yield return null;
    }
    static void Observe()
    {
        if(Flow==null||Board==null||Motion==null||Shadow==null||Terrain==null) return;
        if(observed!=Motion)
        {
            observed=Motion;
            Motion.Impacted+=stone=>
            {
                if(!stone.IsMain) return;
                mainContacts++; contactTime=Time.unscaledTime;
                foreach(var side in Board.Stones.Where(s=>!s.IsMain)) stoneRest[side]=(side.transform.position,side.Visual.localRotation);
                Debug.Log("P2 CONTACT: terrain="+Terrain.IsReacting+", neighbours="+Board.Stones.Count(s=>!s.IsMain&&s.IsReacting)+", dust="+Motion.Feedback.HeavyDust.particleCount+", chips="+Motion.Feedback.ImpactChips.particleCount);
                contactClean&=!Terrain.IsReacting&&Board.Stones.Where(s=>!s.IsMain).All(s=>!s.IsReacting)
                    &&Motion.Feedback.HeavyDust.particleCount>0&&Motion.Feedback.ImpactChips.particleCount>0;
            };
        }
        var falling=Shadow.Stone;
        if(falling!=null && !QuestProgressionRuntime.WasPresented(falling.Offer.Id) && falling.Visual.gameObject.activeSelf && falling.CurrentOffset.y>0.02f && Shadow.IsVisible)
        {
            shadowOffers.Add(falling.Offer.Id);
            if(shadowId!=falling.Offer.Id)
            {
                shadowId=falling.Offer.Id; previousShadowHeight=float.MaxValue; previousShadowOpacity=0; previousShadowWidth=float.MaxValue;
            }
            if(falling.CurrentOffset.y<=previousShadowHeight)
                shadowTightens&=Shadow.Opacity>=previousShadowOpacity-0.0001f&&Shadow.Footprint.x<=previousShadowWidth+0.0001f;
            previousShadowHeight=falling.CurrentOffset.y; previousShadowOpacity=Shadow.Opacity; previousShadowWidth=Shadow.Footprint.x;
            if(falling.IsMain && Shadow.Opacity>0.06f && falling.CurrentOffset.y>1 && !pixelVerified)
            { pixelVerified=true; Capture("p2-main-approaching-shadow",true); }
        }
        if(!traceWave) return;
        foreach(var stone in Board.Stones.Where(s=>!s.IsMain&&s.IsReady&&!s.IsReacting))
            if(!stoneRest.ContainsKey(stone)) stoneRest[stone]=(stone.transform.position,stone.Visual.localRotation);
        if(mainContacts==0) return;
        foreach(var stone in Board.Stones.Where(s=>!s.IsMain))
        {
            if(!stoneRest.TryGetValue(stone,out var rest)) continue;
            posesStable&=stone.IsReady&&!stone.IsMoving&&stone.HitCollider.enabled&&Mathf.Abs(stone.CurrentOffset.y)<0.03f;
            float angle=Quaternion.Angle(rest.rot,stone.Visual.localRotation);
            if(angle<0.001f) continue;
            if(!stoneTraces.TryGetValue(stone,out var trace)) trace=(Vector3.Distance(stone.transform.position,Board.Stones.Single(s=>s.IsMain).transform.position),Time.unscaledTime-contactTime,0,0);
            trace.max=Mathf.Max(trace.max,angle); trace.lift=Mathf.Max(trace.lift,stone.CurrentOffset.y); stoneTraces[stone]=trace;
        }
        foreach(var item in terrainRest)
        {
            if(item.Key==null) continue;
            float angle=Quaternion.Angle(item.Value.rot,item.Key.localRotation);
            if(angle<0.001f && (item.Key.localPosition-item.Value.pos).sqrMagnitude<0.0000000001f) continue;
            if(!terrainTraces.TryGetValue(item.Key,out var trace)) trace=(new Vector2(item.Value.pos.x,item.Value.pos.z).magnitude,Time.unscaledTime-contactTime,0,0);
            trace.max=Mathf.Max(trace.max,angle); trace.lift=Mathf.Max(trace.lift,item.Key.localPosition.y-item.Value.pos.y); terrainTraces[item.Key]=trace;
        }
        if(Terrain.IsReacting) Capture("p2-wave-through-terrain");
    }
    static bool AtRest()=>!Shadow.IsVisible&&Shadow.Stone==null&&!Terrain.IsReacting
        &&Board.Stones.All(s=>s.IsReady&&!s.IsMoving&&!s.IsReacting&&s.CurrentOffset==Vector3.zero&&s.HitCollider.enabled);
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Time.timeScale=0; Application.runInBackground=true; QualitySettings.vSyncCount=0; Application.targetFrameRate=-1;
        foreach(var device in InputSystem.devices.Where(d=>d is Mouse||d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
        captures=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"WorldReactionCaptures"); Directory.CreateDirectory(captures);
        yield return Ready();
        Check(shadowOffers.Count==6&&shadowTightens,"All six falling side stones get a landing shadow that tightens and darkens before contact (offers="+shadowOffers.Count+", monotonic="+shadowTightens+")");
        Check(AtRest()&&Motion.Feedback.SmallImpacts==6&&Motion.Feedback.HeavyImpacts==0,"Arrival shadows disappear after settling without changing the six ordinary drops");
        Check(Shadow.GetComponentsInChildren<Collider>(true).Length==0&&Shadow.Surface.sharedMaterial.GetFloat("_ZWrite")==0
            &&Shadow.Surface.shadowCastingMode==ShadowCastingMode.Off,"The transparent contact proxy has no collider, depth write or shadow casting");
        foreach(var renderer in Terrain.GetComponentsInChildren<MeshRenderer>()) terrainRest[renderer.transform]=(renderer.transform.localPosition,renderer.transform.localRotation);
        traceWave=true;
        for(int i=0;i<3;i++) { Check(QuestStoneBoardChecks.CompleteSmallForTest(Flow),"Tracked side completion "+(i+1)); yield return Ready(); }
        traceWave=false;
        Check(mainContacts==1&&contactClean&&Motion.Feedback.HeavyImpacts==1,"Dust, chips and impact begin once at contact; surrounding pieces wait for the outward wave (contacts="+mainContacts+", clean="+contactClean+", heavy="+Motion.Feedback.HeavyImpacts+", successes="+Board.Progression.SuccessfulSmallCompletions+")");
        Check(pixelVerified,"The heavy approach includes a visible contact-shadow rendering check");
        Check(stoneTraces.Count==6&&posesStable,"All six neighbours slide on the table while keeping ready colliders and no upward hop");
        var near=stoneTraces.Values.OrderBy(t=>t.distance).First(); var far=stoneTraces.Values.OrderBy(t=>t.distance).Last();
        Check(near.onset<=far.onset&&near.max>far.max*1.1f,"Actual nearby stone reactions start no later and reach a stronger tilt than distant ones (near="+near+", far="+far+")");
        var inner=terrainTraces.Values.OrderBy(t=>t.distance).Take(3).ToArray(); var outer=terrainTraces.Values.OrderByDescending(t=>t.distance).Take(3).ToArray();
        Check(terrainTraces.Count==Terrain.RidgeCount&&inner.Average(t=>t.onset)<outer.Average(t=>t.onset),"The outward impact reaches every terrain group, including the rim");
        Check(stoneRest.Where(item=>Board.Stones.Contains(item.Key)).All(item=>(item.Key.transform.position-item.Value.root).magnitude>=0.35f)
            &&terrainTraces.Count==Terrain.RidgeCount,
            "Every surviving side quest slides and every terrain group reacts; ridges yield to clear quest destinations");
        Check(terrainTraces.Values.Max(t=>t.max)<=5.1f&&stoneTraces.Values.Max(t=>t.max)<3.1f,"Tabletop terrain recoil and restrained skid tilt stay inside their authored caps");
        Debug.Log("AMPLIFIED IMPACT: side tilt="+stoneTraces.Values.Min(t=>t.max)+"–"+stoneTraces.Values.Max(t=>t.max)
            +", side lift="+stoneTraces.Values.Min(t=>t.lift)+"–"+stoneTraces.Values.Max(t=>t.lift)
            +", terrain="+terrainTraces.Count+", terrain tilt="+terrainTraces.Values.Min(t=>t.max)+"–"+terrainTraces.Values.Max(t=>t.max));
        Check(AtRest()&&terrainRest.All(item=>Quaternion.Angle(item.Key.localRotation,item.Value.rot)<0.001f)
            &&Terrain.BrokenCount==Terrain.PlanBreaks().Count,"The settling timeline clears transient rotations and shadow while retaining pushed positions and isolated rubble");
        Check(Board.Stones.All(s=>Board.StoneAt(Board.BoardCamera.WorldToScreenPoint(s.HitCollider.bounds.center))==s),"All six side quests and the centered main still raycast independently");
        Capture("p2-settled-board");
        var run=Board.Progression; var catalogue=run.Catalogue;
        SceneManager.LoadScene("PreMissionScene"); yield return Ready();
        Check(Board.Progression==run&&Motion.Feedback.HeavyImpacts==0&&AtRest(),"Scene return does not replay the shadow, main impact or surface wave");

        Shadow.enabled=false;
        int contacts=Motion.Feedback.SmallImpacts;
        Check(QuestStoneBoardChecks.CompleteSmallForTest(Flow),"A new offer can arrive with the optional shadow disabled"); yield return Ready();
        Check(!Shadow.IsVisible&&Board.InputEnabled&&Motion.Feedback.SmallImpacts==contacts+1,"Disabling the optional proxy preserves arrivals, contact feedback and input");
        Shadow.enabled=true;

        foreach(bool descent in new[]{false,true})
        {
            QuestProgressionRuntime.StartNewProgression(catalogue,descent?16:15); yield return null; yield return null; Flow.SkipPosting(); yield return Ready();
            for(int i=0;i<2;i++) { QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return Ready(); }
            int heavy=Motion.Feedback.HeavyImpacts; QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return null;
            while(!Motion.IsAnticipatingMain) yield return null;
            if(descent) while(!Board.Stones.Single(s=>s.IsMain).Visual.gameObject.activeSelf) yield return null;
            Flow.SkipPosting(); yield return Ready();
            Check(AtRest()&&Motion.Feedback.HeavyImpacts==heavy&&Board.InputEnabled,"Skip during heavy "+(descent?"descent":"anticipation")+" clears the proxy and wave without synthetic contact");
        }

        QuestProgressionRuntime.StartNewProgression(catalogue,19); yield return null; yield return null; Flow.SkipPosting(); yield return Ready();
        for(int i=0;i<2;i++) { QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return Ready(); }
        QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return null;
        while(!Terrain.IsReacting) yield return null;
        Flow.enabled=false; Flow.enabled=true; yield return Ready();
        Check(AtRest()&&Board.InputEnabled&&Motion.Feedback.BoardSurface.localPosition==Vector3.zero,"Disabling during the traveling wave restores terrain, input, board recoil and shadow");

        Motion.Feedback.SetReducedMotion(true); QuestProgressionRuntime.StartNewProgression(catalogue,23); yield return null; yield return null; Flow.SkipPosting(); yield return Ready();
        bool reducedStill=true;
        for(int i=0;i<3;i++)
        {
            QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return null; yield return null;
            while(Busy) { reducedStill&=!Shadow.IsVisible&&!Terrain.IsReacting&&Board.Stones.All(s=>!s.IsReacting&&s.CurrentOffset==Vector3.zero); yield return null; }
        }
        Check(reducedStill&&AtRest(),"Reduced motion suppresses approaching shadows and the entire local surface wave");
        Check(new SerializedObject(Motion.ProgressTrack).FindProperty("cameraAttention").floatValue==0
            &&new[]{"smallImpact","heavyImpact","handlingClip","progressClip","selectionClip","confirmationClip"}.All(n=>new SerializedObject(Motion.Feedback).FindProperty(n).objectReferenceValue==null)
            &&Board.Progression.ActiveAttempt==null&&SceneManager.GetActiveScene().name=="PreMissionScene","Routine stationary framing, six empty audio roles and the empty gameplay boundary remain intact");
        Motion.Feedback.SetReducedMotion(false); Time.timeScale=1;
    }
    static void Capture(string name,bool compareShadow=false)
    {
        if(captures==null||!images.Add(name)) return;
        var camera=Board.BoardCamera; var oldTarget=camera.targetTexture; int mask=camera.cullingMask;
        var canvases=Object.FindObjectsByType<Canvas>().Where(c=>c.isRootCanvas).ToArray();
        var modes=canvases.Select(c=>c.renderMode).ToArray(); var cameras=canvases.Select(c=>c.worldCamera).ToArray(); var distances=canvases.Select(c=>c.planeDistance).ToArray();
        var target=new RenderTexture(1600,900,24); var active=RenderTexture.active; var texture=new Texture2D(1600,900,TextureFormat.RGB24,false);
        bool shadowWasEnabled=Shadow.Surface.enabled;
        Color32[] Render()
        {
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest { destination=target });
            RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,1600,900),0,0); texture.Apply(); return texture.GetPixels32();
        }
        try
        {
            camera.targetTexture=target; camera.cullingMask|=(1<<0)|(1<<5);
            foreach(var canvas in canvases) { canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Canvas.ForceUpdateCanvases();
            if(compareShadow)
            {
                Shadow.Surface.enabled=false; var without=Render(); Shadow.Surface.enabled=shadowWasEnabled; var with=Render();
                int darker=0,maxDark=0,maxLight=0;
                for(int i=0;i<with.Length;i++)
                {
                    int delta=without[i].r+without[i].g+without[i].b-with[i].r-with[i].g-with[i].b;
                    if(delta>3) darker++; maxDark=Math.Max(maxDark,delta); maxLight=Math.Max(maxLight,-delta);
                }
                File.WriteAllBytes(Path.Combine(captures,"p2-shadow-on.png"),texture.EncodeToPNG());
                texture.SetPixels32(without); texture.Apply(); File.WriteAllBytes(Path.Combine(captures,"p2-shadow-off.png"),texture.EncodeToPNG());
                texture.SetPixels32(with); texture.Apply();
                Debug.Log("P2 SHADOW RENDER: opacity="+Shadow.Opacity+", enabled="+shadowWasEnabled+", active="+Shadow.Surface.gameObject.activeInHierarchy+", position="+Shadow.Surface.transform.position+", scale="+Shadow.Surface.transform.localScale+", shader="+Shadow.Surface.sharedMaterial.shader.name+", dark="+maxDark+", light="+maxLight);
                Check(darker>25,"URP renders actual soft landing darkening when only the proxy is enabled ("+darker+" pixels)");
            }
            else Render();
            Check(texture.GetPixels32().Count(p=>p.r>220&&p.b>220&&p.g<70)<1600*900/200,name+": no missing-shader output");
            File.WriteAllBytes(Path.Combine(captures,name+".png"),texture.EncodeToPNG());
        }
        finally
        {
            Shadow.Surface.enabled=shadowWasEnabled; RenderTexture.active=active; camera.targetTexture=oldTarget; camera.cullingMask=mask;
            for(int i=0;i<canvases.Length;i++) { canvases[i].renderMode=modes[i]; canvases[i].worldCamera=cameras[i]; canvases[i].planeDistance=distances[i]; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Object.Destroy(target); Object.Destroy(texture);
        }
    }
}
