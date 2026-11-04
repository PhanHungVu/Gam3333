using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

public static partial class EnemyBehaviorChecks
{
    public static void RunRefinementBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene(EnemyBehaviorArenaSetup.ScenePath);
        foreach (string mode in new[] { ".Arena", ".Rope", ".Coordination", ".AttackCoordination", ".Pressure", ".Ranged", ".Special", ".Encounters" }) SessionState.SetBool(Pending + mode, false);
        SessionState.SetBool(Pending + ".Refinement", true); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    sealed class CombatSample
    {
        public int Releases, Participants, Axes, Peak, Sectors;
        public float ReadyWait, MinimumSpacing = float.PositiveInfinity;
        public float MinimumCameraDistance = float.PositiveInfinity;
        public bool Cooldowns = true, Valid = true, Unlimited = true;
        public double MeanPlan;
    }
    static IEnumerator MeasureLargeCombat(EnemyBehaviorPlaytest arena, bool compact, CombatSample sample)
    {
        ResetPlayer(); UnityEngine.Random.InitState(67421); arena.Coordinator.CompactPressureApproach = compact;
        arena.StartPreset(arena.EncounterPresets[4]); yield return Frames(3);
        var actors = ArenaBrains(); var melee = actors.Where(a => a.Role <= EnemyBehaviorRole.ThrowingAxe).ToArray();
        var view = Camera.main;
        var attacking = new bool[melee.Length]; var last = Enumerable.Repeat(float.NegativeInfinity, melee.Length).ToArray();
        var ready = new float[melee.Length]; var sectors = new HashSet<int>();
        for (int frame = 0; frame < 1500; frame++)
        {
            yield return Frames(1); sample.Peak = Mathf.Max(sample.Peak, melee.Count(a => a.Attack.IsAttacking));
            if (frame >= 120 && view != null)
                sample.MinimumCameraDistance = Mathf.Min(sample.MinimumCameraDistance, Vector3.Distance(view.transform.position, player.transform.position));
            sample.Valid &= actors.All(a => a.IsReady && a.Graph.Graph.IsRunning && float.IsFinite(a.transform.position.x) && float.IsFinite(a.transform.position.z));
            sample.Unlimited &= arena.Coordinator.ActiveMeleeReservations == 0 && arena.Coordinator.PendingMeleeRequests == 0;
            for (int i = 0; i < melee.Length; i++)
            {
                var actor = melee[i]; bool active = actor.Attack.IsAttacking;
                if (active && !attacking[i])
                {
                    sample.Cooldowns &= Time.time - last[i] + .025f >= actor.Attack.CooldownOf(actor.Attack.Weapon);
                    last[i] = Time.time;
                    float angle = Mathf.Atan2(actor.transform.position.x - player.transform.position.x, actor.transform.position.z - player.transform.position.z) * Mathf.Rad2Deg;
                    sectors.Add(Mathf.FloorToInt(Mathf.Repeat(angle, 360) / 90));
                }
                attacking[i] = active;
                bool canAttack = !active && actor.CooldownRemaining <= 0 && actor.TargetInReach && actor.Agent.DistanceToTarget <= actor.Agent.AttackRange + .25f;
                ready[i] = canAttack ? ready[i] + Time.deltaTime : 0; sample.ReadyWait = Mathf.Max(sample.ReadyWait, ready[i]);
                for (int j = i + 1; j < melee.Length; j++) sample.MinimumSpacing = Mathf.Min(sample.MinimumSpacing, Steering.Flat(actor.transform.position - melee[j].transform.position).magnitude);
            }
        }
        sample.Releases = melee.Sum(a => a.Attack.Releases); sample.Participants = melee.Count(a => a.Attack.Releases > 0);
        sample.Axes = melee.Where(a => a.Role == EnemyBehaviorRole.ThrowingAxe).Sum(a => a.Attack.Releases); sample.Sectors = sectors.Count;
        sample.MeanPlan = arena.Coordinator.PressurePlanningMilliseconds / Math.Max(1, arena.Coordinator.PressurePlans);
        Debug.Log($"M6 COMBAT: compact={compact}, releases={sample.Releases}, participants={sample.Participants}/18, axes={sample.Axes}, peak={sample.Peak}, ready wait={sample.ReadyWait:F3}, spacing={sample.MinimumSpacing:F3}, plan mean ms={sample.MeanPlan:F3}; "
            + string.Join("; ", melee.GroupBy(a => a.Role).Select(g => g.Key + "=" + g.Sum(a => a.Attack.Releases))));
        Debug.Log("M6 BLOCKED: " + string.Join("; ", melee.Where(a => a.Attack.Releases == 0).Select(a => a.Role + " distance=" + a.Agent.DistanceToTarget.ToString("F2") + " action=" + a.CurrentAction + " pos=" + a.transform.position)) + "; player=" + player.transform.position);
        arena.CaptureCombatReview();
        var follow = Object.FindAnyObjectByType<CinemachineThirdPersonFollow>();
        var obstacle = follow != null ? follow.CurrentObstacle : null;
        Debug.Log("M6 CAMERA: minimum distance=" + sample.MinimumCameraDistance.ToString("F2")
            + "; obstacle=" + (obstacle != null ? obstacle.name + " layer=" + obstacle.gameObject.layer + " tag=" + obstacle.tag
                + " owner=" + obstacle.transform.root.name + " health=" + obstacle.GetComponentInParent<CombatHealth>() : "none"));
        if (compact) Capture("m6-horde", actors.First(a => a.Role == EnemyBehaviorRole.Hammer));
    }
    static IEnumerator RunCombatRefinement()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.captureFramerate = 60;
        captures = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "EnemyM6Captures"); Directory.CreateDirectory(captures);
        yield return Frames(8); var arena = Object.FindAnyObjectByType<EnemyBehaviorPlaytest>();
        player = GameObject.FindWithTag("Player"); health = player.GetComponent<CombatHealth>(); health.SetMaximum(1000000, true);
        player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        grid = Object.FindAnyObjectByType<NavGrid>(); field = Object.FindAnyObjectByType<FlowField>();
        arena.ClearEnemies(); yield return Frames(3);
        var background = InputSystem.settings.backgroundBehavior; var editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var keyboard = InputSystem.AddDevice<Keyboard>("Combat review shortcuts");
        bool wasCompact = arena.Coordinator.CompactPressureApproach; yield return PressArenaKey(keyboard, Key.Home);
        Check(arena.Coordinator.CompactPressureApproach != wasCompact && !arena.Coordinator.UseAttackPermissions,
            "Home compares melee movement without enabling a shared attack budget");
        yield return PressArenaKey(keyboard, Key.End);
        Check(arena.CombatReviewReport.Contains("Combat snapshot") && arena.CombatReviewReport.Contains("ordinary attack reservations=0"),
            "End records a combat/CPU snapshot without a player HUD");
        InputSystem.RemoveDevice(keyboard); InputSystem.settings.backgroundBehavior = background; InputSystem.settings.editorInputBehaviorInPlayMode = editorInput;
        var baseline = new CombatSample(); yield return MeasureLargeCombat(arena, false, baseline);
        var refined = new CombatSample(); yield return MeasureLargeCombat(arena, true, refined);
        Check(float.IsFinite(refined.MinimumCameraDistance) && refined.MinimumCameraDistance > 10,
            "The open-arena horde keeps the player camera outside the mech at its normal follow distance");
        Check(refined.Valid && refined.Unlimited && refined.Peak >= 3, "All 30 actors stay functional and ordinary melee overlaps independently");
        Check(refined.Axes > 0 && refined.Participants > baseline.Participants,
            "Stationary crowd refinement opens attack opportunities for axes and more individual melee enemies");
        Check(refined.Releases >= baseline.Releases * .85f && refined.Cooldowns && refined.ReadyWait < .25f,
            "Refinement preserves continuous pressure, prompt ready attacks and authored cooldowns");
        Check(refined.MinimumSpacing >= 1.15f && refined.Sectors >= 3, "Attack approaches use several directions while retaining body clearance");
        Check(refined.MeanPlan < 1, "Bounded local planning stays below one millisecond per plan in this headless prototype");
        arena.ClearEnemies(); yield return Frames(3); ResetPlayer();

        // Rotate the target around a planted ranged actor while remaining in its
        // valid band. Previously Hold suppressed turning and it stopped firing.
        foreach (int role in new[] { 5, 6 })
        {
            var actor = SpawnRole(role, role == 5 ? 6.8f : 14);
            yield return Until(() => actor.Attack.IsAttacking, 2, "Ranged setup attack missing");
            yield return Frames(12); Capture("m6-warning-" + actor.Role, actor);
            yield return Until(() => !actor.Attack.IsAttacking, 3, "Ranged setup attack failed to recover");
            Vector3 position = actor.transform.position; int releases = actor.Attack.Releases;
            MovePlayer(position + Vector3.right * (role == 5 ? 6.8f : 14));
            yield return Frames(42);
            Check(Vector3.Angle(actor.transform.forward, Steering.Flat(player.transform.position - actor.transform.position)) < 15
                && Steering.Flat(actor.transform.position - position).magnitude < .3f,
                actor.Role + " turns toward a sideways target while holding its useful firing position");
            yield return Until(() => actor.Attack.Releases > releases, 7, "Planted ranged enemy failed to fire after target rotation");
            Check(actor.Attack.Releases > releases, actor.Role + " resumes its original shot cycle after turning");
            yield return Remove(actor); ClearShots(); yield return Frames(2); ResetPlayer();
        }
        subject = Spawn(new Vector3(0, 1, 2)); var member = subject.gameObject.AddComponent<EnemyCoordinationMember>(); member.Bind(arena.Coordinator);
        yield return Until(() => subject.Attack.IsAttacking, .5f, "Ready melee target did not trigger promptly");
        Quaternion committed = subject.transform.rotation; float hp = health.CurrentHealth;
        MovePlayer(Vector3.right * 8); yield return Frames(12);
        Check(Quaternion.Angle(committed, subject.transform.rotation) < 1 && health.CurrentHealth == hp,
            "Committed melee windup keeps its facing and can be escaped with existing movement");
        yield return Frames(45); Check(health.CurrentHealth == hp, "Escaping the snapshotted swing avoids its existing melee release");
        subject.GetComponent<CombatHealth>().ApplyDamage(10000); yield return Frames(3);
        arena.ClearEnemies(); yield return Frames(3);
        Check(arena.Coordinator.RegisteredCount == 0 && arena.Coordinator.PressurePlans == 0 && arena.Coordinator.ActiveMeleeReservations == 0,
            "Death/reset clears movement participation and review counters");

        ResetPlayer(); var axe = SpawnRole(3, 3.1f); float standingAxeHealth = health.CurrentHealth;
        yield return Until(() => axe.Attack.IsAttacking, 1, "Axe did not use its revised engagement distance");
        Check(axe.Agent.AttackRange == 3.2f && axe.Attack.ReachOf(axe.Attack.Weapon) >= axe.Agent.AttackRange,
            "Axe engagement uses its existing weapon reach without changing the shared hitbox or part data");
        yield return Until(() => axe.Attack.Releases > 0, 2, "Axe failed to release at its revised engagement distance");
        Check(health.CurrentHealth < standingAxeHealth, "A stationary target at the revised axe distance takes the existing melee hit");
        yield return Remove(axe); ResetPlayer(); axe = SpawnRole(3, 3.1f);
        yield return Until(() => axe.Attack.IsAttacking, 1, "Axe dodge setup did not begin its windup");
        float beforeAxe = health.CurrentHealth; MovePlayer(Vector3.left * 8);
        yield return Until(() => axe.Attack.Releases > 0, 2, "Axe never released its committed swing");
        Check(health.CurrentHealth == beforeAxe, "The longer axe engagement still permits escaping its committed overhead windup");
        yield return Remove(axe);
    }
}
