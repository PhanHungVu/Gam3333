using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class EnemyBehaviorChecks
{
    public static void RunAttackCoordinationBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene(EnemyBehaviorArenaSetup.ScenePath);
        SessionState.SetBool(Pending + ".Arena", false); SessionState.SetBool(Pending + ".Rope", false);
        SessionState.SetBool(Pending + ".Coordination", false); SessionState.SetBool(Pending + ".AttackCoordination", true);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    sealed class MeleeSample
    {
        public int PeakAttacks, PeakReservations, PeakQueued;
        public bool WithinLimit = true, AllReserved = true, CooldownsRespected = true, WaitersMove;
        public readonly HashSet<EnemyCoordinationMember> Attackers = new HashSet<EnemyCoordinationMember>();
        public readonly HashSet<int> Directions = new HashSet<int>();
    }
    static IEnumerator SampleMelee(EnemyBehaviorPlaytest arena, int capacity, int frames, MeleeSample sample)
    {
        arena.ClearEnemies(); arena.Coordinator.MaximumMeleeAttacks = capacity; arena.StartCoordinatedSwarm(); yield return Frames(4);
        var members = SwarmMembers(); var wasAttacking = new bool[members.Length];
        var lastStart = Enumerable.Repeat(float.NegativeInfinity, members.Length).ToArray(); var releases = new int[members.Length];
        foreach (var member in members) { var drops = member.GetComponent<EnemyPartDrop>(); if (drops != null) drops.enabled = false; }
        for (int frame = 0; frame < frames; frame++)
        {
            int attacking = 0;
            for (int i = 0; i < members.Length; i++)
            {
                var member = members[i]; var attack = member.Controller.Attack; bool active = attack.IsAttacking;
                if (active)
                {
                    attacking++; sample.AllReserved &= member.HasCommittedAttack;
                    if (!wasAttacking[i])
                    {
                        float cooldown = attack.CooldownOf(attack.Weapon);
                        sample.CooldownsRespected &= Time.time - lastStart[i] + 0.025f >= cooldown;
                        lastStart[i] = Time.time;
                    }
                }
                wasAttacking[i] = active;
                if (attack.Releases > releases[i])
                {
                    releases[i] = attack.Releases; sample.Attackers.Add(member);
                    Vector3 offset = member.transform.position - player.transform.position;
                    sample.Directions.Add(Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg, 360) / 90));
                }
                if (!active && !member.HasAttackPermission && member.AttackPermissionStatus.Contains("Waiting") && member.Agent.Velocity.sqrMagnitude > 0.01f)
                    sample.WaitersMove = true;
            }
            sample.PeakAttacks = Mathf.Max(sample.PeakAttacks, attacking);
            sample.PeakReservations = Mathf.Max(sample.PeakReservations, arena.Coordinator.ActiveMeleeReservations);
            sample.PeakQueued = Mathf.Max(sample.PeakQueued, arena.Coordinator.PendingMeleeRequests);
            sample.WithinLimit &= attacking <= capacity && arena.Coordinator.ActiveMeleeReservations <= capacity;
            yield return null;
        }
        Debug.Log($"M2 MELEE METRICS: limit={capacity}, peak attacks={sample.PeakAttacks}, peak reservations={sample.PeakReservations}, queued={sample.PeakQueued}, attackers={sample.Attackers.Count}, directions={sample.Directions.Count}, moving waiters={sample.WaitersMove}");
    }
    static void CheckMeleePoolClock(EnemyCoordinationMember[] members)
    {
        var a = members[0]; var b = members[1]; var c = members[2]; var target = player.transform;
        var pool = new EnemyMeleeAttackPool(); int cancelled = 0;
        Action<EnemyCoordinationMember> cancel = _ => cancelled++;
        Func<EnemyCoordinationMember, Transform, bool> valid = (_, __) => true;
        Func<EnemyCoordinationMember, bool> executing = _ => true;
        pool.Request(a, target, 0, true); pool.Request(b, target, 0, true); pool.Request(c, target, 0, true);
        pool.Tick(0, 2, 0, 0.5f, valid, executing, cancel);
        Check(pool.HasPermission(a) && pool.HasPermission(b) && !pool.HasPermission(c), "Oldest ready requests receive the two available permissions");
        pool.Request(a, target, 0.1f, true);
        Check(pool.ActiveCount == 2 && pool.PendingCount == 1, "Repeated requests do not allocate duplicate reservations");
        pool.Commit(a, 0, 4); pool.Commit(b, 0, 4); pool.Release(a, false, cancel);
        pool.Request(a, target, 0.1f, true); pool.Request(c, target, 0.1f, true);
        pool.Tick(0.1f, 2, 0, 0.5f, valid, executing, cancel);
        Check(pool.HasPermission(c) && !pool.HasPermission(a), "A repeat attacker cannot jump ahead of an older waiting actor");
        pool.Clear(cancel);
        pool.Request(a, target, 0, false); pool.Request(b, target, 0, true);
        pool.Tick(0, 1, 0, 0.5f, valid, executing, cancel);
        Check(pool.HasPermission(b) && !pool.HasPermission(a), "An unready older actor does not block useful attacks");
        pool.Clear(cancel);
        var otherTarget = new GameObject("Second budget target");
        pool.Request(a, target, 0, true); pool.Request(b, target, 0, true); pool.Request(c, otherTarget.transform, 0, true);
        pool.Tick(0, 1, 0, 0.5f, valid, executing, cancel);
        Check(pool.HasPermission(a) && pool.HasPermission(c) && !pool.HasPermission(b), "Attack capacity belongs to each target, rather than the whole scene");
        pool.Clear(cancel); Object.Destroy(otherTarget);
        pool.Request(a, target, 0, true); pool.Request(b, target, 0, true); pool.Request(c, target, 0, true);
        pool.Tick(0, 3, 0.2f, 0.5f, valid, executing, cancel); Check(pool.ActiveCount == 1, "Start spacing prevents simultaneous grants even with free capacity");
        pool.Tick(0.19f, 3, 0.2f, 0.5f, valid, executing, cancel); Check(pool.ActiveCount == 1, "Start spacing lasts for the configured interval");
        pool.Tick(0.2f, 3, 0.2f, 0.5f, valid, executing, cancel); Check(pool.ActiveCount == 2, "Another attack may overlap after the short start separation");
        pool.Tick(0.4f, 3, 0.2f, 0.5f, valid, executing, cancel); Check(pool.ActiveCount == 3, "Three ready requests can use the configurable three-attack capacity");
        pool.Clear(cancel);
        pool.Request(a, target, 0, true); pool.Request(b, target, 0, true);
        pool.Tick(0, 1, 0, 0.2f, valid, executing, cancel); pool.Request(b, target, 0.25f, true);
        pool.Tick(0.25f, 1, 0, 0.2f, valid, executing, cancel);
        Check(pool.HasPermission(b) && !pool.HasPermission(a), "An unstarted grant expires and returns behind an existing waiter");
        pool.Tick(1, 1, 0, 0.2f, valid, executing, cancel);
        Check(pool.ActiveCount == 0 && pool.PendingCount == 0, "Abandoned requests cannot retain capacity indefinitely");
        cancelled = 0;
        pool.Request(a, target, 0, true); pool.Tick(0, 1, 0, 0.5f, valid, executing, cancel); pool.Commit(a, 0, 1);
        pool.Commit(a, 0.5f, 20); // repeated acknowledgement must not renew its watchdog
        pool.Request(b, target, 1.01f, true); pool.Tick(1.01f, 1, 0, 0.5f, valid, executing, cancel);
        Check(cancelled == 1 && pool.HasPermission(b) && !pool.HasPermission(a), "Duplicate commits cannot extend the watchdog; a stuck commitment admits another actor");
        pool.Commit(b, 1.01f, 1); pool.Tick(1.02f, 1, 0, 0.5f, valid, _ => false, cancel);
        Check(pool.ActiveCount == 0 && cancelled == 1, "Normal attack completion returns capacity without cancellation");
        pool.Request(a, target, 2, true); pool.Tick(2, 1, 0, 0.5f, valid, executing, cancel); pool.Commit(a, 2, 1);
        pool.Tick(2.01f, 1, 0, 0.5f, (_, __) => false, executing, cancel);
        Check(pool.ActiveCount == 0 && cancelled == 2, "Invalid actor or target cancels the committed reservation");
    }
    static IEnumerator AwaitMeleeCommit()
    {
        yield return Until(() => SwarmMembers().Any(m => m.HasCommittedAttack && m.Controller.Attack.IsTelegraphing), 10, "No fresh melee windup for cleanup check");
    }
    static EnemyCoordinationMember CurrentMeleeCommit() => SwarmMembers().First(m => m.HasCommittedAttack && m.Controller.Attack.IsTelegraphing);
    static IEnumerator RunAttackCoordination()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.captureFramerate = 60; QualitySettings.vSyncCount = 0;
        yield return Frames(8);
        var arena = Object.FindAnyObjectByType<EnemyBehaviorPlaytest>(); var coordinator = arena.Coordinator;
        coordinator.MeleeStyle = EnemyMeleeStyle.ReservedPositions; coordinator.UseAttackPermissions = true;
        player = GameObject.FindWithTag("Player"); health = player.GetComponent<CombatHealth>(); health.SetMaximum(100000, true);
        player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        grid = Object.FindAnyObjectByType<NavGrid>(); field = FlowField.Instance;
        Check(coordinator.UseAttackPermissions && coordinator.MaximumMeleeAttacks == 2, "Reserved comparison retains its configurable basic-melee budget");
        var normal = new MeleeSample(); yield return SampleMelee(arena, 2, 1500, normal);
        Check(normal.WithinLimit && normal.AllReserved && normal.PeakAttacks == 2, "Actual graph-driven swings overlap within the two-attack limit and all hold reservations");
        Check(normal.PeakQueued > 0 && normal.WaitersMove, "Waiting enemies keep moving while requesting an attack opportunity");
        Check(normal.Attackers.Count >= 5 && normal.Directions.Count >= 3, "Several actors receive attacks from different sides rather than one actor monopolizing pressure");
        Check(normal.CooldownsRespected, "Shared permissions never shorten the weapons' existing cooldowns");
        CheckMeleePoolClock(SwarmMembers());
        foreach (int limit in new[] { 1, 3 })
        {
            var sample = new MeleeSample(); yield return SampleMelee(arena, limit, 840, sample);
            Check(sample.WithinLimit && sample.AllReserved && sample.PeakAttacks >= Mathf.Min(2, limit), "Live attacks respect configurable capacity without forcing enemies off cooldown: " + limit);
        }
        yield return AwaitMeleeCommit(); var member = CurrentMeleeCommit(); subject = member.Controller;
        Check(!subject.RequestAttack() && member.HasCommittedAttack && subject.Attack.IsAttacking, "Repeated attack polling preserves an existing committed swing");
        float remaining = subject.CooldownRemaining; subject.CancelAttack();
        Check(!member.HasAttackPermission && !subject.Attack.IsAttacking && subject.CooldownRemaining >= remaining - 0.001f, "Cancellation returns permission and preserves the spent weapon cooldown");
        yield return AwaitMeleeCommit(); member = CurrentMeleeCommit(); int registered = coordinator.RegisteredCount;
        member.enabled = false;
        Check(!member.HasAttackPermission && !member.Controller.Attack.IsAttacking && coordinator.RegisteredCount == registered - 1, "Disabling a member immediately cancels its unreleased hit and returns capacity");
        member.enabled = true; yield return Frames(3);
        Check(coordinator.RegisteredCount == registered, "Re-enabled member registers once and can rejoin the queue");
        yield return AwaitMeleeCommit(); member = CurrentMeleeCommit();
        member.GetComponent<CombatHealth>().ApplyDamage(10000); yield return Frames(2);
        Check(!member.HasAttackPermission && !member.gameObject.activeSelf, "Death releases the committed attack without leaving a token");
        yield return AwaitMeleeCommit(); member = CurrentMeleeCommit(); member.Controller.Graph.enabled = false; yield return Frames(3);
        Check(!member.HasAttackPermission && !member.Controller.Attack.IsAttacking, "Stopping the Behavior Graph releases and cancels its commitment");
        member.Controller.Graph.enabled = true; yield return Frames(3);
        yield return AwaitMeleeCommit(); member = CurrentMeleeCommit();
        var secondPlayer = new GameObject("Live target change"); secondPlayer.transform.position = player.transform.position + Vector3.right * 4;
        secondPlayer.AddComponent<CombatHealth>().Team = CombatTeam.Player;
        var agentFields = new SerializedObject(member.Agent); agentFields.FindProperty("target").objectReferenceValue = secondPlayer.transform; agentFields.ApplyModifiedPropertiesWithoutUndo();
        member.Controller.SensePlayer();
        Check(!member.HasAttackPermission && !member.Controller.Attack.IsAttacking, "Changing target cancels the old committed strike and reservation immediately");
        agentFields.FindProperty("target").objectReferenceValue = player.transform; agentFields.ApplyModifiedPropertiesWithoutUndo(); member.Controller.SensePlayer(); Object.Destroy(secondPlayer);
        yield return AwaitMeleeCommit(); player.SetActive(false); yield return Frames(4);
        Check(coordinator.ActiveMeleeReservations == 0 && coordinator.PendingMeleeRequests == 0, "Unavailable player clears active and queued requests even when no new requests arrive");
        player.SetActive(true); yield return Frames(60);
        yield return AwaitMeleeCommit(); coordinator.UseAttackPermissions = false;
        Check(coordinator.ActiveMeleeReservations == 0 && coordinator.CommittedMeleeAttacks == 0, "Live M1 comparison clears M2 reservations safely");
        yield return Frames(120); coordinator.UseAttackPermissions = true; yield return Frames(120);
        Check(SwarmMembers().All(m => !m.Controller.Attack.IsAttacking || m.HasCommittedAttack)
            && coordinator.ActiveMeleeReservations <= coordinator.MaximumMeleeAttacks, "Enabling permissions adopts an ongoing fight without unreserved swings");
        arena.ClearEnemies(); yield return Frames(3);
        Check(coordinator.RegisteredCount == 0 && coordinator.ActiveMeleeReservations == 0 && coordinator.PendingMeleeRequests == 0, "Arena reset clears all positioning and attack ownership");
        foreach (int role in new[] { 1, 2, 3 })
        {
            var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[role]), new Vector3(5, 1, -2), Quaternion.LookRotation(Vector3.left));
            member = actor.AddComponent<EnemyCoordinationMember>(); member.Bind(coordinator); subject = member.Controller;
            yield return Until(() => subject.Attack.IsAttacking, 15, "Basic melee variant did not get a coordinated attack: " + role);
            Check(member.HasCommittedAttack, "Opt-in basic melee variant shares the pool: " + subject.Role);
            actor.SetActive(false); Object.Destroy(actor); yield return Frames(3);
        }
        foreach (int role in new[] { 4, 5, 6, 7, 8 })
        {
            var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[role]), new Vector3(12, 1, 0), Quaternion.LookRotation(Vector3.left));
            member = actor.AddComponent<EnemyCoordinationMember>(); member.Bind(coordinator); yield return Frames(4);
            Check(!member.Controller.UsesMeleeAttackCoordination && !member.HasAttackPermission && coordinator.ActiveMeleeReservations == 0,
                "Ranged/special role never consumes basic melee capacity: " + member.Controller.Role);
            actor.SetActive(false); Object.Destroy(actor); yield return Frames(3);
        }
        Check(coordinator.RegisteredCount == 0 && coordinator.PendingMeleeRequests == 0, "Mixed-role cleanup leaves no members or reservations");
    }
}
