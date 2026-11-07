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
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Run in a disposable project. Uses real physics, authored weapon rigs and the shipped health prefab.
public static class CombatPolishChecks
{
    const string Pending = "CombatPolishChecks.Pending";
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static bool running; static double stable, deadline; static int checks, frame = -1;
    static object Get(object target, string name) => target.GetType().GetField(name, Private).GetValue(target);
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Private).Invoke(target, args);
    [InitializeOnLoadMethod] static void Register() { stable = EditorApplication.timeSinceStartup + 3; EditorApplication.update += Tick; }
    public static void RunBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !root.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only");
        SessionState.SetBool(Pending, true); stable = EditorApplication.timeSinceStartup + 3;
    }
    static void Tick()
    {
        try
        {
            if (SessionState.GetBool(Pending, false) && !EditorApplication.isPlaying)
            {
                if (EditorApplication.isCompiling) { stable = EditorApplication.timeSinceStartup + 3; return; }
                if (EditorApplication.timeSinceStartup < stable) return;
                SessionState.SetBool(Pending, false); CombatPolishSetup.Apply();
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
                GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
                QualitySettings.renderPipeline = null; running = true; deadline = EditorApplication.timeSinceStartup + 180;
                EditorApplication.EnterPlaymode();
            }
            if (!running || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
            frame = Time.frameCount;
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Combat polish timed out");
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (steps.Count == 0) steps.Push(Run());
            while (steps.Count > 0)
            {
                var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; } return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        running = false; Time.timeScale = 1; PreMissionReliabilityChecks.EndLogAudit();
        if (error != null) Debug.LogException(error);
        Debug.Log("COMBAT POLISH CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("POLISH PASS: " + message); }
    static IEnumerator Frames(int count = 2) { int until = Time.frameCount + count; while (Time.frameCount < until) yield return null; }
    static IEnumerator Delay(float seconds) { float until = Time.unscaledTime + seconds; while (Time.unscaledTime < until) yield return null; }
    static IEnumerator GameDelay(float seconds) { float until = Time.time + seconds; while (Time.time < until) yield return null; }
    static int Effects(CombatFeedback feedback, CombatFeedbackEffect.Kind kind) =>
        ((GameObject)Get(feedback, "poolRoot")).GetComponentsInChildren<CombatFeedbackEffect>(true)
        .Count(effect => effect.IsPlaying && (CombatFeedbackEffect.Kind)Get(effect, "kind") == kind);
    static WeaponVisualSettings Profile(Part part, Material material)
    {
        var profile = part.gameObject.AddComponent<WeaponVisualSettings>(); profile.material = material; return profile;
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.timeScale = 1;
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Combat/Prototype Combat.mat");
        var smoke = AssetDatabase.LoadAssetAtPath<Material>("Assets/Combat/Weapon Smoke.mat");
        Check(smoke != null && !ShaderUtil.ShaderHasError(smoke.shader), "Smoke material uses a successfully compiled soft-puff shader");
        foreach (var name in new[] { "arm_sword", "arm_mace", "arm_crossbow", "arm_throwingaxe", "shoulder_cannon", "shoulder_crossbow" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/" + name + ".prefab");
            var settings = prefab.GetComponent<WeaponVisualSettings>();
            Check(settings != null && settings.material == material && settings.trailAnchor != null,
                name + " has authored presentation settings and an animated trail/muzzle anchor");
        }
        var cannonProfile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/shoulder_cannon.prefab").GetComponent<WeaponVisualSettings>();
        var boltProfile = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/shoulder_crossbow.prefab").GetComponent<WeaponVisualSettings>();
        Check(boltProfile.firingShake < cannonProfile.firingShake && boltProfile.impactSize < cannonProfile.impactSize,
            "Large crossbow retains lighter presentation than the cannon despite its existing splash-damage subtype");
        var player = new GameObject("Polish fixture player"); player.SetActive(false); player.transform.position = Vector3.up;
        var health = player.AddComponent<CombatHealth>(); health.Team = CombatTeam.Player;
        var combat = player.AddComponent<MechCombat>();
        var parts = TestMech.Equip(player, (PartCategory.ArmL, TestMech.Ranged("Bolt", PartType.Arm, PartSubtype.Projectile, 5, 100, .1f, 30, 20, .06f)),
            (PartCategory.ShoulderL, TestMech.Cannon()), (PartCategory.ArmR, TestMech.Sword(5)));
        foreach (var part in parts) Profile(part, material);
        var feedback = player.AddComponent<CombatFeedback>(); Set(feedback, "effectMaterial", material); Set(feedback, "smokeMaterial", smoke);
        Set(feedback, "maximumEffects", 16); Set(feedback, "maximumTrails", 4);
        var vignette = player.AddComponent<PlayerDamageVignette>();
        var cameraRoot = new GameObject("Polish camera"); cameraRoot.SetActive(false);
        var camera = cameraRoot.AddComponent<Camera>(); camera.tag = "MainCamera";
        var control = cameraRoot.AddComponent<CameraControlThirdPerson>(); Set(control, "target", player.transform);
        Set(feedback, "cameraControl", control); Set(combat, "gameplayCamera", camera); Set(combat, "combatMaterial", material);
        cameraRoot.SetActive(true); player.SetActive(true); yield return Frames();
        var rig = (CinemachineCamera)Get(control, "cinemachineCamera"); var follow = rig.Follow; var look = rig.LookAt; float fov = rig.Lens.FieldOfView;
        int impacts = 0; MechProjectileImpact impact = default; combat.ProjectileImpact += value => { impacts++; impact = value; };
        var shot = combat.TryAttack(PartCategory.ArmL).Projectile;
        var shotProfile = parts[0].GetComponent<WeaponVisualSettings>();
        shotProfile.PlayWeaponFireVfx();   // no weapon fire vfx assigned: must do nothing
        Check(shot != null && shotProfile.weaponFireVfx == null
              && Effects(feedback, CombatFeedbackEffect.Kind.Muzzle) == 0
              && Effects(feedback, CombatFeedbackEffect.Kind.Smoke) == 0,
            "A weapon with no fire vfx plays no muzzle flash and no smoke when it fires");
        Check(feedback.FiringImpulseCount == 1 && feedback.TrailPoolSize == 1, "Successful firing emits a local camera impulse and starts a pooled projectile trail");
        int ammo = parts[0].RemainingAmmo;
        Check(!combat.TryAttack(PartCategory.ArmL).Started && parts[0].RemainingAmmo == ammo && feedback.FiringImpulseCount == 1,
            "Rejected cooldown presses produce no extra shots, effects, shake or ammo consumption");
        yield return GameDelay(.1f);
        var trail = ((GameObject)Get(feedback, "poolRoot")).GetComponentInChildren<CombatProjectileTrail>();
        Check(trail != null && trail.IsFollowing && trail.Renderer.positionCount > 1, "Trail follows the projectile's actual travel");
        Call(shot, "Step", 20f);
        Check(!shot.IsLive && impacts == 0 && trail.IsPlaying && !trail.IsFollowing, "Range expiry preserves its fading tail and emits no impact");
        yield return GameDelay(.4f); Check(trail.IsPlaying && trail.Renderer.positionCount>1 && trail.Renderer.startColor.a>0 && trail.Renderer.startColor.a<.8f,
            "Projectile path retains real geometry and visibly fades after the shot has despawned");
        yield return GameDelay(.7f); Check(!trail.IsPlaying, "Expired projectile tail returns to its pool");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Impact wall";
        wall.transform.position = new Vector3(0, 2, 8); wall.transform.localScale = new Vector3(20, 5, .2f); Physics.SyncTransforms();
        TestMech.Advance(player, 1); shot = combat.TryAttack(PartCategory.ArmL).Projectile;
        Check(trail.IsFollowing && Mathf.Approximately(trail.Renderer.time,parts[0].GetComponent<WeaponVisualSettings>().trailLifetime)
            && trail.Renderer.startColor.a>.9f && feedback.TrailPoolSize==1,"Reused trail resets lifetime and opacity without allocating another trail");
        yield return GameDelay(.12f);
        Call(shot, "Step", 1f);
        Check(impacts == 1 && Vector3.Dot(impact.Normal, Vector3.back) > .95f && Mathf.Abs(impact.ContactPoint.z - 7.9f) < .02f,
            "Projectile impact publishes the actual wall contact and outward normal once");
        Check(Effects(feedback, CombatFeedbackEffect.Kind.Dust) == 1, "Ordinary projectile impact emits oriented dust");
        Call(shot, "Step", 1f); Check(impacts == 1, "A spent projectile cannot repeat impact feedback");
        int smokeBeforeCannon=Effects(feedback,CombatFeedbackEffect.Kind.Smoke);
        TestMech.Advance(player, 1); shot = combat.TryAttack(PartCategory.ShoulderL).Projectile; Call(shot, "Step", .2f);
        // With no authored weaponFireVfx, firing creates no extra smoke (checked above).
        Check(Effects(feedback, CombatFeedbackEffect.Kind.Explosion) == 1 && Effects(feedback, CombatFeedbackEffect.Kind.Smoke) == smokeBeforeCannon+1,
            "Cannon impact reuses its blast flash, ring and embers and adds one smoke plume without unauthored firing smoke");
        yield return Capture("weapon-impact", camera, feedback, null, null);
        yield return GameDelay(.2f);
        yield return Capture("projectile-lingering-tail",camera,feedback,null,null,new Vector3(0,2,5),9);
        int localImpulses = feedback.FiringImpulseCount;
        var enemy = new GameObject("Enemy shot owner"); enemy.SetActive(false); enemy.transform.position = new Vector3(4, 1, 0);
        var enemyHealth = enemy.AddComponent<CombatHealth>(); enemyHealth.Team = CombatTeam.Enemy;
        enemy.AddComponent<MechCombat>(); var enemyPart = TestMech.Equip(enemy, (PartCategory.ArmL, TestMech.Axe()))[0]; Profile(enemyPart, material);
        enemy.SetActive(true); RangedAttack.Fire(enemyPart, enemy.transform, enemy.transform.position + Vector3.up, wall.transform.position, Vector3.forward, 30, ~0, material, null);
        Check(feedback.FiringImpulseCount == localImpulses && feedback.TrailPoolSize >= 1, "Enemy shots share VFX without shaking the local camera on enemy firing");
        enemy.SetActive(false);
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube); target.name = "Melee target"; target.transform.position = new Vector3(0, 1, 3);
        var targetHealth = target.AddComponent<CombatHealth>(); targetHealth.Team = CombatTeam.Enemy;
        var duplicate = new GameObject("Second hurt collider").AddComponent<BoxCollider>(); duplicate.transform.SetParent(target.transform, false);
        int melee = 0; Action<Part, Transform, Vector3, Vector3> onMelee = (p, o, point, normal) => melee++;
        CombatVisualEvents.MeleeImpact += onMelee; Physics.SyncTransforms();
        Check(combat.TryAttack(PartCategory.ArmR).Started && melee == 1 && targetHealth.CurrentHealth == 95,
            "Melee emits one visual impact per accepted victim despite compound colliders");
        CombatVisualEvents.MeleeImpact -= onMelee; target.SetActive(false);
        health.ApplyDamage(10); health.ApplyDamage(10);
        Check(feedback.DamageImpulseCount == 2 && health.CurrentHealth == 80, "Repeated valid damage produces independent short damage impulses without changing HP rules");
        Check(rig.Follow == follow && rig.LookAt == look && rig.Lens.FieldOfView == fov, "Feedback preserves camera follow, lock framing and lens");
        float oldTime = CinemachineCore.CurrentTimeOverride;
        try
        {
            CinemachineCore.CurrentTimeOverride = Time.time + .05f;
            Check(CinemachineImpulseManager.Instance.GetStrongestImpulseAt(camera.transform.position, false, 64, out var displacement, out var rotation)
                && displacement.magnitude <= .21f && Quaternion.Angle(rotation, Quaternion.identity) < .01f,
                "Repeated camera feedback remains bounded and has no rotational drift signal");
        }
        finally { CinemachineCore.CurrentTimeOverride = oldTime; }
        yield return GameDelay(1.5f);
        Check(feedback.ActiveEffects == 0 && ((GameObject)Get(feedback, "poolRoot")).GetComponentsInChildren<Collider>(true).Length == 0,
            "All VFX expire, remain bounded and have no gameplay colliders");
        var canvasRoot = new GameObject("Health fixture canvas", typeof(Canvas)); var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var hudRoot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.HealthHudPath), canvasRoot.transform);
        var hud = hudRoot.GetComponent<MechHealthHud>(); hud.Bind(health); health.SetMaximum(100, true);
        health.ApplyDamage(25); yield return Delay(.6f);
        Check(Mathf.Abs(hud.NormalizedHealth - .75f) < .001f && Mathf.Abs(hud.LostHealth - 1) < .001f, "White HP segment holds recently lost health for the full one-second delay");
        health.ApplyDamage(10); yield return Delay(.6f);
        Check(Mathf.Abs(hud.LostHealth - 1) < .001f, "Additional damage restarts the white-bar delay");
        yield return Delay(1.2f);
        Check(Mathf.Abs(hud.LostHealth - hud.NormalizedHealth) < .001f, "White segment drains to actual HP in the configured duration");
        health.SetMaximum(200, false);
        Check(Mathf.Abs(hud.LostHealth - .325f) < .001f && Mathf.Abs(vignette.Alpha - PlayerDamageVignette.Evaluate(.325f)) < .001f,
            "Capacity changes refresh both health displays without inventing a damage segment");
        health.SetMaximum(100, true);
        Check(vignette.Alpha == 0 && hud.LostHealth == 1, "Refill removes low-health feedback and restores both HP bars");
        health.ApplyDamage(50); Check(vignette.Alpha == 0, "Vignette is invisible at 50 percent health");
        health.ApplyDamage(25); Check(Mathf.Abs(vignette.Alpha - .8f * .25f / .49f) < .001f, "Vignette alpha rises linearly below 50 percent");
        var mask = vignette.Overlay.sprite.texture;
        Check(mask.GetPixel(mask.width / 2, mask.height / 2).a == 0 && mask.GetPixel(0, mask.height / 2).a > .99f
            && !vignette.Overlay.raycastTarget, "Vignette keeps the combat center clear and never intercepts input");
        health.ApplyDamage(24); Check(Mathf.Abs(vignette.Alpha - .8f) < .001f, "Vignette reaches 80 percent alpha at 1 percent HP");
        yield return Capture("low-health", camera, feedback, canvas, vignette);
        health.SetMaximum(100, true); Check(vignette.Alpha == 0, "Healing/refill immediately clears the vignette");
        wall.SetActive(false);
        yield return CheckMeleeRig(player, combat, material, "arm_sword");
        yield return CheckMeleeRig(player, combat, material, "arm_mace");
        Set(feedback, "presentationEnabled", false); Call(feedback, "Tick", 0f);
        int damageImpulses = feedback.DamageImpulseCount; health.ApplyDamage(1);
        Check(feedback.ActiveEffects == 0 && feedback.DamageImpulseCount == damageImpulses && control.GetCombatImpulseListener().Gain == 0,
            "Master feedback toggle clears effects and silences impulses without disabling damage");
        health.ApplyDamage(1000);
        Check(!player.activeSelf && vignette.Alpha == 0 && !hud.IsVisible, "Death clears local feedback while preserving normal gameplay deactivation");
        Object.DestroyImmediate(player); Object.DestroyImmediate(canvasRoot); Object.DestroyImmediate(cameraRoot);
        MechProjectileModelChecks.Run(Check); MissionChecks.Run(Check);
        CombatFeedbackChecks.Run(Check); MechEnemyCombatChecks.Run(Check);
    }
    static IEnumerator CheckMeleeRig(GameObject player, MechCombat combat, Material material, string name)
    {
        CombatHitStop.ClearFrom(player.GetComponent<CombatFeedback>());
        // Sample the real playable graph deterministically; graphics import stalls
        // must not skip the entire sub-second swing between editor update frames.
        float previousScale = Time.timeScale; Time.timeScale = 0;
        var weapon = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/" + name + ".prefab")).GetComponent<Part>();
        Check(player.GetComponent<EquipManager>().TryEquip(weapon, PartCategory.ArmR), "Actual " + name + " equips without modifying its authored rig");
        var driver = weapon.GetComponent<MechWeaponAnimator>(); var profile = weapon.GetComponent<WeaponVisualSettings>();
        driver.RefreshEquipment(); Check(driver.PlayAttack(1f), "Actual " + name + " uses its existing attack clip");
        float fireDuration = (float)Get(driver, "fireDuration");
        driver.Tick(fireDuration * .25f); Call(profile, "LateUpdate");
        Check(profile.IsSwingEmitting, name + " emits a trail only within its active authored swing window");
        Vector3 anchor = profile.trailAnchor.position;
        for (int i = 0; i < 3; i++) { driver.Tick(fireDuration * .08f); Call(profile, "LateUpdate"); yield return Frames(1); }
        Check(Vector3.Distance(anchor, profile.trailAnchor.position) > .01f, name + " trail anchor follows the animated weapon, not the player root");
        var ribbon = (TrailRenderer)Get(profile, "swing");
        Check(ribbon != null && ribbon.positionCount > 1, name + " leaves visible trail geometry along its animated motion");
        Check(Vector3.Distance(ribbon.transform.lossyScale, Vector3.one) < .001f
            && Vector3.Distance(ribbon.transform.position, profile.trailAnchor.position) < .001f,
            name + " keeps world-sized trail width while following the scaled animation bone");
        var meshFilter = profile.trailAnchor.GetComponentInParent<MeshFilter>();
        var skin = weapon.GetComponentInChildren<SkinnedMeshRenderer>();
        Bounds posedBounds; Vector3 localTip; Mesh posed = null;
        if (meshFilter != null) { posedBounds = meshFilter.sharedMesh.bounds; localTip = meshFilter.transform.InverseTransformPoint(profile.trailAnchor.position); }
        else { posed = new Mesh(); skin.BakeMesh(posed); posedBounds = posed.bounds; localTip = skin.transform.InverseTransformPoint(profile.trailAnchor.position); }
        posedBounds.Expand(.1f);
        Check(posedBounds.Contains(localTip),
            name + " trail tip stays inside the actual posed weapon geometry rather than the old firing offset");
        if (posed != null) Object.DestroyImmediate(posed);
        var renderers = weapon.GetComponentsInChildren<Renderer>().Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer || r is TrailRenderer)).ToArray();
        var bounds = renderers[0].bounds; foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        bounds.Encapsulate(ribbon.bounds);
        yield return Capture(name + "-trail", null, null, null, null, bounds.center, bounds.extents.magnitude * 2.3f);
        driver.Tick(fireDuration); Call(profile, "LateUpdate"); Check(!profile.IsSwingEmitting, name + " stops emitting after its swing");
        player.GetComponent<EquipManager>().Unequip(PartCategory.ArmR); Call(profile, "LateUpdate");
        Check(!profile.IsSwingEmitting, name + " cannot leave a trail running when dropped");
        Object.DestroyImmediate(weapon.gameObject); Time.timeScale = previousScale; yield return Frames();
    }
    static IEnumerator Capture(string name, Camera camera, CombatFeedback feedback, Canvas healthCanvas, PlayerDamageVignette vignette, Vector3? center = null, float distance = 6)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
        var preview = new GameObject("Polish preview camera").AddComponent<Camera>();
        preview.transform.SetPositionAndRotation(new Vector3(11, 7, -9), Quaternion.LookRotation(new Vector3(-9, -4, 15)));
        if (center.HasValue) { preview.transform.position = center.Value + new Vector3(1, .5f, -1).normalized * Mathf.Max(3, distance); preview.transform.LookAt(center.Value); }
        preview.backgroundColor = new Color(.08f, .1f, .13f); preview.clearFlags = CameraClearFlags.SolidColor;
        var light = new GameObject("Polish preview light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(45, 30, 0);
        var canvases = new List<Canvas>(); if (healthCanvas != null) canvases.Add(healthCanvas);
        if (vignette != null && vignette.Overlay != null) canvases.Add(vignette.Overlay.GetComponentInParent<Canvas>());
        foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = preview; canvas.planeDistance = 1; }
        var target = new RenderTexture(1280, 720, 24); preview.targetTexture = target; yield return Frames();
        preview.Render(); var old = RenderTexture.active; RenderTexture.active = target;
        var picture = new Texture2D(1280, 720, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); picture.Apply();
        var folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "CombatPolishCaptures"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), picture.EncodeToPNG());
        RenderTexture.active = old; preview.targetTexture = null; target.Release();
        foreach (var canvas in canvases) { canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null; }
        Object.DestroyImmediate(picture); Object.DestroyImmediate(target); Object.DestroyImmediate(preview.gameObject); Object.DestroyImmediate(light.gameObject);
    }
}
