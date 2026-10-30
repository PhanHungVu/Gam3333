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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object=UnityEngine.Object;

// Real prefab, existing detector, real input actions and equipment callbacks.
// Disposable project only; fixtures never edit live scenes/data/input bindings.
public static class PartSwapWorldUiChecks
{
    const string Pending="PartSwapWorldUiChecks.Pending",SceneMode="PartSwapWorldUiChecks.Scene";
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> stack=new Stack<IEnumerator>();
    static readonly List<GameObject> owned=new List<GameObject>();
    static EquipManager equipment; static PickupDetector detector; static MechPlayerInput input;
    static PartSwapWorldHudPresenter hud; static Mouse mouse; static Keyboard keyboard;
    static bool started; static int checks; static double deadline; static string captures;
    static Camera Camera=>equipment.GetComponent<MechPlayerRig>().GameplayCamera;
    [InitializeOnLoadMethod] static void Register()=>EditorApplication.update+=Step;
    public static void RunBatch()=>Prepare(false);
    public static void RunSceneBatch()=>Prepare(true);
    static void Prepare(bool scene)
    {
        string root=Directory.GetParent(Application.dataPath).FullName;
        if(!Application.isBatchMode || !Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))
            throw new Exception("Run only in a disposable temporary project");
        PartSwapWorldUiSetup.UpgradeAssets(); PartSwapWorldUiSetup.UpgradeAssets();
        GraphicsSettings.defaultRenderPipeline=AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline=null;
        if(scene) EditorSceneManager.OpenScene("Assets/Scenes/JhMapScene.unity");
        else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        SessionState.SetBool(SceneMode,scene); SessionState.SetBool(Pending,true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool valid,string message)
    { if(!valid) throw new Exception(message); checks++; Debug.Log("WORLD SWAP UI PASS: "+message); }
    static void Call(object target,string name,params object[] args)=>target.GetType().GetMethod(name,Private).Invoke(target,args);
    static IEnumerator Frames(int count=2) { int frame=Time.frameCount+count; while(Time.frameCount<frame) yield return null; }
    static IEnumerator Visible(Part part)
    {
        yield return Frames(); double until=EditorApplication.timeSinceStartup+12;
        while(hud.DisplayedPart!=part || !hud.IsVisible || hud.View.Group.alpha<0.999f)
        {
            if(EditorApplication.timeSinceStartup>until) throw new Exception("UI did not settle for "+part.name+"; selected="+equipment.GetSelectedPart()+", same="+(hud.DisplayedPart==part)+", alpha="+hud.View.Group.alpha
                +", canvas="+hud.View.Canvas.enabled+", target="+part.transform.position+", camera="+Camera.transform.position+", forward="+Camera.transform.forward+", enabled="+Camera.isActiveAndEnabled);
            yield return null;
        }
    }
    static void Step()
    {
        if(!SessionState.GetBool(Pending,false)||!EditorApplication.isPlaying||EditorApplication.isCompiling) return;
        try
        {
            if(!started) { started=true; deadline=EditorApplication.timeSinceStartup+240; stack.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if(EditorApplication.timeSinceStartup>deadline) throw new Exception("World swap UI timed out");
            while(stack.Count>0)
            {
                var frame=stack.Peek(); if(!frame.MoveNext()) { (frame as IDisposable)?.Dispose(); stack.Pop(); continue; }
                if(frame.Current is IEnumerator nested) { stack.Push(nested); continue; } return;
            }
            Finish(true,null);
        }
        catch(Exception error) { Finish(false,error); }
    }
    static void Finish(bool passed,Exception error)
    {
        PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending,false); Time.timeScale=1;
        if(mouse!=null) InputSystem.RemoveDevice(mouse); if(keyboard!=null) InputSystem.RemoveDevice(keyboard);
        if(error!=null) Debug.LogException(error);
        Debug.Log("WORLD SWAP UI CHECKS "+(passed?"COMPLETE: ":"FAILED: ")+checks+"; scene="+SessionState.GetBool(SceneMode,false)); EditorApplication.Exit(passed?0:1);
    }
    static Part Drop(string file,Vector3 position)
    {
        var source=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/"+file+".prefab");
        if(source==null) throw new Exception("Missing existing part fixture "+file);
        var root=Object.Instantiate(source,position,Quaternion.identity); owned.Add(root);
        var part=root.GetComponent<Part>(); part.isStartingPart=false;
        // Some supplied part prefabs contain only inactive collider children.
        // This controlled UI fixture needs a selectable actor; do not repair the
        // gameplay asset here. The real-scene arm test uses its original colliders.
        if(root.GetComponentsInChildren<Collider>().Length==0)
        { root.AddComponent<BoxCollider>().size=Vector3.one*0.3f; Debug.Log("UI FIXTURE COLLIDER ONLY: "+file); }
        var body=root.GetComponent<Rigidbody>(); if(body!=null) { body.isKinematic=true; body.useGravity=false; }
        return part;
    }
    static void Target(Part part)
    {
        detector.enabled=false; detector.enabled=true;
        if(part!=null) foreach(var collider in part.GetComponentsInChildren<Collider>()) Call(detector,"OnTriggerStay",collider);
        Call(detector,"Update");
        if(part!=null) Check(equipment.GetSelectedPart()==part,"Existing detector accepts the controlled UI fixture: "+part.name);
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground=true;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        foreach(var device in InputSystem.devices.Where(d=>d is Mouse||d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
        keyboard=InputSystem.AddDevice<Keyboard>(); mouse=InputSystem.AddDevice<Mouse>();
        captures=Path.Combine(Directory.GetParent(Application.dataPath).FullName,"WorldSwapUiCaptures"); Directory.CreateDirectory(captures);
        bool scene=SessionState.GetBool(SceneMode,false);
        if(!scene)
        {
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor); floor.transform.position=new Vector3(0,-0.25f,0); floor.transform.localScale=new Vector3(100,0.5f,100);
            var light=new GameObject("Fixture sunlight").AddComponent<Light>(); owned.Add(light.gameObject); light.type=LightType.Directional; light.transform.rotation=Quaternion.Euler(55,-30,0); light.intensity=1.2f;
            var player=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PartSwapWorldUiSetup.PlayerPath),Vector3.zero,Quaternion.identity); owned.Add(player);
        }
        yield return Frames(3);
        hud=Object.FindObjectsByType<PartSwapWorldHudPresenter>().Single(h=>h.isActiveAndEnabled);
        equipment=hud.GetComponent<EquipManager>(); detector=equipment.GetComponentInChildren<PickupDetector>(); input=equipment.GetComponent<MechPlayerInput>();
        input.AssumeCursorCaptured=true;
        Check(hud.View!=null&&detector.worldSpaceHud==hud,"Shared player owns exactly one connected world view after repeated setup");
        Check(hud.View.Canvas.renderMode==RenderMode.WorldSpace&&!hud.View.Group.blocksRaycasts&&!hud.View.Group.interactable
            &&hud.View.GetComponentsInChildren<Graphic>(true).All(g=>!g.raycastTarget)
            &&hud.View.GetComponentsInChildren<GraphicRaycaster>(true).Length==0&&hud.View.GetComponentsInChildren<Selectable>(true).Length==0
            &&hud.View.GetComponentsInChildren<Collider>(true).Length==0,"The panel cannot consume movement/combat input or add physics");
        if(scene) { yield return SceneFlow(); yield break; }
        equipment.GetComponent<MechMovementController>().enabled=false;
        // Data/input fixtures deliberately cluster large parts around the player.
        // Keep their paused camera deterministic; the scene run retains the real
        // camera/obstruction behavior and validates actual trigger selection.
        Camera.GetComponent<CameraControlThirdPerson>().enabled=false;
        Camera.GetComponent<Unity.Cinemachine.CinemachineBrain>().enabled=false;
        Camera.transform.SetPositionAndRotation(new Vector3(0,4,-10),Quaternion.LookRotation(new Vector3(0,0.6f,1)-new Vector3(0,4,-10)));
        Time.timeScale=0; Target(null); yield return Frames();
        Check(!hud.IsVisible&&hud.DisplayedPart==null,"No active part leaves the UI hidden");
        var arm=Drop("arm_throwingaxe",new Vector3(1,0.3f,1)); Target(arm);
        yield return Frames(2); Check(hud.View.Group.alpha<1,"Appearance preserves an observable fade at paused gameplay time");
        yield return Visible(arm); CheckArm(arm);
        Capture("arms",1280,720); Capture("arms-4x3",1024,768);
        Capture("arms-occluded",1280,720,true);
        var firstView=hud.View; var cannon=Drop("shoulder_cannon",new Vector3(-1,0.3f,1));
        Target(cannon); yield return Frames(2); Check(hud.View.Group.alpha<1,"Target changes retract the existing panel before replacing its data");
        yield return Visible(cannon);
        Check(ReferenceEquals(hud.View,firstView)&&hud.View.DisplayedSlots.SequenceEqual(new[]{PartCategory.ShoulderL,PartCategory.ShoulderR}),"Shoulder selection reuses the view and exposes the actual two shoulder sockets");
        Check(hud.View.Card(0).binding.text.Contains(input.SwapBindingLabel(PartCategory.ShoulderL))
            &&hud.View.Card(1).binding.text.Contains(input.SwapBindingLabel(PartCategory.ShoulderR)),"Shoulder prompts show the actual 1/2 actions");
        var basic=hud.View.transform.Find("New Part/Stats Right").GetComponent<TMP_Text>();
        Check(!hud.View.transform.Find("New Part/Stats Left").gameObject.activeSelf&&basic.gameObject.activeSelf
            &&basic.text=="Damage 100\nFire rate 0.33/s\nRange 5\nAmmo 20","The new cannon's four basic numeric stats appear on the upper card's right side");
        Check(basic.rectTransform.anchoredPosition.x>=338&&basic.fontSizeMax<=22
            &&hud.View.Card(1).comparison.text.Contains("+70")&&hud.View.Card(1).comparison.text.Contains("-0.67/s")
            &&hud.View.Card(1).comparison.text.Contains("-30"),"New stats use a smaller font beside the name; the old crossbow shows actual damage, fire-rate and ammo differences");
        Capture("shoulders",1280,720);
        yield return PlacementFlow(cannon,arm);
        var head=Drop("head_base",new Vector3(0.6f,0.3f,1)); Target(head); yield return Visible(head);
        Check(hud.View.DisplayedSlots.Count==1&&hud.View.Card(0).category==PartCategory.Head&&hud.View.Card(0).available
            &&hud.View.Card(0).binding.text=="[1] Head","Single-slot head exposes key 1");
        Capture("head",1280,720);
        var debug=equipment.GetComponent<MechLocomotionDebug>();
        Check(debug!=null&&debug.TrySwap(1),"Fixture selects a different leg module before testing number-key conflicts");
        var debugFields=new SerializedObject(debug); debugFields.FindProperty("enableSwapShortcuts").boolValue=true; debugFields.ApplyModifiedPropertiesWithoutUndo();
        var heldLegs=equipment.GetEquipped(PartCategory.Legs);
        Time.timeScale=1; yield return Frames(2);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Digit1)); yield return Frames(2);
        Check(equipment.GetEquipped(PartCategory.Head)==head&&equipment.GetEquipped(PartCategory.Legs)==heldLegs,
            "Key 1 equips a single-slot part without firing the old leg-debug shortcut");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState()); Time.timeScale=0; yield return Frames(2);
        Target(arm); yield return Visible(arm);
        var runtime=(InputActionAsset)typeof(MechPlayerInput).GetField("runtimeInputActions",Private).GetValue(input);
        runtime.FindAction("Player/SwapLeft").ApplyBindingOverride("<Keyboard>/z"); yield return Frames();
        Check(hud.View.Card(0).binding.text.Contains("Z"),"Binding changes refresh the UI from the real runtime actions");
        runtime.FindAction("Player/SwapLeft").RemoveAllBindingOverrides(); yield return Frames();
        var originalRight=equipment.GetEquipped(PartCategory.ArmR); equipment.Unequip(PartCategory.ArmR); yield return Frames();
        Check(hud.View.Card(1).current.text=="Empty"&&hud.View.Card(1).available,"Empty sockets retain key 2 without raw-stat noise");
        Check(equipment.TryEquip(originalRight,PartCategory.ArmR),"Fixture restores the existing right-arm occupant through the normal equipment API"); yield return Frames();
        Time.timeScale=1; yield return Frames(2);
        int changed=0; equipment.EquipmentChanged+=()=>changed++;
        var leftBefore=equipment.GetEquipped(PartCategory.ArmL);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Digit2));
        yield return Frames(2);
        Check(arm.isEquipped&&equipment.GetEquipped(PartCategory.ArmR)==arm&&equipment.GetEquipped(PartCategory.ArmL)==leftBefore&&changed==1,
            "Key 2 performs exactly one right-arm swap without replacing the left or invoking debug modules");
        Check(equipment.GetEquipped(PartCategory.Legs)==heldLegs&&arm.RemainingAmmo==Mathf.RoundToInt(arm.Stats.GetStat(StatType.Ammo)),
            "Number-key swapping neither changes legs nor fires the incoming weapon");
        Check(hud.View.Card(1).binding.text=="EQUIPPED"&&hud.View.Group.alpha<1,"The actual right-socket equipment event acknowledges the chosen card");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState()); InputSystem.QueueStateEvent(mouse,new MouseState());
        Time.timeScale=0; Target(null); yield return Frames(2);
        double hiddenBy=EditorApplication.timeSinceStartup+3;
        // Alpha can cross the visibility threshold one frame before the short
        // equipment acknowledgement clears its data. Wait for the final state.
        while((hud.IsVisible||hud.DisplayedPart!=null||!string.IsNullOrEmpty(hud.View.DisplayedName))
            &&EditorApplication.timeSinceStartup<hiddenBy) yield return null;
        Check(!hud.IsVisible&&hud.DisplayedPart==null&&string.IsNullOrEmpty(hud.View.DisplayedName),"After removal the panel hides and clears stale part data");
        detector.enabled=false; Time.timeScale=1; changed=0;
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Digit1)); yield return Frames(2);
        Check(changed==0&&equipment.GetEquipped(PartCategory.Legs)==heldLegs,"Key 1 with no selected pickup does nothing");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState()); Time.timeScale=0; detector.enabled=true; yield return Frames(2);
        var sword=Drop("arm_sword",new Vector3(0.5f,0.3f,1)); Target(sword); yield return Visible(sword);
        Time.timeScale=1; changed=0; yield return Frames(2);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Digit1)); yield return Frames(2);
        Check(equipment.GetEquipped(PartCategory.ArmL)==sword&&equipment.GetEquipped(PartCategory.ArmR)==arm&&changed==1,"Key 1 performs a single left-arm swap without replacing the right");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState()); Time.timeScale=0; yield return Frames(2);
        Time.timeScale=0; Target(cannon); yield return Visible(cannon);
        Time.timeScale=1; yield return Frames(2);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Digit1)); yield return Frames(2);
        Check(equipment.GetEquipped(PartCategory.ShoulderL)==cannon&&equipment.GetEquipped(PartCategory.ArmL)==sword,"Key 1 selects the dropped shoulder part's left socket");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState()); Time.timeScale=0; yield return Frames(2);
        var crossbow=Drop("shoulder_crossbow",new Vector3(0.2f,0.3f,1)); Target(crossbow); yield return Visible(crossbow);
        Time.timeScale=1; yield return Frames(2); InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Digit2)); yield return Frames(2);
        Check(equipment.GetEquipped(PartCategory.ShoulderR)==crossbow&&equipment.GetEquipped(PartCategory.ShoulderL)==cannon,"Key 2 selects the right shoulder independently of attack buttons");
        InputSystem.QueueStateEvent(keyboard,new KeyboardState()); Time.timeScale=0; yield return Frames(2);
        var spare=Drop("shoulder_cannon",new Vector3(0.5f,0.3f,1)); cannon=spare; Target(cannon); yield return Visible(cannon);
        cannon.gameObject.SetActive(false); yield return Frames(3);
        Check(!hud.IsVisible&&hud.DisplayedPart==null,"Disabling the selected ground object cannot leave a ghost panel");
        cannon.gameObject.SetActive(true); Target(cannon); yield return Visible(cannon);
        detector.enabled=false; yield return Frames(2); Check(!hud.IsVisible,"Disabling the existing detector clears its view");
        detector.enabled=true; Target(cannon); yield return Visible(cannon);
        hud.enabled=false; Check(!hud.IsVisible,"Disabling the presenter clears the view immediately");
        hud.enabled=true; yield return Visible(cannon);
        Check(hud.View==firstView&&Object.FindObjectsByType<PartSwapWorldView>().Length==1,"Re-enable and repeated selections retain one reusable panel");
        var root=equipment.gameObject; Object.Destroy(root); yield return Frames(3);
        Check(Object.FindObjectsByType<PartSwapWorldView>().Length==0,"Destroying the player releases its owned view and subscriptions");
    }
    static void CheckArm(Part arm)
    {
        Check(hud.DisplayedPart==equipment.GetSelectedPart()&&hud.View.DisplayedName==arm.Stats.partName,"The displayed target/name comes directly from the existing selected part");
        Check(hud.View.DisplayedSlots.SequenceEqual(new[]{PartCategory.ArmL,PartCategory.ArmR}),"Arm-compatible parts display the real left/right sockets");
        Check(hud.View.Card(0).binding.text=="[1] Left"&&hud.View.Card(1).binding.text=="[2] Right","Arm prompts show only 1/2 and left/right");
        string plain=System.Text.RegularExpressions.Regex.Replace(hud.View.Card(0).comparison.text,"<[^>]+>","");
        Check(plain.Contains("Damage -50")&&plain.Contains("Fire rate +0.57/s")&&!plain.Contains("++")&&!plain.Contains("--")&&plain.Split('\n').Length<=4,
            "Comparisons show actual numeric changes; lower cooldown correctly improves fire rate");
        var basic=hud.View.transform.Find("New Part/Stats Right").GetComponent<TMP_Text>();
        Check(basic.gameObject.activeSelf&&basic.text=="Damage 50\nFire rate 2/s\nRange 3\nAmmo 30",
            "The selected throwing axe shows its own values above the two equipped-part comparisons");
        Check(hud.View.Card(1).comparison.text=="—","Identical equipped/incoming stats do not create repeated unchanged rows");
    }
    static void CheckSide(bool right,string message)
    {
        var corners=new Vector3[4]; hud.View.Panel.GetWorldCorners(corners);
        var points=corners.Select(Camera.WorldToViewportPoint).ToArray();
        float playerX=Camera.WorldToViewportPoint(equipment.transform.position).x;
        Check(right ? points.Min(p=>p.x)>playerX+0.115f : points.Max(p=>p.x)<playerX-0.115f,message);
    }
    static IEnumerator CycleTo(Part part)
    {
        InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.V)); yield return Frames(2);
        InputSystem.QueueStateEvent(keyboard,new KeyboardState()); yield return Frames(2);
        Check(equipment.GetSelectedPart()==part,"Actual V input selects the other nearby part: "+part.Stats.partName);
        yield return Visible(part);
    }
    static IEnumerator PlacementFlow(Part left,Part right)
    {
        Target(left);
        foreach(var collider in right.GetComponentsInChildren<Collider>()) Call(detector,"OnTriggerStay",collider);
        Call(detector,"Update"); yield return Visible(left);
        Check(detector.DistinctNearbyPartCount==2,"Placement fixture has two distinct parts on opposite sides of the stationary player");
        var cameraPosition=Camera.transform.position; var cameraRotation=Camera.transform.rotation;
        CheckSide(false,"Selecting the left-side cannon places its complete panel to the left of the golem");
        Capture("cycle-left",1280,720);
        yield return CycleTo(right);
        CheckSide(true,"Cycling to the right-side axe moves its complete panel to the right of the golem");
        Capture("cycle-right",1280,720); Capture("cycle-right-4x3",1024,768);
        yield return CycleTo(left);
        CheckSide(false,"Cycling back returns the reused panel to the left-side part");
        Check(Camera.transform.position==cameraPosition&&Quaternion.Angle(Camera.transform.rotation,cameraRotation)<0.001f,
            "Cycling changes only UI placement and does not move the gameplay camera");
        yield return CycleTo(right);
        Vector3 original=right.transform.position;
        right.transform.position=new Vector3(-0.005f,original.y,original.z); yield return Frames();
        CheckSide(true,"Tiny center crossings do not make the selected part's panel flicker between sides");
        right.transform.position=new Vector3(-1.2f,original.y,original.z); yield return Frames();
        CheckSide(false,"The same selected part moving clearly across the player updates its panel side");
        right.transform.position=original; yield return Frames();
        Camera.transform.SetPositionAndRotation(new Vector3(0,4,10),Quaternion.LookRotation(new Vector3(0,0.6f,0)-new Vector3(0,4,10)));
        yield return Frames();
        CheckSide(false,"Camera orbit chooses the part's projected side instead of its fixed world-X side");
        Capture("cycle-orbit",1280,720);
        Camera.transform.SetPositionAndRotation(cameraPosition,cameraRotation); yield return Frames();
        CheckSide(true,"Restoring the camera puts the same part's panel back on its visible right side");
        Target(left); yield return Visible(left);
    }
    static IEnumerator SceneFlow()
    {
        Time.timeScale=1;
        var center=detector.GetComponent<Collider>().bounds.center;
        var part=Drop("arm_throwingaxe",center+equipment.transform.right*0.4f); Physics.SyncTransforms();
        yield return Frames(12);
        Check(detector.NearbyParts.Contains(part),"JhMapScene's actual physics trigger discovers the new dropped part");
        for(int i=0;equipment.GetSelectedPart()!=part&&i<detector.NearbyParts.Count+1;i++) { detector.CycleSelection(); yield return Frames(2); }
        yield return Visible(part); CheckArm(part); Time.timeScale=0;
        Capture("jhmap-selected",1600,900);
        Capture("jhmap-occluded",1600,900,true);
        Check(Camera.GetComponent<CameraControlThirdPerson>().Target==equipment.transform,"World UI retains the existing gameplay camera and player target");
        equipment.gameObject.SetActive(false); yield return Frames(2); Check(!hud.IsVisible,"JhMapScene player disable releases the panel cleanly");
    }
    static void Capture(string name,int width,int height,bool occluded=false)
    {
        var camera=Camera; var before=camera.targetTexture; var active=RenderTexture.active;
        var target=new RenderTexture(width,height,24); var texture=new Texture2D(width,height,TextureFormat.RGB24,false);
        var overlays=Object.FindObjectsByType<Canvas>().Where(c=>c.isRootCanvas&&c.renderMode==RenderMode.ScreenSpaceOverlay).ToArray();
        var cameras=overlays.Select(c=>c.worldCamera).ToArray(); var planes=overlays.Select(c=>c.planeDistance).ToArray();
        GameObject blocker=null; Material blockerMaterial=null;
        var backings=hud.View.GetComponentsInChildren<Image>().Where(i=>i.enabled&&i.rectTransform.rect.width>30).ToArray();
        var backingColors=backings.Select(i=>i.color).ToArray();
        try
        {
            camera.targetTexture=target;
            foreach(var canvas in overlays) { canvas.renderMode=RenderMode.ScreenSpaceCamera; canvas.worldCamera=camera; canvas.planeDistance=1; }
            // Isolate depth testing from the expected blending of the authored
            // translucent cards with different scenery behind them.
            if(occluded) foreach(var image in backings) { var color=image.color; color.a=1; image.color=color; }
            Call(hud,"LateUpdate"); Canvas.ForceUpdateCanvases();
            var corners=new Vector3[4]; hud.View.Panel.GetWorldCorners(corners);
            var points=corners.Select(camera.WorldToViewportPoint).ToArray();
            Check(points.All(p=>p.z>0&&p.x>=0.025f&&p.x<=0.975f&&p.y>=0.025f&&p.y<=0.975f),name+": world panel remains inside the gameplay-camera frame");
            float playerX=camera.WorldToViewportPoint(equipment.transform.position).x;
            Check(points.Max(p=>p.x)<=playerX-0.115f || points.Min(p=>p.x)>=playerX+0.115f,name+": panel keeps the player silhouette clear");
            Check(hud.View.transform.position.y>=hud.DisplayedPart.transform.position.y+0.024f,name+": panel base stays above the selected ground anchor");
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest {destination=target}); RenderTexture.active=target;
            texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply(); var shown=texture.GetPixels32();
            if(occluded)
            {
                // A solid mesh entirely between the camera and panel must hide
                // neither its backing cards nor its font atlas. It has no physics.
                var unobstructed=shown;
                blocker=GameObject.CreatePrimitive(PrimitiveType.Cube); blocker.name="UI depth occluder";
                Object.DestroyImmediate(blocker.GetComponent<Collider>());
                const float depthFraction=0.6f;
                var center=(corners[0]+corners[2])*0.5f;
                blocker.transform.SetPositionAndRotation(camera.transform.position+(center-camera.transform.position)*depthFraction,camera.transform.rotation);
                blocker.transform.localScale=new Vector3(Vector3.Distance(corners[0],corners[3])*depthFraction*1.1f,
                    Vector3.Distance(corners[0],corners[1])*depthFraction*1.1f,0.05f);
                blockerMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                blockerMaterial.SetColor("_BaseColor",new Color(0.03f,0.06f,0.8f));
                blocker.GetComponent<Renderer>().sharedMaterial=blockerMaterial;
                RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest {destination=target});
                texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply(); shown=texture.GetPixels32();
                // Sample the cards and their text, excluding gaps/border pixels
                // where sky, trees and other world objects legitimately change.
                var regions=new List<RectInt>(); var imageCorners=new Vector3[4];
                foreach(var image in backings)
                {
                    image.rectTransform.GetWorldCorners(imageCorners); var vertices=imageCorners.Select(camera.WorldToViewportPoint).ToArray();
                    int left=Mathf.Clamp(Mathf.CeilToInt(vertices.Min(p=>p.x)*width)+2,0,width-1),right=Mathf.Clamp(Mathf.FloorToInt(vertices.Max(p=>p.x)*width)-2,0,width-1);
                    int bottom=Mathf.Clamp(Mathf.CeilToInt(vertices.Min(p=>p.y)*height)+2,0,height-1),top=Mathf.Clamp(Mathf.FloorToInt(vertices.Max(p=>p.y)*height)-2,0,height-1);
                    regions.Add(new RectInt(left,bottom,Mathf.Max(0,right-left),Mathf.Max(0,top-bottom)));
                }
                (int text,int backing) Count(Color32[] pixels)
                {
                    int ink=0,backing=0;
                    foreach(var region in regions) for(int y=region.yMin;y<region.yMax;y++) for(int x=region.xMin;x<region.xMax;x++)
                    {
                        var pixel=pixels[y*width+x];
                        if(pixel.r>180&&pixel.g>180&&pixel.b>170) ink++;
                        if(pixel.r<140&&pixel.g<140&&pixel.b<140) backing++;
                    }
                    return (ink,backing);
                }
                var clean=Count(unobstructed); var covered=Count(shown);
                texture.SetPixels32(shown); texture.Apply(); File.WriteAllBytes(Path.Combine(captures,name+".png"),texture.EncodeToPNG());
                texture.SetPixels32(unobstructed); texture.Apply(); File.WriteAllBytes(Path.Combine(captures,name+"-unobstructed.png"),texture.EncodeToPNG());
                Check(clean.text>100&&covered.text>=clean.text*0.9f&&covered.backing>=clean.backing*0.9f,
                    name+": text and card backgrounds remain visible over an opaque foreground model; text="+covered.text+"/"+clean.text+", cards="+covered.backing+"/"+clean.backing);
            }
            hud.View.SetAlpha(0); Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera,new RenderPipeline.StandardRequest {destination=target});
            texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply(); var hidden=texture.GetPixels32();
            int changed=0; for(int i=0;i<shown.Length;i++) if(Math.Abs(shown[i].r-hidden[i].r)+Math.Abs(shown[i].g-hidden[i].g)+Math.Abs(shown[i].b-hidden[i].b)>50) changed++;
            Check(changed>1500&&shown.Count(p=>p.r>220&&p.b>220&&p.g<70)<shown.Length/200,name+": actual UI pixels render without missing shaders");
            hud.View.SetAlpha(1); texture.SetPixels32(shown); texture.Apply(); File.WriteAllBytes(Path.Combine(captures,name+".png"),texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture=before; RenderTexture.active=active;
            for(int i=0;i<backings.Length;i++) backings[i].color=backingColors[i];
            if(blocker!=null) Object.DestroyImmediate(blocker);
            if(blockerMaterial!=null) Object.DestroyImmediate(blockerMaterial);
            for(int i=0;i<overlays.Length;i++) { overlays[i].renderMode=RenderMode.ScreenSpaceOverlay; overlays[i].worldCamera=cameras[i]; overlays[i].planeDistance=planes[i]; }
            Object.Destroy(target); Object.Destroy(texture); Call(hud,"LateUpdate");
        }
    }
}
