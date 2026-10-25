using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Observe real coroutine frames, rendering and interruption in the disposable Play Mode fixture.
public sealed class QuestPostingChecks
{
    readonly PreMissionFlowController flow;
    readonly PreMissionPaperPresentation presentation;
    readonly Action<bool, string> check;
    readonly Action<string> capture;
    readonly Dictionary<QuestPostingStyle, int> mainSeeds = new Dictionary<QuestPostingStyle, int>();
    readonly Dictionary<QuestNoticeView, Samples> samples = new Dictionary<QuestNoticeView, Samples>();
    readonly HashSet<QuestPostingStyle> captures = new HashSet<QuestPostingStyle>();
    Dictionary<QuestNoticeView, QuestPostingStyle> styles;
    Dictionary<QuestNoticeView, Vector3> homes;
    QuestNoticeView selected;
    QuestSelectionSession session;
    int phase, run, readyCalls;
    sealed class Samples
    {
        public int frames;
        public float minY = float.MaxValue, maxY = float.MinValue;
        public float minScale = float.MaxValue, maxScale = float.MinValue;
        public float minReveal = 1f, maxReveal;
    }
    public QuestPostingChecks(PreMissionFlowController flow, Action<bool, string> check, Action<string> capture)
    {
        this.flow = flow; this.check = check; this.capture = capture;
        flow.gameObject.SetActive(true); flow.CreateFreshBoard();
        presentation = (PreMissionPaperPresentation)new SerializedObject(flow).FindProperty("presentation").objectReferenceValue;
        presentation.FinishPosting(); session = flow.Session;
        for (int seed = 0; seed < 30; seed++)
        {
            presentation.BeginBoard(flow.Notices, seed, null);
            var order = flow.Notices.OrderBy(n => n.Quest.category == QuestCategory.Main ? 1 : 0).ToArray();
            check(order.Select(n => n.PostingStyle).Distinct().Count() == 3
                && !order.Where((n, i) => i > 0 && n.PostingStyle == order[i - 1].PostingStyle).Any(),
                "Seed " + seed + " covers all posting variations without consecutive repeats");
            mainSeeds[order.Last().PostingStyle] = seed;
            var original = order.Select(n => n.PostingStyle).ToArray();
            presentation.FinishPosting(); presentation.BeginBoard(flow.Notices, seed, null);
            check(order.Select(n => n.PostingStyle).SequenceEqual(original), "Posting assignment is deterministic for board seed " + seed);
            presentation.FinishPosting();
        }
        check(mainSeeds.Count == 3, "Main quests can receive any of the three entrance styles");
        StartRun();
    }
    static RectTransform Visual(QuestNoticeView n) => (RectTransform)n.transform.Find("Posting Reveal/Paper Visual");
    static RectMask2D Mask(QuestNoticeView n) => n.GetComponentInChildren<RectMask2D>(true);
    void StartRun()
    {
        samples.Clear();
        presentation.BeginBoard(flow.Notices, mainSeeds[(QuestPostingStyle)run], null);
        foreach (var n in flow.Notices) samples[n] = new Samples();
    }
    void Observe()
    {
        foreach (var n in flow.Notices.Where(n => n.IsMoving))
        {
            var visual = Visual(n); var s = samples[n]; s.frames++;
            s.minY = Mathf.Min(s.minY, visual.anchoredPosition.y); s.maxY = Mathf.Max(s.maxY, visual.anchoredPosition.y);
            s.minScale = Mathf.Min(s.minScale, visual.localScale.y); s.maxScale = Mathf.Max(s.maxScale, visual.localScale.y);
            s.minReveal = Mathf.Min(s.minReveal, n.RevealProgress); s.maxReveal = Mathf.Max(s.maxReveal, n.RevealProgress);
            if (Vector2.Distance(n.Rect.anchoredPosition, n.BoardPosition) > 0.01f || n.Rect.localScale != Vector3.one
                || Mathf.Abs(Mathf.DeltaAngle(n.Rect.localEulerAngles.z, n.BoardAngle)) > 0.01f || n.Button.interactable)
                throw new Exception("Posting disturbed its layout root or enabled an unfinished notice");
            if (n.PostingStyle != QuestPostingStyle.PaintedReveal && Mathf.Abs(visual.anchoredPosition.x) > 0.01f)
                throw new Exception("Posting introduced diagonal travel");
            if (n.PostingStyle == QuestPostingStyle.ShortDrop
                && RectTransformUtility.CalculateRelativeRectTransformBounds(n.Rect.parent, visual).max.y
                    > ((RectTransform)n.Rect.parent).rect.yMax + 0.5f)
                throw new Exception("Short drop crossed above the offers area into the board heading");
            if (n.PostingStyle == QuestPostingStyle.ZoomOutIn && visual.anchoredPosition.sqrMagnitude > 0.001f)
                throw new Exception("Zoom posting moved away from its assigned center");
            if (n.PostingStyle == QuestPostingStyle.PaintedReveal)
            {
                var center = n.Rect.InverseTransformPoint(visual.TransformPoint(visual.rect.center));
                if (center.sqrMagnitude > 0.01f || Mathf.Abs(visual.rect.height - n.BoardSize.y) > 0.1f || visual.localScale != Vector3.one)
                    throw new Exception("Painted reveal moved or resized its content");
            }
            bool captureNow = n.PostingStyle == QuestPostingStyle.ShortDrop ? visual.anchoredPosition.y > 2 && n.Opacity > 0.5f
                : n.PostingStyle == QuestPostingStyle.ZoomOutIn ? visual.localScale.y > 1.04f && n.Opacity > 0.5f
                : n.RevealProgress > 0.3f && n.RevealProgress < 0.65f;
            if (captureNow && captures.Add(n.PostingStyle)) capture("q51-entrance-" + n.PostingStyle);
        }
    }
    void CheckSettled(string context)
    {
        check(flow.Notices.All(n => n.IsPosted && !n.IsMoving && n.IsPaperVisible && n.Button.interactable
            && n.RevealProgress == 1f && !Mask(n).enabled && Visual(n).localScale == Vector3.one
            && Visual(n).anchoredPosition.sqrMagnitude < 0.001f && n.Rect.localScale == Vector3.one
            && Vector2.Distance(n.Rect.anchoredPosition, n.BoardPosition) < 0.01f), context);
    }
    void CheckPaintedPixels()
    {
        var notice = flow.Notices.First(n => n.PostingStyle == QuestPostingStyle.PaintedReveal);
        notice.IsMoving = true; notice.SetPostingFrame(Vector2.zero, Vector2.one, 0.5f, 6);
        capture("q51-painted-half");
        var image = new Texture2D(2, 2);
        try
        {
            image.LoadImage(File.ReadAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "PreMissionCaptures", "q51-painted-half.png")));
            Color Sample(float height)
            {
                var point = notice.Rect.TransformPoint(new Vector2(notice.Rect.rect.xMin + notice.BoardSize.x * 0.12f,
                    notice.Rect.rect.yMin + notice.BoardSize.y * height));
                var screen = RectTransformUtility.WorldToScreenPoint(null, point);
                return image.GetPixel(Mathf.RoundToInt(screen.x), Mathf.RoundToInt(screen.y));
            }
            var top = Sample(0.8f); var bottom = Sample(0.2f);
            check(top.grayscale > bottom.grayscale + 0.25f, "Painted reveal renders the top paper while the lower section remains invisible");
        }
        finally { Object.Destroy(image); notice.IsMoving = false; notice.SnapToBoard(); }
    }
    public bool Step()
    {
        if (flow.IsTransitioning) return false;
        switch (phase)
        {
            case 0:
                Observe(); if (flow.IsPosting) return false;
                foreach (var entry in samples)
                {
                    var s = entry.Value; var style = entry.Key.PostingStyle;
                    check(s.frames >= 3 && (style == QuestPostingStyle.ShortDrop ? s.maxY > 2f && s.minY < -0.1f && s.minScale < 0.99f
                        : style == QuestPostingStyle.ZoomOutIn ? s.maxScale > 1.04f && s.minScale < 0.99f
                        : s.maxReveal - s.minReveal > 0.5f), "Observed complete " + style + " motion on " + entry.Key.Quest.category + " notice");
                }
                CheckSettled("Every entrance restores its exact layout/visual pose, removes clipping and enables selection");
                if (++run < 3) { StartRun(); return false; }
                check(captures.Count == 3, "All three entrance variations were rendered during sequential posting");
                CheckPaintedPixels();
                readyCalls = 0; presentation.BeginBoard(flow.Notices, mainSeeds[QuestPostingStyle.PaintedReveal], () => readyCalls++);
                break;
            case 1:
                if (!flow.Notices.Any(n => n.IsMoving && n.PostingStyle == QuestPostingStyle.PaintedReveal && n.RevealProgress > 0.2f && n.RevealProgress < 0.8f)) return false;
                presentation.FinishPosting(); presentation.FinishPosting();
                CheckSettled("Finishing a partially painted intro restores every notice and removes all masks");
                check(readyCalls == 1, "Finishing the intro repeatedly invokes its completion callback only once");
                styles = flow.Notices.ToDictionary(n => n, n => n.PostingStyle);
                homes = flow.Notices.ToDictionary(n => n, n => new Vector3(n.BoardPosition.x, n.BoardPosition.y, n.BoardAngle));
                selected = flow.Notices.First(n => n.PostingStyle == QuestPostingStyle.PaintedReveal);
                flow.SelectQuest(selected.Quest); break;
            case 2:
                flow.Back(); break;
            case 3:
                check(flow.Session == session && flow.Notices.All(n => styles[n] == n.PostingStyle
                    && Vector3.Distance(homes[n], new Vector3(n.BoardPosition.x, n.BoardPosition.y, n.BoardAngle)) < 0.01f)
                    && !flow.IsPosting, "Declining preserves the original posting styles, layout and session without replaying");
                selected.ResetHighlight();
                presentation.BeginBoard(flow.Notices, mainSeeds[QuestPostingStyle.PaintedReveal], null); break;
            case 4:
                if (!flow.Notices.Any(n => n.IsMoving && n.PostingStyle == QuestPostingStyle.PaintedReveal && n.RevealProgress > 0.2f && n.RevealProgress < 0.8f)) return false;
                flow.gameObject.SetActive(false); flow.gameObject.SetActive(true);
                CheckSettled("Disabling/re-enabling mid-reveal clears clipping and restores all notice controls");
                check(!flow.IsPosting && !flow.IsTransitioning && flow.Session == session, "Interrupted painted posting leaves the original session ready without replaying");
                return true;
        }
        phase++; return false;
    }
}
