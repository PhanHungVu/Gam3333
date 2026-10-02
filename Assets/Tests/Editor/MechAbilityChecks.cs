using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechAbilityChecks
{
    const BindingFlags Flags = BindingFlags.NonPublic | BindingFlags.Instance;
    static FieldInfo Field(object obj, string name)
    {
        for (Type type = obj.GetType(); type != null; type = type.BaseType)
        { var field = type.GetField(name, Flags); if (field != null) return field; }
        throw new Exception("Missing test field " + name);
    }
    static void Set(object obj, string name, object value) => Field(obj, name).SetValue(obj, value);
    static void Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Flags).Invoke(obj, args);
    static CombatHealth Enemy(Vector3 position, bool compound = false)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.transform.position = position;
        var health = obj.AddComponent<CombatHealth>();
        obj.AddComponent<MechTargetable>();
        if (compound)
        {
            var extra = new GameObject("Extra Hurt Collider").AddComponent<BoxCollider>();
            extra.transform.SetParent(obj.transform, false);
            extra.size = Vector3.one * 0.7f;
        }
        return health;
    }
    static GameObject Wall(Vector3 position, Vector3 size)
    {
        var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        obj.transform.position = position; obj.transform.localScale = size; return obj;
    }
    static void RunToEnd(MechPartAbility ability)
    { for (int i = 0; i < 400 && ability.IsActive; i++) ability.Tick(0.05f); }

    public static void Run(Action<bool, string> check)
    {
        Vector3 origin = new Vector3(15000, 2, 0);
        var floor = Wall(origin + Vector3.down * 1.25f, new Vector3(200, 0.5f, 200));
        var player = new GameObject("Ability Owner");
        player.SetActive(false); player.transform.position = origin;
        var combat = player.AddComponent<MechCombatController>();
        var movement = player.AddComponent<MechMovementController>();
        var ownerHealth = player.AddComponent<CombatHealth>();
        var feedback = player.AddComponent<CombatFeedback>();
        Set(feedback, "effectMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/Combat/Prototype Combat.mat"));
        var view = new GameObject("Ability View").AddComponent<Camera>();
        view.transform.SetPositionAndRotation(origin, Quaternion.identity);
        Set(combat, "gameplayCamera", view);
        Set(movement, "gameplayCamera", view);
        player.SetActive(true);
        var pulse = player.AddComponent<ResonancePulseAbility>();
        var zigzag = player.AddComponent<ZigzagBarrageAbility>();
        var bloom = player.AddComponent<BloomMissileAbility>();
        CheckPulse(check, pulse, player.transform, feedback, origin);
        CheckZigzag(check, zigzag, player.transform, feedback, origin + Vector3.right * 30);
        CheckBloom(check, bloom, player.transform, feedback, origin + Vector3.right * 65);
        check(ownerHealth.CurrentHealth == 100 && player.transform.position == origin,
            "All part abilities exclude their owner and never displace the movement root");

        var abilities = new MechPartAbility[] { pulse, zigzag, bloom };
        for (int active = 0; active < abilities.Length; active++)
        {
            for (int i = 0; i < abilities.Length; i++) abilities[i].enabled = i == active;
            var context = new MechAbilityContext(player.transform, origin, Vector3.forward, feedback: feedback);
            check(abilities[active].TryActivate(context) && !abilities[active].TryActivate(context),
                abilities[active].GetType().Name + " works alone and rejects overlapping activation on the same part");
            abilities[active].Tick(0.05f);
            abilities[active].enabled = false;
            check(!abilities[active].IsActive, "Disabling a part cancels its pending damage");
            abilities[active].enabled = true;
            check(abilities[active].TryActivate(context), "Re-enabling a part permits a fresh activation");
            abilities[active].Cancel();
        }
        foreach (var ability in abilities) ability.enabled = true;
        Set(movement, "quickBoostTimer", 0.2f);
        Set(movement, "quickBoostDirection", Vector3.right);
        int ammo = combat.AmmoRemaining(MechWeaponSlot.Cannon);
        Quaternion cameraRotation = view.transform.rotation;
        for (int i = 0; i < abilities.Length; i++)
            check(abilities[i].TryActivate(new MechAbilityContext(player.transform, origin, Vector3.forward, feedback: feedback)),
                "Independent part activation is allowed during boost");
        foreach (var ability in abilities) ability.Tick(0.05f);
        check(movement.IsQuickBoosting && (Vector3)Field(movement, "quickBoostDirection").GetValue(movement) == Vector3.right
            && combat.AmmoRemaining(MechWeaponSlot.Cannon) == ammo && view.transform.rotation == cameraRotation,
            "Concurrent abilities preserve boost state/direction, weapon ammo and camera orientation");
        foreach (var ability in abilities) ability.Cancel();
        Call(feedback, "Tick", 2f);
        check(feedback.ActiveEffects == 0, "Cancelled abilities release all leased wave and projectile visuals");

        int poolSize = feedback.PoolSize;
        for (int iteration = 0; iteration < 15; iteration++)
        {
            foreach (var ability in abilities)
            {
                ability.TryActivate(new MechAbilityContext(player.transform, origin, Vector3.forward, feedback: feedback));
                RunToEnd(ability);
            }
            Call(feedback, "Tick", 2f);
        }
        check(feedback.PoolSize <= 32 && feedback.ActiveEffects == 0 && bloom.ActiveProjectiles == 0
            && bloom.ProjectilePoolSize <= 25, "Repeated activations leave no active work and keep both pools bounded");
        foreach (var name in new[] { "ResonancePulse", "ZigzagBarrage", "BloomMissile" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Abilities/" + name + " Part.prefab");
            check(prefab != null && prefab.GetComponent<MechPartAbility>() != null && prefab.GetComponent<MechAbilityDriver>() != null,
                name + " part prefab supplies an independently equippable ability and configurable driver");
        }
        var part = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Abilities/ResonancePulse Part.prefab"), player.transform);
        var driver = part.GetComponent<MechAbilityDriver>();
        part.GetComponent<Part>().isEquipped = true; // Explicit manual attachment for the legacy prototype fixture.
        driver.BindOwner(player.transform);
        Set(driver, "activateOnWeaponImpact", true);
        Set(driver, "sourceWeapon", MechWeaponSlot.Crossbow);
        Set(driver, "activationCooldown", 0f);
        var target = Enemy(origin + Vector3.forward * 5);
        Physics.SyncTransforms();
        var shot = combat.TryFire(MechWeaponSlot.Crossbow);
        Call(shot, "Step", 0.2f);
        check(part.GetComponent<MechPartAbility>().IsActive && target.CurrentHealth == 70,
            "Generic impact adapter equips Resonance on crossbow without changing original projectile damage");
        part.SetActive(false);
        check(!part.GetComponent<MechPartAbility>().IsActive, "Unequipping the part cancels its active ability");
        Object.DestroyImmediate(part); Object.DestroyImmediate(target.gameObject);
        Object.DestroyImmediate(player); Object.DestroyImmediate(view.gameObject); Object.DestroyImmediate(floor);
    }

    static void CheckPulse(Action<bool, string> check, ResonancePulseAbility pulse, Transform owner, CombatFeedback feedback, Vector3 origin)
    {
        Set(pulse, "ringCount", 2); Set(pulse, "radius", 6f); Set(pulse, "speed", 2f);
        Set(pulse, "width", 0.2f); Set(pulse, "delay", 4f); Set(pulse, "damage", 20f); Set(pulse, "knockback", 0f);
        var a = Enemy(origin + Vector3.forward * 3, true);
        var moving = Enemy(origin + Vector3.right * 10);
        Physics.SyncTransforms();
        pulse.TryActivate(new MechAbilityContext(owner, origin, Vector3.forward, feedback: feedback));
        pulse.Tick(0.5f);
        check(a.CurrentHealth == 100 && feedback.ActiveEffects > 0, "Resonance displays a wave before damage reaches distant enemies");
        pulse.Tick(0.8f);
        check(a.CurrentHealth == 80, "Resonance damages when the front arrives, once for a multi-collider enemy");
        pulse.Tick(0.1f);
        check(a.CurrentHealth == 80, "Remaining inside a pulse cannot produce repeated damage");
        moving.transform.position = origin + Vector3.right * 3.4f;
        Physics.SyncTransforms(); pulse.Tick(0.1f);
        check(moving.CurrentHealth == 80, "Moving into the active wavefront receives damage at the current position");
        var behind = Enemy(origin + Vector3.left);
        Physics.SyncTransforms(); pulse.Tick(0.1f);
        check(behind.CurrentHealth == 100, "Moving behind an already-passed front cannot take retroactive pulse damage");
        behind.gameObject.SetActive(false);
        RunToEnd(pulse);
        check(!pulse.IsActive && a.CurrentHealth == 60 && moving.CurrentHealth == 60,
            "Separate resonance rings may re-hit once each and finish within the lifetime bound");
        Object.DestroyImmediate(a.gameObject); Object.DestroyImmediate(moving.gameObject); Object.DestroyImmediate(behind.gameObject);
        var pushed = Enemy(origin + Vector3.forward * 2);
        var wall = Wall(origin + Vector3.forward * 2.8f, new Vector3(4, 4, 0.2f));
        Set(pulse, "ringCount", 1); Set(pulse, "speed", 10f); Set(pulse, "knockback", 1f);
        Physics.SyncTransforms();
        pulse.TryActivate(new MechAbilityContext(owner, origin, Vector3.forward, feedback: feedback)); pulse.Tick(0.2f);
        check(pushed.CurrentHealth == 80 && pushed.transform.position.z > origin.z + 2
            && pushed.transform.position.z < origin.z + 2.3f, "Pulse knockback moves a survivor but stops before world collision");
        pulse.Cancel(); Object.DestroyImmediate(pushed.gameObject); Object.DestroyImmediate(wall);
        Set(pulse, "knockback", 0f);
        var blocked = Enemy(origin + Vector3.forward * 3);
        wall = Wall(origin + Vector3.forward * 1.5f, new Vector3(4, 4, 0.2f));
        Physics.SyncTransforms(); pulse.TryActivate(new MechAbilityContext(owner, origin, Vector3.forward, feedback: feedback));
        RunToEnd(pulse);
        check(blocked.CurrentHealth == 100, "Resonance preserves world line-of-sight obstruction");
        Object.DestroyImmediate(blocked.gameObject); Object.DestroyImmediate(wall);
    }

    static void CheckZigzag(Action<bool, string> check, ZigzagBarrageAbility zig, Transform owner, CombatFeedback feedback, Vector3 origin)
    {
        Set(zig, "count", 3); Set(zig, "spacing", 3f); Set(zig, "laneWidth", 2f);
        Set(zig, "delay", 0.2f); Set(zig, "blastRadius", 1.2f); Set(zig, "damage", 30f); Set(zig, "maximumTotalDamage", 45f);
        var enemies = new CombatHealth[3];
        for (int i = 0; i < 3; i++)
        {
            Vector3 node = ZigzagBarrageAbility.NodePosition(origin, Vector3.forward, i, 3, 2);
            check(node.x == origin.x + (i % 2 == 0 ? -1 : 1) && node.z == origin.z + (i + 1) * 3,
                "Zigzag alternates lanes while advancing along the captured aim direction");
            enemies[i] = Enemy(node, true);
        }
        Physics.SyncTransforms();
        int detonations = 0; zig.Resolved += result => { if (!result.Complete) detonations++; };
        zig.TryActivate(new MechAbilityContext(owner, origin, Vector3.forward, feedback: feedback));
        zig.Tick(0.19f); check(enemies[0].CurrentHealth == 100, "Zigzag never applies damage before the scheduled visible detonation");
        zig.Tick(0.02f); zig.Tick(0.21f); zig.Tick(0.21f);
        check(detonations == 3 && enemies[0].CurrentHealth == 70 && enemies[1].CurrentHealth == 85 && enemies[2].CurrentHealth == 100,
            "Zigzag deduplicates each blast and obeys a shared total damage cap");
        var wall = Wall(origin + Vector3.forward * 4.5f, new Vector3(12, 8, 0.2f));
        Physics.SyncTransforms(); detonations = 0;
        zig.TryActivate(new MechAbilityContext(owner, origin, Vector3.forward, feedback: feedback)); RunToEnd(zig);
        check(detonations == 1 && enemies[1].CurrentHealth == 85 && !zig.IsActive,
            "Zigzag stops at a wall instead of spawning later explosions on its far side");
        Object.DestroyImmediate(wall);
        detonations = 0;
        zig.TryActivate(new MechAbilityContext(owner, origin + Vector3.right * 1000, Vector3.forward, feedback: feedback)); RunToEnd(zig);
        check(detonations == 0, "Zigzag rejects unsupported placement instead of detonating over a void");
        foreach (var enemy in enemies) Object.DestroyImmediate(enemy.gameObject);
    }

    static void CheckBloom(Action<bool, string> check, BloomMissileAbility bloom, Transform owner, CombatFeedback feedback, Vector3 origin)
    {
        Set(bloom, "bombletCount", 4); Set(bloom, "carrierSpeed", 10f); Set(bloom, "burstDelay", 0.1f);
        Set(bloom, "speed", 10f); Set(bloom, "lifetime", 0.4f); Set(bloom, "elevation", 0f);
        Set(bloom, "explosionRadius", 1f); Set(bloom, "damage", 20f);
        check(Vector3.Angle(BloomMissileAbility.BombletDirection(Vector3.forward, 1, 4, 360, 0), Vector3.right) < 0.01f,
            "Bloom distributes deterministic radial directions without duplicating the full-circle endpoint");
        var enemy = Enemy(origin + Vector3.forward * 3, true);
        Physics.SyncTransforms(); int detonations = 0;
        bloom.Resolved += result => { if (!result.Complete) detonations++; };
        bloom.TryActivate(new MechAbilityContext(owner, origin, Vector3.forward, feedback: feedback));
        bloom.Tick(0.1f);
        check(enemy.CurrentHealth == 100 && bloom.ActiveProjectiles == 4, "Bloom carrier bursts into pooled bomblets without applying damage early");
        bloom.Tick(0.25f);
        check(enemy.CurrentHealth == 80, "Bloom swept collision resolves direct plus AoE damage only once per enemy");
        RunToEnd(bloom);
        check(detonations == 4 && bloom.ActiveProjectiles == 0 && !bloom.IsActive,
            "Every remaining bomblet detonates once at lifetime expiry and returns its visual to the pool");
        Object.DestroyImmediate(enemy.gameObject);
        var wall = Wall(origin + Vector3.forward, new Vector3(30, 8, 0.2f));
        enemy = Enemy(origin + Vector3.forward * 3);
        Set(bloom, "explosionRadius", 4f);
        Physics.SyncTransforms();
        bloom.TryActivate(new MechAbilityContext(owner, origin, Vector3.forward, feedback: feedback)); RunToEnd(bloom);
        check(enemy.CurrentHealth == 100, "Bloom carrier/bomblets collide with a wall and blast obstruction protects its far side");
        Object.DestroyImmediate(wall); Object.DestroyImmediate(enemy.gameObject);
        Set(bloom, "maximumProjectiles", 3); Set(bloom, "maximumDetonations", 1);
        detonations = 0;
        bloom.TryActivate(new MechAbilityContext(owner, origin, Vector3.forward, feedback: feedback)); bloom.Tick(0.1f);
        check(bloom.ActiveProjectiles == 1, "Bloom respects projectile and detonation limits at spawn time");
        RunToEnd(bloom);
        check(detonations == 1, "Bloom detonation count cannot exceed the configured cap");
    }
}
