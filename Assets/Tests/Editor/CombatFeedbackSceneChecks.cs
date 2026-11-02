using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Invoke in a temporary copy of the project, never in the user's interactive scene.
public static class CombatFeedbackSceneChecks
{
    const string Key = "CombatFeedbackSceneChecks.Pending";
    static MechCombat combat;
    static CombatFeedback feedback;
    static MechTargetLockController targeting;
    static MechTargetable[] enemies;
    static float next = -1, deadline;
    static int phase, deaths, impacts, initialPoolSize;
    static MechProjectileImpact impact;
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); Debug.Log("COMBAT FEEL SCENE PASS: " + message); }

    [InitializeOnLoadMethod]
    static void Register() => EditorApplication.update += Tick;

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch checks only.");
        EditorSceneManager.OpenScene("Assets/Scenes/A33.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (next < 0) { next = Time.time + 2; deadline = Time.time + 20; }
        if (Time.time < next) return;
        try
        {
            if (Time.time > deadline) throw new Exception("Feedback scene timed out, phase " + phase);
            if (phase == 0)
            {
                // Screenshots must not advance an effect's entire lifetime in one batch frame.
                Time.captureDeltaTime = 1f / 60f;
                combat = Object.FindAnyObjectByType<MechCombat>();
                feedback = combat.GetComponent<CombatFeedback>();
                targeting = combat.GetComponent<MechTargetLockController>();
                Check(feedback != null && feedback.isActiveAndEnabled, "Actual mech prefab has enabled feedback");
                initialPoolSize = feedback.PoolSize;
                enemies = Object.FindObjectsByType<MechTargetable>();
                Check(enemies.Length >= 3, "Scene supplies a group of test enemies");
                foreach (var enemy in enemies)
                {
                    Check(enemy.GetComponent<EnemyCombatFeedback>() != null, "Scene enemy has attached damage/death presentation");
                    var movement = enemy.GetComponent<AutoMoveTarget>();
                    if (movement != null) movement.enabled = false;
                    enemy.transform.position += Vector3.right * 1000;
                }
                for (int i = 0; i < 3; i++)
                {
                    Vector3 local = i == 0 ? new Vector3(0, 0, 12) : new Vector3(i == 1 ? -1.5f : 1.5f, 0, 14);
                    enemies[i].transform.position += combat.transform.TransformPoint(local) - enemies[i].AimPosition;
                    enemies[i].GetComponent<CombatHealth>().Died += () => deaths++;
                }
                Physics.SyncTransforms();
                targeting.ResumeTargeting();
                targeting.ToggleHardLock();
                combat.ProjectileImpact += value => { impacts++; impact = value; };
                phase = 1; next = Time.time + 0.5f;
            }
            else if (phase == 1)
            {
                Capture("before");
                Check(combat.TryAttack(PartCategory.ShoulderL).Projectile != null && feedback.ActiveEffects == 1, "Firing activates the muzzle effect in the real scene");
                Capture("fire");
                phase = 2;
            }
            else if (phase == 2)
            {
                if (impacts == 0) return;
                Check(impacts == 1 && impact.Kills == 3 && deaths == 3, "Actual scene cannon resolves three deaths in one blast");
                Check(feedback.KillConfirmationCount == 1 && feedback.LastConfirmedKills == 3, "Actual HUD receives one triple-kill confirmation");
                for (int i = 0; i < 3; i++) Check(!enemies[i].gameObject.activeInHierarchy, "Gameplay enemy deactivated immediately");
                Check(feedback.ActiveEffects >= 4, "Explosion and detached death effects remain visible after roots deactivate");
                Check(targeting.IsHardLockMode, "Impact presentation preserves hard-lock mode");
                Capture("impact");
                phase = 3; next = Time.time + 0.2f;
            }
            else if (phase == 3)
            {
                Capture("afterglow");
                phase = 4; next = Time.time + 1f;
            }
            else
            {
                Check(feedback.ActiveEffects == 0 && feedback.PoolSize == initialPoolSize,
                    "Scene effects finish and reuse the prewarmed pool without growth");
                Check(combat.AmmoRemaining(PartCategory.ShoulderL) == (int)combat.GetPart(PartCategory.ShoulderL).Stats.GetStat(StatType.Ammo) - 1,
                    "Presentation consumes only the original single cannon round");
                Capture("settled");
                Debug.Log("COMBAT FEEL SCENE CHECKS COMPLETE");
                SessionState.SetBool(Key, false);
                EditorApplication.Exit(0);
            }
        }
        catch (Exception e) { Debug.LogException(e); SessionState.SetBool(Key, false); EditorApplication.Exit(1); }
    }

    static void Capture(string label)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        var camera = Camera.main;
        var canvases = Object.FindObjectsByType<Canvas>();
        var modes = new RenderMode[canvases.Length];
        for (int i = 0; i < canvases.Length; i++)
        {
            modes[i] = canvases[i].renderMode;
            canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
            canvases[i].worldCamera = camera;
            canvases[i].planeDistance = 1;
        }
        Canvas.ForceUpdateCanvases();
        var texture = new RenderTexture(1280, 720, 24);
        camera.targetTexture = texture;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = texture });
        var previous = RenderTexture.active;
        RenderTexture.active = texture;
        var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        image.Apply();
        File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "Gam3333-feedback-" + label + ".png"), image.EncodeToPNG());
        RenderTexture.active = previous;
        camera.targetTexture = null;
        for (int i = 0; i < canvases.Length; i++) canvases[i].renderMode = modes[i];
        texture.Release();
        Object.Destroy(texture);
        Object.Destroy(image);
    }
}
