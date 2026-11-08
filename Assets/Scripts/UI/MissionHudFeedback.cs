using TMPro;
using UnityEngine;
using UnityEngine.UI;

public readonly struct MissionHudFeedbackState
{
    public readonly int Completed;
    public readonly bool HasCountdown, ZeroMeansSuccess;
    public readonly float Seconds;
    public MissionHudFeedbackState(int completed, bool hasCountdown, float seconds, bool zeroMeansSuccess)
    { Completed = completed; HasCountdown = hasCountdown; Seconds = seconds; ZeroMeansSuccess = zeroMeansSuccess; }

    public static MissionHudFeedbackState Capture(MissionTracker tracker)
    {
        int completed = MissionObjectives.CompleteCount;
        bool countdown = false, success = true; float seconds = float.PositiveInfinity;
        foreach (var objective in MissionObjectives.All)
        {
            if (objective is EscortPayload escort && escort.Rules != null) completed += escort.Rules.CheckpointsPassed;
            if (objective is IMissionCountdown timer && timer.TryGetCountdown(out float left, out bool win))
                Choose(left, win, ref countdown, ref seconds, ref success);
        }
        if (tracker != null && tracker.State == MissionState.ExtractOpen && tracker.Extract != null
            && tracker.Extract.TryGetCountdown(out float extractLeft, out bool extractWin))
        { countdown = true; seconds = extractLeft; success = extractWin; }
        if (tracker != null && tracker.State == MissionState.Complete) countdown = false;
        return new MissionHudFeedbackState(completed, countdown, countdown ? seconds : 0, success);
    }
    static void Choose(float left, bool win, ref bool active, ref float seconds, ref bool success)
    {
        // An expiring failure window takes priority over a successful hold elsewhere.
        if (!active || !win && success || win == success && left < seconds)
        { active = true; seconds = Mathf.Max(0, left); success = win; }
    }
}

// Animates the existing objective text only; no new HUD canvas or string parsing.
[DisallowMultipleComponent, DefaultExecutionOrder(500)]
public sealed class MissionHudFeedback : MonoBehaviour
{
    [SerializeField, Min(.1f)] float lastSeconds = 5;
    [SerializeField, Min(.1f)] float completionDuration = .8f;
    [SerializeField, Range(0, 20)] float shakePixels = 2;
    [SerializeField, Range(0, .5f)] float pulseScale = .04f;
    Graphic label;
    RectTransform rect;
    Vector2 restingPosition;
    Vector3 restingScale;
    Color restingColor;
    Outline outline;
    TMP_Text tmp;
    Material originalMaterial, glowMaterial;
    MissionTracker tracker;
    float searchAt, clock, completionAge;
    int previousComplete;
    bool sampled;
    public bool IsUrgent { get; private set; }
    public bool UrgencyIsFailure { get; private set; }
    public bool CompletionPulseActive => completionAge > 0;

    public static void Attach(Graphic target)
    {
        if (target == null) return;
        var effect = target.GetComponent<MissionHudFeedback>();
        if (effect == null) effect = target.gameObject.AddComponent<MissionHudFeedback>();
        effect.Bind(target);
    }
    public void Bind(Graphic target)
    {
        if (label == target) return;
        Restore(); label = target; rect = (RectTransform)target.transform;
        restingPosition = rect.anchoredPosition; restingScale = rect.localScale; restingColor = target.color;
        tmp = target as TMP_Text;
        if (tmp == null)
        {
            outline = target.GetComponent<Outline>();
            if (outline == null) outline = target.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(3, -3); outline.enabled = false;
        }
        PrepareMaterial();
    }
    void PrepareMaterial()
    {
        if (tmp == null || glowMaterial != null || tmp.fontSharedMaterial == null) return;
        originalMaterial = tmp.fontSharedMaterial; glowMaterial = new Material(originalMaterial) { name = "Mission text feedback (instance)" };
        tmp.fontSharedMaterial = glowMaterial;
    }
    void LateUpdate()
    {
        if (label == null) return;
        if (tracker == null && Time.unscaledTime >= searchAt) { searchAt = Time.unscaledTime + .5f; tracker = FindAnyObjectByType<MissionTracker>(); }
        var state = MissionHudFeedbackState.Capture(tracker);
        if (MissionOutcomeController.BlocksGameplay(gameObject.scene)) state = new MissionHudFeedbackState(state.Completed, false, 0, true);
        Tick(state, Time.unscaledDeltaTime);
    }
    public void Tick(MissionHudFeedbackState state, float dt)
    {
        if (label == null) return;
        PrepareMaterial(); dt = Mathf.Max(0, dt); clock += dt;
        if (sampled && state.Completed > previousComplete) completionAge = completionDuration;
        sampled = true; previousComplete = state.Completed;
        completionAge = Mathf.Max(0, completionAge - dt);
        IsUrgent = state.HasCountdown && state.Seconds > 0 && state.Seconds <= lastSeconds;
        UrgencyIsFailure = IsUrgent && !state.ZeroMeansSuccess;
        float pulse = 0, shake = 0, scale = 1;
        Color color = new Color(.35f, 1, .5f);
        if (IsUrgent)
        {
            color = UrgencyIsFailure ? new Color(1, .85f, .2f) : new Color(.3f, 1, .4f);
            float urgency = 1 - Mathf.Clamp01(state.Seconds / lastSeconds);
            pulse = .7f + .3f * Mathf.Abs(Mathf.Sin(clock * Mathf.Lerp(6, 10, urgency)));
            shake = shakePixels * Mathf.Lerp(.65f, 1, urgency);
            scale += pulseScale * pulse;
        }
        else if (completionAge > 0)
        {
            pulse = Mathf.Sin(Mathf.Clamp01(completionAge / completionDuration) * Mathf.PI);
            scale += pulse * .14f;
        }
        label.color = Color.Lerp(restingColor, color, pulse);
        rect.anchoredPosition = restingPosition + new Vector2(Mathf.Sin(clock * 17), Mathf.Cos(clock * 21)) * shake;
        rect.localScale = restingScale * scale;
        if (outline != null) { outline.enabled = pulse > .01f; outline.effectColor = new Color(color.r, color.g, color.b, pulse * .85f); }
        if (glowMaterial != null && glowMaterial.HasProperty("_OutlineWidth"))
        {
            glowMaterial.SetFloat("_OutlineWidth", Mathf.Lerp(originalMaterial.GetFloat("_OutlineWidth"), .24f, pulse));
            glowMaterial.SetColor("_OutlineColor", new Color(color.r, color.g, color.b, pulse * .85f));
            tmp.UpdateMeshPadding();
        }
    }
    void Restore()
    {
        if (label != null) label.color = restingColor;
        if (rect != null) { rect.anchoredPosition = restingPosition; rect.localScale = restingScale; }
        if (outline != null) outline.enabled = false;
        if (tmp != null && originalMaterial != null) tmp.fontSharedMaterial = originalMaterial;
        if (glowMaterial != null) Destroy(glowMaterial);
        glowMaterial = null; IsUrgent = UrgencyIsFailure = sampled = false; completionAge = 0;
    }
    void OnDisable() => Restore();
    void OnDestroy() => Restore();
}
