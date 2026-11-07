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
using Object = UnityEngine.Object;

// Stage 3: real device input, authored player/sensor/HUD, ordinary equipment transactions.
public static class RemotePickupControlChecks
{
    const string Pending = "RemotePickupControlChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static readonly List<GameObject> owned = new List<GameObject>();
    static Keyboard keyboard; static Mouse mouse;
    static KeyboardState keys; static MouseState buttons; static bool keysPending, mousePending;
    static int frame = -1, checks; static bool running; static double deadline;
    static EquipManager equipment; static PickupDetector detector; static MechPlayerInput input; static GameObject player;
    static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method, Private).Invoke(target, args);
    static void Check(bool valid, string message) { if (!valid) throw new Exception(message); checks++; Debug.Log("PICKUP CONTROL PASS: " + message); }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
        Check(actions.FindAction("Player/LockOn").bindings.Any(b => b.path == "<Mouse>/middleButton")
            && !actions.FindAction("Player/LockOn").bindings.Any(b => b.path == "<Keyboard>/f")
            && actions.FindAction("Player/Pickup").bindings.Single().path == "<Keyboard>/f", "MMB owns lock and F exclusively owns gameplay pickup");
        Check(actions.FindAction("Player/SwapLeft").bindings.Single().path == "<Keyboard>/1"
            && actions.FindAction("Player/SwapRight").bindings.Single().path == "<Keyboard>/2"
            && actions.FindAction("Player/Cannon").bindings.Single().path == "<Keyboard>/q"
            && actions.FindAction("Player/Crossbow").bindings.Single().path == "<Keyboard>/e", "Explicit swaps and both shoulder attack bindings are preserved");
        Check(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Toggle.prefab").GetComponent<ZooToggle>().interactKey == KeyCode.G
            && AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Misc/SoundCube.prefab").GetComponent<ZooSoundplayer>().interactKey == KeyCode.G,
            "Authored Zoo switch/sound prefabs use G");
        var zoo = EditorSceneManager.OpenScene("Assets/Scenes/ZooScene.unity");
        Check(zoo.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ZooBacktoMain>(true)).All(b => b.interactKey == KeyCode.G), "Zoo's menu pad also uses G instead of pickup F");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Feed()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        if (keysPending) { InputSystem.QueueStateEvent(keyboard, keys); keysPending = false; }
        if (mousePending) { InputSystem.QueueStateEvent(mouse, buttons); mousePending = false; }
    }
    static IEnumerator Frames(int count = 3) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Keys(params Key[] pressed) { keys = new KeyboardState(pressed); keysPending = true; while (keysPending) yield return null; yield return Frames(); }
    static IEnumerator Middle(bool pressed) { buttons = new MouseState().WithButton(MouseButton.Middle, pressed); mousePending = true; while (mousePending) yield return null; yield return Frames(); }
    static IEnumerator Press(Key key)
    {
        yield return Keys(key); yield return Keys();
        if(key==Key.F && player!=null) while(player.GetComponent<RemotePartRetrieval>().IsBusy) yield return null;
    }
    static Part Drop(string asset, bool torsoSockets = false, bool compound = false)
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Cube); root.SetActive(false); root.name = "Control drop " + asset;
        root.transform.position = player.transform.position + new Vector3(.8f, .8f, .8f); root.transform.localScale = Vector3.one * .4f;
        var part = root.AddComponent<Part>(); part.isStartingPart = false;
        part.data = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/" + asset + ".asset");
        if (torsoSockets) foreach (PartCategory slot in Enum.GetValues(typeof(PartCategory)))
        { var socket = new GameObject(slot.ToString()).AddComponent<Socket>(); socket.socketType = slot; socket.transform.SetParent(root.transform, false); }
        if (compound) { var child = new GameObject("Second pickup collider"); child.transform.SetParent(root.transform, false); child.AddComponent<BoxCollider>().size = Vector3.one * .6f; }
        owned.Add(root); root.SetActive(true); root.GetComponent<Rigidbody>().isKinematic = true; return part;
    }
    static void Select(Part part)
    {
        detector.enabled = false; detector.enabled = true;
        if (part != null) foreach (var collider in part.GetComponentsInChildren<Collider>()) Call(detector, "OnTriggerStay", collider);
        Call(detector, "Update"); Physics.SyncTransforms();
        Check(equipment.GetSelectedPart() == part, "The existing nearby detector owns the selected drop");
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; deadline = EditorApplication.timeSinceStartup + 120; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Pickup control checks timed out.");
            while (steps.Count > 0)
            {
                var next = steps.Peek(); if (!next.MoveNext()) { steps.Pop(); continue; }
                if (next.Current is IEnumerator child) { steps.Push(child); continue; } return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.timeScale = 1; InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        // Batch mode has no focused Game view; route these synthetic devices to gameplay explicitly.
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>(); InputSystem.onBeforeUpdate += Feed;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor); floor.transform.position = Vector3.down * .25f; floor.transform.localScale = new Vector3(100, .5f, 100);
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath), Vector3.up * .1f, Quaternion.identity); owned.Add(player);
        input = player.GetComponent<MechPlayerInput>(); input.AssumeCursorCaptured = true;
        yield return Frames(6); player.GetComponent<MechMovementController>().enabled = false;
        equipment = player.GetComponent<EquipManager>(); detector = player.GetComponentInChildren<PickupDetector>();
        // This suite isolates local control regressions; the stage 4 suite covers camera-centre acquisition/travel.
        player.GetComponent<RemotePartRetrieval>().enableRemoteTargeting=false;
        player.GetComponent<RemotePartRetrieval>().travelDuration=.02f;
        player.GetComponent<RemotePartRetrieval>().weaponLiftDuration=0; player.GetComponent<RemotePartRetrieval>().weaponHoverDuration=0;
        player.GetComponent<RemotePartRetrieval>().weaponLiftHeight=0; player.GetComponent<RemotePartRetrieval>().weaponFlightDuration=.02f;
        var combat = player.GetComponent<MechCombat>(); var targeting = player.GetComponent<MechTargetLockController>();
        var camera = player.GetComponent<MechPlayerRig>().GameplayCamera;
        camera.GetComponent<CameraControlThirdPerson>().enabled = false;
        camera.GetComponent<Unity.Cinemachine.CinemachineBrain>().enabled = false;
        camera.transform.SetPositionAndRotation(new Vector3(0, 3, -7), Quaternion.LookRotation(new Vector3(0, 1, 5) - new Vector3(0, 3, -7)));
        targeting.BindCamera(camera); combat.BindCamera(camera);
        var cannon = equipment.GetEquipped(PartCategory.ShoulderR); var sword = equipment.GetEquipped(PartCategory.ArmL);
        var runtime = (InputActionAsset)typeof(MechPlayerInput).GetField("runtimeInputActions", Private).GetValue(input);
        int performed = 0; runtime.FindAction("Player/Crossbow").performed += _ => { performed++; Debug.Log("CONTROL E EVENT frame=" + Time.frameCount); };
        Debug.Log("CONTROL E BINDINGS: enabled=" + runtime.FindAction("Player/Crossbow").enabled + "; controls=" + string.Join(",", runtime.FindAction("Player/Crossbow").controls.Select(c => c.path + "#" + c.device.deviceId)) + "; keyboard=" + keyboard.deviceId);
        Check(cannon != null && cannon.Stats.partId == "backweaponA3" && equipment.GetEquipped(PartCategory.ShoulderL) == null
            && sword != null && equipment.GetEquipped(PartCategory.ArmR) == null, "Normal starter carries its cannon on E/right with sword and empty left shoulder/right arm");
        int shots = 0; combat.ShotFired += shot => { shots++; Object.Destroy(shot.gameObject); };
        yield return Press(Key.Q); Check(shots == 0, "Q has no attack while the normal starter's left shoulder is empty");
        int ammo = cannon.RemainingAmmo; yield return Press(Key.E);
        Check(shots == 1 && cannon.RemainingAmmo == ammo - 1, "E fires the starting cannon and spends one real round: shots=" + shots + ", before=" + ammo + ", after=" + cannon.RemainingAmmo
            + ", input enabled=" + input.isActiveAndEnabled + ", armed=" + typeof(MechPlayerInput).GetField("attacksArmed", Private).GetValue(input)
            + ", performed=" + performed + ", combat enabled=" + combat.isActiveAndEnabled + ", frozen=" + CombatHitStop.BlocksNewActions(player.transform)
            + ", blocked=" + MissionOutcomeController.BlocksGameplay(player.scene) + ", scale=" + Time.timeScale + ", ready=" + cannon.ReadyAt + ", now=" + Time.time);
        var dummy = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(dummy); dummy.transform.position = new Vector3(0, 1, 10);
        var enemy = dummy.AddComponent<MechTargetable>(); Physics.SyncTransforms(); yield return Frames();
        yield return Middle(true); yield return Frames(20);
        Check(targeting.IsHardLockMode && targeting.CurrentTarget == enemy, "MMB toggles hard lock once and holding it does not repeat");
        yield return Middle(false); yield return Press(Key.F);
        Check(targeting.IsHardLockMode, "An F press with no nearby loot leaves the lock mode unchanged");
        yield return Middle(true); yield return Middle(false);
        Check(targeting.IsFreeCamera && !targeting.IsFreeAim, "Second MMB press restores soft camera mode with automatic aim enabled");

        var arm = Drop("PartData_Arm_Mace", compound: true); Select(arm);
        Check(detector.IsNearby(arm) && equipment.PreferredSlotFor(arm) == PartCategory.ArmR, "A compound drop prefers the empty right arm");
        var presenter = player.GetComponentInChildren<PartSwapWorldHudPresenter>(true); presenter.View.Populate(arm, equipment, input, detector);
        Check(presenter.View.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text.Contains("[F] Retrieve: Right")), "The existing world pickup UI previews F's actual destination");
        int changed = 0; equipment.EquipmentChanged += () => changed++;
        yield return Keys(Key.F); yield return Frames(12); yield return Keys();
        while(player.GetComponent<RemotePartRetrieval>().IsBusy) yield return null;
        Check(equipment.GetEquipped(PartCategory.ArmR) == arm && equipment.GetEquipped(PartCategory.ArmL) == sword && changed == 1,
            "Holding F equips the empty compatible slot once without ping-pong swapping compound colliders");
        var nextArm = Drop("PartData_Arm_SmallCrossbow"); Select(nextArm); yield return Press(Key.F);
        Check(equipment.GetEquipped(PartCategory.ArmL) == nextArm && equipment.GetEquipped(PartCategory.ArmR) == arm && !sword.isEquipped,
            "F uses the stable left replacement when both arms are occupied and drops the outgoing weapon");
        var explicitRight = Drop("PartData_Arm_Mace"); Select(explicitRight); yield return Press(Key.Digit2);
        Check(equipment.GetEquipped(PartCategory.ArmR) == explicitRight && equipment.GetEquipped(PartCategory.ArmL) == nextArm, "2 still explicitly swaps the right arm");
        var explicitLeft = Drop("PartData_Arm_Sword"); Select(explicitLeft); yield return Press(Key.Digit1);
        Check(equipment.GetEquipped(PartCategory.ArmL) == explicitLeft && equipment.GetEquipped(PartCategory.ArmR) == explicitRight, "1 still explicitly swaps the left arm");
        var shoulder = Drop("PartData_Shoulder_LargeCrossbow"); Select(shoulder); yield return Press(Key.F);
        Check(equipment.GetEquipped(PartCategory.ShoulderL) == shoulder && equipment.GetEquipped(PartCategory.ShoulderR) == cannon, "F fills the empty shoulder and preserves the starting E cannon");

        var invalidTorso = Drop("PartData_Torso"); Select(invalidTorso); var originalTorso = equipment.GetEquipped(PartCategory.Torso);
        Check(!equipment.PreferredSlotFor(invalidTorso).HasValue, "The preview rejects a torso that cannot retain occupied sockets");
        yield return Press(Key.F); Check(equipment.GetEquipped(PartCategory.Torso) == originalTorso && !invalidTorso.isEquipped, "Rejected F pickup leaves current equipment intact");
        var distant = Drop("PartData_Head"); Select(distant); distant.transform.position += Vector3.forward * 30;
        Check(!detector.IsNearby(distant) && !input.RequestPickup(), "A stale tracked target that moved out of the sensor cannot be picked up");
        var head = Drop("PartData_Head"); Select(head); Time.timeScale = 0; yield return Press(Key.F);
        Check(!head.isEquipped, "Paused gameplay cannot pick up equipment"); Time.timeScale = 1;
        input.AssumeCursorCaptured = false; Cursor.lockState = CursorLockMode.None; yield return Press(Key.F);
        Check(!head.isEquipped, "F does not pick up while the cursor is released"); input.AssumeCursorCaptured = true;
        Select(head); yield return Press(Key.F); Check(equipment.GetEquipped(PartCategory.Head) == head, "F supports a non-weapon single head slot");
        var legs = Drop("PartData_Legs"); Select(legs); yield return Press(Key.F); Check(equipment.GetEquipped(PartCategory.Legs) == legs, "F supports the single legs slot");
        var torso = Drop("PartData_Torso", torsoSockets: true); Select(torso); yield return Press(Key.F);
        Check(equipment.GetEquipped(PartCategory.Torso) == torso && equipment.GetEquipped(PartCategory.ShoulderR) == cannon
            && equipment.GetEquipped(PartCategory.ArmL) == explicitLeft, "F supports a compatible torso and preserves transferred weapon occupants");
        Check(targeting.IsFreeCamera && !targeting.IsFreeAim, "Equipment actions never change the existing soft/hard aim state");
        foreach (var item in owned) if (item != null) Object.Destroy(item); yield return Frames(4);
    }
    static void Finish(Exception error)
    {
        InputSystem.onBeforeUpdate -= Feed; SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit(); Time.timeScale = 1;
        if (error != null) Debug.LogException(error);
        Debug.Log("PICKUP CONTROL CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
}
