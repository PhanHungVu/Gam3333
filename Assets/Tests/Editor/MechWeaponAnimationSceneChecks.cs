using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public static class MechWeaponAnimationSceneChecks
{
    const string Pending = "MechWeaponAnimationSceneChecks.Pending";
    static Keyboard keyboard;
    static Mouse mouse;
    static MechMovementController motor;
    static EquipManager equipment;
    static Part originalSword;
    static float next = -1f, deadline;
    static int phase = -2, shots;
    [InitializeOnLoadMethod]
    static void Register() => EditorApplication.update += Update;

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene("Assets/Scenes/Merge Test.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static MechWeaponAnimator Driver(PartCategory category) => equipment.GetEquipped(category).GetComponent<MechWeaponAnimator>();
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); Debug.Log("WEAPON SCENE PASS: " + message); }
    static void Pose(PartCategory category, MechWeaponAnimator.PoseState pose, string family)
    {
        var driver = Driver(category);
        Check(driver != null && driver.Animations.name == family && driver.CurrentPose == pose,
            "Actual input plays " + family + " " + pose + " (actual=" + (driver != null ? driver.CurrentPose.ToString() : "missing") + ")");
    }
    static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
    static void Button(MouseButton? button = null) => InputSystem.QueueStateEvent(mouse,
        button.HasValue ? new MouseState().WithButton(button.Value) : new MouseState());

    static void Update()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        // Batch Unity has no native cursor capture. Keep the real input asset and
        // dispatch code; supply captured-cursor policy through its public boundary.
        if (motor != null) motor.GetComponent<MechCombatController>().ProcessPlayerInput(true);
        if (next < 0f) { next = Time.time + 1.2f; deadline = Time.time + 30f; }
        if (Time.time < next) return;
        try
        {
            if (Time.time > deadline) throw new Exception("Weapon scene timeout at " + phase);
            float delay = 0.15f;
            switch (phase)
            {
                case -2:
                    Time.captureDeltaTime = 1f / 60f;
                    InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    motor = Object.FindAnyObjectByType<MechMovementController>();
                    equipment = motor.GetComponent<EquipManager>();
                    originalSword = equipment.GetEquipped(PartCategory.ArmL);
                    motor.GetComponent<MechCombatController>().ShotFired += _ => shots++;
                    motor.GetComponent<MechCombatController>().ReadPlayerInput = false;
                    var data = new SerializedObject(motor.GetComponent<MechCombatController>());
                    data.FindProperty("showMeleeBox").boolValue = false; data.ApplyModifiedPropertiesWithoutUndo();
                    keyboard = InputSystem.AddDevice<Keyboard>(); mouse = InputSystem.AddDevice<Mouse>();
                    Cursor.lockState = CursorLockMode.Locked;
                    // The first click only captures the cursor, by gameplay design.
                    Keys(); Button(MouseButton.Left);
                    break;
                case -1: Button(); delay = 0.3f; break;
                case 0:
                    Check(equipment.Health > 0f, "Scene player exposes its equipped Health stat");
                    foreach (var pickupName in new[] { "Mace Animation Pickup", "Arm Crossbow Animation Pickup" })
                    {
                        var ground = GameObject.Find(pickupName).GetComponent<MechWeaponAnimator>();
                        Check(!ground.Animator.enabled && !ground.CanAnimate, pickupName + " is static on the ground");
                    }
                    Capture("neutral");
                    delay = 3.3f;
                    break;
                case 1:
                    int neutralCount = 0;
                    foreach (var driver in motor.GetComponentsInChildren<MechWeaponAnimator>())
                        if (driver.NeutralPlaying) neutralCount++;
                    Check(neutralCount == 1, "Standing still selects exactly one real scene weapon for Neutral after three seconds");
                    Keys(Key.W); Button(MouseButton.Left); delay = 0.12f; break;
                case 2:
                    Pose(PartCategory.ArmL, MechWeaponAnimator.PoseState.Fire, "Sword");
                    Check(motor.AnimationData.Speed > 0f, "Sword attack runs alongside existing locomotion");
                    Check(motor.GetComponent<MechEquipmentCombat>().IdleWeapon == null,
                        "Actual movement and attack cancel the stationary Neutral selection");
                    Capture("sword-fire"); Button(); Keys(); delay = 0.5f; break;
                case 3: Button(MouseButton.Right); delay = 0.15f; break;
                case 4:
                    Pose(PartCategory.ArmR, MechWeaponAnimator.PoseState.Fire, "ThrowingAxe");
                    Check(Driver(PartCategory.ArmR).Placement != null, "Actual axe rig exposes its placement bone");
                    Capture("axe-fire"); Button(); Keys(Key.Q); delay = 0.1f; break;
                case 5:
                    Pose(PartCategory.ShoulderL, MechWeaponAnimator.PoseState.Fire, "Cannon");
                    Capture("cannon-fire"); Keys(); delay = 0.3f; break;
                case 6:
                    Pose(PartCategory.ShoulderL, MechWeaponAnimator.PoseState.Reload, "Cannon");
                    Capture("cannon-reload"); Keys(Key.E); delay = 0.1f; break;
                case 7:
                    Pose(PartCategory.ShoulderR, MechWeaponAnimator.PoseState.Fire, "Crossbow");
                    Capture("crossbow-fire"); Keys(); delay = 0.3f; break;
                case 8:
                    Pose(PartCategory.ShoulderR, MechWeaponAnimator.PoseState.Reload, "Crossbow");
                    Pose(PartCategory.ShoulderL, MechWeaponAnimator.PoseState.Reload, "Cannon");
                    Capture("crossbow-reload"); Keys(Key.Digit2, Key.LeftCtrl); delay = 0.2f; break;
                case 9:
                    Check(equipment.GetEquipped(PartCategory.Legs).data.partId == "Legs_Heavy"
                        && equipment.GetEquipped(PartCategory.Legs).GetComponent<MechLegAnimator>().CurrentPose == MechLegAnimator.PoseState.Boost,
                        "Heavy swap/boost works while weapons are reloading");
                    Pose(PartCategory.ShoulderL, MechWeaponAnimator.PoseState.Reload, "Cannon");
                    Keys(); PlacePickup("Mace Animation Pickup"); delay = 0.45f; break;
                case 10: Keys(Key.F); break;
                case 11:
                    Check(Driver(PartCategory.ArmL).Animations.name == "Mace", "F picks up the animated mace normally");
                    // Keep the dropped sword outside pickup range before selecting
                    // the second test pickup; gameplay correctly picks the closest part.
                    originalSword.GetComponent<Rigidbody>().isKinematic = true;
                    originalSword.transform.position += Vector3.right * 100f;
                    Physics.SyncTransforms();
                    Keys(); Button(MouseButton.Left); delay = 0.15f; break;
                case 12:
                    Pose(PartCategory.ArmL, MechWeaponAnimator.PoseState.Fire, "Mace");
                    Capture("mace-fire"); Button(); PlacePickup("Arm Crossbow Animation Pickup"); delay = 0.45f; break;
                case 13: Keys(Key.F); break;
                case 14:
                    Check(Driver(PartCategory.ArmR).Animations.name == "ArmCrossbow", "F picks up the animated arm crossbow normally");
                    Keys(); Button(MouseButton.Right); delay = 0.1f; break;
                case 15:
                    Pose(PartCategory.ArmR, MechWeaponAnimator.PoseState.Fire, "ArmCrossbow");
                    Capture("arm-crossbow-fire"); Button(); delay = 0.3f; break;
                case 16:
                    Pose(PartCategory.ArmR, MechWeaponAnimator.PoseState.Reload, "ArmCrossbow");
                    Capture("arm-crossbow-reload"); delay = 2.2f; break;
                default:
                    Pose(PartCategory.ArmL, MechWeaponAnimator.PoseState.Neutral, "Mace");
                    Pose(PartCategory.ArmR, MechWeaponAnimator.PoseState.Neutral, "ArmCrossbow");
                    Check(shots == 3, "Existing ranged weapons fire once each; animation-only parts add no placeholder shots");
                    Check(Object.FindObjectsByType<MechMovementController>().Length == 1
                        && Camera.main.GetComponent<CameraControlThirdPerson>().Target == motor.transform,
                        "Weapon pickup and firing retain one player and its camera");
                    InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
                    SessionState.SetBool(Pending, false);
                    Debug.Log("WEAPON SCENE CHECKS COMPLETE"); EditorApplication.Exit(0); return;
            }
            phase++; next = Time.time + delay;
        }
        catch (Exception e)
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            if (mouse != null) InputSystem.RemoveDevice(mouse);
            SessionState.SetBool(Pending, false); Debug.LogException(e); EditorApplication.Exit(1);
        }
    }

    static void PlacePickup(string name)
    {
        var pickup = GameObject.Find(name);
        pickup.GetComponent<Rigidbody>().isKinematic = true;
        var collider = pickup.GetComponentInChildren<Collider>();
        pickup.transform.position += motor.transform.position + motor.transform.right * 2.5f - collider.bounds.center;
        Physics.SyncTransforms();
    }

    static void Capture(string label)
    {
        var camera = Camera.main;
        var position = camera.transform.position; var rotation = camera.transform.rotation; float fov = camera.fieldOfView;
        try
        {
            camera.transform.position = motor.transform.position + motor.transform.forward * 7f + motor.transform.right * 3.5f + Vector3.up;
            camera.transform.LookAt(motor.transform.position + Vector3.up * 0.3f); camera.fieldOfView = 42f;
            typeof(CombatFeedbackSceneChecks).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { "weapon-" + label });
        }
        finally { camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov; }
    }
}
