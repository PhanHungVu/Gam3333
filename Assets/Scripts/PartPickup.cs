using UnityEngine;

public class PartPickup : MonoBehaviour
{
    private Part part;

    void Awake()
    {
        part = GetComponent<Part>();
    }

    void OnTriggerEnter(Collider other)
    {
        var manager = other.GetComponentInParent<EquipManager>();
        if (manager != null) manager.SetNearbyPart(part);
    }

    void OnTriggerExit(Collider other)
    {
        var manager = other.GetComponentInParent<EquipManager>();
        if (manager != null) manager.ClearNearbyPart(part);
    }
}