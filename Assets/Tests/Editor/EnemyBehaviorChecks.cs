using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Behavior;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Disposable-project integration checks: live graphs, actual enemy/player prefabs,
// shared flow field, existing attack/animation/hit timing and real damage.
public static partial class EnemyBehaviorChecks
{
    const string Pending = "EnemyBehaviorChecks.Pending";
    static readonly Stack<IEnumerator> stack = new Stack<IEnumerator>();
    static bool started;
    static int checks;
    static double deadline;
    static GameObject player;
    static CombatHealth health;
    static EnemyGraphController subject;
    static NavGrid grid;
    static FlowField field;
    static string captures;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Step;
    public static void RunBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        EnemyRosterSetup.UpgradeAssets(); EnemyRosterSetup.UpgradeAssets();
        SessionState.SetBool(Pending + ".Arena", false);
        GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline = null;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); checks++; Debug.Log("ENEMY BEHAVIOR PASS: " + message); }
    static IEnumerator Frames(int count = 2) { int until = Time.frameCount + count; while (Time.frameCount < until) yield return null; }
    static IEnumerator Until(Func<bool> condition, float seconds, string context)
    {
        float until = Time.time + seconds;
        while (!condition())
        {
            if (Time.time > until) throw new Exception(context + "; action=" + (subject == null ? "none" : subject.CurrentAction)
                + "; position=" + (subject == null ? Vector3.zero : subject.transform.position)
                + "; graph running=" + (subject != null && subject.Graph.Graph != null && subject.Graph.Graph.IsRunning));
            yield return null;
        }
    }
    static void Step()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!started) { started = true; deadline = EditorApplication.timeSinceStartup + 240; stack.Push(SessionState.GetBool(Pending + ".Arena", false) ? RunArena() : Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Enemy graph checks timed out");
            while (stack.Count > 0)
            {
                var routine = stack.Peek(); if (!routine.MoveNext()) { (routine as IDisposable)?.Dispose(); stack.Pop(); continue; }
                if (routine.Current is IEnumerator nested) { stack.Push(nested); continue; } return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        PreMissionReliabilityChecks.EndLogAudit(); Time.captureFramerate = 0; Time.timeScale = 1;
        SessionState.SetBool(Pending, false); if (error != null) Debug.LogException(error);
        Debug.Log("ENEMY BEHAVIOR CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static EnemyGraphController Spawn(Vector3 position)
    {
        var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[0]), position,
            Quaternion.LookRotation(Steering.Flat(player.transform.position - position)));
        var drops = actor.GetComponent<EnemyPartDrop>(); if (drops != null) drops.enabled = false;
        return actor.GetComponent<EnemyGraphController>();
    }
    static IEnumerator Remove(EnemyGraphController actor)
    { if (actor != null) Object.Destroy(actor.gameObject); yield return Frames(2); }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.captureFramerate = 60; QualitySettings.vSyncCount = 0;
        captures = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "EnemyBehaviorCaptures"); Directory.CreateDirectory(captures);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = new Vector3(0, -0.25f, 0); floor.transform.localScale = new Vector3(240, 0.5f, 240);
        var light = new GameObject("Test sunlight").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(55, -30, 0);
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PartSwapWorldUiSetup.PlayerPath), new Vector3(0, 1.6f, 0), Quaternion.identity);
        player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        player.GetComponent<MechTargetLockController>().EnterFreeAim();
        var navigation = new GameObject("Existing shared navigation"); grid = navigation.AddComponent<NavGrid>(); field = navigation.AddComponent<FlowField>();
        yield return Frames(4); health = player.GetComponent<CombatHealth>(); health.SetMaximum(10000, true);
        Check(field.HasDirection, "Existing shared flow field builds toward the actual player");
        foreach (string family in new[] { "mace", "crossbow", "throwingAxe" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Small Enemies/Enemy_" + family + ".prefab");
            Check(prefab.GetComponent<EnemyGraphController>() != null && prefab.GetComponent<EnemyAgent>().ExternalDecisions
                && prefab.GetComponent<EnemyAttack>().ExternalRequests, family + " uses the same graph-to-execution interface");
        }
        subject = Spawn(new Vector3(0, 1, 80)); yield return Frames(60);
        Check(subject.IsReady && subject.Graph.Graph.IsRunning && subject.Agent.Fsm.Current == null, "Actual sword runs its graph while its legacy state machine stays inactive");
        Check(!subject.TargetDetected && subject.CurrentAction == "Wander" && subject.Agent.Velocity.magnitude > 0.1f, "Undetected sword wanders using existing steering");
        Check(subject.GetComponent<UnityEngine.AI.NavMeshAgent>() == null && subject.GetComponent<MechPlayerInput>() == null, "Graph installs no competing navigation agent or player controller");
        Capture("wander", subject);
        subject.transform.position = new Vector3(0, 1, 18); Physics.SyncTransforms();
        yield return Until(() => subject.TargetDetected && subject.CurrentAction == "Approach", 4, "Sword did not detect/approach");
        float distance = subject.Agent.DistanceToTarget; yield return Frames(60);
        Check(subject.Agent.DistanceToTarget < distance - 1 && subject.Agent.Velocity.magnitude > 0.1f, "Detected sword closes distance through the existing movement agent");
        yield return Until(() => subject.Attack.IsAttacking, 15, "Sword never reached attack range");
        float firstStart = Time.time, before = health.CurrentHealth; var firstWeapon = subject.Attack.Weapon;
        Check(subject.Agent.DistanceToTarget >= 1.5f && subject.Agent.DistanceToTarget <= subject.Agent.AttackRange + 0.5f, "Natural approach stops at a usable melee distance");
        Check(firstWeapon.CurrentPose == MechWeaponAnimator.PoseState.Fire && health.CurrentHealth == before, "Attack starts with its existing sword animation and no immediate damage");
        Quaternion committed = subject.transform.rotation; yield return Frames(12);
        Check(subject.Attack.IsAttacking && firstWeapon.NormalizedTime > 0.1f && health.CurrentHealth == before, "Visible windup advances before its release frame");
        Capture("windup", subject);
        yield return Until(() => subject.Attack.Releases == 1, 2, "Sword never released its strike");
        Check(Mathf.Abs(health.CurrentHealth - (before - subject.Attack.DamageOf(firstWeapon))) < 0.002f, "Animation release applies the existing weapon damage exactly once; before=" + before
            + "; after=" + health.CurrentHealth + "; expected=" + subject.Attack.DamageOf(firstWeapon)
            + "; enemy=" + subject.transform.position + "; scale=" + subject.transform.lossyScale + "; player=" + player.transform.position
            + "; bounds=" + player.GetComponent<Collider>().bounds + "; facing=" + subject.transform.forward);
        Check(Quaternion.Angle(subject.transform.rotation, committed) < 1, "Committed overhead swing keeps its snapshotted strike direction");
        Capture("release", subject);
        yield return Until(() => !subject.Attack.IsAttacking && subject.CurrentAction == "Recover", 2, "Sword did not recover");
        Check(subject.Attack.CooldownRemaining > 0 && subject.Attack.Releases == 1, "Graph exposes a recovery window and respects the weapon cooldown");
        Capture("recovery", subject);
        yield return Until(() => subject.Attack.IsAttacking && subject.Attack.Weapon != firstWeapon, 3, "Next sword arm never attacked");
        Check(Time.time - firstStart >= subject.Attack.CooldownOf(firstWeapon) - 0.034f, "Next attack cannot begin before the existing cooldown expires");
        Check(subject.Attack.Releases == 1, "Second telegraph does not duplicate the previous release");
        before = health.CurrentHealth; subject.GetComponent<CombatHealth>().ApplyDamage(10000); yield return Frames(60);
        Check(!subject.gameObject.activeSelf && !subject.Attack.IsAttacking && !EnemyAgent.All.Contains(subject.Agent)
            && health.CurrentHealth == before, "Enemy death stops the graph, crowd registration and pending damage");
        yield return Remove(subject);

        subject = Spawn(new Vector3(10, 1, 0)); yield return Until(() => subject.TargetDetected, 3, "Target not acquired");
        player.transform.position = new Vector3(0, 1.6f, -100); Physics.SyncTransforms();
        yield return Until(() => !subject.TargetDetected && subject.CurrentAction == "Wander", 4, "Escaped player not forgotten");
        Check(!subject.Attack.IsAttacking, "Player beyond lose radius returns the graph to wandering");
        player.transform.position = new Vector3(0, 1.6f, 0); Physics.SyncTransforms();
        yield return Until(() => subject.TargetDetected, 4, "Player was not reacquired");
        Check(subject.TargetDetected, "Wandering graph reacquires a returning player");
        health.ApplyDamage(10000); yield return Until(() => !subject.TargetDetected && !subject.Attack.IsAttacking, 3, "Dead player remained a target");
        Check(!player.activeSelf, "Dead/inactive player cannot receive another graph attack");
        health.SetMaximum(10000, true); player.SetActive(true); yield return Frames(3); yield return Remove(subject);

        subject = Spawn(new Vector3(0, 1, 2.2f)); yield return Until(() => subject.Attack.IsAttacking, 3, "Close sword did not attack");
        before = health.CurrentHealth; subject.Graph.enabled = false; yield return Frames(60);
        Check(!subject.Attack.IsAttacking && subject.Attack.Releases == 0 && health.CurrentHealth == before
            && subject.Agent.Fsm.Current == null, "Disabling the graph cancels pending damage and never enables the legacy AI");
        subject.Graph.enabled = true; yield return Until(() => subject.Attack.Releases == 1, 4, "Graph did not resume after enable");
        Check(subject.Graph.Graph.IsRunning && health.CurrentHealth < before, "Re-enabling starts a fresh functional graph cycle");
        yield return Remove(subject);

        var group = new[] { Spawn(new Vector3(-3, 1, 9)), Spawn(new Vector3(3, 1, 9)), Spawn(new Vector3(0, 1, 12)) };
        subject = group[0]; yield return Frames(480);
        Check(!ReferenceEquals(group[0].Graph.Graph, group[1].Graph.Graph), "Enemies get separate runtime graph instances from the shared asset");
        float nearest = float.PositiveInfinity;
        for (int a = 0; a < group.Length; a++) for (int b = a + 1; b < group.Length; b++)
            nearest = Mathf.Min(nearest, Steering.Flat(group[a].transform.position - group[b].transform.position).magnitude);
        Check(nearest > 0.9f && group.All(e => e.Agent.Fsm.Current == null), "Existing separation keeps several graph-controlled swords from stacking (minimum " + nearest.ToString("F2") + " m)");
        Check(group.Any(e => e.Attack.Releases > 0), "Multiple approaching swords can enter their attack/recovery loops");
        Capture("crowd", group[0]); foreach (var actor in group) yield return Remove(actor);

        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.tag = "Obstacle"; wall.transform.position = new Vector3(0, 1, 7); wall.transform.localScale = new Vector3(6, 2, 1);
        Physics.SyncTransforms(); grid.Bake(); field.Rebuild(); subject = Spawn(new Vector3(0, 1, 16));
        float largestOffset = 0, until = Time.time + 15;
        while (subject.transform.position.z > 4 && Time.time < until)
        { largestOffset = Mathf.Max(largestOffset, Mathf.Abs(subject.transform.position.x)); yield return Frames(1); }
        Check(subject.transform.position.z <= 4 && largestOffset >= 3.2f, "Graph reuses shared flow-field routing around a real obstacle");
        yield return Remove(subject); Object.Destroy(wall); yield return Frames(2); grid.Bake(); field.Rebuild();
        yield return RosterCases();
    }
    static void Capture(string name, EnemyGraphController actor)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        var meshes = actor.GetComponentsInChildren<Renderer>().Where(r => r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)).ToArray();
        var bounds = meshes[0].bounds; foreach (var mesh in meshes) bounds.Encapsulate(mesh.bounds);
        var view = new GameObject("Pose verification camera").AddComponent<Camera>(); view.nearClipPlane = 0.05f;
        view.transform.position = bounds.center + new Vector3(1, 0.7f, -1).normalized * Mathf.Max(6, bounds.size.magnitude * 1.8f);
        view.transform.LookAt(bounds.center); view.backgroundColor = new Color(0.12f, 0.15f, 0.18f); view.clearFlags = CameraClearFlags.SolidColor;
        var target = new RenderTexture(1280, 720, 24); var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var previous = RenderTexture.active; var canvases = Object.FindObjectsByType<Canvas>().Where(c => c.isRootCanvas && c.enabled).ToArray();
        try
        {
            foreach (var canvas in canvases) canvas.enabled = false;
            view.targetTexture = target; RenderPipeline.SubmitRenderRequest(view, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target; image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(captures, name + ".png"), image.EncodeToPNG());
        }
        finally
        { foreach (var canvas in canvases) canvas.enabled = true; RenderTexture.active = previous; Object.Destroy(view.gameObject); Object.Destroy(target); Object.Destroy(image); }
    }
}
