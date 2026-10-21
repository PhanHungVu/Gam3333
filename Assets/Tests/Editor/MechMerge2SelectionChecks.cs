using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEditor;
using Object = UnityEngine.Object;

public static class MechMerge2SelectionChecks
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); Debug.Log("MERGE2 SELECTION PASS: " + message); }
    static void Call(PickupDetector detector, string method, params object[] args) =>
        typeof(PickupDetector).GetMethod(method, Private).Invoke(detector, args);

    public static void Run(MechMovementController motor)
    {
        var equipment = motor.GetComponent<EquipManager>();
        var detector = motor.GetComponentInChildren<PickupDetector>();
        var health = motor.GetComponent<CombatHealth>();
        var hud = Object.FindObjectsByType<MechHealthHud>().Single(h => h.Health == health);
        Check(detector.highlightElementPrefab != null, "Pickup highlighting remains wired");
        Check(hud != null && health != null && hud.Canvas.enabled && health.MaximumHealth == equipment.Health,
            "Restored health HUD uses the merged player's equipment");
        foreach (string name in new[] { "Heavy Legs Pickup", "Mace Animation Pickup", "Arm Crossbow Animation Pickup" })
            Check(GameObject.Find(name) != null, "Restored showcase pickup: " + name);

        Part heavy = null, axe = null;
        Keyboard keyboard = null;
        var originalSword = equipment.GetEquipped(PartCategory.ArmL);
        var originalAxe = equipment.GetEquipped(PartCategory.ArmR);
        try
        {
            // Test trigger bookkeeping independently of world pickup positions.
            detector.enabled = false; detector.enabled = true;
            heavy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Locomotion/BipedHeavy_Module.prefab"),
                new Vector3(10000f, 100f, 0f), Quaternion.identity).GetComponent<Part>();
            axe = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_throwingaxe.prefab"),
                new Vector3(10010f, 100f, 0f), Quaternion.identity).GetComponent<Part>();
            var heavyCollider = heavy.GetComponentInChildren<Collider>();
            var axeColliders = axe.GetComponentsInChildren<Collider>();
            Check(heavyCollider != null && axeColliders.Length >= 2, "Selection checks use real module colliders");
            Call(detector, "OnTriggerStay", heavyCollider);
            Call(detector, "OnTriggerStay", axeColliders[0]);
            Call(detector, "OnTriggerStay", axeColliders[1]);
            Call(detector, "Update");
            Check(equipment.GetSelectedPart() == heavy, "First highlighted pickup is the actual selected part");
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            keyboard = InputSystem.AddDevice<Keyboard>();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.V)); InputSystem.Update();
            Check(keyboard.vKey.wasPressedThisFrame, "Simulated V input reaches the real Input System in batch mode");
            Call(detector, "Update");
            Check(equipment.GetSelectedPart() == axe, "V cycles to a different part even with multiple colliders");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
            Check(!axe.GetComponent<MechWeaponAnimator>().CanAnimate, "Selected ground weapon stays unanimated");

            // Hold-F swaps, including putting a part on either side and
            // replacing an empty socket.
            Check(equipment.TrySwapSelectedInto(PartCategory.ArmL), "Swapping the selected part into the left arm succeeds");
            Check(equipment.GetEquipped(PartCategory.ArmL) == axe && equipment.GetEquipped(PartCategory.ArmR) == originalAxe,
                "Left arm swap does not replace the right arm");
            Check(axe.GetComponent<MechWeaponAnimator>().CanAnimate && !originalSword.GetComponent<MechWeaponAnimator>().CanAnimate,
                "Equip activates animation and drop stops the former weapon");
            Check(!detector.NearbyParts.Contains(axe), "Equip removes every collider from selection");
            equipment.Unequip(PartCategory.ArmL);
            equipment.Unequip(PartCategory.ArmR);
            equipment.SetSelectedPart(axe);
            Check(equipment.TrySwapSelectedInto(PartCategory.ArmR), "Swapping into an empty right arm succeeds");
            Check(equipment.GetEquipped(PartCategory.ArmR) == axe && equipment.GetEquipped(PartCategory.ArmL) == null,
                "Right arm swap handles an empty socket without a null reference");
            equipment.Unequip(PartCategory.ArmR);
            Check(equipment.TryEquip(originalSword, PartCategory.ArmL) && equipment.TryEquip(originalAxe, PartCategory.ArmR),
                "Original teammate arm loadout can be restored without lost functionality");

            float before = health.CurrentHealth;
            health.ApplyDamage(20f);
            float injured = health.CurrentHealth;
            equipment.SetSelectedPart(heavy);
            Check(equipment.TrySwapSelectedInto(PartCategory.Legs), "Swapping the selected legs succeeds");
            Check(equipment.GetEquipped(PartCategory.Legs) == heavy && motor.CurrentModule == heavy.GetComponent<LocomotionModule>(),
                "Legs swap switches to the Heavy animation/locomotion module");
            Check(health.CurrentHealth == injured && injured == before - 20f && health.MaximumHealth == equipment.Health
                && Mathf.Abs(hud.NormalizedHealth - health.CurrentHealth / health.MaximumHealth) < 0.001f,
                "Heavy swap updates health capacity and UI without healing damage");
            Check(motor.GetComponent<MechLocomotionDebug>().TrySwap(0), "Fast default remains available after a swap");
            Check(!equipment.TryEquip(heavy, PartCategory.Head) && heavy.partType == PartType.Legs,
                "Invalid destination rejects the swap without changing part identity");
            Check(motor.GetComponent<MechEquipmentCombat>().HasSword
                && motor.GetComponent<MechEquipmentCombat>().HasWeapon(MechWeaponType.ThrowingAxe),
                "Restored weapon and animation bridges survive swaps");
        }
        finally
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (heavy != null && !heavy.isEquipped) Object.DestroyImmediate(heavy.gameObject);
            if (axe != null && !axe.isEquipped) Object.DestroyImmediate(axe.gameObject);
            detector.enabled = false; detector.enabled = true;
        }
    }
}
