using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MechHealthHudSceneChecks
{
    const string Pending = "MechHealthHudSceneChecks.Pending";
    static MechHealthHud hud;
    static CombatHealth health;
    static GameObject player;
    static int phase;
    static float next = -1f;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Update;
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene("Assets/Scenes/Merge Test.unity");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); Debug.Log("HEALTH HUD PASS: " + message); }
    static void Value()
    {
        Check(Mathf.Abs(hud.NormalizedHealth - health.CurrentHealth / health.MaximumHealth) < 0.001f
            && Mathf.Abs(hud.transform.Find("Glow").localScale.x - hud.NormalizedHealth) < 0.001f
            && hud.transform.Find("BarFront").localScale == Vector3.one
            && hud.transform.Find("BarBack").localScale == Vector3.one,
            "Front crops and glow squeezes to actual health at phase " + phase);
    }
    static void Capture(string name) => typeof(CombatFeedbackSceneChecks).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { "health-" + name });
    static void Update()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (next < 0f) next = Time.time + 1f;
        if (Time.time < next) return;
        try
        {
            switch (phase)
            {
                case 0:
                    foreach (var spawner in Object.FindObjectsByType<EnemySpawnerManager>()) spawner.enabled = false;
                    foreach (var enemy in Object.FindObjectsByType<EnemyAgent>()) enemy.gameObject.SetActive(false);
                    hud = Object.FindAnyObjectByType<MechHealthHud>(); health = hud.Health; player = health.gameObject;
                    var energy = Object.FindAnyObjectByType<MechEnergyHud>();
                    Check(hud != null && Object.FindObjectsByType<MechHealthHud>().Length == 1, "Scene has one health meter");
                    Check(hud.transform.parent == energy.transform.parent && hud.Canvas == energy.GetComponentInParent<Canvas>()
                        && player.GetComponentInChildren<MechHealthHud>() == null,
                        "Health and movement share one Canvas outside the player prefab");
                    Check(((RectTransform)hud.transform).anchoredPosition == new Vector2(28f, 124f)
                        && ((RectTransform)energy.transform).anchoredPosition == new Vector2(28f, 42f),
                        "Scene places health above movement with both glows inside the screen");
                    Check(hud.transform.childCount == 3 && energy.transform.childCount == 3,
                        "Both meters contain Glow, BarBack and BarFront");
                    foreach (var graphic in hud.GetComponentsInChildren<Graphic>()) Check(!graphic.raycastTarget, "Health UI never intercepts gameplay clicks");
                    Value(); Capture("full"); health.ApplyDamage(40f); break;
                case 1:
                    Value(); Capture("damaged");
                    float injured = health.CurrentHealth;
                    Check(player.GetComponent<MechLocomotionDebug>().TrySwap(1) && health.CurrentHealth == injured,
                        "Heavy leg swap changes max health without healing");
                    Value(); Capture("capacity"); health.ApplyDamage(health.CurrentHealth - 20f); break;
                case 2:
                    Value();
                    Check(hud.transform.Find("BarFront").GetComponent<Image>().color == Color.white,
                        "Low health retains the supplied sprite colours");
                    Capture("low"); player.SetActive(false); Check(!hud.IsVisible && hud.Canvas.enabled, "Disabling player hides HP while keeping shared Canvas active");
                    player.SetActive(true); break;
                case 3:
                    Check(hud.IsVisible && Object.FindObjectsByType<MechHealthHud>().Length == 1,
                        "Re-enabling player restores its health meter without duplication");
                    Value(); health.ApplyDamage(10000f);
                    Check(health.IsDead && !hud.IsVisible && hud.Canvas.enabled && hud.NormalizedHealth == 0f
                        && hud.transform.Find("Glow").localScale.x == 0f, "Death clears front and glow without hiding shared Canvas");
                    SessionState.SetBool(Pending, false); Debug.Log("HEALTH HUD CHECKS COMPLETE"); EditorApplication.Exit(0); return;
            }
            phase++; next = Time.time + 0.3f;
        }
        catch (Exception e) { SessionState.SetBool(Pending, false); Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
