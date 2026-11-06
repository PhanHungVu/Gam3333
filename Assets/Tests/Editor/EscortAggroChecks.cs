using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Actual enemy graphs and cargo health lifecycle, confined to a disposable Unity project.
public static class EscortAggroChecks
{
    const string Pending = "EscortAggroChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static bool running; static int frame = -1, checks; static double deadline;
    static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private).Invoke(owner, args);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunReproBatch() => Begin(true, false);
    public static void RunBatch() => Begin(false, false);
    public static void RunSceneBatch() => Begin(false, true);
    static void Begin(bool repro, bool scene)
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        SessionState.SetBool(Pending + ".Repro", repro); SessionState.SetBool(Pending + ".Scene", scene);
        if (scene) EditorSceneManager.OpenScene("Assets/Scenes/EscortTest.unity");
        else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool valid, string message)
    { if (!valid) throw new Exception(message); checks++; Debug.Log("ESCORT AGGRO PASS: " + message); }
    static IEnumerator Frames(int count = 3) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running)
            {
                running = true; deadline = EditorApplication.timeSinceStartup + 240;
                PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(SessionState.GetBool(Pending + ".Scene", false) ? RunScene() : Run());
            }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Escort aggro checks timed out.");
            while (steps.Count > 0)
            {
                var step = steps.Peek(); if (!step.MoveNext()) { (step as IDisposable)?.Dispose(); steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; } return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit(); Time.captureFramerate = 0;
        if (error != null) Debug.LogException(error);
        Debug.Log("ESCORT AGGRO CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static EnemyGraphController Spawn(string path, Vector3 position)
    {
        var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), position, Quaternion.identity);
        var drops = actor.GetComponent<EnemyPartDrop>(); if (drops != null) drops.enabled = false;
        return actor.GetComponent<EnemyGraphController>();
    }
    static EscortPayload Cargo(float x)
    {
        var root = new GameObject("Test escort cargo"); root.SetActive(false);
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name = "Body";
        body.transform.SetParent(root.transform, false); body.AddComponent<CombatHealth>();
        var payload = root.AddComponent<EscortPayload>(); Set(payload, "speed", 0f); Set(payload, "respawnDelay", 100f);
        root.SetActive(true);
        var route = new EscortRoute(); route.X.AddRange(new[] { x, x + 40, x + 80 }); route.Y.AddRange(new[] { 0f, 0f, 0f });
        route.Z.AddRange(new[] { 0f, 0f, 0f }); route.Distance.AddRange(new[] { 0f, 40f, 80f }); route.CheckpointDistances.AddRange(new[] { 0f, 40f, 80f });
        payload.Configure(route, new GameObject("Route space").transform, false); return payload;
    }
    static IEnumerator Run()
    {
        Time.captureFramerate = 60; Application.runInBackground = true;
        new GameObject("Test audio listener").AddComponent<AudioListener>();
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = Vector3.down * .25f; floor.transform.localScale = new Vector3(2000, .5f, 2000);
        var player = GameObject.CreatePrimitive(PrimitiveType.Capsule); player.name = "Aggro player"; player.tag = "Player"; player.transform.position = Vector3.up;
        var health = player.AddComponent<CombatHealth>(); health.Team = CombatTeam.Player; health.SetMaximum(100000, true);
        var playerField = new GameObject("Shared player flow field").AddComponent<FlowField>();
        yield return Frames();
        var paths = EnemyRosterSetup.Roster;
        int count = SessionState.GetBool(Pending + ".Repro", false) ? 1 : paths.Length;
        for (int i = 0; i < count; i++)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            float x = prefab.GetComponent<EnemyAgent>().DetectRadius + 10;
            var payload = Cargo(x + 12); var brain = Spawn(paths[i], new Vector3(x, 1, 0));
            Call(payload, "OnSpawned", payload, brain.gameObject); yield return Frames(5);
            Check(brain.Agent.Target == payload.Body.transform && brain.SensePlayer() && brain.Graph.Graph.IsRunning, brain.Role + " runs its real graph against live cargo");
            Check(FlowField.For(brain.Agent.Target) == payload.GetComponentInChildren<FlowField>(), "Live cargo uses its own flow field");
            bool switchesDuringDeath = false;
            payload.Body.Died += () => { brain.Agent.RefreshTarget(); switchesDuringDeath = brain.Agent.Target == player.transform && brain.SensePlayer(); };
            payload.Body.ApplyDamage(100000);
            brain.Agent.RefreshTarget(); bool detected = brain.SensePlayer();
            Debug.Log($"ESCORT REPRO: role={brain.Role}, targetPlayer={brain.Agent.Target == player.transform}, detected={detected}, distance={brain.Agent.DistanceToTarget}, detect={brain.Agent.DetectRadius}, deathCallbackSwitched={switchesDuringDeath}");
            if (SessionState.GetBool(Pending + ".Repro", false))
            {
                yield return Frames(30); Debug.Log($"ESCORT REPRO AFTER DEATH: action={brain.CurrentAction}, detected={brain.TargetDetected}, velocity={brain.Agent.Velocity}"); yield break;
            }
            Check(switchesDuringDeath, "Dead cargo is rejected even during Died, before its body deactivates");
            Check(brain.Agent.Target == player.transform && detected && brain.Agent.DistanceToTarget > brain.Agent.DetectRadius,
                brain.Role + " immediately pursues the player outside normal acquisition range when cargo dies");
            Check(FlowField.For(brain.Agent.Target) == playerField, "Fallback navigation switches to the player's field");
            float before = Vector3.Distance(brain.transform.position, player.transform.position); yield return Frames(90);
            Debug.Log($"ESCORT MOTION: {brain.Role}: action={brain.CurrentAction}, detected={brain.TargetDetected}, ready={brain.IsReady}, graph={brain.Graph.Graph.IsRunning}, before={before}, after={Vector3.Distance(brain.transform.position, player.transform.position)}, position={brain.transform.position}, velocity={brain.Agent.Velocity}, goal={brain.Agent.GoalDirection}, force={brain.Agent.BehaviourForce}");
            // The authored throwing-axe zigzag has a 500-unit lateral amplitude;
            // preserve it while verifying forward intent and actual closing distance.
            float minimumProgress = brain.Agent.Pathing == PathingMode.Zigzag ? .1f : 2f;
            Check(brain.TargetDetected && brain.CurrentAction != "Wander" && Vector3.Distance(brain.transform.position, player.transform.position) < before - minimumProgress
                && Vector3.Dot(brain.Agent.GoalDirection, Steering.Flat(player.transform.position - brain.transform.position).normalized) > 0,
                brain.Role + " keeps approaching the player instead of wandering away");
            if (i == 0)
            {
                player.transform.position = new Vector3(-100, 1, 0); Physics.SyncTransforms();
                Check(brain.SensePlayer() && brain.Agent.DistanceToTarget > brain.Agent.LoseRadius,
                    "Cargo-loss pursuit also survives the normal lose radius");
                player.SetActive(false); brain.Agent.RefreshTarget();
                Check(brain.Agent.Target == null && !brain.SensePlayer(), "Fallback never pursues a disabled player");
                player.SetActive(true); player.transform.position = Vector3.up; Physics.SyncTransforms(); yield return Frames(35);
                Check(brain.SensePlayer() && brain.Agent.Target == player.transform, "An available player is reacquired during cargo downtime");
            }
            var reinforcement = Spawn(paths[i], new Vector3(x, 1, 8)); Call(payload, "OnSpawned", payload, reinforcement.gameObject);
            yield return Frames(4);
            Check(reinforcement.SensePlayer() && reinforcement.Agent.Target == player.transform, "Enemies spawned during cargo downtime also pursue the player");
            Call(payload, "Respawn"); brain.Agent.RefreshTarget(); reinforcement.Agent.RefreshTarget();
            Check(!payload.Body.IsDead && payload.Body.gameObject.activeInHierarchy && brain.Agent.Target == payload.Body.transform
                && reinforcement.Agent.Target == payload.Body.transform && brain.SensePlayer(), "Respawn restores cargo priority for surviving and newly spawned enemies");
            payload.Body.gameObject.SetActive(false); brain.Agent.RefreshTarget();
            Check(brain.Agent.Target == player.transform && brain.SensePlayer(), "Disabling the cargo body also permits aggressive player fallback");
            Object.DestroyImmediate(payload.Body.gameObject); brain.Agent.RefreshTarget();
            Check(brain.Agent.Target == player.transform && brain.SensePlayer(), "Destroying the cargo object retains aggressive player fallback");
            brain.Agent.PreferredTarget = null; player.transform.position = brain.transform.position + Vector3.left * (brain.Agent.LoseRadius + 20); Physics.SyncTransforms();
            Check(!brain.SensePlayer(), "Explicitly clearing the objective assignment restores ordinary lose-radius rules");
            player.transform.position = Vector3.up; Physics.SyncTransforms();
            Object.DestroyImmediate(brain.gameObject); Object.DestroyImmediate(reinforcement.gameObject); Object.DestroyImmediate(payload.gameObject); yield return Frames();
        }
        var ordinary = Spawn(paths[0], new Vector3(50, 1, 0)); yield return Frames();
        Check(!ordinary.SensePlayer(), "Ordinary enemies without a cargo assignment retain their normal detection radius");
        player.transform.position = ordinary.transform.position + Vector3.right * 20; Physics.SyncTransforms();
        Check(ordinary.SensePlayer(), "Ordinary enemies still acquire a nearby player");
        player.transform.position = ordinary.transform.position + Vector3.right * 100; Physics.SyncTransforms();
        Check(!ordinary.SensePlayer(), "Ordinary enemies still lose an escaped player");
        Object.DestroyImmediate(ordinary.gameObject); player.transform.position = Vector3.up;
        var legacy = new GameObject("Legacy cargo attacker").AddComponent<EnemyAgent>(); legacy.transform.position = new Vector3(100, 1, 0);
        var legacyCargo = new GameObject("Legacy cargo body"); legacyCargo.transform.position = new Vector3(110, 1, 0);
        legacy.PreferredTarget = legacyCargo.transform; legacy.RefreshTarget(); legacy.Fsm.Tick(.02f);
        legacyCargo.SetActive(false); legacy.RefreshTarget(); legacy.Fsm.Tick(.02f);
        Check(legacy.Target == player.transform && legacy.Fsm.Current == legacy.Chase && legacy.GoalDirection.x < 0,
            "Legacy state-machine enemies also chase the fallback player beyond their lose radius");
        Object.DestroyImmediate(legacy.gameObject); Object.DestroyImmediate(legacyCargo);
        EscortPayloadChecks.Run(Check);
    }
    static IEnumerator RunScene()
    {
        Time.captureFramerate = 60; Application.runInBackground = true; yield return Frames(90);
        var payload = Object.FindAnyObjectByType<EscortPayload>(); var player = GameObject.FindWithTag("Player");
        Check(payload != null && payload.Rules != null && player != null, "EscortTest generates its real configured cargo and player");
        player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        var capsule = player.GetComponent<CharacterController>(); capsule.enabled = false;
        Check(SpawnRing.TryPick(payload.transform.position, 50, 64, Object.FindAnyObjectByType<NavGrid>(), out var playerPosition, out _),
            "The real-map fallback player is placed on a walkable cell outside small-enemy detection range");
        playerPosition.y = NavTerrain.GroundHeight(playerPosition, payload.transform.position.y) + 2;
        player.transform.position = playerPosition; capsule.enabled = true;
        player.GetComponent<CombatHealth>().SetMaximum(100000, true); payload.Body.SetMaximum(100000, true);
        Call(payload, "StartWave"); var director = SpawnDirector.Instance;
        for (int i = 0; i < 12 && EnemyAgent.All.Count(a => a.PreferredTarget == payload.Body.transform) < 4; i++) { director.Tick(1); yield return Frames(2); }
        var agents = EnemyAgent.All.Where(a => a.PreferredTarget == payload.Body.transform).ToArray();
        Check(agents.Length > 0 && agents.All(a => a.Target == payload.Body.transform), "The real Escort wave assigns its spawned enemies to cargo");
        payload.Body.ApplyDamage(100000); yield return Frames(3);
        Check(agents.All(a => a.Target == player.transform && a.GetComponent<EnemyGraphController>().SensePlayer()), "All living EscortTest wave enemies switch to aggressive player pursuit after cargo destruction");
        Call(payload, "Respawn"); yield return Frames(3);
        Check(agents.All(a => a.Target == payload.Body.transform), "Real-map enemies switch back to the restored cargo");
    }
}
