using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class EnemyBehaviorChecks
{
    public static void RunArenaBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene(EnemyBehaviorArenaSetup.ScenePath);
        SessionState.SetBool(Pending + ".Rope", false); SessionState.SetBool(Pending + ".Arena", true); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static IEnumerator RunArena()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.captureFramerate = 60;
        yield return Frames(8);
        var arena = Object.FindAnyObjectByType<EnemyBehaviorPlaytest>(); var director = Object.FindAnyObjectByType<SpawnDirector>();
        player = GameObject.FindWithTag("Player"); health = player.GetComponent<CombatHealth>(); health.SetMaximum(10000, true);
        Check(arena != null && director != null && player.activeSelf && Camera.main != null, "Saved arena contains the actual player, camera and Inspector wave controls");
        Check(EnemyAgent.All.Count == 1 && EnemyAgent.All[0].GetComponent<EnemyGraphController>().IsReady, "Saved arena starts its graph-controlled sword");
        arena.ClearEnemies(); yield return Frames(3); Check(EnemyAgent.All.Count == 0, "Inspector clear removes arena enemies");
        arena.SpawnSelected(); yield return Frames(3); Check(EnemyAgent.All.Count == 1, "Inspector single-enemy control instantiates its selected role");
        arena.ClearEnemies(); yield return Frames(3);
        arena.StartRoaming(); yield return Frames(90);
        Check(director.CurrentWave == AssetDatabase.LoadAssetAtPath<WaveData>(EnemyRosterSetup.Roaming) && EnemyAgent.All.Count >= 2,
            "Saved arena runs the configured roaming wave through its actual navigation and spawn ring");
        arena.StartSurvival(); yield return Frames(180);
        Check(director.CurrentWave == AssetDatabase.LoadAssetAtPath<WaveData>(EnemyRosterSetup.Survival) && EnemyAgent.All.Count >= 8,
            "Inspector survival control replaces the test wave and spawns its mixed roster");
        arena.StopWave(); int count = director.TotalSpawned; yield return Frames(60);
        Check(director.TotalSpawned == count && director.CurrentWave == null, "Inspector stop prevents further wave spawns");
        arena.ClearEnemies(); yield return Frames(3); Check(EnemyAgent.All.Count == 0 && player.activeSelf, "Arena reset preserves the player and clears the spawned roster");
    }
}
