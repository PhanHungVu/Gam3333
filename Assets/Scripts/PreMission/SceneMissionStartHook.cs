using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Launches the chosen quest's mission: starts a tracked attempt with the chosen torso, then loads the scene
// for the quest's Mission Type. The mission scene reports its result back through QuestMissionLaunch, and
// Return brings the player back to this (the quest board's) scene. Assign it as the flow controller's
// Mission Start Hook.
public sealed class SceneMissionStartHook : MissionStartHook
{
    [Serializable]
    public sealed class MissionScene
    {
        public QuestMissionType missionType;
        [Tooltip("Scene asset path, e.g. Assets/Scenes/KOTHTest.unity. It must be in Build Settings.")]
        public string scenePath;
    }

    [SerializeField] List<MissionScene> missionScenes = new List<MissionScene>
    {
        new MissionScene { missionType = QuestMissionType.KingOfTheHill, scenePath = "Assets/Scenes/KOTHTest.unity" },
        new MissionScene { missionType = QuestMissionType.Escort, scenePath = "Assets/Scenes/EscortTest.unity" },
        new MissionScene { missionType = QuestMissionType.Elimination, scenePath = "Assets/Scenes/EliminationTest.unity" },
    };

    [Tooltip("The board catalogue: each starting torso's mission player prefab comes from it.")]
    [SerializeField] QuestBoardCatalogue catalogue;

    public string SceneFor(QuestMissionType type)
    {
        foreach (var entry in missionScenes) if (entry != null && entry.missionType == type) return entry.scenePath;
        return null;
    }

    public override void StartMission(QuestDefinition selectedQuest, PartData startingTorso)
    {
        if (selectedQuest == null) throw new ArgumentNullException(nameof(selectedQuest));
        string scene = SceneFor(selectedQuest.missionType);
        if (string.IsNullOrEmpty(scene))
            throw new InvalidOperationException($"No mission scene is set for {selectedQuest.missionType} on {name}.");
        var run = QuestProgressionRuntime.Current;
        if (run == null || !run.TryBeginMission(selectedQuest, startingTorso, out var attempt))
            throw new InvalidOperationException("The quest board could not start this mission (no active run, or a mission is already in progress).");
        QuestMissionLaunch.Begin(attempt, scene, gameObject.scene.path);
        // The mission's player follows the chosen torso (the scene's own player is used if none is set).
        MissionLoadout.PlayerPrefab = catalogue != null ? catalogue.PlayerPrefabFor(startingTorso) : null;
        if (MissionLoadout.PlayerPrefab == null)
            Debug.LogWarning($"No mission player prefab is set for {(startingTorso != null ? startingTorso.name : "this torso")}; the scene's own player is used.", this);
        QuestMissionLaunch.LoadScene(scene);
    }
}

// The mission currently launched from the quest board: which attempt, which scene it plays in, and where
// Return goes afterwards. When that scene loads, a QuestMissionResultBridge is added to it automatically,
// so mission scenes and level prefabs never need to know about the quest board.
public static class QuestMissionLaunch
{
    public static QuestMissionAttempt Attempt { get; private set; }
    public static string MissionScene { get; private set; }
    public static string ReturnScene { get; private set; }
    public static bool Active => Attempt != null && QuestProgressionRuntime.Current?.ActiveAttempt == Attempt;

    public static void Begin(QuestMissionAttempt attempt, string missionScene, string returnScene)
    {
        Attempt = attempt; MissionScene = missionScene; ReturnScene = returnScene;
    }

    public static void Clear() { Attempt = null; MissionScene = null; ReturnScene = null; }

    // Reports the attempt's result once; afterwards the launch is over.
    public static bool Report(QuestMissionOutcome outcome)
    {
        if (!Active) return false;
        bool reported = QuestProgressionRuntime.ReportMissionResult(Attempt.Id, outcome);
        Attempt = null;
        return reported;
    }

    public static void LoadScene(string path)
    {
        if (Application.CanStreamedLevelBeLoaded(path)) { SceneManager.LoadSceneAsync(path); return; }
#if UNITY_EDITOR
        if (System.IO.File.Exists(path))
        {
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
            return;
        }
#endif
        throw new InvalidOperationException($"Mission scene {path} isn't in Build Settings.");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Install()
    {
        Clear();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!Active || scene.path != MissionScene) return;
        var bridge = new GameObject("Quest Mission Result");
        SceneManager.MoveGameObjectToScene(bridge, scene);
        bridge.AddComponent<QuestMissionResultBridge>();
    }
}

// Lives in a mission scene launched from the quest board. Success is reported straight away; after a failure
// Retry replays the same attempt, and Return reports the failure; closing the scene mid-mission abandons it.
// Return goes back to the quest board.
[DisallowMultipleComponent]
public sealed class QuestMissionResultBridge : MonoBehaviour
{
    MissionOutcomeController outcome;

    void Update()
    {
        if (outcome != null) return;
        outcome = MissionOutcomeController.Find(gameObject.scene);
        if (outcome == null) return;
        if (!string.IsNullOrEmpty(QuestMissionLaunch.ReturnScene)) outcome.ReturnScene = QuestMissionLaunch.ReturnScene;
        outcome.Finished += OnFinished;
        outcome.Returning += OnReturning;
    }

    void OnFinished(MissionOutcome result)
    {
        if (result == MissionOutcome.Succeeded) QuestMissionLaunch.Report(QuestMissionOutcome.Succeeded);
    }

    void OnReturning()
    {
        if (outcome != null && outcome.Outcome == MissionOutcome.Failed) QuestMissionLaunch.Report(QuestMissionOutcome.Failed);
    }

    void OnDestroy()
    {
        if (outcome != null) { outcome.Finished -= OnFinished; outcome.Returning -= OnReturning; }
        // Leaving mid-mission (not via the results screen) abandons it. After a failure the attempt stays
        // open so Retry can replay it.
        bool inProgress = outcome == null || outcome.Outcome == MissionOutcome.InProgress;
        if (inProgress && QuestMissionLaunch.Active && gameObject.scene.path == QuestMissionLaunch.MissionScene)
            QuestMissionLaunch.Report(QuestMissionOutcome.Abandoned);
    }
}
