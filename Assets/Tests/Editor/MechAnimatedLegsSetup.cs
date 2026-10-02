using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechAnimatedLegsSetup
{
    public const string Source = "Assets/Anim/golem2_v2.glb";
    public const string PlayerPath = "Assets/Prefabs/Mech/Base/FullPlayer_mech_base.prefab";
    public const string Folder = "Assets/Prefabs/Mech/Locomotion";
    const string AnimationFolder = "Assets/Anim/Locomotion";

    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch project only.");
        if (!AssetDatabase.IsValidFolder(AnimationFolder)) AssetDatabase.CreateFolder("Assets/Anim", "Locomotion");
        var imported = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        MechBipedAnimationImport.RefreshClips();
        var fastClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(MechBipedAnimationImport.Folder + "/Biped_Run.anim");
        var heavyClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(MechBipedAnimationImport.Folder + "/Heavy_Walk.anim");
        var player = PrefabUtility.LoadPrefabContents(PlayerPath);
        var oldLegs = player.GetComponentsInChildren<Part>(true).Single(p => p.partType == PartCategory.Legs);
        var oldModel = oldLegs.GetComponent<MechLegAnimator>() != null
            ? oldLegs.transform.Find("BlenderVisual")
            : oldLegs.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Golem1Legs").transform;
        Vector3 anchorPosition = oldLegs.transform.InverseTransformPoint(oldModel.position);
        Quaternion anchorRotation = Quaternion.Inverse(oldLegs.transform.rotation) * oldModel.rotation;
        Vector3 anchorScale = Divide(oldModel.lossyScale, oldLegs.transform.lossyScale);
        var model = Object.Instantiate(imported);
        var idle = BuildIdle(model, fastClip);
        Object.DestroyImmediate(model);
        var fastData = ConfigureData("Assets/PartCatalogue/PartData_Legs_Fast.asset", "Legs_Fast", "Fast Legs");
        var heavyData = ConfigureData("Assets/PartCatalogue/PartData_Legs_Slow.asset", "Legs_Heavy", "Heavy Legs");
        var fast = BuildModule("BipedFast_Module", imported, fastData, BuildController("Biped_Fast", idle, fastClip),
            6f, anchorPosition, anchorRotation, anchorScale, oldLegs);
        var heavy = BuildModule("BipedHeavy_Module", imported, heavyData, BuildController("Biped_Heavy", idle, heavyClip),
            3f, anchorPosition, anchorRotation, anchorScale, oldLegs);
        var socket = oldLegs.transform.parent;
        Object.DestroyImmediate(oldLegs.gameObject);
        var starting = (GameObject)PrefabUtility.InstantiatePrefab(fast.gameObject, socket);
        starting.transform.localPosition = Vector3.zero;
        starting.transform.localRotation = Quaternion.identity;
        starting.transform.localScale = Vector3.one;
        starting.GetComponent<Part>().isStartingPart = true;
        // Match the actual authored VisualMech scale. 1.5 was expanding all parts
        // on Awake, sinking the old legs and moving weapons away from their joints.
        player.GetComponent<EquipManager>().partScale = 0.5f;
        var debug = new SerializedObject(player.GetComponent<MechLocomotionDebug>());
        var choices = debug.FindProperty("modulePrefabs");
        choices.arraySize = 4;
        choices.GetArrayElementAtIndex(0).objectReferenceValue = fast;
        choices.GetArrayElementAtIndex(1).objectReferenceValue = heavy;
        choices.GetArrayElementAtIndex(2).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/HoverModule.prefab").GetComponent<Part>();
        choices.GetArrayElementAtIndex(3).objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/TankModule.prefab").GetComponent<Part>();
        debug.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SaveAsPrefabAsset(player, PlayerPath);
        PrefabUtility.UnloadPrefabContents(player);
        AddPickup("Assets/Scenes/A33.unity", heavy);
        AddPickup("Assets/Scenes/Merge Test.unity", heavy);
        AssetDatabase.SaveAssets();
        Debug.Log("ANIMATED LEGS BUILD COMPLETE");
        EditorApplication.Exit(0);
    }

    static PartData ConfigureData(string path, string id, string name)
    {
        var data = AssetDatabase.LoadAssetAtPath<PartData>(path);
        data.partId = id;
        data.partName = name;
        EditorUtility.SetDirty(data);
        return data;
    }

    static AnimationClip BuildIdle(GameObject model, AnimationClip source)
    {
        const string path = AnimationFolder + "/Biped_Stand.anim";
        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (idle == null) { idle = new AnimationClip(); AssetDatabase.CreateAsset(idle, path); }
        idle.ClearCurves();
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            var bone = model.transform.Find(binding.path);
            if (bone == null || !AnimationUtility.GetFloatValue(model, binding, out float value))
                throw new InvalidOperationException("Cannot resolve animation binding: " + binding.path + "/" + binding.propertyName);
            AnimationUtility.SetEditorCurve(idle, binding, AnimationCurve.Constant(0f, 1f, value));
        }
        var settings = AnimationUtility.GetAnimationClipSettings(idle);
        settings.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(idle, settings);
        idle.name = "Biped_Stand";
        EditorUtility.SetDirty(idle);
        return idle;
    }

    static AnimatorController BuildController(string name, AnimationClip idle, AnimationClip move)
    {
        string path = AnimationFolder + "/" + name + ".controller";
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        foreach (var p in controller.parameters) controller.RemoveParameter(0);
        controller.AddParameter("MoveBlend", AnimatorControllerParameterType.Float);
        controller.AddParameter("PlaybackSpeed", AnimatorControllerParameterType.Float);
        var machine = controller.layers[0].stateMachine;
        var locomotion = machine.states.Select(s => s.state).SingleOrDefault(s => s.name == "Locomotion") ?? machine.AddState("Locomotion");
        var tree = locomotion.motion as BlendTree;
        if (tree == null)
        {
            tree = new BlendTree { name = name + " Idle Move" };
            AssetDatabase.AddObjectToAsset(tree, controller);
        }
        tree.blendType = BlendTreeType.Simple1D;
        tree.blendParameter = "MoveBlend";
        tree.useAutomaticThresholds = false;
        tree.children = new ChildMotion[0];
        tree.AddChild(idle, 0f);
        tree.AddChild(move, 1f);
        locomotion.motion = tree;
        locomotion.speedParameter = "PlaybackSpeed";
        locomotion.speedParameterActive = true;
        machine.defaultState = locomotion;
        EditorUtility.SetDirty(controller);
        return controller;
    }

    static Part BuildModule(string name, GameObject imported, PartData data, AnimatorController controller,
        float speed, Vector3 position, Quaternion rotation, Vector3 scale, Part oldLegs)
    {
        var root = new GameObject(name);
        var part = root.AddComponent<Part>();
        part.partType = PartCategory.Legs;
        part.data = data;
        part.isStartingPart = false;
        var locomotion = root.AddComponent<BipedLocomotionModule>();
        var tuning = new SerializedObject(locomotion);
        tuning.FindProperty("usePartSpeedRating").boolValue = true;
        tuning.ApplyModifiedPropertiesWithoutUndo();
        var wrapper = new GameObject("BlenderVisual").transform;
        wrapper.SetParent(root.transform, false);
        wrapper.localPosition = position;
        wrapper.localRotation = rotation;
        wrapper.localScale = scale;
        var rig = (GameObject)PrefabUtility.InstantiatePrefab(imported, wrapper);
        rig.transform.localPosition = Vector3.zero;
        rig.transform.localRotation = Quaternion.identity;
        rig.transform.localScale = Vector3.one;
        foreach (var r in rig.GetComponentsInChildren<Renderer>(true)) r.enabled = r is SkinnedMeshRenderer && r.name == "Golem1Legs";
        // The animated export retains UVs but omits the original base-color texture.
        var originalLegs = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Golem1Models/Golem1Legs.glb");
        rig.GetComponentInChildren<SkinnedMeshRenderer>().sharedMaterials = originalLegs.GetComponentInChildren<MeshRenderer>().sharedMaterials;
        var animator = rig.GetComponent<Animator>();
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var presentation = root.AddComponent<MechLegAnimator>();
        var binding = new SerializedObject(presentation);
        binding.FindProperty("animator").objectReferenceValue = animator;
        binding.FindProperty("referenceSpeed").floatValue = speed;
        binding.ApplyModifiedPropertiesWithoutUndo();
        var collider = wrapper.gameObject.AddComponent<BoxCollider>();
        var mesh = rig.GetComponentInChildren<SkinnedMeshRenderer>().sharedMesh;
        collider.center = mesh.bounds.center + Vector3.up * 0.22936456f;
        collider.size = mesh.bounds.size;
        var joints = oldLegs.GetComponentsInChildren<GolemJointGlow>(true);
        string[] bones = { "Armature/Root/Leg.L", "Armature/Root/Leg.R" };
        for (int i = 0; i < Mathf.Min(joints.Length, bones.Length); i++)
        {
            var effect = Object.Instantiate(joints[i].gameObject, rig.transform.Find(bones[i]));
            effect.name = "Leg Joint VFX";
            effect.transform.localPosition = Vector3.zero;
            effect.transform.localRotation = Quaternion.identity;
        }
        var saved = PrefabUtility.SaveAsPrefabAsset(root, Folder + "/" + name + ".prefab");
        Object.DestroyImmediate(root);
        return saved.GetComponent<Part>();
    }

    static void AddPickup(string path, Part prefab)
    {
        var scene = EditorSceneManager.OpenScene(path);
        if (GameObject.Find("Heavy Legs Pickup") == null)
        {
            var owner = Object.FindAnyObjectByType<EquipManager>();
            var pickup = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, scene);
            pickup.name = "Heavy Legs Pickup";
            pickup.transform.position = owner.transform.position + new Vector3(4f, 2f, 2f);
            pickup.transform.localScale = Vector3.one * owner.partScale;
        }
        EditorSceneManager.SaveScene(scene);
    }

    static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(a.x / b.x, a.y / b.y, a.z / b.z);

    public static void InspectBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch project only.");
        var imported = AssetDatabase.LoadAssetAtPath<GameObject>(Source);
        foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>())
        {
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Debug.Log("RIG CLIP " + clip.name + " length=" + clip.length + " legacy=" + clip.legacy
                + " loop=" + clip.isLooping + " bindings=" + string.Join(";", bindings.Select(x => x.path).Distinct()));
        }
        var model = Object.Instantiate(imported);
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
            Debug.Log("RIG NODE " + AnimationUtility.CalculateTransformPath(t, model.transform) + " pos=" + t.localPosition
                + " rot=" + t.localEulerAngles + " scale=" + t.localScale);
        foreach (var a in model.GetComponentsInChildren<Animator>(true))
            Debug.Log("RIG ANIMATOR " + AnimationUtility.CalculateTransformPath(a.transform, model.transform) + " avatar=" + a.avatar);
        foreach (var r in model.GetComponentsInChildren<Renderer>(true)) Debug.Log("RIG RENDERER " + r.name + " " + r.bounds);
        Object.DestroyImmediate(model);

        var player = PrefabUtility.LoadPrefabContents(PlayerPath);
        var equipment = player.GetComponent<EquipManager>();
        var parts = player.GetComponentsInChildren<Part>(true).OrderBy(p => p.partType == PartCategory.Torso ? 0 : 1).ToArray();
        var sockets = player.GetComponentsInChildren<Socket>(true);
        foreach (var part in parts)
        {
            var socket = sockets.FirstOrDefault(s => s.socketType == part.partType);
            typeof(EquipManager).GetMethod("Place", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(equipment, new object[] { socket, part });
            Debug.Log("PLAYER PART " + part.partType + " pos=" + part.transform.position + " rot=" + part.transform.eulerAngles + " scale=" + part.transform.lossyScale);
            foreach (var r in part.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponentInParent<Part>() == part))
                Debug.Log("PLAYER RENDERER " + part.partType + "/" + r.name + " bounds=" + r.bounds + " localPos=" + r.transform.localPosition + " worldScale=" + r.transform.lossyScale);
        }
        PrefabUtility.UnloadPrefabContents(player);
        Debug.Log("ANIMATED LEGS INSPECTION COMPLETE");
        EditorApplication.Exit(0);
    }
}
