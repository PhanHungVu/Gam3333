using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

public static class RetrievalAnticipationAndCannonChecks
{
    const string Pending="RetrievalAnticipationAndCannonChecks.Pending";
    static readonly Stack<IEnumerator> steps=new Stack<IEnumerator>();
    static readonly List<Object> owned=new List<Object>();
    static int frame=-1,checks; static bool running; static double deadline;
    static GameObject player; static RemotePartRetrieval remote; static RetrievalCombatEffects effects; static RetrievalBonusCooldowns gate;
    static RetrievalFeedback feedback; static Camera camera;
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.update+=Tick;
    static void Check(bool valid,string message) { if(!valid) throw new Exception(message); checks++; Debug.Log("RETRIEVAL CANNON PASS: "+message); }
    public static void RunBatch()
    {
        if(!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only");
        foreach(string path in new[]{"Base/FullPlayer_mech_golem","Base/FullPlayer_mech_heavy","Specialist/FullPlayer_mech_specialist"})
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/"+path+".prefab"); var r=prefab.GetComponent<RemotePartRetrieval>();
            Check(r.weaponLiftHeight==.8f && r.weaponLiftDuration==.12f && r.weaponHoverDuration==.18f && r.weaponFlightDuration==.1f
                && r.pickupRange==20 && prefab.GetComponents<RetrievalFeedback>().Length==1,path+" authors lift/hover/fast return plus one arrival feedback listener");
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling||frame==Time.frameCount) return;
        frame=Time.frameCount;
        try
        {
            if(!running) { running=true; deadline=EditorApplication.timeSinceStartup+240; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("Anticipation/cannon checks timed out");
            while(steps.Count>0) { var step=steps.Peek(); if(!step.MoveNext()) { steps.Pop(); continue; } if(step.Current is IEnumerator child) { steps.Push(child); continue; } return; }
            Finish(null);
        }
        catch(Exception error) { Finish(error); }
    }
    static IEnumerator Frames(int n=3) { int end=Time.frameCount+n; while(Time.frameCount<end) yield return null; }
    static IEnumerator Complete()
    {
        double until=EditorApplication.timeSinceStartup+8;
        while(remote.IsBusy||CombatHitStop.IsFrozen(player.transform)) { if(EditorApplication.timeSinceStartup>until) throw new Exception("Flight did not finish"); yield return null; }
        yield return Frames();
    }
    static IEnumerator Hover()
    {
        while(remote.IsBusy && remote.Phase!=RetrievalPhase.Hovering) yield return null;
        Check(remote.Phase==RetrievalPhase.Hovering,"A visible suspended hover occurs before launch");
    }
    static IEnumerator Departure() { while(remote.IsBusy&&!remote.HasDeparted) yield return null; }
    static IEnumerator Player(float x)
    {
        player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath),new Vector3(x,.1f,0),Quaternion.identity); owned.Add(player);
        yield return Frames(6); player.GetComponent<MechMovementController>().enabled=false; player.GetComponent<MechPlayerInput>().enabled=false;
        player.GetComponent<CombatHealth>().Team=CombatTeam.Player;
        remote=player.GetComponent<RemotePartRetrieval>(); effects=player.GetComponent<RetrievalCombatEffects>(); gate=player.GetComponent<RetrievalBonusCooldowns>(); feedback=player.GetComponent<RetrievalFeedback>();
        // This suite verifies normal arrival/cancellation cooldown rules; a separate suite tests the temporary bypass.
        gate.bypassCooldownForTesting=false;
        remote.enableRemoteTargeting=false;
        camera=player.GetComponent<MechPlayerRig>().GameplayCamera;
        camera.GetComponent<CameraControlThirdPerson>().enabled=false; camera.GetComponent<Unity.Cinemachine.CinemachineBrain>().enabled=false;
        camera.transform.SetPositionAndRotation(new Vector3(x+4,5,-8),Quaternion.LookRotation(new Vector3(x,1,5)-new Vector3(x+4,5,-8)));
    }
    static Part Drop(string file="Specialist/shoulder_cannon_specialist",float z=12)
    {
        string path="Assets/Prefabs/Mech/"+(file.StartsWith("Specialist/")?file:"Base/Parts/"+file)+".prefab";
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path)); owned.Add(root);
        var part=root.GetComponent<Part>(); part.isStartingPart=false; root.transform.position=player.transform.position+new Vector3(0,.6f,z);
        root.GetComponent<Rigidbody>().isKinematic=true;
        typeof(Part).GetMethod("InitializeAmmo",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(part,new object[]{Mathf.RoundToInt(part.Stats.GetStat(StatType.Ammo)),part.Stats.MagazineSize});
        Physics.SyncTransforms(); return part;
    }
    static CombatHealth Dummy(Vector3 local,CombatTeam team=CombatTeam.Enemy,bool compound=false)
    {
        var root=GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(root); root.name="Blast target "+team;
        root.transform.position=player.transform.position+local+Vector3.up*.8f; root.transform.localScale=new Vector3(.7f,1.6f,.7f);
        if(compound) for(int i=0;i<4;i++) { var child=new GameObject("Overlapping collider"); child.transform.SetParent(root.transform,false); child.AddComponent<BoxCollider>(); }
        var health=root.AddComponent<CombatHealth>(); health.Team=team; health.SetMaximum(500,true); return health;
    }
    static IEnumerator Run()
    {
        Time.timeScale=1; Time.maximumDeltaTime=.05f; Application.runInBackground=true;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor); floor.transform.position=Vector3.down*.25f; floor.transform.localScale=new Vector3(250,.5f,100);
        var light=new GameObject("Sun").AddComponent<Light>(); owned.Add(light.gameObject); light.type=LightType.Directional; light.transform.rotation=Quaternion.Euler(55,-30,0);
        yield return Player(0);
        foreach(string weapon in new[]{"arm_sword","arm_mace","arm_throwingaxe","arm_crossbow","shoulder_crossbow","Specialist/shoulder_cannon_specialist"})
        {
            var part=Drop(weapon); Vector3 origin=part.transform.position,scale=part.transform.lossyScale; int count=feedback.ArrivalImpulseCount;
            double launched=-1,arrived=-1; Action<Part,PartCategory> launch=(p,s)=>launched=Time.timeAsDouble; Action<Part,PartCategory> arrive=(p,s)=>arrived=Time.timeAsDouble;
            remote.Departed+=launch; remote.Arrived+=arrive;
            Check(remote.TryStart(part),weapon+": accepts ordinary retrieval"); yield return Hover();
            Check(Vector3.Distance(part.transform.position,origin+Vector3.up*.8f)<.01f && Vector3.Distance(part.transform.lossyScale,scale)<.001f,
                weapon+": rises in place without stretching or shrinking during anticipation");
            Check(!remote.HasDeparted && feedback.ArrivalImpulseCount==count,weapon+": hover neither launches early nor shakes the screen");
            if(weapon.Contains("cannon")) Capture("weapon-hover",part.transform.position,new Vector3(3,2,-4));
            yield return Complete();
            Check(part.isEquipped && feedback.ArrivalImpulseCount==count+1,weapon+": equips and emits one existing-camera arrival impulse");
            Check(launched>=0 && arrived>=launched && arrived-launched<=.17,weapon+": completes its fast 0.10s flight after the hold");
            remote.Departed-=launch; remote.Arrived-=arrive;
        }
        // The new anticipation applies to weapons; body selection remains the existing flow.
        var headRoot=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/head_base.prefab")); owned.Add(headRoot);
        var head=headRoot.GetComponent<Part>(); head.isStartingPart=false; head.transform.position=player.transform.position+new Vector3(0,1,8); head.GetComponent<Rigidbody>().isKinematic=true;
        int prior=feedback.ArrivalImpulseCount; Check(remote.TryStart(head),"Head remains retrievable"); yield return Departure();
        Check(remote.Phase==RetrievalPhase.Flying,"Non-weapon part skips weapon lift and suspense"); yield return Complete();
        Check(feedback.ArrivalImpulseCount==prior,"Body-part arrival does not add weapon shake");
        var master=new SerializedObject(player.GetComponent<CombatFeedback>()); master.FindProperty("presentationEnabled").boolValue=false; master.ApplyModifiedPropertiesWithoutUndo();
        var muted=Drop("arm_throwingaxe"); Check(remote.TryStart(muted),"Retrieval works with presentation disabled"); yield return Complete();
        Check(muted.isEquipped && feedback.ArrivalImpulseCount==prior,"Arrival shake honours the existing presentation master setting");
        master.FindProperty("presentationEnabled").boolValue=true; master.ApplyModifiedPropertiesWithoutUndo();
        yield return CannonCombat(); yield return Cancellation();
        foreach(var item in owned) if(item!=null) Object.Destroy(item); yield return Frames(4);
    }
    static IEnumerator CannonCombat()
    {
        yield return Player(30);
        var left=Dummy(Vector3.left*2,compound:true); var right=Dummy(Vector3.right*2,compound:true); var front=Dummy(Vector3.forward*3,compound:true);
        var edge=Dummy(Vector3.right*4.9f); var outside=Dummy(Vector3.right*6); var friend=Dummy(Vector3.back,CombatTeam.Player);
        var shielded=Dummy(Vector3.back*4); var wall=GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(wall);
        wall.transform.position=player.transform.position+new Vector3(0,1.5f,-2); wall.transform.localScale=new Vector3(2,3,.2f);
        var cannon=Drop(); float ownerHP=player.GetComponent<CombatHealth>().CurrentHealth; int ammo=cannon.RemainingAmmo,events=0;
        bool afterEquip=false; Vector3 center=Vector3.zero;
        Action<Part,Transform,Vector3,float,DamageResult> onBlast=(p,o,pos,r,result)=> { if(o==player.transform) { events++; afterEquip=p.isEquipped&&p.EquippedBy==player.GetComponent<EquipManager>(); center=pos; } };
        CombatVisualEvents.RetrievalBlast+=onBlast;
        Check(remote.TryStart(cannon),"Cannon starts a reserved arrival bonus"); yield return Hover();
        Check(gate.IsReserved(RetrievalWeaponFamily.Cannon)&&gate.Remaining(RetrievalWeaponFamily.Cannon)==0&&front.CurrentHealth==500,
            "Cannon holds its reservation without cooldown or damage during lift/hover");
        yield return Departure();
        Check(remote.IsBusy&&gate.IsReserved(RetrievalWeaponFamily.Cannon)&&front.CurrentHealth==500,"Cannon does not explode during fast travel");
        yield return Complete();
        Check(left.CurrentHealth==400&&right.CurrentHealth==400&&front.CurrentHealth==400&&edge.CurrentHealth==400&&effects.LastBlastHits==4,
            "Arrival explosion damages all targets intersecting its 5m radius once, including compound hitboxes");
        Check(outside.CurrentHealth==500&&friend.CurrentHealth==500&&shielded.CurrentHealth==500&&player.GetComponent<CombatHealth>().CurrentHealth==ownerHP,
            "Explosion preserves owner/friendlies, outside targets and wall-shielded enemies");
        Check(events==1&&afterEquip&&Vector3.Distance(center,player.transform.position+Vector3.up*.5f)<.001f,
            "Blast is reported once after equip at the player's current position");
        Check(cannon.RemainingAmmo==ammo&&cannon.ReadyAt==0&&gate.Remaining(RetrievalWeaponFamily.Cannon)>29.5f,
            "Cannon starts its own 30s timer on arrival without spending ammo or normal weapon cooldown");
        Check(player.GetComponent<CombatFeedback>().ActiveEffects>0,"Cannon arrival reuses existing pooled explosion visuals"); Capture("cannon-arrival-blast",player.transform.position+Vector3.up*.5f,new Vector3(7,12,-7));
        int shake=feedback.ArrivalImpulseCount; var copy=Drop(); Check(remote.TryStart(copy),"Another cannon is retrievable during its family cooldown"); yield return Complete();
        Check(copy.isEquipped&&effects.BonusBlastCount==1&&events==1&&front.CurrentHealth==400&&feedback.ArrivalImpulseCount==shake+1,
            "Cooldown suppresses only cannon bonus; ordinary equip and arrival shake remain");
        Check(gate.IsReady(RetrievalWeaponFamily.Sword)&&gate.IsReady(RetrievalWeaponFamily.Mace),"Cannon's timer does not block either melee family");
        CombatVisualEvents.RetrievalBlast-=onBlast;
    }
    static IEnumerator Cancellation()
    {
        yield return Player(60);
        var sword=Drop("arm_sword"); Check(remote.TryStart(sword),"Sword anticipation reserves its family");
        Check(gate.IsReserved(RetrievalWeaponFamily.Sword)&&!effects.HasActivePath,"Sword has no damaging path during anticipation"); remote.Cancel();
        Check(gate.IsReady(RetrievalWeaponFamily.Sword)&&!sword.IsRetrieving&&feedback.ArrivalImpulseCount==0,"Cancelling before melee departure releases bonus and causes no arrival shake");
        var cannon=Drop(); Check(remote.TryStart(cannon),"Cannon hover cancellation fixture starts"); yield return Hover();
        Time.timeScale=0; Vector3 hoverPosition=cannon.transform.position; yield return Frames(6);
        Check(cannon.transform.position==hoverPosition&&gate.IsReserved(RetrievalWeaponFamily.Cannon)&&gate.Remaining(RetrievalWeaponFamily.Cannon)==0,
            "Pausing suspends the hovering weapon and its pending bonus"); Time.timeScale=1; remote.Cancel();
        Check(gate.IsReady(RetrievalWeaponFamily.Cannon)&&!cannon.IsRetrieving,"Cancelled cannon hover consumes no cooldown");
        var flying=Drop(); Check(remote.TryStart(flying),"Cannon flight cancellation fixture starts"); yield return Departure(); remote.Cancel();
        Check(gate.IsReady(RetrievalWeaponFamily.Cannon)&&effects.BonusBlastCount==0&&feedback.ArrivalImpulseCount==0,
            "Cancelled cannon flight releases the still-pending arrival bonus");
        var disabled=Drop(); Check(remote.TryStart(disabled),"Disabled-effects fixture starts"); effects.enabled=false;
        Check(gate.IsReady(RetrievalWeaponFamily.Cannon),"Disabling effects releases an untriggered arrival reservation"); yield return Complete(); effects.enabled=true; yield return Frames();
        Check(disabled.isEquipped&&effects.BonusBlastCount==0&&gate.IsReady(RetrievalWeaponFamily.Cannon),"Re-enabling after arrival cannot replay an abandoned blast");
        var roofed=Drop(); var roof=GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(roof);
        roof.transform.position=roofed.PickupPoint+Vector3.up*(remote.obstructionRadius+.02f+.4f); roof.transform.localScale=new Vector3(2,.1f,2);
        Physics.SyncTransforms(); Check(remote.TryStart(roofed),"Lift obstruction fixture begins below a ceiling"); yield return Complete(); roof.SetActive(false);
        Check(!roofed.isEquipped&&!roofed.IsRetrieving&&gate.IsReady(RetrievalWeaponFamily.Cannon),"Terrain blocks upward lift and safely refunds the untriggered cannon bonus");
        var death=Drop(); Check(remote.TryStart(death),"Owner death fixture starts"); player.GetComponent<CombatHealth>().ApplyDamage(100000);
        Check(!death.IsRetrieving&&gate.IsReady(RetrievalWeaponFamily.Cannon)&&effects.BonusBlastCount==0,"Owner death cancels pending cannon bonus without an explosion");
    }
    static void Capture(string name,Vector3 focus,Vector3 offset)
    {
        var old=camera.targetTexture; var active=RenderTexture.active; var target=new RenderTexture(1280,720,24); var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);
        Vector3 oldPosition=camera.transform.position; Quaternion oldRotation=camera.transform.rotation;
        var views=Object.FindObjectsByType<PartSwapWorldView>(); var alphas=views.Select(v=>v.Group.alpha).ToArray();
        try
        {
            // Inspect the existing models/VFX clearly; other fixture players' comparison panels are unrelated to this capture.
            foreach(var view in views) view.SetAlpha(0);
            camera.transform.SetPositionAndRotation(focus+offset,Quaternion.LookRotation(-offset));
            camera.targetTexture=target; Canvas.ForceUpdateCanvases(); RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest{destination=target}); RenderTexture.active=target;
            texture.ReadPixels(new Rect(0,0,1280,720),0,0); texture.Apply(); string folder=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"RetrievalCannonCaptures"); Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder,name+".png"),texture.EncodeToPNG());
        }
        finally
        {
            camera.transform.SetPositionAndRotation(oldPosition,oldRotation); for(int i=0;i<views.Length;i++) views[i].SetAlpha(alphas[i]);
            camera.targetTexture=old; RenderTexture.active=active; Object.Destroy(texture); Object.Destroy(target);
        }
    }
    static void Finish(Exception error)
    {
        Time.timeScale=1; PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false); if(error!=null) Debug.LogException(error);
        Debug.Log("RETRIEVAL CANNON CHECKS "+(error==null?"COMPLETE: ":"FAILED: ")+checks); EditorApplication.Exit(error==null?0:1);
    }
}
