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
using UnityEngine.UI;
using Object=UnityEngine.Object;

// Actual slab/preview attachment, staged emergence and overlapping torso handoff.
// Run only in a disposable project; no gameplay or authored mission fixtures.
public static class QuestSlabInspectionChecks
{
    const string Pending="QuestSlabInspectionChecks.Pending";
    static readonly Stack<IEnumerator> frames=new Stack<IEnumerator>();
    static bool started; static int checks; static double deadline;
    static PreMissionFlowController Flow=>Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board=>Flow.StoneBoard;
    static PreMissionStoneMotion Motion=>Board.Motion;
    static PreMissionMissionMiniature Mini=>Motion.Miniature;
    static bool Busy=>Flow.IsPosting||Flow.IsTransitioning;
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.update+=Step;
    public static void RunBatch()
    {
        if(!Application.isBatchMode) throw new Exception("Disposable project only");
        PreMissionMissionLogoSetup.UpgradeAssets(); PreMissionMissionLogoSetup.UpgradeAssets();
        GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline=null;
        EditorSceneManager.OpenScene("Assets/Scenes/PreMissionScene.unity");
        SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool ok,string message) { if(!ok) throw new Exception(message); checks++; Debug.Log("SLAB INSPECTION PASS: "+message); }
    static T Field<T>(Object owner,string name) where T:Object=>(T)new SerializedObject(owner).FindProperty(name).objectReferenceValue;
    static void Step()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        try
        {
            if(!started) { started=true; deadline=EditorApplication.timeSinceStartup+240; frames.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("Slab inspection checks timed out");
            while(frames.Count>0)
            {
                var current=frames.Peek(); if(!current.MoveNext()) { (current as IDisposable)?.Dispose(); frames.Pop(); continue; }
                if(current.Current is IEnumerator nested) { frames.Push(nested); continue; } return;
            }
            PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false);
            Debug.Log("SLAB INSPECTION CHECKS COMPLETE: "+checks); EditorApplication.Exit(0);
        }
        catch(Exception exception) { PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false); Debug.LogException(exception); EditorApplication.Exit(1); }
    }
    static IEnumerator Ready()
    {
        int frame=Time.frameCount+2; while(Time.frameCount<frame) yield return null;
        while(Busy||Board.Progression!=QuestProgressionRuntime.Current||Board.Stones.Count!=Board.Progression.Offers.Count) yield return null;
    }
    static bool Attached(QuestStoneView stone) => Quaternion.Angle(Mini.transform.rotation,stone.SurfaceRotation)<0.02f
        &&Vector3.Distance(Mini.transform.position,stone.SurfacePoint+stone.SurfaceRotation*Vector3.up*0.015f)<0.001f;
    static Transform ActiveVariant()
    {
        var variants=new SerializedObject(Mini).FindProperty("variants");
        for(int i=0;i<variants.arraySize;i++)
        {
            var root=(Transform)variants.GetArrayElementAtIndex(i).FindPropertyRelative("root").objectReferenceValue;
            if(root.gameObject.activeSelf) return root;
        }
        throw new Exception("No active miniature variant");
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Time.timeScale=0; Application.runInBackground=true; QualitySettings.vSyncCount=0; Application.targetFrameRate=60;
        foreach(var device in InputSystem.devices.Where(d=>d is Mouse||d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
        yield return Ready();
        var enabled=new HashSet<QuestMissionType> { QuestMissionType.KingOfTheHill, QuestMissionType.Escort, QuestMissionType.Elimination };
        Check(enabled.SetEquals(Board.Progression.Catalogue.offeredMissionTypes)&&Board.Stones.Count==6
            &&Board.Stones.All(s=>enabled.Contains(s.Offer.Quest.missionType)),
            "The shipped board offers six side quests using only KOTH, Escort and Elimination");
        for(int i=0;i<3;i++) { QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return Ready(); }
        var run=Board.Progression; var ids=string.Join(";",run.Offers.Select(o=>o.Id));
        Check(Board.Stones.Count==7&&run.BigQuest!=null&&enabled.Contains(run.BigQuest.Quest.missionType),
            "Three completions add a supported main mission while keeping all six side quests available");
        var positions=Board.Stones.ToDictionary(s=>s.Offer.Id,s=>s.transform.localPosition);
        var seen=new HashSet<QuestMissionType>();
        foreach(var stone in Board.Stones.ToArray())
        {
            var start=stone.CurrentOffset; Flow.SelectQuest(stone.Offer.Quest);
            bool attached=true; float arc=0;
            while(Busy)
            {
                if(Mini.IsVisible) attached&=Attached(stone);
                float p=Motion.InspectionProgress;
                if(p>0.1f&&p<0.9f)
                {
                    float final=stone.IsMain?0.12f:0.22f;
                    arc=Mathf.Max(arc,stone.CurrentOffset.y-Mathf.Lerp(start.y,final,p));
                }
                yield return null;
            }
            Check(attached&&Attached(stone),"Preview stays attached to the real tilted slab through approach: "+stone.Offer.Quest.title);
            Check(arc>(stone.IsMain?0.002f:0.008f),"Inspection uses a restrained lift arc with lighter movement for the main slab: "+stone.Offer.Quest.title);
            Check(Mathf.Abs(Mathf.DeltaAngle(Board.transform.eulerAngles.y,stone.Visual.eulerAngles.y))<0.1f,
                "Inspection turns the randomized slab toward the reader: "+stone.Offer.Quest.title);
            if(seen.Add(stone.Offer.Quest.missionType))
            {
                var display=Field<Transform>(Mini,"display"); var variant=ActiveVariant(); var full=display.localScale;
                Mini.SetProgress(0.02f);
                Check(display.gameObject.activeInHierarchy&&display.localScale.x/full.x<0.05f
                    &&(Mini.UsesAuthoredLogo || Enumerable.Range(0,variant.childCount).All(i=>!variant.GetChild(i).gameObject.activeSelf)),
                    "The preview grows from a tiny footprint while preserving its art formation: "+Mini.ActiveType);
                Mini.SetProgress(0.26f);
                int shown=Enumerable.Range(0,variant.childCount).Count(i=>variant.GetChild(i).gameObject.activeSelf);
                Check(display.localScale.x>0&&display.localScale.x<full.x&&shown>0&&(Mini.UsesAuthoredLogo ? shown==1 : shown<variant.childCount),
                    "Authored logos form as one object while prototype objectives retain their stagger: "+Mini.ActiveType);
                Mini.SetProgress(1);
                Check(display.localScale==full&&Enumerable.Range(0,variant.childCount).All(i=>variant.GetChild(i).gameObject.activeSelf),
                    "The completed preview restores the authored display scale: "+Mini.ActiveType);
                Mini.SetProgress(0.16f);
                Check(display.gameObject.activeInHierarchy&&display.localScale.x>0
                    &&(Mini.UsesAuthoredLogo || Enumerable.Range(0,variant.childCount).All(i=>!variant.GetChild(i).gameObject.activeSelf)),
                    "The preview retracts coherently before returning to the slab: "+Mini.ActiveType);
                Mini.SetProgress(1);
                int logo=Mini.ActiveType==QuestMissionType.KingOfTheHill ? 1 : Mini.ActiveType==QuestMissionType.Escort ? 2 : Mini.ActiveType==QuestMissionType.Elimination ? 3 : 0;
                Check(Mini.UsesAuthoredLogo==(logo>0),"Only mission types with supplied artwork use the new logos: "+Mini.ActiveType);
                if(logo>0)
                {
                    var meshes=variant.GetComponentsInChildren<MeshFilter>(true);
                    Check(variant.childCount==1&&meshes.Length>0&&meshes.All(m=>AssetDatabase.GetAssetPath(m.sharedMesh)==PreMissionMissionLogoSetup.SourceDirectory+"/Logo"+logo+".glb"),
                        "Mission type maps to its actual supplied model: Logo"+logo+" / "+Mini.ActiveType);
                    Check(variant.GetComponentsInChildren<MeshRenderer>(true).All(r=>r.gameObject.layer==26
                        &&AssetDatabase.GetAssetPath(r.sharedMaterial.GetTexture("_BaseMap"))==PreMissionMissionLogoSetup.SourceDirectory+"/Logo"+logo+"Tex.png")
                        &&!Field<GameObject>(Mini,"prototypePlinth").activeSelf,
                        "The matching texture is used on the board layer without the extra placeholder base: Logo"+logo);
                    QuestCinematicChecks.Capture("Logo"+logo+"-"+Mini.ActiveType);
                }
            }
            if(stone.IsMain)
            {
                var details=Field<GameObject>(Flow,"detailsView").GetComponent<CanvasGroup>();
                var torso=Field<GameObject>(Flow,"torsoView").GetComponent<CanvasGroup>();
                var accept=Field<Button>(Flow,"acceptButton").transform; float normal=accept.localScale.x;
                bool overlap=false; var quest=Flow.Session.SelectedQuest; Flow.AcceptQuest();
                while(Busy)
                {
                    overlap|=accept.localScale.x<normal-0.0001f&&details.alpha<0.999f&&torso.alpha>0.001f;
                    attached&=Attached(stone); yield return null;
                }
                Check(overlap,"Accept press overlaps the torso fade instead of adding a serial pause");
                Check(attached&&Flow.Session.SelectedQuest==quest&&Mini.Progress==1&&Motion.Inspected==stone,
                    "Torso entry preserves the inspected slab, miniature and selected mission");
                Flow.NextTorso(); yield return Ready(); var selectedTorso=Flow.Session.SelectedTorso;
                Flow.Back(); yield return Ready();
                Check(Flow.Session.SelectedQuest==quest&&Flow.Session.SelectedTorso==selectedTorso&&Attached(stone)&&Mini.Progress==1,
                    "Torso Back restores the same attached preview and both data selections");
            }
            Flow.Back(); attached=true;
            while(Busy) { if(Mini.IsVisible) attached&=Attached(stone); yield return null; }
            Check(attached&&!Mini.IsVisible&&stone.InscriptionOpacity==1&&stone.CurrentOffset==Vector3.zero&&stone.CurrentAngles.sqrMagnitude<0.0001f
                &&stone.CurrentScale==Vector3.one&&stone.transform.localPosition==positions[stone.Offer.Id],
                "Decline retracts the attached preview and restores the exact pushed slot: "+stone.Offer.Quest.title);
        }
        Check(seen.SetEquals(run.Catalogue.offeredMissionTypes)&&ids==string.Join(";",run.Offers.Select(o=>o.Id))&&run.SuccessfulSmallCompletions==3,
            "All enabled mission previews were exercised without changing offers or progression");
        Motion.Feedback.SetReducedMotion(true);
        var selected=Board.Stones.First(s=>!s.IsMain); Flow.SelectQuest(selected.Offer.Quest); yield return Ready();
        Check(selected.CurrentOffset==Vector3.zero&&selected.CurrentAngles.sqrMagnitude<0.0001f&&Attached(selected)
            &&Field<Transform>(Mini,"display").localScale==Vector3.one&&Mini.Progress==1,
            "Reduced motion shows the complete attached preview without slab arcs or decorative formation");
        Flow.Back(); yield return Ready();
        Check(Board.InputEnabled&&!Mini.IsVisible&&Board.Stones.All(s=>s.transform.localPosition==positions[s.Offer.Id]),
            "Reduced return preserves the whole-table arrangement and restores input");
        Check(run.ActiveAttempt==null&&SceneManager.GetActiveScene().name=="PreMissionScene"
            &&new[]{"smallImpact","heavyImpact","handlingClip","progressClip","selectionClip","confirmationClip"}.All(n=>Field<AudioClip>(Motion.Feedback,n)==null),
            "No gameplay scene, attempt or audio assignment is added by P3");
        Motion.Feedback.SetReducedMotion(false); Time.timeScale=1;
    }
}
