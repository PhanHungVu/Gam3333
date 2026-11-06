using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class DirectionalDamageChecks
{
    const string Pending = "DirectionalDamageChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static int frame = -1, checks;
    static bool running;
    static double deadline;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; Debug.Log("DAMAGE DIRECTION PASS: " + message); }
    static object Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Private).Invoke(owner, args);
    static IEnumerator Frames(int count = 3) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Delay(float seconds) { float end = Time.unscaledTime + seconds; while (Time.unscaledTime < end) yield return null; }
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        DirectionalDamageHudSetup.Apply();
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>(DirectionalDamageHudSetup.HudPath);
        Check(hud.GetComponentsInChildren<DirectionalDamageHud>(true).Length == 1, "Shared HUD has one authored directional-feedback prefab");
        DirectionalDamageHudSetup.Apply();
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(DirectionalDamageHudSetup.HudPath).GetComponentsInChildren<DirectionalDamageHud>(true).Length == 1,
            "Reapplying setup preserves one HUD and existing tuning");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; deadline = EditorApplication.timeSinceStartup + 240; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Directional feedback checks timed out.");
            while (steps.Count > 0)
            {
                var step = steps.Peek();
                if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; }
                return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit(); Time.timeScale = 1;
        if (error != null) Debug.LogException(error);
        Debug.Log("DAMAGE DIRECTION CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static CombatHealth Target(Vector3 position, CombatTeam team = CombatTeam.Player)
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Cube); root.transform.position = position;
        var health = root.AddComponent<CombatHealth>(); health.Team = team; health.SetMaximum(1000, true); return health;
    }
    static IEnumerator ClearScene()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) Object.Destroy(root);
        yield return Frames();
    }
    static void SourceChecks()
    {
        var origin = new Vector3(12000, 30, 0);
        var victim = Target(origin); var received = new List<CombatDamageInfo>(); int oldEvents = 0;
        victim.DamageReceived += received.Add; victim.Damaged += _ => oldEvents++;
        Check(!victim.ApplyDamage(0) && !victim.ApplyDamage(-1) && !victim.ApplyDamage(float.NaN) && !victim.ApplyDamage(float.PositiveInfinity)
            && received.Count == 0, "Rejected damage emits no source event");
        victim.ApplyDamage(2);
        Check(received.Count == 1 && !received[0].SourcePosition.HasValue && oldEvents == 1, "Original ApplyDamage(float) and Damaged subscribers remain compatible without inventing a source");
        victim.ApplyDamage(3, origin + Vector3.left * 10);
        Check(received.Count == 2 && received[1].Amount == 3 && received[1].SourcePosition == origin + Vector3.left * 10 && oldEvents == 2,
            "Confirmed damage delivers the applied loss and source exactly once");
        victim.SetMaximum(2000, true);
        Check(received.Count == 2, "Healing and maximum-HP changes never fabricate a hit");
        victim.ApplyDamage(1, new Vector3(float.NaN, 0, 0));
        Check(!received.Last().SourcePosition.HasValue, "Invalid source coordinates degrade to non-directional damage");
        victim.ApplyDamage(5000, origin + Vector3.forward);
        Check(received.Last().Amount == 1999 && oldEvents == 4 && victim.IsDead && !victim.ApplyDamage(1), "Lethal source event reports only actual HP lost and existing death behavior remains unchanged");

        victim = Target(origin + Vector3.right * 100); received.Clear(); victim.DamageReceived += received.Add;
        var owner = Target(victim.transform.position + Vector3.back * 5, CombatTeam.Enemy);
        var blast = new CombatBlast(); Physics.SyncTransforms();
        blast.Resolve(victim.transform.position, 0, 2, 0, owner.transform, ~0, victim.GetComponent<Collider>());
        Check(received.Last().SourcePosition == owner.transform.position, "Existing direct-hit callers retain the attacker's source position");
        var impact = victim.transform.position + Vector3.left;
        blast.Resolve(impact, 2, 1, 2, owner.transform, ~0, victim.GetComponent<Collider>());
        Check(received.Last().SourcePosition == impact, "Blast damage points at the impact center rather than a distant shooter");
        blast.ResolveBox(victim.transform.position, Vector3.one, Quaternion.identity, 2, owner.transform, ~0, owner.transform.position);
        Check(received.Last().SourcePosition == owner.transform.position, "Melee hit boxes propagate their real attack origin");
        blast.ResolveCylinder(impact, 2, 2, 2, owner.transform, ~0);
        Check(received.Last().SourcePosition == impact, "Circular attacks propagate their area origin");
        int before = received.Count; owner.Team = CombatTeam.Player;
        blast.Resolve(victim.transform.position, 0, 2, 0, owner.transform, ~0, victim.GetComponent<Collider>());
        Check(received.Count == before, "Friendly-fire rejection produces no directional cue"); owner.Team = CombatTeam.Enemy;
        var muzzle = victim.transform.position + Vector3.back * 6;
        var projectile = MechProjectile.Create(null, "Direction test bolt", muzzle, .1f, null, Color.white);
        projectile.Launch(owner.transform, Vector3.forward, 30, 2, .1f, 30, ~0, muzzle);
        owner.transform.position += Vector3.right * 20; Physics.SyncTransforms();
        Call(projectile, "Step", .3f);
        Check(received.Count == before + 1 && Mathf.Abs(received.Last().SourcePosition.Value.x - victim.transform.position.x) < .05f
            && received.Last().SourcePosition.Value.z < victim.transform.position.z,
            "Real projectile impact uses its incoming trajectory after the shooter moves sideways");
    }
    static DamageDirectionGraphic Visible(DirectionalDamageHud hud) => hud.Indicators.First(i => i.enabled);
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.timeScale = 1;
        DamageChecks.Run(Check); SourceChecks(); yield return ClearScene();
        foreach (var variant in new[] { "golem", "heavy" })
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/FullPlayer_mech_" + variant + ".prefab"), new Vector3(0, 3, 0), Quaternion.identity);
            player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
            player.GetComponent<CombatFeedback>().enabled = false;
            yield return Frames(6);
            var rig = player.GetComponent<MechPlayerRig>(); var camera = rig.GameplayCamera;
            camera.GetComponent<CameraControlThirdPerson>().enabled = false;
            var brain = camera.GetComponent<CinemachineBrain>(); if (brain != null) brain.enabled = false;
            camera.transform.SetPositionAndRotation(new Vector3(0, 8, -15), Quaternion.Euler(20, 0, 0));
            var health = player.GetComponent<CombatHealth>(); health.SetMaximum(1000, true);
            var hud = Object.FindAnyObjectByType<DirectionalDamageHud>();
            Check(hud != null && hud.Player == rig && Object.FindObjectsByType<DirectionalDamageHud>().Length == 1,
                variant + " automatically receives one shared HUD bound to its own health");
            player.transform.rotation = Quaternion.Euler(0, 180, 0);
            Vector3[] world = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
            Vector2[] screen = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
            for (int i = 0; i < world.Length; i++)
            {
                hud.Clear(); health.ApplyDamage(1, player.transform.position + world[i] * 10); yield return Frames(2);
                Check(hud.ActiveIndicatorCount == 1 && Vector2.Dot(Visible(hud).rectTransform.anchoredPosition.normalized, screen[i]) > .99f,
                    variant + " camera-relative " + new[] { "front", "right", "behind", "left" }[i] + " is correct despite the reversed player body");
            }
            hud.Clear(); health.ApplyDamage(1, player.transform.position + Vector3.forward * 10); yield return Frames(2);
            var original = Visible(hud); camera.transform.rotation = Quaternion.Euler(20, 90, 0); yield return Frames(2);
            Check(hud.ActiveIndicatorCount == 1 && Visible(hud) == original && Visible(hud).rectTransform.anchoredPosition.x < 0
                && Mathf.Abs(Visible(hud).rectTransform.anchoredPosition.y) < 1, "An existing cue rotates with the actual gameplay camera");
            camera.transform.rotation = Quaternion.Euler(89.999f, 90, 0);
            Check(DirectionalDamageHud.TryScreenDirection(camera.transform, Vector3.right, out var top) && top.y > .99f,
                "Near-vertical camera pitch keeps a valid horizontal bearing");
            camera.transform.rotation = Quaternion.Euler(20, 0, 0); hud.Clear();
            health.ApplyDamage(1, player.transform.position + Vector3.forward * 10); yield return Frames(2); original = Visible(hud);
            health.ApplyDamage(1, player.transform.position + new Vector3(1, 0, 10)); yield return Frames(2);
            Check(hud.ActiveIndicatorCount == 1 && Visible(hud) == original, "Nearby incoming directions refresh one existing indicator");
            hud.Clear(); foreach (var direction in world) health.ApplyDamage(1, player.transform.position + direction * 10); yield return Frames(2);
            Check(hud.ActiveIndicatorCount == 4 && hud.Indicators.Count(i => i.enabled) == 4, "Four different sources can remain independently visible");
            if (variant == "golem") { yield return Capture(hud, 1280, 720); yield return Capture(hud, 960, 720); }
            for (int i = 0; i < 100; i++) health.ApplyDamage(.1f, player.transform.position + Quaternion.Euler(0, i * 41, 0) * Vector3.forward * 10);
            yield return Frames(2);
            Check(hud.ActiveIndicatorCount <= 4 && hud.GetComponentsInChildren<DamageDirectionGraphic>(true).Length == 8,
                "A hundred rapid hits reuse the fixed pool instead of creating UI objects");
            hud.Clear(); var config = new SerializedObject(hud); config.FindProperty("maximumIndicators").intValue = 2; config.ApplyModifiedPropertiesWithoutUndo();
            foreach (var direction in world) health.ApplyDamage(1, player.transform.position + direction * 10); yield return Frames(2);
            Check(hud.ActiveIndicatorCount == 2, "The configured simultaneous-indicator cap is respected");
            config.FindProperty("maximumIndicators").intValue = 4; config.ApplyModifiedPropertiesWithoutUndo(); hud.Clear();
            health.ApplyDamage(1); health.ApplyDamage(1, player.transform.position); health.ApplyDamage(1, player.transform.position + Vector3.up * 10);
            health.ApplyDamage(0, player.transform.position + Vector3.left); health.SetMaximum(1000, true); yield return Frames(2);
            Check(hud.ActiveIndicatorCount == 0, "Unknown, same-position, vertical-only, rejected hits and healing invent no horizontal direction");
            var other = Target(new Vector3(100, 3, 0)); other.ApplyDamage(1, Vector3.left); yield return Frames(2);
            Check(hud.ActiveIndicatorCount == 0, "Damage to an unrelated actor never appears on this player's HUD");
            hud.Clear(); health.ApplyDamage(1, player.transform.position + Vector3.back * 10); yield return Frames(2);
            yield return Delay(.26f);
            Check(Visible(hud).color.a > 0 && Visible(hud).color.a < .95f, "Indicator has a smooth visible fade after its short hold");
            yield return Delay(.35f);
            Check(hud.ActiveIndicatorCount == 0 && hud.Indicators.All(i => !i.enabled), "All indicators finish fading within the configured half-second lifetime");
            var target = Target(player.transform.position + Vector3.forward * 15, CombatTeam.Enemy); var lockTarget = target.gameObject.AddComponent<MechTargetable>();
            var targeting = player.GetComponent<MechTargetLockController>();
            targeting.ResumeTargeting(); Call(targeting, "SetTarget", lockTarget); if (!targeting.IsHardLockMode) targeting.ToggleHardLock();
            hud.Clear(); health.ApplyDamage(1, player.transform.position + Vector3.back * 10); yield return Frames(2);
            Check(targeting.IsHardLocked && targeting.CurrentTarget == lockTarget && Visible(hud).rectTransform.anchoredPosition.y < 0,
                "A rear hit stays at the bottom while hard-locked to an enemy in front");
            targeting.EnterFreeAim(); hud.Clear(); health.ApplyDamage(1, player.transform.position + Vector3.left * 10); yield return Frames(2);
            Check(Visible(hud).rectTransform.anchoredPosition.x < 0, "Free aim retains the same camera-relative direction");
            hud.gameObject.SetActive(false); health.ApplyDamage(1, player.transform.position + Vector3.back); hud.gameObject.SetActive(true); yield return Frames();
            Check(hud.ActiveIndicatorCount == 0 && hud.Player == rig, "HUD disable and re-enable clear old hits and bind without replaying them");
            health.ApplyDamage(2000, player.transform.position + Vector3.back); yield return Frames();
            Check(hud.ActiveIndicatorCount == 0 && hud.Indicators.All(i => !i.enabled), "Player death clears indicators and leaves no stuck overlays");
            yield return ClearScene();
        }
        EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/EnemyBehaviorPlaytest.unity", new LoadSceneParameters(LoadSceneMode.Single));
        yield return Frames(10);
        var sceneHud = Object.FindAnyObjectByType<DirectionalDamageHud>(); var scenePlayer = Object.FindAnyObjectByType<MechPlayerRig>();
        Check(sceneHud != null && sceneHud.Player == scenePlayer && Object.FindObjectsByType<DirectionalDamageHud>().Length == 1,
            "Existing enemy behavior playtest scene loads the new shared HUD without scene modifications");
        scenePlayer.GetComponent<MechPlayerInput>().enabled = false;
        scenePlayer.GetComponent<CombatFeedback>().enabled = false;
        var movement = scenePlayer.GetComponent<MechMovementController>(); var cc = scenePlayer.GetComponent<CharacterController>();
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = scenePlayer.transform.position + Vector3.down * 4;
        floor.transform.localScale = new Vector3(100, 1, 100); Physics.SyncTransforms(); cc.Move(Vector3.down * 10);
        movement.Tick(new MechLocomotionInput { quickBoost = true, move = Vector2.up }, scenePlayer.GameplayCamera.transform, .02f);
        Check(movement.IsQuickBoosting, "Controlled scene check uses the actual dodge/boost movement state");
        sceneHud.Clear(); Call(sceneHud, "DebugBack"); yield return Frames(2);
        Check(sceneHud.ActiveIndicatorCount == 1 && Visible(sceneHud).rectTransform.anchoredPosition.y < 0,
            "Confirmed damage during an actual dodge still points behind the gameplay camera");
        movement.Tick(default(MechLocomotionInput), scenePlayer.GameplayCamera.transform, 1f);
        cc.Move(Vector3.down * 10);
        movement.Tick(new MechLocomotionInput { assaultBoost = true, move = Vector2.up }, scenePlayer.GameplayCamera.transform, .02f);
        Check(movement.IsAssaultBoosting, "Controlled scene check enters the existing sustained assault-boost state");
        sceneHud.Clear(); var sceneHealth = scenePlayer.GetComponent<CombatHealth>();
        var side = Vector3.ProjectOnPlane(scenePlayer.GameplayCamera.transform.right, Vector3.up).normalized;
        sceneHealth.ApplyDamage(1, scenePlayer.transform.position + side * 10);
        sceneHealth.ApplyDamage(1, scenePlayer.transform.position - side * 10);
        movement.Tick(new MechLocomotionInput { assaultBoost = true, move = Vector2.up }, scenePlayer.GameplayCamera.transform, .1f);
        yield return Frames(2);
        Check(sceneHud.ActiveIndicatorCount == 2 && sceneHud.Indicators.Any(i => i.enabled && i.rectTransform.anchoredPosition.x > 0)
            && sceneHud.Indicators.Any(i => i.enabled && i.rectTransform.anchoredPosition.x < 0),
            "Different incoming directions remain readable while actual assault-boost movement advances the player");
    }
    static IEnumerator Capture(DirectionalDamageHud hud, int width, int height)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
        var canvas = hud.GetComponentInParent<Canvas>().rootCanvas;
        var previousMode = canvas.renderMode; var previousCamera = canvas.worldCamera;
        var review = new GameObject("Damage direction capture camera").AddComponent<Camera>();
        review.clearFlags = CameraClearFlags.SolidColor; review.backgroundColor = new Color(.025f, .035f, .045f);
        review.cullingMask = 1 << LayerMask.NameToLayer("UI");
        var target = new RenderTexture(width, height, 24); review.targetTexture = target;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = review; canvas.planeDistance = 1;
        var scaler = canvas.GetComponent<CanvasScaler>(); if (scaler != null) { scaler.enabled = false; scaler.enabled = true; }
        Canvas.ForceUpdateCanvases(); Call(hud, "LateUpdate"); Canvas.ForceUpdateCanvases(); review.Render();
        var previous = RenderTexture.active; RenderTexture.active = target;
        var picture = new Texture2D(width, height, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, width, height), 0, 0); picture.Apply();
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "DirectionalDamageCaptures"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "directions-" + width + "x" + height + ".png"), picture.EncodeToPNG());
        int left = 0, right = 0, top = 0, bottom = 0, center = 0;
        var pixels = picture.GetPixels32();
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            var p = pixels[y * width + x]; if (p.r < 100 || p.g > 60 || p.b > 60) continue;
            if (x < width * .12f) left++; if (x > width * .88f) right++; if (y < height * .15f) bottom++; if (y > height * .85f) top++;
            if (x > width * .25f && x < width * .75f && y > height * .25f && y < height * .75f) center++;
        }
        Check(left > 10 && right > 10 && top > 10 && bottom > 10 && center == 0,
            width + "x" + height + " rendered red cues reach all four edges and keep the center clear (L/R/T/B/center "
            + left + "/" + right + "/" + top + "/" + bottom + "/" + center + ")");
        RenderTexture.active = previous; canvas.renderMode = previousMode; canvas.worldCamera = previousCamera;
        review.targetTexture = null; target.Release(); Object.DestroyImmediate(picture); Object.DestroyImmediate(target); Object.DestroyImmediate(review.gameObject);
    }
}
