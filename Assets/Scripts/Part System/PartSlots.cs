using System.Collections.Generic;

// What a part is, with no side. Values line up with the matching PartCategory ints
// (Head, ArmL, Legs, ShoulderL, Torso) so existing serialized data maps across.
public enum PartType { Head = 0, Arm = 1, Legs = 3, Shoulder = 4, Torso = 6 }

// One starting part and the slot (side) it starts in. A part may be a scene/child
// instance or a prefab asset (instantiated on start), so a pre-level customiser can
// fill this list later.
[System.Serializable]
public struct LoadoutEntry
{
    public PartCategory slot;
    public Part part;
}

// Maps between side-free part types and the sided socket slots on the torso.
public static class PartSlots
{
    static readonly PartCategory[] head = { PartCategory.Head };
    static readonly PartCategory[] arms = { PartCategory.ArmL, PartCategory.ArmR };
    static readonly PartCategory[] legs = { PartCategory.Legs };
    static readonly PartCategory[] shoulders = { PartCategory.ShoulderL, PartCategory.ShoulderR };
    static readonly PartCategory[] torso = { PartCategory.Torso };

    public static PartType TypeOf(PartCategory slot) => slot switch
    {
        PartCategory.ArmL or PartCategory.ArmR => PartType.Arm,
        PartCategory.ShoulderL or PartCategory.ShoulderR => PartType.Shoulder,
        PartCategory.Head => PartType.Head,
        PartCategory.Legs => PartType.Legs,
        _ => PartType.Torso,
    };

    // Slots a part of this type can occupy, left before right.
    public static IReadOnlyList<PartCategory> SlotsFor(PartType type) => type switch
    {
        PartType.Arm => arms,
        PartType.Shoulder => shoulders,
        PartType.Head => head,
        PartType.Legs => legs,
        _ => torso,
    };

    public static PartCategory DefaultSlot(PartType type) => SlotsFor(type)[0];
    public static bool Fits(PartCategory slot, PartType type) => TypeOf(slot) == type;
}
