using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechUpperBodyChecks
{
    public static void Run(Action<bool, string> check)
    {
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath),
            new Vector3(36000, 1.6f, 0), Quaternion.identity);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.SetPositionAndRotation(new Vector3(36000, -0.5f, 0), Quaternion.identity);
        floor.transform.localScale = new Vector3(300, 1, 300);
        var view = new GameObject("Upper Body Check View");
        var reference = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.LegsModel));
        var equipment = player.GetComponent<EquipManager>();
        var motor = player.GetComponent<MechMovementController>();
        var presentation = player.GetComponent<MechUpperBodyAnimation>();
        var torso = equipment.GetEquipped(PartCategory.Torso);
        var head = equipment.GetEquipped(PartCategory.Head);
        var bodyReceiver = torso.GetComponent<MechPartPoseReceiver>();
        var headReceiver = head.GetComponent<MechPartPoseReceiver>();
        var torsoMesh = torso.transform.Find("Animated Torso Visual");
        var headMesh = head.transform.Find("Golem1Head");
        var socket = equipment.GetEquipped(PartCategory.Legs).transform.parent;
        var socketPosition = socket.localPosition;
        var socketRotation = socket.localRotation;
        Vector3 bodyRest = torsoMesh.localPosition, headRest = headMesh.localPosition;
        Quaternion bodyRestRotation = torsoMesh.localRotation, headRestRotation = headMesh.localRotation;
        try
        {
            TestMech.StopPlayerInput(motor);
            Physics.SyncTransforms();
            Step(player, view.transform, 60, Vector2.up);
            var driver = equipment.GetEquipped(PartCategory.Legs).GetComponent<MechLegAnimator>();
            check(presentation != null && bodyReceiver != null && headReceiver != null, "Current torso/head have independent visual pose receivers");
            check(driver.Animator.layerCount == 3, "Biped controller has leg, torso and head layers");
            CheckReconstruction(driver, reference, "Biped_Run", check);
            Sample(driver.Animator, 0.31f);
            Vector3 root = player.transform.position;
            Vector3 rigPosition = driver.Animator.transform.position;
            var leg = driver.Animator.transform.Find("Armature/Root/Leg.L");
            Vector3 footPosition = leg.GetChild(0).position;
            Vector3 torsoPosition = torso.transform.localPosition;
            presentation.Evaluate(1f);
            check(Vector3.Distance(torsoMesh.localPosition, bodyRest) > 0.02f || Quaternion.Angle(torsoMesh.localRotation, bodyRestRotation) > 1f,
                "Authored body curves visibly animate the existing torso mesh");
            check(Vector3.Distance(headMesh.localPosition, headRest) > 0.01f || Quaternion.Angle(headMesh.localRotation, headRestRotation) > 0.2f,
                "Authored head curves animate the existing head independently");
            check(player.transform.position == root && torso.transform.localPosition == torsoPosition && driver.Animator.transform.position == rigPosition
                && Vector3.Distance(footPosition, leg.GetChild(0).position) < 0.001f && socket.localPosition == socketPosition && socket.localRotation == socketRotation,
                "Body/head presentation never moves the motor, torso Part, leg socket, or evaluated feet");
            var shoulder = equipment.GetEquipped(PartCategory.ShoulderL).transform.parent;
            check(Quaternion.Angle(shoulder.localRotation, socketRotation) > 0.5f,
                "Upper weapon sockets inherit the body's authored rotation");
            var bodyPosition = torsoMesh.localPosition;
            var bodyRotation = torsoMesh.localRotation;
            var headPosition = headMesh.position;
            for (int i = 0; i < 300; i++) presentation.Evaluate(0.02f);
            check(Vector3.Distance(bodyPosition, torsoMesh.localPosition) < 0.0001f && Quaternion.Angle(bodyRotation, torsoMesh.localRotation) < 0.01f
                && Vector3.Distance(headPosition, headMesh.position) < 0.001f, "Repeated pose application does not accumulate drift or double body motion");

            headReceiver.Weight = 0f;
            presentation.Evaluate(0.1f);
            check(Vector3.Distance(headMesh.localPosition, headRest) < 0.0001f && Quaternion.Angle(headMesh.localRotation, headRestRotation) < 0.01f
                && Vector3.Distance(torsoMesh.localPosition, bodyRest) > 0.02f, "Head motion can be disabled while the body and legs continue");
            var bodyOnlyHead = headMesh.position;
            bodyReceiver.Weight = 0f;
            presentation.Evaluate(0.1f);
            check(Vector3.Distance(bodyOnlyHead, headMesh.position) > 0.02f, "Head inherits body motion once through its existing socket");
            headReceiver.Weight = 1f;
            presentation.Evaluate(0.1f);
            check(Vector3.Distance(torsoMesh.localPosition, bodyRest) < 0.0001f && Vector3.Distance(headMesh.localPosition, headRest) > 0.01f,
                "Head channel works independently with body weight zero");
            bodyReceiver.Weight = 1f;
            presentation.Evaluate(0.1f);

            float phase = driver.NormalizedPhase;
            equipment.Unequip(PartCategory.Head);
            check(Vector3.Distance(headMesh.localPosition, headRest) < 0.0001f, "Dropping an animated head restores its authored local pose");
            check(equipment.TryEquip(head), "Existing head re-equips through normal equipment");
            presentation.Evaluate(0.2f);
            check(Mathf.Abs(driver.NormalizedPhase - phase) < 0.0001f && Vector3.Distance(headMesh.localPosition, headRest) > 0.01f,
                "Head pickup resumes presentation without restarting the locomotion gait");

            check(player.GetComponent<MechLocomotionDebug>().TrySwap(1), "Heavy legs swap with upper-body presentation active");
            driver = equipment.GetEquipped(PartCategory.Legs).GetComponent<MechLegAnimator>();
            for (int layer = 1; layer < 3; layer++)
                check(Mathf.Abs(driver.Animator.GetCurrentAnimatorStateInfo(layer).normalizedTime - driver.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime) < 0.001f,
                    "Moving swap synchronizes upper layer " + layer + " with the leg gait");
            CheckReconstruction(driver, reference, "Heavy_Walk.001", check);
            Sample(driver.Animator, 0.43f);
            presentation.Evaluate(1f);
            check(Vector3.Distance(torsoMesh.localPosition, bodyRest) > 0.02f && Vector3.Distance(headMesh.localPosition, headRest) > 0.01f,
                "Heavy clip drives body and head as well as the working legs");
            var cannon = player.GetComponent<MechCombat>();
            var projectile = cannon.TryAttack(PartCategory.ShoulderL).Projectile;
            check(projectile != null && cannon.AmmoRemaining(PartCategory.ShoulderL) == 19, "Weapons still fire with animated attachment sockets");
            if (projectile != null) Object.DestroyImmediate(projectile.gameObject);

            presentation.enabled = false;
            check(Vector3.Distance(torsoMesh.localPosition, bodyRest) < 0.0001f && Vector3.Distance(headMesh.localPosition, headRest) < 0.0001f,
                "Disabling upper-body presentation restores the original model pose");
            presentation.enabled = true;
            presentation.Evaluate(1f);
            check(Vector3.Distance(torsoMesh.localPosition, bodyRest) > 0.02f, "Re-enabling presentation resumes the current rig pose");
            player.GetComponent<MechLocomotionDebug>().TrySwap(2);
            presentation.Evaluate(1f);
            check(Vector3.Distance(torsoMesh.localPosition, bodyRest) < 0.0001f && Vector3.Distance(headMesh.localPosition, headRest) < 0.0001f,
                "Modules without upper-body channels return safely to the neutral pose");
            player.GetComponent<MechLocomotionDebug>().TrySwap(0);
            Step(player, view.transform, 100, Vector2.zero);
            MechAnimationV2Checks.CheckNeutralPose(equipment.GetEquipped(PartCategory.Legs).GetComponent<MechLegAnimator>(), check);
        }
        finally
        {
            if (head != null && !head.transform.IsChildOf(player.transform)) Object.DestroyImmediate(head.gameObject);
            Object.DestroyImmediate(player); Object.DestroyImmediate(reference); Object.DestroyImmediate(view); Object.DestroyImmediate(floor);
        }
    }

    static void CheckReconstruction(MechLegAnimator driver, GameObject reference, string name, Action<bool, string> check)
    {
        // The original walking motion survives in Unity even when the GLB no longer contains it.
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(MechBipedAnimationImport.Folder + "/"
            + (name == "Heavy_Walk.001" ? "Heavy_Walk" : name) + ".anim");
        string[] bones = { "Armature/Root", "Armature/Root/Body", "Armature/Root/Body/Head", "Armature/Root/Leg.L", "Armature/Root/Leg.L/Foot.L",
            "Armature/Root/Leg.R", "Armature/Root/Leg.R/Foor.R" };
        foreach (float phase in new[] { 0.17f, 0.43f, 0.76f })
        {
            Sample(driver.Animator, phase);
            clip.SampleAnimation(reference, phase * clip.length);
            foreach (var path in bones)
            {
                var a = driver.Animator.transform.Find(path); var b = reference.transform.Find(path);
                check(Vector3.Distance(a.localPosition, b.localPosition) < 0.002f && Quaternion.Angle(a.localRotation, b.localRotation) < 0.15f,
                    name + " masked reconstruction matches source at " + phase + " for " + path);
            }
        }
    }

    static void Sample(Animator animator, float phase)
    {
        animator.SetFloat("MoveBlend", 1f);
        for (int i = 0; i < animator.layerCount; i++) animator.Play(Animator.StringToHash(animator.GetLayerName(i) + ".Locomotion"), i, phase);
        animator.Update(0f);
    }

    static void Step(GameObject player, Transform view, int count, Vector2 move)
    {
        var equipment = player.GetComponent<EquipManager>();
        var motor = player.GetComponent<MechMovementController>();
        var upper = player.GetComponent<MechUpperBodyAnimation>();
        for (int i = 0; i < count; i++)
        {
            motor.Tick(new MechLocomotionInput { move = move }, view, 0.02f);
            var driver = equipment.GetEquipped(PartCategory.Legs).GetComponent<MechLegAnimator>();
            driver.ApplyParameters(0.02f); driver.Animator.Update(0.02f); upper.Evaluate(0.02f);
        }
    }
}
