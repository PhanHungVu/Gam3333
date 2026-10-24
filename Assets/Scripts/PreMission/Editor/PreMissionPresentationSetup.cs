using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Explicit, repeatable prefab upgrade. Preserves quest assets, hook and scene/menu wiring.
public static class PreMissionPresentationSetup
{
    [MenuItem("Tools/Mech/Pre-Mission/Upgrade Q2 and Q3 Presentation")]
    public static void UpgradeAssets()
    {
        if (Application.isPlaying) throw new InvalidOperationException("Stop Play mode before upgrading the UI assets.");
        UpgradeNotice();
        var root = PrefabUtility.LoadPrefabContents(PreMissionPrototypeSetup.PrefabPath);
        try
        {
            var controller = root.GetComponent<PreMissionFlowController>();
            var fields = new SerializedObject(controller);
            var board = Get<GameObject>(fields, "boardView");
            var details = Get<GameObject>(fields, "detailsView");
            var torso = Get<GameObject>(fields, "torsoView");
            var parent = Get<Transform>(fields, "noticeParent");
            var grid = parent.GetComponent<GridLayoutGroup>();
            if (grid != null) Object.DestroyImmediate(grid);
            Place((RectTransform)parent, 0.035f, 0.19f, 0.965f, 0.77f);
            var layout = parent.GetComponent<PreMissionNoticeLayout>();
            var briefing = details.transform.Find("Briefing Paper") as RectTransform;
            if (briefing == null)
            {
                briefing = Panel("Briefing Paper", details.transform, new Color(0.84f, 0.79f, 0.66f));
                var shadow = briefing.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0, 0, 0, 0.4f); shadow.effectDistance = new Vector2(9, -10);
                briefing.gameObject.AddComponent<Outline>().effectColor = new Color(0.3f, 0.24f, 0.15f, 0.5f);
                var title = Get<TMP_Text>(fields, "detailsTitle");
                var scroll = Get<TMP_Text>(fields, "detailsBody").GetComponentInParent<ScrollRect>(true);
                title.transform.SetParent(briefing, false); scroll.transform.SetParent(briefing, false);
                Get<Button>(fields, "acceptButton").transform.SetParent(briefing, false);
                Get<Button>(fields, "declineButton").transform.SetParent(briefing, false);
                Place(title.rectTransform, 0.06f, 0.80f, 0.94f, 0.94f);
                Place((RectTransform)scroll.transform, 0.065f, 0.21f, 0.935f, 0.79f);
                var caption = new GameObject("Briefing Caption", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
                caption.transform.SetParent(briefing, false);
                Place(caption.rectTransform, 0.1f, 0.94f, 0.9f, 0.985f);
                caption.font = title.font; caption.fontSize = 14; caption.text = "MISSION BRIEFING";
                caption.color = new Color(0.35f, 0.27f, 0.15f); caption.alignment = TextAlignmentOptions.Center; caption.raycastTarget = false;
            }
            Place(briefing, 0.11f, 0.025f, 0.89f, 0.975f);
            details.GetComponent<Image>().color = Color.clear;
            var shade = root.transform.Find("Transition Shade");
            if (shade == null) shade = Panel("Transition Shade", root.transform, Color.black);
            Place((RectTransform)shade, 0, 0, 1, 1);
            shade.SetSiblingIndex(board.transform.GetSiblingIndex() + 1);
            var shadeGroup = Add<CanvasGroup>(shade.gameObject);
            shadeGroup.alpha = 0f; shadeGroup.interactable = false; shadeGroup.blocksRaycasts = true;
            shade.gameObject.SetActive(false);
            var travelling = root.transform.Find("Travelling Notice");
            if (travelling == null)
            {
                var notice = AssetDatabase.LoadAssetAtPath<GameObject>(PreMissionPrototypeSetup.NoticePath);
                travelling = ((GameObject)PrefabUtility.InstantiatePrefab(notice, root.transform)).transform;
                travelling.name = "Travelling Notice";
            }
            travelling.SetAsLastSibling(); travelling.gameObject.SetActive(false);
            var presentation = Add<PreMissionPaperPresentation>(root);
            var presentationFields = new SerializedObject(presentation);
            Set(presentationFields, "board", Add<CanvasGroup>(board));
            Set(presentationFields, "details", Add<CanvasGroup>(details));
            Set(presentationFields, "torso", Add<CanvasGroup>(torso));
            Set(presentationFields, "layout", layout);
            Set(presentationFields, "briefingPaper", briefing);
            Set(presentationFields, "shade", shadeGroup);
            Set(presentationFields, "travellingPaper", travelling.GetComponent<QuestNoticeView>());
            presentationFields.ApplyModifiedPropertiesWithoutUndo();
            Set(fields, "presentation", presentation); fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PreMissionPrototypeSetup.PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Debug.Log("PRE-MISSION Q2/Q3 PRESENTATION UPGRADED");
    }
    static void UpgradeNotice()
    {
        var root = PrefabUtility.LoadPrefabContents(PreMissionPrototypeSetup.NoticePath);
        try
        {
            var view = root.GetComponent<QuestNoticeView>();
            var fields = new SerializedObject(view);
            var label = Get<TMP_Text>(fields, "label");
            var button = Get<Button>(fields, "button");
            var colors = button.colors; colors.disabledColor = Color.white; button.colors = colors;
            var paper = root.transform.Find("Paper Visual") as RectTransform;
            if (paper == null)
            {
                var oldImage = root.GetComponent<Image>();
                paper = Panel("Paper Visual", root.transform, oldImage.color);
                Place(paper, 0, 0, 1, 1);
                button.targetGraphic = paper.GetComponent<Image>();
                label.transform.SetParent(paper, false);
                Object.DestroyImmediate(oldImage);
                var shadow = paper.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0, 0, 0, 0.3f); shadow.effectDistance = new Vector2(4, -5);
            }
            // Hover moves only the visual; a transparent root graphic keeps the hit area fixed.
            var hitArea = Add<Image>(root); hitArea.color = Color.clear; hitArea.raycastTarget = true;
            paper.GetComponent<Image>().raycastTarget = false; button.targetGraphic = paper.GetComponent<Image>();
            Place(label.rectTransform, 0.045f, 0.035f, 0.955f, 0.93f);
            label.enableAutoSizing = true; label.fontSizeMin = 13f; label.fontSizeMax = 30f;
            var accent = paper.Find("Category Accent") as RectTransform;
            if (accent == null) accent = Panel("Category Accent", paper, new Color(0.38f, 0.43f, 0.33f));
            Place(accent, 0, 0.96f, 1, 1); accent.GetComponent<Image>().raycastTarget = false;
            Set(fields, "paperVisual", paper); Set(fields, "paperImage", paper.GetComponent<Image>());
            Set(fields, "accent", accent.GetComponent<Image>()); Set(fields, "border", Add<Outline>(paper.gameObject));
            Set(fields, "visibility", Add<CanvasGroup>(root));
            fields.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PreMissionPrototypeSetup.NoticePath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    static T Add<T>(GameObject go) where T : Component
    {
        var component = go.GetComponent<T>();
        return component != null ? component : go.AddComponent<T>();
    }
    static T Get<T>(SerializedObject fields, string name) where T : Object => (T)fields.FindProperty(name).objectReferenceValue;
    static void Set(SerializedObject fields, string name, Object value) => fields.FindProperty(name).objectReferenceValue = value;
    static RectTransform Panel(string name, Transform parent, Color color)
    {
        var rect = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.gameObject.layer = 5;
        rect.GetComponent<Image>().color = color;
        return rect;
    }
    static void Place(RectTransform rect, float x0, float y0, float x1, float y1)
    {
        rect.anchorMin = new Vector2(x0, y0); rect.anchorMax = new Vector2(x1, y1);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }
}
