using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Uses native Button clicks so cancelled presses and slider changes stay silent.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(Button))]
public sealed class MainMenuButtonAudio : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    MainMenuAudio owner;
    Button button;
    bool hovered;
    bool suppressStationaryEntry;
    Vector2 initialPointerPosition;

    public void Bind(MainMenuAudio menu)
    {
        if (owner == menu) return;
        owner = menu;
        if (button == null)
        {
            button = GetComponent<Button>();
            button.onClick.AddListener(Clicked);
        }
        CaptureInitialPointer();
    }

    void OnEnable() => CaptureInitialPointer();
    void OnDisable() => hovered = false;

    void OnDestroy()
    {
        if (button != null) button.onClick.RemoveListener(Clicked);
    }

    void CaptureInitialPointer()
    {
        hovered = false;
        suppressStationaryEntry = false;
        var pointer = Pointer.current;
        if (pointer == null) return;
        initialPointerPosition = pointer.position.ReadValue();
        var canvas = GetComponentInParent<Canvas>();
        var camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        suppressStationaryEntry = RectTransformUtility.RectangleContainsScreenPoint(
            (RectTransform)transform, initialPointerPosition, camera);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (hovered) return;
        hovered = true;
        bool silentEntry = suppressStationaryEntry &&
            (eventData.position - initialPointerPosition).sqrMagnitude < 0.01f;
        suppressStationaryEntry = false;
        if (!silentEntry && button != null && button.IsActive() && button.IsInteractable() && owner != null)
            owner.PlayHover();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
        suppressStationaryEntry = false;
    }

    void Clicked()
    {
        if (button != null && button.IsActive() && button.IsInteractable() && owner != null)
            owner.PlayClick();
    }
}
