using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

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
    [Header("Unscaled animation timing")]
    [SerializeField, Min(0.01f)] float postingDuration = 0.25f;
    [SerializeField, Min(0f)] float postingGap = 0.07f;
    [SerializeField, Min(0.01f)] float pullDuration = 0.42f;
    [SerializeField, Min(0.01f)] float returnDuration = 0.36f;
    [SerializeField, Min(0.01f)] float viewFadeDuration = 0.16f;
    [SerializeField] Vector2 postingOffset = new Vector2(-25f, 120f);
    [SerializeField, Min(0f)] float settleDistance = 7f;
    readonly List<QuestNoticeView> notices = new List<QuestNoticeView>();
    Coroutine posting;
    Action posted;
    public bool IsPosting { get; private set; }
    public bool IsTransitioning { get; private set; }

    public void Cancel()
    {
        StopAllCoroutines(); posting = null; posted = null;
        IsPosting = IsTransitioning = false;
        HideTravel();
    }
    public void BeginBoard(IReadOnlyList<QuestNoticeView> papers, int seed, Action ready)
    {
        Cancel(); notices.Clear(); notices.AddRange(papers);
        layout.Configure(papers, seed);
        foreach (var notice in notices) { notice.IsMoving = false; notice.SnapToBoard(); notice.SetPosted(false); notice.SetPaperVisible(true); }
        posted = ready;
        IsPosting = true;
        posting = StartCoroutine(PostNotices());
    }
    IEnumerator PostNotices()
    {
        // Awake can run in a long scene-loading frame. Let the canvas paint before
        // starting the first paper, then animate it with the same steps as the rest.
        yield return null;
        yield return null;
        // Stable ordering: every side notice settles before a main notice is posted.
        foreach (var notice in notices.OrderBy(n => n.Quest.category == QuestCategory.Main ? 1 : 0))
        {
            notice.IsMoving = true;
            yield return Animate(postingDuration, t =>
            {
                float eased = Ease(t);
                notice.Rect.anchoredPosition = notice.BoardPosition + postingOffset * (1f - eased)
                    + Vector2.up * (Mathf.Sin(t * Mathf.PI * 2f) * (1f - t) * settleDistance);
                notice.Rect.localRotation = Quaternion.Euler(0, 0, notice.BoardAngle + (1f - eased) * 7f);
                notice.Rect.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, eased);
                notice.Opacity = Mathf.Clamp01(t * 4f);
            });
            notice.IsMoving = false; notice.SnapToBoard(); notice.SetPosted(true); notice.SetPaperVisible(true);
            if (postingGap > 0) yield return new WaitForSecondsRealtime(postingGap);
        }
        IsPosting = false; posting = null;
        var ready = posted; posted = null; ready?.Invoke();
    }
    public void FinishPosting()
    {
        if (!IsPosting) return;
        if (posting != null) StopCoroutine(posting);
        posting = null; IsPosting = false;
        foreach (var notice in notices)
        {
            notice.IsMoving = false; notice.SnapToBoard(); notice.SetPosted(true); notice.SetPaperVisible(true);
        }
        var ready = posted; posted = null; ready?.Invoke();
    }
    public void SetViews(PreMissionView view, QuestDefinition selectedQuest)
    {
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
        briefingPaper.GetComponent<Image>().color = selected.PaperColor;
        IsTransitioning = true;
        StartCoroutine(Pull(selected, ready));
    }
    IEnumerator Pull(QuestNoticeView selected, Action ready)
    {
        SetGroup(board, true, false); SetGroup(details, true, false); details.alpha = 0f;
        selected.SetPaperVisible(false);
        shade.gameObject.SetActive(true); shade.alpha = 0f;
        Canvas.ForceUpdateCanvases();
        ShowTravel(selected);
        var start = PoseOf(selected.Rect);
        yield return Animate(pullDuration, t =>
        {
            MovePaper(start, PoseOf(briefingPaper), Ease(t));
            float reveal = Mathf.InverseLerp(0.65f, 1f, t);
            details.alpha = reveal; travellingPaper.Opacity = 1f - reveal;
            shade.alpha = Ease(t) * 0.72f;
        });
        IsTransitioning = false; HideTravel(); ready();
    }
    public void ReturnToBoard(QuestNoticeView selected, Action ready)
    {
        IsTransitioning = true;
        StartCoroutine(Return(selected, ready));
    }
    IEnumerator Return(QuestNoticeView selected, Action ready)
    {
        SetGroup(board, true, false); SetGroup(details, true, false);
        selected.SetPaperVisible(false);
        shade.gameObject.SetActive(true); shade.alpha = 0.72f;
        Canvas.ForceUpdateCanvases();
        ShowTravel(selected);
        var start = PoseOf(briefingPaper);
        yield return Animate(returnDuration, t =>
        {
            MovePaper(start, PoseOf(selected.Rect), Ease(t));
            float reveal = Mathf.Clamp01(t * 5f);
            details.alpha = 1f - reveal; travellingPaper.Opacity = reveal;
            shade.alpha = (1f - Ease(t)) * 0.72f;
        });
        selected.ResetHighlight(); selected.SetPaperVisible(true);
        IsTransitioning = false; HideTravel(); ready();
    }
    public void SwitchView(PreMissionView from, PreMissionView to, Action ready)
    {
        IsTransitioning = true;
        StartCoroutine(Fade(from, to, ready));
    }
    IEnumerator Fade(PreMissionView from, PreMissionView to, Action ready)
    {
        var old = Group(from); var next = Group(to);
        SetGroup(old, true, false); SetGroup(next, true, false); next.alpha = 0f;
        yield return Animate(viewFadeDuration, t => { old.alpha = 1f - t; next.alpha = t; });
        IsTransitioning = false; ready();
    }
    CanvasGroup Group(PreMissionView view) => view == PreMissionView.Board ? board : view == PreMissionView.Details ? details : torso;
    static void SetGroup(CanvasGroup group, bool active, bool interactive = true)
    {
        group.gameObject.SetActive(active); group.alpha = active ? 1f : 0f;
        group.interactable = group.blocksRaycasts = active && interactive;
    }
    void ShowTravel(QuestNoticeView selected)
    {
        travellingPaper.Bind(selected.Quest, null);
        travellingPaper.IsMoving = true;
        travellingPaper.SetBoardPose(Vector2.zero, Vector2.one, 0, selected.Quest.category == QuestCategory.Main);
        travellingPaper.PrepareTravelFrom(selected, PoseOf(selected.Rect).size);
        travellingPaper.SetPosted(true); travellingPaper.SetPaperVisible(true); travellingPaper.ResetHighlight();
        travellingPaper.gameObject.SetActive(true);
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
    void MovePaper(PaperPose from, PaperPose to, float t)
    {
        var rect = travellingPaper.Rect;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.Lerp(from.position, to.position, t);
        rect.sizeDelta = Vector2.Lerp(from.size, to.size, t);
        travellingPaper.ScaleTravelText(rect.sizeDelta);
        rect.localRotation = Quaternion.Euler(0, 0, Mathf.LerpAngle(from.angle, to.angle, t)); rect.localScale = Vector3.one;
    }
    static float Ease(float t) => 1f - Mathf.Pow(1f - t, 3f);
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
