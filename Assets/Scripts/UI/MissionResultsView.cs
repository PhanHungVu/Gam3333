using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MissionResultsView : MonoBehaviour
{
    [SerializeField] TMP_Text title, description;
    [SerializeField] RawImage models;
    [SerializeField] Button retry, menu;
    MissionResultModels preview;
    public string Title => title != null ? title.text : "";
    public static MissionResultsView Create(Transform parent)
    {
        var root = new GameObject("Mission Results", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.transform.SetParent(parent, false);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
        var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1920,1080);
        var view = root.AddComponent<MissionResultsView>();
        var panel = Rect("Backdrop", root.transform, new Vector2(.5f,.5f), Vector2.zero, new Vector2(1920,1080));
        panel.anchorMin = Vector2.zero; panel.anchorMax = Vector2.one; panel.offsetMin = panel.offsetMax = Vector2.zero;
        panel.gameObject.AddComponent<Image>().color = new Color(.035f,.045f,.05f,.95f);
        view.title = Text("Title", root.transform, new Vector2(0,350), new Vector2(1500,90), 64);
        view.description = Text("Description", root.transform, new Vector2(0,260), new Vector2(1500,80), 28);
        view.models = Rect("Participants", root.transform, new Vector2(.5f,.5f), new Vector2(0,-20), new Vector2(1400,480)).gameObject.AddComponent<RawImage>();
        view.models.raycastTarget = false;
        view.retry = MakeButton("Retry", root.transform, new Vector2(-220,-390));
        view.menu = MakeButton("Main Menu", root.transform, new Vector2(220,-390));
        return view;
    }
    static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var node = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); node.SetParent(parent, false);
        node.anchorMin = node.anchorMax = anchor; node.anchoredPosition = position; node.sizeDelta = size; return node;
    }
    static TMP_Text Text(string name, Transform parent, Vector2 position, Vector2 size, float fontSize)
    {
        var text = Rect(name, parent, new Vector2(.5f,.5f), position, size).gameObject.AddComponent<TextMeshProUGUI>();
        text.text = name; text.fontSize = fontSize; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false; text.color = new Color(.94f,.9f,.76f); return text;
    }
    static Button MakeButton(string name, Transform parent, Vector2 position)
    {
        var rect = Rect(name, parent, new Vector2(.5f,.5f), position, new Vector2(360,76));
        rect.gameObject.AddComponent<Image>().color = new Color(.22f,.28f,.24f);
        var button = rect.gameObject.AddComponent<Button>(); Text(name, rect, Vector2.zero, rect.sizeDelta, 30); return button;
    }
    public void Show(MissionOutcomeController source)
    {
        gameObject.SetActive(true);
        if (title != null) title.text = source.Outcome == MissionOutcome.Succeeded ? "MISSION COMPLETE" : "MISSION FAILED";
        if (description != null) description.text = source.Reason;
        if (retry != null) { retry.onClick.RemoveAllListeners(); retry.onClick.AddListener(source.Retry); }
        if (menu != null) { menu.onClick.RemoveAllListeners(); menu.onClick.AddListener(source.ReturnToMenu); }
        if (EventSystem.current == null)
        {
            var events = new GameObject("Results Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false); events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        if (models != null)
        {
            models.gameObject.SetActive(source.Outcome == MissionOutcome.Succeeded);
            if (source.Outcome == MissionOutcome.Succeeded)
            {
                preview = gameObject.AddComponent<MissionResultModels>(); preview.Show(source.Participants, models);
            }
        }
    }
}
