using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

// Real player-loop animation, keyboard swaps, and the scene's ordinary F pickup.
public static class MechAnimatedLegSceneChecks
{
    const string Pending = "MechAnimatedLegSceneChecks.Pending";
    static Keyboard keyboard;
    static MechMovementController motor;
    static EquipManager equipment;
    static Part fast, pickup, torso;
    static Transform leg;
    static Quaternion pose;
    static Transform torsoVisual, headVisual;
    static Vector3 bodyRest, headRest, movingBody, movingHead;
    static float next = -1f, deadline;
    static int phase;

    [InitializeOnLoadMethod]
    static void Register() => EditorApplication.update += Update;

    public static void RunBatch() => Start("Assets/Scenes/A33.unity");
    public static void RunMergeBatch() => Start("Assets/Scenes/Merge Test.unity");
    static void Start(string scene)
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated project copy.");
        EditorSceneManager.OpenScene(scene);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Debug.Log("ANIMATED LEGS SCENE PASS: " + message);
    }

    static void Update()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (next < 0f) { next = Time.time + 1f; deadline = Time.time + 25f; }
        if (Time.time < next) return;
        try
        {
            if (Time.time > deadline) throw new Exception("Animated leg scene check timeout: " + phase);
            if (phase == 0)
            {
                Time.captureDeltaTime = 1f / 60f;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                var motors = Object.FindObjectsByType<MechMovementController>();
                Check(motors.Length == 1, "Scene retains one gameplay motor");
                motor = motors[0];
                equipment = motor.GetComponent<EquipManager>();
                fast = equipment.GetEquipped(PartCategory.Legs);
                torso = equipment.GetEquipped(PartCategory.Torso);
                torsoVisual = torso.transform.Find("Animated Torso Visual");
                headVisual = equipment.GetEquipped(PartCategory.Head).transform.Find("Golem1Head");
                Check(torsoVisual != null && motor.GetComponent<MechUpperBodyAnimation>() != null, "Scene includes the upper-body animation connection");
                bodyRest = torsoVisual.localPosition; headRest = headVisual.localPosition;
                Check(fast.data.partId == "Legs_Fast", "Actual scene defaults to fast legs");
                Check(MechAnimatedLegChecks.ClipWeight(fast.GetComponent<MechLegAnimator>(), "Biped_Neutral") > 0.99f,
                    "Scene starts with planted idle legs");
                Capture("idle");
                keyboard = InputSystem.AddDevice<Keyboard>();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            }
            else if (phase == 1)
            {
                Check(motor.AnimationData.Speed > 5.9f && MechAnimatedLegChecks.ClipWeight(fast.GetComponent<MechLegAnimator>(), "Biped_Run") > 0.99f,
                    "W animates fast legs through the normal Update path");
                Check(Vector3.Distance(torsoVisual.localPosition, bodyRest) > 0.01f && Vector3.Distance(headVisual.localPosition, headRest) > 0.005f,
                    "Fast body and head animate through the normal LateUpdate path");
                Capture("fast");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.Digit2));
            }
            else if (phase == 2)
            {
                var heavy = equipment.GetEquipped(PartCategory.Legs);
                Check(heavy.data.partId == "Legs_Heavy" && motor.AnimationData.Speed > 2.9f && motor.AnimationData.Speed < 3.1f,
                    "Keyboard 2 selects the heavy biped and its movement rating");
                var driver = heavy.GetComponent<MechLegAnimator>();
                Check(MechAnimatedLegChecks.ClipWeight(driver, "Heavy_Walk.001") > 0.99f, "Heavy gait plays after the real keyboard swap");
                leg = driver.Animator.transform.Find("Armature/Root/Leg.L");
                pose = leg.localRotation;
                movingBody = torsoVisual.localPosition; movingHead = headVisual.localPosition;
                Capture("heavy");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            }
            else if (phase == 3)
            {
                Check(Quaternion.Angle(pose, leg.localRotation) > 2f, "Heavy bones keep animating across player-loop frames");
                Check(Vector3.Distance(movingBody, torsoVisual.localPosition) > 0.01f && Vector3.Distance(movingHead, headVisual.localPosition) > 0.005f,
                    "Visible torso and head keep following the heavy gait after swapping");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.Digit1));
            }
            else if (phase == 4)
            {
                Check(equipment.GetEquipped(PartCategory.Legs) == fast && MechAnimatedLegChecks.ClipWeight(fast.GetComponent<MechLegAnimator>(), "Biped_Run") > 0.99f,
                    "Keyboard 1 restores the original fast rig and clip");
                pickup = GameObject.Find("Heavy Legs Pickup").GetComponent<Part>();
                pickup.GetComponent<Rigidbody>().isKinematic = true;
                var collider = pickup.GetComponentInChildren<Collider>();
                // Enter the detector without pushing a solid pickup into the capsule.
                pickup.transform.position += motor.transform.position + motor.transform.right * 2.5f - collider.bounds.center;
                Physics.SyncTransforms();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            }
            else if (phase == 5) InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F, Key.W));
            else if (phase == 6)
            {
                Check(equipment.GetEquipped(PartCategory.Legs) == pickup, "F equips the scene's heavy pickup through detector and equipment input");
                Debug.Log("ANIMATED PICKUP STATE: speed=" + motor.AnimationData.Speed + " grounded=" + motor.IsGrounded
                    + " state=" + motor.AnimationData.State + " blend=" + pickup.GetComponent<MechLegAnimator>().Animator.GetFloat("MoveBlend")
                    + " weight=" + MechAnimatedLegChecks.ClipWeight(pickup.GetComponent<MechLegAnimator>(), "Heavy_Walk.001"));
                Capture("pickup");
                Check(pickup.GetComponent<MechLegAnimator>().Animator.isActiveAndEnabled,
                    "Ordinary pickup enables the heavy rig through the normal equipment path");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            }
            else if (phase == 7)
            {
                Check(motor.AnimationData.Speed > 2.9f && MechAnimatedLegChecks.ClipWeight(pickup.GetComponent<MechLegAnimator>(), "Heavy_Walk.001") > 0.99f,
                    "The picked-up heavy rig completes its transition to the walking clip");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            }
            else
            {
                Check(MechAnimatedLegChecks.ClipWeight(pickup.GetComponent<MechLegAnimator>(), "Heavy_Neutral") > 0.99f,
                    "Releasing movement returns the equipped heavy rig to idle");
                MechAnimationV2Checks.CheckNeutralPose(pickup.GetComponent<MechLegAnimator>(), Check);
                Check(equipment.GetEquipped(PartCategory.Torso) == torso && Camera.main.GetComponent<CameraControlThirdPerson>().Target == motor.transform,
                    "Both swapping paths preserve the torso and existing camera target");
                InputSystem.RemoveDevice(keyboard);
                SessionState.SetBool(Pending, false);
                Debug.Log("ANIMATED LEGS SCENE CHECKS COMPLETE");
                EditorApplication.Exit(0);
                return;
            }
            phase++;
            next = Time.time + 0.8f;
        }
        catch (Exception e)
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            SessionState.SetBool(Pending, false);
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static void Capture(string label)
    {
        var capture = typeof(CombatFeedbackSceneChecks).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic);
        string prefix = "animated-" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "-" + label;
        capture.Invoke(null, new object[] { prefix });
        var camera = Camera.main;
        var position = camera.transform.position;
        var rotation = camera.transform.rotation;
        float fov = camera.fieldOfView;
        try
        {
            camera.transform.position = motor.transform.position + motor.transform.forward * 6f + motor.transform.right * 3f + Vector3.up;
            camera.transform.LookAt(motor.transform.position);
            camera.fieldOfView = 38f;
            capture.Invoke(null, new object[] { prefix + "-front" });
        }
        finally { camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov; }
    }
}
