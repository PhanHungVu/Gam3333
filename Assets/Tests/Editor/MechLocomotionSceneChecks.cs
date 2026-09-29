using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

// Exercises the real scene Update/input path, separately from deterministic motor ticks.
public static class MechLocomotionSceneChecks
{
    const string PendingKey = "MechLocomotionSceneChecks.Pending";
    static Keyboard keyboard;
    static MechMovementController motor;
    static Part torso;
    static Vector3 start;
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
        SessionState.SetBool(PendingKey, true);
        EditorApplication.EnterPlaymode();
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Debug.Log("LOCOMOTION SCENE PASS: " + message);
    }

    static void Update()
    {
        if (!SessionState.GetBool(PendingKey, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (next < 0f) { next = Time.time + 1f; deadline = Time.time + 25f; }
        if (Time.time < next) return;
        try
        {
            if (Time.time > deadline) throw new Exception("Locomotion scene check timeout.");
            if (phase == 0)
            {
                Time.captureDeltaTime = 1f / 60f;
                // A hidden batch editor has no focused Game view. Route only this
                // disposable test session's synthetic input into the player loop.
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                var motors = Object.FindObjectsByType<MechMovementController>();
                Check(motors.Length == 1, "Actual scene has one movement controller");
                motor = motors[0];
                Check(motor.GetComponent<MechEquipmentLocomotion>() != null && motor.GetComponent<MechLocomotionDebug>() != null,
                    "Scene inherits locomotion bridge and configured debug controls");
                torso = motor.GetComponent<EquipManager>().GetEquipped(PartCategory.Torso);
                start = motor.transform.position;
                keyboard = InputSystem.AddDevice<Keyboard>();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.Digit3));
            }
            else if (phase <= 3)
            {
                var expected = phase == 1 ? LocomotionType.Hover : phase == 2 ? LocomotionType.Tank : LocomotionType.Biped;
                Check(motor.LocomotionType == expected && motor.AnimationData.LocomotionType == expected,
                    "Real keyboard shortcut selects " + expected + " and publishes its animation identity (actual: " + motor.LocomotionType + ")");
                Check(motor.MoveDirection.sqrMagnitude > 0.9f && motor.GetComponent<EquipManager>().GetEquipped(PartCategory.Torso) == torso,
                    "Movement input and torso survive the " + expected + " swap");
                Check((motor.transform.position - start).sqrMagnitude < 2500f,
                    "Scene swap retains continuous player position");
                var legs = motor.GetComponent<EquipManager>().GetEquipped(PartCategory.Legs);
                var bounds = new Bounds(legs.transform.position, Vector3.zero);
                foreach (var renderer in legs.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
                Debug.Log("LOCOMOTION VISUAL " + expected + " root=" + motor.transform.position
                    + " legs=" + bounds + " capsule=" + motor.GetComponent<CharacterController>().bounds);
                typeof(CombatFeedbackSceneChecks).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { "locomotion-" + expected.ToString().ToLowerInvariant() });
                InputSystem.QueueStateEvent(keyboard, phase == 1 ? new KeyboardState(Key.W, Key.Digit4)
                    : phase == 2 ? new KeyboardState(Key.W, Key.Digit1) : new KeyboardState());
            }
            else
            {
                Check(Object.FindObjectsByType<MechMovementController>().Length == 1
                    && Camera.main.GetComponent<CameraControlThirdPerson>().Target == motor.transform,
                    "Repeated swaps leave one motor and the existing camera tracking the same player");
                InputSystem.RemoveDevice(keyboard);
                SessionState.SetBool(PendingKey, false);
                Debug.Log("LOCOMOTION SCENE CHECKS COMPLETE");
                EditorApplication.Exit(0);
                return;
            }
            phase++;
            next = Time.time + 0.8f;
        }
        catch (Exception e)
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            SessionState.SetBool(PendingKey, false);
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
}
