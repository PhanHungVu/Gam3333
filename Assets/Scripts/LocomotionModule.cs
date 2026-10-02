using UnityEngine;

// A strategy on an ordinary Legs Part. Only the root motor moves the character.
[RequireComponent(typeof(Part))]
[DisallowMultipleComponent]
public abstract class LocomotionModule : MonoBehaviour
{
    public abstract LocomotionType Type { get; }
    public virtual bool SupportsJump => true;
    public virtual bool JumpCancelsAssaultBoost => SupportsJump;
    public virtual float GroundProbeDistance => 0f;
    public virtual bool CanQuickBoost(bool grounded) => grounded;
    public virtual bool CanAssaultBoost(bool grounded) => grounded;
    public virtual Vector3 QuickBoostDirection(Vector3 requested, Vector3 forward) => requested;
    public virtual MechLocomotionSettings Configure(MechLocomotionSettings defaults) => defaults;

    public virtual float VerticalVelocity(MechLocomotionInput input, float velocity,
        bool grounded, float groundDistance, MechLocomotionSettings settings, float deltaTime)
        => GroundVerticalVelocity(input.jump && SupportsJump, velocity, grounded, settings, deltaTime);

    public virtual Vector3 PlanarVelocity(Vector3 velocity, Vector3 direction, bool sprint,
        MechLocomotionSettings settings, float deltaTime)
        => GroundPlanarVelocity(velocity, direction, sprint, settings, deltaTime);

    public static Vector3 GroundPlanarVelocity(Vector3 velocity, Vector3 direction, bool sprint,
        MechLocomotionSettings settings, float deltaTime)
    {
        Vector3 desired = direction * (sprint ? settings.sprintSpeed : settings.moveSpeed);
        float rate = desired.sqrMagnitude > velocity.sqrMagnitude
            ? (sprint ? settings.boostAcceleration : settings.acceleration) : settings.deceleration;
        return Vector3.MoveTowards(velocity, desired, rate * deltaTime);
    }

    public static float GroundVerticalVelocity(bool jump, float velocity, bool grounded,
        MechLocomotionSettings settings, float deltaTime)
    {
        if (!grounded) return velocity + settings.gravity * deltaTime;
        if (velocity < 0f) velocity = -2f;
        return jump ? Mathf.Sqrt(settings.jumpHeight * -2f * settings.gravity) : velocity;
    }
}
