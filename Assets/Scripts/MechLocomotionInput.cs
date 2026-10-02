using UnityEngine;

// Device-independent commands. The motor reads input once; modules never read devices.
public struct MechLocomotionInput
{
    public Vector2 move;
    public bool sprint;
    public bool jump;
    public bool jumpHeld;
    public bool quickBoost;
    public bool assaultBoost;
}

public enum LocomotionType { Biped, Hover, Tank }

public struct MechLocomotionSettings
{
    public float moveSpeed, sprintSpeed, acceleration, deceleration, boostAcceleration;
    public float rotationSharpness, gravity, jumpHeight;
}
