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
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Saved art/prefab integration: independent data, picking, overlap, return and Back.
public static class QuestPortraitSlabChecks
{
    static bool pending, started;
    static double readyAt, deadline;
    static int lastFrame = -1, checks;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static string captures;
    static PreMissionFlowController Flow => Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board => Flow.StoneBoard;
    static T Field<T>(object owner, string name) where T : Object => (T)new SerializedObject((Object)owner).FindProperty(name).objectReferenceValue;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Step;
    public static void RunBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        pending = true; readyAt = EditorApplication.timeSinceStartup + 3;
    }
    static void Step()
    {
        if (!Application.isBatchMode) return;
        if (pending && !EditorApplication.isPlaying)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { readyAt = EditorApplication.timeSinceStartup + 3; return; }
            if (EditorApplication.timeSinceStartup < readyAt) return;
            try
            {
                pending = false; PreMissionPortraitSlabSetup.UpgradeAssets(); PreMissionPortraitSlabSetup.UpgradeAssets();
                GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                QualitySettings.renderPipeline = null; EditorSceneManager.OpenScene("Assets/Scenes/PreMissionScene.unity");
                started = true; deadline = EditorApplication.timeSinceStartup + 300; EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Finish(error); }
            return;
        }
        if (!started || !EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame == Time.frameCount) return;
        if (EditorApplication.isPaused) EditorApplication.isPaused = false;
        lastFrame = Time.frameCount;
        try
        {
            if (steps.Count == 0) steps.Push(Run());
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Portrait UI checks timed out");
            while (steps.Count > 0)
            {
                var step = steps.Peek();
                if (!step.MoveNext()) { (step as IDisposable)?.Dispose(); steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; }
                return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        started = false; Time.captureFramerate = 0; PreMissionReliabilityChecks.EndLogAudit();
        if (error != null) Debug.LogException(error);
        Debug.Log("PORTRAIT QUEST CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static void Check(bool valid, string message)
    { if (!valid) throw new Exception(message); checks++; Debug.Log("PORTRAIT QUEST PASS: " + message); }
    static IEnumerator Frames(int count = 2)
    { int until = Time.frameCount + count; while (Time.frameCount < until) yield return null; }
    static IEnumerator Ready(bool skip = false)
    {
        yield return Frames();
        while (Flow.IsPosting || Flow.IsTransitioning || Board.Progression != QuestProgressionRuntime.Current || Board.Stones.Count != Board.Progression.Offers.Count)
        { if (skip && Flow.IsPosting) Flow.SkipPosting(); yield return null; }
        yield return Frames();
    }
    static Rect Project(Bounds bounds)
    {
        Vector2 min = Vector2.positiveInfinity, max = Vector2.negativeInfinity;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
        {
            Vector2 point = Board.BoardCamera.WorldToViewportPoint(bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z)));
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }
    static void Layout(string context)
    {
        Board.RefreshCameraFrame(); Physics.SyncTransforms();
        var areas = Board.Stones.Select(s => Project(s.HitCollider.bounds)).ToArray();
        Check(areas.All(r => r.xMin > 0 && r.xMax < 1 && r.yMin > .1f && r.yMax < .95f), context + " stays in the visible table area");
        Check(!areas.Where((r, i) => areas.Skip(i + 1).Any(r.Overlaps)).Any(), context + " keeps projected selection areas separate");
        Check(Board.Stones.All(s => Board.StoneAt(Board.BoardCamera.WorldToScreenPoint(s.HitCollider.bounds.center)) == s), context + " raycasts reach every slab");
        Check(Board.Stones.All(s => s.RockBounds.size.z > s.RockBounds.size.x), context + " uses portrait slabs for side and main quests");
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.captureFramerate = 60;
        captures = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "QuestPortraitCaptures"); Directory.CreateDirectory(captures);
        foreach (var input in UnityEngine.InputSystem.InputSystem.devices.Where(d => d is UnityEngine.InputSystem.Mouse || d is UnityEngine.InputSystem.Keyboard).ToArray())
            UnityEngine.InputSystem.InputSystem.DisableDevice(input);
        yield return Ready();
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PreMissionPortraitSlabSetup.FontPath);
        Check(font != null && font.sourceFontFile.name == "MedievalSharp-Book" && font.atlasTextures[0].width > 0, "MedievalSharp has a persistent populated TMP atlas and source font");
        Check(Flow.GetComponentsInChildren<TMP_Text>(true).All(t => t.font == font), "Every quest UI label uses the new font");
        var boardView = Field<GameObject>(Flow, "boardView");
        Check(!boardView.transform.Find("Board Heading").gameObject.activeSelf && !Field<TMP_Text>(Flow, "boardMessage").gameObject.activeSelf, "Board heading and textual progress are hidden");
        var exit = Field<Button>(Flow, "exitButton");
        Check(exit.GetComponent<PreMissionSlabButton>() != null && GameObject.Find("Back Slab") != null, "The existing Back button displays its physical slab");
        foreach (var stone in Board.Stones)
        {
            var fields = new SerializedObject(stone); var label = Field<TMP_Text>(stone, "label");
            Check(label.text == PreMissionFlowController.MissionTypeShortName(stone.Offer.Quest.missionType) && !label.text.Contains(stone.Offer.Quest.title), "Slab shows only its data-driven mission type below the category model");
            var heading = Field<GameObject>(stone, "categoryInscription");
            Check(heading != null && heading.GetComponentsInChildren<MeshFilter>().All(f => AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith("sideQuestText.glb")), "Side category uses the supplied 3D text mesh");
            var logos = fields.FindProperty("missionLogos"); int active = 0;
            for (int i = 0; i < logos.arraySize; i++)
            {
                var entry = logos.GetArrayElementAtIndex(i); var root = (GameObject)entry.FindPropertyRelative("root").objectReferenceValue;
                if (!root.activeSelf) continue;
                active++; Check(entry.FindPropertyRelative("missionType").enumValueIndex == (int)stone.Offer.Quest.missionType, "Active logo matches the quest's mission type");
            }
            Check(active == 1, "Every supported quest has exactly one board logo");
        }
        Layout("Initial six offers"); Capture("side-board");
        foreach (int seed in new[] { 13, 381, 996, 4017, 8721, 10993 })
        {
            QuestProgressionRuntime.StartNewProgression(Field<QuestBoardCatalogue>(Flow, "catalogue"), seed); Flow.CreateFreshBoard(); yield return Ready(true);
            Layout("Seed " + seed);
            for (int n = 0; n < 3; n++)
            { Check(QuestStoneBoardChecks.CompleteSmallForTest(Flow), "A side quest completion advances the existing progression"); yield return Ready(true); }
            Layout("Seed " + seed + " with main unlocked");
            Check(Board.Stones.Count == 7 && Board.Stones.Any(s => s.IsMain), "Three completions unlock main while keeping six side quests selectable");
        }
        var main = Board.Stones.Single(s => s.IsMain);
        Check(Field<GameObject>(main, "categoryInscription").GetComponentsInChildren<MeshFilter>().All(f => AssetDatabase.GetAssetPath(f.sharedMesh).EndsWith("mainQuestText.glb")), "Main category uses the supplied MAIN QUEST mesh");
        Capture("main-board"); Capture("main-board-4x3", 1024, 768);
        var target = Board.Stones.First(s => !s.IsMain && s.Offer.Quest.missionType == QuestMissionType.KingOfTheHill);
        Flow.SelectQuest(target.Offer.Quest); yield return Ready();
        string info = Field<TMP_Text>(Flow, "detailsInfo").text;
        Check(!info.Contains("Estimated") && !info.Contains("minutes") && info.Contains("Enemies spawned: Not specified"), "Briefing removes duration and reads the quest enemy list honestly");
        Check(GameObject.Find("Back Slab") == null, "Board Back slab is hidden during briefing");
        var quest = target.Offer.Quest; var originalEnemies = quest.availableEnemies;
        var configuredEnemy = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[0]);
        quest.availableEnemies = new List<GameObject> { configuredEnemy, null };
        typeof(PreMissionFlowController).GetMethod("RefreshView", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null).Invoke(Flow, null);
        Check(Field<TMP_Text>(Flow, "detailsInfo").text.Contains("Enemies spawned: " + configuredEnemy.name), "A configured enemy appears by name without treating null entries as spawns");
        Capture("briefing");
        quest.availableEnemies = originalEnemies;
        Flow.Back(); yield return Ready();
        Check(target.IsReady && !target.IsHighlighted && target.InscriptionOpacity == 1, "Declining restores the full inscription without a stuck highlight");
        Flow.SelectQuest(main.Offer.Quest); yield return Ready(); Capture("main-briefing");
        Flow.AcceptQuest(); yield return Ready();
        Check(Flow.Session.View == PreMissionView.Torso && Field<TMP_Text>(Flow, "torsoName").font == font, "Accept still reaches torso selection with the new font");
        Flow.Back(); yield return Ready(); Flow.Back(); yield return Ready();
        int exits = 0; var callback = new UnityEvent(); callback.AddListener(() => exits++);
        typeof(PreMissionFlowController).GetField("exitToMenu", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Flow, callback);
        ExecuteEvents.Execute(exit.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        Check(exits == 1, "Clicking the physical Back control invokes the existing menu callback once");
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
            var back = Field<Button>(Flow, "exitButton").GetComponent<PreMissionSlabButton>();
            typeof(PreMissionSlabButton).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(back, null);
            foreach (var preview in Object.FindObjectsByType<StarterTorsoPreview>()) preview.RenderNow();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            var pixels = image.GetPixels32(); Check(pixels.Count(p => p.r > 220 && p.b > 220 && p.g < 70) < pixels.Length / 200, name + " has no missing-shader pink output");
            File.WriteAllBytes(Path.Combine(captures, name + ".png"), image.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous; camera.targetTexture = oldTarget; camera.cullingMask = mask;
            for (int i = 0; i < canvases.Length; i++) { canvases[i].renderMode = modes[i]; canvases[i].worldCamera = cameras[i]; canvases[i].planeDistance = distances[i]; }
            Canvas.ForceUpdateCanvases(); Board.RefreshCameraFrame(); Object.Destroy(image); Object.Destroy(target);
        }
    }
}
