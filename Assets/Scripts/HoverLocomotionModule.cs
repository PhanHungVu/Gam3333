using UnityEngine;

public sealed class HoverLocomotionModule : LocomotionModule
{
    [SerializeField, Min(0f)] float moveSpeed = 12f;
    [SerializeField, Min(0f)] float boostSpeed = 18f;
    [SerializeField, Min(0f)] float acceleration = 20f;
    [SerializeField, Min(0f)] float deceleration = 6f;
    [SerializeField, Min(0.1f)] float clearance = 0.7f;
    [SerializeField, Min(0.1f)] float raisedClearance = 3f;
    [SerializeField, Min(0.1f)] float liftSpeed = 4f;
    [SerializeField, Min(0.1f)] float liftAcceleration = 20f;

    public override LocomotionType Type => LocomotionType.Hover;
    public override bool JumpCancelsAssaultBoost => false;
    public override float GroundProbeDistance => Mathf.Max(clearance, raisedClearance) + 5f;
    public override bool CanQuickBoost(bool grounded) => true;
    public override bool CanAssaultBoost(bool grounded) => true;

    public override MechLocomotionSettings Configure(MechLocomotionSettings defaults)
    {
        defaults.moveSpeed = Mathf.Max(0f, moveSpeed);
        defaults.sprintSpeed = Mathf.Max(0f, boostSpeed);
        defaults.acceleration = defaults.boostAcceleration = Mathf.Max(0f, acceleration);
        defaults.deceleration = Mathf.Max(0f, deceleration);
        return defaults;
    }

    public override float VerticalVelocity(MechLocomotionInput input, float velocity,
        bool grounded, float groundDistance, MechLocomotionSettings settings, float deltaTime)
    {
        // Surface following, not unlimited flight. Over a deep gap, gravity takes over.
        if (float.IsPositiveInfinity(groundDistance))
            return Mathf.Max(-20f, velocity + settings.gravity * deltaTime);
        float height = input.jumpHeld || input.jump ? Mathf.Max(clearance, raisedClearance) : clearance;
        float desired = Mathf.Clamp((height - groundDistance) * 5f, -liftSpeed, liftSpeed);
        return Mathf.MoveTowards(velocity, desired, Mathf.Max(0.1f, liftAcceleration) * deltaTime);
    }
}
