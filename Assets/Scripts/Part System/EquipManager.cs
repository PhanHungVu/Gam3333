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
    Part selectedPart;
    Socket[] sockets;
    Key inputKey;
    public PartSwapHUDScript partSwapHUDScript;
    PickupDetector pickupDetector;
    public Part GetEquipped(PartCategory category) => equipped.TryGetValue(category, out var part) ? part : null;
    // Maximum health supplied by the current loadout.
    public float Health
    {
        get
        {
            float total = 0f;
            foreach (var part in equipped.Values)
                if (part != null && part.isEquipped && part.data != null)
                    total += Mathf.Max(0f, part.data.GetStat(StatType.Health));
            return total;
        }
    }
    void Awake()
    {
        Enum.TryParse(interactKey.ToString(), out inputKey);
        var starting = GetComponentsInChildren<Part>(true);
        partSwapHUDScript = GetComponent<PartSwapHUDScript>();
        pickupDetector = GetComponentInChildren<PickupDetector>(true);
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
    public void SetSelectedPart(Part part) { if (part != null && !part.isEquipped) selectedPart = part; }
    public Part GetSelectedPart() => selectedPart != null && !selectedPart.isEquipped && selectedPart.gameObject.activeInHierarchy ? selectedPart : null;
    public void ClearSelectedPart(Part part) { if (selectedPart == part) selectedPart = null; }
    void Update()
    {
        if (Time.timeScale <= 0 || Keyboard.current == null || inputKey == Key.None || !Keyboard.current[inputKey].wasPressedThisFrame) return;
        var part = GetSelectedPart();
        if (part == null) return;
        if (partSwapHUDScript != null && partSwapHUDScript.IsAvailable) partSwapHUDScript.ShowPartList();
        else TryEquip(part);
    }
    public bool TryEquip(Part part, PartCategory category)
    {
        if (part == null) return false;
        var original = part.partType;
        bool compatible = original == category
            || (original == PartCategory.ArmL || original == PartCategory.ArmR) && (category == PartCategory.ArmL || category == PartCategory.ArmR)
            || (original == PartCategory.ShoulderL || original == PartCategory.ShoulderR) && (category == PartCategory.ShoulderL || category == PartCategory.ShoulderR);
        if (!compatible || part.isEquipped) return false;
        part.partType = category;
        if (TryEquip(part)) return true;
        part.partType = original;
        return false;
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
        ClearSelectedPart(part);
        EquipmentChanged?.Invoke();
        if (pickupDetector != null) pickupDetector.PartEquipped(part);
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
