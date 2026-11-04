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
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class QuestLabelReadabilityChecks
{
    const string Pending = "QuestLabelReadabilityChecks.Pending";
    static bool running;
    static double readyAt, deadline;
    static int lastFrame = -1, checks;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static PreMissionFlowController Flow => Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board => Flow.StoneBoard;
    static string capturePath;
    [InitializeOnLoadMethod] static void Register()
    { EditorApplication.update += Step; readyAt = EditorApplication.timeSinceStartup + 3; }
    public static void RunBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        SessionState.SetBool(Pending, true); readyAt = EditorApplication.timeSinceStartup + 3;
        Debug.Log("QUEST LABEL REVIEW REQUESTED");
    }
    static void Step()
    {
        if (SessionState.GetBool(Pending, false) && !EditorApplication.isPlaying)
        {
            if (EditorApplication.isCompiling) { readyAt = EditorApplication.timeSinceStartup + 3; return; }
            if (EditorApplication.timeSinceStartup < readyAt) return;
            try
            {
                SessionState.SetBool(Pending, false); Debug.Log("QUEST LABEL PREPARING OWNED ASSETS");
                PreMissionPortraitSlabSetup.UpgradeAssets(); PreMissionPortraitSlabSetup.UpgradeAssets();
                GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                QualitySettings.renderPipeline = null; EditorSceneManager.OpenScene("Assets/Scenes/PreMissionScene.unity");
                running = true; deadline = EditorApplication.timeSinceStartup + 180; EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
        }
        if (!running || !EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame == Time.frameCount) return;
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        lastFrame = Time.frameCount;
        try
        {
            if (steps.Count == 0) steps.Push(Run());
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Quest label review timed out");
            while (steps.Count > 0)
            {
                var step = steps.Peek(); if (!step.MoveNext()) { (step as IDisposable)?.Dispose(); steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; } return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Check(bool valid, string message)
    { if (!valid) throw new Exception(message); checks++; Debug.Log("QUEST LABEL PASS: " + message); }
    static void Finish(Exception error)
    {
        running = false; SessionState.SetBool(Pending, false); Time.captureFramerate = 0; PreMissionReliabilityChecks.EndLogAudit();
        if (error != null) Debug.LogException(error);
        Debug.Log("QUEST LABEL CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
    static IEnumerator Frames(int count = 2)
    { int until = Time.frameCount + count; while (Time.frameCount < until) yield return null; }
    static IEnumerator Ready()
    {
        yield return Frames();
        while (Flow.IsPosting || Flow.IsTransitioning || Board.Stones.Count != Board.Progression.Offers.Count) yield return null;
        yield return Frames();
    }
    static T Get<T>(object owner,string name) => (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
    static void Set(object owner,string name,object value) => owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(owner,value);
    static void CheckAtlas(Material material,Rect painted,IEnumerable<Mesh> meshes,string textureName)
    {
        Check(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")).EndsWith(textureName),"Slab material uses the supplied "+textureName);
        var scale=material.GetTextureScale("_BaseMap"); var offset=material.GetTextureOffset("_BaseMap");
        var safe=Rect.MinMaxRect(painted.xMin-.0001f,painted.yMin-.0001f,painted.xMax+.0001f,painted.yMax+.0001f);
        Check(meshes.Where(m=>m!=null).All(m=>m.uv.All(uv=>safe.Contains(Vector2.Scale(uv,scale)+offset))),textureName+" samples only its painted region across all mesh UVs");
    }
    static void CheckRadialLayouts()
    {
        var board=Board; var stones=board.Stones.Where(s=>!s.IsMain).ToArray();
        var poses=stones.Select(s=>s.transform.localPosition).ToArray(); var rotations=stones.Select(s=>s.transform.localRotation).ToArray();
        var names=new[]{"run","slotPositions","slotAngles","landingPositions","impactPositions","projectedFootprints","tabletopCorners","projectedX","projectedZ","footprintCacheValid"};
        var saved=names.Select(n=>Get<object>(board,n)).ToArray(); bool radial=true,stationary=true,paths=true,central=false,table=true,cleared=true;
        var mainPrefab=Get<QuestStoneView>(board,"mainStonePrefab");
        try
        {
            for(int seed=0;seed<64;seed++)
            {
                Set(board,"run",new QuestProgressionSession(Flow.Session.Progression.Catalogue,seed));
                typeof(PreMissionStoneBoard).GetMethod("PreparePlacementData",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(board,null);
                var starts=Get<Vector2[]>(board,"slotPositions"); var landings=Get<Vector2[]>(board,"landingPositions");
                var targets=Get<Vector2[]>(board,"impactPositions"); var angles=Get<float[]>(board,"slotAngles");
                var first=new Rect[stones.Length]; var last=new Rect[stones.Length];
                for(int stage=0;stage<3;stage++)
                {
                    var points=stage==0?starts:stage==1?landings:targets;
                    for(int i=0;i<stones.Length;i++) {int slot=stones[i].Offer.Slot;stones[i].transform.localPosition=new Vector3(points[slot].x,.03f,points[slot].y);stones[i].transform.localRotation=Quaternion.Euler(0,angles[slot],0);}
                    Physics.SyncTransforms();
                    for(int i=0;i<stones.Length;i++)
                    {
                        var area=Project(stones[i].HitCollider.bounds,false);
                        if(stage==0) first[i]=area; else if(stage==2) last[i]=area;
                        if(stage>0)
                        {
                            var collider=(BoxCollider)stones[i].HitCollider; var radii=Get<Vector2>(board,"tableRadii");
                            for(int x=-1;x<=1;x+=2) for(int z=-1;z<=1;z+=2)
                            {
                                var corner=board.transform.InverseTransformPoint(collider.transform.TransformPoint(collider.center+Vector3.Scale(collider.size*.5f,new Vector3(x,0,z))));
                                table&=corner.x*corner.x/(radii.x*radii.x)+corner.z*corner.z/(radii.y*radii.y)<=1.0001f && corner.x>=-8.451f;
                            }
                        }
                    }
                }
                var projectedMain=(Rect)typeof(PreMissionStoneBoard).GetMethod("ProjectedHitArea",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(board,new object[]{mainPrefab,Vector2.zero,0f});
                float margin=Get<float>(board,"slabSpacing")*2;
                var safeMain=Rect.MinMaxRect(projectedMain.xMin-margin,projectedMain.yMin-margin,projectedMain.xMax+margin,projectedMain.yMax+margin);
                var small=Get<QuestStoneView>(board,"smallStonePrefab"); bool hasCenter=false;
                for(int i=0;i<stones.Length;i++)
                {
                    int slot=stones[i].Offer.Slot; var start=starts[slot];
                    var startArea=(Rect)typeof(PreMissionStoneBoard).GetMethod("ProjectedHitArea",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(board,new object[]{small,start,angles[slot]});
                    bool inside=startArea.Overlaps(safeMain); hasCenter|=startArea.Overlaps(projectedMain);
                    if(!inside) stationary&=Vector2.Distance(start,landings[slot])<.0001f;
                    foreach(var point in new[]{landings[slot],targets[slot]})
                    {
                        var travel=point-start;
                        radial&=Mathf.Abs(start.x*travel.y-start.y*travel.x)<.001f && Vector2.Dot(start,travel)>=-.0001f;
                        var area=(Rect)typeof(PreMissionStoneBoard).GetMethod("ProjectedHitArea",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(board,new object[]{small,point,angles[slot]});
                        cleared&=!area.Overlaps(projectedMain);
                    }
                    var sweep=Rect.MinMaxRect(Mathf.Min(first[i].xMin,last[i].xMin),Mathf.Min(first[i].yMin,last[i].yMin),Mathf.Max(first[i].xMax,last[i].xMax),Mathf.Max(first[i].yMax,last[i].yMax));
                    for(int j=0;j<i;j++)
                    {
                        var other=Rect.MinMaxRect(Mathf.Min(first[j].xMin,last[j].xMin),Mathf.Min(first[j].yMin,last[j].yMin),Mathf.Max(first[j].xMax,last[j].xMax),Mathf.Max(first[j].yMax,last[j].yMax));
                        paths&=!sweep.Overlaps(other);
                    }
                }
                central|=hasCenter;
            }
        }
        finally
        {
            for(int i=0;i<names.Length;i++) Set(board,names[i],saved[i]);
            for(int i=0;i<stones.Length;i++) {stones[i].transform.localPosition=poses[i];stones[i].transform.localRotation=rotations[i];}
            Physics.SyncTransforms();
        }
        Debug.Log($"QUEST LAYOUT FLAGS: radial={radial}; stationary={stationary}; paths={paths}; cleared={cleared}; table={table}; center={central}");
        Check(radial&&stationary,"64 layouts only clear outward; slabs beyond the safety margin keep their initial spots");
        Check(paths,"64 layouts keep entire rendered collider travel corridors separate, covering every animation timing");
        Check(cleared&&table,"64 layouts clear the main landing footprint and remain on the tabletop");
        Check(central,"Side quests can still occupy the center before main unlock");
    }
    static IEnumerator ObserveMainArrival(Dictionary<int,Vector2> initial)
    {
        yield return Frames(); bool clearing=false,wave=false,radial=true,separate=true; var previous=new Dictionary<int,float>();
        while(Flow.IsPosting||Flow.IsTransitioning||Board.Stones.Count!=Board.Progression.Offers.Count)
        {
            if(Board.Motion.IsClearingMainArea||Board.Motion.Shockwave.IsActive)
            {
                clearing|=Board.Motion.IsClearingMainArea; wave|=Board.Motion.Shockwave.IsActive; Physics.SyncTransforms();
                var side=Board.Stones.Where(s=>!s.IsMain).ToArray(); var areas=side.Select(s=>Project(s.HitCollider.bounds,false)).ToArray();
                separate&=!areas.Where((r,i)=>areas.Skip(i+1).Any(r.Overlaps)).Any();
                foreach(var stone in side)
                {
                    var p=new Vector2(stone.transform.localPosition.x,stone.transform.localPosition.z); var from=initial[stone.Offer.Slot]; var movement=p-from;
                    radial&=Mathf.Abs(from.x*movement.y-from.y*movement.x)<.002f && Vector2.Dot(from,movement)>=-.001f;
                    if(previous.TryGetValue(stone.Offer.Slot,out float radius)) radial&=p.magnitude>=radius-.001f;
                    previous[stone.Offer.Slot]=p.magnitude;
                }
            }
            yield return null;
        }
        Check(clearing&&wave&&radial,"Live main arrival clears and pushes slabs outward without crossing the center or reversing");
        Check(separate,"Slab selection bodies never pass through each other during live clearance and shockwave");
    }
    static GameObject Heading(QuestStoneView stone) => (GameObject)new SerializedObject(stone).FindProperty("categoryInscription").objectReferenceValue;
    static Bounds BoundsOf(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        var bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds); return bounds;
    }
    static Rect Project(Bounds bounds, bool pixels)
    {
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
        {
            var point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
            Vector2 projected = pixels ? Board.BoardCamera.WorldToScreenPoint(point) : Board.BoardCamera.WorldToViewportPoint(point);
            min = Vector2.Min(min, projected); max = Vector2.Max(max, projected);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.captureFramerate = 60;
        capturePath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ReadabilityCaptures"); Directory.CreateDirectory(capturePath);
        foreach (var input in UnityEngine.InputSystem.InputSystem.devices.Where(d => d is UnityEngine.InputSystem.Mouse || d is UnityEngine.InputSystem.Keyboard).ToArray())
            UnityEngine.InputSystem.InputSystem.DisableDevice(input);
        yield return Ready();
        var controls = Object.FindObjectsByType<PreMissionQuestTestButton>();
        Check(controls.Length == 1, "Repeated setup adds exactly one temporary scene button");
        var test = controls[0]; var button = test.GetComponent<Button>();
        Check(test.CanAdvance && button.interactable, "Temporary +1 Quest button is usable after posting");
        foreach (var stone in Board.Stones)
            Check(Heading(stone).GetComponentsInChildren<Renderer>().All(r => r.sharedMaterial.shader.name == "Universal Render Pipeline/Unlit"),
                "Supplied category meshes use consistently bright unlit lettering");
        var back = GameObject.Find("Back Slab"); var backArea = Project(BoundsOf(back), false);
        Check(backArea.xMin > 0 && backArea.xMax < 1 && backArea.yMin > 0, "Back slab remains inside the live overlay view");
        var backRock=back.transform.Find("Slab");
        Check(backRock.GetComponent<MeshRenderer>().sharedMaterial.GetFloat("_ZWrite")==1,"The fading Back slab writes depth so rear faces cannot overpaint its top");
        CheckAtlas(backRock.GetComponent<MeshRenderer>().sharedMaterial,new Rect(.36f,.35f,.25f,.28f),new[]{backRock.GetComponent<MeshFilter>().sharedMesh},"SlabTex.png");
        var mainPrefab=AssetDatabase.LoadAssetAtPath<GameObject>(PreMissionStoneBoardSetup.DirectoryPath+"/MainQuestStone.prefab").GetComponent<QuestStoneView>();
        var mainFields=new SerializedObject(mainPrefab); var variants=mainFields.FindProperty("slabVariants");
        CheckAtlas(Get<MeshRenderer>(mainPrefab,"rock").sharedMaterial,new Rect(.82f,.12f,.12f,.65f),Enumerable.Range(0,variants.arraySize).Select(i=>(Mesh)variants.GetArrayElementAtIndex(i).objectReferenceValue),"BigSlabTex.png");
        Check(Board.Stones.All(s=>Get<MeshRenderer>(s,"rock").transform.localScale.x<1.65f&&Get<MeshRenderer>(s,"rock").transform.localScale.z<2.5f),"Side slabs have smaller portrait footprints");
        Check(Board.Stones.All(s=>BoundsOf(Heading(s)).size.x<1.4f),"Side category words are smaller and inset within the stone");
        CheckRadialLayouts();
        Capture("side-labels");
        for (int i = 1; i <= 3; i++)
        {
            var initial=Board.Stones.Where(s=>!s.IsMain).ToDictionary(s=>s.Offer.Slot,s=>new Vector2(s.transform.localPosition.x,s.transform.localPosition.z));
            button.onClick.Invoke(); button.onClick.Invoke();
            Check(Board.Progression.SuccessfulSmallCompletions == i, "A click advances one completion and duplicate same-frame input is ignored");
            Check(Board.Progression.ActiveAttempt == null, "Simulated result leaves no active mission attempt");
            if (i < 3) Check(!Board.Progression.BigQuestUnlocked, "Main quest waits for the third completion");
            if(i==3) yield return ObserveMainArrival(initial);
            yield return Ready();
            if (i < 3) Check(test.CanAdvance && button.interactable, "Temporary button becomes usable again after replacement arrivals");
        }
        Check(Board.Progression.BigQuestUnlocked && Board.Stones.Count == 7 && Board.Stones.Count(s => s.IsMain) == 1,
            "Third click triggers the normal main arrival and keeps six side quests");
        Check(!test.CanAdvance && !button.interactable && test.GetComponentInChildren<TMP_Text>().text.Contains("3/3"),
            "Temporary control reports 3/3 and stops advancing after unlock");
        Capture("main-labels"); Capture("main-labels-720p", 1280, 720); Capture("main-labels-4x3", 1024, 768);
        var main = Board.Stones.Single(s => s.IsMain);
        Check(BoundsOf(Heading(main)).size.x<2.6f,"Main category words are smaller and inset within the gold slab");
        foreach(var type in new[]{QuestMissionType.KingOfTheHill,QuestMissionType.Escort,QuestMissionType.Elimination})
        {
            var side=Board.Stones.First(s=>!s.IsMain&&s.Offer.Quest.missionType==type);
            var heading=Heading(side); var inscription=Get<TMP_Text>(side,"label");
            var initialLabel=inscription.text; var initialHeadingPosition=heading.transform.localPosition;
            Flow.SelectQuest(side.Offer.Quest); yield return Ready();
            Check(side.InscriptionOpacity==1&&!Board.Motion.Miniature.IsVisible&&heading.GetComponentsInChildren<Renderer>().All(r=>r.enabled),type+" inspection retains the whole heading/type/logo slab instead of a separate center logo");
            Check(inscription.text==initialLabel&&heading.transform.localPosition==initialHeadingPosition,type+" retains the same on-slab content layout when zoomed");
            Capture("inspection-"+type);
            Flow.Back(); yield return Ready();
        }
        Flow.SelectQuest(main.Offer.Quest); yield return Ready();
        Check(!test.CanAdvance, "Progress cannot advance from the briefing");
        Check(main.InscriptionOpacity==1&&!Board.Motion.Miniature.IsVisible,"Main inspection also retains its full on-slab layout");
        Capture("inspection-main");
        Flow.Back(); yield return Ready();
        Check(main.InscriptionOpacity == 1 && Heading(main).GetComponentsInChildren<Renderer>().All(r => r.enabled),
            "Decline restores the bright main lettering");
        Check(Board.Stones.All(s => Board.StoneAt(Board.BoardCamera.WorldToScreenPoint(s.HitCollider.bounds.center)) == s),
            "All seven slabs remain selectable after the simulated unlock and return");
    }
    static void Capture(string name, int width = 1600, int height = 900)
    {
        var camera = Board.BoardCamera; var oldTarget = camera.targetTexture; int mask = camera.cullingMask;
        var canvases = Object.FindObjectsByType<Canvas>().Where(c => c.isRootCanvas).ToArray();
        var modes = canvases.Select(c => c.renderMode).ToArray(); var cameras = canvases.Select(c => c.worldCamera).ToArray(); var distances = canvases.Select(c => c.planeDistance).ToArray();
        var target = new RenderTexture(width, height, 24); var previous = RenderTexture.active; var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target; camera.cullingMask |= (1 << 0) | (1 << 5);
            foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame();
            var back = Flow.GetComponentInChildren<PreMissionSlabButton>(true);
            typeof(PreMissionSlabButton).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(back, null);
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            var displayed=Flow.Session.View==PreMissionView.Board?Board.Stones:Board.Stones.Where(s=>s.Offer.Quest==Flow.Session.SelectedQuest);
            foreach (var stone in displayed)
            {
                var area = Project(BoundsOf(Heading(stone)), true); int bright = 0;
                for (int y = Mathf.Max(0, Mathf.FloorToInt(area.yMin)); y < Mathf.Min(height, Mathf.CeilToInt(area.yMax)); y++)
                    for (int x = Mathf.Max(0, Mathf.FloorToInt(area.xMin)); x < Mathf.Min(width, Mathf.CeilToInt(area.xMax)); x++)
                    { var pixel = image.GetPixel(x, y); if (pixel.r > .74f && pixel.g > .70f && pixel.b > .62f) bright++; }
                Debug.Log($"QUEST LABEL PIXELS: {name}; {stone.IsMain}; bright={bright}; area={area}");
                Check(bright >= (width < 1200 ? 45 : 70), name + " renders solid bright category lettering on every slab");
            }
            if(Flow.Session.View==PreMissionView.Board)
            {
                var backArea = Project(BoundsOf(GameObject.Find("Back Slab")), false);
                Check(backArea.xMin > 0 && backArea.xMax < 1 && backArea.yMin > 0, name + " keeps the physical Back button on screen");
            }
            File.WriteAllBytes(Path.Combine(capturePath, name + ".png"), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = oldTarget; camera.cullingMask = mask;
            for (int i = 0; i < canvases.Length; i++) { canvases[i].renderMode = modes[i]; canvases[i].worldCamera = cameras[i]; canvases[i].planeDistance = distances[i]; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Object.Destroy(image); Object.Destroy(target);
        }
    }
}
