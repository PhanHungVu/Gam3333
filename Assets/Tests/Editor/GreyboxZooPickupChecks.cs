using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public static class GreyboxZooPickupChecks
{
    const string Pending = "GreyboxZooPickupChecks.Pending";
    static readonly Stack<IEnumerator> work = new Stack<IEnumerator>();
    static bool running, keysPending;
    static Keyboard keyboard;
    static KeyboardState keyState;
    static int frame=-1, checks;
    static double deadline;
    static void Check(bool valid,string message){if(!valid)throw new Exception(message);checks++;Debug.Log("ZOO PICKUP PASS: "+message);}
    [InitializeOnLoadMethod] static void Register()
    {
        EditorApplication.update+=Tick;
        EditorApplication.playModeStateChanged+=state=>
        {
            if(state==PlayModeStateChange.EnteredPlayMode&&SessionState.GetBool(Pending,false)) keyboard=InputSystem.AddDevice<Keyboard>();
        };
    }
    public static void RunBatch()
    {
        if(!Application.isBatchMode || !Application.dataPath.Contains(".quest-review"))throw new Exception("Disposable project only.");
        EditorSceneManager.OpenScene(GreyboxZooWeaponSetup.Zoo);
        // Only the disposable fixture changes: legacy Zoo pad Update methods
        // assume a native keyboard during startup, before virtual input is ready.
        // Their station behavior is outside this weapon-pickup integration test.
        foreach(var pad in Object.FindObjectsByType<ZooSoundplayer>(FindObjectsInactive.Include))pad.enabled=false;
        foreach(var pad in Object.FindObjectsByType<ZooBacktoMain>(FindObjectsInactive.Include))pad.enabled=false;
        foreach(var pad in Object.FindObjectsByType<ZooToggle>(FindObjectsInactive.Include))
        {if(pad.toggleObject!=null)pad.toggleObject.SetActive(false);pad.enabled=false;}
        SessionState.SetBool(Pending,true);EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling||frame==Time.frameCount)return;
        frame=Time.frameCount;
        try
        {
            if(!running){running=true;deadline=EditorApplication.timeSinceStartup+120;PreMissionReliabilityChecks.BeginLogAudit();work.Push(Run());}
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if(EditorApplication.timeSinceStartup>deadline)throw new Exception("Zoo pickup checks timed out.");
            while(work.Count>0){var task=work.Peek();if(!task.MoveNext()){work.Pop();continue;}if(task.Current is IEnumerator child){work.Push(child);continue;}return;}
            Finish(null);
        }
        catch(Exception error){Finish(error);}
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending,false);InputSystem.onBeforeUpdate-=FeedInput;Time.captureFramerate=0;PreMissionReliabilityChecks.EndLogAudit();
        if(error!=null)Debug.LogException(error);Debug.Log("ZOO PICKUP CHECKS "+(error==null?"COMPLETE: ":"FAILED: ")+checks);EditorApplication.Exit(error==null?0:1);
    }
    static IEnumerator Frames(int count){int end=Time.frameCount+count;while(Time.frameCount<end)yield return null;}
    static void FeedInput(){if(InputState.currentUpdateType==InputUpdateType.Dynamic&&keysPending){InputSystem.QueueStateEvent(keyboard,keyState);keysPending=false;}}
    static IEnumerator Press(Key key){keyState=new KeyboardState(key);keysPending=true;yield return Frames(2);keyState=new KeyboardState();keysPending=true;yield return Frames(2);}
    static IEnumerator Run()
    {
        Application.runInBackground=true;Time.captureFramerate=60;AudioListener.volume=0;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        // Zoo's existing sound/toggle pads read Keyboard.current every frame.
        // Supply the virtual keyboard before their first Update in batch mode.
        if(keyboard==null)keyboard=InputSystem.AddDevice<Keyboard>();InputSystem.onBeforeUpdate+=FeedInput;
        var display=GameObject.Find("parts").transform.Find(GreyboxZooWeaponSetup.DisplayName);
        Check(display!=null,"Zoo has a ground-pickup display beside the original parts area");
        var pickups=display.GetComponentsInChildren<Part>().OrderBy(p=>(int)p.GetComponent<GreyboxArmWeapon>().weapon).ToArray();
        Check(pickups.Length==9 && pickups.Select(p=>p.data).Distinct().Count()==9,"Nine physically separate Parts each retain their own weapon definition");
        Check(Object.FindObjectsByType<GreyboxArmWeaponBench>().Length==0,"Zoo pickups require no combined bench or selection-spawner object");
        var originals=GameObject.Find("parts").GetComponentsInChildren<Part>().Where(p=>p.GetComponent<GreyboxArmWeapon>()==null).ToArray();
        Check(originals.Length>=10,"Existing Zoo weapon and body-part pickups remain available");
        Check(GameObject.Find("parts").GetComponentsInChildren<TMPro.TMP_Text>(true).Any(t=>t.text.Contains("1 / 2")),"Zoo's pickup instruction shows the actual 1/2 controls");
        var input=Object.FindAnyObjectByType<MechPlayerInput>();input.enabled=false;var equipment=input.GetComponent<EquipManager>();
        input.GetComponent<CombatFeedback>().enabled=false;
        yield return Frames(40);
        foreach(var part in pickups)
        {
            Check(!part.isEquipped&&!part.HasRolledStats&&part.GetComponent<GreyboxArmWeapon>().keepAuthoredStats,"Loose "+part.data.partName+" keeps its exact prototype stats");
            var collider=part.GetComponentInChildren<Collider>();var body=part.GetComponent<Rigidbody>();
            Check(collider!=null&&collider.enabled&&!body.isKinematic&&collider.gameObject.layer==LayerMask.NameToLayer(Part.LoosePhysicsLayerName),part.data.partName+" has normal independent pickup physics");
            Check(collider.bounds.min.y>-.1f&&collider.bounds.min.y<.2f,part.data.partName+" settles on the Zoo floor");
        }
        Capture(display);
        input.AssumeCursorCaptured=true;input.enabled=true;
        var detector=input.GetComponentInChildren<PickupDetector>();var character=input.GetComponent<CharacterController>();
        for(int i=0;i<pickups.Length;i++)
        {
            var pickup=pickups[i];var bounds=pickup.GetComponentInChildren<Collider>().bounds;
            character.enabled=false;input.transform.SetPositionAndRotation(new Vector3(bounds.center.x,2.2f,bounds.center.z-2),Quaternion.identity);character.enabled=true;Physics.SyncTransforms();
            yield return Frames(8);
            for(int attempt=0;equipment.GetSelectedPart()!=pickup&&attempt<14;attempt++){detector.CycleSelection();yield return Frames(2);}
            Check(detector.NearbyParts.Contains(pickup)&&equipment.GetSelectedPart()==pickup,"Walking near "+pickup.data.partName+" selects that physical ground item");
            var slot=i%2==0?PartCategory.ArmL:PartCategory.ArmR;
            yield return Press(i%2==0?Key.Digit1:Key.Digit2);
            Check(pickup.isEquipped&&pickup.EquippedBy==equipment&&equipment.GetEquipped(slot)==pickup,"Normal "+(i%2==0?"1":"2")+" equips only "+pickup.data.partName+" into the expected arm");
            Check(pickup.transform.parent.GetComponent<Socket>()!=null&&!pickup.GetComponentInChildren<Collider>().enabled,"Picked-up "+pickup.data.partName+" attaches to its socket with carried collisions disabled");
        }
        Check(originals.All(p=>p!=null),"Collecting the new weapons preserves all original Zoo pickups");
        // Existing Zoo pad scripts keep reading Keyboard.current until Unity
        // exits; retain this disposable process's device through the final frame.
    }
    static void Capture(Transform display)
    {
        if(SystemInfo.graphicsDeviceType==UnityEngine.Rendering.GraphicsDeviceType.Null)return;
        var camera=new GameObject("Zoo ground weapon preview").AddComponent<Camera>();camera.transform.position=new Vector3(31,24,-69);camera.transform.LookAt(new Vector3(31,0,-45));
        camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.08f,.1f,.12f);
        var target=new RenderTexture(1280,720,24);camera.targetTexture=target;camera.Render();
        var previous=RenderTexture.active;RenderTexture.active=target;var texture=new Texture2D(1280,720,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,1280,720),0,0);texture.Apply();RenderTexture.active=previous;
        File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath,"../zoo-individual-arm-pickups.png")),texture.EncodeToPNG());camera.targetTexture=null;target.Release();Object.DestroyImmediate(target);Object.DestroyImmediate(texture);Object.DestroyImmediate(camera.gameObject);
    }
}
