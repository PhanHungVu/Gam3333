using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Run only in the disposable review project. No HUD setup or HUD assets are used.
public static class QuestAndControlRevisionChecks
{
    const string Pending = "QuestAndControlRevisionChecks.Pending";
    const string BoardOnly = "QuestAndControlRevisionChecks.BoardOnly";
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static bool running; static double stableAt, deadline; static int checks, lastFrame = -1;
    static Mouse mouse; static Keyboard keyboard;
    static MouseState mouseState; static KeyboardState keyState; static bool mousePending, keysPending;
    static PreMissionFlowController Flow => Object.FindAnyObjectByType<PreMissionFlowController>();
    static PreMissionStoneBoard Board => Flow.StoneBoard;
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
    static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private).Invoke(owner, args);
    [InitializeOnLoadMethod] static void Register() { EditorApplication.update += Step; stableAt = EditorApplication.timeSinceStartup + 3; }
    public static void RunBatch() => Request(false);
    public static void RunBoardBatch() => Request(true);
    static void Request(bool boardOnly)
    {
        var root = Directory.GetParent(Application.dataPath).FullName;
        if (!Application.isBatchMode || !root.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) throw new Exception("Disposable project only");
        SessionState.SetBool(BoardOnly, boardOnly);
        SessionState.SetBool(Pending, true); stableAt = EditorApplication.timeSinceStartup + 3;
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; Debug.Log("CONTROL REVISION PASS: " + message); }
    static void Step()
    {
        try
        {
            if (SessionState.GetBool(Pending, false) && !EditorApplication.isPlaying)
            {
                if (EditorApplication.isCompiling) { stableAt = EditorApplication.timeSinceStartup + 3; return; }
                if (EditorApplication.timeSinceStartup < stableAt) return;
                SessionState.SetBool(Pending, false);
                PreMissionPortraitSlabSetup.UpgradeQuestModelsOnly(); PreMissionPortraitSlabSetup.UpgradeQuestModelsOnly();
                EditorSceneManager.OpenScene("Assets/Scenes/PreMissionScene.unity");
                GraphicsSettings.defaultRenderPipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset"); QualitySettings.renderPipeline = null;
                running = true; deadline = EditorApplication.timeSinceStartup + 240; EditorApplication.EnterPlaymode();
            }
            if (!running || !EditorApplication.isPlaying || EditorApplication.isCompiling || lastFrame == Time.frameCount) return;
            lastFrame = Time.frameCount; PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Revision checks timed out");
            if (steps.Count == 0) steps.Push(Run());
            while (steps.Count > 0)
            {
                var current = steps.Peek(); if (!current.MoveNext()) { (current as IDisposable)?.Dispose(); steps.Pop(); continue; }
                if (current.Current is IEnumerator child) { steps.Push(child); continue; } return;
            }
            Finish(null);
        }
        catch (Exception e) { Finish(e); }
    }
    static void Finish(Exception e)
    {
        running = false; InputSystem.onBeforeUpdate -= Feed; SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit();
        if (e != null) Debug.LogException(e);
        Debug.Log("CONTROL REVISION CHECKS " + (e == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(e == null ? 0 : 1);
    }
    static IEnumerator Frames(int n = 2) { int until = Time.frameCount + n; while (Time.frameCount < until) yield return null; }
    static IEnumerator Delay(float seconds) { float until = Time.realtimeSinceStartup + seconds; while (Time.realtimeSinceStartup < until) yield return null; }
    static IEnumerator Ready() { yield return Frames(); while (Flow.IsPosting || Flow.IsTransitioning) yield return null; yield return Frames(); }
    static void Feed()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        if (mousePending) { InputSystem.QueueStateEvent(mouse, mouseState); mousePending = false; }
        if (keysPending) { InputSystem.QueueStateEvent(keyboard, keyState); keysPending = false; }
    }
    static IEnumerator Keys(params Key[] keys)
    { keyState = new KeyboardState(keys); keysPending = true; while (keysPending) yield return null; yield return Frames(); }
    static IEnumerator MouseButtons(bool left, bool right, bool middle = false)
    {
        mouseState = new MouseState();
        if (left) mouseState = mouseState.WithButton(MouseButton.Left);
        if (right) mouseState = mouseState.WithButton(MouseButton.Right);
        if (middle) mouseState = mouseState.WithButton(MouseButton.Middle);
        mousePending = true; while (mousePending) yield return null; yield return Frames();
    }
    static IEnumerator Capture(string name)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
        { Debug.Log("CONTROL REVISION: headless layout check; skipped capture " + name); yield break; }
        var camera = Board.BoardCamera; var old = camera.targetTexture; float aspect = camera.aspect;
        var target = new RenderTexture(1280, 720, 24); camera.targetTexture = target; camera.aspect = 1280f / 720; camera.ResetProjectionMatrix();
        Board.RefreshCameraFrame(); Object.FindAnyObjectByType<PreMissionQuestDetailsSlab>().RefreshPlacement(); yield return Frames();
        camera.Render(); var previous = RenderTexture.active; RenderTexture.active = target;
        var picture = new Texture2D(1280, 720, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); picture.Apply();
        var folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ControlRevisionCaptures"); Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, name + ".png"), picture.EncodeToPNG());
        RenderTexture.active = previous; camera.targetTexture = old; camera.aspect = aspect; camera.ResetProjectionMatrix();
        Object.DestroyImmediate(picture); target.Release(); Object.DestroyImmediate(target); Board.RefreshCameraFrame(); yield return Frames();
    }
    static IEnumerator Run()
    {
        PreMissionReliabilityChecks.BeginLogAudit(); Application.runInBackground = true; Time.timeScale = 0;
        foreach (var device in InputSystem.devices.Where(d => d is Mouse || d is Keyboard).ToArray()) InputSystem.DisableDevice(device);
        yield return Ready();
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(PreMissionPortraitSlabSetup.BigSlabPath);
        var mainPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/PreMission/StoneBoard/MainQuestStone.prefab").GetComponent<QuestStoneView>();
        var mainMeshes = Get<Mesh[]>(mainPrefab, "slabVariants");
        Check(mainMeshes.Length == 1 && mainMeshes[0].vertexCount == source.GetComponentInChildren<MeshFilter>().sharedMesh.vertexCount, "Main slab uses the supplied BigSlab geometry with no random side variants");
        Check(Get<MeshRenderer>(mainPrefab, "rock").sharedMaterial.GetTextureScale("_BaseMap") == Vector2.one, "New main texture uses the model's full authored UV mapping");
        var side = Board.Stones.First(s => !s.IsMain); var render = Get<MeshRenderer>(side, "rock");
        Check(Vector3.Distance(render.transform.localScale, new Vector3(1.705f, .396f, 2.585f)) < .001f, "Side slabs are ten percent larger");
        var label = Get<TMP_Text>(side, "label"); var heading = Get<GameObject>(side, "categoryInscription");
        var headingMesh = heading.GetComponentInChildren<MeshFilter>();
        var headingCenter = Get<Transform>(side, "visual").InverseTransformPoint(headingMesh.transform.TransformPoint(headingMesh.sharedMesh.bounds.center));
        Check(Mathf.Abs(label.transform.localPosition.z - .24f) < .001f && Mathf.Abs(headingCenter.z - .51f) < .002f, "Side heading moves halfway toward the mission-type line");
        typeof(QuestLabelReadabilityChecks).GetMethod("CheckRadialLayouts", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        Check(true, "Larger side slabs retain separate outward travel paths and table bounds across 64 seeds");
        yield return Capture("larger-side-slabs");
        for (int i = 0; i < 2; i++) { QuestStoneBoardChecks.CompleteSmallForTest(Flow); yield return Ready(); }
        var beforeArrival = Board.Stones.Where(s => !s.IsMain).ToDictionary(s => s.Offer.Slot,
            s => new Vector2(s.transform.localPosition.x, s.transform.localPosition.z));
        QuestStoneBoardChecks.CompleteSmallForTest(Flow);
        yield return (IEnumerator)typeof(QuestLabelReadabilityChecks).GetMethod("ObserveMainArrival", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { beforeArrival });
        yield return Ready();
        var main = Board.Stones.First(s => s.IsMain);
        Check(Get<MeshRenderer>(main, "rock").GetComponent<MeshFilter>().sharedMesh == mainMeshes[0] && Board.Stones.Count == 7, "The normal third-completion arrival uses BigSlab and keeps six selectable side quests");
        var settled = Board.Stones.ToDictionary(s => s.Offer.Slot, s => s.transform.localPosition);
        yield return Capture("big-slab-board"); Flow.SelectQuest(main.Offer.Quest); yield return Ready(); yield return Capture("big-slab-inspection"); Flow.Back(); yield return Ready();
        Check(Board.Stones.All(s => Vector3.Distance(s.transform.localPosition, settled[s.Offer.Slot]) < .001f), "Inspecting and declining a quest preserves the run's settled slab positions");
        if (SessionState.GetBool(BoardOnly, false)) yield break;
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) root.SetActive(false);
        var scene = SceneManager.CreateScene("Control Scheme Fixture"); SceneManager.SetActiveScene(scene); Time.timeScale = 1;
        yield return CheckControls();
    }
    static PartData Body(string name, PartType type) => new PartDataBuilder(name, type).Data;
    sealed class PartDataBuilder
    {
        public readonly PartData Data;
        public PartDataBuilder(string name, PartType type)
        { Data = ScriptableObject.CreateInstance<PartData>(); Data.partName = name; Data.partType = type; Data.stats = new List<StatEntry>(); }
    }
    static PartData Gun(string name, PartType type) => TestMech.WithMagazine(TestMech.Ranged(name, type, PartSubtype.Projectile, 1, 1000, .1f, 10, 20, .05f), 5, .2f);
    static void MotorTicks(MechMovementController motor, Transform camera, int count, Vector2 direction)
    { for (int i = 0; i < count; i++) motor.Tick(new MechLocomotionInput { move = direction }, camera, .02f); }
    static IEnumerator CheckControls()
    {
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        mouse = InputSystem.AddDevice<Mouse>(); keyboard = InputSystem.AddDevice<Keyboard>(); InputSystem.onBeforeUpdate += Feed;
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = Vector3.down * .5f; floor.transform.localScale = new Vector3(2000, 1, 2000);
        var view = new GameObject("Control Camera").AddComponent<Camera>(); view.tag = "MainCamera"; view.transform.position = new Vector3(0, 3, -10); view.transform.LookAt(new Vector3(0, 1, 20));
        var player = new GameObject("Controls Test Player"); player.SetActive(false); player.transform.position = Vector3.up * .03f;
        var capsule = player.AddComponent<CharacterController>(); capsule.height = 2; capsule.center = Vector3.up;
        var health = player.AddComponent<CombatHealth>(); var energy = player.AddComponent<MechEnergyController>();
        var motor = player.AddComponent<MechMovementController>(); var targeting = player.AddComponent<MechTargetLockController>();
        var loadout = TestMech.Equip(player, (PartCategory.Head, Body("Head", PartType.Head)), (PartCategory.Torso, Body("Torso", PartType.Torso)),
            (PartCategory.Legs, Body("Legs", PartType.Legs)), (PartCategory.ArmL, Gun("Left", PartType.Arm)), (PartCategory.ArmR, Gun("Right", PartType.Arm)),
            (PartCategory.ShoulderL, Gun("Left shoulder", PartType.Shoulder)), (PartCategory.ShoulderR, Gun("Right shoulder", PartType.Shoulder)));
        var equipment = player.GetComponent<EquipManager>(); var combat = player.AddComponent<MechCombat>(); var input = player.AddComponent<MechPlayerInput>();
        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions"); Set(input, "inputActions", asset); input.AssumeCursorCaptured = true;
        player.SetActive(true); motor.BindCamera(view); combat.BindCamera(view); targeting.BindCamera(view); Physics.SyncTransforms();
        int leftShots = 0, rightShots = 0, leftShoulderShots = 0, rightShoulderShots = 0, headRequests = 0, torsoRequests = 0, legRequests = 0;
        bool leftGround = false; int dodges = 0;
        motor.QuickBoostStarted += _ => dodges++;
        motor.StateChanged += (before, after) => { if ((before == MechMovementController.MovementState.Grounded || before == MechMovementController.MovementState.GroundBoost) && after == MechMovementController.MovementState.Airborne) leftGround = true; };
        combat.ShotFired += p => { if (p.Source.Slot == PartCategory.ArmL) leftShots++; if (p.Source.Slot == PartCategory.ArmR) rightShots++; if (p.Source.Slot == PartCategory.ShoulderL) leftShoulderShots++; if (p.Source.Slot == PartCategory.ShoulderR) rightShoulderShots++; };
        input.AbilityRequested += (slot, part) => { if (slot == PartCategory.Head) headRequests++; if (slot == PartCategory.Torso) torsoRequests++; if (slot == PartCategory.Legs) legRequests++; };
        yield return Frames(4);
        Check(!targeting.IsFreeAim && targeting.IsFreeCamera, "New players start in free-camera mode with automatic weapon aiming enabled");
        var dummy = GameObject.CreatePrimitive(PrimitiveType.Cube); dummy.name = "Auto-aim Enemy"; dummy.transform.position = new Vector3(0, 1, 20); var enemy = dummy.AddComponent<MechTargetable>();
        Physics.SyncTransforms(); yield return Frames(3);
        Check(targeting.CurrentTarget == enemy && !targeting.IsHardLocked, "Enemies entering the center aim radius are selected without locking the camera");
        Check(Vector3.Distance(AimSolver.AimPoint(targeting, view, player.transform, 60, ~0), enemy.AimPosition) < .001f, "Free-camera projectiles aim at the selected enemy");
        yield return Keys(Key.F); yield return Delay(1.3f);
        Check(targeting.IsHardLocked && !targeting.IsFreeAim, "F locks immediately and holding it does not enter the old free-aim mode"); yield return Keys();
        yield return Keys(Key.F); yield return Keys();
        Check(targeting.IsFreeCamera && targeting.CurrentTarget == null && targeting.IsFreeAim, "Second F press enters no-lock mode and clears automatic weapon aim");
        yield return Keys(Key.F); yield return Keys();
        Check(targeting.IsFreeCamera && targeting.CurrentTarget == enemy && !targeting.IsFreeAim, "Third F press restores free-camera automatic weapon aim");
        yield return MouseButtons(false, false, true); yield return MouseButtons(false, false);
        Check(targeting.IsFreeCamera, "MMB has no gameplay lock-on action while manual aiming is deferred");
        dummy.transform.position = new Vector3(15, 1, 20); Physics.SyncTransforms(); yield return Frames(3);
        Check(targeting.CurrentTarget == null, "Automatic aim releases enemies outside the configured screen-space radius");
        dummy.transform.position = new Vector3(0, 1, 20); Physics.SyncTransforms(); yield return Frames(3);
        yield return MouseButtons(true, true); yield return Delay(.75f); yield return MouseButtons(false, false);
        Check(leftShots >= 5 && rightShots >= 5, "Held LMB/RMB repeatedly fire both arms through cooldown and automatic magazine reload");
        int stopped = leftShots + rightShots; yield return Delay(.2f); Check(leftShots + rightShots == stopped, "Releasing the mouse buttons stops both weapons");
        yield return Keys(Key.Q, Key.E); yield return Keys(); Check(leftShoulderShots == 1 && rightShoulderShots == 1, "Q/E independently fire the left/right shoulder weapons");
        yield return Keys(Key.Tab, Key.LeftCtrl); yield return Keys();
        Check(headRequests == 1 && torsoRequests == 1 && !motor.IsAssaultBoosting, "Tab/Ctrl dispatch equipped-part ability requests and Ctrl no longer boosts");
        float landBy = Time.realtimeSinceStartup + 3;
        while (!motor.IsGrounded && Time.realtimeSinceStartup < landBy) yield return null;
        leftGround = false;
        yield return Keys(Key.Space); yield return Keys();
        Debug.Log("CONTROL LEG SAMPLE: requests=" + legRequests + "; leftGround=" + leftGround + "; grounded=" + motor.IsGrounded + "; velocity=" + motor.Velocity);
        Check(legRequests == 1 && leftGround, "Space dispatches the leg request and preserves its existing jump");
        input.RequestHeadAbility(); input.RequestTorsoAbility(); input.RequestLegAbility();
        Check(headRequests == 2 && torsoRequests == 2 && legRequests == 2, "Public ability functions dispatch the same equipped-part callbacks as the keys");
        Time.timeScale = 0;
        input.RequestHeadAbility(); input.RequestTorsoAbility(); input.RequestLegAbility();
        Check(headRequests == 2 && torsoRequests == 2 && legRequests == 2, "Public ability requests respect gameplay pause");
        Time.timeScale = 1; input.enabled = false;
        input.RequestHeadAbility(); input.RequestTorsoAbility(); input.RequestLegAbility();
        Check(headRequests == 2 && torsoRequests == 2 && legRequests == 2, "Disabled input cannot dispatch public ability requests");
        input.enabled = true; yield return Frames();
        landBy = Time.realtimeSinceStartup + 3;
        while (!motor.CanQuickBoost && Time.realtimeSinceStartup < landBy) yield return null;
        Check(motor.CanQuickBoost, "Jump settles back to a grounded dodge-ready pose");
        yield return Keys(Key.LeftShift); yield return Keys(); Check(dodges == 1, "Shift triggers dodge");
        Check(Mathf.Abs(energy.Current - energy.Capacity) < .001f, "Dodge and movement no longer consume energy");
        landBy = Time.realtimeSinceStartup + 3;
        while (!motor.CanQuickBoost && Time.realtimeSinceStartup < landBy) yield return null;
        energy.TrySpend(energy.Capacity); yield return Keys(Key.LeftShift); yield return Keys();
        Check(dodges == 2 && energy.Current == 0, "Dodge remains available with an empty legacy energy component"); yield return Delay(.8f);
        TestMech.Advance(player, 1); combat.ReloadWeapons(); yield return Delay(.25f); TestMech.Advance(player, 1);
        Check(combat.TryAttack(PartCategory.ArmL).Started, "A loaded arm can fire before manual reload"); var arm = combat.GetPart(PartCategory.ArmL); int ammo = arm.RemainingAmmo;
        yield return Keys(Key.R); yield return Keys();
        Check(arm.IsReloading && arm.MagazineRounds == arm.Stats.MagazineSize && arm.RemainingAmmo == ammo, "R refills a partial magazine without creating or discarding ammo");
        float ready = arm.ReadyAt; Check(!combat.TryReload(PartCategory.ArmL) && arm.ReadyAt == ready, "Reload cannot be restarted while already reloading");
        yield return Delay(.25f); Check(!combat.TryReload(PartCategory.ArmL), "A full magazine ignores manual reload");
        input.enabled = false; MotorTicks(motor, view.transform, 50, Vector2.zero);
        Check(motor.IsGrounded && !motor.IsQuickBoosting, "Run timing fixture starts after the previous dodge has settled");
        MotorTicks(motor, view.transform, 270, Vector2.up);
        Check(!motor.IsAutoRunning && motor.AutoRunProgress == 0, "Walking into the target collider does not accumulate the automatic-run timer");
        dummy.transform.position = new Vector3(50, 1, 20); Physics.SyncTransforms();
        MotorTicks(motor, view.transform, 270, Vector2.up);
        Debug.Log("CONTROL RUN SAMPLE: running=" + motor.IsAutoRunning + "; progress=" + motor.AutoRunProgress + "; grounded=" + motor.IsGrounded + "; state=" + motor.State + "; velocity=" + motor.Velocity + "; walk=" + motor.WalkSpeed);
        Check(motor.IsAutoRunning && motor.Velocity.magnitude > motor.WalkSpeed * 1.5f, "Five seconds of uninterrupted walking starts the existing run speed");
        MotorTicks(motor, view.transform, 1, Vector2.zero); Check(!motor.IsAutoRunning && motor.AutoRunProgress == 0, "Stopping cancels running and clears its timer");
        MotorTicks(motor, view.transform, 270, Vector2.up); health.ApplyDamage(1);
        Check(!motor.IsAutoRunning && motor.AutoRunProgress == 0, "Taking actual damage immediately cancels automatic running");
        MotorTicks(motor, view.transform, 270, Vector2.up); TestMech.Advance(player, 1); combat.TryAttack(PartCategory.ArmR);
        Check(!motor.IsAutoRunning && motor.AutoRunProgress == 0, "A successful weapon attack immediately cancels automatic running");
        for (int i = 0; i < 8; i++) { MotorTicks(motor, view.transform, 60, Vector2.up); TestMech.Advance(player, 1); combat.TryAttack(PartCategory.ArmR); }
        Check(!motor.IsAutoRunning, "Repeated attacks prevent the five-second run timer from accumulating");
        input.enabled = true; yield return Keys();
        var pickup = new GameObject("New Head").AddComponent<Part>(); pickup.transform.position = player.transform.position + Vector3.right; pickup.data = Body("New Head", PartType.Head); equipment.SetSelectedPart(pickup);
        yield return Keys(Key.Digit2); yield return Keys(); Check(!pickup.isEquipped, "A single-slot part ignores slot 2");
        yield return Keys(Key.Digit1); yield return Keys(); Check(equipment.GetEquipped(PartCategory.Head) == pickup, "1 swaps a nearby selected single-slot part");
        var replacement = new GameObject("New Right Arm").AddComponent<Part>(); replacement.transform.position = player.transform.position + Vector3.right; replacement.data = Gun("New Right Arm", PartType.Arm); equipment.SetSelectedPart(replacement);
        yield return Keys(Key.Digit2); yield return Keys(); Check(equipment.GetEquipped(PartCategory.ArmR) == replacement, "2 swaps the selected arm into its right socket");
        input.enabled = false;
        dummy.transform.position = new Vector3(0, 1, 20);
        capsule.enabled = false; player.transform.position = Vector3.up * .03f; capsule.enabled = true; Physics.SyncTransforms();
        var rig = new GameObject("Free Camera Test"); rig.SetActive(false); var rigView = rig.AddComponent<Camera>(); var control = rig.AddComponent<CameraControlThirdPerson>(); Set(control, "target", player.transform); rig.SetActive(true);
        targeting.BindCamera(rigView); targeting.EnterFreeAim(); targeting.ResumeTargeting(); Call(targeting, "SetTarget", enemy);
        Call(control, "ApplySettings"); var cm = Get<CinemachineCamera>(control, "cinemachineCamera");
        Check(cm.LookAt == Get<Transform>(control, "cameraTarget"), "Auto-aim selection leaves the camera looking at its free orbit pivot");
        float yaw = Get<float>(control, "cameraYaw"), pitch = Get<float>(control, "cameraPitch");
        Call(control, "UpdateCameraOrbit", new Vector2(50, -50), .1f);
        Check(Get<float>(control, "cameraYaw") > yaw && Get<float>(control, "cameraPitch") > pitch, "Free-camera mode accepts full horizontal and vertical mouse look while auto-aiming");
        targeting.ToggleHardLock(); Call(control, "ApplySettings");
        Check(cm.LookAt == enemy.AimTransform, "Manual camera lock makes Cinemachine follow the selected enemy");
    }
}
