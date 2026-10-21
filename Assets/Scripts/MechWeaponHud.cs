using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(MechCombatController))]
public class MechWeaponHud : MonoBehaviour
{
    MechCombatController combat;
    MechEquipmentCombat equipment;
    Canvas canvas;
    Text readout;

    void Awake()
    {
        combat = GetComponent<MechCombatController>();
        equipment = GetComponent<MechEquipmentCombat>();
        var root = new GameObject("Weapon HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(transform, false);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = 0.5f;
        var panel = new GameObject("Weapon Readout", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        var rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-24, -24);
        rect.sizeDelta = new Vector2(420, equipment != null ? 132 : 110);
        panel.GetComponent<Image>().color = new Color(0.03f, 0.05f, 0.07f, 0.8f);
        panel.GetComponent<Image>().raycastTarget = false;
        var label = new GameObject("Ammo and Reload", typeof(RectTransform), typeof(Text), typeof(Shadow));
        label.transform.SetParent(panel.transform, false);
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(12, 10);
        labelRect.offsetMax = new Vector2(-12, -10);
        readout = label.GetComponent<Text>();
        readout.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        readout.fontSize = 17;
        readout.alignment = TextAnchor.UpperRight;
        readout.raycastTarget = false;
        label.GetComponent<Shadow>().effectDistance = new Vector2(1, -1);
    }

    void OnEnable() { if (canvas != null) canvas.enabled = true; }
    void OnDisable() { if (canvas != null) canvas.enabled = false; }

    // What a socket's button does: its ability, its ranged weapon, or nothing.
    string MountLine(PartCategory mount, string key)
    {
        if (equipment == null) return WeaponLine(MechCombatController.LegacyWeaponFor(mount), key);
        var ability = equipment.GetAbility(mount);
        if (ability != null) return key + "  " + ability.name.ToUpperInvariant() + (ability.CooldownRemaining > 0 ? $"  {ability.CooldownRemaining:0.0}s" : "  READY");
        var part = equipment.GetEquippedPart(mount);
        var stats = equipment.GetWeapon(part);
        if (stats == null) return AnimationLine(part, key) ?? key + "  NO PART EQUIPPED";
        float remaining = Mathf.Max(0f, part.ReadyAt - Time.time);
        string ready = part.RemainingAmmo == 0 ? "EMPTY" : remaining > 0f ? $"RELOAD {remaining:0.0}s" : "READY";
        return $"{key}  {stats.displayName.ToUpperInvariant()}  {part.RemainingAmmo}  {ready}";
    }

    // Prototype fixtures without an EquipManager: the fixed loadout, by weapon.
    string WeaponLine(MechWeaponType slot, string key)
    {
        if (!combat.HasWeapon(slot)) return key + "  NO PART EQUIPPED";
        int ammo = combat.AmmoRemaining(slot);
        float reload = combat.ReloadRemaining(slot);
        string state = ammo == 0 ? "EMPTY" : reload > 0 ? $"RELOAD {reload:0.0}s" : "READY";
        return $"{key}  {combat.GetWeapon(slot).displayName.ToUpperInvariant()}  {ammo}  {state}";
    }

    void LateUpdate()
    {
        string melee = ArmMeleeLine(PartCategory.ArmL, "LMB") ?? ArmProjectileLine(PartCategory.ArmL, "LMB") ?? (equipment == null && combat.HasSword ? "LMB  SWORD" : equipment != null && equipment.GetAbility(PartCategory.ArmL) != null
            ? "LMB  " + equipment.GetAbility(PartCategory.ArmL).name.ToUpperInvariant() : AnimationLine(equipment != null
                ? equipment.GetEquippedPart(PartCategory.ArmL) : null, "LMB") ?? "LMB  NO SWORD EQUIPPED");
        readout.text = melee + "\n" + (ArmMeleeLine(PartCategory.ArmR, "RMB") ?? ArmProjectileLine(PartCategory.ArmR, "RMB") ?? MountLine(PartCategory.ArmR, "RMB")) + "\n"
            + MountLine(PartCategory.ShoulderL, "Q") + "\n" + MountLine(PartCategory.ShoulderR, "E") + (equipment != null ? "\nHOLD F + BUTTON  SWAP PART" : "");
    }

    string ArmProjectileLine(PartCategory category, string key)
    {
        var part = equipment != null ? equipment.GetEquippedPart(category) : null;
        if (part == null || part.data == null || (part.data.weapon != MechPartWeapon.ArmCrossbow && part.data.weapon != MechPartWeapon.ThrowingAxe)) return null;
        float remaining = Mathf.Max(0f, part.ReadyAt - Time.time);
        string state = part.RemainingAmmo == 0 ? "EMPTY" : remaining > 0f ? $"RELOAD {remaining:0.0}s" : "READY";
        return $"{key}  {part.data.partName.ToUpperInvariant()}  {part.RemainingAmmo}  {state}";
    }

    string ArmMeleeLine(PartCategory category, string key)
    {
        var part = equipment != null ? equipment.GetMeleePart(category) : null;
        if (part == null) return null;
        float remaining = Mathf.Max(0f, part.ReadyAt - Time.time);
        return key + "  " + part.data.partName.ToUpperInvariant() + (remaining > 0f ? $"  {remaining:0.0}s" : "  READY");
    }

    static string AnimationLine(Part part, string key)
    {
        var animation = part != null ? part.GetComponent<MechWeaponAnimator>() : null;
        if (animation == null || animation.Animations == null || !animation.Animations.animationOnly) return null;
        return key + "  " + part.data.partName.ToUpperInvariant() + (animation.PreviewCooldownRemaining > 0f
            ? $"  {animation.PreviewCooldownRemaining:0.0}s" : "  READY");
    }
}
