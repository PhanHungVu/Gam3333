using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class GameplaySceneMaintenanceChecks
{
    const string Pending = "GameplaySceneMaintenanceChecks.Pending";
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static bool running; static int checks, frame = -1; static double deadline;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("GAMEPLAY REPAIR PASS: " + message); }
    static IEnumerator Frames(int count = 4) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new Exception("Disposable project only");
        GameplaySceneMaintenance.Apply();
        var cursor = AssetDatabase.LoadAssetAtPath<GameObject>(GameplaySceneMaintenance.CursorPath);
        Check(cursor.GetComponentsInChildren<MechEnergyHud>(true).Length == 0 && cursor.GetComponentsInChildren<MechWeaponsHud>(true).Length == 1
            && cursor.GetComponentsInChildren<MechAbilitiesHud>(true).Length == 1 && cursor.GetComponentsInChildren<MechHealthHud>(true).Length == 1,
            "Shared cursor prefab contains one complete combat HUD and no stamina meter");
        foreach (var playerPath in GameplaySceneMaintenance.PlayerPaths)
        {
            var rig = new SerializedObject(AssetDatabase.LoadAssetAtPath<GameObject>(playerPath).GetComponent<MechPlayerRig>());
            Check(rig.FindProperty("cursorPrefab").objectReferenceValue == cursor && rig.FindProperty("combatHudPrefab").objectReferenceValue != null,
                playerPath + " supplies the shared cursor/HUD in map scenes without authored UI");
        }
        var catalogue = AssetDatabase.LoadAssetAtPath<QuestBoardCatalogue>("Assets/PreMission/PrototypeBoardCatalogue.asset");
        var heavy = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PreMission/PartData_HeavyTorso_Test.asset");
        Check(heavy.partName == "Heavy Torso (Demo Only)" && catalogue.PlayerPrefabFor(heavy)
            == AssetDatabase.LoadAssetAtPath<GameObject>(GameplaySceneMaintenance.PlayerPaths[1]), "Demo heavy selector retains its real all-weapons player mapping");
        foreach (var path in new[] { "A33", "ZooScene", "JhMapScene", "Merge Test", "KOTHTest", "EscortTest", "EnemyBehaviorPlaytest", "JhCombatUIRevampScene" })
        {
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/" + path + ".unity");
            var all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            Check(all.All(t => !PrefabUtility.IsPrefabAssetMissing(t.gameObject) && GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), path + " opens without missing prefab/script instances");
            Check(all.All(t => t.GetComponent<MechEnergyHud>() == null), path + " has no saved stamina HUD");
            foreach (var objective in all.Select(t => t.GetComponent<TempLevelObjective>()).Where(o => o != null))
                Check(objective.toolTipText != null && objective.sceneManager != null, path + " keeps its objective UI and menu connection");
            if (path == "JhMapScene" || path == "Merge Test" || path == "ZooScene")
            {
                var waves = all.Select(t => t.GetComponent<ZooEnemyWaves>()).First(w => w != null);
                var fields = new SerializedObject(waves);
                var roster = fields.FindProperty("roster").objectReferenceValue as WaveData;
                int count = path == "ZooScene" ? 10 : 9; // Zoo adds Fodder without changing mission/training rosters.
                Check(roster != null && roster.enemies.Length == count && roster.enemies.All(e => e.prefab != null && (path == "ZooScene" || e.weight > 0))
                    && fields.FindProperty("smallSpawnPoint").objectReferenceValue != null && fields.FindProperty("eliteSpawnPoint").objectReferenceValue != null,
                    path + " retains both spawn locations and its " + count + "-entry roster");
            }
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        SessionState.SetBool(Pending, true); deadline = EditorApplication.timeSinceStartup + 300; EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Gameplay repair checks timed out");
            while (steps.Count > 0)
            {
                var current = steps.Peek(); if (!current.MoveNext()) { steps.Pop(); continue; }
                if (current.Current is IEnumerator nested) { steps.Push(nested); continue; } return;
            }
            Finish(null);
        }
        catch (Exception exception) { Finish(exception); }
    }
    static void Finish(Exception exception)
    {
        SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit();
        if (exception != null) Debug.LogException(exception);
        Debug.Log("GAMEPLAY REPAIR CHECKS " + (exception == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(exception == null ? 0 : 1);
    }
    static void CheckHud(string name)
    {
        var cursor = Object.FindObjectsByType<LockOnCursor>(); var weapons = Object.FindObjectsByType<MechWeaponsHud>();
        var abilities = Object.FindObjectsByType<MechAbilitiesHud>(); var health = Object.FindObjectsByType<MechHealthHud>();
        Check(cursor.Length == 1 && weapons.Length == 1 && abilities.Length == 1 && health.Length == 1
            && Object.FindObjectsByType<MechEnergyHud>().Length == 0, name + " runs one cursor, weapon, ability and health HUD without stamina");
        var player = Object.FindAnyObjectByType<MechPlayerRig>();
        Check(player != null && weapons[0].Equipment == player.GetComponent<EquipManager>() && abilities[0].Equipment == weapons[0].Equipment
            && health[0].Health == player.GetComponent<CombatHealth>(), name + " binds every HUD to the actual player");
    }
    static IEnumerator ClearScene()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) Object.Destroy(root);
        yield return Frames(5);
    }
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.timeScale = 1;
        if (Keyboard.current == null) InputSystem.AddDevice<Keyboard>();
        foreach (var playerPath in GameplaySceneMaintenance.PlayerPaths)
        for (int mode = 0; mode < 3; mode++)
        {
            GameObject authored = null;
            if (mode == 1) authored = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GameplaySceneMaintenance.CursorPath));
            if (mode == 2)
            {
                authored = new GameObject("Authored HUD Canvas", typeof(Canvas));
                Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(GameplaySceneMaintenance.CombatHudPath), authored.transform, false);
            }
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(playerPath), new Vector3(0, 5, 0), Quaternion.identity);
            player.GetComponent<MechPlayerInput>().enabled = false;
            yield return Frames(6);
            CheckHud(player.name + ": " + new[] { "Player-only scene", "Existing shared canvas", "Authored HUD without a cursor" }[mode]);
            if (authored != null) Check(authored.activeSelf, "Player setup preserves the scene's authored HUD instance");
            yield return ClearScene();
        }
        foreach (var name in new[] { "KOTHTest", "EscortTest", "ZooScene", "JhMapScene" })
        {
            EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/" + name + ".unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return Frames(8); CheckHud(name);
            var player = Object.FindAnyObjectByType<MechPlayerRig>(); player.GetComponent<MechPlayerInput>().enabled = false;
            player.GetComponent<CombatHealth>().SetMaximum(10000, true);
            if (name == "JhMapScene")
            {
                var director = Object.FindAnyObjectByType<SpawnDirector>();
                Check(director != null && director.CurrentWave != null && director.CurrentWave.enemies.Length == 9, "Gym replacement groups start the current mixed enemy wave");
                director.Tick(2.1f); yield return Frames(2);
                Check(director.TotalSpawned > 0 && Object.FindAnyObjectByType<EnemyGraphController>() != null, "Repaired gym director actually spawns a current Behavior Graph enemy");
            }
        }
    }
}
