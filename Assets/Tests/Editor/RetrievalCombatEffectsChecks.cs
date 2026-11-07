using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Actual player/retrieval/damage events; disposable fixtures never change shared weapon stats.
public static class RetrievalCombatEffectsChecks
{
    const string Pending = "RetrievalCombatEffectsChecks.Pending";
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static readonly List<Object> owned = new List<Object>();
    static int checks, frame = -1; static bool running; static double deadline;
    static GameObject player; static RemotePartRetrieval retrieval; static RetrievalCombatEffects effects; static RetrievalBonusCooldowns gate;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; Debug.Log("RETRIEVAL COMBAT PASS: " + message); }
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only");
        foreach (string name in new[] { "Base/FullPlayer_mech_golem", "Base/FullPlayer_mech_heavy", "Specialist/FullPlayer_mech_specialist" })
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/" + name + ".prefab");
            Check(root.GetComponent<RemotePartRetrieval>().pickupRange == 20 && root.GetComponents<RetrievalBonusCooldowns>().Length == 1
                && root.GetComponent<RetrievalBonusCooldowns>().cooldownDuration == 30 && root.GetComponent<RetrievalBonusCooldowns>().bypassCooldownForTesting
                && root.GetComponents<RetrievalCombatEffects>().Length == 1,
                name + " authors 20m range, the retained 30s duration and enabled temporary bonus bypass");
            var catalogue = root.GetComponent<RetrievalCombatEffects>().Catalogue;
            Check(catalogue != null && catalogue.entries.Length == 6 && catalogue.entries[0].family == RetrievalWeaponFamily.Sword
                && catalogue.entries[1].family == RetrievalWeaponFamily.Mace && catalogue.entries.Take(2).All(e => e.effect == RetrievalEffectKind.ReturnPath && e.hitRadius == .75f && e.damageMultiplier == 1)
                && catalogue.entries[2].family == RetrievalWeaponFamily.Cannon && catalogue.entries[2].effect == RetrievalEffectKind.ArrivalBlast && catalogue.entries[2].hitRadius == 5
                && catalogue.entries[3].family == RetrievalWeaponFamily.ThrowingAxe && catalogue.entries[3].effect == RetrievalEffectKind.ArrivalBarrage && catalogue.entries[3].projectileCount == 24 && catalogue.entries[3].hitRadius == .4f,
                name + " connects sword/mace paths, cannon blast and the separate axe barrage");
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; deadline = EditorApplication.timeSinceStartup + 160; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Retrieval combat checks timed out");
            while (steps.Count > 0)
            {
                var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; } return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static IEnumerator Frames(int count = 3) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Complete(RemotePartRetrieval remote = null)
    {
        remote = remote != null ? remote : retrieval; double until = EditorApplication.timeSinceStartup + 8;
        while (remote.IsBusy || CombatHitStop.IsFrozen(remote.transform)) { if (EditorApplication.timeSinceStartup > until) throw new Exception("Flight/feedback did not finish"); yield return null; }
        yield return Frames();
    }
    static PartData Data(string name) => AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Arm_" + name + ".asset");
    static Part Drop(PartData data, Vector3? position = null)
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(root); root.SetActive(false);
        root.name = "Retrieval combat drop " + data.partId; root.transform.position = position ?? new Vector3(0, .6f, 12); root.transform.localScale = Vector3.one * .35f;
        var part = root.AddComponent<Part>(); part.data = data; part.isStartingPart = false; root.SetActive(true); root.GetComponent<Rigidbody>().isKinematic = true; return part;
    }
    static CombatHealth Dummy(Vector3 position, CombatTeam team = CombatTeam.Enemy, bool compound = false)
    {
        var root = new GameObject("Path health fixture " + team); owned.Add(root); root.transform.position = position;
        var collider = root.AddComponent<BoxCollider>(); collider.center = Vector3.up; collider.size = new Vector3(.7f, 2, .7f);
        if (compound) for (int i = 0; i < 4; i++)
        { var child = new GameObject("Overlapping hitbox " + i); child.transform.SetParent(root.transform, false); var box = child.AddComponent<BoxCollider>(); box.center = Vector3.up; box.size = new Vector3(.6f, 2, .6f); }
        var health = root.AddComponent<CombatHealth>(); health.Team = team; health.SetMaximum(500, true); return health;
    }
    static IEnumerator Run()
    {
        Time.timeScale = 1; Application.runInBackground = true;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor); floor.transform.position = Vector3.down * .25f; floor.transform.localScale = new Vector3(250, .5f, 100);
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath), Vector3.up * .1f, Quaternion.identity); owned.Add(player);
        yield return Frames(6); player.GetComponent<MechMovementController>().enabled = false; player.GetComponent<MechPlayerInput>().enabled = false;
        player.GetComponent<CombatHealth>().Team = CombatTeam.Player;
        retrieval = player.GetComponent<RemotePartRetrieval>(); effects = player.GetComponent<RetrievalCombatEffects>(); gate = player.GetComponent<RetrievalBonusCooldowns>();
        // Keep permanent cooldown regression coverage even while playtest prefabs enable the bypass.
        gate.bypassCooldownForTesting = false;
        retrieval.enableRemoteTargeting = false; retrieval.travelDuration = .3f;
        retrieval.weaponLiftDuration=retrieval.weaponHoverDuration=retrieval.weaponLiftHeight=0; retrieval.weaponFlightDuration=.3f;
        var atStart = Dummy(new Vector3(0, 0, 12), compound: true); var middle = Dummy(new Vector3(0, 0, 8), compound: true); var near = Dummy(new Vector3(0, 0, 4), compound: true);
        var friend = Dummy(new Vector3(0, 0, 6), CombatTeam.Player); var outside = Dummy(new Vector3(4, 0, 8));
        float ownerHealth = player.GetComponent<CombatHealth>().CurrentHealth;
        var sword = Drop(Data("Sword")); float readyAt = sword.ReadyAt; int ammo = sword.RemainingAmmo;
        int reported = 0; Action<Transform, CombatHealth, CombatHitKind> onHit = (owner, victim, kind) => { if (owner == player.transform && kind == CombatHitKind.Melee) reported++; };
        CombatVisualEvents.HitConfirmed += onHit;
        Physics.SyncTransforms();
        Check(retrieval.TryStart(sword), "Valid sword retrieval starts its path bonus");
        while(retrieval.IsBusy && !retrieval.HasDeparted) yield return null;
        Check(gate.Remaining(RetrievalWeaponFamily.Sword) > 29.8f && gate.Remaining(RetrievalWeaponFamily.Sword) <= 30 && effects.HasActivePath,
            "Sword bonus commits its 30s cooldown at departure, before arrival");
        Check(atStart.CurrentHealth == 400, "The starting overlap damages an enemy at the dropped sword once");
        yield return Complete();
        Check(atStart.CurrentHealth == 400 && middle.CurrentHealth == 400 && near.CurrentHealth == 400 && effects.LastPathHits == 3,
            "Actual curved travel hits three enemies once each despite many colliders and frames");
        Check(reported == 3, "Accepted path hits use existing combat feedback events exactly once per enemy");
        Check(friend.CurrentHealth == 500 && outside.CurrentHealth == 500 && player.GetComponent<CombatHealth>().CurrentHealth == ownerHealth,
            "Path damage excludes owner, friendlies and enemies outside its width");
        Check(sword.isEquipped && sword.ReadyAt == readyAt && sword.RemainingAmmo == ammo && !effects.HasActivePath,
            "Arrival equips normally without consuming ammo or resetting the weapon's normal cooldown");
        float healthBefore = middle.CurrentHealth;
        var copy = Drop(Data("Sword"));
        typeof(Part).GetMethod("Consume", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(copy, new object[] { 5f, false, 0f, 0 });
        float copyReadyAt = copy.ReadyAt;
        Check(retrieval.TryStart(copy), "Another sword remains retrievable while its family bonus cools down"); yield return Complete();
        Check(copy.isEquipped && middle.CurrentHealth == healthBefore && effects.LastPathHits == 0,
            "Different physical swords share the same player timer and cannot repeat bonus damage");
        Check(copy.ReadyAt == copyReadyAt && copy.CooldownRemaining > 0, "Retrieving a cooling weapon preserves its existing ordinary attack cooldown");
        var rolled = Object.Instantiate(Data("Sword")); owned.Add(rolled); rolled.partName = "Renamed rolled weapon";
        var rolledPart = Drop(rolled); Check(retrieval.TryStart(rolledPart), "A runtime PartData copy remains retrievable"); yield return Complete();
        Check(middle.CurrentHealth == healthBefore && gate.Remaining(RetrievalWeaponFamily.Sword) > 25,
            "Canonical part ID preserves family cooldown across renamed/stat-roll data copies");
        var mace = Drop(Data("Mace")); Check(retrieval.TryStart(mace), "Mace can trigger while swords are on cooldown"); yield return Complete();
        Check(middle.CurrentHealth == healthBefore - 70 && gate.Remaining(RetrievalWeaponFamily.Mace) > 29 && gate.Remaining(RetrievalWeaponFamily.Sword) > 0,
            "Mace deals its own normal damage and starts an independent 30s family timer");
        float swordRemaining = gate.Remaining(RetrievalWeaponFamily.Sword);
        effects.enabled = false; gate.enabled = false; effects.enabled = true; gate.enabled = true;
        Check(gate.Remaining(RetrievalWeaponFamily.Sword) > swordRemaining - .1f && gate.Remaining(RetrievalWeaponFamily.Mace) > 0,
            "Disabling/re-enabling the listener or gate does not clear either cooldown");
        var ordinary = Drop(Data("Sword")); Check(player.GetComponent<EquipManager>().TryEquip(ordinary, PartCategory.ArmL), "Ordinary nearby equipment transaction remains available during cooldown");
        Check(middle.CurrentHealth == healthBefore - 70 && gate.Remaining(RetrievalWeaponFamily.Sword) > 0,
            "Ordinary equip does not grant a retrieval attack or reset the bonus timer");
        var unknown = Object.Instantiate(Data("Sword")); owned.Add(unknown); unknown.partId = "unconfigured-melee-test";
        var unknownPart = Drop(unknown); Check(retrieval.TryStart(unknownPart), "Unconfigured melee equipment is still retrievable"); yield return Complete();
        Check(effects.LastPathHits == 0 && middle.CurrentHealth == healthBefore - 70,
            "Broad melee subtype alone cannot accidentally grant a sword/mace bonus");
        CombatVisualEvents.HitConfirmed -= onHit;
        yield return OtherPlayerAndCancellation();
        yield return GateSemantics();
        yield return BypassTesting();
        SweptDamageRules();
        foreach (var item in owned) if (item != null) Object.Destroy(item); yield return Frames(4);
    }
    static IEnumerator OtherPlayerAndCancellation()
    {
        var other = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath), new Vector3(30, .1f, 0), Quaternion.identity); owned.Add(other);
        yield return Frames(4); other.GetComponent<MechMovementController>().enabled = false; other.GetComponent<MechPlayerInput>().enabled = false;
        var remote = other.GetComponent<RemotePartRetrieval>(); remote.enableRemoteTargeting = false;
        remote.weaponLiftDuration=remote.weaponHoverDuration=remote.weaponLiftHeight=0; remote.weaponFlightDuration=.3f;
        var otherGate = other.GetComponent<RetrievalBonusCooldowns>();
        otherGate.bypassCooldownForTesting = false;
        Check(otherGate.IsReady(RetrievalWeaponFamily.Sword) && !gate.IsReady(RetrievalWeaponFamily.Sword), "Bonus timers belong to individual players, not global/static weapon data");
        var tooFar = Drop(Data("Sword"), new Vector3(30, .6f, 24));
        Check(!remote.TryStart(tooFar) && otherGate.IsReady(RetrievalWeaponFamily.Sword), "Rejected out-of-range request consumes no bonus cooldown");
        tooFar.transform.position = new Vector3(30, .6f, 12); Physics.SyncTransforms();
        Check(remote.TryStart(tooFar), "Fresh player's valid sword starts after the rejected request");
        while(remote.IsBusy && !remote.HasDeparted) yield return null;
        remote.Cancel();
        Check(!tooFar.IsRetrieving && !tooFar.isEquipped && otherGate.Remaining(RetrievalWeaponFamily.Sword) > 29.8f,
            "Cancelling after departure releases the item but retains the committed attack cooldown");
        var retry = Drop(Data("Sword"), new Vector3(30, .6f, 12)); Check(remote.TryStart(retry), "Cancelled family still permits ordinary re-retrieval"); yield return Complete(remote);
        Check(retry.isEquipped && other.GetComponent<RetrievalCombatEffects>().LastPathHits == 0, "Retry during cancelled bonus cooldown equips with no second attack");
        var amplified = Dummy(new Vector3(30, 0, 12)); other.GetComponent<MechCombat>().DamageModifiers.Set("test existing amp", 1.5f);
        var mace = Drop(Data("Mace"), new Vector3(30, .6f, 12)); Check(remote.TryStart(mace), "Second family's departure bonus starts independently");
        while(remote.IsBusy && !remote.HasDeparted) yield return null;
        Check(amplified.CurrentHealth == 395 && Data("Mace").GetStat(StatType.Damage) == 70, "Retrieval damage respects the existing damage multiplier without changing shared weapon stats");
        other.GetComponent<RetrievalCombatEffects>().enabled = false; remote.Cancel();
        Check(otherGate.Remaining(RetrievalWeaponFamily.Mace) > 29.8f, "Disabling effects during an active path cannot refund its cooldown");
    }
    static IEnumerator GateSemantics()
    {
        var root = new GameObject("Standalone family gate fixture"); owned.Add(root); var pool = root.AddComponent<RetrievalBonusCooldowns>();
        object a = new object(), b = new object(); var family = RetrievalWeaponFamily.Cannon;
        Check(pool.TryReserve(family, a) && !pool.TryReserve(family, b), "Only one pending token can reserve a family bonus");
        Check(!pool.TryCommit(family, b), "Wrong reservation token cannot trigger a bonus"); pool.Release(family, b);
        Check(pool.IsReserved(family), "Wrong token cannot release another pending bonus"); pool.Release(family, a);
        Check(pool.IsReady(family) && pool.Remaining(family) == 0, "Cancellation before trigger releases the pending bonus without starting 30s");
        Check(pool.TryReserve(family, b) && pool.TryCommit(family, b), "Correct token commits the family timer"); pool.Release(family, b);
        Check(!pool.IsReserved(family) && pool.Remaining(family) > 29.8f && !pool.TryCommit(family, b), "Commit consumes its token once and release cannot erase an active timer");
        Check(pool.IsReady(RetrievalWeaponFamily.ThrowingAxe) && pool.IsReady(RetrievalWeaponFamily.SmallCrossbow) && pool.IsReady(RetrievalWeaponFamily.LargeCrossbow),
            "Future axe/small-crossbow/large-crossbow families have separate gates");
        pool.cooldownDuration = .08f; object shortToken = new object();
        Check(pool.TryReserve(RetrievalWeaponFamily.Sword, shortToken) && pool.TryCommit(RetrievalWeaponFamily.Sword, shortToken), "Short fixture cooldown uses the same actual gate clock");
        Time.timeScale = 0; float pausedRemaining = pool.Remaining(RetrievalWeaponFamily.Sword); yield return Frames(8);
        Check(Mathf.Abs(pool.Remaining(RetrievalWeaponFamily.Sword) - pausedRemaining) < .001f, "Pause holds the bonus cooldown clock"); Time.timeScale = 1;
        while (pool.Remaining(RetrievalWeaponFamily.Sword) > 0) yield return null;
        Check(pool.TryReserve(RetrievalWeaponFamily.Sword, new object()), "Elapsed gameplay cooldown becomes eligible again");
    }
    static IEnumerator BypassTesting()
    {
        var actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath), new Vector3(60, .1f, 0), Quaternion.identity); owned.Add(actor);
        yield return Frames(5); actor.GetComponent<MechMovementController>().enabled = false; actor.GetComponent<MechPlayerInput>().enabled = false;
        var remote = actor.GetComponent<RemotePartRetrieval>(); remote.enableRemoteTargeting = false;
        remote.weaponLiftDuration = remote.weaponHoverDuration = remote.weaponLiftHeight = 0; remote.weaponFlightDuration = .08f;
        var pool = actor.GetComponent<RetrievalBonusCooldowns>(); var bonuses = actor.GetComponent<RetrievalCombatEffects>();
        var far = Dummy(new Vector3(60, 0, 12), compound: true); var near = Dummy(new Vector3(60, 0, 4), compound: true);
        far.SetMaximum(2000, true); near.SetMaximum(2000, true);
        Check(pool.bypassCooldownForTesting && pool.cooldownDuration == 30, "Spawned player enables temporary bypass while retaining normal 30s tuning");
        float expected = 2000;
        foreach (var name in new[] { "Sword", "Mace" })
        {
            var data = Data(name); var family = name == "Sword" ? RetrievalWeaponFamily.Sword : RetrievalWeaponFamily.Mace;
            for (int i = 0; i < 2; i++)
            {
                var drop = Drop(data, new Vector3(60, .6f, 12));
                Check(remote.TryStart(drop), name + " bypass copy " + (i + 1) + " begins without a cooldown wait"); yield return Complete(remote);
                expected -= data.GetStat(StatType.Damage);
                Check(far.CurrentHealth == expected && near.CurrentHealth == expected && pool.Remaining(family) == 0,
                    name + " bypass copy " + (i + 1) + " deals one fresh return hit per target");
            }
        }
        var cannonData = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Shoulder_Cannon.asset");
        for (int i = 0; i < 2; i++)
        {
            var cannon = Drop(cannonData, new Vector3(60, .6f, 12));
            Check(remote.TryStart(cannon), "Cannon bypass copy " + (i + 1) + " begins without a cooldown wait"); yield return Complete(remote);
            Check(near.CurrentHealth == expected - (i + 1) * cannonData.GetStat(StatType.Damage) && far.CurrentHealth == expected && bonuses.BonusBlastCount == i + 1,
                "Cannon bypass copy " + (i + 1) + " triggers another arrival blast without normal ammo/cooldown changes");
        }
        object first = new object(), second = new object();
        Check(pool.TryReserve(RetrievalWeaponFamily.Cannon, first) && !pool.TryReserve(RetrievalWeaponFamily.Cannon, second), "Bypass preserves exclusive pending reservations");
        pool.Release(RetrievalWeaponFamily.Cannon, first);
        pool.bypassCooldownForTesting = false;
        Check(pool.Remaining(RetrievalWeaponFamily.Cannon) > 29 && pool.Remaining(RetrievalWeaponFamily.Sword) > 20,
            "Switching bypass off immediately restores stored family cooldowns");
        var cooling = Drop(Data("Sword"), new Vector3(60, .6f, 12)); Check(remote.TryStart(cooling), "Normal retrieval still works after restoring the gate"); yield return Complete(remote);
        Check(far.CurrentHealth == expected && bonuses.LastPathHits == 0, "Restored family gate suppresses repeated damage again");
        pool.bypassCooldownForTesting = true;
        Check(pool.IsReady(RetrievalWeaponFamily.Sword), "Bypass can be enabled live over an existing timer");
    }
    static void SweptDamageRules()
    {
        var root = new GameObject("Sweep owner"); owned.Add(root); root.transform.position = new Vector3(100, 0, 0); root.AddComponent<CombatHealth>().Team = CombatTeam.Player;
        var target = Dummy(new Vector3(100, 0, 7), compound: true); var friend = Dummy(new Vector3(100, 0, 4), CombatTeam.Player);
        var shielded = Dummy(new Vector3(100.65f, 0, 10));
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(wall); wall.transform.position = new Vector3(100.2f, 1, 10); wall.transform.localScale = new Vector3(.1f, 3, 3);
        Physics.SyncTransforms(); var blast = new CombatBlast(); var history = new HashSet<CombatHealth>();
        LayerMask layers = Part.MovementQueryMask;
        var result = blast.ResolveCapsule(new Vector3(100, 1, 14), new Vector3(100, 1, 0), .75f, 100, root.transform, layers, history);
        Check(target.CurrentHealth == 400 && result.Hits == 1, "One large swept segment catches a thin target between frames without tunnelling");
        Check(shielded.CurrentHealth == 500 && friend.CurrentHealth == 500, "Swept width still respects side walls and team rules");
        blast.ResolveCapsule(new Vector3(100, 1, 14), new Vector3(100, 1, 0), .75f, 100, root.transform, layers, history);
        Check(target.CurrentHealth == 400, "The same hit history prevents repeats across overlapping or reversed segments");
    }
    static void Finish(Exception error)
    {
        PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending, false); Time.timeScale = 1;
        if (error != null) Debug.LogException(error);
        Debug.Log("RETRIEVAL COMBAT CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
}
