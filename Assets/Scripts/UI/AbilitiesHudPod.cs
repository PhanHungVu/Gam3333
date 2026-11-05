using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One ability cell of the abilities HUD: the ability's sprite with its cooldown counter over it.
// The counter is fully transparent while the ability is ready. Using the ability dims the sprite
// to 50%, shows the counter at full opacity with the ability's own cooldown value, and counts it
// down; when it reaches 0 the counter fades out and the sprite returns to full opacity.
[DisallowMultipleComponent]
public sealed class AbilitiesHudPod : MonoBehaviour
{
    [Header("Authored references")]
    [Tooltip("Coloured panel behind the ability sprite.")]
    [SerializeField] Image background;
    [SerializeField] Image icon;
    [Tooltip("Cooldown seconds, hidden (0% alpha) until the ability is used.")]
    [SerializeField] TextMeshProUGUI cooldownLabel;
    [Header("Cooldown")]
    [Tooltip("Sprite alpha while the ability is on cooldown.")]
    [SerializeField, Range(0f, 1f)] float cooldownIconAlpha = 0.5f;
    [Tooltip("Imported part artwork; an explicit PartData icon takes priority.")]
    [SerializeField] Sprite[] partIcons;
    [Tooltip("Generic artwork for part variants without a dedicated icon.")]
    [SerializeField] Sprite headIcon, torsoIcon, legsIcon;
    [Header("Cooldown sweep")]
    [SerializeField] Image cooldownLine;
    [SerializeField, Min(.5f)] float cooldownLineThickness = 2f;
    [SerializeField] Color cooldownLineColor = new Color(1f, .93f, .6f, 1f);
    [Header("Cooldown ready effect")]
    [SerializeField] AbilityReadyVfx readyVfx;

    Color iconColour = Color.white;
    Color labelColour = Color.white;
    Part ability;
    PartCategory? slot;
    float cooldown;
    PartAbilityCooldown state;
    bool wasCooling;
    uint observedCueCycle;

    public Part Ability => ability;
    public PartCategory? Slot => slot;
    public float Cooldown => cooldown;
    public float Remaining => state != null ? state.Remaining : 0;
    public bool IsCoolingDown => Remaining > 0f;
    public Sprite CurrentIcon => icon != null ? icon.sprite : null;
    // Use the accepted gameplay duration, which can differ from the part's authored default.
    public float CooldownProgress => state != null && state.Duration > 0 ? Mathf.Clamp01(1f - Remaining / state.Duration) : 1f;

    void Awake()
    {
        if (icon != null) iconColour = icon.color;
        if (cooldownLabel != null) labelColour = cooldownLabel.color;
        SetIconAlpha(1f);
        SetLabelAlpha(0f);
        ConfigureCooldownLine();
        ConfigureReadyVfx();
    }

    // Explicit ability art wins; generic slot artwork also covers Fast/Heavy Legs variants.
    public void Bind(Part part)
    {
        if (readyVfx != null) readyVfx.Stop();
        ability = part;
        slot = part != null ? part.Slot : null;
        var stats = part != null ? part.Stats : null;
        float value = stats != null ? stats.GetStat(StatType.AbilityCooldown) : 0f;
        cooldown = Mathf.Max(0, value);
        state = PartAbilityCooldown.For(part);
        PrimeReadyCue();
        if (icon != null)
        {
            var sprite = stats != null ? stats.icon : null;
            if (sprite == null && stats != null)
                sprite = stats.partType == PartType.Head ? headIcon : stats.partType == PartType.Torso ? torsoIcon : stats.partType == PartType.Legs ? legsIcon : null;
            if (sprite == null && stats != null && partIcons != null)
                foreach (var candidate in partIcons)
                    if (candidate != null && string.Equals(candidate.name.Trim(), (stats.partName ?? "").Trim() + WeaponsHudPod.IconSuffix,
                        System.StringComparison.OrdinalIgnoreCase)) { sprite = candidate; break; }
            icon.sprite = sprite;
            if (sprite != null) iconColour = Color.white;
            SetIconAlpha(1f);
        }
        Refresh();
    }

    // Starts the cooldown: the sprite dims and the counter shows the full ability cooldown.
    public bool Activate()
    {
        if (ability == null || !ability.isEquipped || state == null || !state.ConfirmActivation(cooldown)) return false;
        Refresh(); return true;
    }

    // Lets the cell be fired from the Inspector in play mode while abilities are still unimplemented.
    [ContextMenu("Activate")]
    void ActivateFromInspector() => Activate();

    void Update() => Refresh();
    void Refresh()
    {
        float remaining = Remaining;
        bool cooling = remaining > 0;
        if (wasCooling && !cooling && state != null && state.TryConsumeReadyCue(observedCueCycle)) readyVfx?.Play();
        wasCooling = cooling;
        if (cooling && state != null) observedCueCycle = state.ReadyCueCycle;
        SetIconAlpha(remaining > 0 ? cooldownIconAlpha : 1);
        SetLabelAlpha(remaining > 0 ? 1 : 0);
        if (cooldownLabel != null) cooldownLabel.text = remaining > 0 ? Mathf.CeilToInt(remaining).ToString() : string.Empty;
        HudCooldownSweep.Refresh(icon,cooldownLine,CooldownProgress,remaining>0,cooldownLineThickness);
    }

    // Authored by setup; also supports older scene/prefab copies without adding a new Canvas.
    public void ConfigureCooldownLine()
    {
        cooldownLine=HudCooldownSweep.Configure(icon,cooldownLine,cooldownLineColor,cooldownLineThickness);
        HudCooldownSweep.Refresh(icon,cooldownLine,CooldownProgress,IsCoolingDown,cooldownLineThickness);
    }

    public void ConfigureReadyVfx()
    {
        if (readyVfx == null) readyVfx = GetComponent<AbilityReadyVfx>() ?? gameObject.AddComponent<AbilityReadyVfx>();
        readyVfx.Configure(icon);
    }
    void PrimeReadyCue() { wasCooling = IsCoolingDown; observedCueCycle = state != null ? state.ReadyCueCycle : 0; }
    void OnEnable() => PrimeReadyCue();
    void OnDisable() { wasCooling = false; if (readyVfx != null) readyVfx.Stop(); }

    void SetIconAlpha(float alpha)
    {
        if (icon != null) icon.color = new Color(iconColour.r, iconColour.g, iconColour.b, iconColour.a * alpha);
    }

    void SetLabelAlpha(float alpha)
    {
        if (cooldownLabel != null)
            cooldownLabel.color = new Color(labelColour.r, labelColour.g, labelColour.b, alpha);
    }
}
