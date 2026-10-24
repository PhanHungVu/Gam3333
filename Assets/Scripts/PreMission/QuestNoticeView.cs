using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using UnityEngine.UI;

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
    [SerializeField, Range(1f, 1.05f)] float hoverScale = 1.025f;
    Action<QuestDefinition> selected;
    bool hovered, focused, paperVisible = true;
    bool refreshPointer;
    Vector2 travelReferenceSize;
    float travelReferenceFontSize;
    readonly List<RaycastResult> pointerHits = new List<RaycastResult>();
    public QuestDefinition Quest { get; private set; }
    public Button Button => button;
    public RectTransform Rect => (RectTransform)transform;
    public Vector2 BoardPosition { get; private set; }
    public Vector2 BoardSize { get; private set; }
    public float BoardAngle { get; private set; }
    public bool IsMoving { get; set; }
    public bool IsPosted { get; private set; }
    public bool IsPaperVisible => paperVisible;
    public bool IsHighlighted => (hovered || focused) && button.interactable;
    public Color PaperColor => paperImage.color;
    public float Opacity { get => visibility.alpha; set => visibility.alpha = value; }
    public float TextSize => label.fontSize;

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
    }
    public void SnapToBoard()
    {
        Rect.anchorMin = Rect.anchorMax = Rect.pivot = new Vector2(0.5f, 0.5f);
        Rect.anchoredPosition = BoardPosition; Rect.sizeDelta = BoardSize;
        Rect.localRotation = Quaternion.Euler(0, 0, BoardAngle); Rect.localScale = Vector3.one;
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
        paperVisual.localScale = Vector3.one; paperVisual.anchoredPosition = Vector2.zero;
    }
    void Update()
    {
        // Programmatic focus is a navigation target, not a permanent mouse hover.
        if (Mouse.current != null && (Mouse.current.delta.ReadValue().sqrMagnitude > 0f || Mouse.current.leftButton.wasPressedThisFrame)) focused = false;
        if (refreshPointer) { refreshPointer = false; RefreshPointerHover(); }
        float blend = 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime);
        bool raised = IsHighlighted && !IsMoving;
        paperVisual.localScale = Vector3.Lerp(paperVisual.localScale, Vector3.one * (raised ? hoverScale : 1f), blend);
        paperVisual.anchoredPosition = Vector2.Lerp(paperVisual.anchoredPosition, raised ? new Vector2(0, 3f) : Vector2.zero, blend);
    }
    public void OnPointerEnter(PointerEventData data) => hovered = true;
    public void OnPointerExit(PointerEventData data) => hovered = false;
    public void OnSelect(BaseEventData data) => focused = data is AxisEventData;
    public void OnDeselect(BaseEventData data) => focused = false;
    public void OnMove(AxisEventData data) => focused = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
    void OnEnable() => refreshPointer = true;
    void OnDisable() { if (paperVisual != null) ResetHighlight(); }
    void RefreshPointerHover()
    {
        if (Mouse.current == null || EventSystem.current == null || !button.interactable || IsMoving) return;
        var pointer = new PointerEventData(EventSystem.current) { position = Mouse.current.position.ReadValue() };
        pointerHits.Clear(); EventSystem.current.RaycastAll(pointer, pointerHits);
        hovered = pointerHits.Count > 0 && pointerHits[0].gameObject.GetComponentInParent<QuestNoticeView>() == this;
        pointerHits.Clear();
    }
    public void PrepareTravelFrom(QuestNoticeView source, Vector2 referenceSize)
    {
        source.label.ForceMeshUpdate(true);
        travelReferenceSize = referenceSize;
        travelReferenceFontSize = source.label.fontSize;
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
