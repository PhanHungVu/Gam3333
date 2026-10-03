using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

public static class MechHealthHudSetup
{
    public const string HudPath = "Assets/Prefabs/UI/Player Health HUD.prefab";

    public static readonly string[] ScenePaths = { "Assets/Scenes/A33.unity", "Assets/Scenes/Merge Test.unity", "Assets/Scenes/JhMapScene.unity" };

    [MenuItem("Tools/Mech/UI/Create Standalone Player Health HUD")]
    public static void CreateEditableHud()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(HudPath) == null)
        {
            var view = BuildView();
            try { PrefabUtility.SaveAsPrefabAsset(view, HudPath); }
            finally { UnityEngine.Object.DestroyImmediate(view); }
        }
        var viewRoot = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            var hud = viewRoot.GetComponent<MechHealthHud>() ?? viewRoot.AddComponent<MechHealthHud>();
            var data = new SerializedObject(hud);
            AssignIfMissing(data, "canvas", viewRoot.GetComponent<Canvas>());
            AssignIfMissing(data, "fill", viewRoot.transform.Find("Health Panel/Health Track/Health Fill")?.GetComponent<Image>());
            if (data.FindProperty("tmpLabel").objectReferenceValue == null)
                AssignIfMissing(data, "label", viewRoot.transform.Find("Health Panel/Health Readout")?.GetComponent<Text>());
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(viewRoot, HudPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(viewRoot); }
        SaveAssetPreview(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath), "");
        var root = PrefabUtility.LoadPrefabContents(MechAnimatedLegsSetup.PlayerPath);
        try
        {
            if (root.GetComponent<CombatHealth>() == null) throw new InvalidOperationException("Player health is missing.");
            var hud = root.GetComponent<MechHealthHud>();
            if (hud != null) UnityEngine.Object.DestroyImmediate(hud);
            var ui = root.transform.Find("Player Health HUD");
            if (ui != null) UnityEngine.Object.DestroyImmediate(ui.gameObject);
            PrefabUtility.SaveAsPrefabAsset(root, MechAnimatedLegsSetup.PlayerPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    public static void PlaceInScene(Scene scene)
    {
        var player = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<MechPlayerRig>(true)).Single();
        var hud = scene.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<MechHealthHud>(true)).FirstOrDefault();
        if (hud == null)
            hud = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath), scene)).GetComponent<MechHealthHud>();
        hud.transform.SetParent(null, false);
        var data = new SerializedObject(hud);
        data.FindProperty("health").objectReferenceValue = player.GetComponent<CombatHealth>();
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static void SaveAssetPreview(GameObject asset, string child)
    {
        var rect = (child.Length == 0 ? asset.transform : asset.transform.Find(child)) as RectTransform;
        if (rect == null || rect.localScale != Vector3.zero && rect.sizeDelta != Vector2.zero) return;
        // Edit the persistent prefab after saving. Its Canvas isn't in a live scene,
        // so driven screen dimensions cannot overwrite these authored preview values.
        var data = new SerializedObject(rect);
        data.FindProperty("m_LocalScale").vector3Value = Vector3.one;
        data.FindProperty("m_SizeDelta").vector2Value = new Vector2(1280f, 720f);
        data.FindProperty("m_Pivot").vector2Value = new Vector2(0.5f, 0.5f);
        data.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SavePrefabAsset(asset);
    }

    static void AssignIfMissing(SerializedObject data, string property, UnityEngine.Object value)
    {
        if (data.FindProperty(property).objectReferenceValue == null) data.FindProperty(property).objectReferenceValue = value;
    }

    static GameObject BuildView()
    {
        var root = new GameObject("Player Health HUD", typeof(RectTransform));
        root.layer = 5;
        // Set the authored rectangle before Canvas starts driving it. Unity saves
        // the original values of driven properties, rather than their live values.
        SetPreviewRect(root.GetComponent<RectTransform>());
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f); scaler.matchWidthOrHeight = 0.5f;
        var panel = Image("Health Panel", root.transform, new Color(0.03f, 0.05f, 0.07f, 0.85f));
        var rect = panel.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -24f); rect.sizeDelta = new Vector2(300f, 78f);
        var text = new GameObject("Health Readout", typeof(RectTransform), typeof(Text), typeof(Shadow));
        text.layer = 5;
        text.transform.SetParent(panel.transform, false);
        var label = text.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = 19;
        label.fontStyle = FontStyle.Bold; label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false;
        label.text = "HEALTH  100 / 100";
        var textRect = label.rectTransform;
        textRect.anchorMin = new Vector2(0f, 0.5f); textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14f, 0f); textRect.offsetMax = new Vector2(-14f, -8f);
        text.GetComponent<Shadow>().effectDistance = new Vector2(1f, -1f);
        var track = Image("Health Track", panel.transform, new Color(0.15f, 0.18f, 0.2f));
        var trackRect = track.rectTransform;
        trackRect.anchorMin = trackRect.anchorMax = Vector2.zero; trackRect.pivot = Vector2.zero;
        trackRect.anchoredPosition = new Vector2(14f, 14f); trackRect.sizeDelta = new Vector2(272f, 18f);
        var fill = Image("Health Fill", track.transform, new Color(0.3f, 0.95f, 0.5f));
        fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        SetPreviewRect(root.GetComponent<RectTransform>());
        return root;
    }

    static void SetPreviewRect(RectTransform rect)
    {
        // A headless editor has no screen dimensions. Save a useful editor preview
        // instead of the zero-sized transform Canvas initially drives there.
        rect.localScale = Vector3.one;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(1280f, 720f);
    }

    static Image Image(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.layer = 5; go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
    }

    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        try
        {
            CreateEditableHud();
            foreach (string path in ScenePaths)
            {
                var scene = EditorSceneManager.OpenScene(path);
                PlaceInScene(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Debug.Log("PLAYER HEALTH HUD BUILD COMPLETE"); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
