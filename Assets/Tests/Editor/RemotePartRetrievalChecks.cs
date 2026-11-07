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
using Object = UnityEngine.Object;

// Disposable-project integration: actual input, player, socket poses, detector and world HUD.
public static class RemotePartRetrievalChecks
{
    const string Pending = "RemotePartRetrievalChecks.Pending";
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static readonly List<GameObject> owned = new List<GameObject>();
    static GameObject player; static EquipManager equipment; static PickupDetector detector;
    static RemotePartRetrieval retrieval; static MechPlayerInput input; static Camera camera;
    static Keyboard keyboard; static KeyboardState keys; static bool pendingKeys, running;
    static int frame = -1, checks; static double deadline;
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new Exception("Disposable project only.");
        foreach (string path in new[] { "Base/FullPlayer_mech_golem", "Base/FullPlayer_mech_heavy", "Specialist/FullPlayer_mech_specialist" })
        {
            var root = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/" + path + ".prefab");
            var remote = root.GetComponent<RemotePartRetrieval>();
            Check(remote != null && remote.pickupRange == 20 && Mathf.Abs(remote.travelDuration - .2f) < .001f, path + " has authored 20m / 0.2s retrieval");
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("REMOTE RETRIEVAL PASS: " + message); }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; deadline = EditorApplication.timeSinceStartup + 180; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Retrieval checks timed out");
            while (steps.Count > 0)
            {
                var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator child) { steps.Push(child); continue; } return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static IEnumerator Frames(int n = 3) { int end = Time.frameCount + n; while (Time.frameCount < end) yield return null; }
    static void Feed() { if (InputState.currentUpdateType == InputUpdateType.Dynamic && pendingKeys) { InputSystem.QueueStateEvent(keyboard, keys); pendingKeys = false; } }
    static IEnumerator Keys(params Key[] pressed) { keys = new KeyboardState(pressed); pendingKeys = true; while (pendingKeys) yield return null; yield return Frames(); }
    static IEnumerator Complete()
    {
        double until = EditorApplication.timeSinceStartup + 12;
        while (retrieval.IsBusy) { if (EditorApplication.timeSinceStartup > until) throw new Exception("Flight did not finish"); yield return null; }
        yield return Frames();
    }
    static Part Drop(string data = "PartData_Arm_Mace", bool sockets = false, bool compound = false)
    {
        var root = GameObject.CreatePrimitive(PrimitiveType.Cube); root.SetActive(false); owned.Add(root);
        root.name = "Remote test " + data; root.transform.position = player.transform.position + new Vector3(0, .6f, 6); root.transform.localScale = Vector3.one * .4f;
        var part = root.AddComponent<Part>(); part.isStartingPart = false; part.data = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/" + data + ".asset");
        if (sockets) foreach (PartCategory slot in Enum.GetValues(typeof(PartCategory)))
        { var socket = new GameObject(slot.ToString()).AddComponent<Socket>(); socket.socketType = slot; socket.transform.SetParent(root.transform, false); }
        if (compound) { var other = new GameObject("Inactive collider"); other.transform.SetParent(root.transform, false); other.AddComponent<BoxCollider>().enabled = false; }
        root.SetActive(true); root.GetComponent<Rigidbody>().isKinematic = true; return part;
    }
    static void Aim(Part part)
    {
        camera.transform.rotation = Quaternion.LookRotation(part.transform.position - camera.transform.position);
        Physics.SyncTransforms(); retrieval.RefreshTarget(); detector.RefreshSelection();
    }
    static bool Text(string value) => player.GetComponent<PartSwapWorldHudPresenter>().View.GetComponentsInChildren<TMP_Text>(true).Any(t => t.text.Contains(value));
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.timeScale = 1;
        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        keyboard = InputSystem.AddDevice<Keyboard>(); InputSystem.onBeforeUpdate += Feed;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(floor); floor.transform.position = Vector3.down * .25f; floor.transform.localScale = new Vector3(100, .5f, 100);
        var light = new GameObject("Test light").AddComponent<Light>(); owned.Add(light.gameObject); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(50, -30, 0);
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath), Vector3.up * .1f, Quaternion.identity); owned.Add(player);
        yield return Frames(6);
        equipment = player.GetComponent<EquipManager>(); detector = player.GetComponentInChildren<PickupDetector>();
        retrieval = player.GetComponent<RemotePartRetrieval>(); input = player.GetComponent<MechPlayerInput>(); input.AssumeCursorCaptured = true;
        player.GetComponent<MechMovementController>().enabled = false;
        camera = player.GetComponent<MechPlayerRig>().GameplayCamera;
        camera.GetComponent<CameraControlThirdPerson>().enabled = false; camera.GetComponent<Unity.Cinemachine.CinemachineBrain>().enabled = false;
        camera.transform.position = new Vector3(0, 4, -8); retrieval.travelDuration = .6f;
        // Isolate basic travel regressions; dedicated anticipation checks use the authored lift/hover/fast return.
        retrieval.weaponLiftDuration=retrieval.weaponHoverDuration=retrieval.weaponLiftHeight=0; retrieval.weaponFlightDuration=.6f;
        int began = 0, arrived = 0, cancelled = 0;
        retrieval.Started += (_, __) => began++; retrieval.Arrived += (_, __) => arrived++; retrieval.Cancelled += (_, __) => cancelled++;
        var part = Drop(compound: true); Aim(part); yield return Frames();
        Check(retrieval.Target == part && equipment.GetSelectedPart() == part && !detector.IsNearby(part), "Camera-centre targeting selects a remote drop outside the local sensor");
        Check(retrieval.CanStart(part) && equipment.PreferredSlotFor(part) == PartCategory.ArmR, "Remote preview prefers the empty right arm");
        var presenter = player.GetComponent<PartSwapWorldHudPresenter>(); presenter.View.Populate(part, equipment, input, detector);
        Check(Text("Retrieve: Right") && !presenter.View.Card(0).available && !presenter.View.Card(1).available, "Existing UI previews F destination and disables distant 1/2 actions");
        yield return Capture("remote-available");
        yield return Keys(Key.Digit1); yield return Keys(); yield return Keys(Key.Digit2); yield return Keys();
        Check(!part.isEquipped && !retrieval.IsBusy, "1/2 cannot bypass distance or start remote travel");
        var startingSword = equipment.GetEquipped(PartCategory.ArmL); var cannon = equipment.GetEquipped(PartCategory.ShoulderR);
        int ammunition = cannon.RemainingAmmo;
        Check(input.RequestPickup(), "F entry point begins a valid retrieval");
        Check(part.RetrievalOwner == retrieval && retrieval.Destination == PartCategory.ArmR && !part.isEquipped && began == 1, "Travel reserves the physical item before equipping");
        Check(!retrieval.TryStart(part) && !equipment.TryEquip(part, PartCategory.ArmR), "Repeated retrieval and ordinary equip cannot steal a flying drop");
        var competitor = new GameObject("Competing owner"); competitor.SetActive(false); owned.Add(competitor);
        var otherSocket = new GameObject("Arm socket").AddComponent<Socket>(); otherSocket.socketType = PartCategory.ArmL; otherSocket.transform.SetParent(competitor.transform, false);
        var otherEquipment = competitor.AddComponent<EquipManager>(); var otherRetrieval = competitor.AddComponent<RemotePartRetrieval>(); competitor.SetActive(true);
        Check(!otherRetrieval.TryStart(part) && !otherEquipment.TryEquip(part, PartCategory.ArmL), "A second owner cannot reserve or equip the same flying item"); competitor.SetActive(false);
        Check(!part.GetComponentsInChildren<Collider>(true).Any(c => c.enabled) && !part.GetComponent<Rigidbody>().detectCollisions, "Flying part has no physical obstruction or trigger collisions");
        Vector3 start = part.transform.position;
        while(retrieval.IsBusy && (part.transform.position-start).sqrMagnitude < .01f) yield return null;
        Check(retrieval.IsBusy && (part.transform.position - start).sqrMagnitude >= .01f, "The real dropped item visibly moves during travel: busy="+retrieval.IsBusy+", arrived="+arrived+", cancelled="+cancelled+", position="+part.transform.position);
        player.transform.position += Vector3.right * 1.5f; Physics.SyncTransforms();
        yield return Keys(Key.E); yield return Keys();
        Check(cannon.RemainingAmmo == ammunition - 1, "Existing E cannon can fire while retrieval is in flight");
        yield return Complete();
        Check(equipment.GetEquipped(PartCategory.ArmR) == part && equipment.GetEquipped(PartCategory.ArmL) == startingSword && arrived == 1, "Arrival fills the empty socket without dropping the other arm");
        Check(part.RetrievalOwner == null && part.transform.parent.GetComponent<Socket>().socketType == PartCategory.ArmR, "Arrival releases reservation and uses the actual socket");
        Check(Vector3.Distance(part.transform.position, part.transform.parent.position) < .001f && Vector3.Distance(part.transform.lossyScale, part.authoredScale * equipment.partScale) < .001f, "Moving player's final attachment pose and scale match ordinary equip");

        var replace = Drop(); Aim(replace); Check(input.RequestPickup(), "Full arms accept a deterministic replacement");
        Check(equipment.GetEquipped(PartCategory.ArmL) == startingSword && retrieval.Destination == PartCategory.ArmL, "Outgoing left weapon stays equipped during travel");
        yield return Complete();
        Check(equipment.GetEquipped(PartCategory.ArmL) == replace && !startingSword.isEquipped && startingSword.GetComponent<Rigidbody>().detectCollisions, "Arrival performs the ordinary outgoing-item drop");
        var targetController = player.GetComponent<MechTargetLockController>();
        var lockTarget = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(lockTarget); lockTarget.transform.position = player.transform.position + Vector3.forward * 9;
        var targetable = lockTarget.AddComponent<MechTargetable>(); targetController.ProcessLockButton(true, true, false, .01f);
        var aimMode = targetController.IsHardLockMode; var enemy = targetController.CurrentTarget;
        var held = Drop("PartData_Shoulder_LargeCrossbow"); held.transform.position += Vector3.right * 1; Aim(held);
        yield return Keys(Key.F); yield return Complete(); yield return Frames(10); yield return Keys();
        Check(equipment.GetEquipped(PartCategory.ShoulderL) == held && began == 3 && arrived == 3, "Held F retrieves once and fills empty shoulder without replacing the E cannon");
        Check(targetController.IsHardLockMode == aimMode && targetController.CurrentTarget == enemy, "Pickup selection and F leave enemy lock target and mode unchanged");
        Object.Destroy(lockTarget); yield return Frames();
        var moving = Drop(); Aim(moving); var movement = player.GetComponent<MechMovementController>(); movement.enabled = true;
        Vector3 beforeDash = player.transform.position; Check(input.RequestPickup(), "Moving-player fixture starts");
        yield return Keys(Key.W, Key.LeftShift); yield return Frames(2); yield return Keys(); yield return Complete(); movement.enabled = false;
        Check(Vector3.Distance(beforeDash, player.transform.position) > .3f && moving.isEquipped, "Real movement/dash input remains available while the item follows the player");
        // Check a corrected weapon on the original Specialist torso before replacing that torso below.
        var crossbowRoot = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/Parts/arm_crossbow.prefab")); owned.Add(crossbowRoot);
        var corrected = crossbowRoot.GetComponent<Part>(); corrected.isStartingPart = false; corrected.transform.position = player.transform.position + new Vector3(0, 1, 5);
        crossbowRoot.GetComponent<Rigidbody>().isKinematic = true;
        var correctedSlot = equipment.PreferredSlotFor(corrected).Value;
        Check(equipment.TryGetAttachmentPose(corrected, correctedSlot, out var expectedPose, out var expectedScale), "Real crossbow exposes the same corrected attachment destination used by equip");
        Check(retrieval.TryStart(corrected), "Existing animated crossbow supports retrieval"); yield return Complete();
        var correctedSocket = corrected.transform.parent.GetComponent<Socket>(); var correction = correctedSocket.AttachmentFor(corrected);
        Check(Quaternion.Angle(correction.rotation, Quaternion.identity) > 1 && Quaternion.Angle(corrected.transform.rotation, correctedSocket.transform.rotation * correction.rotation) < .1f,
            "Player crossbow retains its configured non-identity socket correction on arrival");
        yield return BodyAndPose();
        yield return SelectionAndRange();
        yield return Cancellation();
        Check(began == arrived + cancelled, "Every accepted flight reports exactly one arrival or cancellation");
        foreach (var item in owned) if (item != null) Object.Destroy(item); yield return Frames(4);
    }
    static IEnumerator BodyAndPose()
    {
        foreach (var data in new[] { "PartData_Head", "PartData_Legs", "PartData_Torso" })
        {
            var part = Drop(data, sockets: data == "PartData_Torso"); Aim(part);
            Check(input.RequestPickup(), data + " begins retrieval into its actual single slot"); yield return Complete();
            Check(part.isEquipped && part.RetrievalOwner == null, data + " arrives without losing weapon occupants");
        }
        Check(equipment.GetEquipped(PartCategory.ShoulderR) != null && equipment.GetEquipped(PartCategory.ArmR) != null, "Torso replacement transfers existing occupied sockets");
        var invalid = Drop("PartData_Torso"); Aim(invalid);
        Check(!retrieval.CanStart(invalid) && !retrieval.TryStart(invalid), "A torso missing occupied sockets is rejected without taking ownership"); Object.Destroy(invalid.gameObject);
    }
    static IEnumerator SelectionAndRange()
    {
        var a = Drop(); var b = Drop(); b.transform.position = a.transform.position + Vector3.forward * 2; Aim(a);
        Check(retrieval.Target == a, "Closest-to-reticle drop wins with distance tie breaking");
        bool stable = true; for (int i = 0; i < 8; i++) { retrieval.RefreshTarget(); stable &= retrieval.Target == a; }
        Check(stable, "Compound/drop query ordering does not make selection flicker");
        a.gameObject.SetActive(false); b.gameObject.SetActive(false);
        var far = Drop(); far.transform.position = player.transform.position + new Vector3(0, .8f, 19); Aim(far);
        Check(retrieval.Target == far && retrieval.InRange(far) && retrieval.CanStart(far), "A 19m drop is inside the extended retrieval range");
        far.transform.position = player.transform.position + new Vector3(0, .8f, 21); Aim(far);
        Check(retrieval.Target == far && !retrieval.InRange(far) && !retrieval.TryStart(far), "Beyond 20m a preview remains visible but retrieval is rejected");
        player.GetComponent<PartSwapWorldHudPresenter>().View.Populate(far, equipment, input, detector);
        Check(Text("Out of range (20m)"), "Pickup panel states the configured range limit");
        yield return Capture("remote-out-of-range");
        far.gameObject.SetActive(false);
        var blocked = Drop(); Aim(blocked);
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(wall); wall.transform.position = (player.transform.position + blocked.transform.position) * .5f + Vector3.up * 2; wall.transform.localScale = new Vector3(10, 8, .3f);
        Physics.SyncTransforms(); retrieval.RefreshTarget();
        Check(retrieval.Target != blocked && !retrieval.TryStart(blocked), "Solid world walls block both targeting and request validation");
        wall.SetActive(false); blocked.gameObject.SetActive(false); yield return Frames();
    }
    static IEnumerator Cancellation()
    {
        var part = Drop(compound: true); part.GetComponent<Rigidbody>().useGravity = false; Aim(part); Vector3 scale = part.transform.lossyScale;
        Check(input.RequestPickup(), "Cancellation fixture starts"); yield return Frames(2); retrieval.Cancel();
        Check(!part.IsRetrieving && !part.isEquipped && part.GetComponent<Rigidbody>().isKinematic && !part.GetComponent<Rigidbody>().useGravity && part.GetComponent<Rigidbody>().detectCollisions,
            "Cancellation restores the loose item's original rigidbody settings");
        Check(part.GetComponent<Collider>().enabled && !part.transform.Find("Inactive collider").GetComponent<Collider>().enabled && Vector3.Distance(part.transform.lossyScale, scale) < .001f,
            "Cancellation restores per-collider enabled states and original world scale"); part.gameObject.SetActive(false);
        var obstructed = Drop(); Aim(obstructed); Check(input.RequestPickup(), "In-flight obstruction fixture starts");
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); owned.Add(wall); wall.transform.position = (player.transform.position + obstructed.transform.position) * .5f + Vector3.up * 2; wall.transform.localScale = new Vector3(10, 8, .3f);
        Physics.SyncTransforms(); yield return Complete();
        Check(!obstructed.isEquipped && !obstructed.IsRetrieving && obstructed.GetComponent<Collider>().enabled, "New world obstruction cancels travel instead of passing through terrain"); wall.SetActive(false); obstructed.gameObject.SetActive(false);
        var paused = Drop(); Aim(paused); Check(input.RequestPickup(), "Pause fixture starts"); Time.timeScale = 0;
        var position = paused.transform.position; yield return Frames(5);
        Check(retrieval.IsBusy && paused.transform.position == position, "Pause suspends travel without completing equipment");
        Time.timeScale = 1; yield return Complete(); Check(paused.isEquipped, "Unpausing resumes the same retrieval");
        var disabled = Drop(); Aim(disabled); Check(input.RequestPickup(), "Disabled-part fixture starts"); disabled.gameObject.SetActive(false);
        Check(!retrieval.IsBusy && !disabled.IsRetrieving, "Disabling a drop cancels and releases its reservation immediately");
        var lost = Drop(); Aim(lost); Check(input.RequestPickup(), "Destroyed-part fixture starts"); Object.Destroy(lost.gameObject); yield return Frames();
        Check(!retrieval.IsBusy && !retrieval.Destination.HasValue, "Destroyed item leaves no pending flight or invalid equip");
        var changed = Drop(); Aim(changed); Check(input.RequestPickup(), "Changed-destination fixture starts");
        equipment.Unequip(retrieval.Destination.Value); yield return Frames();
        Check(!retrieval.IsBusy && !changed.IsRetrieving && !changed.isEquipped, "Changing the intended occupant cancels instead of replacing an unpreviewed item"); changed.gameObject.SetActive(false);
        var stopped = Drop(); Aim(stopped); Check(input.RequestPickup(), "Disabled-controller fixture starts"); retrieval.enabled = false;
        Check(!stopped.IsRetrieving && stopped.GetComponent<Collider>().enabled, "Disabling retrieval restores the drop and its colliders"); retrieval.enabled = true; stopped.gameObject.SetActive(false);
        var death = Drop(); Aim(death); Check(input.RequestPickup(), "Death fixture starts"); player.GetComponent<CombatHealth>().ApplyDamage(100000);
        Check(!death.IsRetrieving && !death.isEquipped && death.GetComponent<Collider>().enabled, "Player death releases the flying item without equipping it");
    }
    static void Finish(Exception error)
    {
        InputSystem.onBeforeUpdate -= Feed; PreMissionReliabilityChecks.EndLogAudit(); SessionState.SetBool(Pending, false); Time.timeScale = 1;
        if (error != null) Debug.LogException(error);
        Debug.Log("REMOTE RETRIEVAL CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
    static IEnumerator Capture(string name)
    {
        var presenter = player.GetComponent<PartSwapWorldHudPresenter>();
        double until = EditorApplication.timeSinceStartup + 4;
        while (presenter.View.Group.alpha < .99f && EditorApplication.timeSinceStartup < until) yield return null;
        Check(presenter.IsVisible, name + ": world pickup UI is visible before capture");
        var oldTarget = camera.targetTexture; var active = RenderTexture.active;
        var target = new RenderTexture(1280, 720, 24); var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var overlays = Object.FindObjectsByType<Canvas>().Where(c => c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay).ToArray();
        var oldCameras = overlays.Select(c => c.worldCamera).ToArray(); var oldPlanes = overlays.Select(c => c.planeDistance).ToArray();
        try
        {
            camera.targetTexture = target;
            foreach (var canvas in overlays) { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
            typeof(PartSwapWorldHudPresenter).GetMethod("LateUpdate", Hidden).Invoke(presenter, null); Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "RemoteRetrievalCaptures"); Directory.CreateDirectory(path);
            File.WriteAllBytes(Path.Combine(path, name + ".png"), texture.EncodeToPNG());
        }
        finally
        {
            for (int i = 0; i < overlays.Length; i++) { overlays[i].renderMode = RenderMode.ScreenSpaceOverlay; overlays[i].worldCamera = oldCameras[i]; overlays[i].planeDistance = oldPlanes[i]; }
            camera.targetTexture = oldTarget; RenderTexture.active = active; Object.Destroy(target); Object.Destroy(texture);
        }
    }
}
