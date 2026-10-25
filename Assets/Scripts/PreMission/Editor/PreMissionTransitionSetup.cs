using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit Q5.3 upgrade. Retains the existing art, UI references and empty hook.
public static class PreMissionTransitionSetup
{
    public const string HeavyTorsoPath = "Assets/PreMission/PartData_HeavyTorso_Test.asset";
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade Q5.3 Transitions and Test Torso")]
    public static void UpgradeAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode before upgrading the UI assets.");
        AddTestTorso();
        var root = PrefabUtility.LoadPrefabContents(PreMissionPrototypeSetup.PrefabPath);
        try
        {
            var fields = new SerializedObject(root.GetComponent<PreMissionFlowController>());
            var presentation = new SerializedObject(root.GetComponent<PreMissionPaperPresentation>());
            bool firstUpgrade = presentation.FindProperty("briefingGroups").arraySize == 0;
            var paper = Get<RectTransform>(presentation, "briefingPaper");
            var title = Get<TMP_Text>(fields, "detailsTitle"); var body = Get<TMP_Text>(fields, "detailsBody");
            var header = paper.Find("Briefing Header") as RectTransform;
            if (header == null)
            {
                header = Rect("Briefing Header", paper); Place(header, 0.06f, 0.80f, 0.94f, 0.94f);
                title.transform.SetParent(header, false); Place(title.rectTransform, 0, 0.38f, 1, 1);
            }
            var type = Text("Mission Type", header, body); Place(type.rectTransform, 0, 0, 1, 0.35f);
            type.fontSize = 20; type.alignment = TextAlignmentOptions.Center;
            var content = body.transform.parent;
            content.GetComponent<VerticalLayoutGroup>().spacing = 12f;
            var info = Text("Mission Information", content, body);
            var reward = Text("Mission Reward", content, body);
            body.transform.SetSiblingIndex(0); info.transform.SetSiblingIndex(1); reward.transform.SetSiblingIndex(2);
            var controls = paper.Find("Briefing Controls") as RectTransform;
            if (controls == null)
            {
                controls = Rect("Briefing Controls", paper); Place(controls, 0, 0.05f, 1, 0.15f);
                var accept = Get<Button>(fields, "acceptButton"); var decline = Get<Button>(fields, "declineButton");
                accept.transform.SetParent(controls, false); decline.transform.SetParent(controls, false);
                Place((RectTransform)accept.transform, 0.65f, 0, 0.94f, 1);
                Place((RectTransform)decline.transform, 0.06f, 0, 0.35f, 1);
            }
            Set(fields, "detailsType", type); Set(fields, "detailsInfo", info); Set(fields, "detailsReward", reward);
            var groups = new[] { Add<CanvasGroup>(header.gameObject), Add<CanvasGroup>(body.gameObject),
                Add<CanvasGroup>(info.gameObject), Add<CanvasGroup>(reward.gameObject), Add<CanvasGroup>(controls.gameObject) };
            var sequence = presentation.FindProperty("briefingGroups"); sequence.arraySize = groups.Length;
            for (int i = 0; i < groups.Length; i++) { groups[i].alpha = 1f; sequence.GetArrayElementAtIndex(i).objectReferenceValue = groups[i]; }
            var torso = Get<GameObject>(fields, "torsoView").transform;
            var choice = torso.Find("Torso Choice") as RectTransform;
            if (choice == null)
            {
                choice = Rect("Torso Choice", torso); Place(choice, 0, 0, 1, 1);
                foreach (var item in new Transform[] { Get<StarterTorsoPreview>(fields, "torsoPreview").transform.parent,
                    Get<TMP_Text>(fields, "torsoName").transform, Get<TMP_Text>(fields, "torsoDescription").transform,
                    Get<TMP_Text>(fields, "torsoCounter").transform }) item.SetParent(choice, false);
            }
            Set(presentation, "torsoChoice", Add<CanvasGroup>(choice.gameObject));
            Set(presentation, "confirmButton", Get<Button>(fields, "confirmTorsoButton").transform);
            if (firstUpgrade) presentation.FindProperty("viewFadeDuration").floatValue = 0.26f;
            fields.ApplyModifiedPropertiesWithoutUndo(); presentation.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PreMissionPrototypeSetup.PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); Debug.Log("PRE-MISSION Q5.3 TRANSITIONS UPGRADED");
    }
    static void AddTestTorso()
    {
        var original = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Torso.asset");
        var catalogue = AssetDatabase.LoadAssetAtPath<QuestBoardCatalogue>(PreMissionPrototypeSetup.CataloguePath);
        var heavy = AssetDatabase.LoadAssetAtPath<PartData>(HeavyTorsoPath);
        if (heavy == null)
        {
            heavy = Object.Instantiate(original); heavy.name = "PartData_HeavyTorso_Test";
            heavy.partId = "starter_heavy_torso_test"; heavy.partName = "Heavy Torso (Test)";
            heavy.description = "A reinforced core with a higher health contribution. Development test part.";
            heavy.stats = original.stats.Select(s => new StatEntry { type = s.type, value = s.type == StatType.Health ? s.value * 1.5f : s.value }).ToList();
            AssetDatabase.CreateAsset(heavy, HeavyTorsoPath);
        }
        if (!catalogue.starterTorsos.Contains(heavy)) catalogue.starterTorsos.Add(heavy);
        if (catalogue.FindTorsoVisual(heavy) == null)
        {
            var visual = catalogue.FindTorsoVisual(original);
            if (visual == null) throw new InvalidOperationException("Upgrade the Q4 Golem torso model mapping first.");
            catalogue.starterTorsoVisuals.Add(new StarterTorsoVisual { torso = heavy, model = visual.model, rotation = visual.rotation });
        }
        EditorUtility.SetDirty(catalogue);
    }
    static TMP_Text Text(string name, Transform parent, TMP_Text source)
    {
        var existing = parent.Find(name)?.GetComponent<TMP_Text>();
        if (existing != null) return existing;
        var text = Object.Instantiate(source, parent); text.name = name; text.text = string.Empty; text.enableAutoSizing = false;
        return text;
    }
    static T Add<T>(GameObject go) where T : Component { var component = go.GetComponent<T>(); return component != null ? component : go.AddComponent<T>(); }
    static T Get<T>(SerializedObject fields, string name) where T : Object => (T)fields.FindProperty(name).objectReferenceValue;
    static void Set(SerializedObject fields, string name, Object value) => fields.FindProperty(name).objectReferenceValue = value;
    static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); rect.gameObject.layer = 5; return rect;
    }
    static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1); rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
