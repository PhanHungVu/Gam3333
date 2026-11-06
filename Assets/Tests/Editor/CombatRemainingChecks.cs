using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object=UnityEngine.Object;

// Uses actual player/enemy rigs and prefabs. Run only in a disposable project.
public static class CombatRemainingChecks
{
    const string Pending="CombatRemainingChecks.Pending";
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps=new Stack<IEnumerator>();
    static bool running; static int frame=-1,checks; static double stable,deadline;
    static T Get<T>(object target,string field)=>(T)target.GetType().GetField(field,Private).GetValue(target);
    static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private).SetValue(target,value);
    static void Check(bool value,string message) { if(!value) throw new Exception(message); checks++; Debug.Log("REMAINING PASS: "+message); }
    [InitializeOnLoadMethod] static void Register() { stable=EditorApplication.timeSinceStartup+3; EditorApplication.update+=Tick; }
    public static void RunBatch()
    {
        SessionState.SetBool("CombatRemainingChecks.Regression",true); Request();
    }
    public static void RunFocusedBatch()
    {
        SessionState.SetBool("CombatRemainingChecks.Regression",false); Request();
    }
    static void Request()
    {
        var root=Directory.GetParent(Application.dataPath).FullName;
        if(!Application.isBatchMode || !root.StartsWith(Path.GetTempPath(),StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only");
        SessionState.SetBool(Pending,true); stable=EditorApplication.timeSinceStartup+3;
    }
    static void Tick()
    {
        try
        {
            if(SessionState.GetBool(Pending,false) && !EditorApplication.isPlaying)
            {
                if(EditorApplication.isCompiling) { stable=EditorApplication.timeSinceStartup+3; return; }
                if(EditorApplication.timeSinceStartup<stable) return;
                SessionState.SetBool(Pending,false); CombatPolishSetup.Apply(); CombatMilestoneSetup.Apply();
                var menuReview=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
                EditorSceneManager.SaveScene(menuReview,"Assets/Tests/CombatResultMenuReview.unity");
                var retryReview=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
                EditorSceneManager.SaveScene(retryReview,"Assets/Tests/CombatResultRetryReview.unity");
                GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline=null;
                running=true; deadline=EditorApplication.timeSinceStartup+300; EditorApplication.EnterPlaymode();
            }
            if(!running || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame==Time.frameCount) return;
            frame=Time.frameCount; PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("Remaining milestone checks timed out");
            if(steps.Count==0) steps.Push(Run());
            while(steps.Count>0)
            {
                var current=steps.Peek(); if(!current.MoveNext()) { steps.Pop(); continue; }
                if(current.Current is IEnumerator nested) { steps.Push(nested); continue; } return;
            }
            Finish(null);
        }
        catch(Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        running=false; Time.timeScale=1; PreMissionReliabilityChecks.EndLogAudit();
        if(error!=null) Debug.LogException(error);
        Debug.Log("COMBAT REMAINING CHECKS "+(error==null?"COMPLETE: ":"FAILED: ")+checks); EditorApplication.Exit(error==null?0:1);
    }
    static IEnumerator Frames(int n=2) { int end=Time.frameCount+n; while(Time.frameCount<end) yield return null; }
    static IEnumerator Delay(float seconds) { float end=Time.time+seconds; while(Time.time<end) yield return null; }
    static void ClearCorpses() { foreach(var corpse in Object.FindObjectsByType<CombatRagdoll>()) Object.DestroyImmediate(corpse.gameObject); }
    static void StopEnemy(GameObject enemy)
    {
        if(enemy.TryGetComponent<EnemyGraphController>(out var controller)) controller.enabled=false;
        if(enemy.TryGetComponent<EnemyAgent>(out var agent)) agent.enabled=false;
        if(enemy.TryGetComponent<Unity.Behavior.BehaviorGraphAgent>(out var graph)) graph.enabled=false;
        if(enemy.TryGetComponent<EnemyAttack>(out var attack)) attack.enabled=false;
        if(enemy.TryGetComponent<EnemyPartDrop>(out var drop)) drop.enabled=false;
    }
    static GameObject Enemy(string path,Vector3 position)
    {
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/"+path),position,Quaternion.identity);
        StopEnemy(root); return root;
    }
    static IEnumerator Run()
    {
        Application.runInBackground=true; Time.timeScale=1; PreMissionReliabilityChecks.BeginLogAudit();
        if(SessionState.GetBool("CombatRemainingChecks.Regression",true))
            yield return (IEnumerator)typeof(CombatPolishChecks).GetMethod("Run",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
        yield return CombatHitFeelChecks.Run(Check);
        yield return AbilityReadyVfxChecks.Run(Check);
        yield return WeaponReadyVfxChecks.Run(Check);
        ClearCorpses(); yield return Frames();
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name="Physics review floor";
        floor.transform.position=new Vector3(0,-.5f,0); floor.transform.localScale=new Vector3(60,1,60);
        var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath),new Vector3(0,1.2f,0),Quaternion.identity);
        var input=player.GetComponent<MechPlayerInput>(); input.enabled=false;
        var movement=player.GetComponent<MechMovementController>(); movement.enabled=false;
        yield return Frames(3);
        var mission=MissionOutcomeController.Find(player.scene); var equipment=player.GetComponent<EquipManager>(); var health=player.GetComponent<CombatHealth>();
        var abilities=Object.FindAnyObjectByType<MechAbilitiesHud>(); var weapons=Object.FindAnyObjectByType<MechWeaponsHud>();
        Check(mission!=null && mission.Participants.Count==1,"Actual player bootstraps one local mission participant");
        Check(Object.FindObjectsByType<MechWeaponsHud>().Length==1 && weapons.Pods.Count==4,"Scenes without an authored weapon HUD get one shared fallback HUD");
        foreach(var diagnostic in abilities.Pods) Debug.Log("ABILITY AUDIT: "+diagnostic.Ability.Stats.partName+"; selected="+(diagnostic.CurrentIcon!=null?diagnostic.CurrentIcon.name:"none"));
        Check(abilities.Pods.Count==3 && abilities.Pods.All(pod=>pod.CurrentIcon!=null),"All three ability pods resolve imported artwork without changing their layout");
        var head=equipment.GetEquipped(PartCategory.Head); input.enabled=true;
        int requests=0; input.AbilityRequested+=(slot,part)=>requests++;
        input.RequestHeadAbility(); input.enabled=false;
        Check(requests==1 && !PartAbilityCooldown.For(head).IsCoolingDown,"Raw head request remains an exposed hook and does not fabricate a cooldown");
        Check(PartAbilityCooldown.For(head).ConfirmActivation(.8f) && !PartAbilityCooldown.For(head).ConfirmActivation(.8f),"Gameplay-confirmed activation owns the cooldown and rejects overlapping activations");
        yield return Frames();
        var pod=abilities.Pods.Single(p=>p.Ability==head);
        Check(pod.IsCoolingDown && Get<Image>(pod,"icon").color.a<1 && Get<TextMeshProUGUI>(pod,"cooldownLabel").color.a==1,"Accepted cooldown dims its icon and shows a white countdown");
        var loose=equipment.Unequip(PartCategory.Head); Check(equipment.TryEquip(loose,PartCategory.Head),"Physical head can be unequipped and reattached");
        yield return Frames(1);
        Check(abilities.Pods.Single(p=>p.Ability==head).IsCoolingDown,"Equipment/HUD rebuild cannot reset a physical part cooldown");
        yield return Delay(1);
        Check(!abilities.Pods.Single(p=>p.Ability==head).IsCoolingDown && Get<Image>(abilities.Pods.Single(p=>p.Ability==head),"icon").color==Color.white,"Cooldown expiry restores the ready icon");
        var state=PartAbilityCooldown.For(head); state.Synchronize(2,10); state.Synchronize(float.NaN,10);
        Check(state.Remaining>1.8f && state.Duration==10,"Authoritative remaining cooldown updates survive invalid data");
        var ranged=weapons.Pods.First(p=>WeaponsHudPod.HasAmmo(p.Weapon.Stats)).Weapon;
        Check(WeaponsHudPod.AmmoText(ranged)==ranged.MagazineRounds+" / "+ranged.RemainingAmmo,"Existing magazine/total notation remains the default");
        Check(WeaponsHudPod.AmmoText(ranged,WeaponsHudPod.AmmoDenominator.MagazineCapacity).EndsWith(" / "+ranged.Stats.MagazineSize),"Optional magazine-capacity notation uses actual weapon data");
        yield return Capture("hud-abilities",new Vector3(10,7,-12),new Vector3(0,2,0));
        var combat=player.GetComponent<MechCombat>();
        var fired=combat.TryAttack(ranged.Slot.Value);
        Check(fired.Started && ranged.CooldownDuration>0 && ranged.CooldownProgress<.05f,"Accepted weapon attack records its complete fire/reload wait on the physical part");
        yield return Frames();
        var weaponPod=weapons.Pods.Single(p=>p.Weapon==ranged); var line=Get<Image>(weaponPod,"cooldownLine");
        Check(weaponPod.IsCoolingDown && line.enabled && !line.raycastTarget && line.rectTransform.anchorMin.y<.2f
            && Get<Image>(weaponPod,"icon").color.r<1,"Accepted weapon cooldown dims its artwork and starts a non-interactive line at the bottom");
        float readyAt=ranged.ReadyAt;
        Check(!combat.TryAttack(ranged.Slot.Value).Started && ranged.ReadyAt==readyAt,"Rejected repeated fire does not restart the cooldown sweep");
        yield return Delay(ranged.CooldownDuration*.45f);
        weaponPod=weapons.Pods.Single(p=>p.Weapon==ranged);
        Check(weaponPod.CooldownProgress>.4f && weaponPod.CooldownProgress<.7f
            && Mathf.Abs(Get<Image>(weaponPod,"cooldownLine").rectTransform.anchorMin.y-weaponPod.CooldownProgress)<.08f,"Weapon sweep rises with the real reload/fire clock");
        state.Synchronize(5,10); yield return Frames();
        yield return Capture("hud-weapon-and-ability-cooldown",new Vector3(10,7,-12),new Vector3(0,2,0));
        var weaponSlot=ranged.Slot.Value; loose=equipment.Unequip(weaponSlot);
        Check(equipment.TryEquip(loose,weaponSlot) && ranged.ReadyAt==readyAt,"Removing and re-equipping a physical weapon preserves its cooldown deadline");
        yield return Frames();
        weaponPod=weapons.Pods.Single(p=>p.Weapon==ranged);
        Check(weaponPod.IsCoolingDown && Get<Image>(weaponPod,"cooldownLine").enabled && weaponPod.CooldownProgress>.4f,"Rebuilt weapon pod resumes the sweep at its existing progress");
        yield return Delay(ranged.CooldownRemaining+.1f);
        Check(!weaponPod.IsCoolingDown && !Get<Image>(weaponPod,"cooldownLine").enabled && Get<Image>(weaponPod,"icon").color==Color.white,"Weapon ready state hides the line and restores normal brightness");
        yield return CheckMagazineSweep(player.GetComponent<MechPlayerRig>().GameplayCamera);

        movement.enabled=true;
        for(int i=0;i<12;i++) movement.Tick(default(MechLocomotionInput),null,.05f);
        Check(!movement.ApplyKnockback(Vector3.forward,float.NaN,.3f) && !movement.ApplyKnockback(Vector3.zero,15,.3f),"External motion rejects invalid or directionless impulses");
        var spinner=Enemy("Small Enemies/Enemy_spinner.prefab",player.transform.position-Vector3.forward*.65f);
        var attack=spinner.GetComponent<EnemySpinnerAttack>(); var duplicate=new GameObject("Compound hurt collider"); duplicate.transform.SetParent(player.transform,false); duplicate.AddComponent<BoxCollider>();
        Physics.SyncTransforms(); float hp=health.CurrentHealth; attack.BeginAttack(); attack.TickAttack(.02f);
        Check(health.CurrentHealth<hp && movement.IsKnockedBack,"Actual Spinner accepted charge hit applies damage and CharacterController knockback");
        float after=health.CurrentHealth; attack.TickAttack(.02f);
        Check(health.CurrentHealth==after,"One Spinner charge cannot repeat damage or knockback against compound colliders");
        // Remove the deliberately overlapping hit fixture before measuring unobstructed travel.
        Object.DestroyImmediate(spinner); Object.DestroyImmediate(duplicate); Physics.SyncTransforms();
        yield return Delay(.16f); // Knockback resumes after the intentional impact hold.
        var origin=player.transform.position;
        movement.Tick(default(MechLocomotionInput),null,.1f);
        Check(Vector3.Distance(origin,player.transform.position)>.3f,"Spinner impulse produces visible collision-aware table-space displacement");
        for(int i=0;i<12;i++) movement.Tick(default(MechLocomotionInput),null,.05f);
        float distance=Vector3.ProjectOnPlane(player.transform.position-origin,Vector3.up).magnitude;
        Debug.Log("SPINNER KNOCKBACK DISTANCE: "+distance);
        Check(distance>10 && distance<10.8f,"Actual small Spinner pushes the idle player farther than its nine-metre charge reach on open ground");
        Check(!movement.IsKnockedBack && movement.ExternalVelocity==Vector3.zero,"External velocity expires without changing normal movement state permanently");
        var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position=player.transform.position+Vector3.forward*2; wall.transform.localScale=new Vector3(10,5,.2f); Physics.SyncTransforms();
        float wallZ=wall.transform.position.z; movement.ApplyKnockback(Vector3.forward,30,.5f);
        for(int i=0;i<12;i++) movement.Tick(default(MechLocomotionInput),null,.05f);
        Check(player.transform.position.z<wallZ,"Knockback respects a solid wall instead of teleporting through it");
        movement.ApplyKnockback(Vector3.forward,10,.5f); movement.enabled=false;
        Check(!movement.IsKnockedBack,"Disabling/death clears any unfinished knockback"); Object.DestroyImmediate(wall);
        movement.enabled=true;
        // Let native physics publish the manually simulated controller's new query bounds before the next encounter.
        yield return Delay(.1f);
        var orbit=Enemy("Elite Enemies/Enemy_Elite_RockRope.prefab",player.transform.position-Vector3.right*3.5f);
        yield return Frames(3);
        var rope=orbit.GetComponent<EnemyRopeAttack>(); Physics.SyncTransforms();
        // Align this directly invoked area check with the actual player's collider, independent of prefab pivot height.
        Set(rope,"orbitVerticalOffset",player.GetComponent<CharacterController>().bounds.center.y-orbit.transform.position.y); hp=health.CurrentHealth;
        typeof(EnemyRopeAttack).GetMethod("ResolveOrbitArea",Private).Invoke(rope,null);
        Check(health.CurrentHealth<hp && movement.IsKnockedBack,"Charging orbit enemy also knocks back an accepted victim anywhere in its filled spin circle");
        yield return Delay(.16f); origin=player.transform.position;
        movement.Tick(default(MechLocomotionInput),null,.05f); after=health.CurrentHealth;
        float remainingImpulse=Get<float>(movement,"externalRemaining");
        typeof(EnemyRopeAttack).GetMethod("ResolveOrbitArea",Private).Invoke(rope,null);
        Check(health.CurrentHealth==after && Get<float>(movement,"externalRemaining")==remainingImpulse,"One orbit victim cannot receive repeated damage or restarted knockback while still inside the circle");
        for(int i=0;i<13;i++) movement.Tick(default(MechLocomotionInput),null,.05f);
        distance=Vector3.ProjectOnPlane(player.transform.position-origin,Vector3.up).magnitude;
        Check(distance>10 && distance<10.8f && distance>rope.OrbitRadius,"Actual orbit hit pushes the player beyond the full six-metre attack radius on open ground");
        movement.ClearExternalMotion();
        Object.DestroyImmediate(orbit); movement.enabled=false;

        // Explicit team roster: one death must not mean the team has been eliminated.
        var teammate=GameObject.CreatePrimitive(PrimitiveType.Capsule); teammate.name="Registered test participant";
        teammate.transform.position=new Vector3(5,1,0); var teammateHealth=teammate.AddComponent<CombatHealth>(); teammateHealth.Team=CombatTeam.Player;
        Check(mission.RegisterParticipant("test-remote",teammateHealth,false),"Gameplay can register an additional actual participant without a networking dependency");
        teammateHealth.ApplyDamage(1000); yield return Frames();
        Check(mission.Outcome==MissionOutcome.InProgress,"One participant dying does not fail a mission while another is alive");
        var hold=new HoldZoneRules(5,0,.1f); hold.Tick(.1f,true); hold.Tick(.1f,false); hold.Tick(.1f,false);
        Check(hold.State==HoldZoneState.Failed && mission.Outcome==MissionOutcome.InProgress,"Retrying KOTH failure remains separate from terminal mission failure");
        int finishes=0; mission.Finished+=_=>finishes++;
        health.ApplyDamage(health.CurrentHealth+1); yield return Frames(3);
        Check(mission.Outcome==MissionOutcome.Failed && finishes==1 && mission.View.Title=="MISSION FAILED","Eliminating the registered team shows one failure screen");
        Check(Time.timeScale==1 && Cursor.lockState==CursorLockMode.None && !input.enabled,"Results release the cursor and block gameplay without a global time pause");
        Check(!mission.ReportSuccess() && !mission.ReportFailure("Again") && finishes==1,"Terminal mission results cannot be overwritten or shown twice");
        yield return Capture("mission-failure",new Vector3(10,7,-12),Vector3.up);
        Object.DestroyImmediate(mission.gameObject); Object.DestroyImmediate(teammate);
        var corpse=Object.FindObjectsByType<CombatRagdoll>().FirstOrDefault();
        Check(!player.activeSelf && corpse!=null && corpse.BodyCount>=6 && corpse.BodyCount<=14,"Real player death preserves gameplay deactivation and creates a bounded articulated corpse");
        Check(corpse.GetComponentsInChildren<ConfigurableJoint>().Length==corpse.BodyCount-1 && corpse.GetComponentsInChildren<CombatHealth>().Length==0
            && corpse.GetComponentsInChildren<Part>().Length==0 && corpse.GetComponentsInChildren<Animator>().Length==0,"Corpse contains connected physics and visual meshes without AI, health, parts or animation drivers");
        yield return Delay(2);
        Check(corpse.Bodies.All(body=>!float.IsNaN(body.position.x) && body.position.y>-10 && body.transform.lossyScale==Vector3.one),"Scaled artist rigs simulate on uniform physics proxies without exploding or falling through the floor");
        var corpseCollider=corpse.Bodies[0].GetComponent<Collider>();
        var live=new GameObject("New actor after corpse",typeof(CharacterController)); live.transform.position=new Vector3(40,2,0); live.AddComponent<CombatHealth>();
        Check(Physics.GetIgnoreCollision(corpseCollider,live.GetComponent<Collider>()),"Newly spawned actors ignore existing corpse physics contacts");
        var coverTarget=GameObject.CreatePrimitive(PrimitiveType.Cube); coverTarget.AddComponent<CombatHealth>(); Vector3 coverPoint=corpseCollider.bounds.center;
        coverTarget.transform.position=coverPoint+Vector3.forward*4; Physics.SyncTransforms();
        var blast=new CombatBlast();
        Check(!blast.LineBlocked(coverPoint-Vector3.forward*4,coverTarget.GetComponent<Collider>(),null,~0),"Corpse colliders never become combat cover");
        var solid=GameObject.CreatePrimitive(PrimitiveType.Cube); solid.transform.position=coverPoint+Vector3.forward*2; Physics.SyncTransforms();
        Check(blast.LineBlocked(coverPoint-Vector3.forward*4,coverTarget.GetComponent<Collider>(),null,~0),"Corpse filtering retains real scenery blocking");
        Object.DestroyImmediate(solid); Object.DestroyImmediate(coverTarget); Object.DestroyImmediate(live);
        yield return Capture("ragdoll-player",new Vector3(8,6,-9),new Vector3(player.transform.position.x,1,player.transform.position.z));
        corpse.Bodies[0].linearVelocity=Vector3.right; corpse.TickLifetime(0,0);
        foreach(var body in corpse.Bodies) { body.linearVelocity=Vector3.zero; body.angularVelocity=Vector3.zero; body.Sleep(); }
        corpse.TickLifetime(4.9f,0); Check(corpse.gameObject.activeSelf,"Resting corpse survives until the complete five-second rest interval");
        corpse.Bodies[0].linearVelocity=Vector3.right; corpse.TickLifetime(.1f,0);
        Check(corpse.RestAge==0,"A moving corpse restarts its continuous rest interval");
        foreach(var body in corpse.Bodies) { body.linearVelocity=Vector3.zero; body.angularVelocity=Vector3.zero; body.Sleep(); }
        corpse.TickLifetime(5.1f,0); Check(!corpse.gameObject.activeSelf,"Five continuous resting seconds despawn the corpse");
        yield return Frames(); Object.DestroyImmediate(player); ClearCorpses();

        foreach(var relative in new[]{"Small Enemies/Enemy_sword.prefab","Small Enemies/Enemy_crossbow.prefab","Small Enemies/Enemy_spinner.prefab",
            "Elite Enemies/Enemy_Elite_Cannon.prefab","Elite Enemies/Enemy_Elite_RockRope.prefab","Elite Enemies/Enemy_Elite_RockRopePull.prefab"})
        {
            var enemy=Enemy(relative,new Vector3(12,1,0)); var enemyHealth=enemy.GetComponent<CombatHealth>(); enemyHealth.ApplyDamage(100000);
            var ragdoll=Object.FindAnyObjectByType<CombatRagdoll>();
            Check(!enemy.activeSelf && ragdoll!=null && ragdoll.BodyCount>1 && ragdoll.BodyCount<=14,relative+" produces physical modular/bone death without changing attack logic");
            Object.DestroyImmediate(enemy); ClearCorpses();
        }
        var dropEnemy=Enemy("Small Enemies/Enemy_sword.prefab",new Vector3(14,1,0));
        var drop=dropEnemy.GetComponent<EnemyPartDrop>();
        Set(drop,"partPrefabs",new[]{AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_sword.prefab").GetComponent<Part>()});
        Set(drop,"dropChancePercent",100f); drop.enabled=true; int deathEvents=0;
        dropEnemy.GetComponent<CombatHealth>().Died+=()=>deathEvents++;
        var beforeDrop=new HashSet<Part>(Object.FindObjectsByType<Part>()); dropEnemy.GetComponent<CombatHealth>().ApplyDamage(100000);
        var actualDrops=Object.FindObjectsByType<Part>().Where(part=>!beforeDrop.Contains(part)).ToArray();
        Check(deathEvents==1 && actualDrops.Length==1 && !actualDrops[0].isEquipped && Object.FindAnyObjectByType<CombatRagdoll>()!=null,
            "Ragdoll death preserves one normal kill event and the actual guaranteed part drop");
        Object.DestroyImmediate(actualDrops[0].gameObject); Object.DestroyImmediate(dropEnemy); ClearCorpses();
        var visual=GameObject.CreatePrimitive(PrimitiveType.Cube); visual.transform.position=new Vector3(10,2,0);
        var limited=CombatRagdoll.Spawn(visual.transform,Vector3.zero,1,14,5,1);
        Check(limited!=null && CombatRagdoll.Spawn(visual.transform,Vector3.zero,1)==null,"Concurrent ragdoll budget skips excess physics while death gameplay stays independent");
        limited.TickLifetime(0,1.1f); Check(!limited.gameObject.activeSelf,"Maximum lifetime cleans up unsettled corpses even while scaled time is stopped");
        yield return Frames(); Object.DestroyImmediate(visual);

        var resultActor=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath),new Vector3(0,1,0),Quaternion.identity);
        resultActor.GetComponent<MechPlayerInput>().enabled=false; resultActor.GetComponent<MechMovementController>().enabled=false;
        yield return Frames(3); mission=MissionOutcomeController.Find(resultActor.scene);
        var second=GameObject.CreatePrimitive(PrimitiveType.Capsule); var secondHealth=second.AddComponent<CombatHealth>();
        Check(mission.RegisterParticipant("second-registered",secondHealth,false) && mission.ReportSuccess(),"Explicit success accepts the registered participant roster");
        yield return Frames(3);
        var models=mission.View.GetComponent<MissionResultModels>();
        Check(mission.View.Title=="MISSION COMPLETE" && models.DisplayedParticipants==2,"Success presentation uses only the two registered participant models");
        var stage=Get<GameObject>(models,"stage");
        Check(stage.GetComponentsInChildren<CombatHealth>(true).Length==0 && stage.GetComponentsInChildren<MechPlayerInput>(true).Length==0
            && stage.GetComponentsInChildren<Part>(true).Length==0 && stage.GetComponentsInChildren<Collider>(true).Length==0 && Time.timeScale==1,
            "Success model presentation adds no active combat scripts, physics colliders or global pause");
        yield return Capture("mission-success",new Vector3(10,7,-12),Vector3.up);
        Object.DestroyImmediate(mission.gameObject); Object.DestroyImmediate(second); Object.DestroyImmediate(resultActor); yield return Frames();

        mission=MissionOutcomeController.Ensure(floor.scene); mission.BeginParticipantRegistration();
        var dead=GameObject.CreatePrimitive(PrimitiveType.Cube).AddComponent<CombatHealth>(); dead.ApplyDamage(1000);
        mission.RegisterParticipant("late-roster",dead); yield return Frames();
        Check(mission.Outcome==MissionOutcome.InProgress,"Asynchronous roster registration cannot fail before gameplay seals it");
        mission.CompleteParticipantRegistration(); yield return Frames();
        Check(mission.Outcome==MissionOutcome.Failed,"Sealing an eliminated roster resolves the terminal failure");
        Object.DestroyImmediate(mission.gameObject); Object.DestroyImmediate(dead.gameObject);
        mission=MissionOutcomeController.Ensure(floor.scene);
        Check(mission.ReportFailure("Objective destroyed.") && mission.Reason=="Objective destroyed.","Gameplay terminal objective failure hook displays its supplied reason");
        Object.DestroyImmediate(mission.gameObject);
        mission=MissionOutcomeController.Ensure(floor.scene);
        var chunk=new GameObject("Extraction test chunk"); chunk.AddComponent<ChunkDefinition>();
        var extract=chunk.AddComponent<ExtractZone>(); var tracker=new GameObject("Mission tracker integration").AddComponent<MissionTracker>();
        yield return Frames(3); Check(extract.IsOpen,"Existing mission tracker opens extraction through its normal objective flow");
        var extractRules=(HoldZoneRules)typeof(HoldZone).GetField("rules",Private).GetValue(extract);
        extractRules.Tick(.1f,true); extractRules.Tick(100,true); yield return Frames(2);
        Check(mission.Outcome==MissionOutcome.Succeeded && Time.timeScale==1,"Actual extraction completion reaches the success screen instead of freezing global time");
        Object.DestroyImmediate(tracker.gameObject); Object.DestroyImmediate(chunk); Object.DestroyImmediate(mission.gameObject); Object.DestroyImmediate(floor); ClearCorpses();
        mission=MissionOutcomeController.Ensure(UnityEngine.SceneManagement.SceneManager.GetActiveScene()); mission.ReportFailure("Navigation test.");
        Get<Button>(mission.View,"retry").onClick.Invoke(); yield return SceneChanged(mission);
        Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path=="Assets/Tests/CombatResultRetryReview.unity","Retry button reloads the active gameplay scene through its real callback");
        mission=MissionOutcomeController.Ensure(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Set(mission,"mainMenuScene","Assets/Tests/CombatResultMenuReview.unity"); mission.ReportFailure("Navigation test.");
        Get<Button>(mission.View,"menu").onClick.Invoke(); yield return SceneChanged(mission);
        Check(UnityEngine.SceneManagement.SceneManager.GetActiveScene().path=="Assets/Tests/CombatResultMenuReview.unity","Main Menu button loads its configured scene through its real callback");
        PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
    }
    static IEnumerator SceneChanged(MissionOutcomeController previous)
    {
        float until=Time.realtimeSinceStartup+15;
        while(previous!=null) { if(Time.realtimeSinceStartup>until) throw new Exception("Result navigation timed out"); yield return null; }
        yield return Frames(3);
    }
    static IEnumerator CheckMagazineSweep(Camera camera)
    {
        var fixture=new GameObject("Magazine cooldown fixture"); fixture.SetActive(false);
        var combat=fixture.AddComponent<MechCombat>(); Set(combat,"gameplayCamera",camera);
        fixture.transform.position=new Vector3(100,3,100);
        var data=TestMech.WithMagazine(TestMech.Ranged("Cannon",PartType.Shoulder,PartSubtype.Projectile,1,6,.5f,2,20,.1f),4,.8f);
        var part=TestMech.Equip(fixture,(PartCategory.ShoulderL,data))[0]; fixture.SetActive(true);
        var pod=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Combat HUD/WeaponsHUDPodPrefab.prefab")).GetComponent<WeaponsHudPod>(); pod.Bind(part);
        Check(combat.TryAttack(PartCategory.ShoulderL).Started,"Multi-round magazine accepts a real shot for manual-reload verification");
        yield return Delay(.55f);
        Check(combat.TryReload(PartCategory.ShoulderL) && Mathf.Abs(part.CooldownDuration-.8f)<.05f,"Partial-magazine reload starts a sweep for its actual reload duration");
        yield return Delay(.4f);
        Check(pod.IsCoolingDown && pod.CooldownProgress>.4f && pod.CooldownProgress<.7f && Get<Image>(pod,"cooldownLine").enabled,"Manual reload advances the same weapon cooldown sweep");
        float deadline=part.ReadyAt;
        Check(!combat.TryReload(PartCategory.ShoulderL) && deadline==part.ReadyAt,"Repeated reload requests cannot restart an active sweep");
        yield return Delay(part.CooldownRemaining+.1f);
        for(int i=0;i<5;i++) { TestMech.Advance(fixture,1); combat.TryAttack(PartCategory.ShoulderL); }
        yield return Delay(part.CooldownRemaining+.1f);
        Check(part.RemainingAmmo==0 && !pod.IsCoolingDown && !Get<Image>(pod,"cooldownLine").enabled && Get<Image>(pod,"icon").color.r<1,"Exhausted ammunition stays dim without falsely displaying a cooldown");
        foreach(var shot in Object.FindObjectsByType<MechProjectile>()) if(shot.Source==part) Object.DestroyImmediate(shot.gameObject);
        Object.DestroyImmediate(pod.gameObject); Object.DestroyImmediate(fixture); Object.DestroyImmediate(data);
    }
    static IEnumerator Capture(string name,Vector3 position,Vector3 look)
    {
        if(SystemInfo.graphicsDeviceType==GraphicsDeviceType.Null) yield break;
        var camera=new GameObject("Review camera").AddComponent<Camera>(); camera.transform.SetPositionAndRotation(position,Quaternion.LookRotation(look-position));
        camera.backgroundColor=new Color(.08f,.1f,.13f); camera.clearFlags=CameraClearFlags.SolidColor;
        var light=new GameObject("Review light").AddComponent<Light>(); light.type=LightType.Directional; light.transform.rotation=Quaternion.Euler(45,-25,0);
        var canvases=Object.FindObjectsByType<Canvas>();
        foreach(var canvas in canvases) { canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1; }
        var target=new RenderTexture(1920,1080,24); camera.targetTexture=target; yield return Frames(2); camera.Render();
        var old=RenderTexture.active; RenderTexture.active=target; var picture=new Texture2D(1920,1080,TextureFormat.RGB24,false);
        picture.ReadPixels(new Rect(0,0,1920,1080),0,0); picture.Apply();
        var folder=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"CombatRemainingCaptures"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,name+".png"),picture.EncodeToPNG());
        RenderTexture.active=old; camera.targetTexture=null; target.Release();
        foreach(var canvas in canvases) if(canvas!=null) { canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.worldCamera=null; }
        Object.DestroyImmediate(picture); Object.DestroyImmediate(target); Object.DestroyImmediate(camera.gameObject); Object.DestroyImmediate(light.gameObject);
    }
}
