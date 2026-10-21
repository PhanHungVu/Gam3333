using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Actual A33 prefab integration, frame timing and rendering in an isolated project.
public static class SpiralBarrageSceneChecks
{
    const string Key = "SpiralBarrageSceneChecks.Pending";
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static MechCombatController combat;
    static CombatFeedback feedback;
    static MechTargetable[] enemies;
    static MechProjectile shot;
    static CannonSpiralBarrage.Settings settings;
    static Vector3 impact, heading;
    static float next = -1, deadline;
    static int phase, initialCount, nodes, deaths, summaries, initialPool;
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); Debug.Log("SPIRAL SCENE PASS: " + message); }
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Flags).SetValue(obj, value);
    static void Capture(string label) => typeof(CombatFeedbackSceneChecks)
        .GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "spiral-" + label });

    [InitializeOnLoadMethod]
    static void Register() => EditorApplication.update += Tick;

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch checks only.");
        EditorSceneManager.OpenScene("Assets/Scenes/A33.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnImpact(MechProjectileImpact result)
    {
        try { CheckImpact(result); }
        catch (Exception e) { Debug.LogException(e); SessionState.SetBool(Key, false); EditorApplication.Exit(1); }
    }

    static void CheckImpact(MechProjectileImpact result)
    {
        if (result.ConfirmationOnly)
        {
            summaries++;
            Check(result.Kills == 3 && deaths == 3, "Complete barrage aggregates the three distinct gameplay deaths");
        }
        else if (result.Secondary)
        {
            Check(Vector3.Distance(result.Position, CannonSpiralBarrage.NodePosition(impact, heading, settings, nodes)) < 0.001f,
                "Live secondary " + nodes + " follows the configured spiral position");
            if (nodes < 2) Check(result.Kills == 1 && !enemies[nodes + 1].gameObject.activeInHierarchy,
                "Secondary damage/death occurs with its visible explosion event");
            nodes++;
        }
        else
        {
            initialCount++;
            Check(result.Kills == 1 && result.DeferConfirmation, "Original cannon damage resolves once and defers its confirmation");
            impact = result.Position;
            heading = shot.Direction;
            // Put two wounded enemies at future nodes to test live damage timing.
            for (int i = 0; i < 2; i++)
            {
                enemies[i + 1].transform.position += CannonSpiralBarrage.NodePosition(impact, heading, settings, i)
                    - enemies[i + 1].AimPosition;
                enemies[i + 1].GetComponent<CombatHealth>().ApplyDamage(65);
            }
            Physics.SyncTransforms();
        }
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (next < 0) { next = Time.time + 2; deadline = Time.time + 20; }
        if (Time.time < next) return;
        try
        {
            if (Time.time > deadline) throw new Exception("Spiral scene timed out in phase " + phase);
            if (phase == 0)
            {
                Time.captureDeltaTime = 1f / 60f;
                combat = Object.FindAnyObjectByType<MechCombatController>();
                feedback = combat.GetComponent<CombatFeedback>();
                initialPool = feedback.PoolSize;
                settings = CannonSpiralBarrage.Settings.Default;
                Set(combat, "spiralBarrage", true);
                Set(combat, "spiralSettings", settings);
                enemies = Object.FindObjectsByType<MechTargetable>();
                Check(enemies.Length >= 3, "Actual scene supplies the barrage test enemies");
                foreach (var enemy in enemies)
                {
                    var movement = enemy.GetComponent<AutoMoveTarget>();
                    if (movement != null) movement.enabled = false;
                    enemy.transform.position += Vector3.right * 1000;
                }
                for (int i = 0; i < 3; i++) enemies[i].GetComponent<CombatHealth>().Died += () => deaths++;
                enemies[0].transform.position += combat.transform.position + Vector3.forward * 12 - enemies[0].AimPosition;
                Physics.SyncTransforms();
                combat.GetComponent<MechTargetLockController>().ResumeTargeting();
                combat.ProjectileImpact += OnImpact;
                phase = 1; next = Time.time + 0.5f;
            }
            else if (phase == 1)
            {
                shot = combat.TryFire(PartCategory.ShoulderL);
                Check(shot != null && shot.IsLive, "Actual cannon launches its normal projectile with optional pattern enabled");
                phase = 2;
            }
            else if (phase == 2)
            {
                if (nodes < 1) return;
                Check(feedback.ActiveEffects > 0 && feedback.KillConfirmationCount == 0,
                    "Barrage visuals play before the single final HUD confirmation");
                Capture("first-node");
                phase = 3;
            }
            else if (phase == 3)
            {
                if (summaries == 0) return;
                Check(initialCount == 1 && nodes == settings.nodeCount && summaries == 1
                    && feedback.KillConfirmationCount == 1 && feedback.LastConfirmedKills == 3,
                    "Actual scene completes all eight nodes with one triple-kill confirmation");
                Capture("complete");
                phase = 4; next = Time.time + 1;
            }
            else
            {
                var runner = combat.GetComponent<CannonSpiralBarrage>();
                Check(runner.ActiveCount == 0 && runner.PoolSize == 1 && feedback.ActiveEffects == 0
                    && feedback.PoolSize == initialPool && combat.AmmoRemaining(MechWeaponType.Cannon) == 19,
                    "Live sequence and effects clean up without growing the prewarmed pool or consuming extra ammo");
                Debug.Log("SPIRAL SCENE CHECKS COMPLETE");
                SessionState.SetBool(Key, false);
                EditorApplication.Exit(0);
            }
        }
        catch (Exception e) { Debug.LogException(e); SessionState.SetBool(Key, false); EditorApplication.Exit(1); }
    }
}
