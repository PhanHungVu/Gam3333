using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class ShoulderWeaponChecks
{
    const string Pending = "ShoulderWeaponChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static GameObject player;
    static EquipManager equipment;
    static MechCombat combat;
    static Camera view;
    static GreyboxShoulderWeaponCatalogue catalogue;
    static readonly List<GameObject> targets = new List<GameObject>();
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("SHOULDER PASS: " + message); }
    static bool Near(float a, float b) => Mathf.Abs(a - b) < .015f;
    static void Step(MechProjectile shot, float dt) => typeof(MechProjectile).GetMethod("Step", Private).Invoke(shot, new object[] { dt });
    static void Tick(GreyboxShoulderWeapon weapon, float dt) => typeof(GreyboxShoulderWeapon).GetMethod("Tick", Private).Invoke(weapon, new object[] { dt });
    [InitializeOnLoadMethod] static void Register() => EditorApplication.playModeStateChanged += state =>
    { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) Run(); };
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Application.dataPath.Contains(".quest-review")) throw new Exception("Disposable project only.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static Part Equip(int index, PartCategory socket = PartCategory.ShoulderL)
    {
        var part = Object.Instantiate(catalogue.weapons[index]); part.KeepAuthoredStats();
        Check(equipment.TryEquip(part, socket), "Physical " + part.data.partName + " equips in " + socket);
        ClearLoose();
        return part;
    }
    static void ClearLoose()
    {
        foreach(var loose in Object.FindObjectsByType<Part>())
            if(!loose.isEquipped&&loose.GetComponentInParent<CombatHealth>()==null)Object.DestroyImmediate(loose.gameObject);
    }
    static CombatHealth Dummy(Vector3 point, Vector3? size = null, CombatTeam team = CombatTeam.Enemy, int compound = 0)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = "Shoulder target"; go.transform.position = point; go.transform.localScale = size ?? new Vector3(.5f, 3, .5f);
        var health = go.AddComponent<CombatHealth>(); health.Team = team; health.SetMaximum(1000, true); targets.Add(go);
        for (int i = 0; i < compound; i++) { var child = new GameObject("Compound"); child.transform.SetParent(go.transform, false); child.AddComponent<BoxCollider>(); }
        Physics.SyncTransforms(); return health;
    }
    static void Clear()
    {
        foreach (var go in targets) if (go != null) Object.DestroyImmediate(go); targets.Clear();
        foreach (var shot in Object.FindObjectsByType<MechProjectile>()) Object.DestroyImmediate(shot.gameObject);
        foreach (var config in player.GetComponentsInChildren<GreyboxShoulderWeapon>()) typeof(GreyboxShoulderWeapon).GetMethod("Cancel", Private).Invoke(config, null);
        TestMech.Advance(player, 30); Physics.SyncTransforms();
    }
    static MechProjectile Fire(Part part, PartCategory slot = PartCategory.ShoulderL)
    {
        view.transform.SetPositionAndRotation(part.muzzle.position, Quaternion.identity); TestMech.Advance(player, 20); Physics.SyncTransforms();
        var result = combat.TryAttack(slot); Check(result.Started && result.Projectile != null, "Existing combat firing path admits " + part.data.partName); return result.Projectile;
    }
    static void Run()
    {
        SessionState.SetBool(Pending, false); AudioListener.volume = 0;
        try
        {
            PreMissionReliabilityChecks.BeginLogAudit();
            player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath), new Vector3(0, 1.6f, 0), Quaternion.identity);
            player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechMovementController>().enabled = false; player.GetComponent<CombatFeedback>().enabled = false;
            equipment = player.GetComponent<EquipManager>(); combat = player.GetComponent<MechCombat>();
            view = new GameObject("Shoulder camera").AddComponent<Camera>(); combat.BindCamera(view);
            var targeting = player.GetComponent<MechTargetLockController>(); targeting.BindCamera(view); targeting.EnterFreeAim();
            var head = equipment.GetEquipped(PartCategory.Head); var copy = Object.Instantiate(head.data); copy.stats = new List<StatEntry> { new StatEntry { type = StatType.Accuracy, value = 100 } }; head.data = copy;
            catalogue = AssetDatabase.LoadAssetAtPath<GreyboxShoulderWeaponCatalogue>(GreyboxShoulderWeaponSetup.CataloguePath);
            Definitions(); Existing(); Shield(); Fishing(); Mortar(); Katyusha(); SalvoEdges(); Ring(); Tentacles(); Tesla(); TeslaEdges(); Overlapping(); Clear();
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors(); PreMissionReliabilityChecks.EndLogAudit();
            Debug.Log("SHOULDER CHECKS COMPLETE: " + checks); EditorApplication.Exit(0);
        }
        catch (Exception error) { PreMissionReliabilityChecks.EndLogAudit(); Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void Definitions()
    {
        Check(catalogue != null && catalogue.weapons.Length == 9 && catalogue.weapons.Distinct().Count() == 9, "Catalogue has nine independent weapons and reuses the original first two prefabs");
        var previous = equipment.rulesConfig; var mixed = ScriptableObject.CreateInstance<MechRulesConfig>(); mixed.allowMixedTypes = true; equipment.rulesConfig = mixed;
        var statTypes = new[] { StatType.Damage, StatType.SplashDamage, StatType.Ammo, StatType.MagSize, StatType.FireRate, StatType.Reload, StatType.Range, StatType.SplashRadius, StatType.MoveSpeed };
        for (int i = 0; i < 9; i++)
        {
            var part = Object.Instantiate(catalogue.weapons[i]); part.KeepAuthoredStats();
            Check(!equipment.TryEquip(part, PartCategory.ArmL) && !equipment.TryEquip(part, PartCategory.ArmR), part.data.partName + " rejects both Arm sockets even in mixed-type testing");
            Check(equipment.TryEquip(part, PartCategory.ShoulderL), part.data.partName + " fits left Shoulder"); equipment.Unequip(PartCategory.ShoulderL);
            Check(equipment.TryEquip(part, PartCategory.ShoulderR), part.data.partName + " fits right Shoulder"); equipment.Unequip(PartCategory.ShoulderR); Object.DestroyImmediate(part.gameObject);
            if (i < 2) continue;
            var data = catalogue.weapons[i].data; bool exact = true;
            for (int s = 0; s < statTypes.Length; s++) exact &= Near(data.GetStat(statTypes[s]), GreyboxShoulderWeaponSetup.Stats[i - 2,s]);
            Check(exact && data.partType == PartType.Shoulder && data.restrictToPartType && data.icon != null, data.partName + " keeps all authored stats, strict Shoulder identity and HUD icon");
            var roll = Object.Instantiate(catalogue.weapons[i]);
            Check(roll.RollLootStats(3100 + i) && roll.Stats.stats.All(s => s.value >= Math.Min(data.GetStat(s.type) * .9f, data.GetStat(s.type) * 1.1f) - .001f
                && s.value <= Math.Max(data.GetStat(s.type) * .9f, data.GetStat(s.type) * 1.1f) + .001f), data.partName + " supports existing independent +/-10% loot rolls"); Object.DestroyImmediate(roll.gameObject);
            Check(catalogue.weapons[i].GetComponentsInChildren<Animator>(true).Length == 0 && catalogue.weapons[i].GetComponentsInChildren<AudioSource>(true).Length == 0
                && catalogue.weapons[i].GetComponentsInChildren<ParticleSystem>(true).Length == 0, data.partName + " uses primitive presentation without authored animation, audio or particles");
        }
        var arm = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_sword.prefab")).GetComponent<Part>();
        Check(!equipment.TryEquip(arm, PartCategory.ShoulderL) && !equipment.TryEquip(arm, PartCategory.ShoulderR), "Original Arm weapons cannot cross into Shoulder sockets under mixed rules either"); Object.DestroyImmediate(arm.gameObject);
        equipment.rulesConfig = previous; Object.DestroyImmediate(mixed); Clear();
    }
    static void Existing()
    {
        var cannon = Equip(0); Vector3 muzzle = cannon.muzzle.position;
        var direct = Dummy(muzzle + Vector3.forward * 12); var near = Dummy(muzzle + new Vector3(3.7f,0,12)); var far = Dummy(muzzle + new Vector3(4.8f,0,12));
        var shot = Fire(cannon); Step(shot, .3f);
        Check(Near(direct.CurrentHealth,680) && Near(near.CurrentHealth,880) && far.CurrentHealth == 1000, "Cannon applies 200 direct + 120 splash, with splash only inside its new 4m radius");
        Check(cannon.RemainingAmmo == 29 && cannon.IsReloading && Near(cannon.CooldownDuration,5), "Existing one-round Cannon starts its 5-second reload without changing its model"); Clear();
        var crossbow = Equip(1); muzzle = crossbow.muzzle.position;
        direct = Dummy(muzzle + Vector3.forward * 6, compound: 3); near = Dummy(muzzle + Vector3.forward * 12); far = Dummy(muzzle + new Vector3(2,0,6));
        shot = Fire(crossbow); Step(shot,.12f);
        Check(Near(direct.CurrentHealth,820) && Near(near.CurrentHealth,820) && far.CurrentHealth == 1000 && shot.IsLive && crossbow.Stats.piercingProjectile,
            "Existing Large Crossbow still pierces compound enemies once each for 180, with no splash"); Clear();
    }
    static void Shield()
    {
        var part = Equip(2); var config = part.GetComponent<GreyboxShoulderWeapon>(); var health = player.GetComponent<CombatHealth>(); health.SetMaximum(2000,true);
        int events = 0; Action<float> hit = _ => events++; health.Damaged += hit;
        Check(combat.TryAttack(PartCategory.ShoulderL).Started && !WeaponsHudPod.HasAmmo(part.Stats), "Shield activates through Q's existing combat path with N/A ammunition");
        Check(!health.ApplyDamage(100, player.transform.position + Vector3.forward * 10) && !health.ApplyDamage(100, player.transform.position - Vector3.forward * 10)
            && health.CurrentHealth == 2000 && events == 0, "Default all-direction block prevents actual damage and damage events");
        Tick(config,.49f); Check(!health.ApplyDamage(100) && !combat.TryAttack(PartCategory.ShoulderL).Started, "Shield stays active through 0.49s and respects its 2s activation cadence");
        Tick(config,.02f); Check(health.ApplyDamage(100) && health.CurrentHealth == 1900 && !config.IsActive, "Damage resumes after the configured half-second block window");
        TestMech.Advance(player,10); config.blockAllDirections = false; combat.TryAttack(PartCategory.ShoulderL);
        Check(!health.ApplyDamage(10,player.transform.position+Vector3.forward*5) && health.ApplyDamage(10,player.transform.position-Vector3.forward*5), "Optional frontal mode blocks only configured incoming directions");
        equipment.Unequip(PartCategory.ShoulderL);
        Check(health.ApplyDamage(10) && !config.IsActive && config.shieldVisual.localPosition == (Vector3)typeof(GreyboxShoulderWeapon).GetField("shieldRestPosition",Private).GetValue(config), "Dropping an active shield immediately removes protection and restores its stowed model pose");
        health.Damaged -= hit; Object.DestroyImmediate(part.gameObject); Clear();
    }
    static void Fishing()
    {
        var part = Equip(4); Vector3 muzzle = part.muzzle.position;
        var direct = Dummy(muzzle + Vector3.forward * 12,new Vector3(.1f,3,.1f)); var side = Dummy(muzzle + new Vector3(.3f,0,12),new Vector3(.1f,3,.1f));
        var shot=Fire(part); Step(shot,.2f);
        Check(Near(direct.CurrentHealth,990) && side.CurrentHealth==1000 && Near(part.Stats.GetStat(StatType.ProjectileSize),.025f), "Fishing Line has a narrow swept shot, deals 10, and does not hit a nearby off-axis enemy");
        for(int i=0;i<9;i++){TestMech.Advance(player,5);combat.TryAttack(PartCategory.ShoulderL);}
        Check(part.RemainingAmmo==40 && part.MagazineRounds==10 && part.IsReloading && Near(part.CooldownDuration,2), "Fishing Line consumes one round per activation and reloads its ten-round magazine");
        Check(!player.GetComponent<MechMovementController>().IsHookPulling, "Fishing Line never starts the arm Hook movement mechanic"); Clear();
    }
    static void Mortar()
    {
        var part=Equip(5);Vector3 muzzle=part.muzzle.position;
        var direct=Dummy(muzzle+Vector3.forward*50);var nearby=Dummy(muzzle+new Vector3(4,0,50));var outside=Dummy(muzzle+new Vector3(6,0,50));
        var shot=Fire(part);Step(shot,.5f);
        Check(shot.IsLive && shot.transform.position.y>muzzle.y+2 && shot.transform.position.z>muzzle.z+5, "Mortar climbs along a real configurable ballistic arc instead of the Cannon's straight path");
        Step(shot,3);
        Check(Near(direct.CurrentHealth,800) && Near(nearby.CurrentHealth,900) && outside.CurrentHealth==1000 && !shot.IsLive, "Mortar lands on a distant target for 100 direct + 100 splash, with a 5m explosion");
        Check(part.RemainingAmmo==24 && Near(part.CooldownDuration,5), "Mortar uses existing one-round ammo and 5s reload"); Clear();
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=muzzle+Vector3.forward*10;wall.transform.localScale=new Vector3(8,30,.2f);targets.Add(wall);
        direct=Dummy(muzzle+Vector3.forward*50);shot=Fire(part);Step(shot,3);
        Check(!shot.IsLive && direct.CurrentHealth==1000, "Mortar swept collision hits tall world obstacles before its planned target area"); Clear();
    }
    static void Katyusha()
    {
        var part=Equip(6);part.GetComponent<GreyboxShoulderWeapon>().salvoFanAngle=0;part.GetComponent<GreyboxShoulderWeapon>().salvoLaunchSpacing=0;
        var direct=Dummy(part.muzzle.position+Vector3.forward*12,compound:3);var nearby=Dummy(part.muzzle.position+new Vector3(1.5f,0,12));
        int starts=0;var frames=new HashSet<int>();Action<Part> started=p=>{if(p==part)starts++;};Action<MechProjectile> fired=p=>{if(p.Source==part)frames.Add(Time.frameCount);};
        combat.WeaponAttackStarted+=started;combat.ShotFired+=fired;Fire(part);combat.WeaponAttackStarted-=started;combat.ShotFired-=fired;
        var shots=Object.FindObjectsByType<MechProjectile>().Where(p=>p.Source==part).ToArray();
        Check(shots.Length==10&&starts==1&&frames.Count==1&&part.WeaponCooldownCycle==1,"One Katyusha activation launches ten full-damage rockets in one frame and records one attack/cooldown cycle");
        foreach(var shot in shots)Step(shot,.3f);
        Check(Near(direct.CurrentHealth,600)&&Near(nearby.CurrentHealth,800),"Ten rockets each apply 20 direct + 20 splash; individual explosions may overlap without a damage cap");
        foreach(var shot in shots)Step(shot,1);
        Check(Near(direct.CurrentHealth,600)&&Near(nearby.CurrentHealth,800),"Compound bodies and spent projectiles never repeat a rocket's damage");
        Check(part.RemainingAmmo==90&&part.MagazineRounds==10&&part.IsReloading&&Near(part.CooldownDuration,5)&&!combat.RepeatsWhileHeld(PartCategory.ShoulderL),"Katyusha consumes the ten-round magazine atomically, reloads for 5s and requires one new press per salvo");
        Check(!combat.TryAttack(PartCategory.ShoulderL).Started,"A salvo is rejected while its reload/readiness interval is still active");
        TestMech.Advance(player,5.01f);Check(combat.TryAttack(PartCategory.ShoulderL).Started&&part.RemainingAmmo==80&&part.WeaponCooldownCycle==2,"The next admitted salvo consumes ten rounds and one further cooldown cycle");Clear();
    }
    static void SalvoEdges()
    {
        var part=Equip(6);Fire(part);var shots=Object.FindObjectsByType<MechProjectile>().Where(s=>s.Source==part).OrderBy(s=>s.Direction.x).ToArray();
        Check(shots.Length==10&&Near(Vector3.Angle(shots[0].Direction,shots[9].Direction),8)&&shots.Select(s=>s.Direction).Distinct().Count()==10,
            "The default salvo forms ten distinct directions across a configurable eight-degree fan");
        Check(shots.Select(s=>s.transform.position).Distinct().Count()==10,
            "The two-row primitive launcher starts all ten rockets at distinct, visible muzzle ports");Clear();
        var partial=Object.Instantiate(catalogue.weapons[6]);var data=Object.Instantiate(partial.data);
        for(int i=0;i<data.stats.Count;i++)if(data.stats[i].type==StatType.Ammo)data.stats[i]=new StatEntry{type=StatType.Ammo,value=13};
        partial.data=data;partial.KeepAuthoredStats();Check(equipment.TryEquip(partial,PartCategory.ShoulderL),"A partially stocked Katyusha equips through the same physical-part path");ClearLoose();Fire(partial);
        Check(partial.RemainingAmmo==3&&partial.MagazineRounds==3&&partial.IsReloading,"Reload preserves the last three reserve rounds rather than fabricating a full magazine");
        Clear();Fire(partial);shots=Object.FindObjectsByType<MechProjectile>().Where(s=>s.Source==partial).ToArray();
        Check(shots.Length==3&&partial.RemainingAmmo==0&&partial.MagazineRounds==0&&!partial.IsReloading,"A final partial magazine fires only its three actual rounds without negative ammo or an impossible reload");Clear();
        var rolled=Object.Instantiate(catalogue.weapons[6]);rolled.RollLootStats(87123);Check(equipment.TryEquip(rolled,PartCategory.ShoulderL),"A normally rolled Katyusha retains its equipment compatibility");ClearLoose();
        int count=rolled.MagazineRounds,ammo=rolled.RemainingAmmo;Fire(rolled);
        Check(rolled.HasRolledStats&&Object.FindObjectsByType<MechProjectile>().Count(s=>s.Source==rolled)==count&&rolled.RemainingAmmo==ammo-count,
            "Salvo size and ammo consumption follow the physical item's rolled magazine without overriding loot stats");Clear();
        var ring=catalogue.weapons[3].data;var launcher=catalogue.weapons[6].data;var coil=catalogue.weapons[8].data;
        float coilCycle=(coil.MagazineSize-1)*coil.ShotInterval+Mathf.Max(coil.ShotInterval,coil.GetStat(StatType.Reload));
        float coilDps=coil.MagazineSize*coil.GetStat(StatType.Damage)/coilCycle;
        Check(Near(ring.GetStat(StatType.Damage)/ring.ShotInterval,70)
            &&Near(launcher.MagazineSize*(launcher.GetStat(StatType.Damage)+launcher.GetStat(StatType.SplashDamage))/Mathf.Max(launcher.ShotInterval,launcher.GetStat(StatType.Reload)),80)
            &&Near(coilDps,14.285714f)&&Near(coilDps*catalogue.weapons[8].GetComponent<GreyboxShoulderWeapon>().maximumChainTargets,71.42857f),
            "Authored cadence/reload formulas produce 70 ring DPS, 80 nominal salvo DPS, and 14.29/71.43 Tesla single/group DPS");
    }
    static void Ring()
    {
        var part=Equip(3);var config=part.GetComponent<GreyboxShoulderWeapon>();Vector3 center=player.transform.position+Vector3.up*.5f;
        var front=Dummy(center+Vector3.forward*27.5f,compound:4);var side=Dummy(center+Vector3.right*27.5f);var safe=Dummy(center+Vector3.forward*10);
        var crossing=Dummy(center+Vector3.back*26.5f,new Vector3(1,3,3));
        Check(combat.TryAttack(PartCategory.ShoulderL).Started && Near(part.CooldownDuration,3),"Rock ring activates as Shoulder Melee with the new three-second cadence");
        for(int i=0;i<4;i++)Tick(config,.2f);
        Check(Near(front.CurrentHealth,790)&&Near(side.CurrentHealth,790)&&Near(crossing.CurrentHealth,790)&&safe.CurrentHealth==1000,
            "Unchanged 27-28m ring geometry deals 210 to intersecting bodies once and leaves the empty center safe");
        Check(config.LastHitCount==3&&!config.IsActive&&!combat.TryAttack(PartCategory.ShoulderL).Started,"Ring's per-activation history prevents compound/multi-frame repeats and visuals end before cooldown");Clear();
    }
    static void Tentacles()
    {
        var part=Equip(7);var first=Dummy(player.transform.position+new Vector3(0,.5f,12));var config=part.GetComponent<GreyboxShoulderWeapon>();
        Check(combat.TryAttack(PartCategory.ShoulderL).Started&&Near(first.CurrentHealth,950)&&Near(part.CooldownDuration,1/1.5f),"Tentacle input selects an enemy and deals 50 through the shared melee box at 1.5 activations/s");
        Tick(config,.3f);first.gameObject.SetActive(false);var behind=Dummy(player.transform.position+new Vector3(0,.5f,-10));TestMech.Advance(player,2);combat.TryAttack(PartCategory.ShoulderL);
        Check(Near(behind.CurrentHealth,950),"Successive tentacle attacks can select enemies behind the player without changing movement or camera control");Clear();
        var elevated=Dummy(player.transform.position+new Vector3(0,7,8));TestMech.Advance(player,2);combat.TryAttack(PartCategory.ShoulderL);
        Check(Near(elevated.CurrentHealth,950),"Tentacle strike orientation also reaches an elevated selected body within its actual range");Clear();
    }
    static void Tesla()
    {
        var part=Equip(8);var config=part.GetComponent<GreyboxShoulderWeapon>();Vector3 center=player.transform.position+Vector3.up*.5f;
        var chain=new List<CombatHealth>();for(int i=0;i<7;i++)chain.Add(Dummy(center+Vector3.forward*(5+i*3),compound:2));
        var friendly=Dummy(center+new Vector3(.5f,0,5),team:CombatTeam.Player);var far=Dummy(center+new Vector3(15,0,5));
        Check(combat.TryAttack(PartCategory.ShoulderL).Started&&config.LastHitCount==5,"Tesla activates a chain capped at five unique valid targets");
        Check(chain.Take(5).All(h=>Near(h.CurrentHealth,996))&&chain[5].CurrentHealth==1000&&chain[6].CurrentHealth==1000&&friendly.CurrentHealth==1000&&far.CurrentHealth==1000,"Tesla preserves 4 damage per target, chain spacing/count limits, compound deduplication and teams");
        Check(part.RemainingAmmo==69&&part.MagazineRounds==34&&Near(part.CooldownDuration,.2f)&&combat.RepeatsWhileHeld(PartCategory.ShoulderL)&&part.Stats.GetStat(StatType.MoveSpeed)==0,"Tesla pulses on held input at 5/s with existing ammo/cooldown and a neutral movement modifier");
        Clear();int ammo=part.RemainingAmmo;Check(!combat.TryAttack(PartCategory.ShoulderL).Started&&part.RemainingAmmo==ammo,"Tesla with no valid target neither consumes ammunition nor creates a chain");
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=center+Vector3.forward*4;wall.transform.localScale=new Vector3(20,20,.3f);targets.Add(wall);
        var blocked=Dummy(center+Vector3.forward*8);Physics.SyncTransforms();
        Check(!combat.TryAttack(PartCategory.ShoulderL).Started&&blocked.CurrentHealth==1000&&part.RemainingAmmo==ammo,"Tesla cannot acquire or consume ammunition against an enemy hidden behind scenery");Clear();
        var first=Dummy(center+Vector3.forward*5);var next=Dummy(center+new Vector3(3.5f,0,5));
        wall=GameObject.CreatePrimitive(PrimitiveType.Cube);wall.transform.position=center+new Vector3(1.75f,0,5);wall.transform.localScale=new Vector3(.3f,20,3);targets.Add(wall);Physics.SyncTransforms();
        Check(combat.TryAttack(PartCategory.ShoulderL).Started&&Near(first.CurrentHealth,996)&&next.CurrentHealth==1000&&config.LastHitCount==1,"Scenery also blocks a Tesla jump after a valid visible first hit");Clear();
    }
    sealed class BlockAll : ICombatDamageBlocker { public bool BlocksDamage(float amount,Vector3? sourcePosition)=>true; }
    static void TeslaEdges()
    {
        var part=Equip(8);var config=part.GetComponent<GreyboxShoulderWeapon>();Vector3 center=player.transform.position+Vector3.up*.5f;
        var first=Dummy(center+Vector3.forward*5);var tooFar=Dummy(center+new Vector3(4.2f,0,5));
        combat.TryAttack(PartCategory.ShoulderL);
        Check(Near(config.chainDistance,4)&&config.maximumChainTargets==5&&Near(first.CurrentHealth,996)&&tooFar.CurrentHealth==1000,
            "Tesla's four-metre maximum measures beam endpoints, not a large target's near collider edge");Clear();
        first=Dummy(center+Vector3.forward*5);var next=Dummy(center+new Vector3(3,0,5));config.lightningSeconds=.5f;
        combat.TryAttack(PartCategory.ShoulderL);Tick(config,.21f);TestMech.Advance(player,.21f);
        Check(config.IsActive&&combat.TryAttack(PartCategory.ShoulderL).Started&&Near(first.CurrentHealth,992)&&Near(next.CurrentHealth,992),
            "The next rapid pulse can hit both enemies again even while a longer previous visual is still active");
        next.gameObject.SetActive(false);TestMech.Advance(player,.21f);combat.TryAttack(PartCategory.ShoulderL);
        Check(config.LastHitCount==1&&config.GetComponentsInChildren<LineRenderer>().Count(l=>l.enabled)==1,
            "A shorter following pulse reuses lines and hides links left by the previous longer chain");Clear();
        first=Dummy(center+Vector3.forward*5);first.AddDamageBlocker(new BlockAll());combat.TryAttack(PartCategory.ShoulderL);
        Check(first.CurrentHealth==1000&&config.LastHitCount==0&&config.GetComponentsInChildren<LineRenderer>().All(l=>!l.enabled),
            "A fully blocked target does not produce a misleading damage arc");Clear();
        first=Dummy(center+Vector3.forward*5);next=Dummy(center+new Vector3(3,0,5));first.SetMaximum(2,true);combat.TryAttack(PartCategory.ShoulderL);
        Check(first.IsDead&&Near(next.CurrentHealth,996)&&config.LastHitCount==2,"Lightning continues safely when an earlier enemy dies during the same pulse");Clear();
    }
    static CombatHealth RealEnemy(string name, Vector3 contact)
    {
        var parent=new GameObject("Controlled Shoulder victim");parent.SetActive(false);targets.Add(parent);
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Small Enemies/Enemy_"+name+".prefab"),parent.transform);
        var fields=new SerializedObject(root.GetComponent<EnemyAgent>());fields.FindProperty("externalDecisions").boolValue=true;fields.ApplyModifiedPropertiesWithoutUndo();
        foreach(var script in root.GetComponentsInChildren<MonoBehaviour>(true))
            if(script is EnemyAttack||script is EnemyGraphAttack||script is Unity.Behavior.BehaviorGraphAgent||script is EnemyGraphController)script.enabled=false;
        foreach(var nav in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))nav.enabled=false;
        root.GetComponent<EnemyPartDrop>().enabled=false;parent.SetActive(true);
        var health=root.GetComponent<CombatHealth>();health.SetMaximum(2000,true);
        var hurtbox=root.GetComponentsInChildren<Collider>().First(c=>c.enabled&&!c.isTrigger&&c.GetComponentInParent<CombatHealth>()==health);
        root.transform.position+=contact-hurtbox.bounds.center;Physics.SyncTransforms();return health;
    }
    static void Overlapping()
    {
        var tentacle=Equip(7);var coil=Equip(8,PartCategory.ShoulderR);Vector3 center=player.transform.position+Vector3.up*.5f;
        var fodder=RealEnemy("Fodder",center+Vector3.forward*8);var mace=RealEnemy("mace",center+new Vector3(3,0,10));
        Check(combat.TryAttack(PartCategory.ShoulderL).Started&&combat.TryAttack(PartCategory.ShoulderR).Started,
            "Both shoulders admit overlapping Tentacle and Tesla activations against actual Fodder and Mace prefabs");
        Check(fodder.CurrentHealth<=1946&&mace.CurrentHealth<=1996&&fodder.GetComponent<EnemyAgent>().IsMeleePushed,
            "Real enemy hurtboxes, teams and the existing upright melee shove accept the new attacks");
        Check(tentacle.GetComponent<GreyboxShoulderWeapon>().IsActive&&coil.GetComponent<GreyboxShoulderWeapon>().IsActive,
            "Concurrent procedural effects remain active independently on their two physical parts");
        Tick(tentacle.GetComponent<GreyboxShoulderWeapon>(),.3f);Tick(coil.GetComponent<GreyboxShoulderWeapon>(),.3f);Clear();
        var shield=Equip(2,PartCategory.ShoulderR);var launcher=Equip(6);var health=player.GetComponent<CombatHealth>();
        combat.TryAttack(PartCategory.ShoulderR);var shot=Fire(launcher);
        Check(shot.IsLive&&!health.ApplyDamage(100,player.transform.position+Vector3.back*5)
            &&shield.GetComponent<GreyboxShoulderWeapon>().IsActive&&launcher.RemainingAmmo==90,
            "Shield blocks a hit without stopping the other Shoulder from firing a complete Katyusha salvo");Clear();
    }
}
