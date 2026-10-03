using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MechHealthHudChecks
{
    public static void Run(Action<bool, string> check)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MechAnimatedLegsSetup.PlayerPath);
        var viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MechHealthHudSetup.HudPath);
        check(prefab.GetComponentInChildren<MechHealthHud>(true) == null && prefab.transform.Find("Player Health HUD") == null,
            "Player prefab contains no health HUD component or UI hierarchy");
        var assetData = new SerializedObject(viewPrefab.GetComponent<MechHealthHud>());
        var assetCanvas = assetData.FindProperty("canvas").objectReferenceValue as Canvas;
        check(viewPrefab != null && assetCanvas != null && assetCanvas.gameObject == viewPrefab
            && viewPrefab.GetComponent<CombatHealth>() == null,
            "Standalone health UI prefab owns its view without adding gameplay health");
        var preview = viewPrefab.GetComponent<RectTransform>();
        check(preview.localScale == Vector3.one && preview.sizeDelta == new Vector2(1280f, 720f)
            && assetCanvas.transform.localScale == Vector3.one,
            "Standalone HUD saves a visible 1280 x 720 editor preview");
        check(assetData.FindProperty("fill").objectReferenceValue != null
            && assetData.FindProperty("label").objectReferenceValue != null,
            "Standalone UI prefab saves health fill and text references");
        foreach (var graphic in viewPrefab.GetComponentsInChildren<Graphic>(true))
            check(!graphic.raycastTarget, "Editable health UI artwork does not consume gameplay clicks");

        var player = Object.Instantiate(prefab, new Vector3(100000f, 2f, 0f), Quaternion.identity);
        var ui = Object.Instantiate(viewPrefab);
        var texture = new Texture2D(2, 2);
        var sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
        try
        {
            var hud = ui.GetComponent<MechHealthHud>();
            var health = player.GetComponent<CombatHealth>();
            hud.Bind(health);
            var fill = hud.Canvas.transform.Find("Health Panel/Health Track/Health Fill").GetComponent<Image>();
            int hudCount = ui.GetComponentsInChildren<Canvas>(true).Length;
            check(hudCount == 1 && hud.Canvas.enabled && Mathf.Approximately(hud.NormalizedHealth, 1f),
                "Independent UI binds to the player without adding a HUD child to it");
            health.ApplyDamage(health.MaximumHealth * 0.2f);
            check(Mathf.Abs(hud.NormalizedHealth - health.CurrentHealth / health.MaximumHealth) < 0.001f,
                "Editable regular Image tracks damage by resizing its fill");

            var settings = new SerializedObject(hud);
            settings.FindProperty("tintFillByHealth").boolValue = false;
            settings.FindProperty("label").objectReferenceValue = null;
            settings.ApplyModifiedPropertiesWithoutUndo();
            fill.sprite = sprite; fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = 0;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.color = Color.blue;
            health.ApplyDamage(health.MaximumHealth * 0.1f);
            check(Mathf.Abs(fill.fillAmount - health.CurrentHealth / health.MaximumHealth) < 0.001f
                && fill.rectTransform.anchorMax == Vector2.one && fill.sprite == sprite,
                "Designer Filled Image artwork uses fillAmount and keeps its full layout");
            check(fill.color == Color.blue && hud.Readout == string.Empty,
                "Custom artwork tint and optional absent label are preserved");

            var textObject = new GameObject("Designer TMP Readout", typeof(RectTransform));
            textObject.transform.SetParent(hud.Canvas.transform, false);
            var tmp = textObject.AddComponent<TextMeshProUGUI>(); tmp.raycastTarget = false;
            settings.Update(); settings.FindProperty("tmpLabel").objectReferenceValue = tmp;
            settings.ApplyModifiedPropertiesWithoutUndo();
            health.ApplyDamage(health.MaximumHealth * 0.1f);
            check(tmp.text == "HEALTH  " + Mathf.CeilToInt(health.CurrentHealth) + " / " + Mathf.CeilToInt(health.MaximumHealth)
                && hud.Readout == tmp.text, "Designer TextMeshPro label displays real health values");
            hud.enabled = false;
            check(!hud.Canvas.enabled, "Disabling the HUD hides its saved Canvas");
            hud.enabled = true;
            hudCount = ui.GetComponentsInChildren<Canvas>(true).Length;
            check(hud.Canvas.enabled && hudCount == 1, "Re-enabling reuses the same editable HUD");
            player.SetActive(false);
            check(ui.activeSelf && !hud.Canvas.enabled, "Disabling player hides its independent UI without deactivating the HUD object");
            player.SetActive(true);
            check(hud.Canvas.enabled, "Re-enabling player makes its independent UI visible again");
            var replacement = new GameObject("Replacement player").AddComponent<CombatHealth>();
            try
            {
                hud.Bind(replacement);
                float prior = hud.NormalizedHealth;
                health.ApplyDamage(1f);
                check(hud.NormalizedHealth == prior && hud.Health == replacement,
                    "Rebinding UI detaches from the previous player's damage events");
                replacement.ApplyDamage(25f);
                check(Mathf.Approximately(hud.NormalizedHealth, 0.75f), "Rebound UI follows the replacement player's health");
            }
            finally { Object.DestroyImmediate(replacement.gameObject); }
        }
        finally
        {
            Object.DestroyImmediate(ui);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }
    }
}
