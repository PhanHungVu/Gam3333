using UnityEngine;

public sealed class TankLocomotionModule : LocomotionModule
{
    [SerializeField, Min(0f)] float moveSpeed = 4.5f;
    [SerializeField, Min(0f)] float boostSpeed = 8f;
    [SerializeField, Min(0f)] float acceleration = 15f;
    [SerializeField, Min(0f)] float deceleration = 18f;

    public override LocomotionType Type => LocomotionType.Tank;
    public override bool SupportsJump => false;
    public override Vector3 QuickBoostDirection(Vector3 requested, Vector3 forward) => forward;

    public override MechLocomotionSettings Configure(MechLocomotionSettings defaults)
    {
        defaults.moveSpeed = Mathf.Max(0f, moveSpeed);
        defaults.sprintSpeed = Mathf.Max(0f, boostSpeed);
        defaults.acceleration = defaults.boostAcceleration = Mathf.Max(0f, acceleration);
        defaults.deceleration = Mathf.Max(0f, deceleration);
        return defaults;
    }
}
