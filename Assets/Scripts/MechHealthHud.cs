using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(CombatHealth))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(350)]
public sealed class MechHealthHud : MonoBehaviour
{
    CombatHealth health;
    EquipManager equipment;
    Canvas canvas;
    Image fill;
    Text label;
    public Canvas Canvas => canvas;
    public float NormalizedHealth => fill != null ? fill.rectTransform.anchorMax.x : 0f;
    public string Readout => label != null ? label.text : string.Empty;

    void Awake()
    {
        health = GetComponent<CombatHealth>(); equipment = GetComponent<EquipManager>();
        var root = new GameObject("Player Health HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280f, 720f); scaler.matchWidthOrHeight = 0.5f;
        var panel = Image("Health Panel", root.transform, new Color(0.03f, 0.05f, 0.07f, 0.85f));
        var rect = panel.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(24f, -24f); rect.sizeDelta = new Vector2(300f, 78f);
        var text = new GameObject("Health Readout", typeof(RectTransform), typeof(Text), typeof(Shadow));
        text.transform.SetParent(panel.transform, false); label = text.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); label.fontSize = 19;
        label.fontStyle = FontStyle.Bold; label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false;
        var textRect = label.rectTransform;
        textRect.anchorMin = new Vector2(0f, 0.5f); textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14f, 0f); textRect.offsetMax = new Vector2(-14f, -8f);
        text.GetComponent<Shadow>().effectDistance = new Vector2(1f, -1f);
        var track = Image("Health Track", panel.transform, new Color(0.15f, 0.18f, 0.2f));
        var trackRect = track.rectTransform;
        trackRect.anchorMin = trackRect.anchorMax = new Vector2(0f, 0f); trackRect.pivot = Vector2.zero;
        trackRect.anchoredPosition = new Vector2(14f, 14f); trackRect.sizeDelta = new Vector2(272f, 18f);
        fill = Image("Health Fill", track.transform, Color.white);
        fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = Vector2.one;
        fill.rectTransform.offsetMin = fill.rectTransform.offsetMax = Vector2.zero;
        Refresh();
    }
    static Image Image(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image)); go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = false; return image;
    }
    void OnEnable()
    {
        health.Damaged += OnDamaged;
        if (equipment != null) equipment.EquipmentChanged += Refresh;
        canvas.enabled = true; Refresh();
    }
    void OnDisable()
    {
        health.Damaged -= OnDamaged;
        if (equipment != null) equipment.EquipmentChanged -= Refresh;
        if (canvas != null) canvas.enabled = false;
    }
    void OnDamaged(float _) => Refresh();
    void Refresh()
    {
        if (fill == null || health == null) return;
        float ratio = Mathf.Clamp01(health.CurrentHealth / Mathf.Max(1f, health.MaximumHealth));
        fill.rectTransform.anchorMax = new Vector2(ratio, 1f);
        fill.color = ratio <= 0.25f ? new Color(1f, 0.3f, 0.25f) : new Color(0.3f, 0.95f, 0.5f);
        label.text = "HEALTH  " + Mathf.CeilToInt(health.CurrentHealth) + " / " + Mathf.CeilToInt(health.MaximumHealth);
    }
}
