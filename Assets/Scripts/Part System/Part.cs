using UnityEngine;
using UnityEngine.Serialization;

public class Part : MonoBehaviour
{
    public PartData data;    
    public PartCategory partType;
    public bool isStartingPart = true;
    public bool isEquipped = false;
}