using UnityEngine;

// Two-legged walking. Its speed comes from the legs' Speed stat (metres per second), applied for every
// kind of legs by MechMovementController together with the other parts' Speed percentages.
public sealed class BipedLocomotionModule : LocomotionModule
{
    public override LocomotionType Type => LocomotionType.Biped;
    // Jump and horizontal dash are independent. The root motor still owns the cooldown.
    public override bool CanQuickBoost(bool grounded) => true;
}
