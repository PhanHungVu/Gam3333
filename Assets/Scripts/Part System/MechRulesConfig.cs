using UnityEngine;

[CreateAssetMenu(fileName = "MechRulesConfig", menuName = "Mech/Rules Config")]
public class MechRulesConfig : ScriptableObject
{
    public bool allowMixedTypes = false;

    public bool CanEquip(Socket socket, Part part)
    {
        return allowMixedTypes || PartSlots.Fits(socket.socketType, part.partType);
    }
}