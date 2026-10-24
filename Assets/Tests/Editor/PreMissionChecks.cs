using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Run only in a disposable project; no working scene is changed by these checks.
public static class PreMissionChecks
{
    const string Pending = "PreMissionChecks.Pending";
    static int phase, checks;
    static double next;
    static PreMissionFlowController flow;
    static QuestSelectionSession session;
    static QuestDefinition[] offers;
    static QuestNoticeView[] notices;
    static QuestDefinition selected;
    static PartData selectedTorso;
    static Keyboard keyboard;
    static Mouse mouse;
    static string captureDirectory;
    static double deadline;
    static bool sawPosting, capturedPull;
    static Dictionary<QuestDefinition, Vector3> homes;
    static bool wasBusy;
    static int hoverStage;
    static double hoverAt;
    static float selectedTextBeforePull;
    static QuestNoticeView trackedFirst;
    static bool firstHitchInjected, firstCapture;
    static readonly HashSet<int> firstMotionFrames = new HashSet<int>();
    static StarterTorsoChecks torsoChecks;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Update;

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        CheckData();
        PreMissionPrototypeSetup.CreatePrototype();
        PreMissionPresentationSetup.UpgradeAssets();
        StarterTorsoSetup.UpgradeAssets();
        EditorSceneManager.OpenScene(PreMissionPrototypeSetup.ScenePath);
        var serialized = new SerializedObject(Object.FindAnyObjectByType<PreMissionFlowController>());
        Check(serialized.FindProperty("catalogue").objectReferenceValue != null && serialized.FindProperty("noticePrefab").objectReferenceValue != null,
            "Saved scene retains its external catalogue and notice prefab references");
        SessionState.SetInt(Pending + ".Count", checks);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    public static void RunSixOffersBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        PreMissionPrototypeSetup.CreatePrototype();
        var catalogue = AssetDatabase.LoadAssetAtPath<QuestBoardCatalogue>(PreMissionPrototypeSetup.CataloguePath);
        catalogue.minimumOffers = catalogue.maximumOffers = 6;
        EditorUtility.SetDirty(catalogue); AssetDatabase.SaveAssets();
        RunBatch();
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        checks++;
        Debug.Log("PRE-MISSION PASS: " + message);
    }

    static void CheckData()
    {
        var catalogue = ScriptableObject.CreateInstance<QuestBoardCatalogue>();
        var quests = Enumerable.Range(0, 8).Select(i =>
        {
            var q = ScriptableObject.CreateInstance<QuestDefinition>(); q.questId = "check-" + i;
            q.category = i == 0 ? QuestCategory.Main : QuestCategory.Side; return q;
        }).ToArray();
        catalogue.quests.AddRange(quests);
        catalogue.quests.Add(quests[1]); catalogue.quests.Add(null);
        var duplicate = ScriptableObject.CreateInstance<QuestDefinition>(); duplicate.questId = quests[2].questId;
        catalogue.quests.Add(duplicate);
        var unavailable = ScriptableObject.CreateInstance<QuestDefinition>(); unavailable.questId = "unavailable"; unavailable.available = false;
        catalogue.quests.Add(unavailable);
        var blank = ScriptableObject.CreateInstance<QuestDefinition>(); catalogue.quests.Add(blank);
        var torsoA = ScriptableObject.CreateInstance<PartData>(); torsoA.partId = "check-torso-a"; torsoA.partType = PartType.Torso;
        var torsoB = ScriptableObject.CreateInstance<PartData>(); torsoB.partId = "check-torso-b"; torsoB.partType = PartType.Torso;
        var arm = ScriptableObject.CreateInstance<PartData>(); arm.partId = "arm"; arm.partType = PartType.Arm;
        catalogue.starterTorsos.AddRange(new[] { torsoA, torsoB, torsoA, arm, null });
        for (int seed = 0; seed < 30; seed++)
        {
            var model = new QuestSelectionSession(catalogue, seed);
            Check(model.Offers.Count >= 4 && model.Offers.Count <= 6 && model.Offers.Select(q => q.questId).Distinct().Count() == model.Offers.Count
                && model.Offers.Contains(quests[0]) && !model.Offers.Contains(unavailable) && !model.Offers.Contains(blank), "Eligible unique offers and main inclusion, seed " + seed);
        }
        var state = new QuestSelectionSession(catalogue, 12);
        var original = state.Offers.ToArray();
        Check(state.Torsos.Count == 2 && !state.SelectQuest(unavailable) && !state.AcceptQuest() && !state.BrowseTorso(1), "Invalid navigation and invalid torso entries are rejected");
        foreach (var quest in original)
        {
            Check(state.SelectQuest(quest) && state.SelectedQuest == quest && state.View == PreMissionView.Details,
                "Every main/side offer is freely selectable: " + quest.questId);
            Check(!state.SelectQuest(quest) && state.AcceptQuest() && !state.AcceptQuest(), "Repeated selection/accept cannot create duplicate navigation");
            Check(state.BrowseTorso(-1) && state.SelectedTorso == torsoB && state.BrowseTorso(1) && state.SelectedTorso == torsoA, "Torso browsing wraps both ways");
            Check(state.TryRequestLaunch(out var q, out var t) && q == quest && t == torsoA
                && !state.TryRequestLaunch(out _, out _) && !state.BrowseTorso(1), "Launch returns exact selected data once");
            Check(state.Back() && state.SelectedQuest == quest && state.SelectedTorso == torsoA && !state.LaunchRequested
                && state.Back() && state.SelectedQuest == null && state.Offers.SequenceEqual(original), "Both back routes preserve the board and torso selection");
        }
        catalogue.quests.Clear(); catalogue.starterTorsos.Clear();
        var empty = new QuestSelectionSession(catalogue, 0);
        Check(empty.Offers.Count == 0 && empty.SelectedTorso == null && !empty.TryRequestLaunch(out _, out _), "Empty catalogues cannot launch");
        catalogue.quests.Add(quests[1]); catalogue.minimumOffers = 6; catalogue.maximumOffers = 2;
        var small = new QuestSelectionSession(catalogue, 0);
        Check(small.Offers.Count == 1 && small.SelectQuest(quests[1]) && small.AcceptQuest() && !small.TryRequestLaunch(out _, out _), "Small catalogues and inverted counts are safe; missing torso blocks confirmation");
        foreach (var asset in quests.Cast<Object>().Concat(new Object[] { duplicate, unavailable, blank, torsoA, torsoB, arm, catalogue })) Object.DestroyImmediate(asset);
    }

    static T Field<T>(string name) where T : Object => (T)new SerializedObject(flow).FindProperty(name).objectReferenceValue;
    static void CheckView(PreMissionView view)
    {
        Check(flow.Session.View == view && Field<GameObject>("boardView").activeSelf == (view == PreMissionView.Board)
            && Field<GameObject>("detailsView").activeSelf == (view == PreMissionView.Details)
            && Field<GameObject>("torsoView").activeSelf == (view == PreMissionView.Torso), "Exactly one active UI view: " + view);
    }
    static void Capture(string name, int width = 1600, int height = 900)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        var camera = Camera.main;
        var canvases = Object.FindObjectsByType<Canvas>().Where(c => c.isRootCanvas).ToArray();
        var modes = canvases.Select(c => c.renderMode).ToArray();
        var cameras = canvases.Select(c => c.worldCamera).ToArray();
        var distances = canvases.Select(c => c.planeDistance).ToArray();
        var target = new RenderTexture(width, height, 24);
        var oldTarget = camera.targetTexture;
        var oldActive = RenderTexture.active;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1f; }
            Canvas.ForceUpdateCanvases();
            foreach (var preview in Object.FindObjectsByType<StarterTorsoPreview>()) preview.RenderNow();
            if (GraphicsSettings.currentRenderPipeline != null)
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            else camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(captureDirectory, name + ".png"), image.EncodeToPNG());
            if (flow != null && flow.Session.View == PreMissionView.Board && !flow.IsTransitioning && !flow.IsPosting)
            {
                var parent = (RectTransform)Field<Transform>("noticeParent");
                Check(flow.Notices.All(n =>
                {
                    var bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parent, n.transform);
                    return bounds.min.x >= parent.rect.xMin - 1 && bounds.max.x <= parent.rect.xMax + 1
                        && bounds.min.y >= parent.rect.yMin - 1 && bounds.max.y <= parent.rect.yMax + 1;
                }), "Notice hit areas stay within the board at " + width + "x" + height);
                var bounds = flow.Notices.Select(n => RectTransformUtility.CalculateRelativeRectTransformBounds(parent, n.transform)).ToArray();
                Check(!bounds.Where((a, i) => bounds.Skip(i + 1).Any(b =>
                    a.min.x < b.max.x && a.max.x > b.min.x && a.min.y < b.max.y && a.max.y > b.min.y)).Any(),
                    "Rotated paper bounds do not overlap at " + width + "x" + height);
            }
        }
        finally
        {
            RenderTexture.active = oldActive; camera.targetTexture = oldTarget;
            for (int i = 0; i < canvases.Length; i++) { canvases[i].renderMode = modes[i]; canvases[i].worldCamera = cameras[i]; canvases[i].planeDistance = distances[i]; }
            target.Release(); Object.Destroy(target); Object.Destroy(image);
            Canvas.ForceUpdateCanvases();
        }
    }
    static void Escape()
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Escape)); InputSystem.Update();
        keyboard.MakeCurrent();
        Check(keyboard.escapeKey.wasPressedThisFrame, "Escape reaches the real Input System");
        typeof(PreMissionFlowController).GetMethod("Update", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(flow, null);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
    }

    static bool Waiting()
    {
        if (flow.IsTransitioning) { wasBusy = true; return true; }
        // Let Selectables consume the restored CanvasGroup state before pointer checks/captures.
        if (wasBusy) { wasBusy = false; return true; }
        return false;
    }
    static void MoveMouse(Vector2 position)
    {
        var delta = position - mouse.position.ReadValue();
        InputSystem.QueueDeltaStateEvent(mouse.position, position);
        InputSystem.QueueDeltaStateEvent(mouse.delta, delta);
        InputSystem.Update(); mouse.MakeCurrent();
    }
    static bool HoverCycle(QuestNoticeView paper, string context)
    {
        if (hoverStage == 0)
        {
            MoveMouse(new Vector2(-100, -100));
            EventSystem.current.SetSelectedGameObject(null);
            EventSystem.current.SetSelectedGameObject(paper.gameObject);
            hoverStage = 1; hoverAt = EditorApplication.timeSinceStartup + 0.2; return false;
        }
        if (EditorApplication.timeSinceStartup < hoverAt) return false;
        var visual = paper.transform.Find("Paper Visual");
        if (hoverStage == 1)
        {
            Check(!paper.IsHighlighted && visual.localScale.x < 1.005f, context + ": automatic keyboard focus does not leave the paper permanently raised");
            MoveMouse(RectTransformUtility.WorldToScreenPoint(null, paper.Rect.position));
            hoverStage = 2; hoverAt = EditorApplication.timeSinceStartup + 0.25; return false;
        }
        if (hoverStage == 2)
        {
            Check(paper.IsHighlighted && visual.localScale.x > 1.007f && visual.localPosition.y > 0.5f,
                context + ": real UI mouse enter visibly lifts the paper");
            Capture("fix-hover-" + context);
            MoveMouse(new Vector2(-100, -100));
            hoverStage = 3; hoverAt = EditorApplication.timeSinceStartup + 0.25; return false;
        }
        Check(!paper.IsHighlighted && visual.localScale.x < 1.01f, context + ": real mouse exit lowers the paper again");
        hoverStage = 0; return true;
    }
    static void CheckComposedLayouts()
    {
        var layout = Field<Transform>("noticeParent").GetComponent<PreMissionNoticeLayout>();
        int original = layout.SelectedDesignIndex;
        var seeds = new Dictionary<int, int>();
        var signatures = new HashSet<string>();
        for (int seed = 0; seed < 30; seed++)
        {
            layout.Configure(flow.Notices, seed); seeds[layout.SelectedDesignIndex] = seed;
            signatures.Add(string.Join(";", flow.Notices.Select(n => n.BoardPosition + "/" + n.BoardAngle)));
        }
        Check(layout.DesignCount == 3 && seeds.Count == 3 && signatures.Count == 3,
            "Randomization chooses only the three authored compositions, without independent paper jitter");
        foreach (var entry in seeds)
        {
            layout.Configure(flow.Notices, entry.Value);
            Capture("fix-layout-" + layout.SelectedDesignName);
        }
        layout.Configure(flow.Notices, seeds[original]);
    }
    static void ClickControl(Button button)
    {
        var pointer = new PointerEventData(EventSystem.current)
        {
            position = RectTransformUtility.WorldToScreenPoint(null, button.transform.position),
            button = PointerEventData.InputButton.Left
        };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
        var hit = hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null;
        Check(button.IsInteractable() && hit == button.gameObject, "Briefing control is enabled and receives a pointer click: " + button.name);
        ExecuteEvents.Execute(hit, pointer, ExecuteEvents.pointerClickHandler);
    }
    static GameObject PaperHit(QuestNoticeView notice)
    {
        var data = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, notice.Rect.position) };
        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits);
        return hits.Count > 0 ? ExecuteEvents.GetEventHandler<IPointerClickHandler>(hits[0].gameObject) : null;
    }
    static void ClickPaper(QuestNoticeView notice)
    {
        var hit = PaperHit(notice);
        Check(hit == notice.gameObject, "Pointer raycast reaches the selected paper without an overlapping notice or overlay");
        ExecuteEvents.Execute(hit, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left }, ExecuteEvents.pointerClickHandler);
    }
    static void ObservePosting()
    {
        if (flow == null || !flow.IsPosting) return;
        sawPosting = true;
        if (flow.Notices.Count(n => n.IsMoving) > 1) throw new Exception("Posting moves more than one notice at once");
        if (flow.Notices.Any(n => n.Quest.category == QuestCategory.Main && (n.IsPosted || n.IsMoving))
            && flow.Notices.Any(n => n.Quest.category == QuestCategory.Side && !n.IsPosted))
            throw new Exception("Main notice posts before all side notices settle");
        if (trackedFirst != null && trackedFirst.IsMoving && !firstHitchInjected && trackedFirst.Opacity < 0.01f)
        {
            firstHitchInjected = true;
            // Reproduce the long loading frame that previously swallowed the first slide.
            System.Threading.Thread.Sleep(320);
        }
        if (trackedFirst != null && trackedFirst.IsMoving && trackedFirst.Opacity > 0.05f
            && Vector2.Distance(trackedFirst.Rect.anchoredPosition, trackedFirst.BoardPosition) > 0.5f)
        {
            firstMotionFrames.Add(Time.frameCount);
            if (!firstCapture) { Capture("fix-first-post"); firstCapture = true; }
        }
    }
    static void RememberBoard()
    {
        session = flow.Session; offers = session.Offers.ToArray(); notices = flow.Notices.ToArray();
        homes = notices.ToDictionary(n => n.Quest, n => new Vector3(n.BoardPosition.x, n.BoardPosition.y, n.BoardAngle));
    }
    static void CheckStableBoard()
    {
        Check(flow.Session == session && session.Offers.SequenceEqual(offers) && flow.Notices.SequenceEqual(notices), "Navigation preserves the session and exact notice objects");
        Check(notices.All(n => Vector3.Distance(homes[n.Quest], new Vector3(n.BoardPosition.x, n.BoardPosition.y, n.BoardAngle)) < 0.2f
            && Vector2.Distance(n.Rect.anchoredPosition, n.BoardPosition) < 0.1f
            && Mathf.Abs(Mathf.DeltaAngle(n.Rect.eulerAngles.z, n.BoardAngle)) < 0.1f), "Returning restores saved paper position and rotation without reshuffling");
    }
    static void Update()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (next == 0) { next = EditorApplication.timeSinceStartup + 0.05; deadline = next + 10; }
        try
        {
            ObservePosting();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Pre-mission phase timed out: " + phase);
            if (EditorApplication.timeSinceStartup < next) return;
            switch (phase)
            {
                case 0:
                    checks = SessionState.GetInt(Pending + ".Count", 0);
                    Application.runInBackground = true;
                    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    foreach (var device in InputSystem.devices.Where(d => d is Mouse || d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
                    keyboard = InputSystem.AddDevice<Keyboard>();
                    mouse = InputSystem.AddDevice<Mouse>(); MoveMouse(new Vector2(-100, -100));
                    captureDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "PreMissionCaptures");
                    Directory.CreateDirectory(captureDirectory);
                    flow = Object.FindAnyObjectByType<PreMissionFlowController>();
                    CheckView(PreMissionView.Board);
                    Check(Cursor.lockState == CursorLockMode.None && Cursor.visible, "UI has a free visible cursor");
                    break;
                case 1:
                    if (flow.IsPosting) return;
                    var initialMain = flow.Notices.First(n => n.Quest.category == QuestCategory.Main);
                    if (!HoverCycle(initialMain, "initial-main")) return;
                    CheckComposedLayouts();
                    RememberBoard();
                    Check(sawPosting && notices.All(n => n.IsPosted && n.Button.interactable), "Sequential side-first/main-last posting finishes with every quest selectable");
                    var main = notices.First(n => n.Quest.category == QuestCategory.Main);
                    Check(main.BoardSize.x * main.BoardSize.y > notices.Where(n => n != main).Max(n => n.BoardSize.x * n.BoardSize.y) * 1.8f,
                        "Featured main paper is visibly larger without covering side notices");
                    Check(notices.All(n => Mathf.Abs(n.BoardAngle) <= 1.25f), "Authored paper tilts stay subtle and consistent");
                    Check(notices.All(n => PaperHit(n) == n.gameObject), "Every main and side quest has an unobscured pointer hit target");
                    Capture("q23-board"); Capture("q23-board-4x3", 1024, 768);
                    selected = offers.First(q => q.category == QuestCategory.Side);
                    selectedTextBeforePull = notices.Single(n => n.Quest == selected).TextSize;
                    ClickPaper(notices.Single(n => n.Quest == selected));
                    flow.AcceptQuest(); flow.Back(); flow.SelectQuest(offers.First(q => q != selected));
                    Check(flow.IsTransitioning && session.SelectedQuest == selected && session.View == PreMissionView.Details,
                        "Rapid clicks, Accept and Back cannot interrupt the paper pull or change its selection");
                    break;
                case 2:
                    if (Waiting())
                    {
                        if (!capturedPull && flow.IsTransitioning)
                        {
                            var presentation = new SerializedObject(Field<PreMissionPaperPresentation>("presentation"));
                            var travel = (QuestNoticeView)presentation.FindProperty("travellingPaper").objectReferenceValue;
                            if (travel.TextSize > selectedTextBeforePull * 1.3f && travel.Opacity > 0.1f)
                            {
                                Check(true, "Printed notice text grows with the paper during its approach");
                                Capture("q23-pull"); capturedPull = true;
                            }
                        }
                        return;
                    }
                    Check(capturedPull, "The paper zoom exposes visibly enlarged text before revealing the briefing");
                    CheckView(PreMissionView.Details);
                    Check(Field<TMP_Text>("detailsTitle").text == selected.title && Field<TMP_Text>("detailsBody").text.Contains(selected.gniReward + " Gni"), "Briefing displays the selected side quest data");
                    Check(!notices.Single(n => n.Quest == selected).IsPaperVisible, "Selected notice is taken off the board while inspecting details");
                    Capture("q23-details"); Capture("q23-details-4x3", 1024, 768);
                    ClickControl(Field<Button>("declineButton")); flow.Back();
                    Check(flow.IsTransitioning && session.View == PreMissionView.Board, "Decline starts one return transition; repeated Back is ignored");
                    break;
                case 3:
                    if (Waiting()) return;
                    if (!HoverCycle(notices.Single(n => n.Quest == selected), "returned-side")) return;
                    CheckView(PreMissionView.Board); CheckStableBoard();
                    Check(notices.All(n => n.IsPosted && n.IsPaperVisible) && !flow.IsPosting, "Decline restores every paper without replaying posting");
                    selected = offers.First(q => q.category == QuestCategory.Main);
                    MoveMouse(RectTransformUtility.WorldToScreenPoint(null, notices.Single(n => n.Quest == selected).Rect.position));
                    Time.timeScale = 0f;
                    ClickPaper(notices.Single(n => n.Quest == selected));
                    break;
                case 4:
                    if (Waiting()) return;
                    CheckView(PreMissionView.Details);
                    Check(session.SelectedQuest == selected && Time.timeScale == 0f, "Main quest is freely selectable and its paper transition runs while paused");
                    Capture("q23-main-details"); Time.timeScale = 1f;
                    ClickControl(Field<Button>("acceptButton"));
                    break;
                case 5:
                    if (Waiting()) return;
                    CheckView(PreMissionView.Torso); selectedTorso = session.SelectedTorso;
                    Check(selectedTorso != null && !Field<Button>("previousTorsoButton").interactable && !Field<Button>("nextTorsoButton").interactable,
                        "Existing single torso remains valid with disabled browsing");
                    Check(Field<StarterTorsoPreview>("torsoPreview").HasPreview && StarterTorsoChecks.HasRenderedModel(Field<StarterTorsoPreview>("torsoPreview")),
                        "The shipped single torso has a real rendered model preview");
                    Capture("q23-torso"); Capture("q4-torso-4x3", 1024, 768); Escape();
                    break;
                case 6:
                    if (Waiting()) return;
                    CheckView(PreMissionView.Details);
                    Check(session.SelectedQuest == selected && session.SelectedTorso == selectedTorso, "Torso Back retains both choices and the same briefing");
                    ClickControl(Field<Button>("acceptButton"));
                    break;
                case 7:
                    if (Waiting()) return;
                    Field<Button>("confirmTorsoButton").onClick.Invoke(); Field<Button>("confirmTorsoButton").onClick.Invoke();
                    Check(session.LaunchRequested && !Field<Button>("confirmTorsoButton").interactable && session.SelectedQuest == selected
                        && session.SelectedTorso == selectedTorso && SceneManager.GetActiveScene().path == PreMissionPrototypeSetup.ScenePath,
                        "Empty mission-start handoff still receives one validated selection and loads no gameplay scene");
                    Escape(); break;
                case 8:
                    if (Waiting()) return;
                    Escape(); break;
                case 9:
                    if (Waiting()) return;
                    var returnedMain = notices.Single(n => n.Quest == selected);
                    if (hoverStage == 0) Check(returnedMain.IsHighlighted, "A stationary pointer is resynchronized when the selected paper returns");
                    if (!HoverCycle(returnedMain, "returned-main")) return;
                    CheckView(PreMissionView.Board); CheckStableBoard();
                    var hovered = notices.First(n => n.Quest.category == QuestCategory.Side);
                    Check(flow.GetComponentsInChildren<QuestNoticeView>(true).Length == notices.Length + 1, "Repeated browsing reuses one travelling paper without accumulating notice objects");
                    EventSystem.current.SetSelectedGameObject(null);
                    ExecuteEvents.Execute(hovered.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
                    Check(hovered.IsHighlighted, "Pointer hover highlights the paper");
                    ExecuteEvents.Execute(hovered.gameObject, new PointerEventData(EventSystem.current), ExecuteEvents.pointerExitHandler);
                    Check(!hovered.IsHighlighted, "Pointer exit clears its highlight");
                    flow.SelectQuest(hovered.Quest);
                    flow.gameObject.SetActive(false); flow.gameObject.SetActive(true);
                    CheckView(PreMissionView.Details);
                    Check(!flow.IsTransitioning && !flow.IsPosting && flow.Notices.SequenceEqual(notices), "Disabling mid-pull settles the selected details without duplicate UI or lost state");
                    flow.Back(); break;
                case 10:
                    if (Waiting()) return;
                    CheckView(PreMissionView.Board);
                    flow.CreateFreshBoard(); RememberBoard();
                    trackedFirst = flow.Notices.First(n => n.Quest.category == QuestCategory.Side);
                    firstMotionFrames.Clear(); firstHitchInjected = firstCapture = false;
                    break;
                case 11:
                    var postedSide = flow.Notices.FirstOrDefault(n => n.IsPosted && n.Quest.category == QuestCategory.Side);
                    if (postedSide == null) return;
                    Check(firstHitchInjected && firstMotionFrames.Count >= 3,
                        "The first notice slides through multiple visible frames even after a simulated loading hitch");
                    Check(flow.IsPosting, "A settled side paper can be selected before the posting sequence finishes");
                    flow.SelectQuest(postedSide.Quest);
                    Check(!flow.IsPosting && flow.Notices.All(n => n.IsPosted), "Early selection settles unfinished posting without regenerating offers");
                    break;
                case 12:
                    if (Waiting()) return;
                    CheckView(PreMissionView.Details); flow.Back(); break;
                case 13:
                    if (Waiting()) return;
                    flow.CreateFreshBoard(); RememberBoard();
                    flow.gameObject.SetActive(false); flow.gameObject.SetActive(true);
                    CheckView(PreMissionView.Board);
                    Check(!flow.IsPosting && flow.Notices.All(n => n.IsPosted) && flow.Session == session && flow.Notices.SequenceEqual(notices),
                        "Disable/re-enable mid-post preserves offers and settles all notices without replaying");
                    Field<Button>("exitButton").onClick.Invoke(); break;
                case 14:
                    Check(SceneManager.GetActiveScene().name == "MainMenuScene", "Board Back returns to the existing menu");
                    var entry = GameObject.Find("Quest Board").GetComponent<Button>();
                    Check(entry != null && GameObject.Find("Gym") != null && GameObject.Find("Zoo") != null, "Existing menu destinations are retained");
                    Capture("q23-menu"); entry.onClick.Invoke(); break;
                case 15:
                    flow = Object.FindAnyObjectByType<PreMissionFlowController>();
                    if (torsoChecks == null)
                    {
                        if (flow == null || flow.IsPosting) return;
                        Check(SceneManager.GetActiveScene().path == PreMissionPrototypeSetup.ScenePath && flow.Session != session, "Reopening the UI starts a fresh independent board session");
                        CheckView(PreMissionView.Board);
                        torsoChecks = new StarterTorsoChecks(flow, keyboard, Check, label => Capture(label));
                        deadline = EditorApplication.timeSinceStartup + 45;
                    }
                    if (!torsoChecks.Step()) return;
                    InputSystem.RemoveDevice(keyboard); SessionState.SetBool(Pending, false);
                    InputSystem.RemoveDevice(mouse);
                    Debug.Log("PRE-MISSION CHECKS COMPLETE: " + checks + " checks; captures: " + captureDirectory);
                    EditorApplication.Exit(0); return;
            }
            phase++; next = EditorApplication.timeSinceStartup + 0.12; deadline = next + 10;
        }
        catch (Exception exception)
        {
            Time.timeScale = 1f;
            SessionState.SetBool(Pending, false); Debug.LogException(exception); EditorApplication.Exit(1);
        }
    }
}
