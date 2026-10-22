using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class SpiralBarrageChecks
{
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Flags).SetValue(obj, value);
    static object Get(object obj, string field) => obj.GetType().GetField(field, Flags).GetValue(obj);
    static void Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Flags).Invoke(obj, args);
    static CombatHealth Enemy(Vector3 position, float hp = 100)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.SetActive(false);
        obj.transform.position = position;
        var health = obj.AddComponent<CombatHealth>();
        Set(health, "maximumHealth", hp);
        obj.AddComponent<MechTargetable>();
        obj.SetActive(true);
        return health;
    }

    public static void Run(Action<bool, string> check)
    {
        Vector3 origin = new Vector3(12000, 2, 0);
        var settings = CannonSpiralBarrage.Settings.Default;
        settings.nodeCount = 3;
        settings.angularIncrement = 90;
        settings.startRadius = 4;
        settings.spacing = 2;
        settings.initialDelay = 0.25f;
        settings.nodeDelay = 0.25f;
        settings.blastRadius = 1;
        settings.damageMultiplier = 1;
        settings.maximumSecondaryDamage = 200;
        settings.maximumSecondaryTargets = 2;
        Vector3[] offsets = { Vector3.forward * 4, Vector3.right * 6, Vector3.back * 8, Vector3.left * 10 };
        for (int i = 0; i < offsets.Length; i++)
            check(Vector3.Distance(CannonSpiralBarrage.NodePosition(origin, Vector3.forward, settings, i), origin + offsets[i]) < 0.001f,
                "Spiral node " + i + " has deterministic angle, spacing and impact height");
        check(CannonSpiralBarrage.NodePosition(origin, Vector3.up, settings, 0) == origin + offsets[0],
            "Vertical projectile travel has a deterministic horizontal spiral heading");

        var player = new GameObject("Spiral Integration Player");
        player.SetActive(false);
        player.transform.position = origin;
        var combat = player.AddComponent<MechCombatController>();
        var feedback = player.AddComponent<CombatFeedback>();
        Set(feedback, "effectMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/Combat/Prototype Combat.mat"));
        Set(feedback, "prewarmEffects", 4);
        Set(feedback, "maximumEffects", 8);
        Set(feedback, "cameraImpulse", false);
        var view = new GameObject("Spiral Integration Camera").AddComponent<Camera>();
        view.transform.SetPositionAndRotation(origin, Quaternion.identity);
        Set(combat, "gameplayCamera", view);
        check(!(bool)Get(combat, "spiralBarrage"), "Spiral mode is off by default");
        Set(combat, "spiralBarrage", true);
        Set(combat, "spiralSettings", settings);
        player.SetActive(true);
        var events = new List<MechProjectileImpact>();
        combat.ProjectileImpact += events.Add;
        var primary = Enemy(origin + Vector3.forward * 5);
        int deaths = 0;
        primary.Died += () => deaths++;
        Physics.SyncTransforms();
        var shot = combat.TryFire(PartCategory.ShoulderL);
        var runner = player.GetComponent<CannonSpiralBarrage>();
        check(runner != null && runner.ActiveCount == 0, "Firing creates the reusable scheduler but starts no barrage before collision");
        // Switching the Inspector toggle after firing must not mutate the in-flight shot.
        Set(combat, "spiralBarrage", false);
        Call(shot, "Step", 0.2f);
        check(primary.IsDead && deaths == 1 && events.Count == 1 && events[0].DeferConfirmation
            && runner.ActiveCount == 1 && feedback.KillConfirmationCount == 0,
            "Initial cannon impact keeps immediate damage, captures its mode at launch, and defers only the kill confirmation");
        Vector3 impact = events[0].Position;
        Vector3 heading = shot.Direction;
        var first = Enemy(CannonSpiralBarrage.NodePosition(impact, heading, settings, 0));
        var second = Enemy(CannonSpiralBarrage.NodePosition(impact, heading, settings, 1));
        var extra = new GameObject("Compound collider").AddComponent<BoxCollider>();
        extra.transform.SetParent(first.transform, false);
        first.Died += () => deaths++;
        second.Died += () => deaths++;
        Physics.SyncTransforms();
        Call(shot, "Step", 1f);
        Call(shot, "Impact", first.GetComponent<Collider>());
        check(events.Count == 1 && first.CurrentHealth == 100 && second.CurrentHealth == 100,
            "Spent initial projectile cannot resolve twice or apply secondary damage early");
        float start = Time.time;
        Call(runner, "Tick", start + 0.249f);
        check(events.Count == 1 && first.CurrentHealth == 100, "First spiral damage waits for its configured delay");
        Call(feedback, "Tick", 1f);
        Call(runner, "Tick", start + 0.25f);
        check(first.IsDead && !second.IsDead && events.Count == 2 && events[1].Hits == 1 && events[1].Kills == 1
            && events[1].Secondary && events[1].DeferConfirmation && feedback.ActiveEffects == 1,
            "Secondary damage and its pooled visible explosion happen together, deduplicating compound colliders");
        check(feedback.KillConfirmationCount == 0, "Individual secondary kills never post HUD notifications");
        Call(runner, "Tick", start + 0.499f);
        check(!second.IsDead, "Later nodes cannot apply damage ahead of their visible detonation time");
        Call(runner, "Tick", start + 0.5f);
        Call(runner, "Tick", start + 0.75f);
        check(deaths == 3 && events.Count == 5 && events[4].ConfirmationOnly && events[4].Kills == 3
            && feedback.KillConfirmationCount == 1 && feedback.LastConfirmedKills == 3 && runner.ActiveCount == 0,
            "Initial and secondary kills produce one complete-barrage triple-kill confirmation");
        check(combat.AmmoRemaining(MechWeaponType.Cannon) == 19 && combat.ReloadRemaining(MechWeaponType.Cannon) == 3,
            "Secondary detonations consume no extra ammo and do not change the cannon cooldown");
        Call(feedback, "Tick", 1f);
        check(feedback.ActiveEffects == 0, "Secondary visuals expire back into the existing pool");

        // Normal cannon still uses precisely its single impact after a barrage has been used.
        var normalEnemy = Enemy(origin + Vector3.forward * 5);
        Physics.SyncTransforms();
        Call(combat, "TickCooldowns", 3f);
        int before = events.Count;
        shot = combat.TryFire(PartCategory.ShoulderL);
        Call(shot, "Step", 0.2f);
        check(normalEnemy.IsDead && events.Count == before + 1 && !events[before].DeferConfirmation
            && runner.ActiveCount == 0 && feedback.KillConfirmationCount == 2,
            "Turning Spiral Barrage off restores one normal blast and immediate confirmation");

        CheckBlastRules(check, origin + Vector3.right * 100, player.transform);

        var emptyImpact = new MechProjectileImpact(MechWeaponType.Cannon, origin + Vector3.forward * 100, 6, 0, 0);
        var visualOffSettings = settings;
        visualOffSettings.nodeCount = 1;
        var visualOffEnemy = Enemy(CannonSpiralBarrage.NodePosition(emptyImpact.Position, Vector3.forward, visualOffSettings, 0));
        Physics.SyncTransforms();
        Set(feedback, "presentationEnabled", false);
        Call(feedback, "Tick", 0f);
        runner.TryBegin(emptyImpact, 100, Vector3.forward, player.transform, ~0, visualOffSettings);
        Call(runner, "Tick", start + 1);
        check(visualOffEnemy.IsDead && feedback.ActiveEffects == 0 && runner.ActiveCount == 0,
            "Muting presentation does not disable scheduled spiral gameplay damage");
        Set(feedback, "presentationEnabled", true);
        Object.DestroyImmediate(visualOffEnemy.gameObject);
        Set(combat, "spiralBarrage", true);
        var otherWeaponEnemy = Enemy(origin + Vector3.forward * 5);
        Physics.SyncTransforms();
        shot = combat.TryFire(PartCategory.ArmR);
        Call(shot, "Step", 0.2f);
        shot = combat.TryFire(PartCategory.ShoulderR);
        Call(shot, "Step", 0.2f);
        check(otherWeaponEnemy.CurrentHealth == 20 && runner.ActiveCount == 0,
            "Enabling the cannon pattern leaves axe and crossbow damage and impact behavior unchanged");
        Object.DestroyImmediate(otherWeaponEnemy.gameObject);
        before = events.Count;
        runner.TryBegin(emptyImpact, 100, Vector3.forward, player.transform, ~0, settings);
        Call(runner, "Tick", start + 100f);
        check(events.Count == before + 1 && runner.ActiveCount == 1,
            "A long frame emits only one node instead of collapsing the barrage into simultaneous flashes");
        Call(runner, "Tick", start + 100f);
        check(events.Count == before + 1, "Hitch recovery preserves minimum spacing between visible damage events");
        runner.CancelAll();
        before = events.Count;
        for (int i = 0; i < 30; i++)
        {
            runner.TryBegin(emptyImpact, 100, Vector3.forward, player.transform, ~0, settings);
            for (int n = 0; n < settings.nodeCount; n++) Call(runner, "Tick", start + 1 + n * 0.25f);
            Call(feedback, "Tick", 1f);
        }
        check(runner.PoolSize == 1 && runner.ActiveCount == 0 && feedback.PoolSize <= 8 && feedback.ActiveEffects == 0
            && events.Count == before + 30 * 4,
            "Repeated barrages reuse one sequence and bounded VFX storage without lingering work");
        int admitted = 0;
        for (int i = 0; i < 12; i++)
            if (runner.TryBegin(emptyImpact, 100, Vector3.forward, player.transform, ~0, settings)) admitted++;
        check(admitted == 8 && runner.PoolSize == 8 && runner.ActiveCount == 8,
            "Concurrent barrage storage has a hard cap with safe normal-impact fallback");
        combat.enabled = false;
        before = events.Count;
        Call(runner, "Tick", start + 1000f);
        check(runner.ActiveCount == 0 && events.Count == before
            && !runner.TryBegin(emptyImpact, 100, Vector3.forward, player.transform, ~0, settings),
            "Disabling combat cancels pending damage and rejects barrages from old in-flight shots");
        player.SetActive(false);
        Object.DestroyImmediate(player);
        Object.DestroyImmediate(view.gameObject);
        Object.DestroyImmediate(primary.gameObject);
        Object.DestroyImmediate(first.gameObject);
        Object.DestroyImmediate(second.gameObject);
        Object.DestroyImmediate(normalEnemy.gameObject);
    }

    static void CheckBlastRules(Action<bool, string> check, Vector3 origin, Transform owner)
    {
        var resolver = new CombatBlast();
        var budget = new CombatBlast.Budget();
        var near = Enemy(origin + Vector3.forward, 25);
        var far = Enemy(origin + Vector3.forward * 2, 100);
        var extra = new GameObject("Budget compound collider").AddComponent<BoxCollider>();
        extra.transform.SetParent(far.transform, false);
        Physics.SyncTransforms();
        budget.Reset(60, 2);
        var result = resolver.Resolve(origin, 3, 50, owner, ~0, budget: budget);
        check(near.IsDead && far.CurrentHealth == 65 && result.Hits == 2 && result.Kills == 1 && budget.RemainingDamage == 0,
            "Secondary damage budget counts actual health removed, caps the final hit, and deduplicates colliders");
        resolver.Resolve(origin, 3, 50, owner, ~0, budget: budget);
        check(far.CurrentHealth == 65, "Exhausted total damage budget applies across later detonations");
        near.gameObject.SetActive(false);
        var other = Enemy(origin + Vector3.left, 100);
        budget.Reset(100, 1);
        Physics.SyncTransforms();
        resolver.Resolve(origin, 3, 20, owner, ~0, budget: budget);
        resolver.Resolve(origin, 3, 20, owner, ~0, budget: budget);
        check(other.CurrentHealth == 60 && far.CurrentHealth == 65 && budget.Targets.Count == 1,
            "Distinct-target cap spans the barrage; overlapping later blasts hit admitted targets once per node");
        other.gameObject.SetActive(false);
        Vector3 meshOrigin = origin + Vector3.right * 20;
        var meshEnemy = Enemy(meshOrigin + Vector3.forward * 2);
        var mesh = GameObject.CreatePrimitive(PrimitiveType.Plane);
        mesh.transform.position = meshOrigin + Vector3.down;
        budget.Reset(100, 5);
        Physics.SyncTransforms();
        result = resolver.Resolve(meshOrigin, 3, 10, owner, ~0, budget: budget);
        check(result.Hits == 1 && meshEnemy.CurrentHealth == 90,
            "Non-convex ground meshes do not falsely suppress nearby spiral damage");
        mesh.transform.SetPositionAndRotation(meshOrigin + Vector3.forward, Quaternion.Euler(-90, 0, 0));
        Physics.SyncTransforms();
        result = resolver.Resolve(meshOrigin, 3, 10, owner, ~0, budget: budget);
        check(result.Hits == 0 && meshEnemy.CurrentHealth == 90,
            "Non-convex mesh walls still block secondary blast line of sight");
        Object.DestroyImmediate(meshEnemy.gameObject);
        Object.DestroyImmediate(mesh);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = origin + Vector3.forward;
        wall.transform.localScale = new Vector3(5, 5, 0.2f);
        budget.Reset(100, 5);
        Physics.SyncTransforms();
        result = resolver.Resolve(origin, 3, 30, owner, ~0, budget: budget);
        check(result.Hits == 0 && far.CurrentHealth == 65 && budget.RemainingDamage == 100,
            "World obstruction blocks secondary blast damage without spending its budget");
        result = resolver.Resolve(wall.transform.position, 3, 30, owner, ~0, budget: budget);
        check(result.Hits == 0, "A secondary node inside a wall cannot bypass obstruction through an inside-origin ray");
        wall.SetActive(false);
        far.transform.position = owner.position;
        far.transform.SetParent(owner, true);
        Physics.SyncTransforms();
        result = resolver.Resolve(owner.position, 3, 30, owner, ~0, budget: budget);
        check(result.Hits == 0 && far.CurrentHealth == 65, "Secondary blasts preserve owner-hierarchy exclusion");
        far.transform.SetParent(null, true);
        far.transform.position = origin + Vector3.forward;
        far.GetComponent<BoxCollider>().isTrigger = true;
        extra.enabled = false;
        Physics.SyncTransforms();
        result = resolver.Resolve(origin, 3, 30, owner, ~0, budget: budget);
        check(result.Hits == 0, "Secondary blast queries preserve trigger exclusion");
        far.GetComponent<BoxCollider>().isTrigger = false;
        Physics.SyncTransforms();
        result = resolver.Resolve(origin, 3, 30, owner, 0, budget: budget);
        check(result.Hits == 0, "Secondary blast queries preserve collision-layer filtering");
        Object.DestroyImmediate(near.gameObject);
        Object.DestroyImmediate(far.gameObject);
        Object.DestroyImmediate(other.gameObject);
        Object.DestroyImmediate(wall);
    }
}
