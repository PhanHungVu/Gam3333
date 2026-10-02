using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

public static class MechAnimationV2SceneChecks
{
    const string Pending = "MechAnimationV2SceneChecks.Pending";
    static Keyboard keyboard;
    static MechMovementController motor;
    static float next = -1f, deadline;
    static int phase = -1;
    [InitializeOnLoadMethod]
    static void Register() => EditorApplication.update += Update;

    public static void RunBatch() => Start("Assets/Scenes/A33.unity");
    public static void RunMergeBatch() => Start("Assets/Scenes/Merge Test.unity");
    static void Start(string scene)
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated batch copy only.");
        EditorSceneManager.OpenScene(scene);
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); Debug.Log("V2 SCENE PASS: " + message); }

    static void Update()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (next < 0f) { next = Time.time + 1f; deadline = Time.time + 25f; }
        if (Time.time < next) return;
        try
        {
            if (Time.time > deadline) throw new Exception("V2 scene timed out at " + phase);
            float delay = 0.7f;
            if (phase == -1)
            {
                Time.captureDeltaTime = 1f / 60f;
                InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                motor = Object.FindAnyObjectByType<MechMovementController>();
                keyboard = InputSystem.AddDevice<Keyboard>();
                // Keep the real scene player/input/camera, but give its action sequence
                // clear ground so nearby enemies and course walls cannot cancel boosts.
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Temporary animation-check floor";
                floor.transform.position = new Vector3(1000f, -0.5f, 1000f);
                floor.transform.localScale = new Vector3(500f, 1f, 500f);
                var capsule = motor.GetComponent<CharacterController>();
                capsule.enabled = false;
                motor.transform.position = new Vector3(1000f, 1.6f, 1000f);
                capsule.enabled = true;
                Physics.SyncTransforms();
                delay = 1f;
            }
            else if (phase == 0)
            {
                AssertPose(MechLegAnimator.PoseState.Neutral, "Biped_Neutral");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            }
            else if (phase == 1)
            {
                AssertPose(MechLegAnimator.PoseState.Locomotion, "Biped_Run");
                Capture("walk");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
            }
            else if (phase == 2)
            {
                AssertPose(MechLegAnimator.PoseState.Locomotion, "Biped_Run");
                Check(Driver().Animator.GetFloat("PlaybackSpeed") > 1.9f, "Real Shift input accelerates the walking clip");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftAlt));
                delay = 0.12f;
            }
            else if (phase == 3)
            {
                AssertPose(MechLegAnimator.PoseState.Boost, "Boost");
                Capture("dash");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                delay = 0.32f;
            }
            else if (phase == 4)
            {
                AssertPose(MechLegAnimator.PoseState.BoostStop, "BoostStop");
                Capture("dash-stop");
                delay = 1.15f;
            }
            else if (phase == 5)
            {
                AssertPose(MechLegAnimator.PoseState.Neutral, "Biped_Neutral");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
                delay = 0.3f;
            }
            else if (phase == 6)
            {
                AssertPose(MechLegAnimator.PoseState.Boost, "Boost");
                Capture("boost");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                delay = 0.18f;
            }
            else if (phase == 7)
            {
                AssertPose(MechLegAnimator.PoseState.BoostStop, "BoostStop");
                Capture("boost-stop");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                delay = 0.16f;
            }
            else if (phase == 8)
            {
                AssertPose(MechLegAnimator.PoseState.Jump, "Biped_Jump");
                Check(!motor.IsGrounded, "Real Space input jumps and interrupts boost-stop");
                Capture("fast-jump");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                delay = 1.15f;
            }
            else if (phase == 9)
            {
                AssertPose(MechLegAnimator.PoseState.Neutral, "Biped_Neutral");
                Check(motor.IsGrounded, "Landing restores Neutral");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Digit2));
            }
            else if (phase == 10)
            {
                AssertPose(MechLegAnimator.PoseState.Neutral, "Heavy_Neutral");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            }
            else if (phase == 11)
            {
                AssertPose(MechLegAnimator.PoseState.Locomotion, "Heavy_Walk.001");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
            }
            else if (phase == 12)
            {
                AssertPose(MechLegAnimator.PoseState.Locomotion, "Heavy_Walk.001");
                Check(Driver().Animator.GetFloat("PlaybackSpeed") > 1.9f, "Heavy run retains its saved gait with faster cadence");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftCtrl));
                delay = 0.3f;
            }
            else if (phase == 13)
            {
                AssertPose(MechLegAnimator.PoseState.Boost, "Heavy_Boost");
                Capture("heavy-boost");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                delay = 0.18f;
            }
            else if (phase == 14)
            {
                AssertPose(MechLegAnimator.PoseState.BoostStop, "Heavy_BoostStop");
                Capture("heavy-boost-stop");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                delay = 0.16f;
            }
            else if (phase == 15)
            {
                AssertPose(MechLegAnimator.PoseState.Jump, "Heavy_Jump");
                Capture("heavy-jump");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                delay = 1.15f;
            }
            else if (phase == 16)
            {
                AssertPose(MechLegAnimator.PoseState.Neutral, "Heavy_Neutral");
                Check(motor.IsGrounded, "Heavy landing restores its own Neutral");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Digit1));
            }
            else
            {
                AssertPose(MechLegAnimator.PoseState.Neutral, "Biped_Neutral");
                InputSystem.RemoveDevice(keyboard);
                SessionState.SetBool(Pending, false);
                Debug.Log("V2 SCENE CHECKS COMPLETE");
                EditorApplication.Exit(0);
                return;
            }
            phase++; next = Time.time + delay;
        }
        catch (Exception e)
        {
            if (keyboard != null) InputSystem.RemoveDevice(keyboard);
            SessionState.SetBool(Pending, false); Debug.LogException(e); EditorApplication.Exit(1);
        }
    }

    static MechLegAnimator Driver() => motor.GetComponent<EquipManager>().GetEquipped(PartCategory.Legs).GetComponent<MechLegAnimator>();
    static void AssertPose(MechLegAnimator.PoseState expected, string clip)
    {
        var driver = Driver();
        Check(driver.CurrentPose == expected, "Real input selects " + expected);
        for (int layer = 0; layer < 3; layer++)
        {
            var clips = driver.Animator.GetCurrentAnimatorClipInfo(layer);
            Check(clips.Where(c => c.clip.name == clip).Sum(c => c.weight) > 0.95f,
                "Layer " + layer + " plays " + clip + " through the player loop (actual="
                + string.Join(",", clips.Select(c => c.clip.name + ":" + c.weight)) + "; transitioning=" + driver.Animator.IsInTransition(layer) + ")");
        }
    }

    static void Capture(string label)
    {
        var camera = Camera.main;
        var position = camera.transform.position; var rotation = camera.transform.rotation; float fov = camera.fieldOfView;
        try
        {
            camera.transform.position = motor.transform.position + motor.transform.forward * 6f + motor.transform.right * 3f + Vector3.up;
            camera.transform.LookAt(motor.transform.position); camera.fieldOfView = 38f;
            typeof(CombatFeedbackSceneChecks).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { "v2-" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "-" + label });
        }
        finally { camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov; }
    }
}
