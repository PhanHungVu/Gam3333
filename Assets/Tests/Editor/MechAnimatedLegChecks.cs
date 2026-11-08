using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechAnimatedLegChecks
{
    public static void Run(Action<bool, string> check)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath);
        var player = Object.Instantiate(prefab, new Vector3(32000, 1.6f, 0), Quaternion.identity);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = new Vector3(32000, -0.5f, 0);
        floor.transform.localScale = new Vector3(300, 1, 300);
        var view = new GameObject("Animated Legs Check View");
        var motor = player.GetComponent<MechMovementController>();
        TestMech.DisableAutoRun(motor);
        float walk = motor.WalkSpeed; // the mech's walking speed (legs x part modifiers)
        var equipment = player.GetComponent<EquipManager>();
        var debug = player.GetComponent<MechLocomotionDebug>();
        var energy = player.GetComponent<MechEnergyController>();
        var combat = player.GetComponent<MechCombat>();
        Part dropped = null;
        try
        {
            TestMech.StopPlayerInput(motor);
            Physics.SyncTransforms();
            Step(motor, equipment, view, 90);
            var fast = equipment.GetEquipped(PartCategory.Legs);
            var driver = fast.GetComponent<MechLegAnimator>();
            check(fast.data.partId == "Legs_Fast" && driver != null, "Integrated player starts with the real fast animated leg part");
            check(Mathf.Abs(equipment.partScale - 0.5f) < 0.001f && Mathf.Abs(fast.transform.lossyScale.x - 0.5f) < 0.001f,
                "Runtime equipment retains the authored half-scale mech dimensions");
            check(ClipWeight(driver, "Biped_Neutral") > 0.99f, "Standing fast legs use their neutral clip");
            var skin = driver.GetComponentInChildren<SkinnedMeshRenderer>();
            check(skin.enabled && driver.GetComponentsInChildren<Renderer>(true).Count(r => r.enabled && !(r is ParticleSystemRenderer)) == 1,
                "Animated module shows one leg mesh without duplicate torso/head renderers");
            var torso = equipment.GetEquipped(PartCategory.Torso);
            var torsoMesh = torso.GetComponentsInChildren<MeshRenderer>().First(r => r.enabled && r.GetComponentInParent<Part>() == torso);
            check(torsoMesh.enabled && torsoMesh.bounds.size.y < 2.2f,
                "Existing torso stays visible at its authored size after equipment initialization");
            float floorHeight = MinimumVertexHeight(skin);
            Debug.Log("ANIMATED LEGS FOOT HEIGHT: " + floorHeight);
            check(floorHeight > -0.2f && floorHeight < 0.3f, "Animated idle feet align with the ground instead of sinking below the capsule");
            Step(motor, equipment, view, 100, Vector2.up);
            check(Mathf.Abs(motor.AnimationData.Speed - walk) < 0.08f && ClipWeight(driver, "Biped_Run") > 0.99f,
                "Moving the default player runs the fast clip at its Move Speed");
            Transform leg = driver.Animator.transform.Find("Armature/Root/Leg.L");
            Vector3 rootPosition = player.transform.position;
            check(BoneChanges(driver.Animator, leg), "Fast animation visibly changes the leg bone pose");
            check(player.transform.position == rootPosition && !driver.Animator.applyRootMotion,
                "Sampling the real animation never moves the gameplay root");
            float phase = driver.NormalizedPhase;
            Vector3 velocity = motor.Velocity;
            float charge = energy.Current;
            int ammo = combat.AmmoRemaining(PartCategory.ShoulderL);
            var torsoPosition = torso.transform.position;
            var legSocket = fast.transform.parent;
            check(debug.TrySwap(1), "Second shortcut equips a heavy biped even though both modules have the same locomotion family");
            var heavy = equipment.GetEquipped(PartCategory.Legs);
            var heavyDriver = heavy.GetComponent<MechLegAnimator>();
            check(heavy != fast && heavy.data.partId == "Legs_Heavy" && motor.CurrentModule.Type == LocomotionType.Biped,
                "Fast/heavy identity is determined by part data, not Biped enum equality");
            check(player.transform.position == rootPosition && motor.Velocity == velocity && energy.Current == charge
                && combat.AmmoRemaining(PartCategory.ShoulderL) == ammo && torso.transform.position == torsoPosition,
                "Animated leg swap preserves root motion, energy, weapons and torso attachment");
            check(ClipWeight(heavyDriver, "Heavy_Walk.001") > 0.99f && Mathf.Abs(heavyDriver.NormalizedPhase - phase) < 0.02f,
                "Heavy walk is active immediately after a moving swap and inherits gait phase");
            check(!driver.Animator.isActiveAndEnabled && heavyDriver.Animator.isActiveAndEnabled,
                "Only the equipped leg rig continues animating");
            check(heavy.transform.parent == legSocket && Vector3.Distance(heavy.transform.position, legSocket.position) < 0.01f,
                "Both leg modules use the same socket origin");
            Step(motor, equipment, view, 100, Vector2.up);
            check(Mathf.Abs(motor.AnimationData.Speed - walk * 0.5f) < 0.08f && ClipWeight(heavyDriver, "Heavy_Walk.001") > 0.99f,
                "Heavy rating halves movement speed while its authored heavy gait plays");
            leg = heavyDriver.Animator.transform.Find("Armature/Root/Leg.L");
            check(BoneChanges(heavyDriver.Animator, leg), "Heavy animation changes bone transforms rather than just changing a label");
            check(heavyDriver.Animator.runtimeAnimatorController.animationClips.Any(c => c.name == "Heavy_Walk.001" && c.isLooping),
                "Heavy walk retains the imported looping clip");
            phase = heavyDriver.NormalizedPhase;
            player.GetComponent<MechEquipmentLocomotion>().enabled = false;
            check(!heavyDriver.Animator.enabled, "Disabling the equipment bridge unbinds the rig");
            player.GetComponent<MechEquipmentLocomotion>().enabled = true;
            check(heavyDriver.Animator.enabled && ClipWeight(heavyDriver, "Heavy_Walk.001") > 0.99f,
                "Re-enabling the bridge resumes the current module and movement state");
            Step(motor, equipment, view, 100);
            check(ClipWeight(heavyDriver, "Heavy_Neutral") > 0.99f, "Stopping movement blends heavy legs back to their neutral pose");
            Step(motor, equipment, view, 1, Vector2.up, jump: true);
            Step(motor, equipment, view, 12, Vector2.up);
            check(!motor.IsGrounded && heavyDriver.Animator.GetFloat("MoveBlend") < 0.1f,
                "Airborne movement does not continue a ground walking cycle");
            Step(motor, equipment, view, 100);
            dropped = equipment.Unequip(PartCategory.Legs);
            check(!heavyDriver.Animator.enabled && dropped.GetComponent<Rigidbody>() != null,
                "Dropped animated legs restore pickup physics and stop following the player");
            check(equipment.TryEquip(dropped), "Animated heavy part re-equips through the normal pickup API");
            dropped = null;
            Step(motor, equipment, view, 90, Vector2.up);
            check(ClipWeight(heavyDriver, "Heavy_Walk.001") > 0.99f, "Heavy animation resumes after ordinary drop and re-equip");
            check(debug.TrySwap(0) && equipment.GetEquipped(PartCategory.Legs) == fast, "Returning to fast reuses the original animated part");
            Step(motor, equipment, view, 60, Vector2.up);
            check(ClipWeight(driver, "Biped_Run") > 0.99f && player.GetComponentsInChildren<MechMovementController>().Length == 1,
                "Fast animation returns with exactly one gameplay movement controller");
        }
        finally
        {
            if (dropped != null) Object.DestroyImmediate(dropped.gameObject);
            Object.DestroyImmediate(player); Object.DestroyImmediate(floor); Object.DestroyImmediate(view);
        }
    }

    public static float ClipWeight(MechLegAnimator driver, string name) => driver.Animator.GetCurrentAnimatorClipInfo(0)
        .Where(c => c.clip.name == name).Sum(c => c.weight);

    static bool BoneChanges(Animator animator, Transform bone)
    {
        var position = bone.localPosition;
        var rotation = bone.localRotation;
        bool moved = false;
        // A reauthored gait may translate a floating leg or revisit a symmetric pose.
        for (int i = 0; i < 4; i++)
        {
            animator.Update(0.1f);
            moved |= Vector3.Distance(position, bone.localPosition) > 0.01f || Quaternion.Angle(rotation, bone.localRotation) > 2f;
        }
        return moved;
    }

    static void Step(MechMovementController motor, EquipManager equipment, GameObject view, int count,
        Vector2 move = default, bool jump = false)
    {
        for (int i = 0; i < count; i++)
        {
            motor.Tick(new MechLocomotionInput { move = move, jump = jump && i == 0 }, view.transform, 0.02f);
            var part = equipment.GetEquipped(PartCategory.Legs);
            var driver = part != null ? part.GetComponent<MechLegAnimator>() : null;
            if (driver == null) continue;
            driver.ApplyParameters(0.02f);
            driver.Animator.Update(0.02f);
        }
    }

    public static float MinimumVertexHeight(SkinnedMeshRenderer renderer)
    {
        var baked = new Mesh();
        renderer.BakeMesh(baked);
        float min = baked.vertices.Min(v => renderer.transform.TransformPoint(v).y);
        Object.DestroyImmediate(baked);
        return min;
    }
}
