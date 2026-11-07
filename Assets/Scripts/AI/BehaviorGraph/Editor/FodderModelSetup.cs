using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

public static class FodderModelSetup
{
    public const string FodderPath = "Assets/Prefabs/Enemies/Small Enemies/Enemy_Fodder.prefab";
    public const string CrossbowPath = "Assets/Prefabs/Enemies/Small Enemies/Enemy_crossbow.prefab";
    public const string ModelPath = "Assets/Models/Specialist/ENEMY_Fodder.fbx";
    public const string ControllerPath = "Assets/Anim/Specialist/Enemy_Fodder.controller";
    static AnimationClip Clip(string name) => AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>().Single(c => c.name == "Rig_Enemy_Fodder|Fodder_" + name);
    [MenuItem("Tools/Mech/Enemies/Connect Zoo Fodder Model and Animations")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before authoring enemy assets.");
        BuildController();
        var fodder = PrefabUtility.LoadPrefabContents(FodderPath);
        try
        {
            ConfigureFodder(fodder);
            PrefabUtility.SaveAsPrefabAsset(fodder, FodderPath);
            var legacy = PrefabUtility.LoadPrefabContents(CrossbowPath);
            try
            {
                // Keep the legacy root and root component IDs: every mission/scene still resolves its prefab reference.
                foreach (Transform child in legacy.transform.Cast<Transform>().ToArray()) Object.DestroyImmediate(child.gameObject);
                legacy.transform.position = Vector3.zero;
                foreach (Transform child in fodder.transform)
                {
                    var clone = Object.Instantiate(child.gameObject, legacy.transform, false); clone.name = child.name;
                }
                foreach (var source in fodder.GetComponents<MonoBehaviour>())
                {
                    var target = legacy.GetComponent(source.GetType()) ?? legacy.AddComponent(source.GetType());
                    EditorUtility.CopySerialized(source, target);
                    RemapReferences(target, fodder.transform, legacy.transform);
                }
                PrefabUtility.SaveAsPrefabAsset(legacy, CrossbowPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(legacy); }
        }
        finally { PrefabUtility.UnloadPrefabContents(fodder); }
        AssetDatabase.SaveAssets();
        Debug.Log("FODDER MODEL READY: Zoo body/cannon, full presentation, legacy small-crossbow references preserved.");
    }
    static void BuildController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.layers = new AnimatorControllerLayer[0];
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ControllerPath)) if (asset != null && asset != controller) Object.DestroyImmediate(asset, true);
        controller.parameters = new AnimatorControllerParameter[0];
        foreach (string name in new[] { "MoveBlend", "PlaybackSpeed", "MoveX", "MoveZ" }) controller.AddParameter(name, AnimatorControllerParameterType.Float);
        controller.AddLayer("Base Layer");
        var layer = controller.layers[0]; layer.defaultWeight = 1; controller.layers = new[] { layer };
        var machine = layer.stateMachine;
        var tree = new BlendTree { name = "Fodder Directional Locomotion", blendType = BlendTreeType.FreeformCartesian2D, blendParameter = "MoveX", blendParameterY = "MoveZ", useAutomaticThresholds = false };
        AssetDatabase.AddObjectToAsset(tree, controller);
        tree.AddChild(Clip("Idle"), Vector2.zero);
        tree.AddChild(Clip("Walk"), new Vector2(0, .5f)); tree.AddChild(Clip("Jog"), Vector2.up);
        tree.AddChild(Clip("StrafeWalk_L"), new Vector2(-.5f, 0)); tree.AddChild(Clip("StrafeWalk_R"), new Vector2(.5f, 0));
        tree.AddChild(Clip("StrafeJog_L"), Vector2.left); tree.AddChild(Clip("StrafeJog_R"), Vector2.right);
        // There is no authored backpedal clip: reverse the walk cycle without turning the actor away from its target.
        var children = tree.children.ToList(); children.Add(new ChildMotion { motion = Clip("Walk"), position = new Vector2(0, -.5f), timeScale = -1 }); tree.children = children.ToArray();
        var locomotion = machine.AddState("Locomotion"); locomotion.motion = tree; machine.defaultState = locomotion;
        State(EnemyFodderAnimation.Pose.ChargeStart, "CannonChargeStart", .3f);
        State(EnemyFodderAnimation.Pose.ChargeLoop, "CannonChargeLoop");
        State(EnemyFodderAnimation.Pose.Fire, "CannonFire");
        State(EnemyFodderAnimation.Pose.HitFront, "HitReact_Front", .33f); State(EnemyFodderAnimation.Pose.HitBack, "HitReact_Back", .33f);
        State(EnemyFodderAnimation.Pose.HitLeft, "HitReact_Left", .33f); State(EnemyFodderAnimation.Pose.HitRight, "HitReact_Right", .33f);
        void State(EnemyFodderAnimation.Pose pose, string clipName, float seconds = 0)
        {
            var state = machine.AddState(EnemyFodderAnimation.StateName(pose)); var clip = Clip(clipName); state.motion = clip;
            state.speed = seconds > 0 ? clip.length / seconds : 1;
        }
        EditorUtility.SetDirty(controller);
    }
    static void ConfigureFodder(GameObject root)
    {
        root.transform.position = Vector3.zero;
        var animator = root.GetComponentsInChildren<Animator>(true).Single(a => a.name == "ENEMY_Fodder");
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath); animator.applyRootMotion = false;
        Clip("Idle").SampleAnimation(animator.gameObject, 0);
        var visual = root.transform.Find("VisualMech");
        var bodyBounds = BodyBounds(root);
        float pivotHeight = root.GetComponent<EnemyAgent>().PivotHeight;
        visual.position += Vector3.up * (-pivotHeight - root.transform.TransformPoint(new Vector3(0, bodyBounds.min.y, 0)).y);
        bodyBounds = BodyBounds(root);
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            if (collider.GetComponentInParent<Part>() == null) Object.DestroyImmediate(collider);
        var hitbox = root.transform.Find("Fodder Hurtbox");
        if (hitbox == null) { hitbox = new GameObject("Fodder Hurtbox").transform; hitbox.SetParent(root.transform, false); }
        hitbox.gameObject.layer = LayerMask.NameToLayer("EnemyBody");
        var box = hitbox.gameObject.AddComponent<BoxCollider>(); box.center = bodyBounds.center; box.size = Vector3.Max(bodyBounds.size, Vector3.one * .1f);
        var target = new SerializedObject(root.GetComponent<MechTargetable>()); var aim = root.transform.Find("Aim"); aim.localPosition = bodyBounds.center + Vector3.up * bodyBounds.extents.y * .25f;
        target.FindProperty("aimPoint").objectReferenceValue = aim; target.ApplyModifiedPropertiesWithoutUndo();
        var motion = new SerializedObject(root.GetComponent<EnemyAnimation>()); motion.FindProperty("animator").objectReferenceValue = animator;
        motion.FindProperty("referenceSpeed").floatValue = root.GetComponent<EnemyAgent>().MaxSpeed; motion.ApplyModifiedPropertiesWithoutUndo();
        var presentation = root.GetComponent<EnemyFodderAnimation>() ?? root.AddComponent<EnemyFodderAnimation>();
        var fields = new SerializedObject(presentation); fields.FindProperty("animator").objectReferenceValue = animator; fields.ApplyModifiedPropertiesWithoutUndo();
    }
    static Bounds BodyBounds(GameObject root)
    {
        bool found = false; Bounds bounds = default;
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null || filter.GetComponentInParent<Part>() != null) continue;
            var meshBounds = filter.sharedMesh.bounds; var matrix = root.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var point = matrix.MultiplyPoint3x4(meshBounds.center + Vector3.Scale(meshBounds.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; } else bounds.Encapsulate(point);
            }
        }
        if (!found) throw new InvalidOperationException("Fodder model has no body mesh bounds.");
        return bounds;
    }
    static void RemapReferences(Component component, Transform source, Transform target)
    {
        var fields = new SerializedObject(component); var property = fields.GetIterator();
        while (property.Next(true))
        {
            if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
            var reference = property.objectReferenceValue;
            Transform original = reference is GameObject go ? go.transform : reference is Component part ? part.transform : null;
            if (original == null || original != source && !original.IsChildOf(source)) continue;
            string path = AnimationUtility.CalculateTransformPath(original, source); var copy = path.Length == 0 ? target : target.Find(path);
            if (copy == null) throw new InvalidOperationException("Cannot remap Fodder reference: " + path);
            property.objectReferenceValue = reference is GameObject ? (Object)copy.gameObject : copy.GetComponent(reference.GetType());
        }
        fields.ApplyModifiedPropertiesWithoutUndo();
    }
    public static void RunBatch()
    {
        try { Apply(); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
