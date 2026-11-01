using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

// Actual saved ZooScene, floor-switch references, director and Behavior Graph prefabs.
public static class ZooEnemyChecks
{
    const string Pending = "ZooEnemyChecks.Pending";
    static readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
    static bool started;
    static int checks;
    static double deadline;
    static SpawnDirector director;
    static GameObject small, elite;
    static Transform[] objects;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Step;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new Exception("Run only in a disposable project");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/ZooScene.unity");
        foreach (var root in scene.GetRootGameObjects()) foreach (var child in root.GetComponentsInChildren<Transform>(true))
        {
            if (PrefabUtility.IsPrefabAssetMissing(child.gameObject)) throw new Exception("Missing prefab: " + child.name);
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) > 0) throw new Exception("Missing script: " + child.name);
        }
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; Debug.Log("ZOO ENEMY PASS: " + message); }
    static IEnumerator Frames(int count = 3) { int until = Time.frameCount + count; while (Time.frameCount < until) yield return null; }
    static void Clear()
    {
        foreach (var actor in EnemyAgent.All.ToArray())
        { var loot = actor.GetComponent<EnemyPartDrop>(); if (loot != null) loot.enabled = false; Object.DestroyImmediate(actor.gameObject); }
        foreach (var shot in Object.FindObjectsByType<MechProjectile>()) Object.DestroyImmediate(shot.gameObject);
    }
    static void Step()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!started) { started = true; deadline = EditorApplication.timeSinceStartup + 180; routines.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Zoo check timeout");
            while (routines.Count > 0)
            {
                var current = routines.Peek(); if (!current.MoveNext()) { routines.Pop(); continue; }
                if (current.Current is IEnumerator nested) { routines.Push(nested); continue; } return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit(); Time.captureFramerate = 0;
        if (error != null) Debug.LogException(error);
        Debug.Log("ZOO ENEMY CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Time.captureFramerate = 60; Application.runInBackground = true;
        if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
        yield return Frames(8);
        objects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
        director = Object.FindAnyObjectByType<SpawnDirector>();
        small = objects.Single(t => t.name == "Small Enemies").gameObject; elite = objects.Single(t => t.name == "Elite Enemies").gameObject;
        Check(Object.FindObjectsByType<SpawnDirector>().Length == 1 && Object.FindAnyObjectByType<ZooEnemyWaves>() != null, "Saved zoo uses one connected director and its showcase controller");
        Check(objects.Select(t => t.GetComponent<ZooToggle>()).Count(t => t != null && (t.toggleObject == small || t.toggleObject == elite)) == 2,
            "Both original floor switches retain valid references to the replacement enemy groups");
        Check(!small.activeSelf && !elite.activeSelf && director.CurrentWave == null && EnemyAgent.All.Count == 0,
            "Enemy exhibit starts off until a floor switch is activated");
        Check(FlowField.Instance != null && Object.FindAnyObjectByType<NavGrid>().Baked, "Zoo retains its baked shared navigation");
        var player = GameObject.FindWithTag("Player"); player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        player.GetComponent<CombatHealth>().SetMaximum(10000, true);
        var point = objects.Single(t => t.name == "EnemySpawnerModel_Mace").GetComponent<SpawnPoint>();
        player.transform.position = point.transform.position + Vector3.back * 12; var position = player.transform.position; position.y = 1.6f; player.transform.position = position;
        Physics.SyncTransforms(); FlowField.Instance.Rebuild(); UnityEngine.Random.InitState(20261030);
        small.SetActive(true); yield return Frames();
        Check(director.CurrentWave != null && director.CurrentWave.enemies.Length == 6 && director.CurrentWave.maxConcurrentEnemies == 8,
            "Small switch enables the six new small variants with a bounded population");
        yield return Collect(6, true);
        small.SetActive(false); director.enabled = true; yield return Frames();
        int stopped = director.TotalSpawned; yield return Frames(150);
        Check(director.CurrentWave == null && director.TotalSpawned == stopped, "Switching the exhibit off stops further spawns");
        elite.SetActive(true); yield return Frames();
        Check(director.CurrentWave != null && director.CurrentWave.enemies.Length == 3, "Elite switch enables heavy ranged, orbiting rock and preserved throw/pull enemies");
        yield return Collect(3, false);
        director.enabled = true; small.SetActive(true); yield return Frames();
        Check(director.CurrentWave.enemies.Length == 9 && director.CurrentWave.enemies.All(e => e.prefab.GetComponent<EnemyGraphController>() != null),
            "Both switches permit the complete nine-enemy Behavior Graph roster");
        director.enabled = false;
        for (int i = 0; i < 30; i++) director.Tick(3);
        yield return Frames();
        Check(EnemyAgent.All.Count == 8, "Zoo's population cap prevents endless enemy accumulation");
        int left = director.ResourceLeft; director.Tick(3); Check(director.ResourceLeft == left, "A full exhibit pauses spending until space opens");
        small.SetActive(false); elite.SetActive(false); director.enabled = true; yield return Frames(); Clear();
        Check(director.CurrentWave == null, "Both floor switches can stop the shared wave cleanly");
    }
    static IEnumerator Collect(int expected, bool smallGroup)
    {
        director.enabled = false; var seen = new HashSet<EnemyBehaviorRole>();
        bool graphs = true, category = true;
        for (int i = 0; i < 140 && seen.Count < expected; i++)
        {
            director.Tick(3); yield return Frames();
            foreach (var agent in EnemyAgent.All)
            {
                var actor = agent.GetComponent<EnemyGraphController>();
                graphs &= actor != null && actor.IsReady && actor.Graph.Graph != null && actor.Graph.Graph.IsRunning;
                if (actor != null) { seen.Add(actor.Role); category &= (actor.Role <= EnemyBehaviorRole.Crossbow) == smallGroup; }
            }
            Clear();
        }
        Check(seen.Count == expected && category, "Real director spawns every enabled " + (smallGroup ? "small" : "elite") + " role without crossing the disabled group");
        Check(graphs, "Spawned " + (smallGroup ? "small" : "elite") + " prefabs run their real graphs and existing combat interfaces");
    }
}
