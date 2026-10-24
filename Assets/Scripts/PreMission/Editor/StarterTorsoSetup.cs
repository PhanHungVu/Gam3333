using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class StarterTorsoSetup
{
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade Q4 Torso Selection")]
    public static void UpgradeAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode before upgrading the UI assets.");
        var catalogue = AssetDatabase.LoadAssetAtPath<QuestBoardCatalogue>(PreMissionPrototypeSetup.CataloguePath);
        var data = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Torso.asset");
        if (catalogue.FindTorsoVisual(data) == null && catalogue.starterTorsos.Contains(data))
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Golem1Models/Golem1Torso.glb");
            if (model == null) throw new InvalidOperationException("The existing Golem1Torso model must be imported before creating its preview.");
            catalogue.starterTorsoVisuals.Add(new StarterTorsoVisual { torso = data, model = model });
            EditorUtility.SetDirty(catalogue);
        }
        var root = PrefabUtility.LoadPrefabContents(PreMissionPrototypeSetup.PrefabPath);
        try
        {
            var flow = root.GetComponent<PreMissionFlowController>(); var fields = new SerializedObject(flow);
            var torso = Get<GameObject>(fields, "torsoView").transform;
            var name = Get<TMP_Text>(fields, "torsoName");
            var description = Get<TMP_Text>(fields, "torsoDescription");
            var panel = torso.Find("Torso Model Preview") as RectTransform;
            if (panel == null) { panel = torso.Find("Torso Preview Placeholder") as RectTransform; panel.name = "Torso Model Preview"; }
            Place(panel, 0.11f, 0.29f, 0.53f, 0.75f);
            var modelRect = panel.Find("Model") as RectTransform;
            if (modelRect == null) modelRect = Rect("Model", panel);
            Place(modelRect, 0.02f, 0.025f, 0.98f, 0.975f);
            var raw = Add<RawImage>(modelRect.gameObject); raw.raycastTarget = true;
            var fallback = panel.GetComponentInChildren<TMP_Text>(true);
            fallback.name = "Preview Fallback"; fallback.transform.SetParent(modelRect, false);
            Place(fallback.rectTransform, 0.05f, 0.05f, 0.95f, 0.95f);
            var preview = Add<StarterTorsoPreview>(modelRect.gameObject);
            var previewFields = new SerializedObject(preview);
            Set(previewFields, "image", raw); Set(previewFields, "fallback", fallback); previewFields.ApplyModifiedPropertiesWithoutUndo();
            Place(Get<TMP_Text>(fields, "torsoQuestTitle").rectTransform, 0.06f, 0.77f, 0.94f, 0.85f);
            Place(name.rectTransform, 0.62f, 0.57f, 0.94f, 0.72f); name.fontSizeMax = 32; name.fontSizeMin = 22;
            Place(description.rectTransform, 0.62f, 0.32f, 0.94f, 0.56f);
            description.alignment = TextAlignmentOptions.TopLeft; description.fontSizeMax = 22; description.fontSizeMin = 17;
            Place((RectTransform)Get<Button>(fields, "previousTorsoButton").transform, 0.035f, 0.46f, 0.095f, 0.58f);
            Place((RectTransform)Get<Button>(fields, "nextTorsoButton").transform, 0.545f, 0.46f, 0.605f, 0.58f);
            var counter = torso.Find("Torso Counter")?.GetComponent<TMP_Text>();
            if (counter == null) counter = Text("Torso Counter", torso, name.font, "", 23);
            Place(counter.rectTransform, 0.22f, 0.18f, 0.42f, 0.25f);
            var hint = torso.Find("Preview Hint");
            if (hint != null) Object.DestroyImmediate(hint.gameObject);
            Set(fields, "torsoPreview", preview); Set(fields, "torsoCounter", counter); fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PreMissionPrototypeSetup.PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets(); Debug.Log("PRE-MISSION Q4 TORSO SELECTION UPGRADED");
    }
    static T Get<T>(SerializedObject fields, string name) where T : Object => (T)fields.FindProperty(name).objectReferenceValue;
    static void Set(SerializedObject fields, string name, Object value) => fields.FindProperty(name).objectReferenceValue = value;
    static T Add<T>(GameObject go) where T : Component { var component = go.GetComponent<T>(); return component != null ? component : go.AddComponent<T>(); }
    static RectTransform Rect(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false); rect.gameObject.layer = 5; return rect;
    }
    static TMP_Text Text(string name, Transform parent, TMP_FontAsset font, string value, float size)
    {
        var text = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font; text.text = value; text.fontSize = size; text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.15f, 0.13f, 0.11f); text.raycastTarget = false;
        return text;
    }
    static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1); rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
