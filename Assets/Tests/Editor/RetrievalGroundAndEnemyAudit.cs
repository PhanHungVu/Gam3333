using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object=UnityEngine.Object;

// Reproduces fresh loot settling and checks the real enemy hitboxes, rather than tall synthetic targets.
public static class RetrievalGroundAndEnemyAudit
{
    const string Pending="RetrievalGroundAndEnemyAudit.Pending";
    const string Verify="RetrievalGroundAndEnemyAudit.Verify";
    const string EnemiesOnly="RetrievalGroundAndEnemyAudit.EnemiesOnly";
    static readonly Stack<IEnumerator> steps=new Stack<IEnumerator>();
    static readonly List<Object> owned=new List<Object>();
    static int frame=-1,checks; static bool running; static double deadline;
    static GameObject player; static RemotePartRetrieval remote; static Camera camera;
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.update+=Tick;
    public static void RunBatch()=>Prepare(false);
    public static void RunFixedBatch()=>Prepare(true);
    public static void RunEnemiesOnlyBatch()=>Prepare(true,true);
    static void Prepare(bool verify,bool enemiesOnly=false)
    {
        if(!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Verify,verify);SessionState.SetBool(EnemiesOnly,enemiesOnly); SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling||frame==Time.frameCount) return;
        frame=Time.frameCount;
        try
        {
            if(!running) { running=true; deadline=EditorApplication.timeSinceStartup+180; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("Ground audit timed out");
            while(steps.Count>0) { var step=steps.Peek(); if(!step.MoveNext()){steps.Pop();continue;} if(step.Current is IEnumerator child){steps.Push(child);continue;} return; }
            Finish(null);
        }
        catch(Exception error){Finish(error);}
    }
    static IEnumerator Frames(int n=3){int end=Time.frameCount+n;while(Time.frameCount<end)yield return null;}
    static IEnumerator Seconds(float seconds){double end=Time.timeAsDouble+seconds;while(Time.timeAsDouble<end)yield return null;}
    static Bounds BoundsOf(Part part,bool render)
    {
        var bounds=new Bounds(part.transform.position,Vector3.zero); bool first=true;
        if(render) foreach(var item in part.GetComponentsInChildren<Renderer>())
        {if(!(item is MeshRenderer||item is SkinnedMeshRenderer)||item.GetComponentInParent<PickupHighlight>()!=null||!item.enabled)continue;if(first){bounds=item.bounds;first=false;}else bounds.Encapsulate(item.bounds);}
        else foreach(var item in part.GetComponentsInChildren<Collider>())
        {if(!item.enabled||item.isTrigger||item.GetComponentInParent<Part>()!=part)continue;if(first){bounds=item.bounds;first=false;}else bounds.Encapsulate(item.bounds);}
        return bounds;
    }
    static void Report(Part part,string name)
    {
        var mesh=BoundsOf(part,true);var collision=BoundsOf(part,false);
        camera.transform.SetPositionAndRotation(player.transform.position+new Vector3(0,4,-8),Quaternion.LookRotation(mesh.center-(player.transform.position+new Vector3(0,4,-8))));
        Physics.SyncTransforms();remote.RefreshTarget();
        Debug.Log("GROUND AUDIT "+name+": root="+part.transform.position.ToString("F3")+"; mesh="+mesh.center.ToString("F3")+" +/- "+mesh.extents.ToString("F3")
            +"; collision="+collision.center.ToString("F3")+" +/- "+collision.extents.ToString("F3")+"; candidate="+(remote.Target==part)+"; canPull="+remote.CanStart(part));
        if(SessionState.GetBool(Verify,false))Check(remote.Target==part&&remote.CanStart(part),name+" is targetable/retrievable after natural settling");
    }
    static void Check(bool valid,string message){if(!valid)throw new Exception(message);checks++;Debug.Log("GROUNDED RETRIEVAL PASS: "+message);}
    static GameObject Enemy(string file,Vector3 position)
    {
        var parent=new GameObject("Disabled behavior fixture");owned.Add(parent);parent.SetActive(false);
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/"+file+".prefab"),parent.transform);root.transform.position=position;
        foreach(var script in root.GetComponentsInChildren<MonoBehaviour>(true))if(!(script is CombatHealth||script is EquipManager||script is Part||script is EnemyAgent))script.enabled=false;
        var agentFields=new SerializedObject(root.GetComponent<EnemyAgent>());agentFields.FindProperty("externalDecisions").boolValue=true;agentFields.ApplyModifiedPropertiesWithoutUndo();
        foreach(var animator in root.GetComponentsInChildren<Animator>(true))animator.enabled=false;
        foreach(var agent in root.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true))agent.enabled=false;
        parent.SetActive(true);root.GetComponent<CombatHealth>().SetMaximum(500,true);return root;
    }
    static IEnumerator Run()
    {
        Time.timeScale=1;Time.maximumDeltaTime=.05f;Application.runInBackground=true;
        var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);owned.Add(floor);floor.transform.position=Vector3.down*.25f;floor.transform.localScale=new Vector3(300,.5f,200);
        player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath),Vector3.up*.1f,Quaternion.identity);owned.Add(player);
        yield return Frames(6);player.GetComponent<MechMovementController>().enabled=false;player.GetComponent<MechPlayerInput>().enabled=false;
        remote=player.GetComponent<RemotePartRetrieval>();camera=player.GetComponent<MechPlayerRig>().GameplayCamera;
        camera.GetComponent<CameraControlThirdPerson>().enabled=false;camera.GetComponent<Unity.Cinemachine.CinemachineBrain>().enabled=false;
        if(!SessionState.GetBool(EnemiesOnly,false))
        {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_crossbow.prefab");
        foreach(int yaw in new[]{0,90,180,270})
        {
            var root=Object.Instantiate(source,new Vector3(0,1,10),Quaternion.Euler(0,yaw,0));owned.Add(root);var part=root.GetComponent<Part>();
            yield return Seconds(2.5f);Report(part,"fresh crossbow yaw "+yaw);
            if(player.GetComponent<EquipManager>().TryEquip(part,PartCategory.ArmR))
            {player.GetComponent<EquipManager>().Unequip(PartCategory.ArmR);part.transform.position=new Vector3(0,1,10);yield return Seconds(2.5f);Report(part,"cycled crossbow yaw "+yaw);}
            root.SetActive(false);
        }
        var flipped=Object.Instantiate(source,new Vector3(0,1,10),Quaternion.Euler(180,30,0));owned.Add(flipped);
        yield return Seconds(2.5f);Report(flipped.GetComponent<Part>(),"fresh upside-down crossbow");
        bool equipped;
        if(SessionState.GetBool(Verify,false))
        {
            Check(flipped.transform.position.y<0&&BoundsOf(flipped.GetComponent<Part>(),false).min.y>-.03f,"Reproduction keeps pivot under the floor while the physical pickup is above it");
            var input=player.GetComponent<MechPlayerInput>();input.enabled=true;input.AssumeCursorCaptured=true;
            Check(input.RequestPickup(),"F entry point retrieves the fresh overturned crossbow without first using 1/2");
            while(remote.IsBusy)yield return null;
            Check(flipped.GetComponent<Part>().isEquipped,"Overturned crossbow finishes lift/flight through bounds-based collision checks");
            input.enabled=false;equipped=flipped.GetComponent<Part>().isEquipped;
        }
        else equipped=player.GetComponent<EquipManager>().TryEquip(flipped.GetComponent<Part>(),PartCategory.ArmR);
        if(equipped)
        {player.GetComponent<EquipManager>().Unequip(PartCategory.ArmR);flipped.transform.position=new Vector3(0,1,10);yield return Seconds(2.5f);Report(flipped.GetComponent<Part>(),"cycled upside-down crossbow");}
        flipped.SetActive(false);
        }
        var files=new[]{"Small Enemies/Enemy_sword","Small Enemies/Enemy_crossbow","Small Enemies/Enemy_mace","Small Enemies/Enemy_Fodder"};
        var enemies=new List<GameObject>();
        for(int i=0;i<files.Length;i++)
        {
            var enemy=Enemy(files[i],Quaternion.AngleAxis(i*90+22.5f,Vector3.up)*Vector3.forward*4);enemies.Add(enemy);yield return Frames(3);Physics.SyncTransforms();
            foreach(var collider in enemy.GetComponentsInChildren<Collider>())
                if(collider.enabled&&!collider.isTrigger&&collider.GetComponentInParent<CombatHealth>()==enemy.GetComponent<CombatHealth>())
                    Debug.Log("ENEMY BOUNDS AUDIT "+files[i]+" "+collider.name+": "+collider.bounds.center.ToString("F3")+" +/- "+collider.bounds.extents.ToString("F3")
                        +"; layer="+LayerMask.LayerToName(collider.gameObject.layer)+"; partEquipped="+(collider.GetComponentInParent<Part>()!=null?collider.GetComponentInParent<Part>().isEquipped.ToString():"no part")
                        +"; body="+(collider.attachedRigidbody!=null?collider.attachedRigidbody.isKinematic.ToString():"none"));
        }
        yield return Frames(3);
        var axe=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_throwingaxe.prefab"),new Vector3(0,1,12),Quaternion.identity);owned.Add(axe);
        axe.GetComponent<Rigidbody>().isKinematic=true;
        int axes=0;player.GetComponent<RetrievalCombatEffects>().BarrageProjectileCreated+=(shot,direction)=>
        {
            axes++;
            shot.ImpactResolved+=hit=>Debug.Log("AXE IMPACT AUDIT "+hit.Position.ToString("F3")+"; dir="+hit.Direction.ToString("F3")+"; hits="+hit.Hits+"; team="+shot.OwnerTeam);
        };
        Action<MechProjectile,Vector3,Transform> released=(shot,muzzle,owner)=>
        {
            if(owner==player.transform)Debug.Log("AXE RELEASE AUDIT "+muzzle.ToString("F3")+"; live="+shot.IsLive+"; dir="+shot.Direction.ToString("F3"));
        };
        CombatVisualEvents.RetrievalShotReleased+=released;
        Debug.Log("AXE ORIGIN AUDIT player="+player.transform.position.ToString("F3")+"; forward="+player.transform.forward);
        foreach(var hit in Physics.SphereCastAll(player.transform.position+Vector3.up*1.2f,.4f,Quaternion.AngleAxis(15,Vector3.up)*Vector3.forward,6,Part.MovementQueryMask,QueryTriggerInteraction.Ignore))
            Debug.Log("AXE CAST AUDIT "+hit.collider.name+"; layer="+LayerMask.LayerToName(hit.collider.gameObject.layer)+"; distance="+hit.distance+"; health="+hit.collider.GetComponentInParent<CombatHealth>());
        Debug.Log("AXE REAL ENEMY AUDIT start="+remote.TryStart(axe.GetComponent<Part>()));
        while(remote.IsBusy)yield return null;yield return Seconds(.6f);
        Debug.Log("AXE REAL ENEMY AUDIT emitted="+axes+"; last="+player.GetComponent<RetrievalCombatEffects>().LastBarrageAxes);
        CombatVisualEvents.RetrievalShotReleased-=released;
        foreach(var enemy in enemies)
        {
            Debug.Log("AXE REAL ENEMY AUDIT "+enemy.name+": HP="+enemy.GetComponent<CombatHealth>().CurrentHealth);
            Debug.Log("AXE HEALTH AUDIT "+enemy.name+"; enabled="+enemy.GetComponent<CombatHealth>().isActiveAndEnabled+"; team="+enemy.GetComponent<CombatHealth>().Team+"; position="+enemy.transform.position);
        }
        if(SessionState.GetBool(Verify,false))foreach(var enemy in enemies)Check(enemy.GetComponent<CombatHealth>().CurrentHealth<500,enemy.name+" at normal ground height and between the old rays takes barrage damage");
        foreach(var item in owned)if(item!=null)Object.Destroy(item);yield return Frames(4);
    }
    static void Finish(Exception error)
    {Time.timeScale=1;SessionState.SetBool(Pending,false);PreMissionReliabilityChecks.EndLogAudit();if(error!=null)Debug.LogException(error);Debug.Log("GROUND ENEMY AUDIT "+(error==null?"COMPLETE: ":"FAILED: ")+checks);EditorApplication.Exit(error==null?0:1);}
}
