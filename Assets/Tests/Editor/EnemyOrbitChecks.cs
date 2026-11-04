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
    static EnemyGraphController SpawnOrbit(bool pursue = false)
    {
        var actor = SpawnRole(7, 6.4f);
        // Isolate contact/timing fixtures from pursuit; separate cases below
        // exercise the saved prefab's normal movement and collision settings.
        if (!pursue)
        {
            var settings = new SerializedObject(actor.GetComponent<EnemyRopeAttack>());
            settings.FindProperty("orbitChaseStopDistance").floatValue = 1000;
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
        return actor;
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

        ResetPlayer(); var actor = SpawnOrbit(); var attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Orbit windup missing");
        float hp = health.CurrentHealth; yield return Frames(30);
        var warning = actor.transform.Find("Attack warning").GetComponent<LineRenderer>();
        Check(attack.OrbitContacts == 0 && health.CurrentHealth == hp && warning.enabled
            && Mathf.Abs(Steering.Flat(warning.GetPosition(0) - actor.transform.position).magnitude - attack.OrbitRadius) < .005f,
            "Orbit windup announces a ring centered on the enemy before damage");
        MovePlayer(actor.transform.position + Vector3.right * 30);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Grounded, 3, "Orbit throw did not land before swinging");
        var landing = RopePart(attack, "rock").position; float landedAt = Time.time;
        var attackFields = new SerializedObject(attack);
        float groundPause = attackFields.FindProperty("orbitGroundPauseDuration").floatValue;
        float liftDuration = attackFields.FindProperty("orbitLiftDuration").floatValue;
        var restingCenter = actor.transform.position;
        Check(Mathf.Abs(landing.y - .6f) < .01f && attack.OrbitsStarted == 0 && attack.IsExecuting,
            "The orbit throw lands on the actual floor before any swing begins"); Capture("orbit-grounded", actor);
        MovePlayer(landing);
        bool pinned = true;
        for (int i = 0; i < Mathf.FloorToInt(groundPause * 30); i++)
        {
            yield return Frames(1);
            pinned &= attack.CurrentStage == EnemyGraphAttack.Stage.Grounded && TetherAttached(attack)
                && Vector3.Distance(RopePart(attack, "rock").position, landing) < .005f
                && Vector3.Distance(actor.transform.position, restingCenter) < .005f;
        }
        Check(pinned && health.CurrentHealth == hp && attack.OrbitContacts == 0,
            "The landed rock and enemy stay still during the ground pause, with no standing contact damage");
        MovePlayer(actor.transform.position + Vector3.right * 30);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.OrbitLift, 3, "Landed rock did not lift after its pause");
        float liftBegan = Time.time;
        Check(liftBegan - landedAt >= groundPause - .034f && attack.OrbitsStarted == 0,
            "The Inspector ground-pause duration elapses before the rock starts lifting");
        yield return Frames(6);
        Check(attack.CurrentStage == EnemyGraphAttack.Stage.OrbitLift && attack.IsExecuting && TetherAttached(attack)
            && RopePart(attack, "rock").position.y > landing.y && RopePart(attack, "rock").position.y < 1.1f
            && Vector3.Distance(Steering.Flat(RopePart(attack, "rock").position - landing), Vector3.zero) < .005f,
            "The rock rises smoothly from the landing point before starting its circular motion");
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Orbit, 4, "Thrown rock did not enter orbit");
        Check(Time.time - liftBegan >= liftDuration - .034f && attack.OrbitsStarted == 1,
            "The lift completes before the full orbit duration starts");
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
        Check(constantRadius && turned > 720 && noPull && attack.IsExecuting,
            "The faster rock completes over two revolutions at fixed radius while its target is inside the pursuit stopping distance");
        Check(attached && health.CurrentHealth == hp - attack.Damage && attack.OrbitContacts == 1,
            "The full spin circle damages the center once even though the rock never touches it");
        Check(mountFollows, "The body stays upright and turns its attachment toward the rock as it swings");
        MovePlayer(AheadOfRock(attack, 25));
        yield return Frames(2);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Recovery, 2, "Orbit did not stop after its duration");
        Check(health.CurrentHealth == hp - attack.Damage && attack.Impacts == 0 && Vector3.Distance(actor.transform.position, center) < .05f,
            "Moving from the interior to the rock's path cannot duplicate the attack's one hit");
        yield return Until(() => Vector3.Distance(RopePart(attack, "rock").position, HeldRock(attack)) < .005f,
            2, "Orbit recovery did not reel the rock back to its mount");
        Check(Vector3.Distance(RopePart(attack, "rock").position, HeldRock(attack)) < .005f && TetherAttached(attack),
            "The orbiting variant reels the rock back to its held position during recovery; stage=" + attack.CurrentStage
            + "; error=" + Vector3.Distance(RopePart(attack, "rock").position, HeldRock(attack))); Capture("orbit-recovered", actor);
        yield return Remove(actor);

        ResetPlayer(); actor = SpawnOrbit(); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Area-entry windup missing");
        MovePlayer(actor.transform.position + Vector3.right * 30); hp = health.CurrentHealth;
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Orbit, 5, "Area-entry spin missing");
        MovePlayer(actor.transform.position + Vector3.right * (attack.OrbitRadius + 2)); yield return Frames(8);
        Check(health.CurrentHealth == hp && attack.OrbitContacts == 0, "Standing outside the spin circle remains safe");
        // Opposite the rock and well inside the rim: no rock contact can explain this hit.
        MovePlayer(actor.transform.position - RockDirection(attack).normalized * 2.5f); yield return Frames(2);
        Check(health.CurrentHealth == hp - attack.Damage && attack.OrbitContacts == 1,
            "Entering the active circle away from the rock applies damage immediately");
        MovePlayer(actor.transform.position + Vector3.right * 30); yield return Frames(2);
        MovePlayer(actor.transform.position); yield return Frames(2);
        Check(health.CurrentHealth == hp - attack.Damage && attack.OrbitContacts == 1,
            "Leaving and re-entering the same spin cannot duplicate its damage");
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Recovery, 4, "Area-entry recovery missing");
        hp = health.CurrentHealth; yield return Frames(20);
        Check(health.CurrentHealth == hp, "The full circle stops dealing damage during recovery");
        yield return Remove(actor);

        ResetPlayer(); actor = SpawnOrbit(); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Area-wall windup missing");
        MovePlayer(actor.transform.position + Vector3.right * 30);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Orbit, 5, "Area-wall spin missing");
        var cover = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cover.transform.position = actor.transform.position + Vector3.right * 1.5f;
        cover.transform.position = new Vector3(cover.transform.position.x, 3, cover.transform.position.z);
        cover.transform.localScale = new Vector3(.3f, 6, 6);
        MovePlayer(actor.transform.position + Vector3.right * 3); hp = health.CurrentHealth; yield return Frames(8);
        Check(health.CurrentHealth == hp && attack.OrbitContacts == 0, "Cover still blocks the filled spin area");
        Object.Destroy(cover); yield return Frames(2);
        Check(health.CurrentHealth == hp - attack.Damage && attack.OrbitContacts == 1,
            "An unhit player inside the circle takes damage when cover is removed");
        yield return Remove(actor);

        ResetPlayer(); actor = SpawnOrbit(); attack = actor.GetComponent<EnemyRopeAttack>(); hp = health.CurrentHealth;
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Recovery, 8, "Stationary orbit attack did not complete");
        Check(attack.OrbitContacts == 1 && health.CurrentHealth == hp - attack.Damage,
            "Repeated passes and compound colliders cannot damage a stationary target repeatedly in one attack");
        yield return Until(() => attack.OrbitContacts >= 2, 10, "The next orbit attack never hit again");
        Check(health.CurrentHealth == hp - attack.Damage * 2, "A new orbit attack permits a fresh hit after the recovery window");
        yield return Remove(actor);

        ResetPlayer(); actor = SpawnOrbit(); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Blocked-orbit windup missing"); MovePlayer(actor.transform.position);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Orbit, 4, "Blocked orbit did not begin");
        hp = health.CurrentHealth; center = actor.transform.position;
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); var wallPoint = AheadOfRock(attack, 90); wallPoint.y = 3;
        wall.transform.position = wallPoint; wall.transform.localScale = new Vector3(1, 6, 1); Physics.SyncTransforms();
        bool stopped = attack.TickAttack(1.5f);
        Check(!stopped && attack.CurrentStage == EnemyGraphAttack.Stage.Orbit && health.CurrentHealth == hp
            && Vector3.Distance(actor.transform.position, center) < .005f
            && Mathf.Abs(RockDirection(attack).magnitude - attack.OrbitRadius) < .005f,
            "A fast long-frame orbit passes through scenery without ending the spin or repeating its area hit");
        actor.Graph.enabled = false;
        yield return Remove(actor); Object.Destroy(wall); yield return Frames(2);

        ResetPlayer(); actor = SpawnOrbit(); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Blocked-lift windup missing");
        MovePlayer(actor.transform.position + Vector3.right * 30);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Grounded, 3, "Blocked-lift ground pause missing");
        landing = RopePart(attack, "rock").position; center = actor.transform.position; hp = health.CurrentHealth;
        wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = landing + Vector3.up * .95f;
        wall.transform.localScale = new Vector3(2, .2f, 2); Physics.SyncTransforms();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Orbit, 4, "Overhead scenery interrupted the lift");
        Check(attack.OrbitsStarted == 1 && health.CurrentHealth == hp && Vector3.Distance(actor.transform.position, center) < .005f,
            "Overhead scenery does not interrupt lifting the orbit rock");
        yield return Remove(actor); Object.Destroy(wall); yield return Frames(2);

        ResetPlayer(); actor = SpawnOrbit(true); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Pursuit windup missing");
        MovePlayer(actor.transform.position + Vector3.forward * 18);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Orbit, 5, "Pursuit orbit missing");
        center = actor.transform.position; float initialDistance = Steering.Flat(player.transform.position - center).magnitude;
        float chargeSpeed = new SerializedObject(attack).FindProperty("orbitChargeSpeed").floatValue;
        float maximumChargeSpeed = 0;
        constantRadius = attached = mountFollows = true;
        for (int i = 0; i < 60; i++)
        {
            yield return Frames(1);
            maximumChargeSpeed = Mathf.Max(maximumChargeSpeed, actor.Agent.Velocity.magnitude);
            var direction = RockDirection(attack);
            constantRadius &= Mathf.Abs(direction.magnitude - attack.OrbitRadius) < .005f;
            attached &= TetherAttached(attack);
            var mount = Steering.Flat(RopePart(attack, "ropeAnchor").position - actor.transform.position);
            mountFollows &= Vector3.Dot(mount.normalized, direction.normalized) > .98f && Vector3.Dot(actor.transform.up, Vector3.up) > .999f;
        }
        Check(attack.CurrentStage == EnemyGraphAttack.Stage.Orbit
            && Steering.Flat(player.transform.position - actor.transform.position).magnitude < initialDistance - chargeSpeed * .6f
            && maximumChargeSpeed > chargeSpeed * .9f && maximumChargeSpeed <= chargeSpeed + .01f,
            "The spinning enemy accelerates into its configured charge speed and rapidly closes on the live player");
        Check(constantRadius && attached && mountFollows,
            "While walking, the orbit stays centered, the body stays upright and the rope remains attached to its outward-facing mount");
        MovePlayer(actor.transform.position + Vector3.right * 18); float oldX = actor.transform.position.x;
        yield return Frames(45);
        Check(attack.CurrentStage == EnemyGraphAttack.Stage.Orbit && actor.transform.position.x > oldX + chargeSpeed * .3f,
            "Moving the player redirects the fast charge during the same spin");
        Capture("orbit-pursuit", actor);
        actor.Graph.enabled = false; yield return Frames(70);
        Check(attack.CurrentStage == EnemyGraphAttack.Stage.Idle && actor.Agent.Velocity.magnitude < .01f && !RopeLine(attack).enabled,
            "Cancelling pursuit stops the motor and removes the orbiting presentation");
        yield return Remove(actor);

        ResetPlayer(); actor = SpawnOrbit(true); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Body-collision windup missing");
        MovePlayer(actor.transform.position + Vector3.forward * 18);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Orbit, 5, "Body-collision orbit missing");
        center = actor.transform.position;
        wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = center + Vector3.forward * 2;
        wall.transform.position = new Vector3(wall.transform.position.x, 3, wall.transform.position.z);
        wall.transform.localScale = new Vector3(40, 6, .5f); Physics.SyncTransforms();
        yield return Frames(60);
        Check(attack.CurrentStage == EnemyGraphAttack.Stage.Orbit && actor.transform.position.z < wall.transform.position.z - .25f,
            "The enemy body still respects terrain collision even though its spinning rock passes through it");
        yield return Remove(actor); Object.Destroy(wall); yield return Frames(2);

        ResetPlayer(); actor = SpawnOrbit(true); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Windup, 4, "Charge-braking windup missing");
        MovePlayer(actor.transform.position + Vector3.forward * 18);
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Orbit, 5, "Charge-braking orbit missing");
        center = actor.transform.position; hp = health.CurrentHealth; MovePlayer(center + Vector3.forward * 4);
        float stoppingDistance = new SerializedObject(attack).FindProperty("orbitChaseStopDistance").floatValue;
        bool keptClearance = true;
        for (int i = 0; i < 45; i++)
        {
            yield return Frames(1);
            keptClearance &= Steering.Flat(player.transform.position - actor.transform.position).magnitude >= stoppingDistance - .02f;
        }
        Check(attack.CurrentStage == EnemyGraphAttack.Stage.Orbit && keptClearance && actor.Agent.Velocity.magnitude < .1f
            && Steering.Flat(actor.transform.position - center).magnitude > .5f,
            "The fast orbit charge brakes at its player clearance instead of overshooting or sliding through the target");
        Check(health.CurrentHealth == hp - attack.Damage && attack.OrbitContacts == 1,
            "The moving spin circle damages a player inside the charge's stopping distance");
        yield return Remove(actor);

        foreach (var stage in new[] { EnemyGraphAttack.Stage.Windup, EnemyGraphAttack.Stage.Attack,
            EnemyGraphAttack.Stage.Grounded, EnemyGraphAttack.Stage.OrbitLift, EnemyGraphAttack.Stage.Orbit, EnemyGraphAttack.Stage.Recovery })
        {
            ResetPlayer(); actor = SpawnOrbit(); attack = actor.GetComponent<EnemyRopeAttack>();
            yield return Until(() => attack.CurrentStage == stage, 8, "Orbit cancellation stage missing: " + stage);
            hp = health.CurrentHealth; int contacts = attack.OrbitContacts; actor.Graph.enabled = false; yield return Frames(70);
            Check(attack.CurrentStage == EnemyGraphAttack.Stage.Idle && health.CurrentHealth == hp && contacts == attack.OrbitContacts
                && !RopeLine(attack).enabled && Vector3.Distance(RopePart(attack, "rock").position, HeldRock(attack)) < .005f,
                "Orbit cancellation stops damage and restores the held rock: " + stage);
            yield return Remove(actor);
        }
        ResetPlayer(); actor = SpawnOrbit(); attack = actor.GetComponent<EnemyRopeAttack>();
        yield return Until(() => attack.CurrentStage == EnemyGraphAttack.Stage.Orbit, 5, "Orbit death stage missing"); hp = health.CurrentHealth;
        actor.GetComponent<CombatHealth>().ApplyDamage(10000); yield return Frames(90);
        Check(!actor.gameObject.activeSelf && health.CurrentHealth == hp && !RopePart(attack, "rock").gameObject.activeInHierarchy && !RopeLine(attack).enabled,
            "Dying during orbit stops the rock, rope and future contact damage");
        yield return Remove(actor);
    }
}
