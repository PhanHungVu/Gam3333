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

public static partial class EnemyBehaviorChecks
{
    public static void RunEncounterBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene(EnemyBehaviorArenaSetup.ScenePath);
        foreach (string mode in new[] { ".Arena", ".Rope", ".Coordination", ".AttackCoordination", ".Pressure", ".Ranged", ".Special" }) SessionState.SetBool(Pending + mode, false);
        SessionState.SetBool(Pending + ".Encounters", true); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static EnemyEncounterPreset ExplicitTest(params EnemyEncounterSpawn[] spawns)
    {
        var preset = ScriptableObject.CreateInstance<EnemyEncounterPreset>(); preset.name = "Placement validation fixture";
        var fields = new SerializedObject(preset); fields.FindProperty("layout").enumValueIndex = 1;
        var entries = fields.FindProperty("explicitSpawns"); entries.arraySize = spawns.Length;
        for (int i = 0; i < spawns.Length; i++)
        {
            entries.GetArrayElementAtIndex(i).FindPropertyRelative("role").intValue = (int)spawns[i].role;
            entries.GetArrayElementAtIndex(i).FindPropertyRelative("offset").vector2Value = spawns[i].offset;
        }
        fields.ApplyModifiedPropertiesWithoutUndo(); return preset;
    }
    static IEnumerator RunEncounters()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.captureFramerate = 60;
        UnityEngine.Random.InitState(67421);
        yield return Frames(8); var arena = Object.FindAnyObjectByType<EnemyBehaviorPlaytest>();
        player = GameObject.FindWithTag("Player"); health = player.GetComponent<CombatHealth>(); health.SetMaximum(100000, true);
        player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        grid = Object.FindAnyObjectByType<NavGrid>(); field = Object.FindAnyObjectByType<FlowField>();
        Check(arena.EncounterPresets.Count == 7 && arena.EncounterPresets.All(p => p != null), "Saved arena references all seven editable encounter assets");
        arena.ClearEnemies(); yield return Frames(3);
        var background = InputSystem.settings.backgroundBehavior; var editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var keyboard = InputSystem.AddDevice<Keyboard>("Encounter preset shortcuts");
        arena.SelectEncounter(0); yield return PressArenaKey(keyboard, Key.PageDown);
        Check(arena.SelectedEncounterIndex == 1 && EnemyAgent.All.Count == 0, "Page Down selects a named encounter without spawning it");
        yield return PressArenaKey(keyboard, Key.F11);
        Check(ArenaBrains().Length == 12 && ArenaBrains().Count(a => a.Role == EnemyBehaviorRole.Sword) == 10
            && ArenaBrains().Count(a => a.Role == EnemyBehaviorRole.Crossbow) == 2, "F11 starts the selected mixed preset with its exact configured counts");
        var initial = arena.SwarmSpawnPositions.ToArray(); var originalActors = ArenaBrains();
        yield return PressArenaKey(keyboard, Key.F11);
        Check(initial.SequenceEqual(arena.SwarmSpawnPositions) && originalActors.All(a => a == null),
            "Starting the same seed replaces the old actors and preserves spawn positions");
        var shooters = ArenaBrains().Where(a => a.Role == EnemyBehaviorRole.Crossbow).ToArray();
        Check(Vector3.Dot(Steering.Flat(shooters[0].transform.position).normalized, Steering.Flat(shooters[1].transform.position).normalized) < .5f,
            "Seeded role groups are interleaved across the arc rather than clustering all shooters at one end");
        yield return PressArenaKey(keyboard, Key.PageUp); yield return PressArenaKey(keyboard, Key.PageUp);
        Check(arena.SelectedEncounterIndex == 6, "Preset keyboard selection wraps from A to G");
        yield return PressArenaKey(keyboard, Key.PageDown); Check(arena.SelectedEncounterIndex == 0, "Preset keyboard selection wraps from G to A");

        var edited = Object.Instantiate(arena.EncounterPresets[1]); arena.StartPreset(edited); yield return Frames(3);
        var snapshot = arena.SwarmSpawnPositions.ToArray(); Vector3 start = player.transform.position; Quaternion facing = player.transform.rotation;
        float hp = health.CurrentHealth; var edit = new SerializedObject(edited);
        edit.FindProperty("groups").GetArrayElementAtIndex(0).FindPropertyRelative("count").intValue = 4;
        edit.FindProperty("seed").intValue = 9981; edit.ApplyModifiedPropertiesWithoutUndo();
        MovePlayer(new Vector3(45, 1.6f, 35)); player.transform.rotation = Quaternion.Euler(0, 90, 0); arena.SelectEncounter(3);
        yield return PressArenaKey(keyboard, Key.F12);
        Check(ArenaBrains().Length == 12 && snapshot.SequenceEqual(arena.SwarmSpawnPositions)
            && Vector3.Distance(player.transform.position, start) < .01f && Quaternion.Angle(player.transform.rotation, facing) < .01f,
            "F12 replays the captured composition, seed layout and starting pose despite later preset edits or selection changes");
        Check(health.CurrentHealth == hp, "Encounter replay preserves the player's health instead of refilling it");
        yield return PressArenaKey(keyboard, Key.F9); yield return PressArenaKey(keyboard, Key.F12);
        Check(ArenaBrains().Length == 12 && snapshot.SequenceEqual(arena.SwarmSpawnPositions), "Clearing the arena preserves the last preset for replay");
        arena.StartPreset(edited); yield return Frames(3);
        Check(ArenaBrains().Length == 6 && !snapshot.SequenceEqual(arena.SwarmSpawnPositions), "A fresh preset start picks up edited counts and seed");
        Object.Destroy(edited); InputSystem.RemoveDevice(keyboard);
        InputSystem.settings.backgroundBehavior = background; InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;

        int[][] roles = { new[] {10,0,0,0,0,0,0,0,0}, new[] {10,0,0,0,0,2,0,0,0}, new[] {10,0,0,0,1,2,0,0,0},
            new[] {8,0,0,0,0,2,1,0,0}, new[] {10,4,2,2,4,4,2,1,1}, new[] {8,0,0,0,0,2,0,1,0}, new[] {8,0,0,0,0,2,0,0,1} };
        for (int preset = 0; preset < 7; preset++)
        {
            arena.StartPreset(arena.EncounterPresets[preset]); yield return Frames(4); var actors = ArenaBrains();
            Check(arena.SpawnIssues.Count == 0 && actors.Length == roles[preset].Sum()
                && Enumerable.Range(0, 9).All(role => actors.Count(a => (int)a.Role == role) == roles[preset][role]),
                "Actual prefab composition and supported placement: " + arena.EncounterPresets[preset].DisplayName);
            Check(actors.All(a => a.IsReady && a.Graph.Graph.IsRunning) && actors.SelectMany(a => a.GetComponents<EnemyPartDrop>()).All(drop => !drop.enabled)
                && arena.Coordinator.ContinuousMeleePressure && !arena.Coordinator.UseAttackPermissions,
                "Preset uses running original graphs, pressure mode and disabled test loot: " + arena.EncounterPresets[preset].DisplayName);
        }
        arena.ClearEnemies(); yield return Frames(3);

        var floor = GameObject.Find("Prototype ground"); Vector3 floorScale = floor.transform.localScale;
        floor.transform.localScale = new Vector3(80, .5f, 80);
        var obstruction = GameObject.CreatePrimitive(PrimitiveType.Cube); obstruction.transform.position = new Vector3(10, 3, -10); obstruction.transform.localScale = new Vector3(3, 6, 3);
        var raised = GameObject.CreatePrimitive(PrimitiveType.Cube); raised.transform.position = new Vector3(20, 1.5f, -10); raised.transform.localScale = new Vector3(6, 3, 6);
        Physics.SyncTransforms();
        var placement = ExplicitTest(new EnemyEncounterSpawn(EnemyBehaviorRole.Sword, new Vector2(-10, -8)),
            new EnemyEncounterSpawn(EnemyBehaviorRole.Sword, new Vector2(-10, -8)), new EnemyEncounterSpawn(EnemyBehaviorRole.Sword, Vector2.zero),
            new EnemyEncounterSpawn(EnemyBehaviorRole.Sword, new Vector2(0, 8)), new EnemyEncounterSpawn(EnemyBehaviorRole.Sword, new Vector2(150, 0)),
            new EnemyEncounterSpawn(EnemyBehaviorRole.Sword, new Vector2(80, 0)), new EnemyEncounterSpawn((EnemyBehaviorRole)99, new Vector2(10, -20)),
            new EnemyEncounterSpawn(EnemyBehaviorRole.Sword, new Vector2(10, -10)), new EnemyEncounterSpawn(EnemyBehaviorRole.HeavyRanged, new Vector2(20, -10)));
        arena.StartPreset(placement); yield return Frames(4);
        Check(ArenaBrains().Length == 2 && arena.SpawnIssues.Count == 7 && arena.LastEncounterReport.Contains("2/9"),
            "Explicit layouts report duplicate, player-overlap, blocked, off-grid, unsupported, missing-prefab and body-clearance failures");
        var onPlatform = ArenaBrains().Single(a => a.Role == EnemyBehaviorRole.HeavyRanged);
        Check(Mathf.Abs(onPlatform.transform.position.y - (3 + onPlatform.Agent.PivotHeight)) < .05f,
            "A validated raised-floor spawn remains on its sampled floor after EnemyAgent.Start");
        arena.ClearEnemies(); Object.Destroy(placement); Object.Destroy(obstruction); Object.Destroy(raised);
        floor.transform.localScale = floorScale; yield return Frames(3); Physics.SyncTransforms();

        arena.StartPreset(arena.EncounterPresets[0]); yield return Frames(3); var owner = EnemyAgent.All[0];
        MechProjectile Shot(Transform source, string name)
        {
            var shot = MechProjectile.Create(null, name, new Vector3(60, 5, 0), .1f, null, Color.white);
            shot.Launch(source, Vector3.right, 1, 1, .1f, 100, 0, shot.transform.position); return shot;
        }
        var enemyShot = Shot(owner.transform, "Old enemy shot"); var playerShot = Shot(player.transform, "Player shot retained");
        var otherScene = SceneManager.CreateScene("Other encounter cleanup scope");
        var otherShot = Shot(owner.transform, "Other scene enemy shot retained"); SceneManager.MoveGameObjectToScene(otherShot.gameObject, otherScene);
        Object.Destroy(owner.gameObject); yield return Frames(3);
        Check(enemyShot.IsLive && enemyShot.OwnerTeam == CombatTeam.Enemy, "Enemy projectiles retain ownership for cleanup after their shooter is destroyed");
        arena.ClearEnemies(); yield return Frames(3);
        Check(enemyShot == null && playerShot != null && playerShot.IsLive && otherShot != null && otherShot.IsLive,
            "Arena reset removes old enemy shots while preserving player shots and other scenes");
        Object.Destroy(playerShot.gameObject); Object.Destroy(otherShot.gameObject);
        var unload = SceneManager.UnloadSceneAsync(otherScene); yield return Until(() => unload.isDone, 3, "Temporary scene did not unload");

        var limited = Object.Instantiate(arena.EncounterPresets[0]); var limitFields = new SerializedObject(limited);
        limitFields.FindProperty("groups").GetArrayElementAtIndex(0).FindPropertyRelative("count").intValue = 10000; limitFields.ApplyModifiedPropertiesWithoutUndo();
        var plan = new List<EnemyEncounterSpawn>(); limited.BuildSpawnPlan(plan);
        Check(plan.Count == EnemyEncounterPreset.MaximumActors && limited.RequestedCount == 10000,
            "Oversized designer counts produce a bounded plan without losing the requested count for reporting"); Object.Destroy(limited);

        arena.StartPreset(arena.EncounterPresets[4]); yield return Frames(3); var horde = ArenaBrains();
        bool caps = true; int simulated = Time.frameCount; double began = EditorApplication.timeSinceStartup;
        for (int frame = 0; frame < 1500; frame++)
        { yield return Frames(1); caps &= arena.SpecialCoordinator.ActiveSpinnerAttacks <= 1 && arena.SpecialCoordinator.ActiveOrbitAttacks <= 1; }
        Debug.Log("LARGE ENCOUNTER: actors=" + horde.Length + "; simulation frames=" + (Time.frameCount - simulated)
            + "; headless wall seconds=" + (EditorApplication.timeSinceStartup - began).ToString("F2") + "; "
            + string.Join("; ", horde.GroupBy(a => a.Role).Select(group => group.Key + " actors=" + group.Count()
                + " releases=" + group.Sum(a => a.Attack.Releases)
                + " contacts=" + group.Sum(a => a.SpecialAttack is EnemySpinnerAttack spin ? spin.Contacts : 0)
                + " orbits=" + group.Sum(a => a.SpecialAttack is EnemyRopeAttack rope ? rope.OrbitsStarted : 0)
                + " impacts=" + group.Sum(a => a.SpecialAttack is EnemyRopeAttack rope ? rope.Impacts : 0))));
        Check(horde.Length == 30 && horde.All(a => a.IsReady && a.Graph.Graph.IsRunning && float.IsFinite(a.transform.position.x)
            && float.IsFinite(a.transform.position.y) && float.IsFinite(a.transform.position.z)), "The 30-actor preset remains active and finite for twenty-five combat seconds");
        Check(caps && horde.Where(a => a.UsesSpecialCoordination).Any(a => a.SpecialAttack is EnemySpinnerAttack spin && spin.Contacts > 0)
            && horde.Single(a => a.Role == EnemyBehaviorRole.RockRope).GetComponent<EnemyRopeAttack>().OrbitsStarted > 0
            && horde.Single(a => a.Role == EnemyBehaviorRole.RockRopePull).GetComponent<EnemyRopeAttack>().Impacts > 0,
            "Spinner and both rope roles contribute while large-horde special limits remain effective");
        Debug.Log("LARGE STATIONARY NONPARTICIPANTS: " + string.Join("; ", horde.Where(a => a.SpecialAttack == null && a.Attack.Releases == 0)
            .Select(a => a.Role + " distance=" + a.Agent.DistanceToTarget.ToString("F2") + " action=" + a.CurrentAction + " pos=" + a.transform.position)));
        Check(horde.Where(a => a.SpecialAttack == null).Sum(a => a.Attack.Releases) > 50 && arena.Coordinator.ActiveMeleeReservations == 0,
            "The stationary larger horde maintains ordinary attack pressure without a shared attack budget");
        // A separate, repeatable moving-target leg exposes pursuit/repositioning
        // without claiming that stationary crowd participation has been solved.
        for (int frame = 0; frame < 900; frame++)
        {
            float phase = frame / 60f * .6f;
            MovePlayer(new Vector3(Mathf.Sin(phase) * 6, 1.6f, (Mathf.Cos(phase) - 1) * 6));
            yield return Frames(1);
            caps &= arena.SpecialCoordinator.ActiveSpinnerAttacks <= 1 && arena.SpecialCoordinator.ActiveOrbitAttacks <= 1;
        }
        Debug.Log("LARGE MOVING ROLE RELEASES: " + string.Join("; ", horde.Where(a => a.SpecialAttack == null).GroupBy(a => a.Role)
            .Select(group => group.Key + "=" + group.Sum(a => a.Attack.Releases))));
        Check(caps && horde.All(a => a.IsReady && a.Graph.Graph.IsRunning)
            && horde.Where(a => a.SpecialAttack == null).GroupBy(a => a.Role).All(group => group.Sum(a => a.Attack.Releases) > 0),
            "Every ordinary weapon role contributes during the repeatable moving-player encounter");
        arena.ClearEnemies(); yield return Frames(4);
        Check(EnemyAgent.All.Count == 0 && arena.SpecialCoordinator.PendingAttacks == 0 && arena.Coordinator.ActiveMeleeReservations == 0,
            "Large-encounter reset clears actors and coordination state");
    }
}
