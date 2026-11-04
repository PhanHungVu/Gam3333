using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class PreMissionQuestDetailsSlabSetup
{
    public const string PrefabPath = "Assets/Prefabs/UI/PreMission/QuestDescSlabP.prefab";
    public const string ControllerPath = "Assets/Anim/Premission/QuestDetailsRuntime.controller";
    const string Clips = "Assets/Anim/Premission/";
    [MenuItem("Tools/Mech/Pre-Mission/Use Animated 3D Quest Details")]
    public static void UpgradeAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before updating the quest UI.");
        var controller = Controller();
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            root.transform.localPosition = Vector3.zero;
            var animator = root.GetComponentInChildren<Animator>(true);
            animator.runtimeAnimatorController = controller;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            var slab = animator.transform;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PreMissionPortraitSlabSetup.FontPath);
            var title = ConfigureText(slab.Find("TitleText").GetComponent<TMP_Text>(), font, new Vector3(0, .73f, 2.2f), new Vector2(5.1f, .7f), 4.4f, 2.6f, TextAlignmentOptions.Center);
            var type = ConfigureText(slab.Find("Quest Type Text").GetComponent<TMP_Text>(), font, new Vector3(0, .73f, 1.62f), new Vector2(5.1f, .42f), 2.5f, 1.6f, TextAlignmentOptions.Center);
            var description = ConfigureText(slab.Find("DescriptionText").GetComponent<TMP_Text>(), font, new Vector3(0, .73f, .04f), new Vector2(5.05f, 2.65f), 2.6f, 1.5f, TextAlignmentOptions.TopLeft);
            var accept = Action(slab.Find("AcceptBtn"), font, "Accept");
            var decline = Action(slab.Find("declineBtn"), font, "Decline");
            var presenter = root.GetComponent<PreMissionQuestDetailsSlab>();
            if (presenter == null) presenter = root.AddComponent<PreMissionQuestDetailsSlab>();
            var fields = new SerializedObject(presenter);
            fields.FindProperty("animator").objectReferenceValue = animator;
            fields.FindProperty("animatedSlab").objectReferenceValue = slab;
            fields.FindProperty("fallClip").objectReferenceValue = Clip("questDescSlabAnimFall");
            fields.FindProperty("titleText").objectReferenceValue = title;
            fields.FindProperty("questTypeText").objectReferenceValue = type;
            fields.FindProperty("descriptionText").objectReferenceValue = description;
            fields.FindProperty("acceptAction").objectReferenceValue = accept;
            fields.FindProperty("declineAction").objectReferenceValue = decline;
            fields.FindProperty("tabletopHeight").floatValue = 2.4f;
            fields.ApplyModifiedPropertiesWithoutUndo();
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 26;
            Clip("questDescSlabAnimIdle").SampleAnimation(slab.gameObject, 0);
            Clip("questDescAcceptBtnIdle").SampleAnimation(slab.gameObject, 0);
            Clip("questDescDeclineBtnIdle").SampleAnimation(slab.gameObject, 0);
            var bounds = MeshBounds(slab);
            fields.Update(); fields.FindProperty("restingBounds").boundsValue = bounds; fields.ApplyModifiedPropertiesWithoutUndo();
            Clip("questDescSlabAnimPreFall").SampleAnimation(slab.gameObject, 0);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
    }
    [MenuItem("Tools/Mech/Pre-Mission/Connect Animated Details in Current Scene")]
    public static void ConnectCurrentScene()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before connecting the quest UI.");
        var scene = SceneManager.GetActiveScene();
        var flow = Object.FindObjectsByType<PreMissionFlowController>().FirstOrDefault(f => f.gameObject.scene == scene);
        if (flow == null) throw new InvalidOperationException("Open the pre-mission scene first.");
        var flowFields = new SerializedObject(flow);
        var board = (PreMissionStoneBoard)flowFields.FindProperty("stoneBoard").objectReferenceValue;
        if (board == null) throw new InvalidOperationException("Assign the existing stone board first.");
        var slab = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PreMissionQuestDetailsSlab>(true)).FirstOrDefault();
        if (slab == null) slab = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene)).GetComponent<PreMissionQuestDetailsSlab>();
        slab.transform.position = board.transform.TransformPoint(new Vector3(7.16f, 1.1f, 0));
        flowFields.FindProperty("questDetailsSlab").objectReferenceValue = slab; flowFields.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(flow);
        var camera = board.BoardCamera;
        var raycaster = camera.GetComponent<PhysicsRaycaster>();
        if (raycaster == null) raycaster = camera.gameObject.AddComponent<PhysicsRaycaster>();
        raycaster.eventMask = 1 << 26; raycaster.maxRayIntersections = 16;
        EditorSceneManager.MarkSceneDirty(scene);
    }
    static AnimatorController Controller()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        string[][] names = {
            new[] { "questDescSlabAnimPreFall", "questDescSlabAnimFall", "questDescSlabAnimIdle" },
            new[] { "questDescAcceptBtnIdle", "questDescAcceptBtnHighlight", "questDescAcceptBtnHIdle" },
            new[] { "questDescDeclineBtnIdle", "questDescDeclineBtnHighlight", "questDescDeclineBtnHIdle" }
        };
        while (controller.layers.Length < 3) controller.AddLayer(controller.layers.Length == 1 ? "Accept" : "Decline");
        var layers = controller.layers;
        for (int i = 0; i < 3; i++)
        {
            var machine = layers[i].stateMachine; var states = new AnimatorState[3];
            for (int n = 0; n < 3; n++)
            {
                states[n] = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == names[i][n]) ?? machine.AddState(names[i][n], new Vector3(n * 240, 60));
                states[n].motion = Clip(names[i][n]); states[n].writeDefaultValues = false;
                foreach (var transition in states[n].transitions) states[n].RemoveTransition(transition);
            }
            machine.defaultState = states[0]; layers[i].defaultWeight = 1;
            var finish = states[1].AddTransition(states[2]); finish.hasExitTime = true; finish.exitTime = 1; finish.duration = 0; finish.hasFixedDuration = true;
        }
        controller.layers = layers; EditorUtility.SetDirty(controller); return controller;
    }
    static AnimationClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AnimationClip>(Clips + name + ".anim") ?? throw new InvalidOperationException("Missing authored clip: " + name);
    static TMP_Text ConfigureText(TMP_Text text, TMP_FontAsset font, Vector3 position, Vector2 size, float max, float min, TextAlignmentOptions alignment)
    {
        text.font = font; text.fontSharedMaterial = font.material; text.margin = Vector4.zero;
        text.rectTransform.sizeDelta = size; text.rectTransform.pivot = new Vector2(.5f, .5f);
        text.transform.localRotation = Quaternion.Euler(90, 0, 0); text.transform.localPosition = position;
        text.color = new Color(.95f, .92f, .82f); text.enableAutoSizing = true;
        text.fontSize = text.fontSizeMax = max; text.fontSizeMin = min;
        text.alignment = alignment; text.overflowMode = TextOverflowModes.Ellipsis; text.raycastTarget = false;
        return text;
    }
    static PreMissionQuestSlabAction Action(Transform button, TMP_FontAsset font, string label)
    {
        var bounds = MeshBounds(button);
        var hit = button.GetComponent<BoxCollider>();
        if (hit == null) hit = button.gameObject.AddComponent<BoxCollider>();
        hit.center = bounds.center; hit.size = bounds.size + new Vector3(.16f, .1f, .16f);
        var action = button.GetComponent<PreMissionQuestSlabAction>();
        if (action == null) action = button.gameObject.AddComponent<PreMissionQuestSlabAction>();
        var text = button.GetComponentInChildren<TMP_Text>(true);
        ConfigureText(text, font, new Vector3(bounds.center.x, bounds.max.y + .04f, bounds.center.z), new Vector2(bounds.size.x * .86f, bounds.size.z * .75f), 3.1f, 2, TextAlignmentOptions.Center);
        text.text = label;
        return action;
    }
    static Bounds MeshBounds(Transform root)
    {
        var bounds = new Bounds(); bool found = false;
        foreach (var mesh in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mesh.sharedMesh == null || mesh.GetComponent<TMP_Text>() != null) continue;
            var local = mesh.sharedMesh.bounds; var matrix = root.worldToLocalMatrix * mesh.transform.localToWorldMatrix;
            var b = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
            for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
                b.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3(x, y, z))));
            if (!found) { bounds = b; found = true; } else bounds.Encapsulate(b);
        }
        if (!found) throw new InvalidOperationException("Missing geometry in " + root.name);
        return bounds;
    }
}
