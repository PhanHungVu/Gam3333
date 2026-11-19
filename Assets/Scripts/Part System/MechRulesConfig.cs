using UnityEngine;

[CreateAssetMenu(fileName = "MechRulesConfig", menuName = "Mech/Rules Config")]
public class MechRulesConfig : ScriptableObject
{
    public bool allowMixedTypes = false;
    [Tooltip("Metres in one golem length. Part Range stats are measured in golem lengths.")]
    [Min(0.01f)] public float metresPerGolem = 4f;

    public bool CanEquip(Socket socket, Part part)
    {
        // Mixed body-part testing must never cross the Arm/Shoulder weapon families.
        var socketType = PartSlots.TypeOf(socket.socketType);
        if (part.partType == PartType.Arm && socketType == PartType.Shoulder
            || part.partType == PartType.Shoulder && socketType == PartType.Arm) return false;
        return (allowMixedTypes && (part.Stats == null || !part.Stats.restrictToPartType)) || PartSlots.Fits(socket.socketType, part.partType);
    }
}
