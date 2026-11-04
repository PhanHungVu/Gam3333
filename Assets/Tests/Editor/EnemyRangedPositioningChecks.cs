using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public static partial class EnemyBehaviorChecks
{
    public static void RunRangedBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene(EnemyBehaviorArenaSetup.ScenePath);
        foreach (string mode in new[] { ".Arena", ".Rope", ".Coordination", ".AttackCoordination", ".Pressure" }) SessionState.SetBool(Pending + mode, false);
        SessionState.SetBool(Pending + ".Ranged", true); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static IEnumerator PressArenaKey(Keyboard keyboard, Key key, int heldFrames = 2)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); yield return Frames(heldFrames);
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); yield return Frames(2);
    }
    static EnemyGraphController[] ArenaBrains() => EnemyAgent.All.Select(a => a.GetComponent<EnemyGraphController>()).ToArray();
    static IEnumerator RunRanged()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.captureFramerate = 60;
        captures = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "EnemyRangedCaptures"); Directory.CreateDirectory(captures);
        yield return Frames(8);
        var arena = Object.FindAnyObjectByType<EnemyBehaviorPlaytest>();
        player = GameObject.FindWithTag("Player"); health = player.GetComponent<CombatHealth>(); health.SetMaximum(10000, true);
        player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        grid = Object.FindAnyObjectByType<NavGrid>(); field = Object.FindAnyObjectByType<FlowField>();
        arena.ClearEnemies(); yield return Frames(3);
        // Headless Editor has no focused Game view. Route this synthetic device
        // to the runtime explicitly; the working project's input settings are untouched.
        var previousBackground = InputSystem.settings.backgroundBehavior;
        var previousEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var keyboard = InputSystem.AddDevice<Keyboard>("Arena shortcut check");
        var fields = new SerializedObject(arena); fields.FindProperty("selectedRole").enumValueIndex = 7; fields.ApplyModifiedPropertiesWithoutUndo();
        Check(fields.FindProperty("enableKeyboardShortcuts").boolValue, "Saved arena enables keyboard shortcuts by default");
        yield return PressArenaKey(keyboard, Key.F5, 10);
        Check(ArenaBrains().Length == 1 && ArenaBrains()[0].Role == EnemyBehaviorRole.RockRope,
            "F5 spawns the selected orbit enemy once even when the key is held; count=" + ArenaBrains().Length
            + "; roles=" + string.Join(",", ArenaBrains().Select(a => a.Role)) + "; keyboard enabled=" + keyboard.enabled);
        yield return PressArenaKey(keyboard, Key.F9);
        Check(EnemyAgent.All.Count == 0, "F9 clears the current encounter without selecting an Inspector");
        yield return PressArenaKey(keyboard, Key.F6);
        Check(ArenaBrains().Length == 10 && ArenaBrains().All(a => a.Role == EnemyBehaviorRole.Sword)
            && arena.Coordinator.ContinuousMeleePressure && !arena.Coordinator.UseAttackPermissions,
            "F6 replaces the previous group with the continuous-pressure sword swarm");
        yield return PressArenaKey(keyboard, Key.F7);
        Check(ArenaBrains().Length == 5 && ArenaBrains().Count(a => a.Role == EnemyBehaviorRole.Crossbow) == 4
            && ArenaBrains().Count(a => a.Role == EnemyBehaviorRole.HeavyRanged) == 1,
            "F7 replaces the swarm with four crossbows and one heavy ranged enemy");
        var firstPositions = arena.SwarmSpawnPositions.ToArray();
        yield return PressArenaKey(keyboard, Key.F7);
        Check(firstPositions.SequenceEqual(arena.SwarmSpawnPositions), "The ranged shortcut reuses the same seed and spawn layout");
        yield return PressArenaKey(keyboard, Key.F8);
        Check(ArenaBrains().Length == 10 && ArenaBrains().Count(a => a.Role == EnemyBehaviorRole.Sword) == 6
            && ArenaBrains().Count(a => a.Role == EnemyBehaviorRole.Crossbow) == 3
            && ArenaBrains().Count(a => a.Role == EnemyBehaviorRole.HeavyRanged) == 1 && arena.Coordinator.ContinuousMeleePressure,
            "F8 starts six pressure swords, three crossbows and one heavy without an attack budget");
        arena.ClearEnemies(); yield return Frames(3);
        fields.Update(); fields.FindProperty("enableKeyboardShortcuts").boolValue = false; fields.ApplyModifiedPropertiesWithoutUndo();
        yield return PressArenaKey(keyboard, Key.F6);
        Check(EnemyAgent.All.Count == 0, "Arena keyboard shortcuts can be disabled independently of player input");
        fields.Update(); fields.FindProperty("enableKeyboardShortcuts").boolValue = true; fields.ApplyModifiedPropertiesWithoutUndo();
        InputSystem.RemoveDevice(keyboard);
        InputSystem.settings.backgroundBehavior = previousBackground;
        InputSystem.settings.editorInputBehaviorInPlayMode = previousEditorInput;
        Object.Destroy(GameObject.Find("Pathfinding obstacle")); yield return Frames(2); grid.Bake(); field.Rebuild();

        foreach (int role in new[] { 5, 6 })
        {
            ResetPlayer(); var actor = SpawnRole(role, role == 5 ? 6.8f : 14);
            float began = Time.time, hp = health.CurrentHealth; Vector3 start = actor.transform.position;
            yield return Until(() => actor.Attack.IsAttacking, 2, "Ready ranged attacker waited instead of aiming");
            Check(Time.time - began < .25f && actor.UsesRangedPositioning && !actor.UsesMeleeAttackCoordination,
                actor.Role + " immediately starts a ready shot without a firing-slot reservation");
            yield return Frames(12);
            Check(health.CurrentHealth == hp && actor.Attack.IsTelegraphing, actor.Role + " preserves its existing visible windup before release");
            yield return Until(() => actor.Attack.Releases >= 1, 3, "Ranged shot never released");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[role]).GetComponent<EnemyAttack>();
            Check(Mathf.Abs(actor.Attack.DamageOf(actor.Attack.Weapon) - prefab.DamageOf(prefab.Weapons[0])) < .001f
                && Mathf.Abs(actor.Attack.CooldownOf(actor.Attack.Weapon) - prefab.CooldownOf(prefab.Weapons[0])) < .001f,
                actor.Role + " retains authored weapon damage and cooldown");
            yield return Frames(120);
            Check(Steering.Flat(start - actor.transform.position).magnitude < .3f,
                actor.Role + " holds a useful clear firing position instead of constantly chasing");
            if (role == 6)
            {
                player.transform.position += Vector3.right; Physics.SyncTransforms(); yield return Frames(60);
                Check(Steering.Flat(start - actor.transform.position).magnitude < .3f,
                    "Heavy ranged stays planted when small player movements leave its position effective");
            }
            yield return Remove(actor); ClearShots(); yield return Frames(2);
        }

        ResetPlayer(); var crossbow = SpawnRole(5, 3); float initial = Steering.Flat(crossbow.transform.position - player.transform.position).magnitude;
        yield return Until(() => crossbow.CurrentAction == "Reposition", 2, "Close crossbow did not reposition");
        yield return Until(() => crossbow.Attack.IsAttacking, 6, "Retreating crossbow did not resume fire");
        Check(crossbow.Agent.DistanceToTarget >= 5 && crossbow.Agent.DistanceToTarget > initial + 1,
            "Crossbow retreats into its existing firing-distance band then attacks promptly");
        yield return Remove(crossbow); ClearShots(); yield return Frames(2);

        ResetPlayer(); var pair = new[] { SpawnRole(5, 7), SpawnRole(5, 7) };
        pair[1].transform.position += Vector3.right * 1.5f; Physics.SyncTransforms();
        bool spreading = false; float closest = float.PositiveInfinity;
        for (int i = 0; i < 360; i++)
        {
            yield return Frames(1);
            spreading |= pair.Any(a => a.CurrentAction == "Spread ranged firing positions");
            closest = Mathf.Min(closest, Steering.Flat(pair[0].transform.position - pair[1].transform.position).magnitude);
        }
        float separation = Steering.Flat(pair[0].transform.position - pair[1].transform.position).magnitude;
        Debug.Log($"RANGED SPACING: spread={spreading}, final={separation:F2}, closest={closest:F2}, releases={pair[0].Attack.Releases}/{pair[1].Attack.Releases}");
        Check(spreading && separation > 2.5f && pair.All(a => a.Attack.Releases > 0),
            "Nearby crossbows spread during cooldown while both continue firing");
        Check(closest >= pair[0].Agent.BodyRadius * 2 - .06f, "Ranged positioning keeps body collision and crowd clearance");
        foreach (var actor in pair) yield return Remove(actor);
        ClearShots(); yield return Frames(2);

        foreach (int role in new[] { 5, 6 })
        {
            ResetPlayer();
            var cover = GameObject.CreatePrimitive(PrimitiveType.Cube); cover.name = "Firing-position cover"; cover.tag = "Obstacle";
            cover.transform.position = new Vector3(0, 3, role == 5 ? 3.5f : 7);
            cover.transform.localScale = new Vector3(2, 6, .5f); Physics.SyncTransforms(); grid.Bake(); field.Rebuild();
            var actor = SpawnRole(role, role == 5 ? 7.5f : 14); yield return Frames(4);
            Check(!actor.Attack.HasClearRangedPathFrom(actor.transform.position) && actor.Attack.Releases == 0,
                actor.Role + " does not shoot through a blocked firing line");
            yield return Until(() => actor.Attack.Releases > 0, 12, actor.Role + " never found a firing angle around cover");
            Check(Mathf.Abs(actor.transform.position.x) > 1 && actor.Agent.DistanceToTarget >= (role == 5 ? 5 : 11),
                actor.Role + " finds a reachable lateral angle while keeping its preferred distance");
            Capture("ranged-cover-" + role, actor);
            yield return Remove(actor); Object.Destroy(cover); ClearShots(); yield return Frames(2); grid.Bake(); field.Rebuild();
        }

        ResetPlayer(); var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name = "Friendly firing-line blocker";
        blocker.transform.position = new Vector3(0, 1.5f, 3.5f); blocker.transform.localScale = new Vector3(1.5f, 3, 1);
        var blockerHealth = blocker.AddComponent<CombatHealth>(); blockerHealth.Team = CombatTeam.Enemy;
        crossbow = SpawnRole(5, 7.5f); Physics.SyncTransforms(); yield return Frames(3);
        Check(!crossbow.Attack.HasClearRangedPathFrom(crossbow.transform.position), "Another enemy body blocks the existing ranged firing-line check");
        yield return Until(() => crossbow.Attack.Releases > 0, 8, "Body-blocked crossbow never sidestepped to fire");
        Check(Mathf.Abs(crossbow.transform.position.x) > .75f && blockerHealth.CurrentHealth == blockerHealth.MaximumHealth,
            "Crossbow sidesteps a friendly body without firing into it or damaging its own team");
        yield return Remove(crossbow); Object.Destroy(blocker); ClearShots(); yield return Frames(2);

        ResetPlayer(); arena.StartMixedEncounter(); yield return Frames(3);
        var mixed = ArenaBrains(); foreach (var actor in mixed) { var drop = actor.GetComponent<EnemyPartDrop>(); if (drop != null) drop.enabled = false; }
        yield return Frames(1200);
        Debug.Log("RANGED MIXED: " + string.Join("; ", mixed.Select(a => a.Role + " releases=" + a.Attack.Releases + " distance=" + a.Agent.DistanceToTarget.ToString("F2") + " action=" + a.CurrentAction)));
        Check(mixed.All(a => a.IsReady && a.Graph.Graph.IsRunning) && mixed.Where(a => a.UsesRangedPositioning).All(a => a.Attack.Releases > 0),
            "All ranged roles keep firing in the twenty-second melee/ranged encounter");
        Check(mixed.Where(a => a.Role == EnemyBehaviorRole.Sword).Sum(a => a.Attack.Releases) > 12
            && arena.Coordinator.ActiveMeleeReservations == 0 && arena.Coordinator.PendingMeleeRequests == 0,
            "M3 preserves active melee pressure with no shared attack cap");
        player.SetActive(false); yield return Frames(12);
        Check(mixed.All(a => !a.HasRangedPosition && !a.Attack.IsAttacking), "Target loss clears ranged goals and pending attacks");
        arena.ClearEnemies(); yield return Frames(3);
        Check(EnemyAgent.All.Count == 0, "Clearing the mixed encounter removes ranged actors and their positioning state");
    }
}
