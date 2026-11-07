using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
[DefaultExecutionOrder(-150)]
public class EquipManager : MonoBehaviour
{
    public MechRulesConfig rulesConfig;
    [Min(0.01f)] public float partScale = 1f;
    public event Action EquipmentChanged;
    [Tooltip("Starting parts and the slot (side) each starts in. Parts placed under a socket on the player start in that socket, so this only needs entries to override that.")]
    public List<LoadoutEntry> startingLoadout = new List<LoadoutEntry>();
    readonly Dictionary<PartCategory, Part> equipped = new Dictionary<PartCategory, Part>();
    Part selectedPart;
    Socket[] sockets;
    PickupDetector pickupDetector;
    public Part GetEquipped(PartCategory category) => equipped.TryGetValue(category, out var part) ? part : null;
    void Awake()
    {
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
    // Input routes 1 to the selected part's left/single slot and 2 to its right slot.
    // Compatibility and torso-transfer validation remain owned by TryEquip.
    public bool TrySwapSelectedInto(PartCategory slot)
    {
        var part = GetSelectedPart();
        return part != null && TryEquip(part, slot);
    }
    // The same read-only validation drives pickup previews and the final equipment transaction.
    public bool CanEquip(Part part, PartCategory slot)
        => CanEquipForRetrieval(part, slot, null);
    internal bool CanEquipForRetrieval(Part part, PartCategory slot, RemotePartRetrieval requester)
    {
        if (part == null || part.isEquipped || !part.isActiveAndEnabled
            || (part.IsRetrieving && part.RetrievalOwner != requester)) return false;
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
        }
        return true;
    }
    // Empty compatible sockets first; otherwise a stable left/default replacement.
    public PartCategory? PreferredSlotFor(Part part)
    {
        if (part == null) return null;
        PartCategory? fallback = null;
        foreach (var slot in PartSlots.SlotsFor(part.partType))
        {
            if (!CanEquip(part, slot)) continue;
            if (!fallback.HasValue) fallback = slot;
            if (GetEquipped(slot) == null) return slot;
        }
        return fallback;
    }
    // Equip into a named slot. Parts carry no side, so the caller decides the side.
    public bool TryEquip(Part part, PartCategory slot)
    {
        if (!CanEquip(part, slot)) return false;
        var target = FindSocket(slot);
        if (slot == PartCategory.Torso)
            foreach (var pair in equipped)
                if (pair.Key != PartCategory.Torso && pair.Value != null) pair.Value.transform.SetParent(target.transform.parent, true);
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
    public bool TryEquip(Part part)
    {
        var slot = PreferredSlotFor(part);
        return slot.HasValue && TryEquip(part, slot.Value);
    }
    public Part Unequip(PartCategory category) => Unequip(category, true);
    Part Unequip(PartCategory category, bool notify)
    {
        var part = GetEquipped(category);
        if (part == null) return null;
        if (category == PartCategory.Torso && notify)
            foreach (var pair in equipped)
                if (pair.Key != PartCategory.Torso && pair.Value != null) pair.Value.transform.SetParent(part.transform.parent, true);
        part.transform.SetParent(null, true);
        part.ApplyMuzzlePose(false, Pose.identity);
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
    }
    void Place(Socket socket, Part part)
    {
        AttachmentPose(socket, part, out var pose, out var desired);
        part.transform.SetParent(socket.transform, true);
        part.transform.SetPositionAndRotation(pose.position, pose.rotation);
        Vector3 parent = socket.transform.lossyScale;
        part.transform.localScale = new Vector3(desired.x / parent.x, desired.y / parent.y, desired.z / parent.z);
        part.ApplyMuzzlePose(socket.TryMuzzlePose(part, out var muzzlePose), muzzlePose);
    }
    // World destination uses exactly the same per-weapon corrections as final attachment.
    public bool TryGetAttachmentPose(Part part, PartCategory slot, out Pose pose, out Vector3 worldScale)
    {
        pose = Pose.identity; worldScale = Vector3.one;
        var requester = part != null && part.RetrievalOwner != null && part.RetrievalOwner.Equipment == this ? part.RetrievalOwner : null;
        if (!CanEquipForRetrieval(part, slot, requester)) return false;
        AttachmentPose(FindSocket(slot), part, out pose, out worldScale); return true;
    }
    void AttachmentPose(Socket socket, Part part, out Pose pose, out Vector3 worldScale)
    {
        worldScale = Vector3.Scale(transform.lossyScale, part.authoredScale) * partScale;
        var correction = socket.AttachmentFor(part);
        pose = new Pose(socket.transform.position + socket.transform.rotation * Vector3.Scale(worldScale, correction.position),
            socket.transform.rotation * correction.rotation);
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
