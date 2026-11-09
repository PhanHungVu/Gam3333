using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

// Real prefabs, animation graphs, motor movement and damage in a disposable project.
public static class MeleeRosterAndPullChecks
{
    const string Pending = "MeleeRosterAndPullChecks.Pending";
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static int checks, frame = -1;
    static bool running;
    static double deadline;
    static GameObject player;
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("MELEE ROSTER PULL PASS: " + message); }
    static void Set(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static void Call(object target, string name) => target.GetType().GetMethod(name, Private).Invoke(target, null);
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch() => Begin(true);
    public static void RunSavedBatch() => Begin(false);
    static void Begin(bool reauthor)
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only.");
        if (reauthor) { SmallEnemyFodderSetup.Apply(); SmallEnemyFodderSetup.Apply(); }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline = null;
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; deadline = EditorApplication.timeSinceStartup + 300; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Focused melee/roster/pull checks timed out.");
            while (steps.Count > 0) { var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; } if (step.Current is IEnumerator child) { steps.Push(child); continue; } return; }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit(); Time.captureFramerate = 0; Time.timeScale = 1;
        if (error != null) Debug.LogException(error);
        Debug.Log("MELEE ROSTER PULL CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
    static IEnumerator Frames(int count = 2) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Until(Func<bool> condition, float seconds, string message)
    { float end = Time.time + seconds; while (!condition()) { if (Time.time > end) throw new Exception(message); yield return null; } }
    static GameObject Enemy(int role, Vector3 position, bool graph = false)
    {
        var gate = new GameObject("Controlled enemy"); gate.SetActive(false);
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[role]), gate.transform);
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Steering.Flat(player.transform.position - position)));
        root.GetComponent<EnemyGraphController>().enabled = graph;
        root.GetComponent<Unity.Behavior.BehaviorGraphAgent>().enabled = graph;
        root.GetComponent<EnemyPartDrop>().enabled = false;
        gate.SetActive(true);
        var agent = root.GetComponent<EnemyAgent>(); agent.PreferredTarget = player.transform; agent.RefreshTarget();
        return root;
    }
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.captureFramerate = 60; QualitySettings.vSyncCount = 0;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = new Vector3(0, -.5f, 0); floor.transform.localScale = new Vector3(200, 1, 200);
        var sun = new GameObject("Review sunlight").AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.5f; sun.transform.rotation = Quaternion.Euler(50, -35, 0);
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath), new Vector3(0, 1.6f, 0), Quaternion.identity);
        player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        player.GetComponent<MechTargetLockController>().EnterFreeAim(); player.GetComponent<MechTargetLockController>().enabled = false;
        var feedback = new SerializedObject(player.GetComponent<CombatFeedback>()); feedback.FindProperty("hitStop").boolValue = false; feedback.ApplyModifiedPropertiesWithoutUndo();
        player.GetComponent<CombatHealth>().SetMaximum(100000, true);
        yield return Frames(5);
        yield return PlayerSwings();
        yield return SmallRoster();
        yield return PullPath();
    }
    static IEnumerator PlayerSwings()
    {
        var equip = player.GetComponent<EquipManager>(); var combat = player.GetComponent<MechCombat>();
        foreach (string family in new[] { "sword", "mace" })
        foreach (float interval in new[] { .2f, 1f, 2f })
        {
            var part = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_" + family + ".prefab")).GetComponent<Part>();
            var stats = Object.Instantiate(part.data); part.data = stats;
            stats.stats.RemoveAll(s => s.type == StatType.FireRate); stats.stats.Add(new StatEntry { type = StatType.FireRate, value = 1 / interval });
            var previous = equip.GetEquipped(PartCategory.ArmL);
            Check(equip.TryEquip(part, PartCategory.ArmL), family + " equips with interval " + interval);
            if (previous != null) Object.Destroy(previous.gameObject);
            var driver = part.GetComponent<MechWeaponAnimator>();
            Check(combat.TryAttack(PartCategory.ArmL).Started, family + " starts a real swing at " + interval);
            driver.Tick(interval * .6f);
            Check(driver.CurrentPose == MechWeaponAnimator.PoseState.Fire && Mathf.Abs(driver.NormalizedTime - .6f) < .001f,
                family + " animation stretches to its actual interval, with no idle cooldown at " + interval);
            TestMech.Advance(player, 0); // Simulate an elapsed wall-clock wait, but an unfinished visual swing.
            Check(!combat.TryAttack(PartCategory.ArmL).Started, family + " never cuts off an unfinished swing");
            driver.Tick(interval * .4f + .001f);
            Check(driver.CurrentPose == MechWeaponAnimator.PoseState.Neutral && combat.TryAttack(PartCategory.ArmL).Started,
                family + " can restart immediately when the complete swing finishes");
            float progress = driver.NormalizedTime;
            Check(CombatHitStop.Request(player.transform, .08f, driver), family + " accepts actor hitstop");
            driver.Tick(.1f); TestMech.Advance(player, 0);
            Check(driver.NormalizedTime == progress && !combat.TryAttack(PartCategory.ArmL).Started, family + " hitstop cannot advance or skip the swing");
            player.GetComponent<CombatHitStop>().enabled = false; driver.Tick(interval + .01f);
            Object.Destroy(stats);
            yield return Frames();
        }
        var ranged = equip.GetEquipped(PartCategory.ShoulderR).GetComponent<MechWeaponAnimator>();
        Check(ranged.PlayAttack(3), "Ranged crossbow still starts its existing animation"); ranged.Tick(1.5f);
        Check(ranged.CurrentPose == MechWeaponAnimator.PoseState.Reload, "Ranged fire/reload remains unchanged");
    }
    static IEnumerator SmallRoster()
    {
        for (int role = 0; role < 6; role++)
        {
            player.transform.position = new Vector3(0, 1.6f, 0);
            var root = Enemy(role, new Vector3(0, 1, role == 5 ? 6 : 2));
            var agent = root.GetComponent<EnemyAgent>(); var attack = root.GetComponent<EnemyAttack>();
            yield return Frames(4);
            var presentation = root.GetComponent<EnemyFodderAnimation>();
            Check(presentation != null && presentation.Animator.name == "ENEMY_Fodder" && !presentation.Animator.applyRootMotion, "New body and locomotion animator: " + root.name);
            Check(root.GetComponentsInChildren<MechPartPoseReceiver>(true).Length == 0 && root.GetComponentsInChildren<EnemyFodderAnimation>(true).Length == 1,
                "No legacy body or duplicate presenter: " + root.name);
            Check(root.GetComponent<EnemyGraphController>().Role == (EnemyBehaviorRole)role && EnemySpawnCost.Of(root) == (role < 4 ? 1 : 2), "Role and cost retained: " + root.name);
            var box = root.transform.Find("Fodder Hurtbox").GetComponent<BoxCollider>(); Physics.SyncTransforms();
            Check(box.bounds.min.y >= -.05f && box.bounds.max.y > .7f && root.GetComponent<MechTargetable>().AimPosition.y > .3f, "Grounded body hurtbox/target: " + root.name);
            Check(attack.Weapons.All(w => w != null && w.transform.IsChildOf(root.transform) && w.CanAnimate), "Owned weapon rigs remain usable: " + root.name);
            if (role < 4)
            {
                var before = player.GetComponent<CombatHealth>().CurrentHealth;
                Check(attack.TryAttack(), root.name + " admits its existing melee attack");
                float duration = attack.CooldownOf(attack.Weapon);
                Check(Mathf.Abs(attack.AttackDuration - duration) < .001f, root.name + " swing uses its full interval");
                attack.Weapon.Tick(duration * .5f); attack.Tick(duration * .5f);
                Check(attack.Releases == 0 && Mathf.Abs(attack.Weapon.NormalizedTime - .5f) < .001f, root.name + " telegraph and animation share the same clock");
                attack.Weapon.Tick(duration * .25f); attack.Tick(duration * .25f);
                Check(attack.Releases == 1 && player.GetComponent<CombatHealth>().CurrentHealth < before, root.name + " actual melee release damages once");
                attack.Weapon.Tick(duration * .25f + .001f); attack.Tick(duration * .25f + .001f);
                Check(!attack.IsAttacking && attack.CooldownRemaining <= 0 && attack.TryAttack(), root.name + " has no separate wait after the swing");
                yield return Frames(2);
                Check(presentation.CurrentPose == EnemyFodderAnimation.Pose.Locomotion, root.name + " does not play cannon body poses");
            }
            if (role == 4)
            {
                var special = root.GetComponent<EnemySpinnerAttack>();
                Check(special.BeginWindup(), "New spinner body retains its special attack"); special.TickWindup(.5f);
                Check(Quaternion.Angle(root.transform.Find("VisualMech").localRotation, Quaternion.identity) > 1, "New spinner visual still spins independently"); special.Cancel();
            }
            yield return Capture(root.name, root);
            root.GetComponent<EnemyGraphController>().enabled = true; root.GetComponent<Unity.Behavior.BehaviorGraphAgent>().enabled = true;
            yield return Frames(8);
            Check(root.GetComponent<EnemyGraphController>().IsReady && root.GetComponent<Unity.Behavior.BehaviorGraphAgent>().Graph.IsRunning, "Live behavior graph still runs: " + root.name);
            root.SetActive(false); root.SetActive(true); yield return Frames(2);
            Check(presentation.CurrentPose == EnemyFodderAnimation.Pose.Locomotion, "Pooling resets new body: " + root.name);
            Object.Destroy(root.transform.parent.gameObject); yield return Frames();
            foreach (var shot in Object.FindObjectsByType<MechProjectile>()) Object.Destroy(shot.gameObject);
        }
    }
    static IEnumerator PullPath()
    {
        player.transform.position = new Vector3(0, 1.6f, 0); Physics.SyncTransforms();
        var root = Enemy(8, new Vector3(0, 1, 14), true); var rope = root.GetComponent<EnemyRopeAttack>();
        yield return Until(() => rope.Impacts == 1, 6, "Pull rock never landed");
        // Move away from the landing area and into the committed return lane.
        player.transform.position = new Vector3(0, 1.6f, 7); Physics.SyncTransforms();
        var hp = player.GetComponent<CombatHealth>(); float before = hp.CurrentHealth;
        yield return Until(() => rope.PullContacts > 0, 4, "Actual pulling body never damaged the player in its path");
        Check(hp.CurrentHealth == before - rope.Damage && rope.PullContacts == 1, "Actual graph-driven pull damages the path once");
        yield return Until(() => rope.CurrentStage == EnemyGraphAttack.Stage.Recovery, 4, "Pull did not recover");
        Check(rope.PullContacts == 1, "Pull cannot damage the same player every frame");
        Check(Mathf.Abs(Steering.Flat(root.transform.position - rope.LandedPosition).magnitude - 2) < .02f,
            "Directed pull stops at its existing clearance without braking past the rock");
        player.SetActive(false); yield return Frames(4);
        Check(rope.CurrentStage == EnemyGraphAttack.Stage.Idle && !root.transform.Find("Rock rope").GetComponent<LineRenderer>().enabled,
            "Losing the live target clears the attack and hides the abandoned tether");
        player.SetActive(true);
        Object.Destroy(root.transform.parent.gameObject); yield return Frames();

        // Deliberately cross an entire collider in one movement step: test the sweep,
        // rather than only endpoint overlap. Also test wall, friendly and height rules.
        root = Enemy(8, new Vector3(0, 1, 10)); rope = root.GetComponent<EnemyRopeAttack>(); root.GetComponent<EnemyAgent>().enabled = false;
        player.transform.position = new Vector3(0, 1.6f, 5); Physics.SyncTransforms(); before = hp.CurrentHealth;
        rope.BeginPull(); root.transform.position = new Vector3(0, 1, 0); Call(rope, "LateUpdate");
        Check(hp.CurrentHealth == before - rope.Damage && rope.PullContacts == 1, "Fast pull sweep cannot tunnel past the player");
        Call(rope, "LateUpdate"); Check(hp.CurrentHealth == before - rope.Damage, "Stationary contact does not repeat damage");
        rope.Cancel(); root.transform.position = new Vector3(0, 1, 10); rope.BeginPull();
        root.transform.position = new Vector3(0, 1, 0); Call(rope, "LateUpdate");
        Check(rope.PullContacts == 2, "A new pull can hit again after clearing per-pull history");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0, 2, 7); wall.transform.localScale = new Vector3(8, 4, .5f); Physics.SyncTransforms();
        rope.Cancel(); root.transform.position = new Vector3(0, 1, 10); rope.BeginPull(); before = hp.CurrentHealth;
        root.transform.position = new Vector3(0, 1, 0); Call(rope, "LateUpdate");
        Check(hp.CurrentHealth == before, "Shared damage rules block pull damage through scenery");
        Object.Destroy(wall); yield return Frames();
        hp.Team = CombatTeam.Enemy; rope.Cancel(); root.transform.position = new Vector3(0, 1, 10); rope.BeginPull();
        root.transform.position = new Vector3(0, 1, 0); Call(rope, "LateUpdate");
        Check(hp.CurrentHealth == before, "Pull has no friendly fire"); hp.Team = CombatTeam.Player;
        player.transform.position = new Vector3(0, 10, 5); Physics.SyncTransforms();
        rope.Cancel(); root.transform.position = new Vector3(0, 1, 10); rope.BeginPull(); root.transform.position = new Vector3(0, 1, 0); Call(rope, "LateUpdate");
        Check(hp.CurrentHealth == before, "Pull does not hit a player far above its body");
        player.transform.position = new Vector3(0, 1.6f, 5); Physics.SyncTransforms();
        rope.Cancel(); root.transform.position = new Vector3(0, 1, 10); rope.BeginPull(); Time.timeScale = 0;
        yield return Frames(); root.transform.position = new Vector3(0, 1, 0); Call(rope, "LateUpdate");
        Check(hp.CurrentHealth == before, "Paused pull cannot apply damage"); Time.timeScale = 1; rope.Cancel();
        Call(rope, "LateUpdate"); Check(hp.CurrentHealth == before, "Cancelled pull cannot leak a contact hit");
        Object.Destroy(root.transform.parent.gameObject); yield return Frames();
    }
    static IEnumerator Capture(string name, GameObject root)
    {
        foreach (var canvas in Object.FindObjectsByType<Canvas>()) canvas.enabled = false;
        var transforms = root.GetComponentsInChildren<Transform>(true);
        var layers = transforms.Select(t => t.gameObject.layer).ToArray();
        foreach (var node in transforms) node.gameObject.layer = 31;
        var camera = new GameObject("Body review camera").AddComponent<Camera>();
        camera.cullingMask = 1 << 31;
        var focus = root.transform.TransformPoint(FodderModelSetup.BodyBounds(root).center) + Vector3.up * .3f;
        camera.transform.SetPositionAndRotation(focus + new Vector3(3.5f, 2.5f, -4), Quaternion.LookRotation(new Vector3(-3.5f, -2.5f, 4)));
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.1f, .12f, .15f); camera.fieldOfView = 42;
        yield return Frames();
        var texture = new RenderTexture(1000, 700, 24); texture.Create();
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.StandardRequest { destination = texture });
        var previous = RenderTexture.active; RenderTexture.active = texture;
        var pixels = new Texture2D(1000, 700, TextureFormat.RGB24, false); pixels.ReadPixels(new Rect(0, 0, 1000, 700), 0, 0); pixels.Apply();
        string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "MeleeRosterCaptures"); Directory.CreateDirectory(folder); File.WriteAllBytes(Path.Combine(folder, name + ".png"), pixels.EncodeToPNG());
        RenderTexture.active = previous; texture.Release(); Object.Destroy(texture); Object.Destroy(pixels); Object.Destroy(camera.gameObject);
        for (int i = 0; i < transforms.Length; i++) if (transforms[i] != null) transforms[i].gameObject.layer = layers[i];
    }
}
