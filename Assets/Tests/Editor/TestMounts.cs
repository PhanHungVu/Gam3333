// Tests that loop over weapon types fire the socket each weapon occupies in the
// default loadout (cannon left shoulder, crossbow right shoulder, axe right arm).
public static class TestMounts
{
    public static PartCategory For(MechWeaponType weapon) => weapon switch
    {
        MechWeaponType.Cannon => PartCategory.ShoulderL,
        MechWeaponType.Crossbow => PartCategory.ShoulderR,
        _ => PartCategory.ArmR,
    };
}
