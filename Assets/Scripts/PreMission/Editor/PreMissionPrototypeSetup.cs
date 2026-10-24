using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit creation tool only: nothing runs automatically on import or editor startup.
public static class PreMissionPrototypeSetup
{
    public const string ScenePath = "Assets/Scenes/PreMissionScene.unity";
    public const string CataloguePath = "Assets/PreMission/PrototypeBoardCatalogue.asset";
    public const string PrefabPath = "Assets/Prefabs/UI/PreMission/PreMissionUI.prefab";
    public const string NoticePath = "Assets/Prefabs/UI/PreMission/QuestNotice.prefab";
    static readonly Color Ink = new Color(0.15f, 0.13f, 0.11f);
    static readonly Color Paper = new Color(0.84f, 0.79f, 0.66f);
    static TMP_FontAsset font;

    [MenuItem("Tools/Mech/Pre-Mission/Create Prototype")]
    public static void CreatePrototype()
    {
        if (File.Exists(ScenePath))
        {
            Debug.Log("Pre-mission prototype already exists. Open " + ScenePath + "; existing UI/assets were preserved.");
            return;
        }
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Directory.CreateDirectory("Assets/PreMission/Quests");
        Directory.CreateDirectory("Assets/Prefabs/UI/PreMission");
        AssetDatabase.Refresh();
        // Opening a scene can unload assets held only in local variables. Load UI/data after it.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/century-gothic-bold-4_ufonts.asset");
        if (font == null) font = TMP_Settings.defaultFontAsset;
        var catalogue = CreateCatalogue();
        var notice = CreateNoticePrefab();
        var camera = new GameObject("UI Camera", typeof(Camera)).GetComponent<Camera>();
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 0f, -10f);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.065f, 0.075f, 0.075f);
        camera.orthographic = true;
        var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        ConfigureInput(events.GetComponent<InputSystemUIInputModule>());

        var root = new GameObject("PreMission UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600, 900);
        scaler.matchWidthOrHeight = 0.5f;
        var hook = root.AddComponent<MissionStartHook>();
        var loader = root.AddComponent<SceneManagerScript>();
        var controller = root.AddComponent<PreMissionFlowController>();
        var fields = new SerializedObject(controller);
        Set(fields, "catalogue", catalogue); Set(fields, "missionStartHook", hook);
        fields.ApplyModifiedPropertiesWithoutUndo();
        var exitEvent = new UnityEngine.Events.UnityEvent();
        UnityEventTools.AddStringPersistentListener(exitEvent, loader.LoadScene, "MainMenuScene");
        var eventField = typeof(PreMissionFlowController).GetField("exitToMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        eventField.SetValue(controller, exitEvent);
        fields.Update();

        var board = Panel("Quest Board View", root.transform, new Color(0.19f, 0.18f, 0.15f));
        var details = Panel("Quest Details View", root.transform, Paper);
        var torso = Panel("Starting Torso View", root.transform, new Color(0.78f, 0.81f, 0.75f));
        foreach (var view in new[] { board, details, torso }) Place(view, 0.04f, 0.06f, 0.96f, 0.94f);
        Set(fields, "boardView", board.gameObject); Set(fields, "detailsView", details.gameObject); Set(fields, "torsoView", torso.gameObject);

        Text("Board Heading", board, "QUEST BOARD", 42, Color.white, 0.04f, 0.85f, 0.96f, 0.96f);
        Set(fields, "boardMessage", Text("Board Message", board, "", 21, new Color(0.85f, 0.84f, 0.79f), 0.04f, 0.76f, 0.96f, 0.85f));
        var noticeParent = Rect("Offers", board);
        Place(noticeParent, 0.04f, 0.21f, 0.96f, 0.74f);
        var grid = noticeParent.gameObject.AddComponent<GridLayoutGroup>();
        grid.spacing = new Vector2(20, 20);
        noticeParent.gameObject.AddComponent<PreMissionNoticeLayout>();
        Set(fields, "noticeParent", noticeParent); Set(fields, "noticePrefab", notice);
        Set(fields, "exitButton", Button("Back to Main Menu", board, 0.04f, 0.05f, 0.29f, 0.14f));
        Text("Board Hint", board, "Select a notice to read its briefing.", 18, new Color(0.7f, 0.7f, 0.65f), 0.38f, 0.05f, 0.96f, 0.14f);

        Set(fields, "detailsTitle", Text("Mission Title", details, "Quest details", 36, Ink, 0.06f, 0.83f, 0.94f, 0.96f));
        Set(fields, "detailsBody", ScrollBody(details));
        Set(fields, "declineButton", Button("Decline / Back", details, 0.06f, 0.05f, 0.35f, 0.15f));
        Set(fields, "acceptButton", Button("Accept Quest", details, 0.65f, 0.05f, 0.94f, 0.15f));

        Text("Torso Heading", torso, "STARTING TORSO", 38, Ink, 0.06f, 0.84f, 0.94f, 0.96f);
        Set(fields, "torsoQuestTitle", Text("Selected Quest", torso, "", 22, Ink, 0.06f, 0.75f, 0.94f, 0.84f));
        var preview = Panel("Torso Preview Placeholder", torso, new Color(0.64f, 0.68f, 0.62f));
        Place(preview, 0.35f, 0.40f, 0.65f, 0.71f);
        Text("Preview Label", preview, "TORSO\n\nPreview placeholder", 25, Ink, 0.05f, 0.05f, 0.95f, 0.95f);
        Set(fields, "torsoName", Text("Torso Name", torso, "", 29, Ink, 0.20f, 0.32f, 0.80f, 0.40f));
        Set(fields, "torsoDescription", Text("Torso Description", torso, "", 20, Ink, 0.12f, 0.19f, 0.88f, 0.32f));
        Set(fields, "previousTorsoButton", Button("<", torso, 0.20f, 0.49f, 0.29f, 0.61f));
        Set(fields, "nextTorsoButton", Button(">", torso, 0.71f, 0.49f, 0.80f, 0.61f));
        Set(fields, "torsoBackButton", Button("Back to Details", torso, 0.06f, 0.04f, 0.31f, 0.13f));
        Set(fields, "confirmTorsoButton", Button("Confirm Torso", torso, 0.69f, 0.04f, 0.94f, 0.13f));
        Set(fields, "launchStatus", Text("Launch Status", torso, "", 17, Ink, 0.33f, 0.02f, 0.67f, 0.16f));
        fields.ApplyModifiedPropertiesWithoutUndo();
        details.gameObject.SetActive(false); torso.gameObject.SetActive(false);
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        PrefabUtility.InstantiatePrefab(prefab);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddMenuEntry();
        RegisterBuildScene();
        AssetDatabase.SaveAssets();
        PreMissionPresentationSetup.UpgradeAssets();
        StarterTorsoSetup.UpgradeAssets();
        EditorSceneManager.OpenScene(ScenePath);
        Debug.Log("Q1 PRE-MISSION PROTOTYPE CREATED: " + ScenePath);
    }

    public static void BuildBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Batch entry point only.");
        CreatePrototype();
    }

    static QuestBoardCatalogue CreateCatalogue()
    {
        var existing = AssetDatabase.LoadAssetAtPath<QuestBoardCatalogue>(CataloguePath);
        if (existing != null) return existing;
        var torso = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Torso.asset");
        var catalogue = ScriptableObject.CreateInstance<QuestBoardCatalogue>();
        string[] titles = { "The Silent Foundry", "Recover the Lost Core", "Clear the Old Road", "Hold the Crossing", "Escort the Stonekeeper", "Guard the Workshop" };
        QuestMissionType[] types = { QuestMissionType.Retrieval, QuestMissionType.Retrieval, QuestMissionType.Elimination, QuestMissionType.KingOfTheHill, QuestMissionType.Escort, QuestMissionType.Defense };
        for (int i = 0; i < titles.Length; i++)
        {
            string path = "Assets/PreMission/Quests/PrototypeQuest_" + (i + 1) + ".asset";
            var quest = AssetDatabase.LoadAssetAtPath<QuestDefinition>(path);
            if (quest == null)
            {
                quest = ScriptableObject.CreateInstance<QuestDefinition>();
                quest.questId = "prototype-quest-" + (i + 1);
                quest.title = titles[i]; quest.category = i == 0 ? QuestCategory.Main : QuestCategory.Side;
                quest.missionType = types[i]; quest.estimatedMinutes = 8 + i * 2; quest.gniReward = 100 + i * 25;
                quest.description = "A notice calls for a golem to answer this request. Inspect the briefing, accept the quest, and choose a starting torso.\n\nPrototype offer: mission gameplay, enemy settings and reward payment are not connected.";
                if (torso != null) quest.availableParts.Add(torso);
                AssetDatabase.CreateAsset(quest, path);
            }
            catalogue.quests.Add(quest);
        }
        if (torso != null) catalogue.starterTorsos.Add(torso);
        AssetDatabase.CreateAsset(catalogue, CataloguePath);
        return catalogue;
    }

    static QuestNoticeView CreateNoticePrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<QuestNoticeView>(NoticePath);
        if (existing != null) return existing;
        var rect = Panel("Quest Notice", null, Paper);
        rect.sizeDelta = new Vector2(420, 200);
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = rect.GetComponent<Image>();
        var label = Text("Notice Text", rect, "Quest notice", 24, Ink, 0.05f, 0.05f, 0.95f, 0.95f);
        var view = rect.gameObject.AddComponent<QuestNoticeView>();
        var fields = new SerializedObject(view);
        Set(fields, "button", button); Set(fields, "label", label);
        fields.ApplyModifiedPropertiesWithoutUndo();
        var prefab = PrefabUtility.SaveAsPrefabAsset(rect.gameObject, NoticePath);
        Object.DestroyImmediate(rect.gameObject);
        return prefab.GetComponent<QuestNoticeView>();
    }

    static void AddMenuEntry()
    {
        const string menuPath = "Assets/Scenes/MainMenuScene.unity";
        if (!File.Exists(menuPath)) return;
        var scene = EditorSceneManager.OpenScene(menuPath);
        if (GameObject.Find("Quest Board") != null) return;
        var template = GameObject.Find("Gym");
        var loader = Object.FindAnyObjectByType<SceneManagerScript>();
        if (template == null || loader == null) throw new InvalidOperationException("Menu entry requires the existing Gym button and SceneManagerScript.");
        var entry = Object.Instantiate(template, template.transform.parent);
        entry.name = "Quest Board";
        var label = entry.GetComponentInChildren<TMP_Text>();
        label.text = "Quest Board"; label.enableAutoSizing = true; label.fontSizeMin = 16; label.fontSizeMax = 34;
        var button = entry.GetComponent<Button>();
        button.onClick = new Button.ButtonClickedEvent();
        UnityEventTools.AddStringPersistentListener(button.onClick, loader.LoadScene, "PreMissionScene");
        var names = new[] { "Zoo", "Gym", "Quest Board", "Exit" };
        for (int i = 0; i < names.Length; i++)
        {
            var rect = GameObject.Find(names[i]).GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(-450 + i * 300, rect.anchoredPosition.y);
        }
        EditorSceneManager.SaveScene(scene);
    }

    static void RegisterBuildScene()
    {
        if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Concat(new[] { new EditorBuildSettingsScene(ScenePath, true) }).ToArray();
    }

    static void ConfigureInput(InputSystemUIInputModule module)
    {
        const string path = "Assets/InputSystem_Actions.inputactions";
        module.actionsAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        if (module.actionsAsset == null) throw new InvalidOperationException("The project Input System asset is required.");
        // Persist imported action-reference subassets rather than transient runtime defaults.
        var actions = AssetDatabase.LoadAllAssetsAtPath(path).OfType<InputActionReference>().ToArray();
        InputActionReference Action(string name) => actions.First(a => a.action != null && a.action.actionMap.name == "UI" && a.action.name == name);
        module.point = Action("Point"); module.leftClick = Action("Click");
        module.rightClick = Action("RightClick"); module.middleClick = Action("MiddleClick");
        module.scrollWheel = Action("ScrollWheel"); module.move = Action("Navigate");
        module.submit = Action("Submit"); module.cancel = Action("Cancel");
        module.trackedDevicePosition = Action("TrackedDevicePosition"); module.trackedDeviceOrientation = Action("TrackedDeviceOrientation");
    }

    static void Set(SerializedObject fields, string name, Object value) => fields.FindProperty(name).objectReferenceValue = value;
    static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        if (parent != null) rect.SetParent(parent, false);
        return rect;
    }
    static RectTransform Panel(string name, Transform parent, Color color)
    {
        var rect = Rect(name, parent);
        var image = rect.gameObject.AddComponent<Image>(); image.color = color;
        return rect;
    }
    static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
    static TMP_Text Text(string name, Transform parent, string value, float size, Color color, float x0, float y0, float x1, float y1)
    {
        var rect = Rect(name, parent); Place(rect, x0, y0, x1, y1);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.fontSize = size; text.color = color;
        text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
        text.enableAutoSizing = true; text.fontSizeMin = size * 0.7f; text.fontSizeMax = size;
        return text;
    }
    static Button Button(string label, Transform parent, float x0, float y0, float x1, float y1)
    {
        var rect = Panel(label, parent, new Color(0.34f, 0.39f, 0.32f)); Place(rect, x0, y0, x1, y1);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
        Text("Label", rect, label, 25, Color.white, 0.04f, 0.04f, 0.96f, 0.96f);
        return button;
    }
    static TMP_Text ScrollBody(RectTransform parent)
    {
        var scroll = Rect("Briefing Scroll", parent); Place(scroll, 0.06f, 0.20f, 0.94f, 0.80f);
        var background = scroll.gameObject.AddComponent<Image>(); background.color = new Color(1, 1, 1, 0.08f);
        scroll.gameObject.AddComponent<RectMask2D>();
        var content = Rect("Content", scroll);
        content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(0.5f, 1);
        content.sizeDelta = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childControlHeight = layout.childControlWidth = true; layout.childForceExpandHeight = false;
        layout.padding = new RectOffset(12, 12, 12, 12);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var text = Text("Briefing", content, "", 24, Ink, 0, 0, 1, 1);
        text.alignment = TextAlignmentOptions.TopLeft; text.enableAutoSizing = false;
        var scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
        scrollRect.viewport = scroll; scrollRect.content = content; scrollRect.horizontal = false;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        return text;
    }
}
