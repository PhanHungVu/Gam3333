using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using Object = UnityEngine.Object;

// Real input, real player camera/brain and positive frame times. Resetting the
// Cinemachine state with deltaTime=-1 would hide a lock-transition snap.
public static class MechLockModeChecks
{
    const string Pending = "MechLockModeChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static Keyboard keyboard;
    static KeyboardState keyState;
    static bool running, keysPending;
    static int checks, frame = -1;
    static double deadline;
    static Camera view;
    static CameraControlThirdPerson control;
    static MechTargetLockController targeting;
    static GameObject player;

    static T Get<T>(object obj, string field) => (T)obj.GetType().GetField(field, Private).GetValue(obj);
    static void Set(object obj, string field, object value) => obj.GetType().GetField(field, Private).SetValue(obj, value);
    static void Call(object obj, string method, params object[] args) => obj.GetType().GetMethod(method, Private).Invoke(obj, args);
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); checks++; Debug.Log("LOCK MODE PASS: " + message); }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Update;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Application.dataPath.Contains(".quest-review")) throw new Exception("Disposable project only.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Update()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running)
            {
                running = true; deadline = EditorApplication.timeSinceStartup + 150;
                PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run());
            }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Lock-mode check timeout.");
            while (steps.Count > 0)
            {
                var step = steps.Peek();
                if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator child) { steps.Push(child); continue; }
                return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending, false); InputSystem.onBeforeUpdate -= Feed;
        Time.timeScale = 1; Time.captureFramerate = 0; PreMissionReliabilityChecks.EndLogAudit();
        if (error != null) Debug.LogException(error);
        Debug.Log("LOCK MODE CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    static void Feed()
    {
        if (keysPending && InputState.currentUpdateType == InputUpdateType.Dynamic)
        { InputSystem.QueueStateEvent(keyboard, keyState); keysPending = false; }
    }
    static IEnumerator Frames(int count)
    { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Keys(params Key[] keys)
    { keyState = new KeyboardState(keys); keysPending = true; yield return Frames(2); }
    static MechTargetable Enemy(string label, Vector3 offset)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = label; cube.transform.position = player.transform.position + offset;
        cube.transform.localScale = new Vector3(2, 3, 2);
        var health = cube.AddComponent<CombatHealth>(); health.Team = CombatTeam.Enemy; health.SetMaximum(1000, true);
        return cube.AddComponent<MechTargetable>();
    }
    static float Error(MechTargetable enemy) => Vector3.Angle(view.transform.forward, enemy.AimPosition - view.transform.position);
    static IEnumerator Transition(string label, MechTargetable enemy, Quaternion before, float initialError)
    {
        yield return Frames(1);
        float firstStep = Quaternion.Angle(before, view.transform.rotation), firstError = Error(enemy);
        Check(initialError > 12 && firstError > 2 && firstStep < initialError * .7f,
            label + " interpolates its first rendered frame instead of snapping; initial=" + initialError + ", first step=" + firstStep + ", remaining=" + firstError);
        Capture(label + "-first");
        float maximumStep = firstStep;
        Quaternion previous = view.transform.rotation;
        for (int i = 0; i < 90; i++)
        {
            yield return Frames(1); maximumStep = Mathf.Max(maximumStep, Quaternion.Angle(previous, view.transform.rotation)); previous = view.transform.rotation;
        }
        Check(Error(enemy) < .3f && maximumStep < initialError * .75f,
            label + " converges on the selected enemy with no later instant recenter; remaining=" + Error(enemy) + ", max step=" + maximumStep);
        Capture(label + "-settled");
    }
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.captureFramerate = 60; AudioListener.volume = 0;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        keyboard = InputSystem.AddDevice<Keyboard>(); InputSystem.onBeforeUpdate += Feed;
        player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.PlayerPath), new Vector3(0, 2, 0), Quaternion.identity);
        var input = player.GetComponent<MechPlayerInput>(); input.enabled = false;
        player.GetComponent<MechMovementController>().enabled = false;
        player.GetComponent<CombatFeedback>().enabled = false;
        targeting = player.GetComponent<MechTargetLockController>();
        view = player.GetComponent<MechPlayerRig>().GameplayCamera;
        control = view.GetComponent<CameraControlThirdPerson>();
        Set(control, "avoidObstacles", false); Set(control, "movementFeedback", false);
        Check(targeting.Mode == MechTargetLockController.LockMode.FreeCameraLockOn,
            "Existing players retain free-camera auto-aim as their starting mode");
        var front = Enemy("Front target", new Vector3(0, 1, 18));
        var side = Enemy("Side target", new Vector3(-9, 1, 20));
        Physics.SyncTransforms(); targeting.SetLockMode(MechTargetLockController.LockMode.NoLockOn);
        input.AssumeCursorCaptured = true; input.enabled = true; yield return Keys(); yield return Frames(60);
        Check(targeting.CurrentTarget == null && targeting.IsFreeAim && targeting.IsFreeCamera,
            "No-lock mode never acquires the visible enemy and leaves the camera manual");
        targeting.enabled = false; targeting.enabled = true; yield return Frames(3);
        Check(targeting.Mode == MechTargetLockController.LockMode.NoLockOn && targeting.CurrentTarget == null,
            "Re-enabling the controller preserves the player's explicit no-lock choice");
        var lockView = view.transform.rotation;
        yield return Keys(Key.F); yield return Keys(); yield return Frames(5);
        Check(targeting.Mode == MechTargetLockController.LockMode.FreeCameraLockOn && targeting.CurrentTarget == front,
            "Real F cycles no-lock to free-camera auto-aim");
        Check(Quaternion.Angle(lockView, view.transform.rotation) < .1f,
            "Acquiring a soft target does not steer the camera");
        yield return Keys(Key.F); yield return Frames(78);
        Check(targeting.Mode == MechTargetLockController.LockMode.CameraLockOn && targeting.IsHardLocked,
            "Real F enters camera lock; holding F for over a second does not cycle again");
        yield return Keys();
        Check(targeting.IsHardLockMode, "Releasing F does not advance a mode");
        yield return Keys(Key.F); yield return Keys();
        Check(targeting.Mode == MechTargetLockController.LockMode.NoLockOn && targeting.CurrentTarget == null,
            "The next F clears camera lock and weapon auto-aim together");
        yield return Frames(60);
        Call(control, "UpdateCameraOrbit", Vector2.right * 20, 1f / 60);
        Check(!targeting.IsHardLockMode, "No-lock retains manual look");
        control.BindTarget(player.transform); yield return Frames(60);
        yield return Keys(Key.F); yield return Keys();
        Check(targeting.Mode == MechTargetLockController.LockMode.FreeCameraLockOn,
            "F completes the three-mode cycle without restoring remembered hard mode");

        // Select an off-center but visible enemy immediately before changing mode.
        front.transform.position = player.transform.position + new Vector3(10, 1, 18);
        Physics.SyncTransforms(); Call(targeting, "SetTarget", front);
        Quaternion before = view.transform.rotation; float initialError = Error(front);
        targeting.SetLockMode(MechTargetLockController.LockMode.CameraLockOn);
        yield return Transition("enter-camera-lock", front, before, initialError);
        var rig = Get<CinemachineCamera>(control, "cinemachineCamera");
        var composer = rig.GetComponent<CinemachineRotationComposer>();
        Check(composer.Damping.x > .1f && composer.Damping.y > .1f && !composer.Composition.HardLimits.Enabled,
            "The live shared Cinemachine rig uses nonzero lock damping without instant hard-bound correction");

        before = view.transform.rotation; initialError = Error(side);
        targeting.ProcessSwitchInput(Vector2.left * 120, 1f / 60);
        Check(targeting.CurrentTarget == side, "The existing directional flick selects the next visible enemy");
        yield return Transition("flick-switch", side, before, initialError);

        before = view.transform.rotation; initialError = Error(front);
        side.GetComponent<CombatHealth>().ApplyDamage(2000);
        // Do not wait for targeting's next Update: the camera must recover even
        // if the enemy died later in this frame's combat update.
        Call(control, "LateUpdate");
        Check(targeting.IsHardLocked && targeting.CurrentTarget == front,
            "Camera framing itself reacquires after death without an intervening free-camera frame");
        yield return Transition("death-retarget", front, before, initialError);

        before = view.transform.rotation; front.GetComponent<CombatHealth>().ApplyDamage(2000);
        Call(control, "LateUpdate"); yield return Frames(1);
        Check(targeting.IsHardLockMode && targeting.CurrentTarget == null && Quaternion.Angle(before, view.transform.rotation) < 12,
            "Losing the last enemy preserves the selected mode and eases back instead of snapping");
        yield return Frames(60);
        var next = Enemy("New arrival", new Vector3(-9, 1, 18)); Physics.SyncTransforms();
        before = view.transform.rotation; initialError = Error(next); targeting.RefreshHardLockTarget();
        Check(targeting.IsHardLocked && targeting.CurrentTarget == next,
            "Camera-lock mode can acquire a new arrival after the board was empty");
        yield return Transition("new-arrival", next, before, initialError);

        before = view.transform.rotation; var mode = targeting.Mode; Time.timeScale = 0;
        yield return Keys(Key.F); yield return Keys();
        Check(targeting.Mode == mode && Quaternion.Angle(before, view.transform.rotation) < .1f,
            "Paused gameplay neither cycles modes nor advances camera interpolation");
        Time.timeScale = 1;
    }
    static void Capture(string label)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
        var texture = new RenderTexture(960, 540, 24);
        var previousTarget = view.targetTexture; view.targetTexture = texture; view.Render();
        var previous = RenderTexture.active; RenderTexture.active = texture;
        var pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
        pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); pixels.Apply();
        File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "../lock-mode-" + label + ".png")), pixels.EncodeToPNG());
        RenderTexture.active = previous; view.targetTexture = previousTarget; texture.Release(); Object.Destroy(pixels); Object.Destroy(texture);
    }
}
