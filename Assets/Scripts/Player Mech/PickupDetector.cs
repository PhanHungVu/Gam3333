using UnityEngine;

public class PickupDetector : MonoBehaviour
{
    private EquipManager manager;

    void Awake()
    {
        manager = GetComponentInParent<EquipManager>();
    }

    void OnTriggerEnter(Collider other)
    {
        var part = other.GetComponentInParent<Part>();
        if (part != null && !part.isEquipped)
            manager.SetNearbyPart(part);
    }

    void OnTriggerExit(Collider other)
    {
        var part = other.GetComponentInParent<Part>();
        if (part != null)
            manager.ClearNearbyPart(part);
    }
}