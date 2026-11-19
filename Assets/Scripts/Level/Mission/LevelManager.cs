using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public enum LevelPlayerState { Missing, Alive, Dead }

// Observation and future tuning only. No progression/scaling policy is applied.
[DisallowMultipleComponent, DefaultExecutionOrder(500)]
public sealed class LevelManager : MonoBehaviour
{
    [Serializable]
    public sealed class TuningControls
    {
        [Min(0)] public float dropRateMultiplier = 1;
        [Min(0)] public float hordeSizeMultiplier = 1;
        [Min(0)] public float enemyDamageMultiplier = 1;
        [Min(0)] public float enemyHealthMultiplier = 1;
    }

    [Serializable]
    public struct LevelState
    {
        [Header("Level")]
        public float elapsedGameSeconds;
        public bool paused;
        public MissionOutcome outcome;
        public MissionState missionState;
        [Header("Local player")]
        public LevelPlayerState playerState;
        public Vector3 playerPosition;
        public float playerHealth, playerMaximumHealth, playerHealthFraction;
        public Vector3 playerVelocity;
        public MechMovementController.MovementState movementState;
        public bool grounded, quickBoosting, running;
        [Header("Objectives")]
        public int objectivesCompleted, objectivesTotal;
        public int stepsCompleted, stepsTotal;
        public float objectiveProgress;
        [Header("Horde / active wave")]
        public int activeEnemies, totalSpawned, totalLeashed;
        public bool spawningEnabled;
        public SpawnDirector.WaveStatus wave;
    }

    [Serializable]
    public struct EnemyState
    {
        public EnemyAgent enemy;
        public CombatHealth health;
        public EnemyAttack attack;
        public EnemyGraphAttack specialAttack;
        public EnemyPartDrop drops;
        public float currentHealth, maximumHealth, dropChancePercent;
        // Normal attacks can carry several different weapons. Inspect attack.Weapons
        // and attack.DamageOf(weapon) rather than inventing one shared damage.
        public float specialAttackDamage;
    }

    [Header("Sources (same scene; discovered automatically when empty)")]
    [SerializeField] CombatHealth player;
    [SerializeField] MissionTracker tracker;
    [SerializeField] SpawnDirector director;
    [Header("Future tuning only - NOT APPLIED")]
    [SerializeField] TuningControls tuning = new TuningControls();
    [SerializeField] LevelState observed;
    [SerializeField] List<EnemyState> enemies = new List<EnemyState>();
    static readonly Dictionary<Scene, LevelManager> scenes = new Dictionary<Scene, LevelManager>();
    MissionOutcomeController outcome;
    MechMovementController movement;
    EquipManager equipment;
    MechStats playerStats;
    MechCombat playerCombat;
    CombatHealth boundPlayer;
    float nextBindingSearch;

    public LevelState State => observed;
    public TuningControls FutureTuning => tuning;
    public bool AppliesTuning => false;
    public IReadOnlyList<EnemyState> Enemies => enemies;
    public CombatHealth Player => player;
    public MechStats PlayerStats => playerStats;
    public EquipManager PlayerEquipment => equipment;
    public MechCombat PlayerCombat => playerCombat;
    public MechMovementController PlayerMovement => movement;
    public MissionTracker Mission => tracker;
    public SpawnDirector Spawner => director;
    public MissionOutcomeController Outcome => outcome;
    public event Action<LevelState> StateUpdated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRegistry() => scenes.Clear();
    public static LevelManager Find(Scene scene)
        => scenes.TryGetValue(scene, out var found) && found != null && found.isActiveAndEnabled ? found : null;

    public static LevelManager Ensure(Scene scene)
    {
        var found = Find(scene); if (found != null) return found;
        // Also supports an authored manager whose Awake has not run yet.
        found = FindInScene<LevelManager>(scene, true);
        if (found != null) { scenes[scene] = found; return found; }
        var root = new GameObject("Level Manager"); SceneManager.MoveGameObjectToScene(root, scene);
        return root.AddComponent<LevelManager>();
    }
    static T FindInScene<T>(Scene scene, bool onlyEnabled = false) where T : Component
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var component in root.GetComponentsInChildren<T>())
                if (component != null && component.gameObject.activeInHierarchy
                    && (!onlyEnabled || !(component is Behaviour behaviour) || behaviour.isActiveAndEnabled)) return component;
        return null;
    }
    void Awake()
    {
        var existing = Find(gameObject.scene);
        if (existing != null && existing != this) { enabled = false; return; }
        scenes[gameObject.scene] = this;
        Refresh();
    }
    void OnEnable()
    {
        var existing = Find(gameObject.scene);
        if (existing != null && existing != this) { enabled = false; return; }
        scenes[gameObject.scene] = this;
    }
    void OnDisable()
    { if (scenes.TryGetValue(gameObject.scene, out var registered) && registered == this) scenes.Remove(gameObject.scene); }
    void OnDestroy()
    { if (scenes.TryGetValue(gameObject.scene, out var registered) && registered == this) scenes.Remove(gameObject.scene); }

    public void BindPlayer(CombatHealth localPlayer)
    {
        if (localPlayer != null && localPlayer.gameObject.scene != gameObject.scene)
            throw new ArgumentException("LevelManager player must belong to the same scene.", nameof(localPlayer));
        player = localPlayer; CachePlayerSources(); Refresh();
    }
    void CachePlayerSources()
    {
        boundPlayer = player;
        movement = player != null ? player.GetComponent<MechMovementController>() : null;
        equipment = player != null ? player.GetComponent<EquipManager>() : null;
        playerStats = player != null ? player.GetComponent<MechStats>() : null;
        playerCombat = player != null ? player.GetComponent<MechCombat>() : null;
    }
    void ResolveSources()
    {
        if (player != null && player.gameObject.scene != gameObject.scene) player = null;
        if (tracker != null && tracker.gameObject.scene != gameObject.scene) tracker = null;
        if (director != null && director.gameObject.scene != gameObject.scene) director = null;
        // No global scene searches per frame. Retry missing/late-spawned sources
        // twice a second; existing references still report health/death immediately.
        if (Time.unscaledTime >= nextBindingSearch)
        {
            nextBindingSearch = Time.unscaledTime + .5f;
            outcome = MissionOutcomeController.Find(gameObject.scene);
            if (tracker == null || tracker.gameObject.scene != gameObject.scene) tracker = FindInScene<MissionTracker>(gameObject.scene);
            if (director == null || director.gameObject.scene != gameObject.scene) director = FindInScene<SpawnDirector>(gameObject.scene);
            if (player == null || player.gameObject.scene != gameObject.scene)
            {
                player = null;
                if (outcome != null)
                    foreach (var participant in outcome.Participants)
                        if (participant.Local && participant.Health != null) { player = participant.Health; break; }
                // Never mistake the player-team escort payload for the player.
                if (player == null)
                {
                    var input = FindInScene<MechPlayerInput>(gameObject.scene);
                    if (input != null) player = input.GetComponent<CombatHealth>();
                }
            }
        }
        if (player != boundPlayer) CachePlayerSources();
        // A local mission may have been registered after this manager's Awake.
        if (outcome == null) outcome = MissionOutcomeController.Find(gameObject.scene);
    }

    void LateUpdate() => Tick(Time.deltaTime);
    // Refreshing/querying observations never advances the clock or consumes RNG.
    public void Refresh() => Tick(0);
    void Tick(float deltaTime)
    {
        ResolveSources();
        observed.paused = Time.timeScale <= 0 || MissionPauseMenu.IsPaused;
        observed.outcome = outcome != null ? outcome.Outcome : MissionOutcome.InProgress;
        observed.missionState = tracker != null ? tracker.State : MissionState.InProgress;
        if (!observed.paused && observed.outcome == MissionOutcome.InProgress && observed.missionState != MissionState.Complete
            && deltaTime > 0 && !float.IsNaN(deltaTime) && !float.IsInfinity(deltaTime)) observed.elapsedGameSeconds += deltaTime;
        observed.playerState = player == null ? LevelPlayerState.Missing : player.IsDead ? LevelPlayerState.Dead : LevelPlayerState.Alive;
        observed.playerPosition = player != null ? player.transform.position : Vector3.zero;
        observed.playerHealth = player != null ? player.CurrentHealth : 0;
        observed.playerMaximumHealth = player != null ? player.MaximumHealth : 0;
        observed.playerHealthFraction = observed.playerMaximumHealth > 0 ? Mathf.Clamp01(observed.playerHealth / observed.playerMaximumHealth) : 0;
        observed.playerVelocity = movement != null ? movement.Velocity : Vector3.zero;
        observed.movementState = movement != null ? movement.State : default;
        observed.grounded = movement != null && movement.IsGrounded;
        observed.quickBoosting = movement != null && movement.IsQuickBoosting;
        observed.running = movement != null && movement.IsAutoRunning;
        ReadObjectives(); ReadEnemies();
        observed.spawningEnabled = director != null && director.isActiveAndEnabled;
        observed.wave = director != null ? director.InspectCurrentWave() : default;
        observed.totalSpawned = director != null ? director.TotalSpawned : 0;
        observed.totalLeashed = director != null ? director.TotalLeashed : 0;
        StateUpdated?.Invoke(observed);
    }
    void ReadObjectives()
    {
        observed.objectivesTotal = observed.objectivesCompleted = observed.stepsTotal = observed.stepsCompleted = 0;
        foreach (var objective in MissionObjectives.All)
        {
            if (!(objective is Component component) || component == null || component.gameObject.scene != gameObject.scene) continue;
            observed.objectivesTotal++;
            if (objective.IsComplete) observed.objectivesCompleted++;
            if (objective is IMissionProgress progress && progress.StepsTotal > 0)
            { observed.stepsTotal += progress.StepsTotal; observed.stepsCompleted += Mathf.Clamp(progress.StepsDone, 0, progress.StepsTotal); }
            else { observed.stepsTotal++; if (objective.IsComplete) observed.stepsCompleted++; }
        }
        observed.objectiveProgress = observed.stepsTotal > 0 ? (float)observed.stepsCompleted / observed.stepsTotal : 0;
    }
    void ReadEnemies()
    {
        enemies.Clear();
        foreach (var enemy in EnemyAgent.All)
        {
            if (enemy == null || !enemy.isActiveAndEnabled || enemy.gameObject.scene != gameObject.scene) continue;
            var health = enemy.GetComponent<CombatHealth>();
            if (health != null && health.IsDead) continue;
            var drops = enemy.GetComponent<EnemyPartDrop>();
            var special = enemy.GetComponent<EnemyGraphAttack>();
            enemies.Add(new EnemyState
            {
                enemy = enemy, health = health, attack = enemy.GetComponent<EnemyAttack>(), specialAttack = special, drops = drops,
                currentHealth = health != null ? health.CurrentHealth : 0, maximumHealth = health != null ? health.MaximumHealth : 0,
                dropChancePercent = drops != null ? drops.DropChancePercent : 0, specialAttackDamage = special != null ? special.Damage : 0
            });
        }
        observed.activeEnemies = enemies.Count;
    }
}
