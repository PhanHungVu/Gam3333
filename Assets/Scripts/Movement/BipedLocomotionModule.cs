using UnityEngine;

// Two-legged walking. Its speed comes from the legs' Speed stat (metres per second), applied for every
// kind of legs by MechMovementController together with the other parts' Speed percentages.
public sealed class BipedLocomotionModule : LocomotionModule
{
    public override LocomotionType Type => LocomotionType.Biped;
}
