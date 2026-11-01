using UnityEngine;
using UnityEngine.UI;

// Placeholder mission read-out at the top of the screen: objectives done, then the extract, then
// "Mission complete". Only reads MissionTracker, so the real UI can replace it later.
[DisallowMultipleComponent]
[RequireComponent(typeof(MissionTracker))]
public sealed class MissionHud : MonoBehaviour
{
    MissionTracker tracker;
    Text label;

    void Awake()
    {
        tracker = GetComponent<MissionTracker>();
        var root = new GameObject("Mission HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 11;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        var text = new GameObject("Mission Status", typeof(RectTransform), typeof(Text), typeof(Shadow));
        text.transform.SetParent(root.transform, false);
        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0, -20);
        rect.sizeDelta = new Vector2(700, 60);
        label = text.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.fontSize = 24;
        label.alignment = TextAnchor.UpperCenter;
        label.raycastTarget = false;
        text.GetComponent<Shadow>().effectDistance = new Vector2(1, -1);
    }

    void LateUpdate() => label.text = Describe(tracker);

    public static string Describe(MissionTracker tracker)
    {
        switch (tracker.State)
        {
            case MissionState.Complete:
                return "MISSION COMPLETE";
            case MissionState.ExtractOpen:
                var extract = tracker.Extract;
                if (extract == null) return "Extract open";
                return extract.State switch
                {
                    HoldZoneState.Holding => $"Extracting... {extract.TimeRemaining:0.0}s",
                    HoldZoneState.Warning => $"Get back to the extract! {extract.GraceRemaining:0.0}s",
                    HoldZoneState.Failed => "Extraction interrupted",
                    _ => "Extract open: return to the start",
                };
            default:
                return $"Objectives {tracker.ObjectivesComplete} / {tracker.ObjectivesTotal}";
        }
    }
}
