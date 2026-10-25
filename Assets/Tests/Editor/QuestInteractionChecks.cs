using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Physical continuity and lifetime checks run only in the disposable Play Mode fixture.
public sealed class QuestInteractionChecks
{
    readonly PreMissionFlowController flow;
    readonly PreMissionPaperPresentation presentation;
    readonly QuestNoticeView travel;
    readonly Action<bool, string> check;
    readonly Action<string> capture;
    readonly Action<Vector2> moveMouse;
    readonly Action escape;
    readonly QuestSelectionSession session;
    readonly QuestDefinition[] offers;
    readonly Dictionary<QuestNoticeView, Home> homes;
    QuestNoticeView selected;
    GameObject doomed;
    int phase, cycle;
    double next;
    Vector2 startPosition, startSize;
    float minPressRatio;
    bool sawLift, sawSettle, capturedLift, capturedSettle;
    struct Home
    {
        public Transform parent;
        public int sibling;
        public Vector2 position, size;
        public float angle;
        public QuestPostingStyle style;
    }
    public QuestInteractionChecks(PreMissionFlowController flow, Action<bool, string> check, Action<string> capture,
        Action<Vector2> moveMouse, Action escape)
    {
        this.flow = flow; this.check = check; this.capture = capture; this.moveMouse = moveMouse; this.escape = escape;
        presentation = Field<PreMissionPaperPresentation>(flow, "presentation");
        travel = Field<QuestNoticeView>(presentation, "travellingPaper");
        session = flow.Session; offers = session.Offers.ToArray();
        homes = flow.Notices.ToDictionary(n => n, n => new Home { parent = n.transform.parent, sibling = n.transform.GetSiblingIndex(),
            position = n.BoardPosition, size = n.BoardSize, angle = n.BoardAngle, style = n.PostingStyle });
    }
    static T Field<T>(Object target, string name) where T : Object => (T)new SerializedObject(target).FindProperty(name).objectReferenceValue;
    Vector2 Center(RectTransform rect) => ((RectTransform)travel.transform.parent).InverseTransformPoint(rect.TransformPoint(rect.rect.center));
    Vector2 Size(RectTransform rect) => Vector2.Scale(rect.rect.size,
        new Vector2(rect.lossyScale.x / travel.transform.parent.lossyScale.x, rect.lossyScale.y / travel.transform.parent.lossyScale.y));
    void CheckHomes(string context)
    {
        check(flow.Session == session && flow.Session.Offers.SequenceEqual(offers) && flow.Notices.Count == homes.Count
            && flow.Notices.All(n => homes.ContainsKey(n) && n.transform.parent == homes[n].parent && n.transform.GetSiblingIndex() == homes[n].sibling
                && n.PostingStyle == homes[n].style && Vector2.Distance(n.Rect.anchoredPosition, homes[n].position) < 0.01f
                && Vector2.Distance(n.Rect.sizeDelta, homes[n].size) < 0.01f && n.Rect.localScale == Vector3.one
                && Mathf.Abs(Mathf.DeltaAngle(n.Rect.localEulerAngles.z, homes[n].angle)) < 0.01f), context);
    }
    void ObserveOtherPapers()
    {
        foreach (var n in flow.Notices.Where(n => n != selected))
            if (n.Rect.anchoredPosition != homes[n].position || n.Rect.localScale != Vector3.one
                || n.VisualRect.localScale != Vector3.one || n.VisualRect.anchoredPosition.sqrMagnitude > 0.001f)
                throw new Exception("Another notice moved while the selected paper was being handled");
    }
    void BeginSelection()
    {
        startPosition = Center(selected.VisualRect); startSize = Size(selected.VisualRect);
        float printed = selected.TextSize * selected.VisualRect.lossyScale.x / travel.transform.parent.lossyScale.x;
        float angle = selected.VisualRect.eulerAngles.z - travel.transform.parent.eulerAngles.z;
        var pointer = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
        if (cycle % 2 == 0) ExecuteEvents.Execute(selected.gameObject, pointer, ExecuteEvents.pointerClickHandler);
        else ExecuteEvents.Execute(selected.gameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);
        minPressRatio = travel.Rect.rect.height / startSize.y; sawLift = sawSettle = false;
        check(flow.IsTransitioning && session.SelectedQuest == selected.Quest && selected.IsMoving && !selected.Button.IsInteractable()
            && !selected.IsPaperVisible && travel.gameObject.activeSelf && travel.Opacity == 1f,
            "Pointer/Submit immediately starts one locked paper handoff, cycle " + cycle);
        check(Vector2.Distance(Center(travel.Rect), startPosition) < 0.1f && minPressRatio > 0.98f && minPressRatio < 1f
            && Mathf.Abs(Mathf.DeltaAngle(travel.Rect.localEulerAngles.z, angle)) < 0.1f
            && Mathf.Abs(travel.TextSize / printed - minPressRatio) < 0.01f,
            "Click press starts at the hovered paper's visible pose and printed text size");
        check(Field<CanvasGroup>(presentation, "details").alpha == 0f && !Field<Button>(flow, "acceptButton").IsInteractable(),
            "Briefing content and controls stay concealed during the initial click/lift");
        flow.SelectQuest(flow.Notices.First(n => n != selected).Quest); flow.AcceptQuest(); flow.Back();
        check(session.SelectedQuest == selected.Quest && session.View == PreMissionView.Details && flow.IsTransitioning,
            "Rapid selection, Accept and Back cannot replace the active pickup");
        if (cycle == 0)
        {
            capture("q52-click-press");
            check(Vector2.Distance(Center(travel.Rect), startPosition) < 0.1f
                && Mathf.Abs(travel.Rect.rect.height / startSize.y - minPressRatio) < 0.001f,
                "The click pose remains aligned after a Canvas render-mode refit and restoration");
        }
    }
    public bool Step()
    {
        if (EditorApplication.timeSinceStartup < next) return false;
        switch (phase)
        {
            case 0:
                selected = cycle % 2 == 0 ? flow.Notices.Where(n => n.Quest.category == QuestCategory.Side).ElementAt(cycle / 2)
                    : flow.Notices.First(n => n.Quest.category == QuestCategory.Main);
                moveMouse(RectTransformUtility.WorldToScreenPoint(null, selected.Rect.position));
                next = EditorApplication.timeSinceStartup + 0.18; break;
            case 1:
                check(selected.IsHighlighted && selected.VisualRect.localScale.x > 1.02f, "Hover smoothly reaches its restrained raised pose before selection");
                BeginSelection(); break;
            case 2:
                if (flow.IsTransitioning)
                {
                    ObserveOtherPapers();
                    float ratio = travel.Rect.rect.height / startSize.y;
                    minPressRatio = Mathf.Min(minPressRatio, ratio);
                    if (Center(travel.Rect).y > startPosition.y + 3f && ratio < 1.12f) sawLift = true;
                    if (!capturedLift && sawLift && cycle == 0) { capture("q52-pickup-lift"); capturedLift = true; }
                    return false;
                }
                check(minPressRatio < 0.985f && sawLift, "Observed compression, release and initial lift before the foreground approach");
                check(session.View == PreMissionView.Details && session.SelectedQuest == selected.Quest && !selected.IsMoving
                    && !selected.IsPaperVisible && !travel.gameObject.activeSelf && Field<Button>(flow, "acceptButton").IsInteractable(),
                    "Pickup ends on the correct briefing with no visible proxy or disabled controls");
                CheckHomes("Pickup preserves every notice's root, hierarchy, sibling order and Q5.1 assignment");
                if (cycle % 2 == 0) escape(); else Field<Button>(flow, "declineButton").onClick.Invoke();
                flow.Back(); flow.SelectQuest(flow.Notices.First(n => n != selected).Quest); flow.AcceptQuest();
                check(flow.IsTransitioning && session.View == PreMissionView.Board && selected.IsMoving
                    && !selected.IsPaperVisible && travel.gameObject.activeSelf, "Decline/Escape starts one guarded return transition");
                break;
            case 3:
                if (flow.IsTransitioning)
                {
                    ObserveOtherPapers();
                    var home = Center(selected.Rect); float ratio = travel.Rect.rect.height / Size(selected.Rect).y;
                    if (ratio < 0.995f && Vector2.Distance(Center(travel.Rect), home) < 4f)
                    {
                        sawSettle = true;
                        if (cycle == 0 && !capturedSettle) { capture("q52-return-settle"); capturedSettle = true; }
                    }
                    return false;
                }
                check(sawSettle, "Return includes a small compression/settle before the board unlocks");
                next = EditorApplication.timeSinceStartup + 0.16; break;
            case 4:
                CheckHomes("Repeated decline restores the exact board pose and identity, cycle " + cycle);
                check(!flow.IsPosting && flow.Notices.All(n => n.IsPosted && n.IsPaperVisible && !n.IsMoving && n.Button.IsInteractable())
                    && selected.IsHighlighted && selected.VisualRect.localScale.x > 1.02f
                    && flow.GetComponentsInChildren<QuestNoticeView>(true).Length == flow.Notices.Count + 1 && !travel.gameObject.activeSelf,
                    "Board controls and stationary-pointer hover recover without reposting or duplicate papers");
                moveMouse(new Vector2(-100, -100));
                if (++cycle < 4) { phase = 0; next = EditorApplication.timeSinceStartup + 0.16; return false; }
                next = EditorApplication.timeSinceStartup + 0.16; break;
            case 5:
                flow.SelectQuest(selected.Quest); break;
            case 6:
                if (travel.Rect.rect.height < Size(selected.Rect).y * 1.3f) return false;
                flow.gameObject.SetActive(false); flow.gameObject.SetActive(true);
                check(!flow.IsTransitioning && session.View == PreMissionView.Details && !travel.gameObject.activeSelf
                    && !selected.IsMoving, "Disable/re-enable mid-approach settles the current briefing and releases the handled notice");
                flow.Back(); break;
            case 7:
                if (!flow.IsTransitioning || travel.Rect.rect.height >= Size(selected.Rect).y * 0.995f) return false;
                flow.gameObject.SetActive(false); flow.gameObject.SetActive(true);
                check(!flow.IsTransitioning && session.View == PreMissionView.Board && flow.Notices.All(n => !n.IsMoving && n.IsPaperVisible)
                    && !travel.gameObject.activeSelf, "Disable/re-enable during return settling restores the board and clears the proxy");
                CheckHomes("Interruption preserves the same board objects and original hierarchy");
                flow.SelectQuest(selected.Quest); flow.CreateFreshBoard();
                presentation.FinishPosting();
                check(!flow.IsTransitioning && !flow.IsPosting && flow.Session != session && flow.Session.View == PreMissionView.Board
                    && !travel.gameObject.activeSelf && flow.Notices.All(n => !n.IsMoving && n.IsPaperVisible),
                    "An explicit fresh board cancels the old click coroutine before destroying its notices");
                break;
            case 8:
                doomed = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PreMissionPrototypeSetup.PrefabPath));
                var doomedFlow = doomed.GetComponent<PreMissionFlowController>();
                Field<PreMissionPaperPresentation>(doomedFlow, "presentation").FinishPosting();
                doomedFlow.SelectQuest(doomedFlow.Session.Offers.First()); Object.Destroy(doomed);
                break;
            case 9:
                check(doomed == null && Object.FindObjectsByType<PreMissionFlowController>().Length == 1 && !flow.IsTransitioning,
                    "Destroying a UI during its click/pickup leaves no live controller or orphan travelling paper");
                return true;
        }
        phase++; return false;
    }
}
