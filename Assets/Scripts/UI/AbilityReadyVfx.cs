using UnityEngine;
using UnityEngine.UI;

// One reusable UI mesh per ability/weapon pod, allocated on its first ready cue. No gameplay timer or audio.
[DisallowMultipleComponent]
public sealed class AbilityReadyVfx : MonoBehaviour
{
    [SerializeField] Image icon;
    [SerializeField, Range(.1f, 2f)] float duration = .55f;
    [SerializeField, Range(.01f, .2f)] float flashDuration = .08f;
    [SerializeField, Range(8, 128)] int particleCount = 60;
    [Tooltip("Column height, measured in icon heights.")]
    [SerializeField, Range(1f, 10f)] float height = 5.5f;
    [Tooltip("Horizontal spread near the top, measured in icon widths.")]
    [SerializeField, Range(.05f, 1f)] float spread = .28f;
    [SerializeField, Range(.1f, 3f)] float brightness = 1.35f;
    [SerializeField] Vector2 particleLifetime = new Vector2(.25f, .55f);
    [Tooltip("Additional icon scale at the peak. 0.1 produces a 1.1x pulse; 0 disables it.")]
    [SerializeField, Range(0, .25f)] float iconPulse = .1f;

    AbilityReadyBurstGraphic burst;
    Vector3 originalScale;
    float age;
    bool playing;
    public bool IsPlaying => playing;
    public int PlayCount { get; private set; }
    public AbilityReadyBurstGraphic Graphic => burst;
    public float Duration => duration;

    public void Configure(Image target)
    {
        Stop(); icon = target;
    }

    public bool Play()
    {
        if (!isActiveAndEnabled || icon == null || icon.canvas == null || icon.canvasRenderer.GetInheritedAlpha() <= .001f) return false;
        Stop();
        var canvas = icon.canvas.rootCanvas;
        if (burst == null)
        {
            var node = new GameObject("Ability Ready Burst", typeof(RectTransform), typeof(CanvasRenderer), typeof(AbilityReadyBurstGraphic));
            node.layer = canvas.gameObject.layer;
            burst = node.GetComponent<AbilityReadyBurstGraphic>();
        }
        burst.transform.SetParent(canvas.transform, false);
        burst.transform.SetAsLastSibling();
        originalScale = icon.rectTransform.localScale;
        age = 0; playing = true; PlayCount++;
        burst.Begin(Mathf.Clamp(particleCount, 8, 128), Mathf.Clamp(duration, .1f, 2f), Mathf.Clamp(flashDuration, .01f, .2f),
            height, spread, brightness, particleLifetime);
        UpdateAnchor(); return true;
    }

    void LateUpdate()
    {
        if (!playing) return;
        Tick(Time.unscaledDeltaTime);
    }

    // Cosmetic time only. Readiness is supplied exclusively by PartAbilityCooldown.
    public void Tick(float deltaTime)
    {
        if (!playing) return;
        if (icon == null || !icon.isActiveAndEnabled || icon.canvas == null || icon.canvasRenderer.GetInheritedAlpha() <= .001f) { Stop(); return; }
        age += Mathf.Max(0, deltaTime);
        if (age >= duration) { Stop(); return; }
        float phase = Mathf.Clamp01(age / Mathf.Min(duration, .24f));
        icon.rectTransform.localScale = originalScale * (1 + Mathf.Sin(phase * Mathf.PI) * iconPulse);
        UpdateAnchor(); burst.SetAge(age);
    }

    void UpdateAnchor()
    {
        var source = icon.rectTransform; var target = burst.rectTransform;
        // The visual belongs to the existing Canvas, above its other children, so masks on a pod
        // cannot crop the tall column. Inherit the icon's scale/rotation without inheriting its pulse.
        var canvas = icon.canvas.rootCanvas;
        if (target.parent != canvas.transform) target.SetParent(canvas.transform, false);
        target.position = source.TransformPoint(source.rect.center);
        target.rotation = source.rotation;
        Vector3 parentScale = source.parent != null ? source.parent.lossyScale : Vector3.one;
        Vector3 canvasScale = canvas.transform.lossyScale;
        var worldScale = Vector3.Scale(parentScale, originalScale);
        target.localScale = new Vector3(SafeRatio(worldScale.x, canvasScale.x), SafeRatio(worldScale.y, canvasScale.y), 1);
        target.sizeDelta = source.rect.size;
        target.pivot = new Vector2(.5f, .5f);
    }
    static float SafeRatio(float a, float b) => Mathf.Abs(b) > .00001f ? a / b : 1;

    public void Stop()
    {
        if (playing && icon != null) icon.rectTransform.localScale = originalScale;
        playing = false;
        if (burst != null) burst.enabled = false;
    }
    void OnDisable() => Stop();
    void OnDestroy() { if (burst != null) Destroy(burst.gameObject); }
}
