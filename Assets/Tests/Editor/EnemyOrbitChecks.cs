using System.Collections;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class EnemyBehaviorChecks
{
    static Vector3 RockDirection(EnemyRopeAttack attack) => Steering.Flat(RopePart(attack, "rock").position - attack.transform.position);
    static void MovePlayer(Vector3 position) { position.y = 1.6f; player.transform.position = position; Physics.SyncTransforms(); }
    static Vector3 AheadOfRock(EnemyRopeAttack attack, float degrees)
    {
        var d = RockDirection(attack); float angle = Mathf.Atan2(d.z, d.x) + degrees * Mathf.Deg2Rad;
        return attack.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * attack.OrbitRadius;
    }
    static IEnumerator OrbitCases()
    {
        DamageChecks.Run(Check);
        var original = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[7]);
        var preserved = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyRosterSetup.Roster[8]);
        Check(original.GetComponent<EnemyRopeAttack>().Behavior == EnemyRopeAttack.RopeBehavior.Orbit
            && preserved.GetComponent<EnemyRopeAttack>().Behavior == EnemyRopeAttack.RopeBehavior.ThrowAndPull
            && original.GetComponent<EnemyGraphController>().Role == EnemyBehaviorRole.RockRope
            && preserved.GetComponent<EnemyGraphController>().Role == EnemyBehaviorRole.RockRopePull,
            "The original rope elite now orbits; a separate prefab preserves the throw/pull role");
        Check(original.GetComponent<Unity.Behavior.BehaviorGraphAgent>().Graph != preserved.GetComponent<Unity.Behavior.BehaviorGraphAgent>().Graph,
            "Orbit and throw/pull variants use distinct ordered Behavior Graphs");
        foreach (string path in new[] { EnemyRosterSetup.Roaming, EnemyRosterSetup.Survival, "Assets/LevelCatalogue/WaveData_KOTH.asset" })
        {
            var wave = AssetDatabase.LoadAssetAtPath<WaveData>(path);
            var elites = wave.enemies.Where(e => e.prefab.GetComponent<EnemyGraphController>().Role >= EnemyBehaviorRole.HeavyRanged).ToArray();
            Check(wave.enemies.Length == 9 && elites.Length == 3 && elites.Any(e => e.prefab == original) && elites.Any(e => e.prefab == preserved)
                && Mathf.Abs(elites.Sum(e => e.weight) - (path == EnemyRosterSetup.Roaming ? 10 : 30)) < .001f,
                "Both rope variants share the existing total elite probability: " + path);
        }

        ResetPlayer(); var actor = SpawnRole(7, 6.4f); var attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Orbit windup missing");
        float hp = health.CurrentHealth; yield return Frames(30);
        var warning = actor.transform.Find("Attack warning").GetComponent<LineRenderer>();
        Check(attack.OrbitContacts == 0 && health.CurrentHealth == hp && warning.enabled
            && Mathf.Abs(Steering.Flat(warning.GetPosition(0) - actor.transform.position).magnitude - attack.OrbitRadius) < .005f,
            "Orbit windup announces a ring centered on the enemy before damage");
        MovePlayer(actor.transform.position + Vector3.right * 30);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Orbit, 4, "Thrown rock did not enter orbit");
        MovePlayer(actor.transform.position);
        var center = actor.transform.position; var previousDirection = RockDirection(attack); float turned = 0; bool attached = true, constantRadius = true, noPull = true, mountFollows = true;
        Capture("orbit-deployed", actor);
        for (int i = 0; i < 135; i++)
        {
            yield return Frames(1); var direction = RockDirection(attack);
            turned += Mathf.Abs(Vector3.SignedAngle(previousDirection, direction, Vector3.up)); previousDirection = direction;
            constantRadius &= Mathf.Abs(direction.magnitude - attack.OrbitRadius) < .005f;
            attached &= TetherAttached(attack);
            var mount = Steering.Flat(RopePart(attack, "ropeAnchor").position - actor.transform.position);
            mountFollows &= Vector3.Dot(mount.normalized, direction.normalized) > .98f && Vector3.Dot(actor.transform.up, Vector3.up) > .999f;
            noPull &= attack.CurrentStage == EnemyGraphAttack.Stage.Orbit && Vector3.Distance(actor.transform.position, center) < .05f;
            if (i == 40) Capture("orbit-quarter", actor);
            if (i == 80) Capture("orbit-behind", actor);
        }
        Check(constantRadius && turned > 360 && noPull && attack.IsExecuting,
            "The rock completes a full revolution at fixed radius while the enemy remains the center instead of pulling itself");
        Check(attached && health.CurrentHealth == hp && attack.OrbitContacts == 0,
            "The taut rope stays attached and the safe center takes no circular-area damage");
        Check(mountFollows, "The body stays upright and turns its attachment toward the rock as it swings");
        MovePlayer(AheadOfRock(attack, 25));
        yield return Until(() => attack.OrbitContacts == 1, 1, "Moving into the orbiting rock did not cause contact damage");
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Recovery, 2, "Orbit did not stop after its duration");
        Check(health.CurrentHealth == hp - attack.Damage && attack.Impacts == 0 && Vector3.Distance(actor.transform.position, center) < .05f,
            "Entering the rock's path causes one shared-rule hit without a landing blast or enemy lunge");
        yield return Frames(52);
        Check(Vector3.Distance(RopePart(attack, "rock").position, HeldRock(attack)) < .005f && TetherAttached(attack),
            "The orbiting variant reels the rock back to its held position during recovery; stage=" + attack.CurrentStage
            + "; error=" + Vector3.Distance(RopePart(attack, "rock").position, HeldRock(attack))); Capture("orbit-recovered", actor);
        yield return Remove(actor);

        ResetPlayer(); actor = SpawnRole(7, 6.4f); attack = actor.GetComponent<EnemyRopeAttack>(); hp = health.CurrentHealth;
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Recovery, 8, "Stationary orbit attack did not complete");
        Check(attack.OrbitContacts == 1 && health.CurrentHealth == hp - attack.Damage,
            "Repeated passes and compound colliders cannot damage a stationary target repeatedly in one attack");
        yield return Until(() => attack.OrbitContacts >= 2, 10, "The next orbit attack never hit again");
        Check(health.CurrentHealth == hp - attack.Damage * 2, "A new orbit attack permits a fresh hit after the recovery window");
        yield return Remove(actor);

        ResetPlayer(); actor = SpawnRole(7, 6.4f); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Blocked-orbit windup missing"); MovePlayer(actor.transform.position);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Orbit, 4, "Blocked orbit did not begin");
        hp = health.CurrentHealth; center = actor.transform.position;
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); var wallPoint = AheadOfRock(attack, 90); wallPoint.y = 3;
        wall.transform.position = wallPoint; wall.transform.localScale = new Vector3(1, 6, 1); Physics.SyncTransforms();
        bool stopped = attack.TickAttack(1.5f);
        Check(stopped && attack.CurrentStage == EnemyGraphAttack.Stage.Recovery && health.CurrentHealth == hp
            && Vector3.Distance(actor.transform.position, center) < .005f && Steering.Flat(RopePart(attack, "rock").position - wallPoint).magnitude < 2,
            "A long frame sweeps around the arc and stops at a wall without cutting through the center or dragging the enemy");
        actor.Graph.enabled = false;
        yield return Remove(actor); Object.Destroy(wall); yield return Frames(2);

        foreach (var stage in new[] { EnemyGraphAttack.Stage.Windup, EnemyGraphAttack.Stage.Attack, EnemyGraphAttack.Stage.Orbit, EnemyGraphAttack.Stage.Recovery })
        {
            ResetPlayer(); actor = SpawnRole(7, 6.4f); attack = actor.GetComponent<EnemyRopeAttack>();
            yield return Until(() => attack.CurrentStage == stage, 8, "Orbit cancellation stage missing: " + stage);
            hp = health.CurrentHealth; int contacts = attack.OrbitContacts; actor.Graph.enabled = false; yield return Frames(70);
            Check(attack.CurrentStage == EnemyGraphAttack.Stage.Idle && health.CurrentHealth == hp && contacts == attack.OrbitContacts
                && !RopeLine(attack).enabled && Vector3.Distance(RopePart(attack, "rock").position, HeldRock(attack)) < .005f,
                "Orbit cancellation stops damage and restores the held rock: " + stage);
            yield return Remove(actor);
        }
        ResetPlayer(); actor = SpawnRole(7, 6.4f); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Orbit, 5, "Orbit death stage missing"); hp = health.CurrentHealth;
        actor.GetComponent<CombatHealth>().ApplyDamage(10000); yield return Frames(90);
        Check(!actor.gameObject.activeSelf && health.CurrentHealth == hp && !RopePart(attack, "rock").gameObject.activeInHierarchy && !RopeLine(attack).enabled,
            "Dying during orbit stops the rock, rope and future contact damage");
        yield return Remove(actor);
    }
}
