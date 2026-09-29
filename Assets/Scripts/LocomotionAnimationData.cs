using UnityEngine;

// Immutable snapshot of resolved motion, independent of skeletons and Animator parameters.
public readonly struct LocomotionAnimationData
{
    public readonly LocomotionType LocomotionType;
    public readonly MechMovementController.MovementState State;
    // Metres/second; forward/right are relative to the gameplay root, never the visual rig.
    public readonly float Speed, ForwardSpeed, SideSpeed, VerticalSpeed;
    public readonly bool IsGrounded, IsBoosting, IsTurning;
    // Signed degrees/second around world up, positive when turning right.
    public readonly float TurnRate;
    // Normalized world direction: selected target in either lock mode, otherwise camera aim.
    public readonly Vector3 AimDirection;

    public LocomotionAnimationData(LocomotionType type, MechMovementController.MovementState state,
        Vector3 velocity, Quaternion rootRotation, bool grounded, bool boosting,
        float turnRate, Vector3 aimDirection)
    {
        LocomotionType = type;
        State = state;
        Vector3 local = Quaternion.Inverse(rootRotation) * velocity;
        Speed = new Vector2(velocity.x, velocity.z).magnitude;
        ForwardSpeed = local.z;
        SideSpeed = local.x;
        VerticalSpeed = velocity.y;
        IsGrounded = grounded;
        IsBoosting = boosting;
        TurnRate = turnRate;
        IsTurning = Mathf.Abs(turnRate) > 1f;
        AimDirection = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : rootRotation * Vector3.forward;
    }
}
