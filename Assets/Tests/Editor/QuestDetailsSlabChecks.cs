using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class QuestDetailsSlabChecks
{
    const string Pending = "QuestDetailsSlabChecks.Pending";
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static bool running; static double stableAt, deadline; static int lastFrame = -1, checks;
    static Mouse mouse; static Keyboard keyboard;
    static MouseState mouseState; static KeyboardState keyState; static bool mousePending, keysPending;
    static PreMissionFlowController Flow => Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board => Flow.StoneBoard;
    static PreMissionQuestDetailsSlab Slab => Object.FindAnyObjectByType<PreMissionQuestDetailsSlab>();
    static bool Busy => Flow.IsPosting || Flow.IsTransitioning;
    [InitializeOnLoadMethod] static void Register() { EditorApplication.update += Step; stableAt = EditorApplication.timeSinceStartup + 3; }
    public static void RunBatch()
    {
        var root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !root.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only");
        SessionState.SetBool(Pending, true); stableAt = EditorApplication.timeSinceStartup + 3;
    }
    static void Step()
    {
        try
        {
            if (SessionState.GetBool(Pending, false) && !EditorApplication.isPlaying)
            {
                if (EditorApplication.isCompiling) { stableAt = EditorApplication.timeSinceStartup + 3; return; }
                if (EditorApplication.timeSinceStartup < stableAt) return;
                SessionState.SetBool(Pending, false);
                PreMissionQuestDetailsSlabSetup.UpgradeAssets(); PreMissionQuestDetailsSlabSetup.UpgradeAssets();
                EditorSceneManager.OpenScene("Assets/Scenes/PreMissionScene.unity");
                PreMissionQuestDetailsSlabSetup.ConnectCurrentScene(); PreMissionQuestDetailsSlabSetup.ConnectCurrentScene();
                EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
                GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                QualitySettings.renderPipeline = null;
                running = true; deadline = EditorApplication.timeSinceStartup + 240; EditorApplication.EnterPlaymode();
            }
            if (!running || !EditorApplication.isPlaying || EditorApplication.isCompiling || Time.frameCount == lastFrame) return;
            lastFrame = Time.frameCount;
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("3D briefing checks timed out");
            if (steps.Count == 0) steps.Push(Run());
            while (steps.Count > 0)
            {
                var step = steps.Peek();
                if (!step.MoveNext()) { (step as IDisposable)?.Dispose(); steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; } return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        running = false; SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit();
        InputSystem.onBeforeUpdate -= FeedInput;
        if (error != null) Debug.LogException(error);
        Debug.Log("QUEST DETAILS CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("QUEST DETAILS PASS: " + message); }
    static IEnumerator Frames(int n = 2) { int target = Time.frameCount + n; while (Time.frameCount < target) yield return null; }
    static IEnumerator Delay(float seconds) { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }
    static IEnumerator Ready() { yield return Frames(); while (Busy) yield return null; yield return Frames(); }
    static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    static IEnumerator Pointer(Vector2 point, bool pressed = false)
    {
        mouseState = new MouseState { position = point }; if (pressed) mouseState = mouseState.WithButton(MouseButton.Left);
        mousePending = true; while (mousePending) yield return null; yield return Frames();
    }
    static void FeedInput()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        if (mousePending) { InputSystem.QueueStateEvent(mouse, mouseState); mousePending = false; }
        if (keysPending) { InputSystem.QueueStateEvent(keyboard, keyState); keysPending = false; }
    }
    static void Keys(params Key[] keys) { keyState = new KeyboardState(keys); keysPending = true; }
    static Vector2 ButtonPoint(PreMissionQuestSlabAction action) => Board.BoardCamera.WorldToScreenPoint(action.GetComponent<BoxCollider>().bounds.center);
    static bool Hits(PreMissionQuestSlabAction action)
    {
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = ButtonPoint(action) }, hits);
        return hits.Count > 0 && hits[0].gameObject == action.gameObject;
    }
    static IEnumerator Click(PreMissionQuestSlabAction action)
    { yield return Pointer(ButtonPoint(action)); yield return Pointer(ButtonPoint(action), true); yield return Pointer(ButtonPoint(action)); }
    static IEnumerator Capture(string name, int width = 1280, int height = 720)
    {
        var camera = Board.BoardCamera; var old = camera.targetTexture; float aspect = camera.aspect;
        var target = new RenderTexture(width, height, 24); camera.targetTexture = target; camera.aspect = width / (float)height; camera.ResetProjectionMatrix();
        Board.RefreshCameraFrame(); Slab.RefreshPlacement(); Physics.SyncTransforms(); yield return Frames();
        camera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "QuestDetailsCaptures"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), image.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = old; camera.aspect = aspect; camera.ResetProjectionMatrix(); Object.DestroyImmediate(image); target.Release(); Object.DestroyImmediate(target);
        Board.RefreshCameraFrame(); Slab.RefreshPlacement(); yield return Frames();
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.timeScale = 0;
        foreach (var device in InputSystem.devices.Where(d => d is Mouse || d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        mouse = InputSystem.AddDevice<Mouse>(); keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.onBeforeUpdate += FeedInput;
        // The scene uses shared project actions. Bind the disposable fixture's
        // virtual devices explicitly instead of retaining the editor's devices.
        var inputModule = (InputSystemUIInputModule)EventSystem.current.currentInputModule;
        inputModule.actionsAsset.devices = new InputDevice[] { mouse, keyboard };
        inputModule.actionsAsset.bindingMask = null;
        yield return Ready();
        Check(Object.FindObjectsByType<PreMissionQuestDetailsSlab>().Length == 1, "Repeated setup connects one scene-owned artist prefab");
        Check(!Slab.IsShown && Slab.AnimatedSlab.localPosition.y < -12, "PreFall parks the briefing beneath the table");
        Check(!Slab.AcceptAction.GetComponent<Collider>().enabled && !Slab.DeclineAction.GetComponent<Collider>().enabled, "Hidden physical buttons cannot intercept board input");
        var controller = (AnimatorController)Slab.Animator.runtimeAnimatorController;
        Check(controller.layers.Length == 3 && controller.layers.All(l => l.stateMachine.states.All(s => !s.state.writeDefaultValues)), "Three independent layers preserve slab and both button animation poses");
        var paper = Field<RectTransform>(Field<PreMissionPaperPresentation>(Flow, "presentation"), "briefingPaper");
        Check(!paper.gameObject.activeSelf, "Old paper background and its duplicate text/buttons are hidden");
        Check(Field<CanvasGroup>(Field<PreMissionPaperPresentation>(Flow, "presentation"), "details").GetComponentsInChildren<UnityEngine.UI.Graphic>(true).All(g => !g.enabled), "Legacy full-screen graphics neither cover the slab nor block physical button events");
        var queryPoint = (Vector2)Board.BoardCamera.WorldToScreenPoint(Board.Stones[0].HitCollider.bounds.center);
        for (int i = 0; i < 50; i++) Board.StoneAt(queryPoint);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++) Board.StoneAt(queryPoint);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        Check(allocated <= 256, "Physical action raycasting retains allocation-free warmed board queries (" + allocated + " bytes / 500 queries)");
        yield return Capture("board-prefall");
        foreach (var type in new[] { QuestMissionType.KingOfTheHill, QuestMissionType.Escort, QuestMissionType.Elimination })
        {
            var stone = Board.Stones.First(s => s.Offer.Quest.missionType == type); var quest = stone.Offer.Quest;
            Flow.SelectQuest(quest); bool sampled = false;
            while (Busy)
            {
                if (Slab.IsShown && Slab.Animator.GetCurrentAnimatorStateInfo(0).IsName("questDescSlabAnimFall"))
                { sampled = true; Check(!Slab.IsInteractive, "Falling briefing locks both actions"); break; }
                yield return null;
            }
            Check(sampled, "Selecting " + type + " starts the authored falling animation");
            yield return Ready();
            Check(Slab.IsShown && Slab.IsInteractive && Slab.AnimatedSlab.localPosition.sqrMagnitude < .001f, "Fall completes its full two-contact animation before Idle/input");
            Check(Slab.TitleText.text == quest.title && Slab.QuestTypeText.text.Contains(PreMissionFlowController.MissionTypeName(type))
                && Slab.DescriptionText.text.Contains(quest.description) && Slab.DescriptionText.text.Contains("Enemies spawned:") && !Slab.DescriptionText.text.Contains("Estimated duration"), "Selected mission data fills the three authored text fields");
            yield return Capture("details-" + type);
            Check(Hits(Slab.AcceptAction) && Hits(Slab.DeclineAction), "The board camera raycasts the real Accept and Decline stones");
            yield return Pointer(ButtonPoint(Slab.AcceptAction));
            Check(Slab.AcceptAction.IsHighlighted && !Slab.DeclineAction.IsHighlighted, "Mouse hover raises only the Accept stone");
            yield return Delay(.25f);
            Check(Slab.Animator.GetCurrentAnimatorStateInfo(1).IsName("questDescAcceptBtnHIdle") && Slab.AnimatedSlab.localPosition.sqrMagnitude < .001f, "Accept Highlight reaches HIdle without restarting the slab");
            yield return Pointer(new Vector2(5, 5));
            Check(!Slab.AcceptAction.IsHighlighted && Slab.Animator.GetCurrentAnimatorStateInfo(1).IsName("questDescAcceptBtnIdle"), "Mouse exit restores Accept Idle");
            yield return Pointer(ButtonPoint(Slab.DeclineAction)); yield return Delay(.25f);
            Check(Slab.DeclineAction.IsHighlighted && Slab.Animator.GetCurrentAnimatorStateInfo(2).IsName("questDescDeclineBtnHIdle"), "Decline has its own Highlight and HIdle");
            yield return Pointer(new Vector2(5, 5));
            Check(!Slab.DeclineAction.IsHighlighted && Slab.Animator.GetCurrentAnimatorStateInfo(2).IsName("questDescDeclineBtnIdle"), "Mouse exit restores Decline Idle");
            yield return Capture("details-" + type);
            yield return Click(Slab.DeclineAction); yield return Ready();
            Check(Flow.Session.View == PreMissionView.Board && !Slab.IsShown && Slab.AnimatedSlab.localPosition.y < -12, "Physical Decline returns to board and restores hidden PreFall");
            Check(Board.Stones.All(s => Board.StoneAt(Board.BoardCamera.WorldToScreenPoint(s.HitCollider.bounds.center)) == s), "Decline preserves every quest's hit area and saved slot");
        }
        for (int i = 0; i < 3; i++) { QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return Ready(); }
        var main = Board.Stones.First(s => s.IsMain); Flow.SelectQuest(main.Offer.Quest); yield return Ready();
        Check(Slab.IsInteractive && Slab.QuestTypeText.text.StartsWith("MAIN QUEST"), "Unlocked main quest uses the same physical briefing and its own data");
        yield return Capture("details-main"); var chosen = Flow.Session.SelectedQuest;
        yield return Capture("details-4x3", 1024, 768);
        yield return Click(Slab.AcceptAction); yield return Ready();
        Check(Flow.Session.View == PreMissionView.Torso && Flow.Session.SelectedQuest == chosen && !Slab.IsShown, "Physical Accept keeps the exact quest and opens starting torso");
        Flow.Back(); yield return Ready();
        Check(Flow.Session.View == PreMissionView.Details && Slab.IsInteractive && Slab.TitleText.text == chosen.title, "Torso Back restores the same briefing and actions");
        Keys(Key.LeftArrow); yield return Frames();
        Keys(); yield return Delay(.25f);
        Check(Slab.DeclineAction.IsHighlighted && EventSystem.current.currentSelectedGameObject == Slab.DeclineAction.gameObject, "Keyboard navigation focuses and highlights the physical Decline action");
        Keys(Key.Enter); yield return Frames();
        Keys(); yield return Ready();
        Check(Flow.Session.View == PreMissionView.Board && !Slab.IsShown, "Keyboard Submit invokes the physical action once");
        Flow.SelectQuest(Board.Stones[0].Offer.Quest); yield return Ready();
        Board.Motion.Feedback.SetReducedMotion(true); Flow.Back(); yield return Ready();
        Flow.SelectQuest(Board.Stones[1].Offer.Quest); yield return Ready();
        Check(Slab.IsInteractive && Slab.AnimatedSlab.localPosition.sqrMagnitude < .001f, "Reduced motion settles the 3D briefing without a falling sweep");
        Flow.enabled = false; yield return Frames(); Check(!Slab.IsInteractive, "Disabling the flow locks physical actions");
        Flow.enabled = true; yield return Ready(); Check(Slab.IsInteractive, "Re-enabling recovers the current briefing without regenerating offers");
        Slab.enabled = false; yield return Frames(); Check(!Slab.IsShown && !Slab.AcceptAction.GetComponent<BoxCollider>().enabled, "Disabling the briefing resets its pose, visibility and action hit areas");
        Slab.enabled = true; yield return Ready(); Check(Slab.IsInteractive, "Re-enabling the physical briefing restores the current view");
        Board.Motion.Feedback.SetReducedMotion(false); Flow.Back(); yield return Ready();
        Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "PreMissionScene" && Board.Progression.ActiveAttempt == null, "No gameplay launch or scene hookup is introduced");
        Time.timeScale = 1;
    }
}
