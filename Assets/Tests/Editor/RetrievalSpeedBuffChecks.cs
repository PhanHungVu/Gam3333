using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

// Real player, pickup entry, weapon clocks and motor; never edits shared PartData or production scenes.
public static class RetrievalSpeedBuffChecks
{
    const string Pending = "RetrievalSpeedBuffChecks.Pending";
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static readonly List<Object> owned = new List<Object>();
    static int checks, frame = -1;
    static bool running;
    static double deadline;
    static GameObject player;
    static RemotePartRetrieval remote;
    static RetrievalCombatEffects effects;
    static RetrievalBonusCooldowns gate;
    static RetrievalSpeedBuff buff;
    static MechMovementController motor;
    static MechCombat combat;
    static EquipManager equipment;
    static Camera camera;

    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    static void Check(bool valid, string message)
    {
        if (!valid) throw new Exception(message);
        checks++; Debug.Log("RETRIEVAL SPEED PASS: " + message);
    }
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new Exception("Disposable project only");
        foreach (var name in new[] { "Base/FullPlayer_mech_golem", "Base/FullPlayer_mech_heavy", "Specialist/FullPlayer_mech_specialist" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/" + name + ".prefab");
            Check(prefab.GetComponents<RetrievalSpeedBuff>().Length == 1 && prefab.GetComponent<RetrievalSpeedBuff>().enabled,
                name + " authors one enabled player-owned speed bonus");
            var catalogue = prefab.GetComponent<RetrievalCombatEffects>().Catalogue;
            var entry = catalogue.entries.Single(e => e.family == RetrievalWeaponFamily.SmallCrossbow);
            Check(catalogue.entries.Length == 6 && entry.part.partId == "weaponB2" && entry.effect == RetrievalEffectKind.ArrivalSpeedBuff
                && entry.buffDuration == 3 && entry.movementMultiplier == 1.5f && entry.attackCadenceMultiplier == 1.25f && entry.damageMultiplier == 0,
                name + " maps canonical small crossbow to a damage-independent 3s, 1.5x/1.25x buff");
            Check(prefab.GetComponent<RemotePartRetrieval>().pickupRange == 20 && prefab.GetComponent<RetrievalBonusCooldowns>().bypassCooldownForTesting,
                name + " preserves requested range and temporary family cooldown bypass");
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; deadline = EditorApplication.timeSinceStartup + 240; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Retrieval speed checks timed out");
            while (steps.Count > 0)
            {
                var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator child) { steps.Push(child); continue; } return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static IEnumerator Frames(int count = 3) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Wait(float duration)
    {
        double until = Time.timeAsDouble + duration;
        while (Time.timeAsDouble < until) yield return null;
    }
    static IEnumerator Complete()
    {
        double until = EditorApplication.timeSinceStartup + 8;
        while (remote.IsBusy || CombatHitStop.IsFrozen(player.transform))
        { if (EditorApplication.timeSinceStartup > until) throw new Exception("Retrieval did not finish"); yield return null; }
        yield return Frames();
    }
    static IEnumerator Player(float x)
    {
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath), new Vector3(x, .1f, 0), Quaternion.identity);
        owned.Add(player); yield return Frames(6);
        player.GetComponent<MechPlayerInput>().enabled = false;
        player.GetComponent<CombatHealth>().Team = CombatTeam.Player;
        remote = player.GetComponent<RemotePartRetrieval>(); effects = player.GetComponent<RetrievalCombatEffects>(); gate = player.GetComponent<RetrievalBonusCooldowns>();
        buff = player.GetComponent<RetrievalSpeedBuff>(); motor = player.GetComponent<MechMovementController>(); combat = player.GetComponent<MechCombat>(); equipment = player.GetComponent<EquipManager>();
        remote.enableRemoteTargeting = false;
        camera = player.GetComponent<MechPlayerRig>().GameplayCamera;
        camera.GetComponent<CameraControlThirdPerson>().enabled = false; camera.GetComponent<Unity.Cinemachine.CinemachineBrain>().enabled = false;
        combat.BindCamera(camera);
        Check(!buff.Active && buff.MovementMultiplier == 1 && combat.AttackCadenceMultiplier == 1, "New player begins with neutral multipliers");
    }
    static Part Drop(string file = "arm_crossbow", float distance = 18)
    {
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/" + file + ".prefab")); owned.Add(root);
        var part = root.GetComponent<Part>(); part.isStartingPart = false;
        root.transform.position = player.transform.position + new Vector3(0, .6f, distance);
        root.GetComponent<Rigidbody>().isKinematic = true;
        Physics.SyncTransforms(); return part;
    }
    static void Consume(Part part, float interval, bool ammunition, float reload = 0, int magazine = 0)
        => typeof(Part).GetMethod("Consume", Hidden).Invoke(part, new object[] { interval, ammunition, reload, magazine });
    static void ResetMotor(Vector3 position)
    {
        motor.enabled = false;
        var controller = player.GetComponent<CharacterController>(); controller.enabled = false;
        player.transform.SetPositionAndRotation(position, Quaternion.identity); controller.enabled = true; motor.enabled = true;
        Physics.SyncTransforms();
    }
    static float MoveSpeed(Vector3 position, bool dash)
    {
        ResetMotor(position);
        for (int i = 0; i < (dash ? 5 : 60); i++) motor.Tick(new MechLocomotionInput { move = Vector2.up, quickBoost = dash && i == 0 }, null, .02f);
        return dash ? Vector3.ProjectOnPlane(player.transform.position - position, Vector3.up).magnitude : Vector3.ProjectOnPlane(motor.Velocity, Vector3.up).magnitude;
    }
    static IEnumerator Run()
    {
        Time.timeScale = 1; Time.maximumDeltaTime = .05f; Application.runInBackground = true;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor);
        floor.transform.position = Vector3.down * .25f; floor.transform.localScale = new Vector3(550, .5f, 200);
        yield return Player(0);
        var part = Drop(); int beforeAmmo = Mathf.RoundToInt(part.Stats.GetStat(StatType.Ammo));
        int shake = player.GetComponent<RetrievalFeedback>().ArrivalImpulseCount;
        camera.transform.SetPositionAndRotation(player.transform.position + new Vector3(0, 4, -8), Quaternion.LookRotation(part.PickupPoint - player.transform.position - new Vector3(0, 4, -8)));
        remote.enableRemoteTargeting = true; remote.RefreshTarget();
        var input = player.GetComponent<MechPlayerInput>(); input.enabled = true;
        bool accepted = remote.Target == part && input.RequestPickup();
        Check(accepted, "Actual F command retrieves the small crossbow at 18m");
        remote.enableRemoteTargeting = false;
        Check(gate.IsReserved(RetrievalWeaponFamily.SmallCrossbow) && !buff.Active, "Crossbow reserves the bonus during anticipation without applying it early");
        while (remote.IsBusy && !remote.HasDeparted) yield return null;
        Check(!buff.Active && gate.IsReserved(RetrievalWeaponFamily.SmallCrossbow), "Fast departure keeps arrival buff pending");
        yield return Complete();
        input.enabled = false;
        Check(part.isEquipped && part.EquippedBy == equipment && buff.Active && buff.ActivationCount == 1 && buff.Remaining > 2.7f,
            "Successful equip starts one full three-second buff");
        Check(buff.MovementMultiplier == 1.5f && combat.AttackCadenceMultiplier == 1.25f && part.RemainingAmmo == beforeAmmo,
            "Arrival applies configured multipliers without consuming crossbow ammo");
        Check(effects.BonusBlastCount == 0 && effects.BonusBarrageCount == 0 && effects.LastPathHits == 0
            && player.GetComponent<RetrievalFeedback>().ArrivalImpulseCount == shake + 1, "Crossbow grants no damage bonus and retains one arrival shake");
        Time.timeScale = 0; float left = buff.Remaining;
        yield return Frames(6);
        Check(buff.Remaining == left, "Pause freezes buff duration");
        float expected = player.GetComponent<MechStats>().BaseSpeed * player.GetComponent<MechStats>().SpeedMultiplier;
        Check(Mathf.Abs(motor.WalkSpeed - expected * 1.5f) < .001f, "Normal movement bonus composes with actual equipped part speed stats");
        Vector3 origin = player.transform.position;
        float fastWalk = MoveSpeed(origin, false), fastDash = MoveSpeed(origin, true), dashRecovery = motor.QuickBoostCooldownRemaining;
        buff.Clear();
        float walk = MoveSpeed(origin, false), dash = MoveSpeed(origin, true);
        Check(Mathf.Abs(fastWalk / walk - 1.5f) < .02f, "Actual collision-aware walking reaches 1.5x normal speed");
        Check(Mathf.Abs(fastDash - dash) < .001f && Mathf.Abs(motor.QuickBoostCooldownRemaining - dashRecovery) < .001f,
            "Committed dash burst and recovery timer are unchanged by the buff");
        ResetMotor(origin);
        Check(buff.Apply(3, 1.5f, 1.25f), "Timing fixture reapplies the same non-stacking bonus");
        WeaponTiming(part);
        Time.timeScale = 1; yield return Wait(.6f);
        var copy = Drop(); Check(remote.TryStart(copy), "Bypass permits another real crossbow pull for refresh testing"); yield return Complete();
        Check(buff.Remaining > 2.7f && buff.MovementMultiplier == 1.5f && combat.AttackCadenceMultiplier == 1.25f,
            "Another copy refreshes duration without multiplying movement or attack speed");
        gate.bypassCooldownForTesting = false;
        float remaining = buff.Remaining; int activations = buff.ActivationCount;
        var cooling = Drop(); Check(remote.TryStart(cooling), "Ordinary pickup still works during the shared crossbow cooldown"); yield return Complete();
        Check(cooling.isEquipped && buff.ActivationCount == activations && buff.Remaining < remaining,
            "A second copy during normal family cooldown cannot refresh the active buff");
        Check(gate.Remaining(RetrievalWeaponFamily.SmallCrossbow) > 28 && gate.IsReady(RetrievalWeaponFamily.Sword)
            && gate.IsReady(RetrievalWeaponFamily.Cannon), "Only SmallCrossbow uses its shared 30s timer; other weapon families stay independent");
        float oldClock = part.ReadyAt;
        yield return Wait(3.1f);
        float normal = player.GetComponent<MechStats>().BaseSpeed * player.GetComponent<MechStats>().SpeedMultiplier;
        Check(!buff.Active && buff.MovementMultiplier == 1 && combat.AttackCadenceMultiplier == 1 && Mathf.Abs(motor.WalkSpeed - normal) < .03f,
            "Natural expiry restores normal movement/cadence without equipment reinitialization");
        Check(part.ReadyAt == oldClock, "Expiry does not rewrite an existing physical weapon cooldown");
        yield return Cancellation();
        yield return LifecycleAndIdentity();
        yield return HeldFire();
    }
    static void WeaponTiming(Part crossbow)
    {
        var slot = equipment.GetEquipped(PartCategory.ArmL) == crossbow ? PartCategory.ArmL : PartCategory.ArmR;
        var result = combat.TryAttack(slot);
        float expected = Mathf.Max(crossbow.Stats.ShotInterval, crossbow.Stats.GetStat(StatType.Reload)) / 1.25f;
        Check(result.Started && Mathf.Abs(crossbow.CooldownDuration - expected) < .001f && crossbow.RemainingAmmo == 79,
            "Real one-round crossbow shot scales both interval and automatic reload, consuming one round");
        Check(!combat.TryAttack(slot).Started, "Buff cannot bypass the new shot's actual ready time");
        if (result.Projectile != null) Object.Destroy(result.Projectile.gameObject);
        var presentation = crossbow.GetComponent<MechWeaponAnimator>();
        Check(presentation != null && Mathf.Abs((float)typeof(MechWeaponAnimator).GetField("fireDuration", Hidden).GetValue(presentation)
            + (float)typeof(MechWeaponAnimator).GetField("reloadDuration", Hidden).GetValue(presentation) - expected) < .001f,
            "Weapon animation fits the accelerated actual cooldown");
        var sword = equipment.GetEquipped(slot == PartCategory.ArmL ? PartCategory.ArmR : PartCategory.ArmL);
        Check(sword != null && sword.Stats.subtype == PartSubtype.Melee && combat.TryAttack(slot == PartCategory.ArmL ? PartCategory.ArmR : PartCategory.ArmL).Started
            && Mathf.Abs(sword.CooldownDuration - sword.Stats.ShotInterval / 1.25f) < .001f, "Equipped melee attacks use the same 1.25x cadence");
        var cannon = equipment.GetEquipped(PartCategory.ShoulderR);
        var cannonShot = combat.TryAttack(PartCategory.ShoulderR);
        Check(cannonShot.Started && Mathf.Abs(cannon.CooldownDuration - Mathf.Max(cannon.Stats.ShotInterval, cannon.Stats.GetStat(StatType.Reload)) / 1.25f) < .001f,
            "Existing E cannon also accelerates its one-round automatic reload");
        if (cannonShot.Projectile != null) Object.Destroy(cannonShot.Projectile.gameObject);
        // A private multi-round item exercises the interval and manual-reload paths independently.
        var manual = Drop(); var data = Object.Instantiate(manual.data); owned.Add(data); data.partId = "buff-timing-private";
        data.stats = data.stats.Select(s => s.type == StatType.MagSize ? new StatEntry { type = s.type, value = 5 } : s).ToList(); manual.data = data;
        Check(equipment.TryEquip(manual, PartCategory.ArmR), "Private variable-magazine fixture equips through the normal transaction");
        var shot = combat.TryAttack(PartCategory.ArmR);
        Check(shot.Started && manual.MagazineRounds == 4 && !manual.IsReloading && Mathf.Abs(manual.CooldownDuration - data.ShotInterval / 1.25f) < .001f,
            "Multi-round magazines use faster firing intervals before a reload is needed");
        if (shot.Projectile != null) Object.Destroy(shot.Projectile.gameObject);
        int ammo = manual.RemainingAmmo;
        Check(combat.TryReload(PartCategory.ArmR) && manual.MagazineRounds == 5 && manual.RemainingAmmo == ammo
            && Mathf.Abs(manual.CooldownRemaining - Mathf.Max(data.ShotInterval, data.GetStat(StatType.Reload)) / 1.25f) < .001f,
            "Manual partial-magazine reload uses the buff without creating or spending ammunition");
        float runningClock = manual.ReadyAt; buff.Clear();
        Check(manual.ReadyAt == runningClock && combat.AttackCadenceMultiplier == 1, "Ending a buff leaves an already-started reload clock intact");
        var defaultShot = Drop();
        typeof(Part).GetMethod("InitializeAmmo", Hidden).Invoke(defaultShot, new object[] { 80, 1 });
        Consume(defaultShot, 0, false); int defaultAmmo = defaultShot.RemainingAmmo;
        var enemyShot = RangedAttack.Fire(defaultShot, player.transform, player.transform.position + Vector3.up * 2, player.transform.position + Vector3.forward * 30,
            Vector3.forward, 40, ~0, null, null);
        Check(enemyShot != null && Mathf.Abs(defaultShot.CooldownDuration - Mathf.Max(defaultShot.Stats.ShotInterval, defaultShot.Stats.GetStat(StatType.Reload))) < .001f,
            "Shared ranged API keeps unmodified default timing for enemies and callers without a player buff");
        if (enemyShot != null) Object.Destroy(enemyShot.gameObject);
        Check(defaultShot.RemainingAmmo == defaultAmmo - 1 && AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Arm_SmallCrossbow.asset").MagazineSize == 1,
            "Default shots still consume ammo and shared canonical weapon data remains unchanged");
        buff.Apply(3, 1.5f, 1.25f);
    }
    static IEnumerator Cancellation()
    {
        yield return Player(80); gate.bypassCooldownForTesting = false;
        var tooFar = Drop(distance: 22); Check(!remote.TryStart(tooFar) && !buff.Active && gate.IsReady(RetrievalWeaponFamily.SmallCrossbow), "Out-of-range rejection never grants a buff or consumes its gate");
        var hover = Drop(); Check(remote.TryStart(hover), "Cancellation fixture reserves SmallCrossbow"); remote.Cancel();
        Check(!buff.Active && gate.IsReady(RetrievalWeaponFamily.SmallCrossbow), "Cancelled anticipation refunds bonus without starting a buff");
        var flight = Drop(); Check(remote.TryStart(flight), "Flight cancellation starts"); while (remote.IsBusy && !remote.HasDeparted) yield return null; remote.Cancel();
        Check(!buff.Active && gate.IsReady(RetrievalWeaponFamily.SmallCrossbow), "Cancelled fast flight cannot grant or consume an arrival buff");
        var gone = Drop(); Check(remote.TryStart(gone), "Destroyed drop starts pending arrival"); Object.Destroy(gone.gameObject); yield return Complete();
        Check(!buff.Active && gate.IsReady(RetrievalWeaponFamily.SmallCrossbow), "Destroyed drop releases reservation without a buff");
        var blocked = Drop(); Check(remote.TryStart(blocked), "Obstructed pull starts pending arrival");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(wall); wall.transform.position = player.transform.position + new Vector3(0, 2, 9); wall.transform.localScale = new Vector3(8, 5, .3f);
        Physics.SyncTransforms(); yield return Complete(); wall.SetActive(false);
        Check(!blocked.isEquipped && !buff.Active && gate.IsReady(RetrievalWeaponFamily.SmallCrossbow), "A wall cancels retrieval and leaves buff ready");
        var off = Drop(); Check(remote.TryStart(off), "Listener disable starts pending arrival"); effects.enabled = false; yield return Complete(); effects.enabled = true;
        Check(off.isEquipped && !buff.Active && gate.IsReady(RetrievalWeaponFamily.SmallCrossbow), "Disabled listener releases pending token and cannot replay buff after re-enable");
        var disabledBuff = Drop(); buff.enabled = false; Check(remote.TryStart(disabledBuff), "A disabled buff receiver still permits ordinary pickup"); yield return Complete(); buff.enabled = true;
        Check(disabledBuff.isEquipped && !buff.Active && gate.IsReady(RetrievalWeaponFamily.SmallCrossbow), "Missing active receiver consumes no bonus cooldown");
        var lostReceiver = Drop(); Check(remote.TryStart(lostReceiver) && gate.IsReserved(RetrievalWeaponFamily.SmallCrossbow), "An active buff receiver reserves its pending bonus");
        buff.enabled = false; yield return Complete(); buff.enabled = true;
        Check(lostReceiver.isEquipped && !buff.Active && gate.IsReady(RetrievalWeaponFamily.SmallCrossbow), "Receiver disabled during travel refunds its pending bonus at arrival");
    }
    static IEnumerator LifecycleAndIdentity()
    {
        var ordinary = Drop(); Check(equipment.TryEquip(ordinary, PartCategory.ArmR) && !buff.Active && gate.IsReady(RetrievalWeaponFamily.SmallCrossbow),
            "Nearby ordinary equip does not grant a remote-retrieval buff");
        var large = Drop("shoulder_crossbow"); Check(remote.TryStart(large), "Large crossbow can still be retrieved normally"); yield return Complete();
        Check(large.isEquipped && !buff.Active && gate.IsReady(RetrievalWeaponFamily.SmallCrossbow), "Shared projectile/splash subtype cannot misclassify another weapon as small crossbow");
        var rolled = Drop(); var rolledData = Object.Instantiate(rolled.data); owned.Add(rolledData);
        rolledData.stats = rolledData.stats.Select(s => s.type == StatType.Damage ? new StatEntry { type = s.type, value = 0 } : s).ToList(); rolled.data = rolledData;
        Check(remote.TryStart(rolled), "A rolled copy with canonical identity and zero damage accepts retrieval"); yield return Complete();
        Check(buff.Active && buff.ActivationCount == 1 && gate.Remaining(RetrievalWeaponFamily.SmallCrossbow) > 29,
            "Canonical ID fallback grants non-damage buff even when Damage is zero");
        buff.enabled = false;
        Check(!buff.Active && buff.MovementMultiplier == 1 && combat.AttackCadenceMultiplier == 1, "Disabling the buff clears both multipliers immediately"); buff.enabled = true;
        Check(!buff.Active, "Re-enabling cannot restore a cancelled bonus");
        gate.bypassCooldownForTesting = true;
        var again = Drop(); Check(remote.TryStart(again), "Bypass allows lifecycle fixture to trigger again"); yield return Complete(); effects.enabled = false;
        Check(!buff.Active && combat.AttackCadenceMultiplier == 1, "Disabling retrieval effects ends the active buff"); effects.enabled = true;
        var dead = Drop(); Check(remote.TryStart(dead), "Owner-death fixture starts pending arrival"); player.GetComponent<CombatHealth>().ApplyDamage(100000);
        Check(!buff.Active && !dead.IsRetrieving && !gate.IsReserved(RetrievalWeaponFamily.SmallCrossbow), "Owner death clears speed bonus and cancels pending retrieval");
        yield return Frames();
        Check(!buff.Apply(3, 1.5f, 1.25f), "Disabled dead owner cannot acquire a new buff");
        yield return Player(140);
        buff.Apply(3, 1.5f, 1.25f); player.SetActive(false); player.SetActive(true);
        Check(!buff.Active && motor.WalkSpeed > 0 && combat.AttackCadenceMultiplier == 1, "Pooled player disable/re-enable leaves no stale movement/cadence bonus");
        Check(!buff.Apply(float.NaN, 1.5f, 1.25f) && !buff.Apply(3, float.PositiveInfinity, 1.25f) && !buff.Apply(-1, 1.5f, 1.25f),
            "Invalid settings cannot introduce non-finite speed or a permanent timer");
    }
    static IEnumerator HeldFire()
    {
        yield return Player(200);
        // Isolated batch mode has no focused Game view; route only this fixture's synthetic input to gameplay.
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        var crossbow = Drop(); Check(equipment.TryEquip(crossbow, PartCategory.ArmR), "Held-fire fixture equips the real small crossbow");
        var mouse = InputSystem.AddDevice<Mouse>();
        var input = player.GetComponent<MechPlayerInput>();
        var times = new List<float>();
        Action<Part> fired = part => { if (part == crossbow) times.Add(Time.time); };
        combat.WeaponAttackStarted += fired;
        try
        {
            input.AssumeCursorCaptured = true; input.enabled = true;
            InputSystem.QueueStateEvent(mouse, new MouseState()); yield return Frames(3);
            buff.Apply(3, 1.5f, 1.25f);
            int ammo = crossbow.RemainingAmmo;
            InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Right));
            yield return Wait(2.85f);
            InputSystem.QueueStateEvent(mouse, new MouseState()); yield return Frames(2);
            Debug.Log("HELD BUFF OBSERVATION: shots=" + times.Count + "; timestamps=" + string.Join(",", times)
                + "; ammo=" + crossbow.RemainingAmmo + "/" + ammo + "; remaining=" + buff.Remaining
                + "; activeInput=" + input.isActiveAndEnabled + "; blockedMission=" + MissionOutcomeController.BlocksGameplay(player.scene)
                + "; currentMouse=" + (Mouse.current == mouse) + "; combatEnabled=" + combat.enabled);
            Check(times.Count == 3 && crossbow.RemainingAmmo == ammo - 3, "Holding RMB fires three real boosted crossbow shots in 2.85s with correct ammunition");
            float interval = Mathf.Max(crossbow.Stats.ShotInterval, crossbow.Stats.GetStat(StatType.Reload)) / 1.25f;
            Check(times.Skip(1).Select((time, i) => time - times[i]).All(gap => gap >= interval - .002f && gap < interval + .11f),
                "Held fire repeats at the boosted accepted cadence, without early shots");
            float clock = crossbow.ReadyAt; int count = times.Count;
            yield return Wait(.3f);
            Check(!buff.Active && times.Count == count && crossbow.ReadyAt == clock, "Release stops held fire; buff expiry preserves the last shot's clock");
            input.enabled = false;
            yield return Wait(Mathf.Max(0, crossbow.ReadyAt - Time.time) + .05f);
            var normal = combat.TryAttack(PartCategory.ArmR);
            Check(normal.Started && Mathf.Abs(crossbow.CooldownDuration - Mathf.Max(crossbow.Stats.ShotInterval, crossbow.Stats.GetStat(StatType.Reload))) < .001f,
                "The same physical crossbow returns to its normal shot/reload timing after expiry");
            if (normal.Projectile != null) Object.Destroy(normal.Projectile.gameObject);
        }
        finally
        {
            combat.WeaponAttackStarted -= fired;
            input.enabled = false;
            InputSystem.RemoveDevice(mouse);
        }
    }
    static void Finish(Exception error)
    {
        Time.timeScale = 1; PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending, false);
        if (error != null) Debug.LogException(error);
        Debug.Log("RETRIEVAL SPEED CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
