using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechEnemyCombatSetup
{
    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        try
        {
            var player = PrefabUtility.LoadPrefabContents(MechAnimatedLegsSetup.PlayerPath);
            try
            {
                if (player.GetComponent<CombatHealth>() == null) player.AddComponent<CombatHealth>();
                if (player.GetComponent<MechStats>() == null) player.AddComponent<MechStats>();
                PrefabUtility.SaveAsPrefabAsset(player, MechAnimatedLegsSetup.PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            foreach (string family in new[] { "mace", "crossbow" })
            {
                string path = "Assets/Prefabs/Enemies/Enemy_" + family + ".prefab";
                var enemy = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var attack = enemy.GetComponent<EnemyAttack>() ?? enemy.AddComponent<EnemyAttack>();
                    var weapons = enemy.GetComponentsInChildren<MechWeaponAnimator>(true);
                    if (weapons.Length != 2) throw new InvalidOperationException("Expected two existing weapon parts on " + path);
                    foreach (var weapon in weapons)
                    {
                        var part = weapon.GetComponent<Part>();
                        part.isEquipped = true; part.isStartingPart = false;
                        if (part.muzzle == null)
                        {
                            var bone = weapon.Animator.transform.Find("CrossBowArmRig/WeapRoot/CrossBowArmBody");
                            if (bone != null)
                            {
                                var muzzle = new GameObject("Enemy Muzzle").transform;
                                muzzle.SetParent(bone, false);
                                var mesh = bone.GetComponentInChildren<MeshFilter>().sharedMesh;
                                var meshTransform = bone.GetComponentInChildren<MeshFilter>().transform;
                                var front = mesh.bounds.center + Vector3.forward * mesh.bounds.extents.z;
                                muzzle.position = meshTransform.TransformPoint(front);
                                part.muzzle = muzzle;
                            }
                        }
                    }
                    var data = new SerializedObject(attack);
                    data.FindProperty("kind").enumValueIndex = family == "mace" ? 0 : 1;
                    data.FindProperty("damage").floatValue = family == "mace" ? 20f : 12f;
                    data.FindProperty("cooldown").floatValue = family == "mace" ? 1.8f : 2f;
                    data.FindProperty("fireDuration").floatValue = 0.7f;
                    data.FindProperty("projectileMaterial").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Material>("Assets/Combat/Prototype Combat.mat");
                    var array = data.FindProperty("weapons"); array.arraySize = weapons.Length;
                    for (int i = 0; i < weapons.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = weapons[i];
                    data.ApplyModifiedPropertiesWithoutUndo();
                    var agentData = new SerializedObject(enemy.GetComponent<EnemyAgent>());
                    agentData.FindProperty("attackRange").floatValue = family == "mace" ? 2.2f : 9f;
                    agentData.ApplyModifiedPropertiesWithoutUndo();
                    if (enemy.GetComponent<EnemyAnimation>() == null) BuildLocomotion(enemy);
                    PrefabUtility.SaveAsPrefabAsset(enemy, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(enemy); }
            }
            AssetDatabase.SaveAssets();
            Debug.Log("ENEMY COMBAT BUILD COMPLETE"); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static void BuildLocomotion(GameObject enemy)
    {
        var oldLeg = enemy.GetComponentsInChildren<MeshFilter>(true).Single(m => m.sharedMesh.name == "Golem1LegsMesh");
        var wrapper = new GameObject("Enemy Biped Rig"); wrapper.transform.SetParent(oldLeg.transform.parent, false);
        var rig = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MechAnimatedLegsSetup.Source), wrapper.transform);
        rig.name = "BlenderVisual";
        var reference = rig.GetComponentsInChildren<SkinnedMeshRenderer>(true).Single(r => r.name == "Golem1Legs");
        var matrix = oldLeg.transform.localToWorldMatrix * reference.transform.worldToLocalMatrix * wrapper.transform.localToWorldMatrix;
        wrapper.transform.rotation = matrix.rotation;
        var parentScale = wrapper.transform.parent.lossyScale;
        var scale = matrix.lossyScale;
        wrapper.transform.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
        wrapper.transform.position = matrix.GetColumn(3);
        reference.sharedMaterials = oldLeg.GetComponent<MeshRenderer>().sharedMaterials;
        foreach (var renderer in rig.GetComponentsInChildren<Renderer>(true)) renderer.enabled = renderer == reference;
        oldLeg.GetComponent<MeshRenderer>().enabled = false;
        var animator = rig.GetComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>("Assets/Anim/Locomotion/Biped_Fast.controller");
        animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var part = wrapper.AddComponent<Part>(); part.isEquipped = true; part.isStartingPart = false;
        var poses = wrapper.AddComponent<MechAnimationPoseSource>();
        var poseData = new SerializedObject(poses);
        poseData.FindProperty("animator").objectReferenceValue = animator;
        SetChannel(poseData.FindProperty("body"), rig.transform.Find("Armature/Root/Body"));
        SetChannel(poseData.FindProperty("head"), rig.transform.Find("Armature/Root/Body/Head"));
        poseData.ApplyModifiedPropertiesWithoutUndo();
        var torso = enemy.GetComponentsInChildren<MeshFilter>(true).Single(m => m.sharedMesh.name == "Golem1TorsoMesh" && !m.transform.IsChildOf(rig.transform));
        var visual = new GameObject("Animated Enemy Torso"); visual.transform.SetParent(torso.transform, false);
        visual.AddComponent<MeshFilter>().sharedMesh = torso.sharedMesh;
        EditorUtility.CopySerialized(torso.GetComponent<MeshRenderer>(), visual.AddComponent<MeshRenderer>());
        torso.GetComponent<MeshRenderer>().enabled = false;
        var targets = torso.transform.Cast<Transform>().Where(t => t == visual.transform || t.name.Contains("socket") && !t.name.Contains("legs")).ToArray();
        var body = Receiver(torso.transform, rig.transform.Find("Armature/Root/Body"), targets);
        var headMesh = enemy.GetComponentsInChildren<MeshFilter>(true).Single(m => m.sharedMesh.name == "Golem1HeadMesh" && !m.transform.IsChildOf(rig.transform));
        var head = Receiver(headMesh.transform.parent, rig.transform.Find("Armature/Root/Body/Head"), new[] { headMesh.transform });
        var animation = enemy.AddComponent<EnemyAnimation>();
        var data = new SerializedObject(animation);
        data.FindProperty("animator").objectReferenceValue = animator;
        data.FindProperty("poses").objectReferenceValue = poses;
        data.FindProperty("body").objectReferenceValue = body;
        data.FindProperty("head").objectReferenceValue = head;
        data.ApplyModifiedPropertiesWithoutUndo();
    }
    static void SetChannel(SerializedProperty channel, Transform bone)
    {
        channel.FindPropertyRelative("bone").objectReferenceValue = bone;
        channel.FindPropertyRelative("restPosition").vector3Value = bone.localPosition;
        channel.FindPropertyRelative("restRotation").quaternionValue = bone.localRotation;
    }
    static MechPartPoseReceiver Receiver(Transform owner, Transform bone, Transform[] targets)
    {
        var part = owner.GetComponent<Part>() ?? owner.gameObject.AddComponent<Part>(); part.isEquipped = true; part.isStartingPart = false;
        var receiver = owner.GetComponent<MechPartPoseReceiver>() ?? owner.gameObject.AddComponent<MechPartPoseReceiver>();
        var data = new SerializedObject(receiver);
        data.FindProperty("pivotPosition").vector3Value = owner.InverseTransformPoint(bone.position);
        data.FindProperty("pivotRotation").quaternionValue = Quaternion.Inverse(owner.rotation) * bone.rotation;
        var array = data.FindProperty("targets"); array.arraySize = targets.Length;
        for (int i = 0; i < targets.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
        data.ApplyModifiedPropertiesWithoutUndo();
        return receiver;
    }
    public static void InspectBatch()
    {
        foreach (var path in new[] { "Assets/Prefabs/Enemies/Enemy_mace.prefab", "Assets/Prefabs/Enemies/Enemy_crossbow.prefab", MechAnimatedLegsSetup.Source })
        {
            var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            foreach (var mesh in model.GetComponentsInChildren<Renderer>(true).Where(r => r is MeshRenderer || r is SkinnedMeshRenderer))
                Debug.Log("ENEMY MESH " + path + " path=" + AnimationUtility.CalculateTransformPath(mesh.transform, model.transform)
                    + " mesh=" + (mesh is SkinnedMeshRenderer s ? s.sharedMesh.name : mesh.GetComponent<MeshFilter>().sharedMesh.name)
                    + " pos=" + mesh.transform.position + " rot=" + mesh.transform.eulerAngles + " scale=" + mesh.transform.lossyScale
                    + " bounds=" + mesh.bounds);
            Object.DestroyImmediate(model);
        }
        EditorApplication.Exit(0);
    }
}
