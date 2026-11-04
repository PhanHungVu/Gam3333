using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One row of the weapons HUD: the weapon's name above a coloured panel that holds its sprite and
// ammunition. Ammo reads [rounds in the magazine / rounds left in total], or N/A for weapons that
// carry no ammunition (melee). While the weapon reloads the sprite and the ammo count dim by 25%,
// then come back to full when the magazine is replenished.
[DisallowMultipleComponent]
public sealed class WeaponsHudPod : MonoBehaviour
{
    [Header("Authored references")]
    [Tooltip("Coloured panel behind the sprite and the ammo count.")]
    [SerializeField] Image background;
    [Tooltip("Weapon name, shown above the panel.")]
    [SerializeField] TextMeshProUGUI nameLabel;
    [SerializeField] Image icon;
    [SerializeField] TextMeshProUGUI ammoLabel;
    [Header("Reload")]
    [Tooltip("Multiplier applied to the sprite and the ammo count while the weapon reloads.")]
    [SerializeField, Range(0f, 1f)] float reloadDim = 0.75f;

    Part weapon;
    Color iconColour = Color.white;
    Color ammoColour = Color.white;
    float appliedDim = -1f;

    public Part Weapon => weapon;
    public Image Background => background;

    void Awake()
    {
        if (icon != null) iconColour = icon.color;
        if (ammoLabel != null) ammoColour = ammoLabel.color;
    }

    // Reload is read straight from the part, so the row follows the real magazine without events.
    void LateUpdate()
    {
        if (weapon == null || weapon.Stats == null) return;
        if (ammoLabel != null) ammoLabel.text = AmmoText(weapon);
        ApplyDim(weapon.IsReloading);
    }

    // Shows one equipped weapon. The name comes from the part's data asset; its icon is optional
    // until part art exists, in which case the sprite stays an empty, faint panel.
    public void Bind(Part part)
    {
        weapon = part;
        var stats = part != null ? part.Stats : null;
        if (nameLabel != null) nameLabel.text = stats != null ? stats.partName.ToUpperInvariant() : string.Empty;
        if (icon != null)
        {
            var sprite = stats != null ? stats.icon : null;
            icon.sprite = sprite;
            if (sprite != null) iconColour = Color.white;
            icon.color = iconColour;
        }
        if (ammoLabel != null) ammoLabel.text = AmmoText(part);
        appliedDim = -1f;
        ApplyDim(part != null && part.IsReloading);
    }

    // [in the magazine / in total]. A magazine of 0 never reloads, so it reads as one full count.
    public static string AmmoText(Part part)
    {
        var stats = part != null ? part.Stats : null;
        if (stats == null || !HasAmmo(stats)) return "N/A";
        return part.MagazineRounds + " / " + part.RemainingAmmo;
    }

    public static bool HasAmmo(PartData stats) =>
        stats != null && (stats.subtype == PartSubtype.Projectile || stats.subtype == PartSubtype.Splash)
        && (stats.GetStat(StatType.Ammo) > 0f || stats.MagazineSize > 0);

    void ApplyDim(bool reloading)
    {
        float dim = reloading ? reloadDim : 1f;
        if (Mathf.Approximately(dim, appliedDim)) return;
        appliedDim = dim;
        if (icon != null) icon.color = Dim(iconColour, dim);
        if (ammoLabel != null) ammoLabel.color = Dim(ammoColour, dim);
    }

    static Color Dim(Color colour, float dim) =>
        new Color(colour.r * dim, colour.g * dim, colour.b * dim, colour.a * dim);
}
