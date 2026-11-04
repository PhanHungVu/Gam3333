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
    public static void RunPressureBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene(EnemyBehaviorArenaSetup.ScenePath);
        SessionState.SetBool(Pending + ".Arena", false); SessionState.SetBool(Pending + ".Rope", false);
        SessionState.SetBool(Pending + ".Coordination", false); SessionState.SetBool(Pending + ".AttackCoordination", false);
        SessionState.SetBool(Pending + ".Pressure", true); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    sealed class PressureSample
    {
        public int Starts, PeakAttacks;
        public float LongestReadyWait;
        public bool CooldownsRespected = true, NoReservations = true;
        public readonly HashSet<EnemyCoordinationMember> Attackers = new HashSet<EnemyCoordinationMember>();
    }
    static IEnumerator MeasurePressure(EnemyBehaviorPlaytest arena, bool pressure, PressureSample sample)
    {
        if (pressure) arena.StartPressureSwarm(); else arena.StartCoordinatedSwarm();
        yield return Frames(4); var members = SwarmMembers();
        var active = new bool[members.Length]; var lastStart = Enumerable.Repeat(float.NegativeInfinity, members.Length).ToArray();
        var readyWait = new float[members.Length];
        foreach (var member in members) { var drops = member.GetComponent<EnemyPartDrop>(); if (drops != null) drops.enabled = false; }
        for (int frame = 0; frame < 1200; frame++)
        {
            int attacking = 0;
            for (int i = 0; i < members.Length; i++)
            {
                var controller = members[i].Controller; var attack = controller.Attack; bool isAttacking = attack.IsAttacking;
                if (isAttacking)
                {
                    attacking++;
                    if (!active[i])
                    {
                        sample.Starts++; sample.Attackers.Add(members[i]);
                        sample.CooldownsRespected &= Time.time - lastStart[i] + 0.025f >= attack.CooldownOf(attack.Weapon);
                        lastStart[i] = Time.time;
                    }
                }
                active[i] = isAttacking;
                bool ready = !isAttacking && attack.CooldownRemaining <= 0 && controller.TargetInReach
                    && controller.Agent.DistanceToTarget <= controller.Agent.AttackRange + 0.25f;
                readyWait[i] = ready ? readyWait[i] + Time.deltaTime : 0;
                sample.LongestReadyWait = Mathf.Max(sample.LongestReadyWait, readyWait[i]);
            }
            sample.PeakAttacks = Mathf.Max(sample.PeakAttacks, attacking);
            sample.NoReservations &= arena.Coordinator.ActiveMeleeReservations == 0 && arena.Coordinator.PendingMeleeRequests == 0;
            yield return null;
        }
        Debug.Log($"MELEE PRESSURE METRICS: pressure={pressure}, starts={sample.Starts}, peak attacks={sample.PeakAttacks}, attackers={sample.Attackers.Count}, longest ready wait={sample.LongestReadyWait:F3}");
    }
    static IEnumerator RunPressure()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.captureFramerate = 60; QualitySettings.vSyncCount = 0;
        yield return Frames(8);
        var arena = Object.FindAnyObjectByType<EnemyBehaviorPlaytest>(); var coordinator = arena.Coordinator;
        player = GameObject.FindWithTag("Player"); health = player.GetComponent<CombatHealth>(); health.SetMaximum(1000000, true);
        player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        grid = Object.FindAnyObjectByType<NavGrid>(); field = FlowField.Instance;
        Check(coordinator.ContinuousMeleePressure && !coordinator.UseAttackPermissions, "Saved arena defaults to continuous pressure with no shared attack budget");
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[0]).GetComponent<EnemyAttack>();
        float sourceCooldown = source.CooldownOf(source.Weapons[0]), sourceDamage = source.DamageOf(source.Weapons[0]);
        var reserved = new PressureSample(); yield return MeasurePressure(arena, false, reserved);
        var pressure = new PressureSample(); yield return MeasurePressure(arena, true, pressure);
        Check(pressure.NoReservations && pressure.PeakAttacks >= 3, "More than two swords can attack together without a permission queue");
        Check(pressure.Starts > reserved.Starts * 1.3f, "Same seeded group produces substantially more pressure with unchanged weapon cooldowns");
        Check(pressure.Attackers.Count >= 7, "Most of the horde reaches an attack opportunity instead of occupying waiting positions");
        Check(pressure.CooldownsRespected && pressure.LongestReadyWait < 0.25f, "Ready melee attacks begin promptly and still respect each weapon's own cooldown");
        Check(SwarmMembers().All(m => m.PositionIndex == -1), "Continuous approach has no reserved inner or outer slots");
        Check(SwarmMembers().All(m => Mathf.Approximately(m.Controller.Attack.CooldownOf(m.Controller.Attack.Weapons[0]), sourceCooldown)
            && Mathf.Approximately(m.Controller.Attack.DamageOf(m.Controller.Attack.Weapons[0]), sourceDamage)), "Pressure does not alter authored weapon damage or cooldown tuning");
        arena.ClearEnemies(); yield return Frames(3);
        Check(coordinator.RegisteredCount == 0 && coordinator.ActiveMeleeReservations == 0, "Pressure encounter clears without leaked ownership");

        var start = player.transform.position;
        subject = Spawn(new Vector3(0, 1, 2)); var member = subject.gameObject.AddComponent<EnemyCoordinationMember>(); member.Bind(coordinator);
        float spawnedAt = Time.time; yield return Until(() => subject.Attack.IsAttacking, 0.5f, "Reachable sword waited for a spatial assignment");
        Check(Time.time - spawnedAt < 0.25f && !member.HasAttackPermission, "A reachable target is attacked immediately without a slot or token");
        Check(subject.Attack.IsTelegraphing && health.CurrentHealth > 0, "Prompt attacks retain the authored windup");
        yield return Until(() => !subject.Attack.IsAttacking, 2, "Sword did not complete its attack");
        float remaining = subject.CooldownRemaining;
        var capsule = player.GetComponent<CharacterController>(); capsule.enabled = false;
        player.transform.position += Vector3.right * 6; Physics.SyncTransforms();
        Vector3 before = subject.transform.position; int observeFrames = Mathf.Clamp(Mathf.FloorToInt(remaining * 30), 2, 8);
        yield return Frames(observeFrames);
        Check(remaining > 0 && subject.Agent.Velocity.magnitude > 0.2f && Steering.Flat(subject.transform.position - before).magnitude > 0.025f,
            "Sword pursues a moving player during its remaining weapon cooldown");
        arena.ClearEnemies(); yield return Frames(3); player.transform.position = start; Physics.SyncTransforms();

        subject = Spawn(new Vector3(0, 1, 2));
        yield return Until(() => subject.Attack.IsAttacking, 0.5f, "Unbound basic enemy did not attack");
        Check(subject.ContinuousMeleePressure && subject.GetComponent<EnemyCoordinationMember>() == null,
            "Ordinary basic enemy prefabs also use continuous pursuit outside the comparison tool");
        yield return Until(() => !subject.Attack.IsAttacking, 2, "Ordinary sword did not recover");
        before = subject.transform.position; player.transform.position += Vector3.right * 6; Physics.SyncTransforms(); yield return Frames(6);
        Check(subject.Agent.Velocity.magnitude > 0.15f && Steering.Flat(subject.transform.position - before).magnitude > 0.005f,
            "An enemy without a coordinator also pursues during cooldown");
        arena.ClearEnemies(); yield return Frames(3); player.transform.position = start; Physics.SyncTransforms();
        foreach (int role in new[] { 4, 5, 6, 7, 8 })
        {
            var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[role]), new Vector3(12, 1, 0), Quaternion.LookRotation(Vector3.left));
            yield return Frames(3);
            Check(!actor.GetComponent<EnemyGraphController>().ContinuousMeleePressure, "Special/ranged attack pattern retained: " + (EnemyBehaviorRole)role);
            actor.SetActive(false); Object.Destroy(actor); yield return Frames(2);
        }
    }
}
