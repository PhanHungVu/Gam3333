using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class MechHealthHudSetup
{
    public const string HudPath = "Assets/Prefabs/UI/Player Health HUD.prefab";
    public const string CursorPath = "Assets/Prefabs/Mech Cursor Canvas.prefab";
    public static readonly string[] ScenePaths = { "Assets/Scenes/A33.unity", "Assets/Scenes/Merge Test.unity", "Assets/Scenes/JhMapScene.unity", "Assets/Scenes/ZooScene.unity" };

    [MenuItem("Tools/Mech/UI/Set Up Shared Health And Movement Bars")]
    public static void CreateEditableHud()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs/UI")) AssetDatabase.CreateFolder("Assets/Prefabs", "UI");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(HudPath) == null)
        {
            var empty = new GameObject("Player Health HUD", typeof(RectTransform));
            try { PrefabUtility.SaveAsPrefabAsset(empty, HudPath); }
            finally { UnityEngine.Object.DestroyImmediate(empty); }
        }
        var root = PrefabUtility.LoadPrefabContents(HudPath);
        try
        {
            // Upgrade the old independent Canvas into a meter that shares its parent's Canvas.
            foreach (var component in root.GetComponents<CanvasScaler>()) UnityEngine.Object.DestroyImmediate(component);
            foreach (var component in root.GetComponents<Canvas>()) UnityEngine.Object.DestroyImmediate(component);
            RemoveChild(root, "Health Panel");
            var group = root.GetComponent<CanvasGroup>();
            if (group == null) group = root.AddComponent<CanvasGroup>();
            group.alpha = 1f; group.blocksRaycasts = false; group.interactable = false;
            ConfigureMeter(root, "Hp", new Vector2(28f, 124f));
            var hud = root.GetComponent<MechHealthHud>();
            if (hud == null) hud = root.AddComponent<MechHealthHud>();
            var data = new SerializedObject(hud);
            data.FindProperty("canvas").objectReferenceValue = null;
            data.FindProperty("visibility").objectReferenceValue = group;
            data.FindProperty("fill").objectReferenceValue = root.transform.Find("BarFront").GetComponent<Image>();
            data.FindProperty("glow").objectReferenceValue = root.transform.Find("Glow").GetComponent<Image>();
            data.FindProperty("label").objectReferenceValue = null;
            data.FindProperty("tmpLabel").objectReferenceValue = null;
            data.FindProperty("tintFillByHealth").boolValue = false;
            data.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, HudPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        var cursor = PrefabUtility.LoadPrefabContents(CursorPath);
        try
        {
            ConfigureMovement(cursor.GetComponentInChildren<MechEnergyHud>(true));
            var hud = cursor.GetComponentInChildren<MechHealthHud>(true);
            if (hud == null)
                hud = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath), cursor.transform)).GetComponent<MechHealthHud>();
            PlaceHealth(hud, cursor.transform);
            Bind(hud, cursor.GetComponent<Canvas>(), null);
            PrefabUtility.SaveAsPrefabAsset(cursor, CursorPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(cursor); }
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(CursorPath);
        var preview = new SerializedObject(asset.GetComponent<RectTransform>());
        preview.FindProperty("m_LocalScale").vector3Value = Vector3.one;
        preview.FindProperty("m_SizeDelta").vector2Value = new Vector2(1280f, 720f);
        preview.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.SavePrefabAsset(asset);
    }

    public static void PlaceInScene(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var player = roots.SelectMany(go => go.GetComponentsInChildren<MechPlayerRig>(true)).Single();
        var movement = roots.SelectMany(go => go.GetComponentsInChildren<MechEnergyHud>(true)).Single();
        ConfigureMovement(movement);
        var canvas = movement.GetComponentInParent<Canvas>();
        if (canvas == null) throw new InvalidOperationException("Movement HUD has no Canvas in " + scene.path);
        var hud = roots.SelectMany(go => go.GetComponentsInChildren<MechHealthHud>(true)).SingleOrDefault();
        if (hud == null)
            hud = ((GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(HudPath), scene)).GetComponent<MechHealthHud>();
        // Clear the old screen-sized Canvas transform overrides before reparenting.
        if (PrefabUtility.IsPartOfPrefabInstance(hud.transform))
            PrefabUtility.RevertObjectOverride(hud.transform, InteractionMode.AutomatedAction);
        PlaceHealth(hud, movement.transform.parent);
        Bind(hud, canvas, player.GetComponent<CombatHealth>());
        PrefabUtility.RecordPrefabInstancePropertyModifications(hud);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static void PlaceHealth(MechHealthHud hud, Transform parent)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(HudPath).GetComponent<RectTransform>();
        var rect = (RectTransform)hud.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax; rect.pivot = source.pivot;
        rect.sizeDelta = source.sizeDelta; rect.anchoredPosition3D = source.anchoredPosition3D;
        rect.localScale = source.localScale; rect.localRotation = source.localRotation;
        PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
    }

    static void Bind(MechHealthHud hud, Canvas canvas, CombatHealth health)
    {
        var data = new SerializedObject(hud);
        data.FindProperty("canvas").objectReferenceValue = canvas;
        data.FindProperty("health").objectReferenceValue = health;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureMovement(MechEnergyHud hud)
    {
        if (hud == null) throw new InvalidOperationException("Movement HUD is missing.");
        RemoveChild(hud.gameObject, "Energy Fill"); RemoveChild(hud.gameObject, "Energy Label");
        var oldBackground = hud.GetComponent<Image>();
        if (oldBackground != null) UnityEngine.Object.DestroyImmediate(oldBackground);
        ConfigureMeter(hud.gameObject, "Energy", new Vector2(28f, 42f));
        var data = new SerializedObject(hud);
        data.FindProperty("fill").objectReferenceValue = hud.transform.Find("BarFront").GetComponent<Image>();
        data.FindProperty("glow").objectReferenceValue = hud.transform.Find("Glow").GetComponent<Image>();
        data.FindProperty("label").objectReferenceValue = null;
        data.FindProperty("tintFillByEnergy").boolValue = false;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureMeter(GameObject root, string prefix, Vector2 position)
    {
        root.layer = 5;
        var frontSprite = Sprite(prefix + "Bar");
        float width = 360f, scale = width / frontSprite.rect.width;
        var size = frontSprite.rect.size * scale;
        var rect = root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.zero;
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
        rect.anchoredPosition = position; rect.sizeDelta = size;
        var glow = Layer(root, "Glow", Sprite(prefix + "BarGlow"));
        glow.transform.SetSiblingIndex(0);
        glow.rectTransform.anchorMin = glow.rectTransform.anchorMax = Vector2.zero;
        glow.rectTransform.pivot = new Vector2(0f, 0.5f);
        glow.rectTransform.sizeDelta = glow.sprite.rect.size * scale;
        glow.rectTransform.anchoredPosition = new Vector2((size.x - glow.rectTransform.sizeDelta.x) * 0.5f, size.y * 0.5f);
        glow.color = new Color(1f, 1f, 1f, 0.3f);
        var back = Layer(root, "BarBack", Sprite(prefix + "BarBack")); back.transform.SetSiblingIndex(1);
        var front = Layer(root, "BarFront", frontSprite); front.transform.SetSiblingIndex(2);
        foreach (var image in new[] { back, front })
        {
            image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = Vector2.zero;
            image.rectTransform.anchoredPosition = Vector2.zero; image.rectTransform.sizeDelta = size;
        }
        front.type = Image.Type.Filled; front.fillMethod = Image.FillMethod.Horizontal;
        front.fillOrigin = 0; front.fillAmount = 1f;
    }

    static Image Layer(GameObject root, string name, Sprite sprite)
    {
        var child = root.transform.Find(name);
        if (child == null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root.transform, false); child = go.transform;
        }
        child.gameObject.layer = 5;
        var image = child.GetComponent<Image>();
        image.sprite = sprite; image.color = Color.white; image.raycastTarget = false;
        image.type = Image.Type.Simple; child.localScale = Vector3.one;
        return image;
    }
    static Sprite Sprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/" + name + ".png")
        ?? throw new InvalidOperationException("Missing sprite " + name);
    static void RemoveChild(GameObject root, string name)
    {
        var child = root.transform.Find(name);
        if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
    }
    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        try
        {
            CreateEditableHud();
            foreach (string path in ScenePaths)
            {
                var scene = EditorSceneManager.OpenScene(path); PlaceInScene(scene); EditorSceneManager.SaveScene(scene);
                var health = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MechHealthHud>(true)).Single();
                var energy = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MechEnergyHud>(true)).Single();
                if (health.transform.parent != energy.transform.parent || health.Canvas != energy.GetComponentInParent<Canvas>())
                    throw new InvalidOperationException("Meters do not share the same Canvas in " + path);
                if (((RectTransform)health.transform).anchoredPosition != new Vector2(28f, 124f)
                    || ((RectTransform)energy.transform).anchoredPosition != new Vector2(28f, 42f))
                    throw new InvalidOperationException("Meter placement is incorrect in " + path);
                Debug.Log("SHARED METER SCENE PASS: " + path);
            }
            Debug.Log("PLAYER HEALTH HUD BUILD COMPLETE"); EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
