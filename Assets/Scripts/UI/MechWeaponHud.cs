using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(MechCombat))]
public class MechWeaponHud : MonoBehaviour
{
    MechCombat combat;
    Canvas canvas;
    Text readout;

    void Awake()
    {
        combat = GetComponent<MechCombat>();
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
        rect.sizeDelta = new Vector2(420, 132);
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

    // What a socket's button does: its weapon (by subtype), an animation-only attack, or nothing.
    string SocketLine(PartCategory socket, string key)
    {
        var part = combat.GetPart(socket);
        if (part == null) return key + "  NO PART EQUIPPED";
        string name = part.Stats.partName.ToUpperInvariant();
        float wait = combat.ReloadRemaining(socket);
        switch (part.Stats.subtype)
        {
            case PartSubtype.Melee:
                return key + "  " + name + (wait > 0f ? $"  {wait:0.0}s" : "  READY");
            case PartSubtype.Projectile:
            case PartSubtype.Splash:
                int ammo = part.RemainingAmmo;
                // Magazine / total ammo, or just the total for weapons without a magazine.
                string rounds = part.Stats.MagazineSize > 0 ? $"{part.MagazineRounds}/{ammo}" : ammo.ToString();
                string state = ammo == 0 ? "EMPTY" : part.IsReloading ? $"RELOAD {wait:0.0}s" : wait > 0f ? $"{wait:0.0}s" : "READY";
                return $"{key}  {name}  {rounds}  {state}";
            default:
                return AnimationLine(part, key) ?? key + "  NO PART EQUIPPED";
        }
    }

    void LateUpdate()
    {
        readout.text = SocketLine(PartCategory.ArmL, "LMB") + "\n" + SocketLine(PartCategory.ArmR, "RMB") + "\n"
            + SocketLine(PartCategory.ShoulderL, "Q") + "\n" + SocketLine(PartCategory.ShoulderR, "E");
    }

    static string AnimationLine(Part part, string key)
    {
        var animation = part.GetComponent<MechWeaponAnimator>();
        if (animation == null || animation.Animations == null || !animation.Animations.animationOnly) return null;
        return key + "  " + part.Stats.partName.ToUpperInvariant() + (animation.PreviewCooldownRemaining > 0f
            ? $"  {animation.PreviewCooldownRemaining:0.0}s" : "  READY");
    }
}
