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
    public static void RunCoordinationBatch()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene(EnemyBehaviorArenaSetup.ScenePath);
        SessionState.SetBool(Pending + ".Arena", false); SessionState.SetBool(Pending + ".Rope", false);
        SessionState.SetBool(Pending + ".Coordination", true); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static EnemyCoordinationMember[] SwarmMembers() => EnemyAgent.All
        .Select(a => a.GetComponent<EnemyCoordinationMember>()).Where(m => m != null).ToArray();
    static IEnumerator RunCoordination()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.captureFramerate = 60; QualitySettings.vSyncCount = 0;
        yield return Frames(8);
        var arena = Object.FindAnyObjectByType<EnemyBehaviorPlaytest>(); var coordinator = arena.Coordinator;
        player = GameObject.FindWithTag("Player"); health = player.GetComponent<CombatHealth>(); health.SetMaximum(100000, true);
        player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        grid = Object.FindAnyObjectByType<NavGrid>(); field = FlowField.Instance;
        Check(coordinator != null && grid.Baked && field.HasDirection, "Saved arena has an opt-in coordinator and the existing navigation");
        var initialPlayer = player.transform.position;
        arena.StartBaselineSwarm(); var poses = arena.SwarmSpawnPositions.ToArray(); yield return Frames(90);
        Check(EnemyAgent.All.Count == 10 && poses.Length == 10, "Baseline starts exactly ten actual sword prefabs and clears the original scene enemy");
        Check(SwarmMembers().All(m => !m.HasPosition) && !coordinator.enabled, "Baseline uses ordinary graph approach with no positioning assignments");
        Check(EnemyAgent.All.All(a => a.ExternalDecisions && a.Fsm.Current == null), "Behavior Graphs remain the sole decision owner");
        arena.StartPositioningSwarm();
        Check(poses.SequenceEqual(arena.SwarmSpawnPositions), "Baseline and coordinated starts reuse the exact seeded spawn poses");
        yield return Frames(4);
        var members = SwarmMembers(); subject = members[0].Controller;
        Check(members.Length == 10 && coordinator.RegisteredCount == 10, "Coordinated restart registers ten members without old encounter reservations");
        Check(Object.FindAnyObjectByType<SpawnDirector>().CurrentWave == null, "Comparison encounter bypasses production wave data and spawning");
        var innerSlots = new HashSet<int>(); var attackers = new HashSet<EnemyCoordinationMember>();
        var attackSectors = new HashSet<int>(); var releaseCounts = new int[members.Length];
        float minSpacing = float.PositiveInfinity; bool unique = true, blocked = false, ordinarySteering = false;
        for (int frame = 0; frame < 1500; frame++)
        {
            var assigned = members.Where(m => m.HasPosition).ToArray();
            unique &= assigned.Select(m => m.PositionIndex).Distinct().Count() == assigned.Length;
            foreach (var member in assigned)
            {
                blocked |= grid.IsBlockedWorld(member.DesiredPosition);
                if (member.IsAttackPosition) innerSlots.Add(member.PositionIndex);
                if (member.Agent.SeparationRadius < 3 && member.Agent.SeparationRadius >= member.Agent.BodyRadius * 2) ordinarySteering = true;
            }
            for (int i = 0; i < members.Length; i++)
            {
                int releases = members[i].Controller.Attack.Releases;
                if (releases <= releaseCounts[i]) continue;
                releaseCounts[i] = releases; attackers.Add(members[i]);
                Vector3 offset = members[i].transform.position - player.transform.position;
                attackSectors.Add(Mathf.FloorToInt(Mathf.Repeat(Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg, 360) / 90));
            }
            for (int a = 0; a < members.Length; a++)
                for (int b = a + 1; b < members.Length; b++)
                    minSpacing = Mathf.Min(minSpacing, Steering.Flat(members[a].transform.position - members[b].transform.position).magnitude);
            yield return null;
        }
        Debug.Log($"M1 SWARM METRICS: inner positions={innerSlots.Count}, attack directions={attackSectors.Count}, distinct attackers={attackers.Count}, minimum centre spacing={minSpacing:F2}");
        Check(unique && !blocked, "Local positions remain exclusive and outside baked obstacles throughout the encounter");
        Check(innerSlots.Count >= 3, "Sword group uses several approach angles instead of a single shared goal");
        Check(attackSectors.Count >= 3, "Actual attack releases come from at least three directions around the player");
        Check(attackers.Count >= 6 && health.CurrentHealth < health.MaximumHealth, "Position leases rotate opportunities and multiple swords still land natural attacks");
        Check(minSpacing >= members.Min(m => m.Agent.BodyRadius) * 2 - 0.06f && ordinarySteering, "Body-sized local separation preserves spacing without the wide-radius melee deadlock");
        Check(members.All(m => m.Agent.Velocity.magnitude <= m.Agent.MaxSpeed + 0.01f && m.Agent.Fsm.Current == null), "Positioning adds no charge speed, teleport or competing state machine");

        arena.SetSwarmCoordination(false); yield return Frames(4);
        Check(members.All(m => !m.HasPosition && m.Agent.SeparationRadius >= 3.49f), "Disabling coordination releases positions and restores ordinary crowd settings");
        arena.SetSwarmCoordination(true); yield return Frames(30);
        Check(members.Any(m => m.HasPosition), "Coordination can be re-enabled during the same fight");

        var removed = members[0]; int count = coordinator.RegisteredCount;
        removed.GetComponent<CombatHealth>().ApplyDamage(10000); yield return Frames(3);
        Check(!removed.HasPosition && coordinator.RegisteredCount == count - 1, "Death releases the member and its position immediately");
        var disabled = members[1]; disabled.enabled = false; yield return Frames(3);
        Check(!disabled.HasPosition && coordinator.RegisteredCount == count - 2, "Disabling participation releases its assignment");
        disabled.enabled = true; yield return Frames(3);
        Check(coordinator.RegisteredCount == count - 1, "Re-enabling participation registers exactly once");

        yield return Frames(20);
        Check(SwarmMembers().Any(m => m.HasPosition), "Target-loss check starts with live positioning assignments");
        player.SetActive(false); yield return Frames(5);
        Check(SwarmMembers().All(m => !m.HasPosition), "Losing an assigned target clears positions without another actor requesting a plan");
        player.SetActive(true);
        subject = members[2].Controller;
        yield return Until(() => members.Where(m => m.isActiveAndEnabled).All(m => m.Controller.TargetDetected), 2, "Swords did not reacquire the restored player");

        var targetController = player.GetComponent<CharacterController>(); targetController.enabled = false;
        player.transform.position = initialPlayer + Vector3.forward * 38; Physics.SyncTransforms(); yield return Frames(8);
        Check(SwarmMembers().All(m => !m.HasPosition), "A fast escape drops stale local assignments and restores flow-field pursuit");
        var pursuer = members[2]; float distance = pursuer.Agent.DistanceToTarget; yield return Frames(90);
        yield return Until(() => pursuer.Agent.DistanceToTarget < distance - 1, 6, "Sword did not make progress after the escape detour");
        Check(pursuer.Agent.DistanceToTarget < distance - 1, "Sword continues navigating after the player escapes the local formation");
        player.SetActive(false); yield return Frames(5);
        Check(SwarmMembers().All(m => !m.HasPosition), "Unavailable target cannot retain local positions");
        player.SetActive(true); player.transform.position = initialPlayer; Physics.SyncTransforms(); yield return Frames(5);

        arena.ClearEnemies(); yield return Frames(3);
        Check(coordinator.RegisteredCount == 0 && EnemyAgent.All.Count == 0, "Arena clear removes all encounter actors and assignments");
        subject = Spawn(new Vector3(-6, 1, 0)); var isolated = subject.gameObject.AddComponent<EnemyCoordinationMember>(); isolated.Bind(coordinator);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Coordination blocked approach"; wall.tag = "Obstacle";
        wall.transform.position = new Vector3(-3, 1, 0); wall.transform.localScale = new Vector3(1, 2, 8);
        Physics.SyncTransforms(); grid.Bake(); field.Rebuild(); yield return Frames(3);
        Check(!isolated.TryGetPosition(out _), "Blocked local routes are rejected rather than steering through a wall");
        yield return Until(() => subject.Attack.Releases > 0, 22, "Existing flow field failed to route the sword around the blocked local approach");
        Check(subject.Attack.Releases > 0, "Rejected positions fall back to existing navigation and recover into a normal attack");
        Object.Destroy(wall); arena.ClearEnemies(); yield return Frames(3); grid.Bake(); field.Rebuild();

        var ranged = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[5]), new Vector3(5, 1, 0), Quaternion.LookRotation(Vector3.left));
        var rangedMember = ranged.AddComponent<EnemyCoordinationMember>(); rangedMember.Bind(coordinator); yield return Frames(5);
        Check(!rangedMember.TryGetPosition(out _) && !rangedMember.HasPosition, "M1 leaves crossbow and special-enemy decisions untouched");
        arena.ClearEnemies(); yield return Frames(3);
        Check(coordinator.RegisteredCount == 0, "Repeated starts and clears leave no registered members");
    }
}
