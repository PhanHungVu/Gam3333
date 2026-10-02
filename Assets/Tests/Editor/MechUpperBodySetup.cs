using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechUpperBodySetup
{
    const string AnimationFolder = "Assets/Anim/Locomotion";

    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch copy only.");
        var imported = AssetDatabase.LoadAssetAtPath<GameObject>(MechAnimatedLegsSetup.Source);
        var paths = imported.GetComponentsInChildren<Transform>(true)
            .Select(t => AnimationUtility.CalculateTransformPath(t, imported.transform)).ToArray();
        var masks = new[] { BuildMask("Legs", paths), BuildMask("Torso", paths), BuildMask("Head", paths) };
        foreach (var name in new[] { "Fast", "Heavy" })
        {
            ConfigureLayers(AssetDatabase.LoadAssetAtPath<AnimatorController>(AnimationFolder + "/Biped_" + name + ".controller"), masks);
            string path = MechAnimatedLegsSetup.Folder + "/Biped" + name + "_Module.prefab";
            var module = PrefabUtility.LoadPrefabContents(path);
            var animator = module.GetComponentInChildren<Animator>(true);
            var source = module.GetComponent<MechAnimationPoseSource>() ?? module.AddComponent<MechAnimationPoseSource>();
            var data = new SerializedObject(source);
            data.FindProperty("animator").objectReferenceValue = animator;
            SetChannel(data.FindProperty("body"), animator.transform.Find("Armature/Root/Body"));
            SetChannel(data.FindProperty("head"), animator.transform.Find("Armature/Root/Body/Head"));
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(module, path);
            PrefabUtility.UnloadPrefabContents(module);
        }
        var player = PrefabUtility.LoadPrefabContents(MechAnimatedLegsSetup.PlayerPath);
        var parts = player.GetComponentsInChildren<Part>(true);
        var torso = parts.Single(p => p.partType == PartCategory.Torso);
        var head = parts.Single(p => p.partType == PartCategory.Head);
        var rig = parts.Single(p => p.partType == PartCategory.Legs).GetComponentInChildren<Animator>(true).transform;
        var original = torso.GetComponent<MeshRenderer>();
        var visual = torso.transform.Find("Animated Torso Visual");
        if (visual == null)
        {
            visual = new GameObject("Animated Torso Visual").transform;
            visual.SetParent(torso.transform, false);
            visual.gameObject.AddComponent<MeshFilter>().sharedMesh = torso.GetComponent<MeshFilter>().sharedMesh;
            EditorUtility.CopySerialized(original, visual.gameObject.AddComponent<MeshRenderer>());
        }
        visual.GetComponent<MeshRenderer>().enabled = true;
        original.enabled = false;
        var bodyTargets = torso.GetComponentsInChildren<Socket>(true)
            .Where(s => s.transform.parent == torso.transform && s.socketType != PartCategory.Legs).Select(s => s.transform)
            .Prepend(visual).ToArray();
        ConfigureReceiver(torso, rig.Find("Armature/Root/Body"), bodyTargets);
        // EquipManager snaps the head Part to its socket at runtime. Calibrate in
        // that pose, then retain the user's authored prefab placement for editing.
        var savedPosition = head.transform.localPosition;
        var savedRotation = head.transform.localRotation;
        var savedScale = head.transform.localScale;
        head.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        head.transform.localScale = Vector3.one;
        ConfigureReceiver(head, rig.Find("Armature/Root/Body/Head"), head.transform.Cast<Transform>().ToArray());
        head.transform.SetLocalPositionAndRotation(savedPosition, savedRotation);
        head.transform.localScale = savedScale;
        if (player.GetComponent<MechUpperBodyAnimation>() == null) player.AddComponent<MechUpperBodyAnimation>();
        PrefabUtility.SaveAsPrefabAsset(player, MechAnimatedLegsSetup.PlayerPath);
        PrefabUtility.UnloadPrefabContents(player);
        AssetDatabase.SaveAssets();
        Debug.Log("UPPER BODY BUILD COMPLETE");
        EditorApplication.Exit(0);
    }

    static void SetChannel(SerializedProperty channel, Transform bone)
    {
        channel.FindPropertyRelative("bone").objectReferenceValue = bone;
        channel.FindPropertyRelative("restPosition").vector3Value = bone.localPosition;
        channel.FindPropertyRelative("restRotation").quaternionValue = bone.localRotation;
    }

    static void ConfigureReceiver(Part part, Transform bone, Transform[] targets)
    {
        var receiver = part.GetComponent<MechPartPoseReceiver>() ?? part.gameObject.AddComponent<MechPartPoseReceiver>();
        var data = new SerializedObject(receiver);
        data.FindProperty("pivotPosition").vector3Value = part.transform.InverseTransformPoint(bone.position);
        data.FindProperty("pivotRotation").quaternionValue = Quaternion.Inverse(part.transform.rotation) * bone.rotation;
        var array = data.FindProperty("targets");
        array.arraySize = targets.Length;
        for (int i = 0; i < targets.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = targets[i];
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    static AvatarMask BuildMask(string region, string[] paths)
    {
        string path = AnimationFolder + "/Mask_" + region + ".mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(path);
        if (mask == null) { mask = new AvatarMask { name = "Mask_" + region }; AssetDatabase.CreateAsset(mask, path); }
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, false);
        mask.transformCount = paths.Length;
        for (int i = 0; i < paths.Length; i++)
        {
            mask.SetTransformPath(i, paths[i]);
            bool active = region == "Legs" ? paths[i].StartsWith("Armature/Root/Leg.", StringComparison.Ordinal)
                : paths[i] == (region == "Torso" ? "Armature/Root/Body" : "Armature/Root/Body/Head");
            mask.SetTransformActive(i, active);
        }
        EditorUtility.SetDirty(mask);
        return mask;
    }

    static void ConfigureLayers(AnimatorController controller, AvatarMask[] masks)
    {
        string[] names = { "Base Layer", "Torso", "Head" };
        for (int i = controller.layers.Length; i < names.Length; i++) controller.AddLayer(names[i]);
        var layers = controller.layers;
        var baseState = layers[0].stateMachine.defaultState;
        for (int i = 0; i < names.Length; i++)
        {
            layers[i].avatarMask = masks[i];
            layers[i].defaultWeight = 1f;
            layers[i].blendingMode = AnimatorLayerBlendingMode.Override;
            if (i == 0) continue;
            var machine = layers[i].stateMachine;
            var state = machine.defaultState ?? machine.AddState("Locomotion");
            if (state.motion == null)
            {
                var tree = Object.Instantiate((BlendTree)baseState.motion);
                tree.name = names[i] + " Idle Move";
                AssetDatabase.AddObjectToAsset(tree, controller);
                state.motion = tree;
            }
            state.speedParameter = "PlaybackSpeed";
            state.speedParameterActive = true;
            machine.defaultState = state;
        }
        controller.layers = layers;
        EditorUtility.SetDirty(controller);
    }

    public static void InspectBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch copy only.");
        var player = PrefabUtility.LoadPrefabContents(MechAnimatedLegsSetup.PlayerPath);
        foreach (var t in player.GetComponentsInChildren<Transform>(true))
        {
            if (t.GetComponent<ParticleSystem>() != null || t.name.Contains("Glow")) continue;
            if (t.GetComponentInParent<MechLegAnimator>() != null) continue;
            Debug.Log("UPPER NODE " + AnimationUtility.CalculateTransformPath(t, player.transform)
                + " pos=" + t.localPosition + " rot=" + t.localEulerAngles + " scale=" + t.localScale
                + " components=" + string.Join(",", t.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType().Name)));
        }
        var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MechAnimatedLegsSetup.Source));
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
            Debug.Log("POSE NODE " + AnimationUtility.CalculateTransformPath(t, model.transform) + " pos=" + t.localPosition
                + " rot=" + t.localEulerAngles + " scale=" + t.localScale);
        foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(MechAnimatedLegsSetup.Source).OfType<AnimationClip>())
        foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(b => b.path.EndsWith("Body") || b.path.EndsWith("Head")))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            Debug.Log("POSE CURVE " + clip.name + " " + binding.path + "/" + binding.propertyName
                + " min=" + curve.keys.Min(k => k.value) + " max=" + curve.keys.Max(k => k.value));
        }
        Object.DestroyImmediate(model);
        PrefabUtility.UnloadPrefabContents(player);
        Debug.Log("UPPER BODY INSPECTION COMPLETE");
        EditorApplication.Exit(0);
    }
}
