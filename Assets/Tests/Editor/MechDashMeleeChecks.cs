using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

public static class MechDashMeleeChecks
{
    public static void Run(Action<bool, string> check)
    {
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        var dash = actions.FindAction("Player/QuickBoost");
        check(actions.FindAction("Player/Sprint", false) == null, "Sprint action is removed from the input asset");
        bool shift = false, alt = false, stick = false;
        foreach (var binding in dash.bindings)
        { shift |= binding.path == "<Keyboard>/leftShift"; alt |= binding.path == "<Keyboard>/leftAlt"; stick |= binding.path == "<Gamepad>/leftStickPress"; }
        check(shift && stick && !alt, "Dash uses former sprint bindings, including Left Shift, and no longer uses Alt");
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MechAnimatedLegsSetup.PlayerPath),
            new Vector3(65000f, 2f, 0f), Quaternion.identity);
        var cleanup = new List<GameObject>();
        try
        {
            var combat = player.GetComponent<MechCombat>(); combat.ReadPlayerInput = false;
            var equipment = player.GetComponent<EquipManager>();
            var settings = new SerializedObject(combat);
            check(settings.FindProperty("meleeSize").vector3Value == new Vector3(5f, 3.5f, 5.5f), "Scene player uses the enlarged 5 x 3.5 x 5.5 melee volume");
            foreach (string family in new[] { "sword", "mace" })
            {
                var part = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_" + family + ".prefab")).GetComponent<Part>();
                var old = equipment.GetEquipped(PartCategory.ArmL);
                check(equipment.TryEquip(part, PartCategory.ArmL), family + " equips as a damaging melee weapon");
                cleanup.Add(old.gameObject); old.transform.position += Vector3.left * 100f;
                var target = GameObject.CreatePrimitive(PrimitiveType.Cube); cleanup.Add(target);
                target.transform.localScale = Vector3.one * 0.3f;
                target.transform.position = player.transform.position + new Vector3(2.2f, 0f, family == "sword" ? 5f : 3.4f);
                var health = target.AddComponent<CombatHealth>();
                var second = new GameObject("Second Hurt Collider"); second.transform.SetParent(target.transform, false); second.AddComponent<BoxCollider>();
                Physics.SyncTransforms();
                check(combat.TryAttack(PartCategory.ArmL).Started && health.CurrentHealth == (family == "sword" ? 0f : 50f),
                    family + " enlarged hitbox reaches a wider/farther target and damages compound colliders only once");
                check(!combat.TryAttack(PartCategory.ArmL).Started, family + " retains its configured melee cooldown");
                check(part.GetComponent<MechWeaponAnimator>().CurrentPose == MechWeaponAnimator.PoseState.Fire,
                    family + " damage still starts the authored attack animation");
                Object.DestroyImmediate(target);
            }
            var left = equipment.Unequip(PartCategory.ArmL); cleanup.Add(left.gameObject); left.transform.position += Vector3.left * 100f;
            var rightMace = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_mace.prefab")).GetComponent<Part>();
            var oldRight = equipment.GetEquipped(PartCategory.ArmR);
            check(equipment.TryEquip(rightMace, PartCategory.ArmR), "Mace also equips into the right arm");
            cleanup.Add(oldRight.gameObject); oldRight.transform.position += Vector3.left * 200f;
            var blocked = GameObject.CreatePrimitive(PrimitiveType.Cube); cleanup.Add(blocked);
            blocked.transform.position = player.transform.position + Vector3.forward * 3f;
            var blockedHealth = blocked.AddComponent<CombatHealth>();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); cleanup.Add(wall);
            wall.transform.position = player.transform.position + Vector3.forward * 1.5f;
            wall.transform.localScale = new Vector3(8f, 8f, 0.2f); Physics.SyncTransforms();
            int admissions = 0; combat.WeaponAttackStarted += p => { if (p == rightMace) admissions++; };
            combat.TryAttack(PartCategory.ArmR);
            check(admissions == 1 && blockedHealth.CurrentHealth == 100f, "RMB admits right-arm mace while walls still block its damage");
            check(!combat.TryAttack(PartCategory.ArmL).Started, "An empty left arm never attacks using the right-arm melee weapon");
            var drop = equipment.Unequip(PartCategory.ArmR); cleanup.Add(drop.gameObject);
            combat.TryAttack(PartCategory.ArmR); check(admissions == 1, "Dropped mace cannot admit another attack");
        }
        finally
        {
            foreach (var obj in cleanup) if (obj != null) Object.DestroyImmediate(obj);
            Object.DestroyImmediate(player);
        }
    }
}
