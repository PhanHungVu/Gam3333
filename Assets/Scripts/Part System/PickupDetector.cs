using System.Collections.Generic;
using UnityEngine;
public class PickupDetector : MonoBehaviour
{
    EquipManager manager;
    readonly Dictionary<Collider, Part> contacts = new Dictionary<Collider, Part>();
    void Awake() => manager = GetComponentInParent<EquipManager>();
    void OnTriggerStay(Collider other)
    {
        if (manager == null) return;
        var part = other.GetComponentInParent<Part>();
        if (part == null || part.isEquipped) return;
        contacts[other] = part; manager.SetNearbyPart(part);
    }
    void OnTriggerEnter(Collider other) => OnTriggerStay(other);
    void OnTriggerExit(Collider other)
    {
        if (!contacts.TryGetValue(other, out var part)) return;
        contacts.Remove(other);
        foreach (var remaining in contacts.Values) if (remaining == part) return;
        if (manager != null) manager.ClearNearbyPart(part);
    }
    void OnDisable()
    {
        if (manager != null) foreach (var part in contacts.Values) manager.ClearNearbyPart(part);
        contacts.Clear();
    }
}
