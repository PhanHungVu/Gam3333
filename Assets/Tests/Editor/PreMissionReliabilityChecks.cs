using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Final reliability checks use real Play Mode UI, temporary hooks and render targets.
public sealed class PreMissionReliabilityChecks
{
    const string ExpectedFailure = "Q5.4 expected hook failure";
    static readonly List<string> unexpectedErrors = new List<string>();
    static bool expectFailure, observedFailure;
    readonly PreMissionFlowController flow;
    readonly PreMissionPaperPresentation presentation;
    readonly Keyboard keyboard;
    readonly Action<bool, string> check;
    readonly Action<string, int, int> capture;
    readonly IEnumerator routine;
    QuestSelectionSession session;
    QuestNoticeView[] notices;
    QuestDefinition[] offers;
    QuestPostingStyle[] styles;
    MissionStartHook originalHook;
    PreMissionRetryTestHook retryHook;
    public static void BeginLogAudit()
    {
        unexpectedErrors.Clear(); expectFailure = observedFailure = false;
        Application.logMessageReceived -= OnLog; Application.logMessageReceived += OnLog;
    }
    public static void EndLogAudit() => Application.logMessageReceived -= OnLog;
    static void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        if (expectFailure && message.Contains(ExpectedFailure)) { expectFailure = false; observedFailure = true; return; }
        // Existing Editor Search startup issue, independent of the runtime UI.
        if (stack.Contains("UnityEditor.Search.SearchDatabase.EnumerateAll") || stack.Contains("UnityEditor.Search.SearchDatabase+<EnumerateAll>")) return;
        unexpectedErrors.Add(message);
    }
    public static void AssertNoUnexpectedErrors()
    {
        if (unexpectedErrors.Count > 0) throw new Exception("Unexpected Unity runtime error: " + unexpectedErrors[0]);
    }
    public PreMissionReliabilityChecks(PreMissionFlowController flow, Keyboard keyboard, Action<bool, string> check, Action<string, int, int> capture)
    {
        this.flow = flow; this.keyboard = keyboard; this.check = check; this.capture = capture;
        presentation = Flow<PreMissionPaperPresentation>("presentation");
        routine = Run();
    }
    static T Field<T>(Object target, string name) where T : Object => (T)new SerializedObject(target).FindProperty(name).objectReferenceValue;
    T Flow<T>(string name) where T : Object => Field<T>(flow, name);
    CanvasGroup Group(string name) => Field<CanvasGroup>(presentation, name);
    QuestNoticeView Travel => Field<QuestNoticeView>(presentation, "travellingPaper");
    StarterTorsoPreview Preview => Flow<StarterTorsoPreview>("torsoPreview");
    CanvasGroup[] BriefingGroups
    {
        get
        {
            var sequence = new SerializedObject(presentation).FindProperty("briefingGroups");
            return Enumerable.Range(0, sequence.arraySize).Select(i => (CanvasGroup)sequence.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
        }
    }
    void FreshBoard()
    {
        flow.CreateFreshBoard(); session = flow.Session; notices = flow.Notices.ToArray();
        offers = session.Offers.ToArray(); styles = notices.Select(n => n.PostingStyle).ToArray();
    }
    void StableBoard(string label)
    {
        check(flow.Session == session && session.Offers.SequenceEqual(offers) && flow.Notices.SequenceEqual(notices)
            && notices.Select(n => n.PostingStyle).SequenceEqual(styles)
            && notices.All(n => n.IsPosted && n.IsPaperVisible && !n.IsMoving && n.Opacity == 1f && n.RevealProgress == 1f
                && !n.GetComponentInChildren<RectMask2D>(true).enabled && n.Rect.localScale == Vector3.one
                && Vector2.Distance(n.Rect.anchoredPosition, n.BoardPosition) < 0.01f
                && Mathf.Abs(Mathf.DeltaAngle(n.Rect.localEulerAngles.z, n.BoardAngle)) < 0.01f), label);
    }
    void ReadyView(string label)
    {
        check(!flow.IsPosting && !flow.IsTransitioning && !Travel.gameObject.activeSelf
            && !Field<CanvasGroup>(presentation, "shade").gameObject.activeSelf
            && new[] { Group("board"), Group("details"), Group("torso") }.Count(g => g.gameObject.activeSelf) == 1
            && Group(session.View == PreMissionView.Board ? "board" : session.View == PreMissionView.Details ? "details" : "torso").interactable,
            label + ": one interactive view, no unfinished animation/shade/proxy");
        if (session.View == PreMissionView.Board) StableBoard(label + ": original offers, styles, transforms and clipping restored");
        if (session.View == PreMissionView.Details)
            check(BriefingGroups.All(g => g.alpha == 1f) && Flow<Button>("acceptButton").IsInteractable(), label + ": briefing and controls restored");
        if (session.View == PreMissionView.Torso)
            check(Preview.Torso == session.SelectedTorso && Flow<TMP_Text>("torsoName").text == session.SelectedTorso.partName
                && Field<CanvasGroup>(presentation, "torsoChoice").alpha == 1f
                && Field<CanvasGroup>(presentation, "torsoChoice").transform.localScale == Vector3.one,
                label + ": displayed torso matches the selected data");
    }
    void PressSpace()
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); InputSystem.Update(); keyboard.MakeCurrent();
        typeof(PreMissionFlowController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(flow, null);
        EventSystem.current.currentInputModule.Process();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
    }
    void BlockedActions()
    {
        flow.SelectQuest(offers[0]); flow.AcceptQuest(); flow.PreviousTorso(); flow.NextTorso(); flow.ConfirmTorso(); flow.Back(); flow.SkipPosting(); flow.CreateFreshBoard();
    }
    public bool Step() => !routine.MoveNext();
    IEnumerator Run()
    {
        Time.timeScale = 0f;
        FreshBoard();
        check(Flow<Button>("skipPostingButton") != null && Flow<TMP_Text>("boardHint") != null,
            "Shipped prefab retains its serialized Skip Posting button and normal board hint");
        while (Group("board").alpha < 0.2f) yield return null;
        check(flow.IsPosting && notices.All(n => !n.IsPosted), "Slow board entry preserves the unposted intro until its fade finishes");
        capture("q54-skip-entry", 1280, 720);
        var skip = Flow<Button>("skipPostingButton");
        check(skip.gameObject.activeSelf && skip.IsInteractable(), "Skip Posting is available during the board entry fade");
        ExecuteEvents.Execute(skip.gameObject, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
        int skippedFrame = Time.frameCount;
        flow.SelectQuest(offers[0]); flow.SkipPosting(); flow.SkipPosting();
        check(session.View == PreMissionView.Board && !flow.IsPosting && !Flow<Button>("skipPostingButton").gameObject.activeSelf
            && Flow<TMP_Text>("boardHint").gameObject.activeSelf, "Pointer skip finishes once, restores the hint and cannot also select a quest");
        StableBoard("Skipping the entry fade settles every notice without regenerating the board");
        while (Time.frameCount == skippedFrame) yield return null;

        FreshBoard();
        while (!notices.Any(n => n.IsMoving && n.PostingStyle == QuestPostingStyle.PaintedReveal && n.RevealProgress > 0.1f && n.RevealProgress < 0.8f)) yield return null;
        PressSpace(); skippedFrame = Time.frameCount;
        check(session.View == PreMissionView.Board && !flow.IsPosting && !flow.IsTransitioning,
            "Space through the real Input System completes a partially painted intro without a Submit cascade");
        StableBoard("Space skip removes partial masks and restores every exact board pose");
        while (Time.frameCount == skippedFrame) yield return null;

        FreshBoard(); skip = Flow<Button>("skipPostingButton"); EventSystem.current.SetSelectedGameObject(skip.gameObject);
        ExecuteEvents.Execute(skip.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        skippedFrame = Time.frameCount;
        StableBoard("Keyboard/UI Submit on Skip Posting completes the intro safely");
        while (Time.frameCount == skippedFrame) yield return null;

        presentation.enabled = false; BlockedActions();
        check(flow.Session == session && session.View == PreMissionView.Board && !Group("board").interactable,
            "Disabled presenter cancels work, locks controls and ignores public navigation calls");
        presentation.enabled = true; ReadyView("Presenter re-enabled on the board");
        flow.enabled = false; BlockedActions();
        check(flow.Session == session && session.View == PreMissionView.Board && !Group("board").interactable,
            "Disabled controller ignores button/API calls and does not create another board");
        flow.enabled = true; ReadyView("Controller re-enabled on the board");

        var selected = notices.First(n => n.Quest.category == QuestCategory.Main);
        flow.SelectQuest(selected.Quest);
        while (Travel.Rect.rect.height < selected.Rect.rect.height * 1.3f) yield return null;
        presentation.enabled = false; presentation.enabled = true;
        ReadyView("Presenter interrupted mid-pickup");
        flow.Back(); while (flow.IsTransitioning) yield return null;
        yield return null; flow.SelectQuest(selected.Quest);
        while (Travel.gameObject.activeSelf || !BriefingGroups.Any(g => g.alpha > 0f && g.alpha < 1f)) yield return null;
        flow.gameObject.SetActive(false); flow.gameObject.SetActive(true);
        ReadyView("UI interrupted during the slower information reveal");
        flow.AcceptQuest();
        while (Group("torso").alpha < 0.2f) yield return null;
        presentation.enabled = false; presentation.enabled = true;
        ReadyView("Presenter interrupted during forward navigation");
        var original = session.SelectedTorso;
        flow.NextTorso(); presentation.enabled = false; presentation.enabled = true;
        check(session.SelectedTorso == original, "Presenter cancellation before browse commit retains the outgoing torso");
        flow.NextTorso(); while (session.SelectedTorso == original) yield return null;
        presentation.enabled = false; presentation.enabled = true;
        ReadyView("Presenter interrupted after torso commit");
        check(session.SelectedTorso != original, "Incoming browse cancellation retains the new torso data");
        flow.Back(); while (Group("details").alpha < 0.2f) yield return null;
        flow.enabled = false; flow.enabled = true;
        ReadyView("Controller interrupted during backward navigation");
        check(Preview.Texture == null && !Preview.HasPreview, "Returning to details releases the hidden preview resources");
        for (int i = 0; i < 3; i++) { flow.gameObject.SetActive(false); flow.gameObject.SetActive(true); }
        ReadyView("Repeated UI close/open retains the same selections");

        // Instantiate inactive so the disabled-component cases precede Awake.
        foreach (bool disablePresenter in new[] { true, false })
        {
            var host = new GameObject("Q54 Initially Disabled UI Fixture"); host.SetActive(false);
            var clone = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PreMissionPrototypeSetup.PrefabPath), host.transform);
            var cloneFlow = clone.GetComponent<PreMissionFlowController>(); var clonePresentation = clone.GetComponent<PreMissionPaperPresentation>();
            if (disablePresenter) clonePresentation.enabled = false; else cloneFlow.enabled = false;
            host.SetActive(true);
            check(cloneFlow.Session == null && !Field<CanvasGroup>(clonePresentation, "board").interactable,
                "Initially disabled " + (disablePresenter ? "presenter" : "controller") + " defers board creation and locks controls");
            if (disablePresenter) clonePresentation.enabled = true; else cloneFlow.enabled = true;
            check(cloneFlow.Session != null && cloneFlow.Session.Progression == QuestProgressionRuntime.Current && cloneFlow.Notices.Count > 0,
                "Enabling the initially disabled component restores the persistent board");
            Object.Destroy(host); yield return null;
            while (host != null) yield return null;
            check(Object.FindObjectsByType<PreMissionFlowController>().Length == 1, "Destroyed startup fixture leaves no extra UI controller");
            flow.RecoverPresentation();
        }

        flow.AcceptQuest(); while (flow.IsTransitioning) yield return null;
        originalHook = Flow<MissionStartHook>("missionStartHook"); retryHook = flow.gameObject.AddComponent<PreMissionRetryTestHook>();
        var fields = new SerializedObject(flow); fields.FindProperty("missionStartHook").objectReferenceValue = retryHook; fields.ApplyModifiedPropertiesWithoutUndo();
        expectFailure = true; flow.ConfirmTorso(); while (flow.IsTransitioning) yield return null;
        check(observedFailure && retryHook.Calls == 1 && !session.LaunchRequested && Flow<Button>("confirmTorsoButton").interactable
            && Flow<TMP_Text>("launchStatus").text.Contains("failed"), "A hook failure reports the error, clears its reservation and permits retry");
        var expectedQuest = session.SelectedQuest; var expectedTorso = session.SelectedTorso;
        flow.ConfirmTorso(); flow.ConfirmTorso(); flow.NextTorso(); flow.Back();
        while (flow.IsTransitioning) yield return null;
        check(retryHook.Calls == 2 && retryHook.Quest == expectedQuest && retryHook.Torso == expectedTorso && session.LaunchRequested
            && SceneManager.GetActiveScene().path == PreMissionPrototypeSetup.ScenePath,
            "Retry submits the same exact quest/torso once and never loads gameplay");
        flow.NextTorso(); flow.PreviousTorso();
        check(session.SelectedTorso == expectedTorso, "Submitted selections remain locked against rapid browsing");
        var restore = new SerializedObject(flow); restore.FindProperty("missionStartHook").objectReferenceValue = originalHook; restore.ApplyModifiedPropertiesWithoutUndo();
        Object.Destroy(retryHook);
        flow.Back(); while (flow.IsTransitioning) yield return null;
        flow.Back(); while (flow.IsTransitioning) yield return null;
        yield return null;

        // Exercise the same session through every screen and mid-motion Canvas
        // refits. Capture also checks all settled controls against Canvas bounds.
        foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(1024, 768) })
        {
            string tag = size.x + "x" + size.y;
            capture("q54-board-" + tag, size.x, size.y);
            var side = notices.First(n => n.Quest.category == QuestCategory.Side);
            float sourceFont = side.TextSize;
            flow.SelectQuest(side.Quest);
            while (!Travel.gameObject.activeSelf || Travel.Opacity < 0.2f || Travel.TextSize <= sourceFont * 1.1f) yield return null;
            capture("q54-pickup-refit-" + tag, size.x, size.y);
            while (flow.IsTransitioning) yield return null;
            capture("q54-details-" + tag, size.x, size.y);
            flow.AcceptQuest();
            while (Group("torso").alpha < 0.2f) yield return null;
            capture("q54-forward-refit-" + tag, size.x, size.y);
            while (flow.IsTransitioning) yield return null;
            capture("q54-torso-" + tag, size.x, size.y);
            check(Preview.Torso == session.SelectedTorso && StarterTorsoChecks.HasRenderedModel(Preview), "Real torso pixels/data remain valid at " + tag);
            var before = session.SelectedTorso;
            flow.PreviousTorso(); flow.PreviousTorso(); flow.NextTorso(); flow.ConfirmTorso();
            while (Field<CanvasGroup>(presentation, "torsoChoice").alpha > 0.7f) yield return null;
            capture("q54-browse-refit-" + tag, size.x, size.y);
            while (flow.IsTransitioning) yield return null;
            check(session.SelectedTorso != before && Preview.Torso == session.SelectedTorso && !session.LaunchRequested,
                "Rapid browse accepts one change and keeps actual selection/display aligned at " + tag);
            flow.NextTorso(); while (flow.IsTransitioning) yield return null;
            flow.Back(); while (flow.IsTransitioning) yield return null;
            flow.Back(); while (flow.IsTransitioning) yield return null;
            yield return null;
            ReadyView("Completed repeated navigation at " + tag);
        }
        Time.timeScale = 1f;
        AssertNoUnexpectedErrors();
        check(!expectFailure, "The only injected exception was observed; no unexpected runtime errors occurred");
    }
}

public sealed class PreMissionRetryTestHook : MissionStartHook
{
    public int Calls;
    public QuestDefinition Quest;
    public PartData Torso;
    public override void StartMission(QuestDefinition selectedQuest, PartData startingTorso)
    {
        Calls++;
        if (Calls == 1) throw new InvalidOperationException("Q5.4 expected hook failure");
        Quest = selectedQuest; Torso = startingTorso;
    }
}
