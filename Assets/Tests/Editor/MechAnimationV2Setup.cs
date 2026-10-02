using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechAnimationV2Setup
{
    public const string Source = "Assets/Anim/golem2_v2.glb";

    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch copy only.");
        MechBipedAnimationImport.RefreshClips();
        var imported = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        foreach (var name in new[] { "Fast", "Heavy" })
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Anim/Locomotion/Biped_" + name + ".controller");
            ConfigureController(controller, Clip(name == "Fast" ? "Biped_Run" : "Heavy_Walk"), name);
            string path = MechAnimatedLegsSetup.Folder + "/Biped" + name + "_Module.prefab";
            var module = PrefabUtility.LoadPrefabContents(path);
            var oldAnimator = module.GetComponentInChildren<Animator>(true);
            var rig = (GameObject)PrefabUtility.InstantiatePrefab(imported, oldAnimator.transform.parent);
            rig.transform.SetLocalPositionAndRotation(oldAnimator.transform.localPosition, oldAnimator.transform.localRotation);
            rig.transform.localScale = oldAnimator.transform.localScale;
            foreach (var renderer in rig.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = renderer is SkinnedMeshRenderer && renderer.name == "Golem1Legs";
            rig.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials = oldAnimator.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials;
            foreach (var effect in oldAnimator.GetComponentsInChildren<GolemJointGlow>(true))
            {
                var parent = rig.transform.Find(AnimationUtility.CalculateTransformPath(effect.transform.parent, oldAnimator.transform));
                var copy = Object.Instantiate(effect.gameObject, parent);
                copy.name = effect.name;
                copy.transform.SetLocalPositionAndRotation(effect.transform.localPosition, effect.transform.localRotation);
                copy.transform.localScale = effect.transform.localScale;
            }
            var animator = rig.GetComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var driver = new SerializedObject(module.GetComponent<MechLegAnimator>());
            driver.FindProperty("animator").objectReferenceValue = animator;
            driver.FindProperty("boostStopClip").objectReferenceValue = MechBipedAnimationImport.ActionClip(name, "BoostStop");
            driver.ApplyModifiedPropertiesWithoutUndo();
            var pose = new SerializedObject(module.GetComponent<MechAnimationPoseSource>());
            pose.FindProperty("animator").objectReferenceValue = animator;
            SetChannel(pose.FindProperty("body"), rig.transform.Find("Armature/Root/Body"));
            SetChannel(pose.FindProperty("head"), rig.transform.Find("Armature/Root/Body/Head"));
            pose.ApplyModifiedPropertiesWithoutUndo();
            Object.DestroyImmediate(oldAnimator.gameObject);
            PrefabUtility.SaveAsPrefabAsset(module, path);
            PrefabUtility.UnloadPrefabContents(module);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("BIPED V2 SETUP COMPLETE");
        EditorApplication.Exit(0);
    }

    static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(MechBipedAnimationImport.Folder + "/" + name + ".anim");

    // Upgrade existing modules in place without replacing their rig, materials or VFX.
    public static void UpgradeFamilyActionsBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch copy only.");
        MechBipedAnimationImport.RefreshClips();
        foreach (string family in new[] { "Fast", "Heavy" })
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>("Assets/Anim/Locomotion/Biped_" + family + ".controller");
            foreach (var layer in controller.layers)
            {
                foreach (string state in new[] { "Neutral", "Boost", "BoostStop", "Jump" })
                    layer.stateMachine.states.Single(s => s.state.name == state).state.motion = MechBipedAnimationImport.ActionClip(family, state);
                var tree = (BlendTree)layer.stateMachine.states.Single(s => s.state.name == "Locomotion").state.motion;
                var children = tree.children;
                children[0].motion = MechBipedAnimationImport.ActionClip(family, "Neutral");
                tree.children = children;
                EditorUtility.SetDirty(tree);
            }
            EditorUtility.SetDirty(controller);
            string path = MechAnimatedLegsSetup.Folder + "/Biped" + family + "_Module.prefab";
            var module = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var driver = new SerializedObject(module.GetComponent<MechLegAnimator>());
                driver.FindProperty("boostStopClip").objectReferenceValue = MechBipedAnimationImport.ActionClip(family, "BoostStop");
                driver.ApplyModifiedPropertiesWithoutUndo();
                // New exports may include weapon meshes. Existing equipped parts own
                // those visuals; this nested model supplies only the legs and pose bones.
                var animator = module.GetComponentInChildren<Animator>(true);
                foreach (var renderer in animator.GetComponentsInChildren<Renderer>(true))
                    if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                        renderer.enabled = renderer is SkinnedMeshRenderer && renderer.name == "Golem1Legs";
                PrefabUtility.SaveAsPrefabAsset(module, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(module); }
        }
        AssetDatabase.SaveAssets();
        Debug.Log("BIPED FAMILY ACTIONS UPGRADE COMPLETE");
        EditorApplication.Exit(0);
    }

    static void ConfigureController(AnimatorController controller, AnimationClip walk, string family)
    {
        if (controller.layers.Length != 3) throw new InvalidOperationException("Generate the upper-body masked layers before v2 actions.");
        foreach (var layer in controller.layers)
        {
            var machine = layer.stateMachine;
            var locomotion = machine.states.Single(s => s.state.name == "Locomotion").state;
            var tree = (BlendTree)locomotion.motion;
            tree.children = new[] { new ChildMotion { motion = MechBipedAnimationImport.ActionClip(family, "Neutral"), threshold = 0f, timeScale = 1f },
                new ChildMotion { motion = walk, threshold = 1f, timeScale = 1f } };
            locomotion.writeDefaultValues = true;
            foreach (var stateName in new[] { "Neutral", "Boost", "BoostStop", "Jump" })
            {
                var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName) ?? machine.AddState(stateName);
                state.motion = MechBipedAnimationImport.ActionClip(family, stateName);
                state.writeDefaultValues = true;
                state.speed = 1f;
                state.speedParameterActive = false;
                if (stateName == "Neutral") machine.defaultState = state;
            }
            EditorUtility.SetDirty(tree);
        }
        EditorUtility.SetDirty(controller);
    }

    static void SetChannel(SerializedProperty channel, Transform bone)
    {
        channel.FindPropertyRelative("bone").objectReferenceValue = bone;
        channel.FindPropertyRelative("restPosition").vector3Value = bone.localPosition;
        channel.FindPropertyRelative("restRotation").quaternionValue = bone.localRotation;
    }

    public static void InspectBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch copy only.");
        foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>())
        {
            Debug.Log("V2 CLIP " + clip.name + " length=" + clip.length + " loop=" + clip.isLooping + " legacy=" + clip.legacy);
            if (clip.name != "Boost" && clip.name != "BoostStop") continue;
            foreach (var binding in AnimationUtility.GetCurveBindings(clip).Where(b => b.propertyName.StartsWith("m_LocalPosition")))
            {
                var c = AnimationUtility.GetEditorCurve(clip, binding);
                Debug.Log("V2 CURVE " + clip.name + " " + binding.path + "/" + binding.propertyName
                    + " first=" + c.keys.First().value + " last=" + c.keys.Last().value
                    + " min=" + c.keys.Min(k => k.value) + " max=" + c.keys.Max(k => k.value));
            }
        }
        Debug.Log("V2 INSPECTION COMPLETE");
        EditorApplication.Exit(0);
    }
}
