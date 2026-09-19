using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
[DefaultExecutionOrder(-150)]
public class EquipManager : MonoBehaviour
{
    public MechRulesConfig rulesConfig;
    public KeyCode interactKey = KeyCode.F;
    [Min(0.01f)] public float partScale = 1f;
    public event Action EquipmentChanged;
    readonly Dictionary<PartCategory, Part> equipped = new Dictionary<PartCategory, Part>();
    readonly HashSet<Part> nearby = new HashSet<Part>();
    Socket[] sockets;
    Key inputKey;
    public Part GetEquipped(PartCategory category) => equipped.TryGetValue(category, out var part) ? part : null;
    void Awake()
    {
        Enum.TryParse(interactKey.ToString(), out inputKey);
        var starting = GetComponentsInChildren<Part>(true);
        RefreshSockets();
        foreach (var part in starting)
            if ((part.isStartingPart || part.transform.parent.GetComponent<Socket>() != null) && part.partType == PartCategory.Torso) AttachStarting(part);
        RefreshSockets();
        foreach (var part in starting)
            if ((part.isStartingPart || part.transform.parent.GetComponent<Socket>() != null) && part.partType != PartCategory.Torso) AttachStarting(part);
    }
    void AttachStarting(Part part)
    {
        var socket = FindSocketForType(part.partType);
        if (socket != null && GetEquipped(part.partType) == null) Attach(socket, part);
    }
    void RefreshSockets() => sockets = GetComponentsInChildren<Socket>(true);
    public void SetNearbyPart(Part part) { if (part != null && !part.isEquipped) nearby.Add(part); }
    public void ClearNearbyPart(Part part) => nearby.Remove(part);
    void Update()
    {
        if (Time.timeScale <= 0 || Keyboard.current == null || inputKey == Key.None || !Keyboard.current[inputKey].wasPressedThisFrame) return;
        Part closest = null; float distance = float.MaxValue;
        foreach (var part in nearby)
        {
            if (part == null || part.isEquipped || !part.gameObject.activeInHierarchy) continue;
            float d = (part.transform.position - transform.position).sqrMagnitude;
            if (d < distance) { distance = d; closest = part; }
        }
        if (closest != null) TryEquip(closest);
    }
    public bool TryEquip(Part part)
    {
        if (part == null || part.isEquipped || !part.gameObject.activeInHierarchy) return false;
        var target = FindSocketForType(part.partType);
        if (target == null || rulesConfig != null && !rulesConfig.CanEquip(target, part)) return false;
        if (part.partType == PartCategory.Torso)
        {
            var replacement = part.GetComponentsInChildren<Socket>(true);
            foreach (var pair in equipped)
            {
                if (pair.Key == PartCategory.Torso || pair.Value == null) continue;
                bool found = false;
                foreach (var socket in replacement) if (socket.socketType == pair.Key) found = true;
                if (!found) return false;
            }
            foreach (var pair in equipped)
                if (pair.Key != PartCategory.Torso && pair.Value != null) pair.Value.transform.SetParent(target.transform.parent, true);
        }
        Unequip(part.partType, false);
        Attach(target, part);
        if (part.partType == PartCategory.Torso)
        {
            RefreshSockets();
            foreach (var pair in equipped)
                if (pair.Key != PartCategory.Torso && pair.Value != null) Place(FindSocketForType(pair.Key), pair.Value);
        }
        nearby.Remove(part);
        EquipmentChanged?.Invoke();
        return true;
    }
    public Part Unequip(PartCategory category) => Unequip(category, true);
    Part Unequip(PartCategory category, bool notify)
    {
        var part = GetEquipped(category);
        if (part == null) return null;
        if (category == PartCategory.Torso && notify)
            foreach (var pair in equipped)
                if (pair.Key != PartCategory.Torso && pair.Value != null) pair.Value.transform.SetParent(part.transform.parent, true);
        foreach (var driver in part.GetComponentsInChildren<MechAbilityDriver>(true)) driver.BindOwner(null);
        part.transform.SetParent(null, true);
        part.isEquipped = false; part.EquippedBy = null;
        part.SetEquippedPhysics(false);
        equipped.Remove(category);
        RefreshSockets();
        if (notify) EquipmentChanged?.Invoke();
        return part;
    }
    void Attach(Socket socket, Part part)
    {
        Place(socket, part);
        part.isEquipped = true; part.EquippedBy = this;
        part.SetEquippedPhysics(true);
        equipped[part.partType] = part;
        foreach (var driver in part.GetComponentsInChildren<MechAbilityDriver>(true)) driver.BindOwner(transform);
    }
    void Place(Socket socket, Part part)
    {
        part.transform.SetParent(socket.transform, true);
        part.transform.SetPositionAndRotation(socket.transform.position, socket.transform.rotation);
        Vector3 desired = Vector3.Scale(transform.lossyScale, part.authoredScale) * partScale;
        Vector3 parent = socket.transform.lossyScale;
        part.transform.localScale = new Vector3(desired.x / parent.x, desired.y / parent.y, desired.z / parent.z);
    }
    Socket FindSocketForType(PartCategory type)
    {
        foreach (var socket in sockets) if (socket != null && socket.socketType == type) return socket;
        return null;
    }
}
