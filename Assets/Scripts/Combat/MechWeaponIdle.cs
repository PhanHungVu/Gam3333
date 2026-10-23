using System;
using UnityEngine;

// Presentation: after the mech stands still for a while, one randomly chosen equipped weapon plays
// its Neutral animation. One owner arbitrates all its weapons; a part never starts an idle by itself.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-120)]
[RequireComponent(typeof(EquipManager))]
public sealed class MechWeaponIdle : MonoBehaviour
{
    static readonly PartCategory[] Sockets = (PartCategory[])Enum.GetValues(typeof(PartCategory));
    [SerializeField, Min(0f)] float weaponIdleDelay = 3f;
    EquipManager equipment;
    MechMovementController movement;
    float weaponIdleTime;
    public MechWeaponAnimator IdleWeapon { get; private set; }

    void Awake() { equipment = GetComponent<EquipManager>(); movement = GetComponent<MechMovementController>(); }
    void OnEnable() { equipment.EquipmentChanged += ResetWeaponIdle; ResetWeaponIdle(); }
    void OnDisable() { equipment.EquipmentChanged -= ResetWeaponIdle; ResetWeaponIdle(); }

    void LateUpdate()
    {
        var state = movement != null ? movement.AnimationData : default;
        bool stationary = movement != null && movement.isActiveAndEnabled && state.IsGrounded
            && !state.IsBoosting && state.Speed < 0.05f && Mathf.Abs(state.VerticalSpeed) < 0.05f;
        TickWeaponIdle(Time.deltaTime, stationary);
    }

    public void ResetWeaponIdle() { weaponIdleTime = 0f; IdleWeapon = null; }

    public void TickWeaponIdle(float deltaTime, bool stationary)
    {
        if (!isActiveAndEnabled || !stationary) { ResetWeaponIdle(); return; }
        foreach (var socket in Sockets)
        {
            var part = equipment.GetEquipped(socket);
            var driver = part != null ? part.GetComponent<MechWeaponAnimator>() : null;
            if (driver != null && driver.CurrentPose != MechWeaponAnimator.PoseState.Neutral)
            { ResetWeaponIdle(); return; }
        }
        weaponIdleTime += Mathf.Max(0f, deltaTime);
        if (weaponIdleTime < weaponIdleDelay || IdleWeapon != null) return;
        int candidates = 0;
        foreach (var socket in Sockets)
        {
            var part = equipment.GetEquipped(socket);
            var driver = part != null ? part.GetComponent<MechWeaponAnimator>() : null;
            if (driver == null || !driver.CanAnimate) continue;
            // Reservoir sampling chooses uniformly without allocating a list.
            if (UnityEngine.Random.Range(0, ++candidates) == 0) IdleWeapon = driver;
        }
    }
}
