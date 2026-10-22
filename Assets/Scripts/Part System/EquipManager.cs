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
    [Tooltip("Starting parts and the slot (side) each starts in. Parts placed under a socket on the player start in that socket, so this only needs entries to override that.")]
    public List<LoadoutEntry> startingLoadout = new List<LoadoutEntry>();
    readonly Dictionary<PartCategory, Part> equipped = new Dictionary<PartCategory, Part>();
    Part selectedPart;
    Socket[] sockets;
    Key inputKey;
    PickupDetector pickupDetector;
    public Part GetEquipped(PartCategory category) => equipped.TryGetValue(category, out var part) ? part : null;
    void Awake()
    {
        Enum.TryParse(interactKey.ToString(), out inputKey);
        var starting = GetComponentsInChildren<Part>(true);
        pickupDetector = GetComponentInChildren<PickupDetector>(true);
        RefreshSockets();
        // Torso first so the limb sockets exist, then everything else.
        AttachStarting(true, starting);
        RefreshSockets();
        AttachStarting(false, starting);
    }
    // Starting side: a loadout entry wins (hook for the pre-level customiser); otherwise a
    // part placed under a socket starts in that socket, else in the first free slot that fits.
    void AttachStarting(bool torso, Part[] placed)
    {
        foreach (var entry in startingLoadout)
        {
            if (entry.part == null || (entry.part.partType == PartType.Torso) != torso) continue;
            var part = entry.part.gameObject.scene.IsValid() ? entry.part : Instantiate(entry.part);
            if (!StartIn(part, entry.slot))
                Debug.LogWarning($"Starting loadout: {entry.part.name} cannot start in {entry.slot}", this);
        }
        foreach (var part in placed)
        {
            if (part.EquippedBy != null || (part.partType == PartType.Torso) != torso) continue;
            var parent = part.transform.parent != null ? part.transform.parent.GetComponent<Socket>() : null;
            if (!part.isStartingPart && parent == null) continue;
            PartCategory? slot = parent != null && PartSlots.Fits(parent.socketType, part.partType) ? parent.socketType : FindFreeSlot(part.partType);
            if (slot != null) StartIn(part, slot.Value);
        }
    }
    bool StartIn(Part part, PartCategory slot)
    {
        var socket = FindSocket(slot);
        if (socket == null || GetEquipped(slot) != null || !PartSlots.Fits(slot, part.partType)) return false;
        Attach(socket, part);
        return true;
    }
    void RefreshSockets() => sockets = GetComponentsInChildren<Socket>(true);
    public void SetSelectedPart(Part part) { if (part != null && !part.isEquipped) selectedPart = part; }
    public Part GetSelectedPart() => selectedPart != null && !selectedPart.isEquipped && selectedPart.gameObject.activeInHierarchy ? selectedPart : null;
    public void ClearSelectedPart(Part part) { if (selectedPart == part) selectedPart = null; }
    // Hold the interact key (F) while a floor part is selected: MechCombatController then
    // routes the next attack button to TrySwapSelectedInto for that button's socket.
    public bool IsSwapHeld => Time.timeScale > 0 && Keyboard.current != null && inputKey != Key.None
        && Keyboard.current[inputKey].isPressed && GetSelectedPart() != null;
    // Swap the selected floor part into this socket. Incompatible parts are refused by
    // TryEquip's rules check (e.g. F + Q on an arm part does nothing).
    // Head and legs have no button yet; a future key alongside F will call this with Head/Legs.
    public bool TrySwapSelectedInto(PartCategory slot)
    {
        var part = GetSelectedPart();
        return part != null && TryEquip(part, slot);
    }
    // Equip into a named slot. Parts carry no side, so the caller (swap input, loadout)
    // always decides which socket a part goes into.
    public bool TryEquip(Part part, PartCategory slot)
    {
        if (part == null || part.isEquipped || !part.gameObject.activeInHierarchy) return false;
        var target = FindSocket(slot);
        if (target == null || !CanEquip(target, part)) return false;
        if (slot == PartCategory.Torso)
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
        Unequip(slot, false);
        Attach(target, part);
        if (slot == PartCategory.Torso)
        {
            RefreshSockets();
            foreach (var pair in equipped)
                if (pair.Key != PartCategory.Torso && pair.Value != null) Place(FindSocket(pair.Key), pair.Value);
        }
        ClearSelectedPart(part);
        EquipmentChanged?.Invoke();
        if (pickupDetector != null) pickupDetector.PartEquipped(part);
        return true;
    }
    // No slot given: the first free slot that fits (left before right), otherwise the left slot.
    public bool TryEquip(Part part) =>
        part != null && TryEquip(part, FindFreeSlot(part.partType) ?? PartSlots.DefaultSlot(part.partType));
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
        part.isEquipped = false; part.EquippedBy = null; part.Slot = null;
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
        equipped[socket.socketType] = part;
        part.Slot = socket.socketType;
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
    Socket FindSocket(PartCategory slot)
    {
        foreach (var socket in sockets) if (socket != null && socket.socketType == slot) return socket;
        return null;
    }
    // Without a rules config, parts still only go into sockets of their own type.
    bool CanEquip(Socket socket, Part part) =>
        rulesConfig != null ? rulesConfig.CanEquip(socket, part) : PartSlots.Fits(socket.socketType, part.partType);
    PartCategory? FindFreeSlot(PartType type)
    {
        foreach (var slot in PartSlots.SlotsFor(type))
            if (GetEquipped(slot) == null && FindSocket(slot) != null) return slot;
        return null;
    }
}
