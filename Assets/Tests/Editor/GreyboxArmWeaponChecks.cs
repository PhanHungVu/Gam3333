using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class GreyboxArmWeaponChecks
{
    const string Pending = "GreyboxArmWeaponChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static GameObject player;
    static MechCombat combat;
    static MechMovementController motor;
    static EquipManager equipment;
    static Camera view;
    static GreyboxArmWeaponBench bench;
    static readonly List<GameObject> targets = new List<GameObject>();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("GREYBOX PASS: " + message); }
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .01f;
    static void Step(MechProjectile shot, float dt) => typeof(MechProjectile).GetMethod("Step", Private).Invoke(shot, new object[] { dt });
    static Part Equip(int index, PartCategory slot = PartCategory.ArmL)
    {
        bench.selected = index; Check(bench.Equip(slot), "Bench equips " + GreyboxArmWeaponSetup.Names[index] + " in " + slot);
        // Earlier swaps leave physical pickups beside the sockets; they must not
        // obstruct an unrelated damage fixture's muzzle or firing lane.
        foreach (var loose in Object.FindObjectsByType<Part>(FindObjectsInactive.Include))
            if (!loose.isEquipped && loose.GetComponentInParent<CombatHealth>() == null) Object.DestroyImmediate(loose.gameObject);
        return equipment.GetEquipped(slot);
    }
    static CombatHealth Dummy(Vector3 offset, float hp = 1000, Vector3? size = null, CombatTeam team = CombatTeam.Enemy)
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Cube); root.name = "Greybox target"; root.transform.position = player.transform.position + offset;
        root.transform.localScale = size ?? new Vector3(.4f, 6, .4f); var health = root.AddComponent<CombatHealth>(); health.Team = team; health.SetMaximum(hp, true); targets.Add(root); Physics.SyncTransforms(); return health;
    }
    static void Clear()
    {
        foreach (var target in targets) if (target != null) Object.DestroyImmediate(target); targets.Clear();
        foreach (var shot in Object.FindObjectsByType<MechProjectile>(FindObjectsInactive.Include)) Object.DestroyImmediate(shot.gameObject);
        combat.Melee.Cancel(); motor.CancelHookPull(); TestMech.Advance(player, 20);
    }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) Run(); };
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).Contains(".quest-review")) throw new Exception("Disposable project only.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Run()
    {
        SessionState.SetBool(Pending, false); AudioListener.volume = 0;
        try
        {
            PreMissionReliabilityChecks.BeginLogAudit(); Definitions();
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = Vector3.down * .3f; floor.transform.localScale = new Vector3(160,.6f,160);
            player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath), new Vector3(0,1.6f,0), Quaternion.identity);
            player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<CombatFeedback>().enabled = false;
            equipment = player.GetComponent<EquipManager>(); combat = player.GetComponent<MechCombat>(); motor = player.GetComponent<MechMovementController>();
            player.GetComponent<CombatHealth>().Team = CombatTeam.Player;
            view = new GameObject("Greybox camera").AddComponent<Camera>(); view.transform.SetPositionAndRotation(new Vector3(0,2.1f,-8), Quaternion.identity); combat.BindCamera(view); motor.BindCamera(view);
            var targeting = player.GetComponent<MechTargetLockController>(); targeting.BindCamera(view); targeting.EnterFreeAim();
            // Exact-damage fixtures isolate new mechanics from the separately tested head-spread system.
            var head = equipment.GetEquipped(PartCategory.Head); var copy = Object.Instantiate(head.data); copy.stats = new List<StatEntry>(copy.stats);
            copy.stats.RemoveAll(s => s.type == StatType.Accuracy); copy.stats.Add(new StatEntry { type = StatType.Accuracy, value = 100 }); head.data = copy;
            bench = new GameObject("Greybox bench").AddComponent<GreyboxArmWeaponBench>(); bench.catalogue = AssetDatabase.LoadAssetAtPath<GreyboxArmWeaponCatalogue>(GreyboxArmWeaponSetup.CataloguePath); bench.player = equipment; bench.keyboardShortcuts = false;
            CompatibilityAndStats(); Minigun(); Shotgun(); Melee(); Scythe(); Punches(); MetalBlades(); Hook();
            Clear(); PreMissionReliabilityChecks.AssertNoUnexpectedErrors(); PreMissionReliabilityChecks.EndLogAudit();
            Debug.Log("GREYBOX ARM CHECKS COMPLETE: " + checks); EditorApplication.Exit(0);
        }
        catch (Exception error) { PreMissionReliabilityChecks.EndLogAudit(); Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void Definitions()
    {
        var types = new[] { StatType.Damage, StatType.SplashDamage, StatType.Ammo, StatType.MagSize, StatType.FireRate, StatType.Reload, StatType.Range, StatType.SplashRadius, StatType.MoveSpeed };
        for (int i = 0; i < 9; i++)
        {
            var data = AssetDatabase.LoadAssetAtPath<PartData>(GreyboxArmWeaponSetup.DataPath(i)); var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GreyboxArmWeaponSetup.PrefabPath(i));
            Check(data != null && prefab != null && prefab.GetComponent<Part>().data == data && data.partType == PartType.Arm && data.restrictToPartType, "Weapon " + (i+5) + " has its own Arm-only definition and physical prefab");
            bool exact = true; for (int s = 0; s < types.Length; s++) exact &= Near(data.GetStat(types[s]), GreyboxArmWeaponSetup.Stats[i,s]);
            Check(exact, data.partName + " retains every exact spreadsheet stat, with Fire Rate in attacks per second");
            Check(data.icon != null && prefab.GetComponentsInChildren<MeshFilter>().Length == 1 && prefab.GetComponentsInChildren<MeshFilter>()[0].sharedMesh.name == "Cube"
                && prefab.GetComponentsInChildren<Animator>().Length == 0 && prefab.GetComponentsInChildren<AudioSource>().Length == 0 && prefab.GetComponentsInChildren<ParticleSystem>().Length == 0,
                data.partName + " uses one primitive cube and an unpolished HUD icon, without authored animation/audio/particles");
        }
    }
    static void CompatibilityAndStats()
    {
        var rules = ScriptableObject.CreateInstance<MechRulesConfig>(); rules.allowMixedTypes = true; var previousRules = equipment.rulesConfig; equipment.rulesConfig = rules;
        for (int i = 0; i < 9; i++)
        {
            bench.selected = i; var pickup = bench.SpawnSelected();
            Check(pickup != null && !pickup.HasRolledStats && !pickup.RollLootStats(10), "Bench " + pickup.data.partName + " keeps exact stats instead of applying loot variation");
            Check(!equipment.TryEquip(pickup, PartCategory.ShoulderL) && !equipment.TryEquip(pickup, PartCategory.ShoulderR), pickup.data.partName + " rejects both shoulder slots even under mixed-type test rules");
            equipment.Unequip(PartCategory.ArmL); float before = player.GetComponent<MechStats>().SpeedMultiplier;
            Check(equipment.TryEquip(pickup, PartCategory.ArmL) && Near(player.GetComponent<MechStats>().SpeedMultiplier / before, 1 + pickup.Stats.GetStat(StatType.MoveSpeed) / 100), pickup.data.partName + " applies its exact equipped movement penalty");
            equipment.Unequip(PartCategory.ArmL); Check(equipment.TryEquip(pickup, PartCategory.ArmR), pickup.data.partName + " also fits the normal right arm");
        }
        equipment.rulesConfig = previousRules; Object.DestroyImmediate(rules); Clear();
    }
    static void Minigun()
    {
        var part = Equip(0); Check(part.RemainingAmmo == 400 && part.MagazineRounds == 50, "Minigun initializes 400 total rounds and a 50-round magazine");
        int fired = 0; Action<MechProjectile> count = p => fired++; combat.ShotFired += count;
        for (int i = 0; i < 50; i++) { TestMech.Advance(player, 1); Check(combat.TryAttack(PartCategory.ArmL).Started, "Minigun admits ready shot " + (i+1)); foreach (var shot in Object.FindObjectsByType<MechProjectile>()) Object.DestroyImmediate(shot.gameObject); }
        combat.ShotFired -= count;
        Check(fired == 50 && part.RemainingAmmo == 350 && part.MagazineRounds == 50 && part.IsReloading && Near(part.ReadyAt-Time.time, 2), "Minigun consumes one round per bullet and automatically reloads in exactly 2 seconds after shot 50");
        Check(!combat.TryAttack(PartCategory.ArmL).Started, "Minigun cannot shoot through its reload");
        TestMech.Advance(player, 3); combat.TryAttack(PartCategory.ArmL);
        Check(Near(part.CooldownDuration, .1f), "Minigun has a 0.1-second firing interval (10 per second)");
        TestMech.Advance(player, 1); int ammo = part.RemainingAmmo;
        Check(combat.TryReload(PartCategory.ArmL) && part.RemainingAmmo == ammo && part.MagazineRounds == 50 && Near(part.CooldownRemaining, 2), "Manual reload restores the partial magazine without creating ammo");
        var drop = equipment.Unequip(PartCategory.ArmL); float ready = drop.ReadyAt;
        Check(equipment.TryEquip(drop, PartCategory.ArmL) && drop.ReadyAt == ready && drop.RemainingAmmo == ammo, "Dropping and swapping a new weapon preserves ammunition and reload deadline"); Clear();
    }
    static void Shotgun()
    {
        var part = Equip(4); var target = Dummy(new Vector3(0,.5f,8),1000,new Vector3(10,10,.3f));
        int attacks = 0, shots = 0; Action<Part> attack = p => attacks++; Action<MechProjectile> shotEvent = p => shots++; combat.WeaponAttackStarted += attack; combat.ShotFired += shotEvent;
        var result = combat.TryAttack(PartCategory.ArmL); var pellets = Object.FindObjectsByType<MechProjectile>();
        Check(result.Started && pellets.Length == 6 && attacks == 1 && shots == 6 && part.RemainingAmmo == 49 && part.MagazineRounds == 4, "One Shotgun activation launches six ordinary pellets but consumes one round and one attack event; live=" + pellets.Length + ", attacks=" + attacks + ", shots=" + shots + ", ammo=" + part.RemainingAmmo + ", magazine=" + part.MagazineRounds);
        Check(pellets.Select(p=>p.Direction).Distinct().Count() > 1 && combat.SpreadHalfAngle >= 6, "Shotgun pellets spread even at perfect head precision and the crosshair includes that spread");
        foreach (var pellet in pellets) Step(pellet, 1);
        Check(Near(target.CurrentHealth,850), "Six Shotgun hits deal 25 each for 150 total damage");
        combat.WeaponAttackStarted -= attack; combat.ShotFired -= shotEvent;
        for (int i=0;i<4;i++){TestMech.Advance(player,4); combat.TryAttack(PartCategory.ArmL);}
        Check(part.RemainingAmmo == 45 && part.MagazineRounds == 5 && part.IsReloading && Near(part.CooldownRemaining,3), "Shotgun reloads its five-round magazine in 3 seconds without per-pellet ammo consumption"); Clear();
    }
    static void Melee()
    {
        Equip(2); var direct=Dummy(new Vector3(0,.5f,2)); var outer=Dummy(new Vector3(2,.5f,4));
        combat.TryAttack(PartCategory.ArmL);
        Check(Near(direct.CurrentHealth,900) && Near(outer.CurrentHealth,950), "Hammer deals 100 to its direct box and 50 to its secondary 3m area, without stacking"); Clear();
        Equip(3); direct=Dummy(new Vector3(0,.5f,11)); outer=Dummy(new Vector3(2,.5f,8)); var far=Dummy(new Vector3(0,.5f,14)); combat.TryAttack(PartCategory.ArmL);
        Check(Near(direct.CurrentHealth,910) && Near(outer.CurrentHealth,1000) && Near(far.CurrentHealth,1000) && Near(combat.Melee.BoxSize.z,12), "Spear deals 90 with its unchanged narrow 12-metre hitbox"); Clear();
        var super=Equip(5); direct=Dummy(new Vector3(1.8f,.5f,9)); combat.TryAttack(PartCategory.ArmL);
        Check(Near(direct.CurrentHealth,750) && Near(super.CooldownDuration,1/.35f) && !combat.TryAttack(PartCategory.ArmL).Started, "Supersword deals 250 with its unchanged broad 10m box at 0.35 attacks per second"); Clear();
        Equip(2); direct=Dummy(new Vector3(0,.5f,3)); var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position=player.transform.position+new Vector3(0,.5f,1.5f); wall.transform.localScale=new Vector3(8,8,.2f); Physics.SyncTransforms(); combat.TryAttack(PartCategory.ArmL);
        Check(Near(direct.CurrentHealth,1000), "New melee boxes and splash cannot damage through walls"); Object.DestroyImmediate(wall); Clear();
    }
    static void Scythe()
    {
        Equip(6); var health=player.GetComponent<CombatHealth>(); health.SetMaximum(200,true); health.ApplyDamage(100);
        Dummy(new Vector3(0,.5f,3),30); Dummy(new Vector3(1.5f,.5f,6.8f),20); Dummy(new Vector3(-1,.5f,3),1000,team:CombatTeam.Player);
        combat.TryAttack(PartCategory.ArmL);
        Check(Near(health.CurrentHealth,105), "Scythe heals 10% of confirmed enemy HP removed, including splash, excluding overkill and friendly targets"); Clear();
        Equip(6); health.SetMaximum(200,true); health.ApplyDamage(2); Dummy(new Vector3(0,.5f,3),1000); combat.TryAttack(PartCategory.ArmL);
        Check(Near(health.CurrentHealth,200), "Scythe lifesteal never exceeds maximum health"); Clear();
        Equip(6); health.ApplyDamage(50); float before=health.CurrentHealth; combat.TryAttack(PartCategory.ArmL);
        Check(Near(health.CurrentHealth,before), "Scythe whiffs do not heal"); Clear();
    }
    static void Punches()
    {
        var part=Equip(7); var target=Dummy(new Vector3(0,.5f,2)); int hits=0; target.Damaged+=amount=>hits++;
        combat.TryAttack(PartCategory.ArmL); Check(Near(target.CurrentHealth,970) && combat.Melee.SequenceActive && !combat.TryAttack(PartCategory.ArmL).Started, "Punchy starts with one 30-damage hit and blocks overlapping sequences");
        combat.Melee.Tick(.2f); combat.Melee.Tick(.2f);
        Check(hits==3 && Near(target.CurrentHealth,910) && !combat.Melee.SequenceActive && Near(part.CooldownDuration,.5f), "Punchy performs exactly three 30-damage strikes by 0.4 seconds, with a 0.5-second activation cadence"); Clear();
        Equip(7); target=Dummy(new Vector3(0,.5f,2)); combat.TryAttack(PartCategory.ArmL); equipment.Unequip(PartCategory.ArmL); combat.Melee.Tick(1);
        Check(Near(target.CurrentHealth,970) && !combat.Melee.SequenceActive, "Swapping away cancels the remaining punches"); Clear();
    }
    static void MetalBlades()
    {
        Equip(8); var direct=Dummy(new Vector3(0,.5f,8),size:new Vector3(.4f,6,.1f)); var outer=Dummy(new Vector3(0,.5f,8.3f),size:new Vector3(.4f,6,.1f));
        // Aim at the primary dummy explicitly; projectile release still uses the shared ranged path.
        var target=direct.gameObject.AddComponent<MechTargetable>(); var locking=player.GetComponent<MechTargetLockController>(); locking.ResumeTargeting(); typeof(MechTargetLockController).GetMethod("LateUpdate",Private).Invoke(locking,null);
        var result=combat.TryAttack(PartCategory.ArmL); int impacts=0; result.Projectile.ImpactResolved+=impact=>impacts++; Step(result.Projectile,1);
        Check(Near(direct.CurrentHealth,910) && Near(outer.CurrentHealth,970) && impacts==1, "Metal Blades applies direct 60 plus one unchanged 30-damage splash; direct="+direct.CurrentHealth+", nearby="+outer.CurrentHealth+", impacts="+impacts);
        locking.EnterFreeAim(); Clear();
    }
    static void Hook()
    {
        var part=Equip(1); var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position=player.transform.position+new Vector3(0,1,10); wall.transform.localScale=new Vector3(30,15,.2f); Physics.SyncTransforms();
        var shot=combat.TryAttack(PartCategory.ArmL).Projectile; Step(shot,1);
        Check(motor.IsHookPulling && part.RemainingAmmo==29 && Near(part.CooldownRemaining,5), "Hook surface impact starts a motor pull and retains the specified 5s interval and one-round ammo use");
        Vector3 start=player.transform.position;
        for(int i=0;i<100 && motor.IsHookPulling;i++) motor.Tick(new MechLocomotionInput(),view.transform,.02f);
        Check(player.transform.position.z>start.z+3 && player.transform.position.z<wall.transform.position.z && !motor.IsHookPulling, "Hook moves the existing capsule toward its impact and stops before the surface"); Object.DestroyImmediate(wall);
        var cc=player.GetComponent<CharacterController>(); cc.enabled=false; player.transform.SetPositionAndRotation(new Vector3(0,1.6f,0),Quaternion.identity); cc.enabled=true;
        wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position=new Vector3(0,2,4); wall.transform.localScale=new Vector3(20,10,.3f); Physics.SyncTransforms();
        Check(motor.BeginHookPull(new Vector3(0,1.6f,12),24,1,2,part), "Hook accepts a bounded destination through the existing motor API");
        for(int i=0;i<100 && motor.IsHookPulling;i++) motor.Tick(new MechLocomotionInput(),view.transform,.02f);
        Check(player.transform.position.z<4 && !motor.IsHookPulling, "An intervening wall blocks and terminates pulling instead of teleporting through it"); Object.DestroyImmediate(wall);
        motor.BeginHookPull(player.transform.position+Vector3.forward*10,24,1,2,part); equipment.Unequip(PartCategory.ArmL); motor.Tick(new MechLocomotionInput(),view.transform,.02f);
        Check(!motor.IsHookPulling, "Unequipping the Hook cancels active pulling"); Clear();
    }
}
