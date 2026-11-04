using UnityEngine;
using UnityEngine.EventSystems;

// PhysicsRaycaster supplies pointer events to the actual animated stone buttons.
[DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
public sealed class PreMissionQuestSlabAction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerClickHandler, ISelectHandler, IDeselectHandler, ISubmitHandler
{
    PreMissionQuestDetailsSlab owner;
    BoxCollider hit;
    bool pointer, selected, highlighted;
    public bool IsHighlighted => highlighted;
    public void Bind(PreMissionQuestDetailsSlab value)
    { owner = value; hit = GetComponent<BoxCollider>(); SetAvailable(false); }
    public void SetAvailable(bool available)
    {
        if (hit == null) hit = GetComponent<BoxCollider>();
        hit.enabled = available;
        if (!available) ResetHover();
    }
    public void ResetHover() { pointer = selected = false; Apply(); }
    void Apply()
    {
        bool next = owner != null && owner.IsInteractive && (pointer || selected);
        if (next == highlighted) return;
        highlighted = next; if (owner != null) owner.Hover(this, next);
    }
    public void OnPointerEnter(PointerEventData data) { pointer = true; Apply(); }
    public void OnPointerExit(PointerEventData data) { pointer = false; Apply(); }
    public void OnSelect(BaseEventData data) { selected = true; Apply(); }
    public void OnDeselect(BaseEventData data) { selected = false; Apply(); }
    public void OnPointerClick(PointerEventData data)
    { if (data.button == PointerEventData.InputButton.Left && owner != null) owner.Invoke(this); }
    public void OnSubmit(BaseEventData data) { if (owner != null) owner.Invoke(this); }
}
