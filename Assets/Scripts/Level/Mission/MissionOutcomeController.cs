using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

public enum MissionOutcome { InProgress, Succeeded, Failed }

// One terminal outcome per loaded gameplay scene. No global time pause and no networking assumptions.
[DisallowMultipleComponent]
public sealed class MissionOutcomeController : MonoBehaviour
{
    public sealed class Participant
    {
        public string Id { get; internal set; }
        public CombatHealth Health { get; internal set; }
        public bool Local { get; internal set; }
        public bool Dead { get; internal set; }
        internal Action onDeath;
    }
    static readonly Dictionary<Scene, MissionOutcomeController> scenes = new Dictionary<Scene, MissionOutcomeController>();
    readonly List<Participant> participants = new List<Participant>();
    [SerializeField] MissionResultsView resultsPrefab;
    [SerializeField] string mainMenuScene = "Assets/Scenes/MainMenuScene.unity";
    [SerializeField] UnityEvent succeeded = new UnityEvent(), failed = new UnityEvent();
    bool rosterReady = true;
    MissionResultsView view;
    public MissionOutcome Outcome { get; private set; }
    public string Reason { get; private set; }
    public IReadOnlyList<Participant> Participants => participants;
    public MissionResultsView View => view;
    public event Action<MissionOutcome> Finished;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void Reset() => scenes.Clear();
    public static MissionOutcomeController Find(Scene scene) => scenes.TryGetValue(scene, out var result) && result != null ? result : null;
    public static bool BlocksGameplay(Scene scene) => Find(scene) is MissionOutcomeController mission && mission.Outcome != MissionOutcome.InProgress;
    public static MissionOutcomeController Ensure(Scene scene, MissionResultsView prefab = null)
    {
        var existing = Find(scene); if (existing != null) return existing;
        var root = new GameObject("Mission Outcome"); SceneManager.MoveGameObjectToScene(root, scene);
        var result = root.AddComponent<MissionOutcomeController>(); result.resultsPrefab = prefab; return result;
    }
    void Awake()
    {
        var existing = Find(gameObject.scene);
        if (existing != null && existing != this) { enabled = false; return; }
        scenes[gameObject.scene] = this;
    }
    public bool RegisterParticipant(string id, CombatHealth health, bool locallyControlled = false)
    {
        if (Outcome != MissionOutcome.InProgress || string.IsNullOrWhiteSpace(id) || health == null) return false;
        foreach (var entry in participants)
            if (entry.Id == id || entry.Health == health) return entry.Health == health && entry.Id == id;
        var participant = new Participant { Id = id, Health = health, Local = locallyControlled, Dead = health.IsDead };
        participant.onDeath = () => participant.Dead = true;
        health.Died += participant.onDeath; participants.Add(participant); return true;
    }
    // Call before registering an asynchronously supplied team, then seal after the complete roster arrives.
    public void BeginParticipantRegistration() => rosterReady = false;
    public void CompleteParticipantRegistration() => rosterReady = true;
    void LateUpdate()
    {
        if (!rosterReady || Outcome != MissionOutcome.InProgress || participants.Count == 0) return;
        foreach (var participant in participants) if (!participant.Dead) return;
        ReportFailure("The team has fallen.");
    }
    public bool ReportSuccess() => Finish(MissionOutcome.Succeeded, "Mission complete.");
    // Explicit terminal failure only. Ordinary failed/retrying KOTH holds must not call this.
    public bool ReportFailure(string reason) => Finish(MissionOutcome.Failed, string.IsNullOrWhiteSpace(reason) ? "Mission failed." : reason);
    bool Finish(MissionOutcome outcome, string reason)
    {
        if (Outcome != MissionOutcome.InProgress) return false;
        Outcome = outcome; Reason = reason;
        foreach (var component in FindObjectsByType<MonoBehaviour>())
        {
            if (component.gameObject.scene != gameObject.scene) continue;
            if (component is MechPlayerInput || component is MechMovementController || component is MechCombat
                || component is MechTargetLockController || component is PickupDetector || component is EnemyAgent
                || component is EnemyAttack || component is EnemyGraphAttack || component is Unity.Behavior.BehaviorGraphAgent
                || component is SpawnDirector || component is RoamingSpawner || component is EnemyBehaviorPlaytest)
                component.enabled = false;
        }
        foreach (var projectile in FindObjectsByType<MechProjectile>())
            if (projectile.gameObject.scene == gameObject.scene) projectile.gameObject.SetActive(false);
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        view = resultsPrefab != null ? Instantiate(resultsPrefab, transform) : MissionResultsView.Create(transform);
        view.Show(this);
        Finished?.Invoke(outcome);
        if (outcome == MissionOutcome.Succeeded) succeeded.Invoke(); else failed.Invoke();
        return true;
    }
    public void Retry() => Load(gameObject.scene.path);
    public void ReturnToMenu() => Load(mainMenuScene);
    void Load(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (Application.CanStreamedLevelBeLoaded(path)) { SceneManager.LoadSceneAsync(path); return; }
#if UNITY_EDITOR
        if (System.IO.File.Exists(path)) UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#endif
    }
    void OnDestroy()
    {
        if (Find(gameObject.scene) == this) scenes.Remove(gameObject.scene);
        foreach (var participant in participants) if (participant.Health != null) participant.Health.Died -= participant.onDeath;
    }
}
