using System;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class MissionPauseMenuSetup
{
    public const string MenuPath = "Assets/Prefabs/UI/Mission Pause Menu.prefab";
    public const string CanvasPath = "Assets/Prefabs/Mech Cursor Canvas.prefab";
    [MenuItem("Tools/Mech/UI/Install Mission Pause Menu")]
    public static void Apply()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play Mode before editing HUD prefabs.");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath);
        if (prefab == null) prefab = Build();
        RepairPrefabLayout();
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPath);
        var canvas = PrefabUtility.LoadPrefabContents(CanvasPath);
        try
        {
            var existing = canvas.GetComponentsInChildren<MissionPauseMenu>(true);
            if (existing.Length > 1) throw new InvalidOperationException("The shared canvas contains multiple pause menus.");
            var menu = existing.Length == 0
                ? ((GameObject)PrefabUtility.InstantiatePrefab(prefab, canvas.transform)).GetComponent<MissionPauseMenu>() : existing[0];
            var rect = (RectTransform)menu.transform;
            Stretch(rect); rect.pivot = new Vector2(.5f, .5f); rect.localRotation = Quaternion.identity;
            var overlayCanvas = menu.GetComponent<Canvas>(); overlayCanvas.overrideSorting = true; overlayCanvas.sortingOrder = 1000;
            menu.transform.SetAsLastSibling();
            PrefabUtility.RecordPrefabInstancePropertyModifications(rect);
            PrefabUtility.RecordPrefabInstancePropertyModifications(overlayCanvas);
            PrefabUtility.SaveAsPrefabAsset(canvas, CanvasPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(canvas); }
        AssetDatabase.SaveAssets();
        Debug.Log("MISSION PAUSE MENU INSTALLED: linked prefab in Mech Cursor Canvas.");
    }
    static void RepairPrefabLayout()
    {
        // Root screen-space canvases serialize display-driven zero transforms
        // in headless authoring. Author this as a child canvas, its actual use.
        var root = PrefabUtility.LoadPrefabContents(MenuPath);
        GameObject staging = null;
        try
        {
            staging = new GameObject("Pause layout host", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(staging, root.scene);
            root.transform.SetParent(staging.transform, false);
            var rect = (RectTransform)root.transform; Stretch(rect); rect.pivot = new Vector2(.5f, .5f);
            var canvas = root.GetComponent<Canvas>(); canvas.overrideSorting = true; canvas.sortingOrder = 1000;
            PrefabUtility.SaveAsPrefabAsset(root, MenuPath);
        }
        finally
        {
            root.transform.SetParent(null, false);
            if (staging != null) Object.DestroyImmediate(staging);
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
    static GameObject Build()
    {
        var root = new GameObject("Mission Pause Menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.SetActive(false); root.layer = 5;
        try
        {
            Stretch((RectTransform)root.transform);
            var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true; canvas.sortingOrder = 1000;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            var menu = root.AddComponent<MissionPauseMenu>();
            var overlay = Rect("Pause Overlay", root.transform, Vector2.zero, Vector2.zero); Stretch(overlay);
            overlay.gameObject.AddComponent<Image>().color = new Color(.02f, .035f, .03f, .78f);
            var panel = Rect("Panel", overlay, Vector2.zero, new Vector2(600, 560));
            panel.gameObject.AddComponent<Image>().color = new Color(.12f, .16f, .145f, .99f);
            var accent = Rect("Accent", panel, new Vector2(0, 277), new Vector2(600, 6));
            accent.gameObject.AddComponent<Image>().color = new Color(.76f, .7f, .49f);
            Label("Title", panel, "PAUSED", new Vector2(0, 192), new Vector2(530, 80), 56);
            Label("Subtitle", panel, "Take a moment.", new Vector2(0, 128), new Vector2(530, 44), 25);
            var resume = MakeButton("Resume", panel, 34);
            var restart = MakeButton("Restart Mission", panel, -56);
            var exit = MakeButton("Exit Mission", panel, -146);
            Label("Hint", panel, "ESC TO RESUME", new Vector2(0, -233), new Vector2(530, 32), 20);
            UnityEventTools.AddPersistentListener(resume.onClick, menu.Resume);
            UnityEventTools.AddPersistentListener(restart.onClick, menu.RestartMission);
            UnityEventTools.AddPersistentListener(exit.onClick, menu.ExitMission);
            var fields = new SerializedObject(menu);
            fields.FindProperty("panel").objectReferenceValue = overlay.gameObject;
            fields.FindProperty("resumeButton").objectReferenceValue = resume;
            fields.ApplyModifiedPropertiesWithoutUndo();
            overlay.gameObject.SetActive(false); root.SetActive(true);
            return PrefabUtility.SaveAsPrefabAsset(root, MenuPath);
        }
        finally { Object.DestroyImmediate(root); }
    }
    static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.gameObject.layer = 5;
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = position; rect.sizeDelta = size; return rect;
    }
    static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one; }
    static TMP_Text Label(string name, Transform parent, string copy, Vector2 position, Vector2 size, float fontSize)
    {
        var text = Rect(name, parent, position, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/century-gothic-bold-4_ufonts.asset") ?? TMP_Settings.defaultFontAsset;
        text.text = copy; text.fontSize = fontSize; text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(.94f, .91f, .79f); text.raycastTarget = false;
        return text;
    }
    static Button MakeButton(string name, Transform parent, float y)
    {
        var rect = Rect(name, parent, new Vector2(0, y), new Vector2(450, 70));
        var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.25f, .32f, .28f);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        var colors = button.colors;
        colors.highlightedColor = colors.selectedColor = new Color(1.45f, 1.4f, 1.2f);
        colors.pressedColor = new Color(.8f, .86f, .75f); colors.fadeDuration = .08f; button.colors = colors;
        var navigation = button.navigation; navigation.mode = Navigation.Mode.Vertical; button.navigation = navigation;
        Label("Label", rect, name, Vector2.zero, new Vector2(420, 60), 30);
        return button;
    }
}
