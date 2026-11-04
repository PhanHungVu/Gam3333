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
    [Tooltip("Used when the part's Ability Cooldown stat is 0.")]
    [SerializeField, Min(0.01f)] float defaultCooldown = 1f;

    Color iconColour = Color.white;
    Color labelColour = Color.white;
    Part ability;
    PartCategory? slot;
    float cooldown;
    float remaining;

    public Part Ability => ability;
    public PartCategory? Slot => slot;
    public float Cooldown => cooldown;
    public float Remaining => remaining;
    public bool IsCoolingDown => remaining > 0f;

    void Awake()
    {
        if (icon != null) iconColour = icon.color;
        if (cooldownLabel != null) labelColour = cooldownLabel.color;
        SetIconAlpha(1f);
        SetLabelAlpha(0f);
    }

    // Shows one equipped head, torso or legs ability. Its cooldown comes from the part's
    // Ability Cooldown stat; the sprite stays an empty, faint panel until ability art exists.
    public void Bind(Part part)
    {
        ability = part;
        slot = part != null ? part.Slot : null;
        var stats = part != null ? part.Stats : null;
        float value = stats != null ? stats.GetStat(StatType.AbilityCooldown) : 0f;
        cooldown = value > 0f ? value : defaultCooldown;
        remaining = 0f;
        if (icon != null)
        {
            var sprite = stats != null ? stats.icon : null;
            icon.sprite = sprite;
            if (sprite != null) iconColour = Color.white;
            SetIconAlpha(1f);
        }
        if (cooldownLabel != null)
        {
            cooldownLabel.text = Mathf.CeilToInt(cooldown).ToString();
            SetLabelAlpha(0f);
        }
    }

    // Starts the cooldown: the sprite dims and the counter shows the full ability cooldown.
    public bool Activate()
    {
        if (ability == null) return false;
        remaining = Mathf.Max(0.01f, cooldown);
        SetIconAlpha(cooldownIconAlpha);
        if (cooldownLabel != null) cooldownLabel.text = Mathf.CeilToInt(remaining).ToString();
        SetLabelAlpha(1f);
        return true;
    }

    // Lets the cell be fired from the Inspector in play mode while abilities are still unimplemented.
    [ContextMenu("Activate")]
    void ActivateFromInspector() => Activate();

    void Update()
    {
        if (remaining <= 0f) return;
        remaining = Mathf.Max(0f, remaining - Time.deltaTime);
        if (remaining > 0f)
        {
            if (cooldownLabel != null) cooldownLabel.text = Mathf.CeilToInt(remaining).ToString();
            return;
        }
        SetIconAlpha(1f);
        SetLabelAlpha(0f);
    }

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
