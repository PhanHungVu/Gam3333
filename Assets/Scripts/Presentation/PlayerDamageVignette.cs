using UnityEngine;
using UnityEngine.UI;

// Local-player edge overlay. Independent of the teammate-owned combat HUD layout.
[DisallowMultipleComponent, RequireComponent(typeof(CombatHealth))]
public sealed class PlayerDamageVignette : MonoBehaviour
{
    [SerializeField, Range(.01f, 1f)] float startsAtHealth = .5f;
    [SerializeField, Range(0f, .49f)] float fullAtHealth = .01f;
    [SerializeField, Range(0f, 1f)] float maximumAlpha = .8f;
    [SerializeField] Color tint = new Color(.65f, .015f, .015f);
    [SerializeField] Sprite edgeSprite;
    CombatHealth health;
    Image image;
    Texture2D generatedTexture;
    Sprite generatedSprite;
    public float Alpha { get; private set; }
    public Image Overlay => image;
    public static float Evaluate(float healthRatio, float starts = .5f, float full = .01f, float maximum = .8f)
        => Mathf.Clamp01((starts - healthRatio) / Mathf.Max(.001f, starts - full)) * Mathf.Clamp01(maximum);
    void Awake() => health = GetComponent<CombatHealth>();
    void OnEnable() { health.Changed += Refresh; health.AvailabilityChanged += Availability; Refresh(); }
    void OnDisable()
    {
        health.Changed -= Refresh; health.AvailabilityChanged -= Availability;
        Alpha = 0; if (image != null) image.gameObject.SetActive(false);
    }
    void Availability(bool _) => Refresh();
    void Refresh()
    {
        Alpha = health != null && health.isActiveAndEnabled && !health.IsDead
            ? Evaluate(health.CurrentHealth / Mathf.Max(1, health.MaximumHealth), startsAtHealth, fullAtHealth, maximumAlpha) : 0;
        if (Alpha > 0 && image == null) CreateOverlay();
        if (image == null) return;
        image.gameObject.SetActive(Alpha > 0); var color = tint; color.a = Alpha; image.color = color;
    }
    void CreateOverlay()
    {
        var root = new GameObject("Player Damage Vignette", typeof(RectTransform), typeof(Canvas));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = -10;
        var graphic = new GameObject("Edges", typeof(RectTransform), typeof(Image)); graphic.transform.SetParent(root.transform, false);
        image = graphic.GetComponent<Image>(); image.raycastTarget = false;
        var rect = image.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        if (edgeSprite == null)
        {
            const int size = 128;
            generatedTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Damage edge mask", wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++) for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x / (size - 1f), y / (size - 1f)) * 2 - Vector2.one;
                float t = Mathf.Clamp01((p.magnitude - .48f) / .5f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255 * t * t * (3 - 2 * t)));
            }
            generatedTexture.SetPixels32(pixels); generatedTexture.Apply(false, false);
            generatedSprite = Sprite.Create(generatedTexture, new Rect(0, 0, size, size), Vector2.one * .5f, 100);
        }
        image.sprite = edgeSprite != null ? edgeSprite : generatedSprite;
    }
    void OnDestroy()
    {
        if (generatedSprite != null) Destroy(generatedSprite);
        if (generatedTexture != null) Destroy(generatedTexture);
    }
}
