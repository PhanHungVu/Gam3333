using System;
using System.Collections;
using System.Reflection;
using Unity.Cinemachine;
using UnityEngine;
using Object=UnityEngine.Object;

// Accepted real damage, real weapon poses, physics and actor-local recovery; no fabricated hit events.
public static class CombatHitFeelChecks
{
    static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Set(object target,string field,object value)=>target.GetType().GetField(field,Private).SetValue(target,value);
    static T Get<T>(object target,string field)=>(T)target.GetType().GetField(field,Private).GetValue(target);
    static IEnumerator Frames(int n=2) { int end=Time.frameCount+n; while(Time.frameCount<end) yield return null; }
    static IEnumerator Delay(float seconds) { float end=Time.unscaledTime+seconds; while(Time.unscaledTime<end) yield return null; }
    public static IEnumerator Run(Action<bool,string> check)
    {
        var staging=new GameObject("Hit feel fixture"); staging.SetActive(false);
        var player=Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath),staging.transform);
        player.transform.position=new Vector3(200,3,200);
        player.GetComponent<MissionParticipant>().enabled=false; player.GetComponent<MechPlayerInput>().enabled=false;
        player.GetComponent<MechLocomotionDebug>().enabled=false; staging.SetActive(true); yield return Frames(3);
        var health=player.GetComponent<CombatHealth>(); var feedback=player.GetComponent<CombatFeedback>();
        var movement=player.GetComponent<MechMovementController>(); var equipment=player.GetComponent<EquipManager>();
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position=new Vector3(200,-.5f,200); floor.transform.localScale=new Vector3(30,1,30);
        Physics.SyncTransforms(); for(int i=0;i<15;i++) movement.Tick(default(MechLocomotionInput),null,.05f);
        var sword=equipment.GetEquipped(PartCategory.ArmL); var driver=sword.GetComponent<MechWeaponAnimator>();
        var enemy=GameObject.CreatePrimitive(PrimitiveType.Cube); enemy.name="Accepted hit target"; enemy.transform.position=player.transform.position+Vector3.forward*3;
        var victim=enemy.AddComponent<CombatHealth>(); victim.Team=CombatTeam.Enemy; victim.SetMaximum(1000,true);
        var targetAnimator=enemy.AddComponent<Animator>(); targetAnimator.speed=.7f;
        var duplicate=new GameObject("Compound collider"); duplicate.transform.SetParent(enemy.transform,false); duplicate.AddComponent<SphereCollider>();
        Physics.SyncTransforms(); var blast=new CombatBlast();
        var control=player.GetComponent<MechPlayerRig>().GameplayCamera.GetComponent<CameraControlThirdPerson>();
        var rig=Get<CinemachineCamera>(control,"cinemachineCamera"); var follow=rig.Follow; var look=rig.LookAt; float fov=rig.Lens.FieldOfView;
        check(Get<float>(feedback,"damageImpulseStrength")>=.16f && Get<float>(feedback,"damageImpulseDuration")>=.24f,"Damage shake is configured stronger than the previous .08 / .20s profile");
        driver.PlayAttack(1); driver.Tick(.05f); float phase=driver.NormalizedTime; int hitShakes=feedback.HitImpulseCount;
        var result=blast.ResolveBox(enemy.transform.position,Vector3.one,Quaternion.identity,5,player.transform,~0,player.transform.position+Vector3.up,source:sword);
        check(result.Hits==1 && victim.CurrentHealth==995 && feedback.HitImpulseCount==hitShakes+1,"One accepted melee victim emits one hit-confirm shake despite compound colliders");
        check(CombatHitStop.IsFrozen(player.transform) && CombatHitStop.IsFrozen(enemy.transform) && targetAnimator.speed==0,"Melee hitstop freezes only the attacker and struck actor animation");
        driver.Tick(.1f); check(Mathf.Approximately(driver.NormalizedTime,phase),"Real authored weapon pose remains held during hitstop");
        Vector3 origin=player.transform.position; movement.Tick(new MechLocomotionInput {move=Vector2.up},null,.1f);
        check(player.transform.position==origin,"Hitstop holds the existing CharacterController instead of replacing or teleporting it");
        movement.Tick(new MechLocomotionInput {quickBoost=true},null,.02f);
        var stop=player.GetComponent<CombatHitStop>(); int count=stop.StopsStarted;
        for(int i=0;i<10;i++) CombatHitStop.Request(player.transform,.2f,feedback);
        check(stop.StopsStarted==count && stop.Remaining<=.101f,"Repeated pellets/hits cannot accumulate an unbounded pause");
        var unrelated=GameObject.CreatePrimitive(PrimitiveType.Cube); unrelated.transform.position=new Vector3(300,30,300);
        var body=unrelated.AddComponent<Rigidbody>(); body.useGravity=false; body.linearVelocity=Vector3.forward;
        var oldMode=Physics.simulationMode;
        try { Physics.simulationMode=SimulationMode.Script; var before=body.position; Physics.Simulate(.02f);
            check(Time.timeScale==1 && body.position.z>before.z,"The world clock and unrelated physics continue during the actor's hitstop"); }
        finally { Physics.simulationMode=oldMode; }
        check(rig.Follow==follow && rig.LookAt==look && rig.Lens.FieldOfView==fov,"Hit feedback preserves camera framing, follow/lock targets and lens");
        yield return Delay(.22f);
        check(!stop.IsStopped && !CombatHitStop.IsFrozen(enemy.transform) && Mathf.Abs(targetAnimator.speed-.7f)<.001f,"Hitstop expires automatically and restores the original animation speed");
        driver.Tick(.05f); check(driver.CurrentPose==MechWeaponAnimator.PoseState.Neutral || driver.NormalizedTime>phase,
            "The actual weapon animation resumes or completes naturally after hitstop");
        movement.Tick(default(MechLocomotionInput),null,.01f);
        check(movement.IsQuickBoosting,"A dodge pressed during hitstop is buffered until movement resumes");
        movement.enabled=false; movement.enabled=true;
        int oldHits=feedback.HitImpulseCount;
        blast.ResolveBox(enemy.transform.position,Vector3.one,Quaternion.identity,0,player.transform,~0,player.transform.position+Vector3.up,source:sword);
        victim.Team=CombatTeam.Player;
        blast.ResolveBox(enemy.transform.position,Vector3.one,Quaternion.identity,5,player.transform,~0,player.transform.position+Vector3.up,source:sword);
        blast.ResolveBox(Vector3.one*500,Vector3.one,Quaternion.identity,5,player.transform,~0,player.transform.position+Vector3.up,source:sword);
        check(feedback.HitImpulseCount==oldHits && !stop.IsStopped,"Rejected damage, friendly hits and misses produce no hit shake or hitstop");
        victim.Team=CombatTeam.Enemy;
        int incoming=feedback.DamageImpulseCount; health.ApplyDamage(1);
        check(feedback.DamageImpulseCount==incoming+1 && stop.IsStopped,"Accepted incoming damage produces stronger camera shake and local hitstop");
        yield return Frames(1);
        check(CombatHitStop.BlocksNewActions(player.transform),"Attack admission pauses on subsequent hitstop frames without cancelling a simultaneous first-frame volley");
        yield return Delay(.2f);
        hitShakes=feedback.HitImpulseCount; count=stop.StopsStarted;
        result=blast.Resolve(enemy.transform.position,0,5,0,player.transform,~0,enemy.GetComponent<Collider>());
        check(result.Hits==1 && feedback.HitImpulseCount==hitShakes+1 && stop.StopsStarted==count+1 && stop.Remaining<=.04f,
            "Ordinary projectile damage uses hit-confirm shake and its shorter local pause");
        yield return Delay(.2f);
        CombatHitStop.Request(enemy.transform,.08f,feedback); enemy.SetActive(false);
        check(!CombatHitStop.IsFrozen(enemy.transform) && Mathf.Abs(targetAnimator.speed-.7f)<.001f,"Actor disable/death cannot leave an animation frozen"); enemy.SetActive(true);
        yield return Delay(.2f);
        CombatHitStop.Request(player.transform,.08f,feedback); CombatHitStop.Request(enemy.transform,.08f,feedback);
        Set(feedback,"presentationEnabled",false); typeof(CombatFeedback).GetMethod("Tick",Private).Invoke(feedback,new object[]{0f});
        check(!CombatHitStop.IsFrozen(player.transform) && !CombatHitStop.IsFrozen(enemy.transform),"Disabling presentation immediately clears all hitstops requested by that feedback owner");
        health.ApplyDamage(1); check(feedback.DamageImpulseCount==incoming+1 && !stop.IsStopped,"Muted feedback preserves damage without shake or hitstop");
        Object.DestroyImmediate(enemy); Object.DestroyImmediate(unrelated); Object.DestroyImmediate(staging); Object.DestroyImmediate(floor); yield return Frames(3);
    }
}
