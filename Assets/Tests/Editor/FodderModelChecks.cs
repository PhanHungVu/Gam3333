using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class FodderModelChecks
{
    const string Pending = "FodderModelChecks.Pending";
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static int frame = -1, checks; static bool running; static double deadline;
    static void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; Debug.Log("FODDER MODEL PASS: " + message); }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; deadline = EditorApplication.timeSinceStartup + 300; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Fodder model checks timed out");
            while (steps.Count > 0) { var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; } if (step.Current is IEnumerator child) { steps.Push(child); continue; } return; }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        running = false; SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit();
        if (error != null) Debug.LogException(error);
        Debug.Log("FODDER MODEL CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
    static IEnumerator Frames(int count = 3) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Seconds(float seconds) { double end = Time.timeAsDouble + seconds; while (Time.timeAsDouble < end) yield return null; }
    static IEnumerator Until(Func<bool> condition, float seconds, string message)
    { double end = Time.timeAsDouble + seconds; while (!condition()) { if (Time.timeAsDouble > end) throw new Exception(message); yield return null; } }
    static IEnumerator Run()
    {
        var legacy = AssetDatabase.LoadAssetAtPath<GameObject>(FodderModelSetup.CrossbowPath);
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(legacy, out string guid, out long id);
        Check(guid == "01499eb7afac19d479cb6df1d4aae7e9" && id == 7062185459325199375, "Legacy small-crossbow prefab keeps its exact GUID/root fileID for saved mission references");
        Check(legacy.GetComponent<EnemyFodderAnimation>() != null && legacy.GetComponent<EnemyGraphController>().Role == EnemyBehaviorRole.Crossbow,
            "Existing crossbow references now resolve the Fodder model while keeping the ranged AI role");
        foreach (string name in new[] { "Roaming", "KOTH", "Escort", "Elimination", "Training", "Zoo" })
        {
            var wave = AssetDatabase.LoadAssetAtPath<WaveData>("Assets/LevelCatalogue/WaveData_" + name + ".asset");
            Check(wave != null && wave.enemies.Any(e => e.prefab == legacy), name + " retains a valid existing ranged-enemy prefab reference");
        }
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = new Vector3(0, -.5f, 0); floor.transform.localScale = new Vector3(100, 1, 100);
        var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath), new Vector3(0, .1f, 0), Quaternion.identity);
        yield return Frames(6); player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        player.GetComponent<MechTargetLockController>().enabled = false; player.GetComponent<CombatHealth>().Team = CombatTeam.Player;
        player.GetComponent<CombatHealth>().SetMaximum(100000, true);
        player.GetComponent<MeleeBoxPreview>().enabled = false;
        foreach (var renderer in player.GetComponentsInChildren<Renderer>()) renderer.enabled = false; // Unobstructed review captures only.
        foreach (var canvas in Object.FindObjectsByType<Canvas>()) canvas.enabled = false;
        foreach (string path in new[] { FodderModelSetup.FodderPath, FodderModelSetup.CrossbowPath })
        {
            string label = Path.GetFileNameWithoutExtension(path);
            var gate = new GameObject("Controlled Fodder fixture"); gate.SetActive(false);
            var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), gate.transform);
            root.transform.SetPositionAndRotation(new Vector3(0, 1, 6), Quaternion.Euler(0, 180, 0));
            root.GetComponent<EnemyGraphController>().enabled = false; root.GetComponent<Unity.Behavior.BehaviorGraphAgent>().enabled = false;
            root.GetComponent<EnemyPartDrop>().enabled = false;
            gate.SetActive(true); yield return Frames(6);
            var agent = root.GetComponent<EnemyAgent>(); agent.PreferredTarget = player.transform; agent.RefreshTarget();
            var attack = root.GetComponent<EnemyAttack>(); var presentation = root.GetComponent<EnemyFodderAnimation>(); var health = root.GetComponent<CombatHealth>(); health.SetMaximum(500, true);
            var animator = presentation.Animator;
            Check(animator != null && animator.transform.IsChildOf(root.transform) && animator.runtimeAnimatorController.name == "Enemy_Fodder" && !animator.applyRootMotion,
                label + " uses its own imported body Animator without taking over movement");
            Check(attack.Weapons.Count == 1 && attack.Weapon.GetComponent<Part>().Stats.partId == "backweaponA3" && attack.Weapon.GetComponentInParent<EnemyAttack>() == attack,
                label + " uses the actual Zoo Specialist cannon loadout, with no stale source-prefab weapon references");
            Check(root.GetComponentsInChildren<Transform>().Any(t => t.name == "ENEMY_Fodder") && root.GetComponentsInChildren<MechPartPoseReceiver>().Length == 0,
                label + " contains the new Fodder rig rather than the legacy golem/crossbow body");
            var box = root.transform.Find("Fodder Hurtbox").GetComponent<BoxCollider>(); Physics.SyncTransforms();
            Check(box.enabled && box.bounds.min.y > -.05f && box.bounds.max.y > .7f && root.GetComponent<MechTargetable>().AimPosition.y > .3f,
                label + " keeps its solid hurtbox and aim point above the physical floor");
            Check(presentation.CurrentPose == EnemyFodderAnimation.Pose.Locomotion && animator.GetCurrentAnimatorStateInfo(0).IsName("Locomotion"), label + " starts in the authored idle/locomotion state");
            yield return Capture(label + "-idle", root);
            var initialRotation = root.transform.rotation;
            Check(agent.ApplyMeleePush(root.transform.right * 5, .5f), label + " accepts grounded lateral movement for strafe validation");
            yield return Seconds(.12f);
            Check(animator.GetFloat("MoveX") > .25f && Mathf.Abs(animator.GetFloat("MoveZ")) < .2f, label + " selects strafing from actual local motor velocity");
            yield return Capture(label + "-strafe", root); yield return Seconds(.6f);
            root.transform.SetPositionAndRotation(new Vector3(0, 1, 6), initialRotation); Physics.SyncTransforms(); agent.RefreshTarget();
            int shots = 0; Vector3 shotOrigin = default;
            Action<MechProjectile, Vector3, Transform> observe = (shot, origin, owner) => { if (owner == root.transform) { shots++; shotOrigin = origin; } };
            CombatVisualEvents.ShotReleased += observe;
            Check(attack.TryAttack(), label + " starts its actual ranged attack through EnemyAttack");
            yield return Frames(2);
            Check(presentation.CurrentPose == EnemyFodderAnimation.Pose.ChargeStart && shots == 0, label + " shows charge start before any projectile release");
            Check(!attack.TryAttack(), label + " presentation cannot bypass the normal attack/cooldown gate");
            yield return Seconds(.34f);
            Check(presentation.CurrentPose == EnemyFodderAnimation.Pose.ChargeLoop && shots == 0, label + " holds the authored charge loop during the telegraph");
            yield return Capture(label + "-charge", root);
            yield return Until(() => shots > 0, 2, label + " shot never released"); yield return Frames(2);
            Check(shots == 1 && attack.Releases == 1 && presentation.CurrentPose == EnemyFodderAnimation.Pose.Fire, label + " firing pose follows the actual shot release, exactly once");
            Check(shotOrigin.y > .3f && Vector3.Distance(shotOrigin, attack.Weapon.GetComponent<Part>().muzzle.position) < 2,
                label + " launches from the new cannon mount above ground");
            CombatVisualEvents.ShotReleased -= observe;
            yield return Capture(label + "-fire", root); yield return Seconds(1.7f);
            Check(presentation.CurrentPose == EnemyFodderAnimation.Pose.Locomotion, label + " recovers to locomotion after the authored fire clip");
            foreach (var direction in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
            {
                var expected = direction == Vector3.forward ? EnemyFodderAnimation.Pose.HitFront : direction == Vector3.back ? EnemyFodderAnimation.Pose.HitBack
                    : direction == Vector3.left ? EnemyFodderAnimation.Pose.HitLeft : EnemyFodderAnimation.Pose.HitRight;
                var position = root.transform.position;
                health.ApplyDamage(1, root.transform.position + root.transform.TransformDirection(direction) * 10);
                yield return Frames(2);
                Check(presentation.CurrentPose == expected && Vector3.Distance(position, root.transform.position) < .01f && Quaternion.Angle(initialRotation, root.transform.rotation) < .01f,
                    label + " chooses " + expected + " from source direction without moving or tumbling the navigation root");
                yield return Seconds(.38f);
            }
            root.SetActive(false); root.SetActive(true); yield return Frames(4);
            Check(presentation.CurrentPose == EnemyFodderAnimation.Pose.Locomotion && !attack.IsAttacking, label + " pooling resets presentation without replaying the previous shot");
            var controller = root.GetComponent<EnemyGraphController>(); controller.enabled = true; controller.Graph.enabled = true;
            yield return Frames(8);
            Check(controller.IsReady && controller.Graph.Graph.IsRunning, label + " actual behavior graph runs on the new rig and remapped Self");
            controller.enabled = false; controller.Graph.enabled = false; attack.enabled = true;
            root.transform.SetPositionAndRotation(new Vector3(0, 1, 2.5f), initialRotation); Physics.SyncTransforms();
            var combat = player.GetComponent<MechCombat>();
            Check(combat.TryAttack(PartCategory.ArmL).Started && agent.IsMeleePushed, label + " still accepts the player's normal melee push through the new hurtbox");
            yield return Seconds(.15f);
            Check(Vector3.Dot(root.transform.up, Vector3.up) > .999f && Object.FindObjectsByType<CombatRagdoll>().Length == 0, label + " stays upright while the shared melee force moves it");
            var drop = root.GetComponent<EnemyPartDrop>(); var fields = new SerializedObject(drop); fields.FindProperty("dropChancePercent").floatValue = 100; fields.ApplyModifiedPropertiesWithoutUndo(); drop.enabled = true;
            health.SetMaximum(1, true); var sword = player.GetComponent<EquipManager>().GetEquipped(PartCategory.ArmL);
            new CombatBlast().ResolveBox(box.bounds.center, Vector3.one * 2, player.transform.rotation, 1000, player.transform, ~0, box.bounds.center, source: sword);
            var corpse = root.GetComponent<CombatDeathRagdoll>().LastRagdoll;
            Check(health.IsDead && corpse != null && corpse.PoseLocked && drop.LastDrop != null && corpse.HiddenLootRendererCount > 0,
                label + " keeps the upright killing launch and independent cannon loot on the new model");
            Object.Destroy(gate); foreach (var body in Object.FindObjectsByType<CombatRagdoll>()) Object.Destroy(body.gameObject);
            foreach (var loose in Object.FindObjectsByType<Part>()) if (!loose.isEquipped) Object.Destroy(loose.gameObject);
            foreach (var shot in Object.FindObjectsByType<MechProjectile>()) Object.Destroy(shot.gameObject);
            yield return Frames(4); yield return Seconds(.8f);
        }
    }
    static IEnumerator Capture(string name, GameObject actor)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
        foreach (var canvas in Object.FindObjectsByType<Canvas>()) canvas.enabled = false;
        var camera = new GameObject("Fodder review camera").AddComponent<Camera>(); var focus = actor.transform.position + Vector3.up * .2f;
        camera.transform.SetPositionAndRotation(focus + new Vector3(4, 2.8f, -5), Quaternion.LookRotation(new Vector3(-4, -2.8f, 5)));
        camera.backgroundColor = new Color(.1f, .13f, .16f); camera.clearFlags = CameraClearFlags.SolidColor;
        var light = new GameObject("Fodder review light").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(45, -35, 0);
        var render = new RenderTexture(1280, 720, 24); camera.targetTexture = render; yield return Frames(2); camera.Render();
        var previous = RenderTexture.active; RenderTexture.active = render; var picture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        picture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); picture.Apply();
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "FodderModelCaptures"); Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, name + ".png"), picture.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = null; render.Release();
        Object.Destroy(picture); Object.Destroy(render); Object.Destroy(camera.gameObject); Object.Destroy(light.gameObject);
    }
}
