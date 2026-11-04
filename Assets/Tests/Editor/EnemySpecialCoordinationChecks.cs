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

public static partial class EnemyBehaviorChecks
{
    public static void RunSpecialCoordinationBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene(EnemyBehaviorArenaSetup.ScenePath);
        foreach (string mode in new[] { ".Arena", ".Rope", ".Coordination", ".AttackCoordination", ".Pressure", ".Ranged" }) SessionState.SetBool(Pending + mode, false);
        SessionState.SetBool(Pending + ".Special", true); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static EnemyGraphController SpecialAt(EnemyBehaviorPlaytest arena, int role, Vector3 position)
    {
        var actor = SpawnRole(role, 8); position.y = actor.Agent.PivotHeight; actor.transform.position = position;
        actor.transform.rotation = Quaternion.LookRotation(Steering.Flat(player.transform.position - position));
        actor.BindSpecialCoordinator(arena.SpecialCoordinator); Physics.SyncTransforms(); return actor;
    }
    static IEnumerator RunSpecialCoordination()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.captureFramerate = 60;
        UnityEngine.Random.InitState(64821);
        captures = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "EnemySpecialCaptures"); Directory.CreateDirectory(captures);
        yield return Frames(8);
        var arena = Object.FindAnyObjectByType<EnemyBehaviorPlaytest>(); var special = arena.SpecialCoordinator;
        player = GameObject.FindWithTag("Player"); health = player.GetComponent<CombatHealth>(); health.SetMaximum(10000, true);
        player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        grid = Object.FindAnyObjectByType<NavGrid>(); field = Object.FindAnyObjectByType<FlowField>();
        arena.ClearEnemies(); yield return Frames(3);
        var background = InputSystem.settings.backgroundBehavior; var editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var keyboard = InputSystem.AddDevice<Keyboard>("Special encounter shortcuts");
        yield return PressArenaKey(keyboard, Key.F10);
        var encounter = ArenaBrains();
        Check(encounter.Length == 13 && encounter.Count(a => a.Role == EnemyBehaviorRole.Spinner) == 3
            && encounter.Count(a => a.Role == EnemyBehaviorRole.RockRope) == 1 && encounter.Count(a => a.Role == EnemyBehaviorRole.RockRopePull) == 1,
            "F10 spawns the repeatable three-spinner, two-rope and pressure-horde encounter");
        Check(encounter.Count(a => a.UsesSpecialCoordination) == 5
            && encounter.Where(a => a.Role <= EnemyBehaviorRole.ThrowingAxe || a.UsesRangedPositioning).All(a => !a.UsesSpecialCoordination),
            "Ordinary melee and ranged never join special attack admission");
        var positions = arena.SwarmSpawnPositions.ToArray(); yield return PressArenaKey(keyboard, Key.F10);
        Check(positions.SequenceEqual(arena.SwarmSpawnPositions), "F10 repeats the saved seed and spawn layout");
        yield return PressArenaKey(keyboard, Key.F9);
        Check(EnemyAgent.All.Count == 0 && special.PendingAttacks == 0 && special.ActiveSpinnerAttacks == 0 && special.ActiveOrbitAttacks == 0,
            "F9 removes special actors, active commitments and pending requests");
        InputSystem.RemoveDevice(keyboard); InputSystem.settings.backgroundBehavior = background; InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
        Object.Destroy(GameObject.Find("Pathfinding obstacle")); yield return Frames(2); grid.Bake(); field.Rebuild();

        ResetPlayer(); var cover = GameObject.CreatePrimitive(PrimitiveType.Cube); cover.name = "Blocked charge lane"; cover.tag = "Obstacle";
        cover.transform.position = new Vector3(0, 3, 4); cover.transform.localScale = new Vector3(4, 6, .5f);
        Physics.SyncTransforms(); grid.Bake(); field.Rebuild();
        var spinner = SpecialAt(arena, 4, Vector3.forward * 8); var charge = spinner.GetComponent<EnemySpinnerAttack>();
        yield return Frames(8);
        Check(!charge.CanChargeFrom(spinner.transform.position) && charge.CurrentStage != EnemyGraphAttack.Stage.Windup && charge.CooldownRemaining == 0,
            "A blocked spinner lane does not start a windup or spend cooldown");
        yield return Until(() => charge.CurrentStage == EnemyGraphAttack.Stage.Windup, 12, "Spinner never found a clear lane around cover");
        Check(Mathf.Abs(spinner.transform.position.x) > 2 && charge.CanChargeFrom(spinner.transform.position),
            "Spinner moves to a usable lateral charge lane before telegraphing");
        Vector3 committed = charge.ChargeDirection; float hp = health.CurrentHealth;
        MovePlayer(player.transform.position + Vector3.left * 12);
        yield return Until(() => charge.CurrentStage == EnemyGraphAttack.Stage.Recovery, 4, "Committed lane never recovered");
        Check(charge.ChargeDirection == committed && charge.Contacts == 0 && health.CurrentHealth == hp,
            "The chosen spinner lane stays committed and remains dodgeable after windup begins");
        yield return Remove(spinner); Object.Destroy(cover); yield return Frames(2); grid.Bake(); field.Rebuild();

        ResetPlayer(); spinner = SpecialAt(arena, 4, Vector3.forward * 8);
        var overlap = GameObject.CreatePrimitive(PrimitiveType.Cube); overlap.transform.position = spinner.transform.position + Vector3.up * .7f;
        overlap.transform.localScale = Vector3.one * 3; Physics.SyncTransforms();
        Check(!spinner.GetComponent<EnemySpinnerAttack>().CanChargeFrom(spinner.transform.position),
            "A spinner already overlapping scenery cannot begin a charge through it");
        yield return Remove(spinner); Object.Destroy(overlap); yield return Frames(2);

        ResetPlayer(); MovePlayer(new Vector3(210, 1.6f, 0));
        var launch = GameObject.CreatePrimitive(PrimitiveType.Cube); launch.transform.position = new Vector3(220, -.25f, 0); launch.transform.localScale = new Vector3(6, .5f, 6);
        var landing = GameObject.CreatePrimitive(PrimitiveType.Cube); landing.transform.position = new Vector3(210, -.25f, 0); landing.transform.localScale = new Vector3(3, .5f, 6);
        spinner = SpecialAt(arena, 4, new Vector3(218, 1, 0)); yield return Frames(2);
        Check(!spinner.GetComponent<EnemySpinnerAttack>().CanChargeFrom(spinner.transform.position)
            && spinner.CooldownRemaining == 0, "Spinner refuses a straight lane with missing ground between platforms");
        yield return Remove(spinner); Object.Destroy(launch); Object.Destroy(landing); yield return Frames(2);

        ResetPlayer(); var pack = new EnemyGraphController[6];
        for (int i = 0; i < pack.Length; i++)
        {
            pack[i] = SpawnRole(0, 2.1f); pack[i].Graph.enabled = false;
            pack[i].transform.position = new Vector3((i - 2.5f) * .3f, 1, 2.1f);
        }
        spinner = SpecialAt(arena, 4, Vector3.forward * 8); yield return Frames(2);
        Check(special.TryPosition(spinner, out var flank) && Vector3.Angle(Steering.Flat(flank), Vector3.forward) >= 20,
            "Special approach suggestions favour an angle away from the close melee pack");
        foreach (var actor in pack) yield return Remove(actor); yield return Remove(spinner);

        ResetPlayer(); var group = new EnemyGraphController[3];
        for (int i = 0; i < group.Length; i++) group[i] = SpecialAt(arena, 4, Quaternion.Euler(0, i * 120, 0) * Vector3.forward * 8);
        var basic = new EnemyGraphController[6];
        for (int i = 0; i < basic.Length; i++)
        {
            basic[i] = SpawnRole(0, 3.2f); basic[i].transform.position = Quaternion.Euler(0, i * 60, 0) * Vector3.forward * 3.2f + Vector3.up;
            basic[i].transform.rotation = Quaternion.LookRotation(-Steering.Flat(basic[i].transform.position));
            basic[i].gameObject.AddComponent<EnemyCoordinationMember>().Bind(arena.Coordinator);
        }
        Physics.SyncTransforms(); int[] starts = new int[3]; bool[] winding = new bool[3]; float lastStart = float.NegativeInfinity;
        bool capped = true, spaced = true, movedWhilePending = false; int peakMelee = 0;
        for (int frame = 0; frame < 1200; frame++)
        {
            yield return Frames(1); capped &= special.ActiveSpinnerAttacks <= special.MaximumSpinnerAttacks;
            peakMelee = Mathf.Max(peakMelee, basic.Count(a => a.Attack.IsAttacking));
            for (int i = 0; i < group.Length; i++)
            {
                bool windup = group[i].SpecialAttack.CurrentStage == EnemyGraphAttack.Stage.Windup;
                if (windup && !winding[i]) { starts[i]++; spaced &= Time.time - lastStart >= special.StartSpacing - .034f; lastStart = Time.time; }
                winding[i] = windup;
                movedWhilePending |= !group[i].SpecialAttack.IsExecuting && group[i].CooldownRemaining <= 0 && group[i].Agent.Velocity.magnitude > .3f;
            }
        }
        Debug.Log($"SPECIAL SPINNER METRICS: starts={string.Join(",", starts)}, capped={capped}, spacing={spaced}, pending movement={movedWhilePending}, peak melee={peakMelee}");
        Check(capped && spaced && starts.All(n => n > 0), "Spinner windups/charges stay within the local limit and all three attackers get turns");
        Check(movedWhilePending, "Spinners reposition while another charge is active instead of standing in a waiting ring");
        Check(peakMelee >= 3 && basic.Sum(a => a.Attack.Releases) > 12 && arena.Coordinator.ActiveMeleeReservations == 0,
            "Several ordinary melee attacks overlap freely while special charges are coordinated");
        foreach (var actor in group.Concat(basic)) yield return Remove(actor);

        ResetPlayer(); var orbits = new[] { SpecialAt(arena, 7, Vector3.right * 6.4f), SpecialAt(arena, 7, Vector3.left * 6.4f) };
        var pull = SpecialAt(arena, 8, Vector3.forward * 16); bool orbitCap = true;
        for (int frame = 0; frame < 1200; frame++) { yield return Frames(1); orbitCap &= special.ActiveOrbitAttacks <= special.MaximumOrbitAttacks; }
        Debug.Log("SPECIAL ROPE METRICS: " + string.Join("; ", orbits.Append(pull).Select(a => a.Role + " stage=" + a.SpecialAttack.CurrentStage
            + " pos=" + a.transform.position + " orbits=" + a.GetComponent<EnemyRopeAttack>().OrbitsStarted + " impacts=" + a.GetComponent<EnemyRopeAttack>().Impacts)));
        Check(orbitCap && orbits.All(a => a.GetComponent<EnemyRopeAttack>().OrbitsStarted > 0), "Nearby orbit elites alternate their existing area-control attacks without stacking active orbits");
        Check(pull.GetComponent<EnemyRopeAttack>().Impacts > 0 && pull.GetComponent<EnemyRopeAttack>().Behavior == EnemyRopeAttack.RopeBehavior.ThrowAndPull,
            "The preserved throw/pull attack still operates alongside the orbit elite");
        Check(orbits.All(a => a.GetComponent<EnemyRopeAttack>().Damage == 35) && health.CurrentHealth < health.MaximumHealth,
            "Coordination preserves rope damage and the filled spinning circle");
        foreach (var actor in orbits.Append(pull)) yield return Remove(actor);

        ResetPlayer(); spinner = SpecialAt(arena, 4, Vector3.forward * 8);
        yield return Until(() => spinner.SpecialAttack.CurrentStage == EnemyGraphAttack.Stage.Windup, 3, "Cancellation windup missing");
        spinner.Graph.enabled = false; yield return Frames(3);
        Check(special.ActiveSpinnerAttacks == 0 && special.PendingAttacks == 0 && spinner.SpecialAttack.CurrentStage == EnemyGraphAttack.Stage.Idle,
            "Disabling a special's graph cancels its attack and frees admission immediately");
        yield return Remove(spinner);
        ResetPlayer(); var lifeCyclePair = new[] { SpecialAt(arena, 4, Vector3.forward * 8), SpecialAt(arena, 4, Vector3.back * 8) };
        yield return Until(() => special.ActiveSpinnerAttacks == 1 && special.PendingAttacks > 0, 4, "Death cleanup fixture never queued");
        var dying = lifeCyclePair.First(actor => actor.SpecialAttack.IsExecuting);
        var survivor = lifeCyclePair.First(actor => actor != dying);
        dying.GetComponent<CombatHealth>().ApplyDamage(10000);
        yield return Until(() => survivor.SpecialAttack.CurrentStage == EnemyGraphAttack.Stage.Windup, 2, "Death did not free the pending spinner");
        Check(!dying.gameObject.activeSelf && special.ActiveSpinnerAttacks == 1,
            "Death frees the special slot for a ready attacker without duplicate commitments");
        survivor.gameObject.SetActive(false); yield return Frames(2);
        Check(special.ActiveSpinnerAttacks == 0 && special.PendingAttacks == 0, "Pooling a special releases its active and pending state");
        survivor.gameObject.SetActive(true);
        yield return Until(() => survivor.SpecialAttack.CurrentStage == EnemyGraphAttack.Stage.Windup, 6, "Re-enabled spinner failed to rejoin coordination");
        Check(survivor.UsesSpecialCoordination && special.ActiveSpinnerAttacks == 1 && survivor.Graph.Graph.IsRunning,
            "Re-enabled specials re-register once and start a fresh graph attack after their cooldown");
        foreach (var actor in lifeCyclePair) yield return Remove(actor);
        ResetPlayer(); var orbit = SpecialAt(arena, 7, Vector3.forward * 6.4f);
        yield return Until(() => orbit.SpecialAttack.CurrentStage == EnemyGraphAttack.Stage.Orbit, 8, "Target-loss orbit missing");
        hp = health.CurrentHealth; player.SetActive(false); yield return Frames(8);
        Check(orbit.SpecialAttack.CurrentStage == EnemyGraphAttack.Stage.Idle && special.ActiveOrbitAttacks == 0
            && special.PendingAttacks == 0 && health.CurrentHealth == hp, "Target loss cancels active orbit damage and clears coordination");
        player.SetActive(true); yield return Remove(orbit);

        ResetPlayer(); var secondTarget = GameObject.CreatePrimitive(PrimitiveType.Cube); secondTarget.transform.position = new Vector3(16, 1.6f, 0);
        var secondHealth = secondTarget.AddComponent<CombatHealth>(); secondHealth.Team = CombatTeam.Player; secondHealth.SetMaximum(10000, true);
        var firstTargetActor = SpecialAt(arena, 4, Vector3.forward * 8); var secondTargetActor = SpecialAt(arena, 4, new Vector3(16, 1, 8));
        var targetFields = new SerializedObject(secondTargetActor.Agent); targetFields.FindProperty("target").objectReferenceValue = secondTarget.transform; targetFields.ApplyModifiedPropertiesWithoutUndo();
        secondTargetActor.transform.rotation = Quaternion.LookRotation(Vector3.back); Physics.SyncTransforms();
        yield return Until(() => firstTargetActor.SpecialAttack.CurrentStage == EnemyGraphAttack.Stage.Windup && secondTargetActor.SpecialAttack.CurrentStage == EnemyGraphAttack.Stage.Windup,
            2, "Independent target specials incorrectly blocked one another");
        Check(special.ActiveSpinnerAttacks == 2, "Separate targets have independent special capacity and start spacing");
        yield return Remove(firstTargetActor); yield return Remove(secondTargetActor); Object.Destroy(secondTarget); yield return Frames(2);
        Check(special.PendingAttacks == 0 && special.ActiveSpinnerAttacks == 0 && special.ActiveOrbitAttacks == 0,
            "Removing all actors leaves no special coordination state behind");

        ResetPlayer(); arena.StartSpecialEncounter(); yield return Frames(3);
        encounter = ArenaBrains(); bool mixedCaps = true;
        foreach (var actor in encounter) { var drop = actor.GetComponent<EnemyPartDrop>(); if (drop != null) drop.enabled = false; }
        for (int frame = 0; frame < 1800; frame++)
        { yield return Frames(1); mixedCaps &= special.ActiveSpinnerAttacks <= 1 && special.ActiveOrbitAttacks <= 1; }
        Debug.Log("SPECIAL FULL ENCOUNTER: " + string.Join("; ", encounter.Select(actor => actor.Role + " action=" + actor.CurrentAction
            + " releases=" + actor.Attack.Releases + (actor.SpecialAttack is EnemySpinnerAttack spinning ? " contacts=" + spinning.Contacts : "")
            + (actor.SpecialAttack is EnemyRopeAttack rope ? " orbits=" + rope.OrbitsStarted + " impacts=" + rope.Impacts : ""))));
        Check(mixedCaps && encounter.All(actor => actor.IsReady && actor.Graph.Graph.IsRunning)
            && encounter.Where(actor => actor.Role == EnemyBehaviorRole.Spinner).All(actor => actor.GetComponent<EnemySpinnerAttack>().Contacts > 0),
            "The thirty-second F10 horde keeps all graphs running and all three spinners attacking within their limits");
        Check(encounter.Single(actor => actor.Role == EnemyBehaviorRole.RockRope).GetComponent<EnemyRopeAttack>().OrbitsStarted > 0
            && encounter.Single(actor => actor.Role == EnemyBehaviorRole.RockRopePull).GetComponent<EnemyRopeAttack>().Impacts > 0,
            "Both rope roles contribute their existing attacks in the full horde");
        Check(encounter.Where(actor => actor.Role <= EnemyBehaviorRole.ThrowingAxe || actor.UsesRangedPositioning).All(actor => actor.Attack.Releases > 0)
            && arena.Coordinator.ActiveMeleeReservations == 0,
            "Ordinary melee and the slower crossbows continue attacking independently in the special encounter");
        arena.ClearEnemies(); yield return Frames(3);
    }
}
