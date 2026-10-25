using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Q5.3 verifies the actual authored torso assets and stops at a recording hook.
public sealed class PreMissionTransitionChecks
{
    readonly PreMissionFlowController flow;
    readonly PreMissionPaperPresentation presentation;
    readonly Action<bool, string> check;
    readonly Action<string> capture;
    readonly QuestSelectionSession session;
    readonly QuestDefinition quest;
    readonly PartData original, heavy;
    readonly string description;
    readonly CanvasGroup[] groups;
    readonly CanvasGroup choice;
    readonly QuestNoticeView travel;
    readonly Vector2 detailsHome, torsoHome, choiceHome;
    readonly MissionStartHook originalHook;
    PreMissionRecordingHook recording;
    GameObject previewStage;
    float rotation;
    int phase;
    bool sawStagger, sawForward, sawBackward, sawNextOut, sawNextIn, sawPreviousOut, sawPreviousIn, sawConfirm, capturedNext;
    public PreMissionTransitionChecks(PreMissionFlowController flow, Action<bool, string> check, Action<string> capture)
    {
        this.flow = flow; this.check = check; this.capture = capture;
        presentation = Field<PreMissionPaperPresentation>(flow, "presentation");
        var fields = new SerializedObject(presentation); var sequence = fields.FindProperty("briefingGroups");
        groups = Enumerable.Range(0, sequence.arraySize).Select(i => (CanvasGroup)sequence.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
        choice = Field<CanvasGroup>(presentation, "torsoChoice"); travel = Field<QuestNoticeView>(presentation, "travellingPaper");
        session = flow.Session; quest = session.Offers.First(); original = session.Torsos[0]; heavy = session.Torsos[1];
        originalHook = Field<MissionStartHook>(flow, "missionStartHook");
        var catalogue = Field<QuestBoardCatalogue>(flow, "catalogue");
        check(original != heavy && original.partId != heavy.partId && heavy == AssetDatabase.LoadAssetAtPath<PartData>(PreMissionTransitionSetup.HeavyTorsoPath)
            && original.GetStat(StatType.Health) == 100 && heavy.GetStat(StatType.Health) == 150,
            "Authored Original/Heavy torso definitions are distinct with actual 100/150 health contributions");
        check(catalogue.FindTorsoVisual(original).model == catalogue.FindTorsoVisual(heavy).model
            && catalogue.FindTorsoVisual(original).rotation == catalogue.FindTorsoVisual(heavy).rotation,
            "Both authored torso choices reference exactly the same existing model and orientation");
        detailsHome = Rect("details").anchoredPosition; torsoHome = Rect("torso").anchoredPosition; choiceHome = ((RectTransform)choice.transform).anchoredPosition;
        description = quest.description;
        quest.description += "\n\n" + string.Join(" ", Enumerable.Repeat("Temporary long-briefing verification text.", 110));
        Time.timeScale = 0f; flow.SelectQuest(quest);
        check(flow.IsTransitioning && groups.Length == 5 && groups.All(g => g.alpha == 0f), "All five information groups start concealed during paper pickup");
    }
    static T Field<T>(Object target, string name) where T : Object => (T)new SerializedObject(target).FindProperty(name).objectReferenceValue;
    T Flow<T>(string name) where T : Object => Field<T>(flow, name);
    RectTransform Rect(string field) => (RectTransform)Field<CanvasGroup>(presentation, field).transform;
    StarterTorsoPreview Preview => Flow<StarterTorsoPreview>("torsoPreview");
    float ChoiceX => ((RectTransform)choice.transform).anchoredPosition.x - choiceHome.x;
    void ToggleController() { flow.enabled = false; flow.enabled = true; }
    void Click(string field) => Flow<Button>(field).onClick.Invoke();
    void ExpectTorso(PartData expected)
    {
        check(session.SelectedTorso == expected && Preview.Torso == expected && Flow<TMP_Text>("torsoName").text == expected.partName
            && Flow<TMP_Text>("torsoDescription").text.Contains(expected.GetStat(StatType.Health).ToString())
            && ChoiceX == 0f && choice.alpha == 1f && choice.transform.localScale == Vector3.one,
            "Settled torso data, model preview, label, health and presentation agree: " + expected.partName);
    }
    void NextWithRapidInput()
    {
        flow.NextTorso(); flow.NextTorso(); flow.PreviousTorso(); flow.Back(); flow.ConfirmTorso();
        check(flow.IsTransitioning && session.SelectedTorso == original && !session.LaunchRequested && session.View == PreMissionView.Torso,
            "Rapid competing input accepts one browse operation and leaves the outgoing data intact until its midpoint");
    }
    public bool Step()
    {
        switch (phase)
        {
            case 0:
                if (flow.IsTransitioning)
                {
                    if (groups[0].alpha > 0f)
                    {
                        if (travel.gameObject.activeSelf || Field<CanvasGroup>(presentation, "board").gameObject.activeSelf
                            || Flow<Button>("acceptButton").IsInteractable()) throw new Exception("Briefing reveal left a board/proxy visible or unlocked input before completion");
                        for (int i = 0; i < groups.Length - 1; i++)
                            if (groups[i].alpha < groups[i + 1].alpha) throw new Exception("Briefing reveal order reversed");
                        if (!sawStagger && groups[0].alpha > 0.3f && groups[4].alpha < 0.1f) { sawStagger = true; capture("q53-briefing-stagger"); }
                    }
                    return false;
                }
                check(sawStagger && groups.All(g => g.alpha == 1f) && Flow<Button>("acceptButton").IsInteractable(),
                    "Briefing reveals header, objective, mission information, reward and controls in order while paused");
                check(Flow<TMP_Text>("detailsBody").text == quest.description && Flow<TMP_Text>("detailsReward").text.Contains(quest.gniReward + " Gni")
                    && Flow<TMP_Text>("detailsInfo").text.Contains(quest.estimatedMinutes + " minutes"), "Separated briefing groups preserve the actual mission data");
                var scroll = Flow<TMP_Text>("detailsBody").GetComponentInParent<ScrollRect>();
                Canvas.ForceUpdateCanvases();
                check(scroll.content.rect.height > scroll.viewport.rect.height + 100 && scroll.verticalNormalizedPosition > 0.99f,
                    "Long briefing content grows inside the existing scroll viewport and starts at the top");
                scroll.verticalNormalizedPosition = 0f; capture("q53-long-briefing-bottom"); flow.AcceptQuest(); break;
            case 1:
                if (flow.IsTransitioning)
                {
                    if (Rect("details").anchoredPosition.x < detailsHome.x && Rect("torso").anchoredPosition.x > torsoHome.x) sawForward = true;
                    return false;
                }
                check(sawForward && Rect("details").anchoredPosition == detailsHome && Rect("torso").anchoredPosition == torsoHome,
                    "Forward navigation slides/fades in complementary directions and restores both view poses");
                ExpectTorso(original);
                previewStage = Resources.FindObjectsOfTypeAll<GameObject>().Single(g => g.name == "Starter Torso Preview Stage" && g.activeSelf);
                Preview.OnDrag(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, delta = new Vector2(50, 0) });
                rotation = Preview.Rotation; NextWithRapidInput();
                Preview.OnDrag(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, delta = new Vector2(100, 0) });
                check(Preview.Rotation == rotation, "Model drag is ignored while torso navigation locks input"); break;
            case 2:
                if (flow.IsTransitioning)
                {
                    if (session.SelectedTorso == original && ChoiceX < -2f) sawNextOut = true;
                    if (session.SelectedTorso == heavy && ChoiceX > 2f)
                    {
                        sawNextIn = true;
                        if (!capturedNext && choice.alpha > 0.25f) { capture("q53-next-entering"); capturedNext = true; }
                    }
                    return false;
                }
                check(sawNextOut && sawNextIn, "Next exits left and enters right before settling at the original choice position");
                check(Resources.FindObjectsOfTypeAll<GameObject>().Single(g => g.name == "Starter Torso Preview Stage" && g.activeSelf) == previewStage
                    && Preview.Rotation == rotation && StarterTorsoChecks.HasRenderedModel(Preview),
                    "Same-model browsing reuses its geometry/camera stage and rotation while render textures may refit to the Canvas");
                ExpectTorso(heavy); flow.Back(); break;
            case 3:
                if (flow.IsTransitioning)
                {
                    if (Rect("torso").anchoredPosition.x > torsoHome.x && Rect("details").anchoredPosition.x < detailsHome.x) sawBackward = true;
                    return false;
                }
                check(sawBackward && session.SelectedQuest == quest && session.SelectedTorso == heavy
                    && Flow<TMP_Text>("detailsBody").GetComponentInParent<ScrollRect>().verticalNormalizedPosition < 0.01f,
                    "Backward navigation reverses direction and retains the quest, Heavy choice and briefing scroll position");
                flow.AcceptQuest(); break;
            case 4:
                if (flow.IsTransitioning) return false;
                ExpectTorso(heavy); flow.PreviousTorso(); break;
            case 5:
                if (flow.IsTransitioning)
                {
                    if (session.SelectedTorso == heavy && ChoiceX > 2f) sawPreviousOut = true;
                    if (session.SelectedTorso == original && ChoiceX < -2f) sawPreviousIn = true;
                    return false;
                }
                check(sawPreviousOut && sawPreviousIn, "Previous reverses torso movement: exits right, enters left");
                ExpectTorso(original);
                flow.NextTorso(); ToggleController();
                check(!flow.IsTransitioning && session.SelectedTorso == original, "Interrupted outgoing browse retains its uncommitted torso choice");
                flow.NextTorso(); break;
            case 6:
                if (session.SelectedTorso != heavy) return false;
                ToggleController(); ExpectTorso(heavy);
                check(!flow.IsTransitioning, "Interrupted incoming browse retains the committed choice and restores its UI");
                flow.PreviousTorso(); break;
            case 7:
                if (flow.IsTransitioning) return false;
                recording = flow.gameObject.AddComponent<PreMissionRecordingHook>();
                var fields = new SerializedObject(flow); fields.FindProperty("missionStartHook").objectReferenceValue = recording; fields.ApplyModifiedPropertiesWithoutUndo();
                flow.ConfirmTorso(); ToggleController();
                check(recording.Calls == 0 && !session.LaunchRequested && !flow.IsTransitioning,
                    "Disabling during confirmation cancels the unsent callback and allows a deliberate retry");
                flow.ConfirmTorso(); break;
            case 8:
                if (flow.IsTransitioning) return false;
                check(recording.Calls == 1 && recording.Quest == quest && recording.Torso == original, "Original torso confirmation passes its exact data once after feedback");
                flow.Back(); break;
            case 9:
                if (flow.IsTransitioning) return false;
                flow.AcceptQuest(); break;
            case 10:
                if (flow.IsTransitioning) return false;
                flow.NextTorso(); break;
            case 11:
                if (flow.IsTransitioning) return false;
                ExpectTorso(heavy); capture("q53-heavy-torso");
                flow.ConfirmTorso(); flow.ConfirmTorso(); flow.NextTorso(); flow.Back(); break;
            case 12:
                if (flow.IsTransitioning)
                {
                    if (choice.transform.localScale.x > 1.01f) sawConfirm = true;
                    if (recording.Calls != 1) throw new Exception("Confirmation callback fired before feedback completed");
                    return false;
                }
                check(sawConfirm && recording.Calls == 2 && recording.Quest == quest && recording.Torso == heavy
                    && recording.Torso.GetStat(StatType.Health) == 150 && session.LaunchRequested
                    && SceneManager.GetActiveScene().path == PreMissionPrototypeSetup.ScenePath,
                    "Heavy confirmation pulses, submits the exact 150-health definition once and remains in the UI scene");
                capture("q53-confirmed"); flow.Back(); break;
            case 13:
                if (flow.IsTransitioning) return false;
                flow.Back(); break;
            case 14:
                if (flow.IsTransitioning) return false;
                quest.description = description; Time.timeScale = 1f;
                var restore = new SerializedObject(flow); restore.FindProperty("missionStartHook").objectReferenceValue = originalHook; restore.ApplyModifiedPropertiesWithoutUndo();
                Object.Destroy(recording);
                check(flow.Session == session && session.View == PreMissionView.Board && !flow.IsPosting && flow.Notices.All(n => n.IsPosted && !n.IsMoving),
                    "Q5.3 navigation and confirmation preserve the original quest board without reposting");
                return true;
        }
        phase++; return false;
    }
}
