using UnityEngine;

[CreateAssetMenu(fileName = "MechRulesConfig", menuName = "Mech/Rules Config")]
public class MechRulesConfig : ScriptableObject
{
    public bool allowMixedTypes = false;
    [Tooltip("Metres in one golem length. Part Range stats are measured in golem lengths.")]
    [Min(0.01f)] public float metresPerGolem = 4f;

    public bool CanEquip(Socket socket, Part part)
    {
        return (allowMixedTypes && (part.Stats == null || !part.Stats.restrictToPartType)) || PartSlots.Fits(socket.socketType, part.partType);
    }
}
