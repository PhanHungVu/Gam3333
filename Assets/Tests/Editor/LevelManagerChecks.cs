using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class LevelManagerObjectiveProbe : MonoBehaviour, IMissionObjective, IMissionProgress
{
    public bool complete;
    public int done, total;
    public bool IsComplete => complete;
    public int StepsDone => done;
    public int StepsTotal => total;
    void OnEnable() => MissionObjectives.Register(this);
    void OnDisable() => MissionObjectives.Unregister(this);
}

public static class LevelManagerChecks
{
    const string Pending = "LevelManagerChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("LEVEL MANAGER PASS: " + message); }
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    static object Get(object obj, string field) => obj.GetType().GetField(field, Private).GetValue(obj);
    static void Tick(LevelManager level, float dt) => typeof(LevelManager).GetMethod("Tick", Private).Invoke(level, new object[] { dt });
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) Run(); };
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).Contains(".quest-review")) throw new Exception("Disposable project only.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static CombatHealth Health(string name, float maximum, CombatTeam team)
    {
        var health = new GameObject(name).AddComponent<CombatHealth>(); health.Team = team; health.SetMaximum(maximum, true); return health;
    }
    static void Run()
    {
        SessionState.SetBool(Pending, false); AudioListener.volume = 0; Time.timeScale = 1;
        try
        {
            PreMissionReliabilityChecks.BeginLogAudit();
            var scene = SceneManager.GetActiveScene(); var outcome = MissionOutcomeController.Ensure(scene);
            var level = LevelManager.Find(scene);
            Check(level != null && level == LevelManager.Ensure(scene), "Existing mission creation automatically provides exactly one scene level manager");
            Check(!level.AppliesTuning && level.FutureTuning.dropRateMultiplier == 1 && level.FutureTuning.hordeSizeMultiplier == 1
                && level.FutureTuning.enemyDamageMultiplier == 1 && level.FutureTuning.enemyHealthMultiplier == 1, "Future controls start neutral and explicitly do not apply scaling");
            level.Refresh(); Check(level.State.playerState == LevelPlayerState.Missing && level.State.objectivesTotal == 0 && level.State.activeEnemies == 0,
                "Missing player/objectives/waves are safe in an empty or still-loading level");
            var player = Health("Level test player", 200, CombatTeam.Player); player.transform.position = new Vector3(4, 2, 7);
            outcome.RegisterParticipant("local-level-test", player, true); Set(level, "nextBindingSearch", 0f); level.Refresh();
            Check(level.Player == player && level.State.playerState == LevelPlayerState.Alive, "Late participant registration binds the local player without treating player-team cargo as a player");
            player.ApplyDamage(50); level.Refresh();
            Check(level.State.playerHealth == 150 && level.State.playerMaximumHealth == 200 && level.State.playerHealthFraction == .75f
                && level.State.playerPosition == player.transform.position, "Observed player HP/fraction/position reflect real health without modifying it");
            int updates = 0; level.StateUpdated += _ => updates++;
            Tick(level, 2); level.Refresh(); Check(level.State.elapsedGameSeconds == 2 && updates == 2, "Game clock advances with gameplay delta; read-only refresh emits state but never advances time");
            Time.timeScale = 0; Tick(level, 5); Check(level.State.elapsedGameSeconds == 2 && level.State.paused, "Paused gameplay never advances the level clock"); Time.timeScale = 1;
            Tick(level, float.NaN); Tick(level, float.PositiveInfinity); Tick(level, -1);
            Check(level.State.elapsedGameSeconds == 2, "Invalid or negative elapsed time cannot corrupt the clock");

            var simple = new GameObject("Simple objective").AddComponent<LevelManagerObjectiveProbe>();
            var stepped = new GameObject("Escort-style objective").AddComponent<LevelManagerObjectiveProbe>(); stepped.total = 3; stepped.done = 1;
            level.Refresh(); Check(level.State.objectivesTotal == 2 && level.State.objectivesCompleted == 0 && level.State.stepsCompleted == 1
                && level.State.stepsTotal == 4 && level.State.objectiveProgress == .25f, "Objective counts and escort-style multi-step progress remain distinct");
            simple.complete = true; stepped.done = 3; stepped.complete = true; level.Refresh();
            Check(level.State.objectivesCompleted == 2 && level.State.stepsCompleted == 4 && level.State.objectiveProgress == 1,
                "Actual objective completion updates the observation, without completing the mission itself");
            Check(outcome.Outcome == MissionOutcome.InProgress, "Observing all completed objectives never triggers extraction, success or progression rules");

            var enemy = Health("Level enemy", 300, CombatTeam.Enemy); enemy.gameObject.AddComponent<EnemyAgent>();
            var drops = enemy.gameObject.AddComponent<EnemyPartDrop>(); var attack = enemy.gameObject.AddComponent<EnemyAttack>();
            var weaponObject = new GameObject("Observed weapon"); weaponObject.transform.SetParent(enemy.transform, false);
            var part = weaponObject.AddComponent<Part>(); part.data = ScriptableObject.CreateInstance<PartData>();
            part.data.stats = new List<StatEntry> { new StatEntry { type = StatType.Damage, value = 80 } };
            var animator = weaponObject.AddComponent<MechWeaponAnimator>();
            Set(attack, "weapons", new[] { new EnemyWeapon { weapon = animator, damageMultiplier = 1.25f, cooldownMultiplier = 1, projectileSpeedMultiplier = 1 } });
            enemy.ApplyDamage(25); level.Refresh();
            Check(level.State.activeEnemies == 1 && level.Enemies.Count == 1 && level.Enemies[0].currentHealth == 275
                && level.Enemies[0].maximumHealth == 300 && level.Enemies[0].dropChancePercent == 5, "Horde observation exposes each active enemy's real HP and unchanged five-percent drop chance");
            Check(level.Enemies[0].attack.Weapons.Count == 1 && level.Enemies[0].attack.DamageOf(animator) == 100,
                "Normal enemy weapon damage remains accessible with its existing per-weapon multiplier");

            var director = new GameObject("Observed director").AddComponent<SpawnDirector>();
            var wave = ScriptableObject.CreateInstance<WaveData>(); wave.resourceTotal = 10; wave.spawnInterval = .75f; wave.maxConcurrentEnemies = 6;
            var point = new GameObject("Observed spawn point").AddComponent<SpawnPoint>();
            Check(director.RunWave(wave, new[] { point }, director), "Existing director starts its own wave normally");
            director.ScaleWave(director, 1.5f); director.SetBudget(director, 7); Set(level, "director", director); level.Refresh();
            var status = level.State.wave;
            Check(status.data == wave && status.resourceBudget == 15 && status.resourceRemaining == 10 && status.concurrentEnemyLimit == 6
                && status.objectiveCostLimit == 7 && status.spawnInterval == .75f && director.TotalSpawned == 0,
                "Manager reads actual runtime-scaled budget/caps/interval without ticking or spawning the wave");
            var rng = JsonUtility.ToJson(UnityEngine.Random.state);
            level.FutureTuning.dropRateMultiplier = 4; level.FutureTuning.hordeSizeMultiplier = 5;
            level.FutureTuning.enemyDamageMultiplier = 6; level.FutureTuning.enemyHealthMultiplier = 7;
            for (int i = 0; i < 10; i++) level.Refresh();
            Check(drops.DropChancePercent == 5 && enemy.MaximumHealth == 300 && enemy.CurrentHealth == 275 && attack.DamageOf(animator) == 100
                && wave.resourceTotal == 10 && director.InspectCurrentWave().resourceRemaining == 10 && director.TotalSpawned == 0,
                "Even non-neutral future controls do not alter drops, HP, damage, wave assets or enemy numbers");
            Check(JsonUtility.ToJson(UnityEngine.Random.state) == rng, "Repeated observation never rerolls loot or consumes combat/spawn RNG");
            var roamer = new GameObject("Dormant roaming source").AddComponent<RoamingSpawner>(); Set(roamer, "target", player.transform);
            var idleDirector = new GameObject("Dormant roaming director").AddComponent<SpawnDirector>(); Set(idleDirector, "roamingWave", wave); Set(idleDirector, "roamingSpawner", roamer);
            Set(level, "director", idleDirector); level.Refresh();
            Check(Get(idleDirector, "roaming") == null && level.State.wave.data == null,
                "Monitoring does not initialize a dormant roaming cycle through CurrentWave's side-effectful getter");

            var secondScene = SceneManager.CreateScene("Level Manager additive check"); SceneManager.SetActiveScene(secondScene);
            var secondOutcome = MissionOutcomeController.Ensure(secondScene); var second = LevelManager.Find(secondScene);
            var otherObjective = new GameObject("Other-scene objective").AddComponent<LevelManagerObjectiveProbe>(); otherObjective.complete = true;
            var otherEnemy = Health("Other-scene enemy", 450, CombatTeam.Enemy); otherEnemy.gameObject.AddComponent<EnemyAgent>();
            level.Refresh(); second.Refresh();
            Check(second != null && second != level && LevelManager.Ensure(scene) == level && LevelManager.Ensure(secondScene) == second,
                "Additively loaded levels each retain their own manager");
            Check(level.State.objectivesTotal == 2 && second.State.objectivesTotal == 1 && level.State.activeEnemies == 1 && second.State.activeEnemies == 1,
                "Objective and enemy registries are filtered by scene, without leaking another level's state");
            bool rejected = false; try { level.BindPlayer(otherEnemy); } catch (ArgumentException) { rejected = true; }
            Check(rejected && level.Player == player, "Explicit binding rejects a player from another scene");
            SceneManager.SetActiveScene(scene);
            enemy.ApplyDamage(1000); level.Refresh(); Check(level.State.activeEnemies == 0, "Dead/disabled enemies leave the active horde observation");
            player.ApplyDamage(1000); level.Refresh(); Check(level.State.playerState == LevelPlayerState.Dead && level.State.playerHealth == 0,
                "Player death is retained even after CombatHealth deactivates the player object");
            float elapsed = level.State.elapsedGameSeconds; outcome.ReportFailure("Level manager terminal-state check"); Tick(level, 10);
            Check(level.State.outcome == MissionOutcome.Failed && level.State.elapsedGameSeconds == elapsed, "The existing mission outcome stops the observation clock without the manager deciding results");
            level.enabled = false; Check(LevelManager.Find(scene) == null, "Disabled manager is not returned as an active scene service");
            level.enabled = true; Check(LevelManager.Find(scene) == level && level.State.elapsedGameSeconds == elapsed, "Re-enabling retains elapsed time and re-registers the same manager");
            Object.DestroyImmediate(level.gameObject); Check(LevelManager.Find(scene) == null && LevelManager.Find(secondScene) == second, "Destroying one level manager clears only that scene's registration");
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors(); PreMissionReliabilityChecks.EndLogAudit();
            Debug.Log("LEVEL MANAGER CHECKS COMPLETE: " + checks); EditorApplication.Exit(0);
        }
        catch (Exception error) { Time.timeScale = 1; PreMissionReliabilityChecks.EndLogAudit(); Debug.LogException(error); EditorApplication.Exit(1); }
    }
}
