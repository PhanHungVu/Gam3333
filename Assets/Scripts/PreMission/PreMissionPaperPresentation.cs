using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Only presentation lives here. The selection session remains the navigation authority.
[DisallowMultipleComponent]
public sealed class PreMissionPaperPresentation : MonoBehaviour
{
    [SerializeField] CanvasGroup board;
    [SerializeField] CanvasGroup details;
    [SerializeField] CanvasGroup torso;
    [SerializeField] PreMissionNoticeLayout layout;
    [SerializeField] RectTransform briefingPaper;
    [SerializeField] CanvasGroup shade;
    [SerializeField] QuestNoticeView travellingPaper;
    [Header("Quest posting (unscaled seconds)")]
    [SerializeField, Min(0.01f)] float dropDuration = 0.30f;
    [SerializeField, Min(0.01f)] float zoomDuration = 0.30f;
    [SerializeField, Min(0.01f)] float paintedDuration = 0.48f;
    [SerializeField, Min(0.01f)] float settleDuration = 0.10f;
    [SerializeField, Min(0f)] float postingGap = 0.07f;
    [SerializeField, Range(1f, 1.5f)] float mainPostingTimeMultiplier = 1.12f;
    [Header("Posting motion (relative to the assigned layout)")]
    [SerializeField, Min(0f)] float dropHeight = 48f;
    [SerializeField, Min(0f)] float settleDistance = 4f;
    [SerializeField, Range(0f, 0.06f)] float impactCompression = 0.025f;
    [SerializeField, Range(1f, 1.2f)] float zoomStartScale = 1.10f;
    [SerializeField, Range(0.9f, 1f)] float zoomUndershootScale = 0.96f;
    [SerializeField, Range(1f, 1.06f)] float zoomReboundScale = 1.02f;
    [SerializeField, Range(0, 16)] int paintedEdgeSoftness = 6;
    [Header("Quest handling (unscaled seconds)")]
    [SerializeField, Min(0.01f)] float clickFeedbackDuration = 0.08f;
    [SerializeField, Range(0f, 0.06f)] float clickCompression = 0.03f;
    [SerializeField, Min(0.01f)] float pullDuration = 0.42f;
    [SerializeField, Min(0.01f)] float returnDuration = 0.36f;
    [SerializeField, Min(0.01f)] float returnSettleDuration = 0.08f;
    [SerializeField, Min(0f)] float pickupLift = 10f;
    [SerializeField, Range(1f, 1.08f)] float pickupLiftScale = 1.035f;
    [SerializeField, Min(0f)] float returnSettleDistance = 2f;
    [SerializeField, Range(0f, 0.04f)] float returnCompression = 0.012f;
    [Header("Screen transitions (unscaled seconds)")]
    [SerializeField, Min(0.01f)] float viewFadeDuration = 0.26f;
    [SerializeField, Min(0f)] float viewTravel = 28f;
    [SerializeField, Min(0.01f)] float boardEntryDuration = 0.60f;
    [SerializeField] CanvasGroup[] briefingGroups;
    [SerializeField, Min(0.01f)] float briefingFadeDuration = 0.28f;
    [SerializeField, Min(0f)] float briefingStagger = 0.18f;
    [Header("Starting torso transitions")]
    [SerializeField] CanvasGroup torsoChoice;
    [SerializeField] RectTransform confirmButton;
    [SerializeField, Min(0.01f)] float torsoBrowseDuration = 0.26f;
    [SerializeField, Min(0f)] float torsoBrowseTravel = 36f;
    [SerializeField, Min(0.01f)] float confirmationDuration = 0.36f;
    [SerializeField, Range(0f, 0.06f)] float confirmationPulse = 0.025f;
    readonly List<QuestNoticeView> notices = new List<QuestNoticeView>();
    Coroutine posting;
    Action posted;
    QuestNoticeView handledNotice;
    Action paperFrame;
    bool rememberedPoses;
    bool wasDisabled;
    Vector2 detailsPosition, torsoPosition, choicePosition;
    Vector3 choiceScale, confirmScale;
    bool tabletopBriefing, tabletopLayoutKnown;
    Vector2 briefingPosition;
    RectTransform[] tabletopRects;
    Vector2[] savedMin,savedMax,savedOffsetMin,savedOffsetMax;
    TMP_Text[] tabletopLabels;
    bool[] savedAutoSizing;
    float[] savedFontSize,savedFontMin,savedFontMax;
    RectTransform tabletopAccept,tabletopDecline;
    Vector3 acceptScale,declineScale;
    public bool TabletopBriefing => tabletopBriefing;
    public bool IsPosting { get; private set; }
    public bool IsTransitioning { get; private set; }

    public void ConfigureTabletopBriefing(bool enabled,Button accept,Button decline,TMP_Text title)
    {
        if(!enabled && !tabletopLayoutKnown) return;
        if(!tabletopLayoutKnown)
        {
            tabletopAccept=(RectTransform)accept.transform; tabletopDecline=(RectTransform)decline.transform;
            acceptScale=tabletopAccept.localScale; declineScale=tabletopDecline.localScale;
            tabletopRects=new[]{briefingPaper,tabletopAccept,tabletopDecline};
            savedMin=tabletopRects.Select(r=>r.anchorMin).ToArray(); savedMax=tabletopRects.Select(r=>r.anchorMax).ToArray();
            savedOffsetMin=tabletopRects.Select(r=>r.offsetMin).ToArray(); savedOffsetMax=tabletopRects.Select(r=>r.offsetMax).ToArray();
            tabletopLabels=new[]{title,accept.GetComponentInChildren<TMP_Text>(true),decline.GetComponentInChildren<TMP_Text>(true)};
            savedAutoSizing=tabletopLabels.Select(t=>t.enableAutoSizing).ToArray(); savedFontSize=tabletopLabels.Select(t=>t.fontSize).ToArray();
            savedFontMin=tabletopLabels.Select(t=>t.fontSizeMin).ToArray(); savedFontMax=tabletopLabels.Select(t=>t.fontSizeMax).ToArray();
            tabletopLayoutKnown=true;
        }
        tabletopBriefing=enabled;
        for(int i=0;i<tabletopRects.Length;i++)
        {
            tabletopRects[i].anchorMin=savedMin[i]; tabletopRects[i].anchorMax=savedMax[i];
            tabletopRects[i].offsetMin=savedOffsetMin[i]; tabletopRects[i].offsetMax=savedOffsetMax[i];
            tabletopLabels[i].enableAutoSizing=savedAutoSizing[i]; tabletopLabels[i].fontSize=savedFontSize[i];
            tabletopLabels[i].fontSizeMin=savedFontMin[i]; tabletopLabels[i].fontSizeMax=savedFontMax[i];
        }
        if(!enabled) return;
        PlaceTabletop(briefingPaper,new Vector2(0.50f,0.025f),new Vector2(1f,0.975f));
        PlaceTabletop(tabletopAccept,new Vector2(0.52f,0),new Vector2(0.94f,1));
        PlaceTabletop(tabletopDecline,new Vector2(0.06f,0),new Vector2(0.48f,1));
        for(int i=0;i<tabletopLabels.Length;i++)
        {
            tabletopLabels[i].enableAutoSizing=true; tabletopLabels[i].fontSizeMin=i==0?22:18; tabletopLabels[i].fontSizeMax=i==0?36:26;
        }
    }
    static void PlaceTabletop(RectTransform rect,Vector2 min,Vector2 max)
    { rect.anchorMin=min; rect.anchorMax=max; rect.offsetMin=rect.offsetMax=Vector2.zero; }

    void OnDisable()
    {
        if (!Application.isPlaying) return;
        wasDisabled = true;
        var flow = GetComponent<PreMissionFlowController>();
        if (flow != null && flow.isActiveAndEnabled) flow.RecoverPresentation();
        else { Cancel(); LockInput(); }
    }
    void OnEnable()
    {
        if (!Application.isPlaying) return;
        var flow = GetComponent<PreMissionFlowController>();
        if (!wasDisabled && (flow == null || flow.Session != null)) return;
        wasDisabled = false;
        if (flow != null && flow.isActiveAndEnabled) flow.RecoverPresentation();
    }
    public void LockInput()
    {
        foreach (var group in new[] { board, details, torso })
            if (group != null) group.interactable = group.blocksRaycasts = false;
    }

    public void Cancel()
    {
        StopAllCoroutines(); posting = null; posted = null; paperFrame = null;
        ReleaseNotice();
        ResetMotion(); SetBriefingReveal(1f);
        IsPosting = IsTransitioning = false;
        HideTravel();
    }
    public void BeginBoard(IReadOnlyList<QuestNoticeView> papers, int seed, Action ready,
        bool reserveMain = false, int smallSlots = 0, IReadOnlyCollection<QuestNoticeView> pending = null, bool fadeEntry = true)
    {
        Cancel(); notices.Clear(); notices.AddRange(papers);
        layout.Configure(papers, seed, reserveMain, smallSlots);
        AssignPostingStyles(seed);
        var toPost = pending == null ? notices.ToArray() : notices.Where(n => pending.Contains(n)).ToArray();
        foreach (var notice in notices) { notice.IsMoving = false; notice.SnapToBoard(); notice.SetPosted(!toPost.Contains(notice)); notice.SetPaperVisible(true); }
        posted = ready;
        // Back and Skip remain available during the entry fade. Every notice is
        // individually disabled until it has finished posting.
        board.alpha = fadeEntry && toPost.Length > 0 ? 0f : 1f; board.interactable = board.blocksRaycasts = true;
        if (toPost.Length == 0) { var completed = posted; posted = null; completed?.Invoke(); return; }
        IsPosting = true;
        posting = StartCoroutine(PostNotices(toPost, fadeEntry));
    }
    void AssignPostingStyles(int seed)
    {
        // A seeded shuffle bag covers all three styles before repeating. Keep bag
        // boundaries different too; this never consumes gameplay's Unity RNG.
        var random = new System.Random(seed ^ 0x51A7);
        var bag = new[] { QuestPostingStyle.ShortDrop, QuestPostingStyle.ZoomOutIn, QuestPostingStyle.PaintedReveal };
        QuestPostingStyle? previous = null;
        int index = bag.Length;
        foreach (var notice in PostingOrder())
        {
            if (index == bag.Length)
            {
                for (int i = bag.Length - 1; i > 0; i--)
                {
                    int other = random.Next(i + 1);
                    var saved = bag[i]; bag[i] = bag[other]; bag[other] = saved;
                }
                if (previous == bag[0]) { var saved = bag[0]; bag[0] = bag[1]; bag[1] = saved; }
                index = 0;
            }
            notice.SetPostingStyle(bag[index++]); previous = notice.PostingStyle;
        }
    }
    // World presentation owns stone arrivals; this is the first-entry HUD fade.
    public void BeginStoneBoard(Action ready, bool fadeEntry)
    {
        Cancel(); notices.Clear(); SetViews(PreMissionView.Board, null);
        if (!fadeEntry) { ready?.Invoke(); return; }
        board.alpha = 0f; posted = ready; IsPosting = true;
        posting = StartCoroutine(EnterStoneBoard());
    }
    IEnumerator EnterStoneBoard()
    {
        yield return null; yield return null;
        yield return Animate(boardEntryDuration, t => board.alpha = Smooth(t));
        posting = null; IsPosting = false;
        var ready = posted; posted = null; ready?.Invoke();
    }
    public void PresentStoneDetails(bool main, Action ready)
    {
        if (IsTransitioning) return;
        briefingPaper.GetComponent<Image>().color = main ? new Color(0.91f, 0.81f, 0.57f) : new Color(0.84f, 0.79f, 0.66f);
        IsTransitioning = true; StartCoroutine(StoneBriefing(true, ready));
    }
    public void PrepareStoneInspection() { SetGroup(board, true, false); HideTravel(); }
    public void SetStoneInspectionProgress(float progress) { board.alpha = 1f - Mathf.Clamp01(progress); }
    public void ReturnFromStoneDetails(Action ready)
    {
        if (IsTransitioning) return;
        IsTransitioning = true; StartCoroutine(StoneBriefing(false, ready));
    }
    IEnumerator StoneBriefing(bool entering, Action ready)
    {
        HideTravel(); RememberPoses();
        float boardStart = board.alpha;
        SetGroup(board, true, false); SetGroup(details, true, false); board.alpha = boardStart;
        if (entering) SetBriefingReveal(0f);
        yield return Animate(viewFadeDuration, t =>
        {
            float alpha = Smooth(t);
            board.alpha = entering ? boardStart*(1f-alpha) : 0f;
            details.alpha = entering ? alpha : 1f - alpha;
            if(tabletopBriefing) briefingPaper.anchoredPosition=briefingPosition+Vector2.right*(viewTravel*(entering?1-alpha:alpha));
        });
        if (entering) yield return RevealBriefing();
        IsTransitioning = false; ready();
    }
    IEnumerable<QuestNoticeView> PostingOrder() => notices.OrderBy(n => n.Quest.category == QuestCategory.Main ? 1 : 0);
    IEnumerator PostNotices(IReadOnlyCollection<QuestNoticeView> toPost, bool fadeEntry)
    {
        // Awake can run in a long scene-loading frame. Let the canvas paint before
        // starting the first paper, then animate it with the same steps as the rest.
        yield return null;
        yield return null;
        if (fadeEntry) yield return Animate(boardEntryDuration, t => board.alpha = Smooth(t));
        board.interactable = board.blocksRaycasts = true;
        // Stable ordering: every side notice settles before a main notice is posted.
        foreach (var notice in PostingOrder().Where(n => toPost.Contains(n)))
        {
            notice.IsMoving = true;
            notice.ResetHighlight();
            float emphasis = notice.Quest.category == QuestCategory.Main ? mainPostingTimeMultiplier : 1f;
            float duration = notice.PostingStyle == QuestPostingStyle.ShortDrop ? dropDuration
                : notice.PostingStyle == QuestPostingStyle.ZoomOutIn ? zoomDuration : paintedDuration;
            yield return Animate(duration * emphasis, t =>
            {
                switch (notice.PostingStyle)
                {
                    case QuestPostingStyle.ShortDrop:
                        float compression = Smooth(Mathf.InverseLerp(0.7f, 1f, t)) * impactCompression;
                        notice.SetPostingFrame(Vector2.up * Mathf.Lerp(DropHeight(notice), -settleDistance, Smooth(t)),
                            new Vector2(1f + compression, 1f - compression));
                        break;
                    case QuestPostingStyle.ZoomOutIn:
                        float scale = t < 0.65f ? Mathf.Lerp(zoomStartScale, zoomUndershootScale, Smooth(t / 0.65f))
                            : Mathf.Lerp(zoomUndershootScale, zoomReboundScale, Smooth((t - 0.65f) / 0.35f));
                        notice.SetPostingFrame(Vector2.zero, Vector2.one * scale);
                        break;
                    case QuestPostingStyle.PaintedReveal:
                        notice.SetPostingFrame(Vector2.zero, Vector2.one, Smooth(t), paintedEdgeSoftness);
                        break;
                }
                notice.Opacity = notice.PostingStyle == QuestPostingStyle.PaintedReveal ? 1f : Mathf.Clamp01(t * 8f);
            });
            yield return Animate(settleDuration * emphasis, t =>
            {
                float remaining = 1f - Smooth(t);
                if (notice.PostingStyle == QuestPostingStyle.ShortDrop)
                    notice.SetPostingFrame(Vector2.down * (settleDistance * remaining),
                        new Vector2(1f + impactCompression * remaining, 1f - impactCompression * remaining));
                else if (notice.PostingStyle == QuestPostingStyle.ZoomOutIn)
                    notice.SetPostingFrame(Vector2.zero, Vector2.one * Mathf.Lerp(1f, zoomReboundScale, remaining));
                else notice.SetPostingFrame(Vector2.zero, Vector2.one);
            });
            notice.IsMoving = false; notice.SnapToBoard(); notice.SetPosted(true); notice.SetPaperVisible(true);
            if (postingGap > 0) yield return new WaitForSecondsRealtime(postingGap);
        }
        IsPosting = false; posting = null;
        var ready = posted; posted = null; ready?.Invoke();
    }
    float DropHeight(QuestNoticeView notice)
    {
        float height = Mathf.Min(dropHeight, notice.BoardSize.y * 0.35f);
        var area = notice.Rect.parent as RectTransform;
        if (area == null) return height;
        float angle = notice.BoardAngle * Mathf.Deg2Rad;
        float top = notice.BoardPosition.y + (notice.BoardSize.y * Mathf.Abs(Mathf.Cos(angle))
            + notice.BoardSize.x * Mathf.Abs(Mathf.Sin(angle))) * 0.5f;
        // Keep top-row drops inside the offers area, clear of the board heading.
        return Mathf.Min(height, Mathf.Max(0f, area.rect.yMax - top));
    }
    public void FinishPosting()
    {
        if (!IsPosting) return;
        if (posting != null) StopCoroutine(posting);
        posting = null; IsPosting = false;
        board.alpha = 1f; board.interactable = board.blocksRaycasts = true;
        foreach (var notice in notices)
        {
            notice.IsMoving = false; notice.SnapToBoard(); notice.SetPosted(true); notice.SetPaperVisible(true);
        }
        var ready = posted; posted = null; ready?.Invoke();
    }
    public void SetViews(PreMissionView view, QuestDefinition selectedQuest)
    {
        ResetMotion(); SetBriefingReveal(1f);
        SetGroup(board, view == PreMissionView.Board);
        SetGroup(details, view == PreMissionView.Details);
        SetGroup(torso, view == PreMissionView.Torso);
        foreach (var notice in notices) notice.SetPaperVisible(view == PreMissionView.Board || notice.Quest != selectedQuest);
        HideTravel();
    }
    public void Settle(PreMissionView view, QuestDefinition selectedQuest)
    {
        Cancel();
        foreach (var notice in notices)
        {
            if (notice == null) continue;
            notice.IsMoving = false; notice.SnapToBoard(); notice.SetPosted(true); notice.ResetHighlight();
        }
        SetViews(view, selectedQuest);
    }
    public void PresentDetails(QuestNoticeView selected, Action ready)
    {
        if (IsTransitioning || selected == null) return;
        briefingPaper.GetComponent<Image>().color = selected.PaperColor;
        IsTransitioning = true;
        StartCoroutine(Pull(selected, ready));
    }
    IEnumerator Pull(QuestNoticeView selected, Action ready)
    {
        SetBriefingReveal(0f);
        SetGroup(board, true, false); SetGroup(details, true, false); details.alpha = 0f;
        shade.gameObject.SetActive(true); shade.alpha = 0f;
        Canvas.ForceUpdateCanvases();
        // Capture the visual, including its current hover lift/scale. The root,
        // parent and sibling order never leave the layout or need restoring.
        Vector2 grabOffset = selected.VisualRect.anchoredPosition;
        Vector2 grabScale = selected.VisualRect.localScale;
        float grabAngle = Mathf.DeltaAngle(selected.Rect.eulerAngles.z, selected.VisualRect.eulerAngles.z);
        ShowTravel(selected, GrabPose(selected, grabOffset, grabScale, grabAngle));
        handledNotice = selected; selected.IsMoving = true;
        selected.SetPaperVisible(false); selected.ResetHighlight();
        yield return AnimatePaper(clickFeedbackDuration, t =>
        {
            var start = GrabPose(selected, grabOffset, grabScale, grabAngle);
            travellingPaper.RefreshTravelReference(selected, start.size, grabScale.x);
            float scale = t < 0.45f ? Mathf.Lerp(1f - clickCompression * 0.35f, 1f - clickCompression, Smooth(t / 0.45f))
                : Mathf.Lerp(1f - clickCompression, 1f, Smooth((t - 0.45f) / 0.55f));
            var pressed = start; pressed.size *= scale; PlacePaper(pressed);
        });
        yield return AnimatePaper(pullDuration, t =>
        {
            var start = GrabPose(selected, grabOffset, grabScale, grabAngle);
            travellingPaper.RefreshTravelReference(selected, start.size, grabScale.x);
            PlacePaper(PickupPose(start, PoseOf(briefingPaper), t));
            float reveal = Smooth(Mathf.InverseLerp(0.88f, 1f, t));
            details.alpha = reveal; travellingPaper.Opacity = 1f - reveal;
            shade.alpha = Smooth(t) * 0.72f;
        });
        ReleaseNotice(); SetGroup(board, false); HideTravel(); details.alpha = 1f;
        yield return RevealBriefing();
        IsTransitioning = false; ready();
    }
    public void ReturnToBoard(QuestNoticeView selected, Action ready)
    {
        if (IsTransitioning || selected == null) return;
        IsTransitioning = true;
        StartCoroutine(Return(selected, ready));
    }
    IEnumerator Return(QuestNoticeView selected, Action ready)
    {
        SetGroup(board, true, false); SetGroup(details, true, false);
        handledNotice = selected; selected.IsMoving = true;
        selected.ResetHighlight(); selected.SetPaperVisible(false);
        shade.gameObject.SetActive(true); shade.alpha = 0.72f;
        Canvas.ForceUpdateCanvases();
        ShowTravel(selected, PoseOf(selected.Rect));
        PlacePaper(PoseOf(briefingPaper));
        yield return AnimatePaper(returnDuration, t =>
        {
            var home = PoseOf(selected.Rect);
            travellingPaper.RefreshTravelReference(selected, home.size);
            PlacePaper(PickupPose(home, PoseOf(briefingPaper), 1f - t));
            float reveal = Smooth(Mathf.Clamp01(t / 0.15f));
            details.alpha = 1f - reveal; travellingPaper.Opacity = reveal;
            shade.alpha = (1f - Smooth(t)) * 0.72f;
        });
        yield return AnimatePaper(returnSettleDuration, t =>
        {
            float impact = Mathf.Sin(t * Mathf.PI);
            var home = PoseOf(selected.Rect);
            travellingPaper.RefreshTravelReference(selected, home.size);
            home.position += Vector2.down * (returnSettleDistance * impact);
            home.size = Vector2.Scale(home.size, new Vector2(1f + returnCompression * impact, 1f - returnCompression * impact));
            PlacePaper(home);
        });
        ReleaseNotice(); selected.SetPaperVisible(true);
        IsTransitioning = false; HideTravel(); ready();
    }
    public void SwitchView(PreMissionView from, PreMissionView to, Action ready)
    {
        IsTransitioning = true;
        StartCoroutine(Fade(from, to, ready));
    }
    IEnumerator Fade(PreMissionView from, PreMissionView to, Action ready)
    {
        RememberPoses();
        var old = Group(from); var next = Group(to);
        SetGroup(old, true, false); SetGroup(next, true, false); next.alpha = 0f;
        if(tabletopBriefing && to==PreMissionView.Torso)
            yield return Animate(clickFeedbackDuration,t=>tabletopAccept.localScale=acceptScale*(1f-0.025f*Mathf.Sin(t*Mathf.PI)));
        float direction = to == PreMissionView.Torso ? 1f : -1f;
        var oldRect = (RectTransform)old.transform; var nextRect = (RectTransform)next.transform;
        var oldHome = from == PreMissionView.Details ? detailsPosition : torsoPosition;
        var nextHome = to == PreMissionView.Details ? detailsPosition : torsoPosition;
        yield return Animate(viewFadeDuration, t =>
        {
            float eased = Smooth(t); old.alpha = 1f - eased; next.alpha = eased;
            oldRect.anchoredPosition = oldHome + Vector2.left * (direction * viewTravel * eased);
            nextRect.anchoredPosition = nextHome + Vector2.right * (direction * viewTravel * (1f - eased));
        });
        ResetMotion();
        IsTransitioning = false; ready();
    }
    void RememberPoses()
    {
        if (rememberedPoses) return;
        detailsPosition = ((RectTransform)details.transform).anchoredPosition;
        briefingPosition=briefingPaper.anchoredPosition;
        torsoPosition = ((RectTransform)torso.transform).anchoredPosition;
        if (torsoChoice != null) { choicePosition = ((RectTransform)torsoChoice.transform).anchoredPosition; choiceScale = torsoChoice.transform.localScale; }
        if (confirmButton != null) confirmScale = confirmButton.localScale;
        rememberedPoses = true;
    }
    void ResetMotion()
    {
        RememberPoses();
        ((RectTransform)details.transform).anchoredPosition = detailsPosition;
        briefingPaper.anchoredPosition=briefingPosition;
        if(tabletopAccept!=null) tabletopAccept.localScale=acceptScale;
        if(tabletopDecline!=null) tabletopDecline.localScale=declineScale;
        ((RectTransform)torso.transform).anchoredPosition = torsoPosition;
        if (torsoChoice != null) { ((RectTransform)torsoChoice.transform).anchoredPosition = choicePosition; torsoChoice.transform.localScale = choiceScale; torsoChoice.alpha = 1f; }
        if (confirmButton != null) confirmButton.localScale = confirmScale;
    }
    void SetBriefingReveal(float alpha)
    {
        if (briefingGroups == null) return;
        foreach (var group in briefingGroups) if (group != null) group.alpha = alpha;
    }
    IEnumerator RevealBriefing()
    {
        int count = briefingGroups != null ? briefingGroups.Length : 0;
        if (count == 0) yield break;
        float fade = Mathf.Max(0.01f, briefingFadeDuration), stagger = Mathf.Max(0f, briefingStagger);
        float duration = fade + stagger * (count - 1);
        yield return Animate(duration, t =>
        {
            for (int i = 0; i < count; i++)
                if (briefingGroups[i] != null) briefingGroups[i].alpha = Smooth(Mathf.Clamp01((t * duration - i * stagger) / fade));
        });
    }
    public void BrowseTorso(int direction, Action change, Action ready)
    {
        if (IsTransitioning) return;
        IsTransitioning = true; StartCoroutine(Browse(direction, change, ready));
    }
    IEnumerator Browse(int direction, Action change, Action ready)
    {
        RememberPoses(); SetGroup(torso, true, false);
        var rect = (RectTransform)torsoChoice.transform;
        yield return Animate(torsoBrowseDuration * 0.5f, t =>
        {
            float eased = Smooth(t); torsoChoice.alpha = 1f - eased;
            rect.anchoredPosition = choicePosition + Vector2.left * (direction * torsoBrowseTravel * eased);
        });
        change();
        yield return Animate(torsoBrowseDuration * 0.5f, t =>
        {
            float eased = Smooth(t); torsoChoice.alpha = eased;
            rect.anchoredPosition = choicePosition + Vector2.right * (direction * torsoBrowseTravel * (1f - eased));
        });
        ResetMotion(); IsTransitioning = false; ready();
    }
    public void ConfirmTorso(Action ready)
    {
        if (IsTransitioning) return;
        IsTransitioning = true; StartCoroutine(Confirm(ready));
    }
    IEnumerator Confirm(Action ready)
    {
        RememberPoses(); SetGroup(torso, true, false);
        yield return Animate(confirmationDuration, t =>
        {
            float press = Mathf.Sin(Mathf.Clamp01(t / 0.35f) * Mathf.PI);
            confirmButton.localScale = confirmScale * (1f - 0.03f * press);
            float pulse = Mathf.Sin(t * Mathf.PI);
            torsoChoice.transform.localScale = choiceScale * (1f + confirmationPulse * pulse);
        });
        ResetMotion(); IsTransitioning = false; ready();
    }
    CanvasGroup Group(PreMissionView view) => view == PreMissionView.Board ? board : view == PreMissionView.Details ? details : torso;
    static void SetGroup(CanvasGroup group, bool active, bool interactive = true)
    {
        group.gameObject.SetActive(active); group.alpha = active ? 1f : 0f;
        group.interactable = group.blocksRaycasts = active && interactive;
    }
    void ReleaseNotice()
    {
        if (handledNotice != null) { handledNotice.IsMoving = false; handledNotice.SnapToBoard(); }
        handledNotice = null;
    }
    void ShowTravel(QuestNoticeView selected, PaperPose reference)
    {
        travellingPaper.Bind(selected.Quest, null);
        travellingPaper.IsMoving = true;
        travellingPaper.SetBoardPose(Vector2.zero, Vector2.one, 0, selected.Quest.category == QuestCategory.Main);
        travellingPaper.PrepareTravelFrom(selected, reference.size);
        travellingPaper.SetPosted(true); travellingPaper.SetPaperVisible(true); travellingPaper.ResetHighlight();
        PlacePaper(reference); travellingPaper.gameObject.SetActive(true);
    }
    void HideTravel()
    {
        if (travellingPaper != null) travellingPaper.gameObject.SetActive(false);
        if (shade != null) shade.gameObject.SetActive(false);
    }
    struct PaperPose { public Vector2 position, size; public float angle; }
    PaperPose PoseOf(RectTransform rect)
    {
        var parent = (RectTransform)travellingPaper.transform.parent;
        Vector3 position = parent.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
        return new PaperPose { position = position,
            size = new Vector2(rect.rect.width * rect.lossyScale.x / parent.lossyScale.x, rect.rect.height * rect.lossyScale.y / parent.lossyScale.y),
            angle = rect.eulerAngles.z - parent.eulerAngles.z };
    }
    PaperPose GrabPose(QuestNoticeView notice, Vector2 offset, Vector2 scale, float angle)
    {
        var pose = PoseOf(notice.Rect);
        // Preserve the captured hover relative to its layout root, so a Canvas
        // resize/refit cannot strand the proxy at old screen coordinates.
        pose.position = travellingPaper.transform.parent.InverseTransformPoint(notice.Rect.TransformPoint(notice.Rect.rect.center + offset));
        pose.size = Vector2.Scale(pose.size, scale); pose.angle += angle;
        return pose;
    }
    PaperPose PickupPose(PaperPose home, PaperPose foreground, float t)
    {
        float lift = Smooth(Mathf.Clamp01(t / 0.22f));
        float approach = Smooth(Mathf.InverseLerp(0.16f, 1f, t));
        return new PaperPose
        {
            position = Vector2.Lerp(home.position, foreground.position, approach) + Vector2.up * (pickupLift * lift * (1f - approach)),
            size = Vector2.Lerp(home.size * Mathf.Lerp(1f, pickupLiftScale, lift), foreground.size, approach),
            angle = Mathf.LerpAngle(home.angle, foreground.angle, Smooth(t))
        };
    }
    void PlacePaper(PaperPose pose)
    {
        var rect = travellingPaper.Rect;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = pose.position;
        rect.sizeDelta = pose.size;
        travellingPaper.ScaleTravelText(rect.sizeDelta);
        rect.localRotation = Quaternion.Euler(0, 0, pose.angle); rect.localScale = Vector3.one;
    }
    static float Smooth(float t) => Mathf.SmoothStep(0f, 1f, t);
    void LateUpdate() => RefreshPaperPose();
    // Reapply the current sample after a Canvas refit without advancing time.
    public void RefreshPaperPose() { if (IsTransitioning) paperFrame?.Invoke(); }
    IEnumerator AnimatePaper(float seconds, Action<float> frame)
    {
        float progress = 0f;
        paperFrame = () => frame(progress);
        yield return Animate(seconds, t => { progress = t; frame(t); });
        paperFrame = null;
    }
    static IEnumerator Animate(float seconds, Action<float> frame)
    {
        float age = 0f; seconds = Mathf.Max(0.01f, seconds);
        frame(0);
        while (age < seconds)
        {
            yield return null;
            // A loading/compilation hitch must not consume an entire short UI animation.
            age += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            frame(Mathf.Clamp01(age / seconds));
        }
    }
}
