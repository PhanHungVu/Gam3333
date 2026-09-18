using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(MechCombatController))]
public class MechWeaponHud : MonoBehaviour
{
    MechCombatController combat;
    Canvas canvas;
    Text readout;

    void Awake()
    {
        combat = GetComponent<MechCombatController>();
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
        rect.sizeDelta = new Vector2(400, 110);
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

    string WeaponLine(MechWeaponSlot slot, string key)
    {
        int ammo = combat.AmmoRemaining(slot);
        float reload = combat.ReloadRemaining(slot);
        string state = ammo == 0 ? "EMPTY" : reload > 0 ? $"RELOAD {reload:0.0}s" : "READY";
        return $"{key}  {combat.GetWeapon(slot).displayName.ToUpperInvariant()}  {ammo}  {state}";
    }

    void LateUpdate()
    {
        readout.text = "RMB  SWORD\n" + WeaponLine(MechWeaponSlot.ThrowingAxe, "LMB") + "\n"
            + WeaponLine(MechWeaponSlot.Cannon, "E") + "\n" + WeaponLine(MechWeaponSlot.Crossbow, "Q");
    }
}
