using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// The default player mech (Prefabs/Mech/Specialist) keeps the part system fully modular: every socket is
// present and mirrored, every part an enemy can drop (and every legs module) equips into each socket of its
// type, sits under that socket, and swaps back out cleanly; parts never fit a socket of another type.
public static class SpecialistMechChecks
{
    public const string SpecialistPath = "Assets/Prefabs/Mech/Specialist/FullPlayer_mech_specialist.prefab";

    public static void Run(Action<bool, string> check)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPath);
        check(prefab != null, "The specialist player prefab exists at " + SpecialistPath);
        if (prefab == null) return;

        // Sockets: one of each type, left and right mirrored across the mech.
        var sockets = prefab.GetComponentsInChildren<Socket>(true);
        var types = sockets.Select(s => s.socketType).ToList();
        bool complete = Enum.GetValues(typeof(PartCategory)).Cast<PartCategory>().All(c => types.Count(t => t == c) == 1);
        check(complete, "The specialist has exactly one socket of each type: " + string.Join(", ", types));
        Socket Of(PartCategory c) => sockets.FirstOrDefault(s => s.socketType == c);
        string mirror = Mirrored(prefab.transform, Of(PartCategory.ArmL), Of(PartCategory.ArmR))
            + Mirrored(prefab.transform, Of(PartCategory.ShoulderL), Of(PartCategory.ShoulderR));
        check(mirror.Length == 0, "Left and right arm and shoulder sockets mirror each other across the mech" + mirror);

        // Drops: every part an enemy can drop is a valid part with data, for a socket the specialist has.
        var drops = new List<Part>();
        var badDrops = new List<string>();
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Enemies" }))
        {
            var enemy = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            foreach (var drop in enemy.GetComponentsInChildren<EnemyPartDrop>(true))
            {
                var list = new SerializedObject(drop).FindProperty("partPrefabs");
                for (int i = 0; i < list.arraySize; i++)
                {
                    var part = list.GetArrayElementAtIndex(i).objectReferenceValue as Part;
                    if (part == null || part.data == null) badDrops.Add(enemy.name + "[" + i + "]");
                    else if (!drops.Contains(part)) drops.Add(part);
                }
            }
        }
        check(badDrops.Count == 0 && drops.Count > 0, "Every part enemies can drop is a part with PartData" + (badDrops.Count > 0 ? "; broken: " + string.Join(", ", badDrops) : ""));

        // Parts to try on the mech: the drops, the specialist's own parts, the shared parts (e.g. head_base) and the legs modules.
        var candidates = new List<Part>(drops);
        foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs/Mech/Specialist", "Assets/Prefabs/Mech/Locomotion", "Assets/Prefabs/Mech/Base/Parts" })
                     .Select(AssetDatabase.GUIDToAssetPath))
        {
            if (path == SpecialistPath) continue;
            var part = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<Part>();
            if (part != null && part.data != null && !candidates.Contains(part)) candidates.Add(part);
        }

        var player = Object.Instantiate(prefab, new Vector3(-40000f, 1.6f, 0f), Quaternion.identity);
        var spawned = new List<GameObject>();
        try
        {
            TestMech.StopPlayerInput(player.GetComponent<MechPlayerInput>());
            var equipment = player.GetComponent<EquipManager>();
            var live = player.GetComponentsInChildren<Socket>(true);
            var original = new Dictionary<PartCategory, Part>();
            foreach (PartCategory c in Enum.GetValues(typeof(PartCategory))) original[c] = equipment.GetEquipped(c);
            check(original[PartCategory.Head] != null && original[PartCategory.Torso] != null && original[PartCategory.Legs] != null,
                "The specialist starts with a head, a torso and legs equipped");

            var failures = new Dictionary<PartType, List<string>>();
            var tried = new Dictionary<PartType, int>();
            foreach (var prefabPart in candidates)
            {
                var type = prefabPart.partType;
                if (type == PartType.Torso) continue; // torso swaps move every other part; covered by the torso transfer checks
                if (!failures.ContainsKey(type)) { failures[type] = new List<string>(); tried[type] = 0; }
                tried[type]++;
                foreach (var slot in PartSlots.SlotsFor(type))
                {
                    var instance = Object.Instantiate(prefabPart.gameObject, new Vector3(-40010f, 1f, 0f), Quaternion.identity);
                    spawned.Add(instance);
                    var part = instance.GetComponent<Part>();
                    var socket = live.FirstOrDefault(s => s.socketType == slot);
                    bool equipped = equipment.TryEquip(part, slot);
                    bool placed = equipped && equipment.GetEquipped(slot) == part && part.isEquipped && part.Slot == slot
                        && socket != null && part.transform.IsChildOf(socket.transform);
                    var old = original[slot];
                    bool oldFreed = old == null || (!old.isEquipped && !old.transform.IsChildOf(player.transform));
                    // put the original back (or empty the slot)
                    bool restored = old != null ? equipment.TryEquip(old, slot) && equipment.GetEquipped(slot) == old
                                                : equipment.Unequip(slot) == part && equipment.GetEquipped(slot) == null;
                    bool released = !part.isEquipped && !part.transform.IsChildOf(player.transform);
                    if (!(placed && oldFreed && restored && released))
                        failures[type].Add($"{prefabPart.name}->{slot} (equip {equipped}, placed {placed}, old freed {oldFreed}, restored {restored}, released {released})");
                }
                // never into a socket of another type
                var wrongSlot = Enum.GetValues(typeof(PartCategory)).Cast<PartCategory>()
                    .FirstOrDefault(c => !PartSlots.SlotsFor(type).Contains(c) && c != PartCategory.Torso);
                var stray = Object.Instantiate(prefabPart.gameObject, new Vector3(-40020f, 1f, 0f), Quaternion.identity);
                spawned.Add(stray);
                if (equipment.TryEquip(stray.GetComponent<Part>(), wrongSlot))
                {
                    failures[type].Add($"{prefabPart.name} also fitted {wrongSlot}");
                    if (original[wrongSlot] != null) equipment.TryEquip(original[wrongSlot], wrongSlot); else equipment.Unequip(wrongSlot);
                }
            }

            foreach (var type in new[] { PartType.Head, PartType.Arm, PartType.Shoulder, PartType.Legs })
            {
                int count = tried.TryGetValue(type, out var n) ? n : 0;
                var list = failures.TryGetValue(type, out var f) ? f : new List<string>();
                check(count > 0 && list.Count == 0,
                    $"{type} parts ({count}) equip into each {type} socket of the specialist, sit under it, swap back out cleanly, and fit no other socket"
                    + (list.Count > 0 ? "; " + string.Join("; ", list) : count == 0 ? "; no " + type + " parts found to try" : ""));
            }
            bool intact = Enum.GetValues(typeof(PartCategory)).Cast<PartCategory>().All(c => equipment.GetEquipped(c) == original[c]);
            check(intact && player.GetComponentsInChildren<MechMovementController>(true).Length == 1,
                "After every swap the specialist is back to its own parts, with its single motor");
        }
        finally
        {
            foreach (var go in spawned) if (go != null) Object.DestroyImmediate(go);
            Object.DestroyImmediate(player);
        }
    }

    // Empty when the two sockets mirror each other across the mech's centre (x flipped, y and z equal).
    static string Mirrored(Transform root, Socket left, Socket right)
    {
        if (left == null || right == null) return "; missing " + (left == null ? "left" : "right") + " socket";
        Vector3 l = root.InverseTransformPoint(left.transform.position), r = root.InverseTransformPoint(right.transform.position);
        bool ok = Mathf.Abs(l.x + r.x) < 0.05f && Mathf.Abs(l.y - r.y) < 0.05f && Mathf.Abs(l.z - r.z) < 0.05f && Mathf.Abs(l.x) > 0.05f;
        return ok ? "" : $"; {left.name} {l} vs {right.name} {r}";
    }
}
