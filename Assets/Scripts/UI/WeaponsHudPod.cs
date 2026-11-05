using System;
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
    // Part art is named after the part it shows: "Cannon" uses "Cannon_Icon".
    public const string IconSuffix = "_Icon";

    [Header("Authored references")]
    [Tooltip("Coloured panel behind the sprite and the ammo count.")]
    [SerializeField] Image background;
    [Tooltip("Weapon name, shown above the panel.")]
    [SerializeField] TextMeshProUGUI nameLabel;
    [SerializeField] Image icon;
    [SerializeField] TextMeshProUGUI ammoLabel;
    [Tooltip("Every sprite in Assets/Sprites/PartIcons. A part picks its icon by name: <partName>_Icon.")]
    [SerializeField] Sprite[] partIcons;
    [Header("Reload")]
    [Tooltip("Multiplier applied to the sprite and the ammo count while the weapon reloads.")]
    [SerializeField, Range(0f, 1f)] float reloadDim = 0.75f;

    Part weapon;
    Color iconBase = Color.white;
    Color placeholder = Color.white;
    Color ammoColour = Color.white;
    float appliedDim = -1f;

    public Part Weapon => weapon;
    public Image Background => background;
    public Sprite CurrentIcon => icon != null ? icon.sprite : null;

    void Awake()
    {
        if (icon != null) placeholder = icon.color;
        if (ammoLabel != null) ammoColour = ammoLabel.color;
    }

    // Reload is read straight from the part, so the row follows the real magazine without events.
    void LateUpdate()
    {
        if (weapon == null || weapon.Stats == null) return;
        if (ammoLabel != null) ammoLabel.text = AmmoText(weapon);
        ApplyDim(weapon.IsReloading);
    }

    // Shows one equipped weapon: the sprite comes from the part's icon in Assets/Sprites/PartIcons,
    // the row is titled with the part's own name, and the ammo is read from the live part.
    public void Bind(Part part)
    {
        weapon = part;
        var stats = part != null ? part.Stats : null;
        if (nameLabel != null) nameLabel.text = stats != null ? stats.partName.ToUpperInvariant() : string.Empty;
        if (icon != null)
        {
            var sprite = IconFor(stats);
            icon.sprite = sprite;
            // Without art the row keeps the faint empty slot the prefab was authored with.
            iconBase = sprite != null ? Color.white : placeholder;
        }
        if (ammoLabel != null) ammoLabel.text = AmmoText(part);
        appliedDim = -1f;
        ApplyDim(part != null && part.IsReloading);
    }

    // Part icons live in Assets/Sprites/PartIcons and follow the part name: "Cannon" -> "Cannon_Icon".
    // A build cannot read a folder by path, so the folder's sprites are listed on this prefab, and a
    // part's own Icon (when set) still wins over the naming convention.
    public Sprite IconFor(PartData stats)
    {
        if (stats == null) return null;
        if (stats.icon != null) return stats.icon;
        string wanted = (stats.partName ?? string.Empty).Trim() + IconSuffix;
        if (partIcons != null)
            foreach (var candidate in partIcons)
                if (candidate != null && string.Equals(candidate.name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
                    return candidate;
        Debug.LogWarning($"Weapons HUD: Assets/Sprites/PartIcons has no '{wanted}'; check the pod prefab's icon list.", this);
        return null;
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
        if (icon != null) icon.color = Dim(iconBase, dim);
        if (ammoLabel != null) ammoLabel.color = Dim(ammoColour, dim);
    }

    static Color Dim(Color colour, float dim) =>
        new Color(colour.r * dim, colour.g * dim, colour.b * dim, colour.a * dim);
}
