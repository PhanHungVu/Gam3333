using UnityEngine;

[CreateAssetMenu(fileName = "MechRulesConfig", menuName = "Mech/Rules Config")]
public class MechRulesConfig : ScriptableObject
{
    public bool allowMixedTypes = false;

    public bool CanEquip(Socket socket, Part part)
    {
        return allowMixedTypes || socket.socketType == part.partType;
    }
}