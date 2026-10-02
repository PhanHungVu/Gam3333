using UnityEngine;

// EquipManager remains the sole owner of part attachment and physics.
[RequireComponent(typeof(EquipManager), typeof(MechMovementController))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-110)]
public sealed class MechEquipmentLocomotion : MonoBehaviour
{
    EquipManager equipment;
    MechMovementController movement;
    MechLegAnimator legAnimator;
    void Awake() { equipment = GetComponent<EquipManager>(); movement = GetComponent<MechMovementController>(); }
    void OnEnable() { equipment.EquipmentChanged += Refresh; Refresh(); }
    void OnDisable()
    {
        equipment.EquipmentChanged -= Refresh;
        if (legAnimator != null) legAnimator.Bind(null);
        legAnimator = null;
        movement.SetLocomotionModule(null);
    }
    void Refresh()
    {
        var legs = equipment.GetEquipped(PartCategory.Legs);
        var nextAnimator = legs != null ? legs.GetComponent<MechLegAnimator>() : null;
        float phase = legAnimator != null ? legAnimator.NormalizedPhase : 0f;
        if (legAnimator != nextAnimator && legAnimator != null) legAnimator.Bind(null);
        movement.SetLocomotionModule(legs != null ? legs.GetComponent<LocomotionModule>() : null);
        legAnimator = nextAnimator;
        if (legAnimator != null) legAnimator.Bind(movement, phase);
    }
}
