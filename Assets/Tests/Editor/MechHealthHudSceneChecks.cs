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
            && hud.Readout == "HEALTH  " + Mathf.CeilToInt(health.CurrentHealth) + " / " + Mathf.CeilToInt(health.MaximumHealth),
            "Bar and numbers show actual current/max health at phase " + phase);
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
                    Check(hud != null && Object.FindObjectsByType<MechHealthHud>().Length == 1, "Scene has one standalone health HUD");
                    Check(hud.transform.parent == null && player.GetComponentInChildren<MechHealthHud>() == null,
                        "Health HUD is a separate scene root, outside the player prefab");
                    foreach (var graphic in hud.Canvas.GetComponentsInChildren<Graphic>()) Check(!graphic.raycastTarget, "Health UI never intercepts gameplay clicks");
                    Value(); Capture("full"); health.ApplyDamage(40f); break;
                case 1:
                    Value(); Capture("damaged");
                    float injured = health.CurrentHealth;
                    Check(player.GetComponent<MechLocomotionDebug>().TrySwap(1) && health.CurrentHealth == injured,
                        "Heavy leg swap changes max health without healing");
                    Value(); Capture("capacity"); health.ApplyDamage(health.CurrentHealth - 20f); break;
                case 2:
                    Value();
                    Check(hud.Canvas.transform.Find("Health Panel/Health Track/Health Fill").GetComponent<Image>().color.r > 0.9f,
                        "Low health uses a red fill");
                    Capture("low"); player.SetActive(false); Check(!hud.Canvas.enabled, "Disabling player hides health HUD");
                    player.SetActive(true); break;
                case 3:
                    Check(hud.Canvas.enabled && Object.FindObjectsByType<MechHealthHud>().Length == 1,
                        "Re-enabling player restores the standalone HUD without duplication");
                    Value(); health.ApplyDamage(10000f);
                    Check(health.IsDead && !hud.Canvas.enabled && hud.NormalizedHealth == 0f, "Death clears fill and follows existing player UI lifecycle");
                    SessionState.SetBool(Pending, false); Debug.Log("HEALTH HUD CHECKS COMPLETE"); EditorApplication.Exit(0); return;
            }
            phase++; next = Time.time + 0.3f;
        }
        catch (Exception e) { SessionState.SetBool(Pending, false); Debug.LogException(e); EditorApplication.Exit(1); }
    }
}
