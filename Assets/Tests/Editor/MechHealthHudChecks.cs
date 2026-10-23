using System;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MechHealthHudChecks
{
    public static void Run(Action<bool, string> check)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath);
        var viewPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.HealthHudPath);
        check(prefab.GetComponentInChildren<MechHealthHud>(true) == null, "Player prefab contains no health UI");
        check(viewPrefab.GetComponent<Canvas>() == null && viewPrefab.GetComponent<CombatHealth>() == null
            && viewPrefab.GetComponent<CanvasGroup>() != null && viewPrefab.transform.childCount == 3,
            "Health prefab is a three-layer meter with independent visibility under a shared Canvas");
        var assetFill = viewPrefab.transform.Find("BarFront").GetComponent<Image>();
        check(assetFill.type == Image.Type.Filled && assetFill.fillMethod == Image.FillMethod.Horizontal && assetFill.fillOrigin == 0,
            "Health front crops horizontally from the right instead of stretching artwork");
        check(assetFill.sprite == AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/HpBar.png")
            && viewPrefab.transform.Find("BarBack").GetComponent<Image>().sprite == AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/HpBarBack.png")
            && viewPrefab.transform.Find("Glow").GetComponent<Image>().sprite == AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Sprites/HpBarGlow.png"),
            "Health meter references all three supplied HP sprites");
        foreach (var graphic in viewPrefab.GetComponentsInChildren<Graphic>(true)) check(!graphic.raycastTarget, "Health artwork never consumes clicks");
        var player = Object.Instantiate(prefab, new Vector3(100000f, 2f, 0f), Quaternion.identity);
        var ui = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.CursorPath));
        try
        {
            var hud = ui.GetComponentInChildren<MechHealthHud>();
            var energyHud = ui.GetComponentInChildren<MechEnergyHud>();
            var canvas = ui.GetComponent<Canvas>();
            var health = player.GetComponent<CombatHealth>();
            hud.Bind(health);
            energyHud.Bind(player.GetComponent<MechEnergyController>());
            check(hud.Canvas == canvas && energyHud.GetComponentInParent<Canvas>() == canvas
                && hud.transform.parent == energyHud.transform.parent && hud.IsVisible,
                "Health and movement meters share the same Canvas and parent");
            check(((RectTransform)hud.transform).anchoredPosition == new Vector2(28f, 124f)
                && ((RectTransform)energyHud.transform).anchoredPosition == new Vector2(28f, 42f),
                "Canvas prefab places health above movement with room for both glows");
            var fill = hud.transform.Find("BarFront").GetComponent<Image>();
            var glow = hud.transform.Find("Glow").GetComponent<Image>();
            var back = hud.transform.Find("BarBack").GetComponent<Image>();
            var fullSize = fill.rectTransform.sizeDelta;
            var backSize = back.rectTransform.sizeDelta;
            var colour = fill.color;
            health.ApplyDamage(health.MaximumHealth * 0.2f);
            check(Mathf.Abs(fill.fillAmount - health.CurrentHealth / health.MaximumHealth) < 0.001f
                && fill.rectTransform.sizeDelta == fullSize && fill.rectTransform.localScale == Vector3.one,
                "Damage crops HP front without squeezing its graphics");
            check(Mathf.Approximately(glow.rectTransform.localScale.x, hud.NormalizedHealth)
                && glow.rectTransform.localScale.y == 1f && back.rectTransform.sizeDelta == backSize,
                "Damage squeezes glow horizontally and leaves the backing full-sized");
            check(fill.color == colour && hud.Readout == string.Empty, "HP sprite colours are retained without prototype labels");
            var energy = player.GetComponent<MechEnergyController>(); energy.TrySpend(50f);
            typeof(MechEnergyHud).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(energyHud, null);
            check(Mathf.Approximately(energyHud.transform.Find("BarFront").GetComponent<Image>().fillAmount, 0.5f)
                && Mathf.Approximately(energyHud.transform.Find("Glow").localScale.x, 0.5f),
                "Movement front crops and its glow squeezes independently from HP");
            var settings = new SerializedObject(hud);
            var textObject = new GameObject("Designer TMP Readout", typeof(RectTransform));
            textObject.transform.SetParent(hud.transform, false);
            var tmp = textObject.AddComponent<TextMeshProUGUI>(); tmp.raycastTarget = false;
            settings.FindProperty("tmpLabel").objectReferenceValue = tmp; settings.ApplyModifiedPropertiesWithoutUndo();
            health.ApplyDamage(health.MaximumHealth * 0.1f);
            check(tmp.text == "HEALTH  " + Mathf.CeilToInt(health.CurrentHealth) + " / " + Mathf.CeilToInt(health.MaximumHealth),
                "Optional designer health readout still follows real health");
            hud.enabled = false;
            check(!hud.IsVisible && canvas.enabled && energyHud.isActiveAndEnabled, "Disabling HP never hides the movement bar or shared Canvas");
            hud.enabled = true; check(hud.IsVisible, "Re-enabling HP restores its own meter");
            player.SetActive(false); check(!hud.IsVisible && canvas.enabled, "Disabling player hides HP without disabling shared UI");
            player.SetActive(true); check(hud.IsVisible, "Re-enabling player restores HP");
            var replacement = new GameObject("Replacement player").AddComponent<CombatHealth>();
            try
            {
                hud.Bind(replacement); float before = hud.NormalizedHealth; health.ApplyDamage(1f);
                check(hud.NormalizedHealth == before && hud.Health == replacement, "Rebinding detaches old player damage events");
                replacement.ApplyDamage(25f);
                check(Mathf.Approximately(hud.NormalizedHealth, 0.75f), "Rebound meter follows replacement health");
                replacement.ApplyDamage(1000f);
                check(!hud.IsVisible && canvas.enabled && fill.fillAmount == 0f && glow.rectTransform.localScale.x == 0f,
                    "Death empties front and glow without hiding the shared Canvas");
            }
            finally { Object.DestroyImmediate(replacement.gameObject); }
        }
        finally { Object.DestroyImmediate(ui); Object.DestroyImmediate(player); }
    }
}
