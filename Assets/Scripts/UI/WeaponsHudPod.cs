using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// One row of the weapons HUD: the weapon's name above a coloured panel that holds its sprite and
// ammunition. Ammo reads [rounds in the magazine / rounds left in total], or N/A for weapons that
// carry no ammunition (melee). The icon and ammo dim while an attack/reload cooldown runs;
// a bottom-to-top sweep follows the physical part's actual time until it can attack again.
// The key that fires the weapon is printed in the row's top-right corner.
[DisallowMultipleComponent]
public sealed class WeaponsHudPod : MonoBehaviour
{
    public enum AmmoDenominator { TotalRemaining, MagazineCapacity, TotalCapacity }
    [SerializeField] AmmoDenominator ammoDenominator;
    // Part art is named after the part it shows: "Cannon" uses "Cannon_Icon".
    public const string IconSuffix = "_Icon";

    [Header("Authored references")]
    [Tooltip("Coloured panel behind the sprite and the ammo count.")]
    [SerializeField] Image background;
    [Tooltip("Weapon name, shown above the panel.")]
    [SerializeField] TextMeshProUGUI nameLabel;
    [SerializeField] Image icon;
    [SerializeField] TextMeshProUGUI ammoLabel;
    [Tooltip("Key that fires this weapon, named by the socket it is mounted in: LMB, RMB, Q or E.")]
    [SerializeField] TextMeshProUGUI keyText;
    [Tooltip("Every sprite in Assets/Sprites/PartIcons. A part picks its icon by name: <partName>_Icon.")]
    [SerializeField] Sprite[] partIcons;
    [Header("Unavailable appearance")]
    [Tooltip("Multiplier applied to the sprite and ammo during cooldown, reload, or when out of ammo.")]
    [SerializeField, Range(0f, 1f)] float reloadDim = 0.75f;
    [Header("Cooldown sweep")]
    [SerializeField] Image cooldownLine;
    [SerializeField, Min(.5f)] float cooldownLineThickness = 2f;
    [SerializeField] Color cooldownLineColor = new Color(1f,.93f,.6f,1f);

    Part weapon;
    Color iconBase = Color.white;
    Color placeholder = Color.white;
    Color ammoColour = Color.white;
    float appliedDim = -1f;

    public Part Weapon => weapon;
    public Image Background => background;
    public Sprite CurrentIcon => icon != null ? icon.sprite : null;
    public float CooldownProgress => weapon != null ? weapon.CooldownProgress : 1;
    public bool IsCoolingDown => weapon != null && weapon.CooldownRemaining > 0;

    void Awake()
    {
        if (icon != null) placeholder = icon.color;
        if (ammoLabel != null) ammoColour = ammoLabel.color;
        ConfigureCooldownLine();
    }

    // Read directly from the part so swaps, reloads and attack cooldowns use the same gameplay clock.
    void LateUpdate()
    {
        Refresh();
    }

    // Shows one equipped weapon: the sprite comes from the part's icon in Assets/Sprites/PartIcons,
    // the row is titled with the part's own name, the key comes from the socket it is mounted in,
    // and the ammo is read from the live part.
    public void Bind(Part part)
    {
        weapon = part;
        var stats = part != null ? part.Stats : null;
        if (nameLabel != null) nameLabel.text = stats != null ? stats.partName.ToUpperInvariant() : string.Empty;
        if (keyText != null) keyText.text = part != null && part.Slot.HasValue ? KeyLabel(part.Slot.Value) : string.Empty;
        if (icon != null)
        {
            var sprite = IconFor(stats);
            icon.sprite = sprite;
            // Without art the row keeps the faint empty slot the prefab was authored with.
            iconBase = sprite != null ? Color.white : placeholder;
        }
        appliedDim = -1f;
        Refresh();
    }

    // Keys are fixed per socket: the arms fire on the mouse, the shoulders on Q and E.
    public static string KeyLabel(PartCategory socket) => socket switch
    {
        PartCategory.ArmL => "LMB",
        PartCategory.ArmR => "RMB",
        PartCategory.ShoulderL => "Q",
        PartCategory.ShoulderR => "E",
        _ => string.Empty,
    };

    void Refresh()
    {
        if(ammoLabel!=null) ammoLabel.text=AmmoText(weapon,ammoDenominator);
        bool empty=weapon!=null && HasAmmo(weapon.Stats) && weapon.RemainingAmmo<=0;
        ApplyDim(IsCoolingDown || empty);
        HudCooldownSweep.Refresh(icon,cooldownLine,CooldownProgress,IsCoolingDown,cooldownLineThickness);
    }
    public void ConfigureCooldownLine()
    {
        cooldownLine=HudCooldownSweep.Configure(icon,cooldownLine,cooldownLineColor,cooldownLineThickness);
        HudCooldownSweep.Refresh(icon,cooldownLine,CooldownProgress,IsCoolingDown,cooldownLineThickness);
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
    public static string AmmoText(Part part, AmmoDenominator denominator = AmmoDenominator.TotalRemaining)
    {
        var stats = part != null ? part.Stats : null;
        if (stats == null || !HasAmmo(stats)) return "N/A";
        int maximum = denominator == AmmoDenominator.MagazineCapacity && stats.MagazineSize > 0 ? stats.MagazineSize
            : denominator == AmmoDenominator.TotalCapacity ? Mathf.Max(0, Mathf.RoundToInt(stats.GetStat(StatType.Ammo))) : part.RemainingAmmo;
        return part.MagazineRounds + " / " + maximum;
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
