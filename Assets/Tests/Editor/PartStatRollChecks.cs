using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Uses real PartData assets, dropped prefabs, equipment, combat and comparison formatting.
public static class PartStatRollChecks
{
    const string Pending = "PartStatRollChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static int checks, frame = -1;
    static bool running;
    static double deadline;
    static GameObject player;
    static EquipManager equipment;
    static MechCombat combat;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("PART ROLL PASS: " + message); }
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .001f;
    static float Value(object target, string name) => (float)target.GetType().GetField(name, Private).GetValue(target);
    static bool Same(PartData a, PartData b) => a.stats.Count == b.stats.Count && a.stats.Zip(b.stats, (x, y) => x.type == y.type && x.value == y.value).All(v => v);
    static bool Bounded(PartData data, PartData rolled)
    {
        if (data.stats.Count != rolled.stats.Count) return false;
        for (int i = 0; i < data.stats.Count; i++)
        {
            var before = data.stats[i]; var after = rolled.stats[i];
            double lower = Math.Min(before.value * .9d, before.value * 1.1d), upper = Math.Max(before.value * .9d, before.value * 1.1d);
            double tolerance = Math.Max(1e-6, Math.Abs(before.value) * 1e-6);
            if (before.type != after.type || after.value < lower - tolerance || after.value > upper + tolerance) return false;
        }
        return true;
    }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; deadline = EditorApplication.timeSinceStartup + 180; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Item stat roll checks timed out.");
            while (steps.Count > 0) { var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; } if (step.Current is IEnumerator child) { steps.Push(child); continue; } return; }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit(); Time.captureFramerate = 0;
        if (error != null) Debug.LogException(error);
        Debug.Log("PART STAT ROLL CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
    static IEnumerator Frames(int count = 3) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.captureFramerate = 60;
        Distribution(); Catalogue();
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = Vector3.down * .25f; floor.transform.localScale = new Vector3(200, .5f, 200);
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath), new Vector3(0, 1.6f, 0), Quaternion.identity);
        player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false;
        player.GetComponent<MechTargetLockController>().EnterFreeAim();
        var feedback = player.GetComponent<CombatFeedback>(); feedback.enabled = false;
        equipment = player.GetComponent<EquipManager>(); combat = player.GetComponent<MechCombat>();
        yield return Frames(5);
        Check(player.GetComponentsInChildren<Part>().Where(p => p.isEquipped).All(p => !p.HasRolledStats && p.Stats == p.data), "Starting gear retains its authored stats and identity");
        var starter = equipment.Unequip(PartCategory.ArmR);
        yield return Frames();
        Check(!starter.HasRolledStats && !starter.RollLootStats(99) && starter.Stats == starter.data, "Discarding starting gear cannot turn it into a reroll exploit");
        Check(equipment.TryEquip(starter, PartCategory.ArmR), "Fixed starting gear remains re-equippable");
        yield return WorldLoot();
        yield return EnemyLootAndCombat();
        yield return HealthMovementAndAbility();
    }
    static void Distribution()
    {
        var data = ScriptableObject.CreateInstance<PartData>(); data.stats = new List<StatEntry>();
        foreach (StatType type in Enum.GetValues(typeof(StatType))) data.stats.Add(new StatEntry { type = type, value = type == StatType.MoveSpeed ? -20 : type == StatType.MagSize ? 1 : type == StatType.Reload ? 0 : 100 });
        string original = EditorJsonUtility.ToJson(data);
        float low = float.MaxValue, high = float.MinValue; double sum = 0; bool bounds = true, counts = true, signs = true, zeros = true, independent = false;
        var randomState = UnityEngine.Random.state;
        for (int seed = 0; seed < 1000; seed++)
        {
            var roll = PartStatRoll.Create(data, seed);
            bounds &= Bounded(data, roll);
            float damage = roll.GetStat(StatType.Damage), rate = roll.GetStat(StatType.FireRate);
            low = Mathf.Min(low, damage); high = Mathf.Max(high, damage); sum += damage;
            counts &= roll.GetStat(StatType.MagSize) == 1 && roll.GetStat(StatType.Ammo) == Mathf.Round(roll.GetStat(StatType.Ammo));
            signs &= roll.GetStat(StatType.MoveSpeed) < 0;
            zeros &= roll.GetStat(StatType.Reload) == 0;
            independent |= Mathf.Abs(damage - rate) > 1;
            Object.DestroyImmediate(roll);
        }
        Check(bounds, "All stat types remain within -10% to +10% across 1,000 seeded rolls");
        Check(low < 91 && high > 109 && Math.Abs(sum / 1000 - 100) < 1, "Distribution reaches both ends of the range without a systematic quality bias");
        Check(independent, "Stats roll independently rather than sharing one item-wide multiplier");
        Check(counts, "Ammo stays integral and one-round magazines stay at one");
        Check(signs && zeros, "Signed movement penalties and zero/off values retain their semantics");
        Check(UnityEngine.Random.state.Equals(randomState), "Seeded stat generation does not consume global combat RNG per stat");
        Check(EditorJsonUtility.ToJson(data) == original, "Distribution never mutates the shared definition");
        var a = PartStatRoll.Create(data, 12345); var b = PartStatRoll.Create(data, 12345); var c = PartStatRoll.Create(data, 54321);
        Check(a != b && a.stats != b.stats && Same(a, b) && !Same(a, c), "Same seeds reproduce values in separate instances; different seeds produce new rolls");
        a.stats[0] = new StatEntry { type = StatType.Health, value = 1 };
        Check(b.stats[0].value != 1 && data.stats[0].value == 100, "Editing one rolled instance cannot affect another or its source");
        Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(c); Object.DestroyImmediate(data);
    }
    static void Catalogue()
    {
        foreach (string guid in AssetDatabase.FindAssets("t:PartData", new[] { "Assets/PartCatalogue" }))
        {
            var source = AssetDatabase.LoadAssetAtPath<PartData>(AssetDatabase.GUIDToAssetPath(guid)); var original = EditorJsonUtility.ToJson(source);
            var rolled = PartStatRoll.Create(source, 76543);
            Check(Bounded(source, rolled) && source.partId == rolled.partId && source.partType == rolled.partType && source.subtype == rolled.subtype
                && source.partName == rolled.partName && source.icon == rolled.icon && source.projectilePrefab == rolled.projectilePrefab
                && source.ability == rolled.ability && source.abilityVfx == rolled.abilityVfx && EditorJsonUtility.ToJson(source) == original,
                source.name + " rolls every authored stat while preserving metadata and the source asset");
            Object.DestroyImmediate(rolled);
        }
    }
    static Part Spawn(string path, Vector3 position) => Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), position, Quaternion.identity).GetComponent<Part>();
    static IEnumerator WorldLoot()
    {
        string[] paths = { SpecialistPlayerIntegrationSetup.CannonPath, "Assets/Prefabs/Mech/Base/Parts/arm_sword.prefab", "Assets/Prefabs/Mech/Base/Parts/head_base.prefab", "Assets/Prefabs/Mech/Base/Parts/Old/torso_base.prefab", "Assets/Prefabs/Mech/Specialist/BipedSpecialist_Module.prefab" };
        foreach (string path in paths)
        {
            var part = Spawn(path, new Vector3(30, 1, 0)); yield return Frames();
            Check(part.HasRolledStats && part.Stats != part.data && Bounded(part.data, part.Stats), "World pickup initializes a private roll: " + part.name);
            var stats = part.Stats; int seed = part.StatRollSeed; var state = UnityEngine.Random.state;
            Check(!part.RollLootStats() && !part.RollLootStats(4444) && part.Stats == stats && UnityEngine.Random.state.Equals(state), "Existing item cannot be rerolled: " + part.name);
            part.gameObject.SetActive(false); part.gameObject.SetActive(true); yield return Frames();
            Check(part.Stats == stats && part.StatRollSeed == seed, "Visibility/pooling preserves this physical item's roll: " + part.name);
            Object.Destroy(part.gameObject); yield return Frames();
            Check(stats == null, "Destroying a pickup releases its private runtime stat object");
        }
        var first = Spawn(paths[1], new Vector3(30, 1, 0)); var second = Spawn(paths[1], new Vector3(32, 1, 0));
        first.RollLootStats(111); second.RollLootStats(222);
        Check(first.data == second.data && first.Stats != second.Stats && !Same(first.Stats, second.Stats), "Two copies of one weapon have independent private stats");
        PartSwapDisplay.BasicStats(first, out string basic); PartSwapDisplay.Comparison(second, first, out string comparison);
        string damage = first.Stats.GetStat(StatType.Damage).ToString("0.##", CultureInfo.InvariantCulture);
        Check(basic.Contains("Damage " + damage) && comparison.Contains("Damage ") && !comparison.Contains("++"), "Existing comparison UI reads the actual rolled numbers");
        Object.Destroy(first.gameObject); Object.Destroy(second.gameObject); yield return Frames();
    }
    static IEnumerator EnemyLootAndCombat()
    {
        var gate = new GameObject("Controlled rolled loot enemy"); gate.SetActive(false);
        var enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(FodderModelSetup.FodderPath), gate.transform);
        enemy.transform.position = new Vector3(40, 1, 0);
        enemy.GetComponent<EnemyAgent>().enabled = false; enemy.GetComponent<EnemyAttack>().enabled = false;
        enemy.GetComponent<EnemyGraphController>().enabled = false; enemy.GetComponent<Unity.Behavior.BehaviorGraphAgent>().enabled = false;
        var drop = enemy.GetComponent<EnemyPartDrop>(); var tuning = new SerializedObject(drop); tuning.FindProperty("dropChancePercent").floatValue = 100; tuning.ApplyModifiedPropertiesWithoutUndo();
        gate.SetActive(true); yield return Frames();
        Check(enemy.GetComponentsInChildren<Part>().All(p => !p.HasRolledStats && p.Stats == p.data), "Loot randomness does not rebalance enemy-carried weapon attacks");
        var planned = drop.PlannedDrop; enemy.GetComponent<CombatHealth>().ApplyDamage(10000);
        var loot = drop.LastDrop;
        Check(loot != null && loot.HasRolledStats && loot.data == planned.data && Bounded(loot.data, loot.Stats), "Enemy death exposes a rolled item immediately, retaining the gold marker's promised weapon identity");
        Check(drop.ResolveDeathDrop() == loot, "Repeated death callbacks cannot spawn or reroll another item");
        var stats = loot.Stats; int seed = loot.StatRollSeed;
        var old = equipment.GetEquipped(PartCategory.ShoulderR);
        Check(equipment.TryEquip(loot, PartCategory.ShoulderR) && loot.Stats == stats, "Same-frame pickup equips the enemy drop's existing rolled stats");
        if (old != null) Object.Destroy(old.gameObject);
        int ammo = Mathf.RoundToInt(stats.GetStat(StatType.Ammo));
        Check(loot.RemainingAmmo == ammo && loot.MagazineRounds == stats.MagazineSize, "Ammo and magazine initialize from the rolled whole-number capacities");
        var shot = combat.TryAttack(PartCategory.ShoulderR).Projectile;
        Check(shot != null && Near(Value(shot, "damage"), stats.GetStat(StatType.Damage) * combat.DamageModifiers.Total)
            && Near(Value(shot, "speed"), stats.GetStat(StatType.ProjectileSpeed)) && Near(Value(shot, "explosionRadius"), stats.GetStat(StatType.SplashRadius)),
            "Actual fired projectile uses rolled damage, speed and splash radius");
        if (shot != null) Object.Destroy(shot.gameObject);
        float ready = loot.ReadyAt; int remaining = loot.RemainingAmmo;
        Check(Near(loot.CooldownDuration, Mathf.Max(stats.ShotInterval, stats.GetStat(StatType.Reload))), "Reload and fire interval use this item's rolled timing");
        for (int cycle = 0; cycle < 5; cycle++)
        {
            var slot = loot.Slot.Value; equipment.Unequip(slot);
            Check(!loot.RollLootStats(9876) && equipment.TryEquip(loot, cycle % 2 == 0 ? PartCategory.ShoulderL : PartCategory.ShoulderR)
                && loot.Stats == stats && loot.StatRollSeed == seed && loot.RemainingAmmo == remaining && loot.ReadyAt == ready,
                "Equip/drop cycle retains stats, seed, ammunition and cooldown, cycle " + cycle);
        }
        Object.Destroy(gate); foreach (var corpse in Object.FindObjectsByType<CombatRagdoll>()) Object.Destroy(corpse.gameObject);
        yield return Frames();

        var sword = Spawn("Assets/Prefabs/Mech/Base/Parts/arm_sword.prefab", new Vector3(30, 1, 0)); sword.RollLootStats(2468);
        var oldSword = equipment.GetEquipped(PartCategory.ArmL); Check(equipment.TryEquip(sword, PartCategory.ArmL), "Rolled melee weapon equips normally");
        if (oldSword != null) Object.Destroy(oldSword.gameObject);
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube); target.transform.position = player.transform.position + Vector3.forward * 3;
        var health = target.AddComponent<CombatHealth>(); health.Team = CombatTeam.Enemy; health.SetMaximum(1000, true); Physics.SyncTransforms();
        float before = health.CurrentHealth;
        Check(combat.TryAttack(PartCategory.ArmL).Started && Near(before - health.CurrentHealth, sword.Stats.GetStat(StatType.Damage) * combat.DamageModifiers.Total), "Actual melee hit uses this sword's rolled damage");
        var animation = sword.GetComponent<MechWeaponAnimator>(); animation.Tick(sword.Stats.ShotInterval * .5f);
        Check(animation.CurrentPose == MechWeaponAnimator.PoseState.Fire && Near(animation.NormalizedTime, .5f), "Melee animation speed follows the rolled attack rate");
        Object.Destroy(target); yield return Frames();
    }
    static IEnumerator HealthMovementAndAbility()
    {
        var head = Spawn("Assets/Prefabs/Mech/Base/Parts/head_base.prefab", new Vector3(30, 1, 0));
        var legs = Spawn("Assets/Prefabs/Mech/Specialist/BipedSpecialist_Module.prefab", new Vector3(32, 1, 0));
        yield return Frames();
        var oldHead = equipment.GetEquipped(PartCategory.Head); var oldLegs = equipment.GetEquipped(PartCategory.Legs);
        Check(equipment.TryEquip(head, PartCategory.Head) && equipment.TryEquip(legs, PartCategory.Legs), "Rolled head and legs use the existing slots and locomotion path");
        if (oldHead != null) Object.Destroy(oldHead.gameObject); if (oldLegs != null) Object.Destroy(oldLegs.gameObject);
        var aggregate = player.GetComponent<MechStats>();
        float total = player.GetComponentsInChildren<Part>().Where(p => p.isEquipped).Sum(p => Mathf.Max(0, p.Stats.GetStat(StatType.Health)));
        Check(Near(aggregate.MaxHealth, total) && Near(player.GetComponent<CombatHealth>().MaximumHealth, total), "Health totals use the individual rolled part values");
        var movement = player.GetComponent<MechMovementController>();
        Check(Near(movement.WalkSpeed, aggregate.BaseSpeed * aggregate.SpeedMultiplier) && Near(aggregate.BaseSpeed, legs.Stats.GetStat(StatType.MoveSpeed)), "Movement uses rolled leg speed and signed equipment modifiers");
        float duration = -1; var abilities = player.GetComponent<MechAbilities>();
        abilities.AbilityStarted += (slot, part, seconds) => { if (part == head) duration = seconds; };
        Check(abilities.Request(PartCategory.Head, head) && Near(PartAbilityCooldown.For(head).Duration, head.Stats.GetStat(StatType.AbilityCooldown))
            && Near(duration, head.Stats.GetStat(StatType.AbilityDuration)), "Abilities consume rolled cooldown and duration rather than shared defaults");
        var rolled = head.Stats; var definition = head.data;
        var replacementData = Object.Instantiate(definition); head.data = replacementData;
        Check(!head.HasRolledStats && head.Stats == replacementData, "Replacing an item's definition never exposes stale rolled values from another definition");
        Object.Destroy(player); yield return Frames();
        Check(rolled == null && definition != null, "Player destruction frees rolled data and leaves the base asset intact");
        Object.Destroy(replacementData);
    }
}
