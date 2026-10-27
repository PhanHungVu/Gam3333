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

// Real art/rendering, moving-front timing, permanent slides and isolated fracture.
public static class QuestTabletopShockwaveChecks
{
    const string Pending="QuestTabletopShockwaveChecks.Pending";
    static readonly Stack<IEnumerator> frames=new Stack<IEnumerator>();
    static readonly Dictionary<int,Vector3> starts=new Dictionary<int,Vector3>();
    static readonly HashSet<int> touched=new HashSet<int>();
    static readonly HashSet<string> capturesWritten=new HashSet<string>();
    static bool started, watch, frontTiming=true, stayedOnTable=true, sawFront;
    static bool landingSafe=true,clearanceCameraStill=true; static int clearanceFrames,anticipationFrames;
    static Vector3 boardCameraHome; static Quaternion boardCameraRotation; static float boardCameraSize;
    static int checks; static double deadline; static string captures;
    static PreMissionFlowController Flow=>Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board=>Flow.StoneBoard;
    static PreMissionStoneMotion Motion=>Board.Motion;
    static PreMissionShockwave Wave=>Motion.Shockwave;
    static PreMissionBoardTerrain Terrain=>Motion.Terrain;
    static bool Busy=>Flow.IsPosting||Flow.IsTransitioning;
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.update+=Step;
    public static void RunBatch()
    {
        if(!Application.isBatchMode) throw new Exception("Disposable project only");
        PreMissionShockwaveSetup.UpgradeAssets(); PreMissionShockwaveSetup.UpgradeAssets();
        GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline=null;
        EditorSceneManager.OpenScene("Assets/Scenes/PreMissionScene.unity"); SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); checks++; Debug.Log("TABLETOP PASS: "+message); }
    static void Step()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        try
        {
            if(!started) { started=true; deadline=EditorApplication.timeSinceStartup+300; frames.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors(); Observe();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("Tabletop checks timed out");
            while(frames.Count>0)
            {
                var current=frames.Peek(); if(!current.MoveNext()) { (current as IDisposable)?.Dispose(); frames.Pop(); continue; }
                if(current.Current is IEnumerator nested) { frames.Push(nested); continue; } return;
            }
            PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false);
            Debug.Log("TABLETOP SHOCKWAVE CHECKS COMPLETE: "+checks+"; captures: "+captures); EditorApplication.Exit(0);
        }
        catch(Exception exception) { PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false); Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    static IEnumerator Ready()
    {
        int frame=Time.frameCount+2; while(Time.frameCount<frame) yield return null;
        while(Busy||Board.Progression!=QuestProgressionRuntime.Current||Board.Stones.Count!=Board.Progression.Offers.Count
            ||Board.Stones.Any(s=>Board.Progression.FindOffer(s.Offer.Id)==null)) yield return null;
    }
    static void Observe()
    {
        if(!watch||Flow==null||Wave==null) return;
        if(Motion.IsClearingMainArea)
        {
            clearanceFrames++;
            clearanceCameraStill&=Vector3.Distance(Board.BoardCamera.transform.position,boardCameraHome)<0.001f
                &&Quaternion.Angle(Board.BoardCamera.transform.rotation,boardCameraRotation)<0.001f
                &&Mathf.Abs(Board.BoardCamera.orthographicSize-boardCameraSize)<0.001f;
        }
        if(Motion.IsAnticipatingMain)
        {
            anticipationFrames++; var main=Board.ImpactExclusions(0)[0];
            landingSafe&=Board.Stones.Where(s=>!s.IsMain).All(s=>!Board.ProjectTerrainBounds(s.HitCollider.bounds).Overlaps(main));
            Capture("center-cleared-before-drop");
        }
        if(!Wave.IsActive) return;
        sawFront|=Wave.IsVisible;
        foreach(var stone in Board.Stones.Where(s=>!s.IsMain))
        {
            var start=starts[stone.Offer.Slot]; var delta=stone.transform.localPosition-start;
            stayedOnTable&=Mathf.Abs(delta.y)<0.0001f&&Mathf.Abs(stone.CurrentOffset.y)<0.03f;
            if(new Vector2(delta.x,delta.z).magnitude>0.02f && touched.Add(stone.Offer.Slot))
                frontTiming&=Wave.Radius+0.05f>=new Vector2(start.x,start.z).magnitude;
        }
        if(Wave.IsVisible&&Wave.Radius>4&&Wave.Radius<7) Capture("traveling-front",true);
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Time.timeScale=0; Application.runInBackground=true;
        foreach(var device in InputSystem.devices.Where(d=>d is Mouse||d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
        captures=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"TabletopShockwaveCaptures"); Directory.CreateDirectory(captures);
        yield return Ready();
        Motion.Impacted+=stone=> {
            if(!stone.IsMain) return;
            starts.Clear(); foreach(var side in Board.Stones.Where(s=>!s.IsMain)) starts[side.Offer.Slot]=side.transform.localPosition;
        };
        Check(Wave!=null&&Board.GetComponentsInChildren<PreMissionShockwave>(true).Length==1,"One shared wave owns the visible front and physical interaction timing");
        Check(!Board.transform.Find("Impact Inlays").gameObject.activeSelf
            &&Board.Stones.Any(s=>Board.ProjectTerrainBounds(s.HitCollider.bounds).Overlaps(Board.ImpactExclusions(0)[0])),
            "Before unlock, side quests can occupy the center and no main-location marker is shown");
        var table=Board.transform.Find("Board Surface/Art Table");
        Check(table!=null&&table.gameObject.activeInHierarchy&&Board.transform.Find("Board Surface/Art Slab")==null&&Board.transform.Find("Art Table")==null,
            "The supplied table alone is the physical board surface; no oversized slab remains");
        Check(table.GetComponentsInChildren<MeshFilter>().Any(m=>AssetDatabase.GetAssetPath(m.sharedMesh)=="Assets/Models/table.glb")
            &&Board.Stones.All(s=>s.Visual.Find("Faceted Rock")==null&&UsesSlab(s)),
            "All quest objects use the actual slab GLB, replacing the prototype rocks");
        var sourceSlab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/slab.glb").GetComponentInChildren<MeshFilter>().sharedMesh;
        Check(Board.Stones.All(s=>s.Visual.Find("Art Quest Slab").GetComponentInChildren<MeshFilter>().sharedMesh.normals.SequenceEqual(sourceSlab.normals.Select(n=>-n))),
            "The quest render copy corrects the slab's inward normals without changing source vertices or UVs");
        Check(Board.Stones.All(s=>s.Visual.Find("Art Quest Slab").GetComponentsInChildren<MeshRenderer>().All(r=>AssetDatabase.GetAssetPath(r.sharedMaterial.GetTexture("_BaseMap"))=="Assets/Materials/TableandSlabTextures/SlabTex.png"))
            &&table.GetComponentsInChildren<MeshRenderer>().All(r=>AssetDatabase.GetAssetPath(r.sharedMaterial.GetTexture("_BaseMap"))=="Assets/Materials/TableandSlabTextures/TableTex.png"),"Quest slabs and table use their respective supplied texture files");
        var surface=table.GetComponentInChildren<MeshFilter>();
        Check(Mathf.Abs(surface.transform.TransformPoint(Vector3.up).y-0.02f)<0.001f&&Board.Stones.All(s=>Mathf.Abs(s.RockBounds.min.y-0.03f)<0.001f),
            "The actual flat tabletop, terrain base and quest slabs share the same contact plane");
        Check(table.GetComponentsInChildren<MeshRenderer>().Concat(Board.Stones.SelectMany(s=>s.Visual.Find("Art Quest Slab").GetComponentsInChildren<MeshRenderer>())).All(r=>r.receiveShadows&&r.shadowCastingMode==ShadowCastingMode.On),
            "Table and quest slab renderers cast and receive real lighting shadows");
        Check(!Board.transform.Find("Board Surface/Board Slab").gameObject.activeSelf
            &&!Board.transform.Find("Board Surface/Inset Surface").gameObject.activeSelf,"Old slab/inset placeholders are disabled");
        Check(Motion.Feedback.SmallImpacts==6&&Motion.Feedback.HeavyImpacts==0&&Motion.Feedback.SmallDust.main.startSize.constantMax>=0.20f,
            "Small quest arrivals now use the former main-dust profile");
        foreach(var stone in Board.Stones) starts[stone.Offer.Slot]=stone.transform.localPosition;
        var initial=Board.Stones.ToDictionary(s=>s.Offer.Slot,s=>s.transform.localPosition);
        boardCameraHome=Board.BoardCamera.transform.position; boardCameraRotation=Board.BoardCamera.transform.rotation; boardCameraSize=Board.BoardCamera.orthographicSize;
        int expectedBreaks=Terrain.PlanBreaks().Count;
        Check(expectedBreaks>0,"The generated terrain contains isolated one- or two-ridge groups to fracture");
        Capture("art-before-impact",false,true); watch=true;
        for(int i=0;i<3;i++)
        {
            Check(QuestStoneBoardChecks.CompleteSmallForTest(Flow),"Tracked side completion "+(i+1)); yield return Ready();
            if(i<2) Check(!QuestProgressionRuntime.WasLandingCleared(Board.Progression)&&Board.Stones.All(s=>s.transform.localPosition==initial[s.Offer.Slot]),
                "Side completions before the main unlock keep the full-table locations, success "+(i+1));
        }
        watch=false;
        Check(clearanceFrames>2&&clearanceCameraStill&&anticipationFrames>2&&landingSafe,
            "Slabs clear the main footprint before descent; the camera stays fixed during their clearance");
        Check(sawFront&&frontTiming&&touched.Count==6,"The visible moving front reaches each stone before that stone starts sliding");
        Check(stayedOnTable,"Side quests slide along the surface rather than hopping above it");
        var distances=Board.Stones.Where(s=>!s.IsMain).Select(s=>Vector3.Distance(starts[s.Offer.Slot],s.transform.localPosition)).ToArray();
        Check(distances.All(d=>d>=0.35f),"Every small quest is visibly pushed outward ("+string.Join(",",distances.Select(d=>d.ToString("F2")))+")");
        Check(Board.Stones.Where(s=>!s.IsMain).All(s=>Vector3.Dot(s.transform.localPosition-starts[s.Offer.Slot],starts[s.Offer.Slot])>0),"Every displacement points away from the main impact");
        Check(Terrain.BrokenCount==expectedBreaks&&Motion.Feedback.TerrainBreaks==expectedBreaks,"Every isolated group fractures once when hit; clustered ridges survive");
        Check(Board.Stones.Where(s=>s.IsMain).All(s=>UsesSlab(s)
            &&s.RockBounds.size.x>Board.Stones.Where(other=>!other.IsMain).Max(other=>other.RockBounds.size.x)*1.4f),"The main quest also uses a slab and remains visibly larger than side quests");
        Check(Terrain.GetComponentsInChildren<MeshFilter>().Count(m=>m.name.StartsWith("Broken Ridge"))>=expectedBreaks*2,"Broken ridges leave actual separated mesh fragments on the board");
        Check(Motion.Feedback.HeavyPlume!=null&&Motion.Feedback.HeavyDust.main.startSize.constantMax>Motion.Feedback.SmallDust.main.startSize.constantMax*2
            &&Motion.Feedback.HeavyDust.main.maxParticles>=300,"The main impact has a larger ground burst and a separate rising dust plume");
        Check(!Wave.IsActive&&!Wave.IsVisible&&Board.InputEnabled&&Board.Stones.All(s=>Board.StoneAt(Board.BoardCamera.WorldToScreenPoint(s.HitCollider.bounds.center))==s),"All seven pushed/resting quest colliders are synchronized and independently selectable");
        Capture("art-after-impact");
        var run=Board.Progression; var catalogue=run.Catalogue;
        var final=Board.Stones.Where(s=>!s.IsMain).ToDictionary(s=>s.Offer.Slot,s=>s.transform.localPosition);
        SceneManager.LoadScene("PreMissionScene"); yield return Ready();
        Check(Board.Progression==run&&Board.Stones.Where(s=>!s.IsMain).All(s=>s.transform.localPosition==final[s.Offer.Slot])
            &&Terrain.BrokenCount==expectedBreaks&&Motion.Feedback.HeavyImpacts==0,"Scene return remembers pushed quest locations and rubble without replaying the impact");
        Check(QuestStoneBoardChecks.CompleteSmallForTest(Flow),"A further real side success replaces an offer after the impact"); yield return Ready();
        Check(Board.Stones.Where(s=>!s.IsMain).All(s=>s.transform.localPosition==final[s.Offer.Slot]),"Replacement quest rocks drop into their moved slot instead of snapping back");

        QuestProgressionRuntime.StartNewProgression(catalogue,31); yield return Ready();
        Check(!QuestProgressionRuntime.HasShockwave(Board.Progression)&&Terrain.BrokenCount==0,"A genuinely new progression restores the undamaged arrangement");
        for(int i=0;i<2;i++) { QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return Ready(); }
        QuestStoneBoardChecks.CompleteSmallForTest(Flow);
        while(!Wave.IsActive||Wave.Radius<2) yield return null;
        int heavy=Motion.Feedback.HeavyImpacts; Flow.SkipPosting(); yield return Ready();
        Check(!Wave.IsVisible&&!Wave.IsActive&&Motion.Feedback.HeavyImpacts==heavy&&Board.InputEnabled
            &&Terrain.BrokenCount==Terrain.PlanBreaks().Count,"Skipping a traveling wave applies its final slides/rubble without extra impacts or stuck input");

        QuestProgressionRuntime.StartNewProgression(catalogue,37); yield return Ready();
        for(int i=0;i<2;i++) { QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return Ready(); }
        QuestStoneBoardChecks.CompleteSmallForTest(Flow); while(!Motion.IsClearingMainArea) yield return null;
        var clearingRun=Board.Progression; var landingPoses=Board.Stones.Where(s=>!s.IsMain).ToDictionary(s=>s.Offer.Slot,s=>Board.LandingSlotPosition(s.Offer.Slot,s.transform.localPosition.y));
        SceneManager.LoadScene("PreMissionScene"); yield return Ready();
        Check(Board.Progression==clearingRun&&QuestProgressionRuntime.HasShockwave(clearingRun)&&Motion.Feedback.HeavyImpacts==1
            &&Board.Stones.Where(s=>!s.IsMain).All(s=>Vector3.Distance(s.transform.localPosition,landingPoses[s.Offer.Slot])>=0.349f),
            "Leaving during clearance restores its saved layout and completes one main drop on return");

        Motion.Feedback.SetReducedMotion(true); QuestProgressionRuntime.StartNewProgression(catalogue,33); yield return Ready();
        bool quiet=true;
        for(int i=0;i<3;i++)
        {
            QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return null;
            while(Busy) { quiet&=!Wave.IsVisible; yield return null; } yield return Ready();
        }
        Check(quiet&&QuestProgressionRuntime.HasShockwave(Board.Progression)&&Terrain.BrokenCount==Terrain.PlanBreaks().Count
            &&Motion.Feedback.HeavyDust.particleCount==0&&Motion.Feedback.HeavyPlume.particleCount==0,"Reduced motion applies the same final physical outcomes without the traveling visual or dust");
        Check(new[]{"smallImpact","heavyImpact","handlingClip","progressClip","selectionClip","confirmationClip"}.All(n=>new SerializedObject(Motion.Feedback).FindProperty(n).objectReferenceValue==null)
            &&Board.Progression.ActiveAttempt==null&&SceneManager.GetActiveScene().name=="PreMissionScene","Audio and gameplay hooks remain empty; no gameplay scene is loaded");
        // The six destinations must remain safe across randomized terrain/layouts,
        // rather than merely looking right for the scene's default seed.
        float shortest=float.MaxValue;
        for(int seed=0;seed<64;seed++)
        {
            QuestProgressionRuntime.StartNewProgression(catalogue,seed);
            int frame=Time.frameCount+2; while(Time.frameCount<frame) yield return null;
            Flow.SkipPosting(); yield return Ready();
            var mainArea=Board.ImpactExclusions(0)[0]; var initialAreas=Board.Stones.Select(s=>Board.ProjectTerrainBounds(s.HitCollider.bounds)).ToArray();
            Check(!Board.transform.Find("Impact Inlays").gameObject.activeSelf&&initialAreas.Any(a=>a.Overlaps(mainArea))
                &&!initialAreas.Where((a,i)=>initialAreas.Skip(i+1).Any(b=>a.Overlaps(b))).Any(),
                "64-seed sweep: center is available before unlock with six separate selection areas, seed "+seed);
            var landingAreas=new List<Rect>(); bool landingClear=true;
            foreach(var stone in Board.Stones)
            {
                var bounds=stone.HitCollider.bounds; bounds.center+=Board.LandingSlotPosition(stone.Offer.Slot,stone.transform.localPosition.y)-stone.transform.localPosition;
                var area=Board.ProjectTerrainBounds(bounds); landingClear&=!mainArea.Overlaps(area)&&!landingAreas.Any(a=>a.Overlaps(area)); landingAreas.Add(area);
            }
            Check(landingClear,"64-seed sweep: the planned landing layout clears the main without side overlaps, seed "+seed);
            var terrainMoves=Terrain.PlanDisplacements(Vector3.zero);
            var obstacles=Terrain.SurvivingDestinationBounds(terrainMoves,Terrain.PlanBreaks());
            var moves=Board.PlanShockwaveSlides(0.85f,obstacles);
            var waveMoves=Board.Stones.ToDictionary(s=>s.Offer.Slot,s=>s.transform.localPosition+new Vector3(moves[s.Offer.Slot].x,0,moves[s.Offer.Slot].y)-Board.LandingSlotPosition(s.Offer.Slot,s.transform.localPosition.y));
            shortest=Mathf.Min(shortest,waveMoves.Values.Min(v=>v.magnitude));
            Check(moves.Count==6&&waveMoves.All(m=>m.Value.magnitude>=0.349f),"64-seed sweep: all six stones have a visible safe outward slide after landing clearance, seed "+seed);
            var areas=new List<Rect>(); bool safe=true;
            foreach(var stone in Board.Stones)
            {
                var hit=stone.HitCollider;
                // This fixture's board is at the world origin with unit scale.
                var bounds=hit.bounds;
                var offset=moves[stone.Offer.Slot]; bounds.center+=new Vector3(offset.x,0,offset.y);
                var area=Board.ProjectTerrainBounds(bounds);
                var landing=Board.LandingSlotPosition(stone.Offer.Slot,stone.transform.localPosition.y);
                safe&=Vector3.Dot(waveMoves[stone.Offer.Slot],landing)>0
                    &&bounds.min.x>=-10.2f&&bounds.max.x<=10.2f&&bounds.min.z>=-5.2f&&bounds.max.z<=5.2f
                    &&!areas.Any(a=>a.Overlaps(area))&&!obstacles.Any(b=>Board.ProjectTerrainBounds(b).Overlaps(area))
                    &&!area.Overlaps(Board.ImpactExclusions(0)[0]);
                areas.Add(area);
            }
            Check(safe,"64-seed sweep: destinations stay on the art slab and clear of other quests/surviving ridges, seed "+seed);
        }
        Debug.Log("SHORTEST SAFE SLIDE: "+shortest);
        Motion.Feedback.SetReducedMotion(false); Time.timeScale=1;
    }
    static void Capture(string name,bool compareFront=false,bool compareShadows=false)
    {
        if(!capturesWritten.Add(name)) return;
        var camera=Board.BoardCamera; var oldTarget=camera.targetTexture; int mask=camera.cullingMask;
        var canvases=Object.FindObjectsByType<Canvas>().Where(c=>c.isRootCanvas).ToArray();
        var modes=canvases.Select(c=>c.renderMode).ToArray(); var cameras=canvases.Select(c=>c.worldCamera).ToArray(); var distances=canvases.Select(c=>c.planeDistance).ToArray();
        var target=new RenderTexture(1600,900,24); var active=RenderTexture.active; var texture=new Texture2D(1600,900,TextureFormat.RGB24,false);
        bool enabled=Wave.Ring.enabled;
        var lights=Board.GetComponentsInChildren<Light>().Where(l=>l.shadows!=LightShadows.None).ToArray(); var shadows=lights.Select(l=>l.shadows).ToArray();
        Color32[] Render()
        {
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest { destination=target }); RenderTexture.active=target;
            texture.ReadPixels(new Rect(0,0,1600,900),0,0); texture.Apply(); return texture.GetPixels32();
        }
        try
        {
            camera.targetTexture=target; camera.cullingMask|=1<<0|1<<5;
            foreach(var canvas in canvases) { canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Canvas.ForceUpdateCanvases();
            if(compareShadows)
            {
                foreach(var light in lights) light.shadows=LightShadows.None; var without=Render();
                for(int i=0;i<lights.Length;i++) lights[i].shadows=shadows[i]; var with=Render(); int darker=0;
                for(int i=0;i<with.Length;i++) if(without[i].r+without[i].g+without[i].b-with[i].r-with[i].g-with[i].b>15) darker++;
                File.WriteAllBytes(Path.Combine(captures,name+"-shadows-off.png"),EncodePixels(without));
                Check(darker>300,"Real lighting shadows visibly land on the supplied table ("+darker+" darker pixels)");
            }
            else if(compareFront)
            {
                Wave.Ring.enabled=false; var without=Render(); Wave.Ring.enabled=enabled; var with=Render(); int changed=0;
                for(int i=0;i<with.Length;i++) if(Math.Abs(with[i].r-without[i].r)+Math.Abs(with[i].g-without[i].g)+Math.Abs(with[i].b-without[i].b)>8) changed++;
                File.WriteAllBytes(Path.Combine(captures,name+".png"),texture.EncodeToPNG());
                Debug.Log("FRONT RENDER: "+Wave.Ring.bounds+", mesh="+Wave.Ring.GetComponent<MeshFilter>().sharedMesh.vertexCount+", layer="+Wave.Ring.gameObject.layer+", material="+Wave.Ring.sharedMaterial.name+", alpha="+Wave.Ring.GetComponent<MeshFilter>().sharedMesh.colors.Max(c=>c.a));
                Check(changed>150,"The traveling front visibly renders on the art slab ("+changed+" changed pixels)");
            }
            else Render();
            Check(texture.GetPixels32().Count(p=>p.r>220&&p.b>220&&p.g<70)<1600*900/200,name+": no missing-shader output");
            File.WriteAllBytes(Path.Combine(captures,name+".png"),texture.EncodeToPNG());
        }
        finally
        {
            Wave.Ring.enabled=enabled; RenderTexture.active=active; camera.targetTexture=oldTarget; camera.cullingMask=mask;
            for(int i=0;i<lights.Length;i++) if(lights[i]!=null) lights[i].shadows=shadows[i];
            for(int i=0;i<canvases.Length;i++) { canvases[i].renderMode=modes[i]; canvases[i].worldCamera=cameras[i]; canvases[i].planeDistance=distances[i]; }
            Board.RefreshCameraFrame(); Object.Destroy(target); Object.Destroy(texture);
        }
    }
    static byte[] EncodePixels(Color32[] pixels)
    {
        var image=new Texture2D(1600,900,TextureFormat.RGB24,false); image.SetPixels32(pixels); image.Apply();
        var png=image.EncodeToPNG(); Object.Destroy(image); return png;
    }
    static bool UsesSlab(QuestStoneView stone)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/slab.glb").GetComponentInChildren<MeshFilter>().sharedMesh;
        var rendered=stone.Visual.Find("Art Quest Slab").GetComponentInChildren<MeshFilter>().sharedMesh;
        return rendered.vertices.SequenceEqual(source.vertices)&&rendered.uv.SequenceEqual(source.uv);
    }
}
