using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using UnityEngine.UI;

public enum QuestPostingStyle { ShortDrop, ZoomOutIn, PaintedReveal }

[DisallowMultipleComponent, RequireComponent(typeof(CanvasGroup))]
public sealed class QuestNoticeView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler, IMoveHandler
{
    [SerializeField] Button button;
    [SerializeField] TMP_Text label;
    [SerializeField] RectTransform paperVisual;
    [SerializeField] Image paperImage;
    [SerializeField] Image accent;
    [SerializeField] Outline border;
    [SerializeField] CanvasGroup visibility;
    [SerializeField] RectMask2D postingReveal;
    [Header("Posted notice hover (unscaled)")]
    [SerializeField, Range(1f, 1.05f)] float hoverScale = 1.025f;
    [SerializeField, Min(0.01f)] float hoverDuration = 0.12f;
    [SerializeField, Range(0f, 8f)] float hoverLift = 3f;
    Action<QuestDefinition> selected;
    bool hovered, focused, paperVisible = true;
    bool refreshPointer;
    Vector2 travelReferenceSize;
    float travelReferenceFontSize;
    float hoverAmount, hoverFrom, hoverTarget, hoverAge;
    bool moving;
    readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
    public QuestDefinition Quest { get; private set; }
    public Button Button => button;
    public RectTransform Rect => (RectTransform)transform;
    public RectTransform VisualRect => paperVisual;
    public Vector2 BoardPosition { get; private set; }
    public Vector2 BoardSize { get; private set; }
    public float BoardAngle { get; private set; }
    public bool IsMoving
    {
        get => moving;
        set { moving = value; UpdateInteraction(); }
    }
    public bool IsPosted { get; private set; }
    public bool IsPaperVisible => paperVisible;
    public bool IsHighlighted => (hovered || focused) && button.IsInteractable();
    public Color PaperColor => paperImage.color;
    public float Opacity { get => visibility.alpha; set => visibility.alpha = value; }
    public float TextSize => label.fontSize;
    public QuestPostingStyle PostingStyle { get; private set; }
    public float RevealProgress { get; private set; } = 1f;

    public void Bind(QuestDefinition quest, Action<QuestDefinition> onSelected)
    {
        Quest = quest;
        selected = onSelected;
        label.enableAutoSizing = true;
        button.onClick.RemoveListener(Select);
        button.onClick.AddListener(Select);
        bool main = quest.category == QuestCategory.Main;
        paperImage.color = main ? new Color(0.91f, 0.81f, 0.57f) : new Color(0.84f, 0.79f, 0.66f);
        accent.color = main ? new Color(0.57f, 0.32f, 0.12f) : new Color(0.38f, 0.43f, 0.33f);
        border.effectColor = main ? new Color(0.57f, 0.32f, 0.12f, 0.8f) : new Color(0.24f, 0.2f, 0.14f, 0.45f);
        label.text = (main ? "MAIN QUEST" : "SIDE QUEST") + "\n" + quest.title + "\n\n"
            + PreMissionFlowController.MissionTypeName(quest.missionType) + "\n~" + quest.estimatedMinutes + " min   |   " + quest.gniReward + " Gni";
        SetPosted(false);
    }
    public void SetBoardPose(Vector2 position, Vector2 size, float angle, bool featured)
    {
        BoardPosition = position; BoardSize = size; BoardAngle = angle;
        label.fontSizeMax = featured ? 30f : 21f;
        label.fontSizeMin = featured ? 20f : 13f;
        if (!IsMoving) SnapToBoard();
        else
        {
            // Layout can refit during posting. Its root stays authoritative while
            // the temporary visual pose remains under the animation's control.
            ApplyBoardPose();
            if (postingReveal != null && RevealProgress < 1f) SetReveal(RevealProgress, postingReveal.softness.y);
        }
    }
    public void SnapToBoard()
    {
        ApplyBoardPose(); ResetHighlight(); SetReveal(1f, 0); refreshPointer = true;
    }
    void ApplyBoardPose()
    {
        Rect.anchorMin = Rect.anchorMax = Rect.pivot = new Vector2(0.5f, 0.5f);
        Rect.anchoredPosition = BoardPosition; Rect.sizeDelta = BoardSize;
        Rect.localRotation = Quaternion.Euler(0, 0, BoardAngle); Rect.localScale = Vector3.one;
    }
    public void SetPostingStyle(QuestPostingStyle style) => PostingStyle = style;
    public void SetPostingFrame(Vector2 offset, Vector2 scale, float reveal = 1f, int softness = 0)
    {
        // Leave the layout/hit root at its assigned pose. Hover and posting share
        // this visual child, but Update never overwrites it while it is moving.
        ApplyBoardPose();
        SetReveal(reveal, softness);
        paperVisual.anchoredPosition += offset;
        paperVisual.localScale = new Vector3(scale.x, scale.y, 1f);
    }
    void SetReveal(float progress, int softness)
    {
        RevealProgress = Mathf.Clamp01(progress);
        if (postingReveal == null) { paperVisual.anchoredPosition = Vector2.zero; return; }
        var clip = postingReveal.rectTransform;
        clip.anchorMin = new Vector2(0f, 1f - RevealProgress); clip.anchorMax = Vector2.one;
        clip.offsetMin = clip.offsetMax = Vector2.zero;
        // Compensate the changing clip height so paper, accent and TMP text stay
        // full-sized and stationary as the clipping edge moves down across them.
        paperVisual.offsetMin = new Vector2(0f, -Rect.rect.height * (1f - RevealProgress));
        paperVisual.offsetMax = Vector2.zero;
        postingReveal.softness = new Vector2Int(0, softness);
        postingReveal.enabled = RevealProgress < 1f;
    }
    public void SetPosted(bool posted)
    {
        if (posted && !IsPosted) refreshPointer = true;
        IsPosted = posted;
        if (!posted) { hovered = focused = false; visibility.alpha = 0f; }
        UpdateInteraction();
    }
    public void SetPaperVisible(bool visible)
    {
        if (visible && !paperVisible) refreshPointer = true;
        paperVisible = visible;
        visibility.alpha = visible && IsPosted ? 1f : 0f;
        if (!visible) hovered = focused = false;
        UpdateInteraction();
    }
    void UpdateInteraction()
    {
        button.interactable = IsPosted && paperVisible && !IsMoving;
        visibility.interactable = visibility.blocksRaycasts = button.interactable;
    }
    public void ResetHighlight()
    {
        hovered = focused = false;
        hoverAmount = hoverFrom = hoverTarget = hoverAge = 0f;
        paperVisual.localScale = Vector3.one; paperVisual.anchoredPosition = Vector2.zero;
    }
    void Update()
    {
        // Programmatic focus is a navigation target, not a permanent mouse hover.
        if (Mouse.current != null && (Mouse.current.delta.ReadValue().sqrMagnitude > 0f || Mouse.current.leftButton.wasPressedThisFrame)) focused = false;
        if (refreshPointer) { refreshPointer = false; RefreshPointerHover(); }
        // A locked board freezes the other papers while the proxy handles the
        // selected notice. Posting/pickup never compete with hover over this child.
        if (IsMoving || !button.IsInteractable()) return;
        float target = IsHighlighted ? 1f : 0f;
        if (target != hoverTarget) { hoverFrom = hoverAmount; hoverTarget = target; hoverAge = 0f; }
        hoverAge += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
        hoverAmount = Mathf.Lerp(hoverFrom, hoverTarget, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(hoverAge / Mathf.Max(0.01f, hoverDuration))));
        paperVisual.localScale = Vector3.one * Mathf.Lerp(1f, hoverScale, hoverAmount);
        paperVisual.anchoredPosition = Vector2.up * (hoverLift * hoverAmount);
    }
    public void OnPointerEnter(PointerEventData data) => hovered = true;
    public void OnPointerExit(PointerEventData data) => hovered = false;
    public void OnSelect(BaseEventData data) => focused = data is AxisEventData;
    public void OnDeselect(BaseEventData data) => focused = false;
    public void OnMove(AxisEventData data) => focused = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
    void OnEnable() => refreshPointer = true;
    void OnDisable() { if (paperVisual != null) { ResetHighlight(); SetReveal(1f, 0); } }
    void RefreshPointerHover()
    {
        if (Mouse.current == null || EventSystem.current == null || !button.IsInteractable() || IsMoving) return;
        var pointer = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
        pointerHits.Clear(); EventSystem.current.RaycastAll(pointer, pointerHits);
        hovered = pointerHits.Count > 0 && pointerHits[0].gameObject.GetComponentInParent<QuestNoticeView>() == this;
        pointerHits.Clear();
    }
    public void PrepareTravelFrom(QuestNoticeView source, Vector2 referenceSize)
    {
        StoreTravelReference(source, referenceSize, source.paperVisual.lossyScale.x / source.Rect.lossyScale.x);
    }
    public void RefreshTravelReference(QuestNoticeView source, Vector2 referenceSize, float visualScale = 1f)
    {
        if ((referenceSize - travelReferenceSize).sqrMagnitude > 0.001f) StoreTravelReference(source, referenceSize, visualScale);
    }
    void StoreTravelReference(QuestNoticeView source, Vector2 referenceSize, float visualScale)
    {
        source.label.ForceMeshUpdate(true);
        travelReferenceSize = referenceSize;
        // Match the actual printed size, including the source's current hover
        // scale, in the travelling paper parent's Canvas units.
        travelReferenceFontSize = source.label.fontSize * source.Rect.lossyScale.x / Rect.parent.lossyScale.x * visualScale;
        label.enableAutoSizing = false;
        label.fontSize = travelReferenceFontSize;
    }
    public void ScaleTravelText(Vector2 paperSize)
    {
        if (travelReferenceSize.x <= 0f || travelReferenceSize.y <= 0f) return;
        // Scale type with the paper instead of letting TMP keep its board-sized font cap.
        label.fontSize = travelReferenceFontSize * Mathf.Min(paperSize.x / travelReferenceSize.x, paperSize.y / travelReferenceSize.y);
    }
    void Select() { if (button.interactable) selected?.Invoke(Quest); }
    void OnDestroy() { if (button != null) button.onClick.RemoveListener(Select); }
}
