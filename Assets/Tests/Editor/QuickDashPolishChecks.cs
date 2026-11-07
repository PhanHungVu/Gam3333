using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class QuickDashPolishChecks
{
    const string Pending = "QuickDashPolishChecks.Pending";
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static readonly List<Measurement> measurements = new List<Measurement>();
    static int frame = -1, checks;
    static bool running;
    static double deadline;
    [Serializable] public class Measurement
    {
        public string variant;
        public int fps;
        public bool held;
        public float initialSpeed, peakSpeed, dashDistance, earlyShare, totalDistance, cooldown;
    }
    static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, Private).GetValue(owner);
    static void Set(object owner, string name, object value) => owner.GetType().GetField(name, Private).SetValue(owner, value);
    static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, Private).Invoke(owner, args);
    static void Check(bool valid, string message)
    { if (!valid) throw new Exception(message); checks++; Debug.Log("DASH POLISH PASS: " + message); }
    static IEnumerator Frames(int count = 4) { int end = Time.frameCount + count; while (Time.frameCount < end) yield return null; }
    static IEnumerator Delay(float seconds) { float end = Time.unscaledTime + seconds; while (Time.unscaledTime < end) yield return null; }
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        var protectedFiles = new[] { QuickDashPolishSetup.BoardPath, "Assets/PreMission/StoneBoard/Impact Dust.mat", "Assets/PreMission/StoneBoard/Dust Puff.asset",
            "Assets/InputSystem_Actions.inputactions", "Assets/Scripts/Movement/MechEnergyController.cs", "Assets/Scripts/Movement/TankLocomotionModule.cs", "Assets/Scripts/Movement/HoverLocomotionModule.cs" };
        var before = protectedFiles.Select(Hash).ToArray(); QuickDashPolishSetup.Apply();
        Check(protectedFiles.Select(Hash).SequenceEqual(before), "Setup preserves mission board VFX, controls, retired energy and existing leg modules");
        var dust = AssetDatabase.LoadAssetAtPath<GameObject>(QuickDashPolishSetup.DustPath).GetComponent<ParticleSystem>();
        var boardDust = AssetDatabase.LoadAssetAtPath<GameObject>(QuickDashPolishSetup.BoardPath).GetComponentInChildren<QuestBoardImpactFeedback>(true).SmallDust;
        Check(dust.main.simulationSpace == ParticleSystemSimulationSpace.World && !dust.main.useUnscaledTime && !dust.main.loop
            && !dust.emission.enabled && dust.gameObject.layer == 0 && dust.GetComponent<ParticleSystemRenderer>().sharedMaterial == boardDust.GetComponent<ParticleSystemRenderer>().sharedMaterial,
            "Separate gameplay dust reuses board material with world simulation, scaled time and manual emission");
        SessionState.SetBool(Pending + ".Scene", false);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    public static void RunSceneBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        QuickDashPolishSetup.Apply();
        EditorSceneManager.OpenScene("Assets/Scenes/A33.unity");
        SessionState.SetBool(Pending + ".Scene", true); SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static string Hash(string path) { using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))); }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running) { running = true; deadline = EditorApplication.timeSinceStartup + 240; PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(SessionState.GetBool(Pending + ".Scene", false) ? RunScene() : Run()); }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Dash polish checks timed out.");
            while (steps.Count > 0)
            {
                var step = steps.Peek(); if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; } return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit(); Time.timeScale = 1; Time.captureFramerate = 0;
        if (error != null) Debug.LogException(error);
        string folder = Directory.GetParent(Application.dataPath).FullName;
        if (measurements.Count > 0) File.WriteAllLines(Path.Combine(folder, "QuickDashMeasurements.jsonl"), measurements.Select(m => JsonUtility.ToJson(m)));
        Debug.Log("DASH POLISH CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks); EditorApplication.Exit(error == null ? 0 : 1);
    }
    static void Reset(GameObject player, Vector3 position)
    {
        var motor = player.GetComponent<MechMovementController>(); var cc = player.GetComponent<CharacterController>();
        motor.enabled = false; cc.enabled = false; player.transform.SetPositionAndRotation(position, Quaternion.identity);
        cc.enabled = true; motor.enabled = true; Physics.SyncTransforms(); cc.Move(Vector3.down * 30);
        // Establish the motor's downward ground adhesion after teleporting its capsule.
        for (int i = 0; i < 3; i++) motor.Tick(default(MechLocomotionInput), .02f);
    }
    static IEnumerator Clear()
    {
        foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects()) Object.Destroy(root);
        yield return Frames();
    }
    static Measurement Measure(GameObject player, Transform heading, string variant, int fps, bool held)
    {
        Reset(player, new Vector3(0, 4, 0)); var motor = player.GetComponent<MechMovementController>();
        float time = 0, distanceAtEnd = 0, halfDistance = 0, peak = 0, initial = 0;
        int starts = 0, ends = 0; Action<Vector3> onStart = _ => starts++; Action onEnd = () => ends++;
        motor.QuickBoostStarted += onStart; motor.QuickBoostEnded += onEnd;
        Vector3 origin = player.transform.position; float energy = player.GetComponent<MechEnergyController>().Current;
        while (time < .65f - .000001f)
        {
            float dt = Mathf.Min(1f / fps, .65f - time);
            if (time < .125f && time + dt > .125f) dt = .125f - time;
            motor.Tick(new MechLocomotionInput { quickBoost = time == 0, move = held ? Vector2.up : Vector2.zero }, heading, dt);
            float speed = Vector3.ProjectOnPlane(motor.Velocity, Vector3.up).magnitude;
            if (time == 0) initial = speed; peak = Mathf.Max(peak, speed); time += dt;
            if (Mathf.Abs(time - .125f) < .00001f) halfDistance = player.transform.position.z - origin.z;
            if (ends > 0 && distanceAtEnd == 0) distanceAtEnd = player.transform.position.z - origin.z - (held ? motor.WalkSpeed * Mathf.Max(0, time - .25f) : 0);
        }
        motor.QuickBoostStarted -= onStart; motor.QuickBoostEnded -= onEnd;
        var sample = new Measurement { variant = variant, fps = fps, held = held, initialSpeed = initial, peakSpeed = peak,
            dashDistance = distanceAtEnd, earlyShare = halfDistance / distanceAtEnd, totalDistance = player.transform.position.z - origin.z,
            cooldown = motor.QuickBoostCooldownRemaining };
        measurements.Add(sample); Debug.Log("DASH MEASURE " + JsonUtility.ToJson(sample));
        Check(starts == 1 && ends == 1 && !motor.IsQuickBoosting, variant + " " + fps + " FPS dash starts and ends once");
        Check(initial >= 47 && peak <= 50.1f && peak > 49, "Immediate acceleration reaches the configured burst without a walking ramp");
        Check(distanceAtEnd > 7.9f && distanceAtEnd < 8.65f && sample.earlyShare > .68f,
            "Travel stays near the old distance with most displacement in the first half");
        Check(motor.CanQuickBoost && player.GetComponent<MechEnergyController>().Current == energy,
            "The 0.65-second cycle preserves retired energy behavior");
        Check(held ? Mathf.Abs(motor.AnimationData.Speed - motor.WalkSpeed) < .05f : motor.AnimationData.Speed < .01f,
            held ? "Held movement resumes without a pause or residual dash speed" : "Released movement ends without a residual slide");
        return sample;
    }
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.timeScale = 1;
        foreach (var variant in new[] { "golem", "heavy" })
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = new Vector3(0, -.25f, 0); floor.transform.localScale = new Vector3(500, .5f, 500);
            var heading = new GameObject("Controlled heading").transform;
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Mech/Base/FullPlayer_mech_" + variant + ".prefab"), new Vector3(0, 4, 0), Quaternion.identity);
            var motor = player.GetComponent<MechMovementController>(); var feedback = player.GetComponent<QuickDashFeedback>();
            var combatFeedback = player.GetComponent<CombatFeedback>();
            player.GetComponent<MechPlayerInput>().enabled = false; player.GetComponent<MechBoostPresentation>().enabled = false;
            feedback.enabled = false; combatFeedback.enabled = false;
            yield return Frames();
            foreach (bool held in new[] { false, true })
            {
                var batch = new[] { 30, 60, 120 }.Select(fps => Measure(player, heading, variant, fps, held)).ToArray();
                Check(batch.Max(m => m.totalDistance) - batch.Min(m => m.totalDistance) < .025f,
                    variant + " displacement agrees within 2.5cm across 30/60/120 FPS");
            }
            Reset(player, new Vector3(0, 4, 0));
            motor.Tick(new MechLocomotionInput { move = Vector2.up, quickBoost = true }, heading, .02f);
            var direction = Get<Vector3>(motor, "quickBoostDirection");
            for (int i = 0; i < 7; i++) motor.Tick(new MechLocomotionInput { move = Vector2.right }, heading, .02f);
            Check(Mathf.Abs(player.transform.position.x) < .001f && Get<Vector3>(motor, "quickBoostDirection") == direction,
                "The burst stays committed through its first 80 percent despite sideways input");
            for (int i = 0; i < 6; i++) motor.Tick(new MechLocomotionInput { move = Vector2.right }, heading, .02f);
            Check(player.transform.position.x > 0 && motor.AnimationData.Speed <= motor.WalkSpeed + .1f,
                "The short exit blend returns sideways walking control after the committed burst");
            Reset(player, new Vector3(0, 4, 0));
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0, 3, 5); wall.transform.localScale = new Vector3(20, 8, .2f);
            Physics.SyncTransforms();
            for (int i = 0; i < 10; i++) motor.Tick(new MechLocomotionInput { quickBoost = i == 0, move = Vector2.up }, heading, 1f / 30);
            Check(player.GetComponent<CharacterController>().bounds.max.z <= wall.GetComponent<Collider>().bounds.min.z + .15f,
                "The faster burst still stops at a thin wall through CharacterController sweeps");
            Object.DestroyImmediate(wall);
            Reset(player, new Vector3(0, 4, 0));
            var slope = GameObject.CreatePrimitive(PrimitiveType.Cube); slope.transform.position = Vector3.up * .25f;
            slope.transform.localScale = new Vector3(30, .5f, 30); slope.transform.rotation = Quaternion.Euler(-12, 0, 0);
            Reset(player, new Vector3(0, 4, 0));
            motor.Tick(new MechLocomotionInput { quickBoost = true, move = Vector2.up }, heading, .02f);
            Check(motor.QuickBoostHasGroundContact && motor.QuickBoostGroundNormal.y > .95f && motor.QuickBoostGroundNormal.z < -.1f,
                "Push-off contact records the real walkable slope normal");
            for (int i = 0; i < 14; i++) motor.Tick(default(MechLocomotionInput), heading, .02f);
            Check(motor.IsGrounded && player.transform.position.z > 7 && float.IsFinite(motor.Velocity.y), "An uphill dash retains valid collision and grounded recovery");
            Object.DestroyImmediate(slope);
            Reset(player, new Vector3(0, 4, 0));
            var loot = GameObject.CreatePrimitive(PrimitiveType.Cube); loot.SetActive(false); loot.name = "Dropped dash obstacle";
            loot.transform.position = new Vector3(0, 1, 3); loot.transform.localScale = new Vector3(2, 2, 2);
            var loosePart = loot.AddComponent<Part>(); loosePart.isStartingPart = false;
            loosePart.data = AssetDatabase.LoadAssetAtPath<PartData>("Assets/PartCatalogue/PartData_Head.asset");
            loot.SetActive(true); loot.GetComponent<Rigidbody>().constraints = RigidbodyConstraints.FreezeAll; Physics.SyncTransforms();
            for (int i = 0; i < 15; i++) motor.Tick(new MechLocomotionInput { quickBoost = i == 0 }, heading, .02f);
            Check(player.transform.position.z > 8 && motor.QuickBoostGroundPoint.y < .05f,
                "Dropped parts neither block a dash nor replace the push-off floor contact");
            Object.DestroyImmediate(loot);
            combatFeedback.enabled = true; feedback.enabled = true; yield return Frames();
            Reset(player, new Vector3(0, 4, 0));
            int dust = feedback.DustBurstCount, startImpulse = feedback.StartImpulseCount, endImpulse = feedback.EndImpulseCount;
            Vector3 launch = player.transform.position;
            motor.Tick(new MechLocomotionInput { quickBoost = true, move = Vector2.up }, heading, .02f);
            Check(feedback.DustBurstCount == dust + 1 && feedback.StartImpulseCount == startImpulse + 1,
                "Accepted grounded dash produces one dust burst and one shared Cinemachine start impulse");
            Check(Vector3.Distance(feedback.LastDustPosition, motor.QuickBoostGroundPoint) < .04f && Mathf.Abs(feedback.LastDustPosition.z - launch.z) < .01f
                && player.transform.position.z > launch.z + .8f, "Dust uses the original push-off contact rather than the already-displaced feet");
            var emitter = feedback.DustSystems.First(s => s.particleCount > 0);
            var particles = new ParticleSystem.Particle[128]; int count = emitter.GetParticles(particles);
            Vector3 average = Vector3.zero; for (int i = 0; i < count; i++) average += particles[i].velocity; average /= count;
            Check(Vector3.Dot(average, Vector3.back) > 1 && feedback.PoolSize == 12, "The fixed pool throws world-space dust backward");
            for (int i = 0; i < 14; i++) { motor.Tick(new MechLocomotionInput { quickBoost = true }, heading, .02f); Call(feedback, "LateUpdate"); }
            Call(feedback, "LateUpdate");
            Check(feedback.DustBurstCount == dust + 6 && feedback.EndImpulseCount == endImpulse + 1,
                "One accepted dash leaves six separate puffs; rejected repeats add no sequence and completion adds one weaker stop impulse");
            if (variant == "golem") yield return Capture(player, feedback, launch);
            int pool = feedback.PoolSize; motor.enabled = false; Call(feedback, "LateUpdate");
            Check(feedback.PoolSize == pool && feedback.EndImpulseCount == endImpulse + 1, "Motor cancellation does not create an extra stop impulse");
            CheckBreadcrumbs(player, heading);
            Reset(player, new Vector3(0, 4, 0));
            motor.Tick(new MechLocomotionInput { quickBoost = true }, heading, .02f);
            var cc = player.GetComponent<CharacterController>(); cc.Move(Vector3.up * .1f); Set(motor, "verticalVelocity", -8f); Set(motor, "quickBoostTimer", .01f);
            int beforeLanding = feedback.EndImpulseCount;
            motor.Tick(default(MechLocomotionInput), heading, .02f); Call(feedback, "LateUpdate");
            Check(motor.IsGrounded && feedback.EndImpulseCount == beforeLanding, "A true landing on the end frame suppresses the redundant dash-stop impulse");
            Reset(player, new Vector3(0, 4, 0)); var debug = player.GetComponent<MechLocomotionDebug>();
            Check(debug.TrySwap(2), "Hover module remains equippable");
            motor.enabled = false; cc.enabled = false; player.transform.position = new Vector3(0, 8, 0); cc.enabled = true; motor.enabled = true; cc.Move(Vector3.zero);
            dust = feedback.DustBurstCount; startImpulse = feedback.StartImpulseCount; endImpulse = feedback.EndImpulseCount;
            motor.Tick(new MechLocomotionInput { quickBoost = true }, heading, .02f);
            Check(motor.IsQuickBoosting && feedback.DustBurstCount == dust && feedback.StartImpulseCount == startImpulse + 1,
                "Hover retains an airborne dash with camera feedback and no floating ground dust");
            for (int i = 0; i < 14; i++) motor.Tick(default(MechLocomotionInput), heading, .02f); Call(feedback, "LateUpdate");
            Check(feedback.EndImpulseCount == endImpulse, "An airborne dash ending is not treated as a landing");
            Check(debug.TrySwap(0), "Biped module returns through the existing equipment path");
            motor.enabled = false; cc.enabled = false; player.transform.position = new Vector3(0, 8, 0); cc.enabled = true; motor.enabled = true; cc.Move(Vector3.zero);
            dust = feedback.DustBurstCount; motor.Tick(new MechLocomotionInput { quickBoost = true }, heading, .02f);
            Check(motor.IsQuickBoosting && feedback.DustBurstCount == dust, "Biped accepts an airborne dash without floating ground dust");
            foreach (int fps in new[] { 30, 60, 120 })
            foreach (int order in new[] { 0, 1, 2 })
            {
                Reset(player, new Vector3(0, 4, 0)); float dt = 1f / fps;
                if (order == 1) motor.Tick(new MechLocomotionInput { jump = true, move = Vector2.up }, heading, dt);
                if (order == 2) motor.Tick(new MechLocomotionInput { quickBoost = true, move = Vector2.up }, heading, dt);
                float height = player.transform.position.y;
                motor.Tick(new MechLocomotionInput { jump = order != 1, quickBoost = order != 2, move = Vector2.up }, heading, dt);
                Check(motor.IsQuickBoosting && motor.AnimationData.VerticalSpeed > 0 && player.transform.position.y > height,
                    variant + " combines jump/dash at " + fps + " FPS in order " + order + " (0=simultaneous, 1=jump-first, 2=dash-first)");
                float cooldown = motor.QuickBoostCooldownRemaining;
                motor.Tick(new MechLocomotionInput { jump = true, quickBoost = true, move = Vector2.up }, heading, dt);
                Check(motor.QuickBoostCooldownRemaining < cooldown && motor.AnimationData.VerticalSpeed < Mathf.Sqrt(75) + .01f,
                    "Airborne repeat cannot restart dash cooldown or add another jump");
            }
            Check(debug.TrySwap(3), "Tank module remains equippable"); Reset(player, new Vector3(0, 4, 0));
            Vector3 forward = player.transform.forward; motor.Tick(new MechLocomotionInput { quickBoost = true, move = Vector2.left }, heading, .02f);
            Check(Vector3.Dot(Get<Vector3>(motor, "quickBoostDirection"), forward) > .99f, "Tank preserves its forward-only dash rule");
            Check(debug.TrySwap(0), "Biped restores after all leg-type tests"); Reset(player, new Vector3(0, 4, 0));
            var camera = player.GetComponent<MechPlayerRig>().GameplayCamera.GetComponent<CameraControlThirdPerson>();
            var rig = Get<CinemachineCamera>(camera, "cinemachineCamera"); var follow = rig.GetComponent<CinemachineThirdPersonFollow>();
            motor.Tick(new MechLocomotionInput { quickBoost = true }, heading, .02f);
            for (int i = 0; i < 5; i++) Call(camera, "UpdateMovementFeedback", .01f); Call(camera, "ApplySettings");
            Check(Mathf.Abs(rig.Lens.FieldOfView - 65) < .1f && follow.Damping.sqrMagnitude < .00001f,
                "Existing camera reaches its five-degree FOV kick in 0.05s with immediate follow");
            Set(camera, "meleeFraming", .7f); Call(camera, "UpdateMovementFeedback", .3f); Call(camera, "ApplySettings");
            Check(Mathf.Abs(rig.Lens.FieldOfView - 60) < .01f, "Dash FOV fully recovers even during melee framing"); Set(camera, "meleeFraming", 0f);
            for (int i = 0; i < 14; i++) motor.Tick(default(MechLocomotionInput), heading, .02f);
            Call(camera, "UpdateMovementFeedback", .15f); Call(camera, "ApplySettings");
            Check(Vector3.Distance(follow.Damping, new Vector3(.045f, .09f, .13f)) < .001f,
                "Follow damping smoothly returns to the existing normal profile");
            Reset(player, new Vector3(0, 4, 0));
            for (int i = 0; i < 20; i++) { motor.Tick(new MechLocomotionInput { assaultBoost = true, move = Vector2.up }, heading, .05f); Call(camera, "UpdateMovementFeedback", .05f); }
            Call(camera, "ApplySettings");
            Debug.Log($"DASH ASSAULT MEASURE {variant}: active={motor.IsAssaultBoosting}, speed={motor.AnimationData.Speed}, FOV={rig.Lens.FieldOfView}, distance={follow.CameraDistance}");
            Check(motor.IsAssaultBoosting && Mathf.Abs(motor.AnimationData.Speed - 28) < .1f && Mathf.Abs(rig.Lens.FieldOfView - 70) < .05f
                && Mathf.Abs(follow.CameraDistance - 14.56f) < .03f, "Retained assault boost keeps 28-unit speed, +10 FOV and 1.12x framing");
            Reset(player, new Vector3(0, 4, 0)); camera.enabled = false; camera.enabled = true;
            var health = player.GetComponent<CombatHealth>(); health.SetMaximum(1000, true);
            motor.Tick(new MechLocomotionInput { quickBoost = true }, heading, .02f); health.ApplyDamage(5);
            Check(health.CurrentHealth == 995 && motor.IsQuickBoosting, "Damage still applies during a dash; no invulnerability or new cancellation is added");
            yield return Delay(.15f);
            Check(!CombatHitStop.IsFrozen(player.transform), "Existing damage hitstop resumes naturally");
            if (variant == "heavy")
            {
                var shot = player.GetComponent<MechCombat>().TryAttack(PartCategory.ShoulderL).Projectile;
                Check(shot != null && motor.IsQuickBoosting, "Existing shoulder firing remains available during a dash"); if (shot != null) Object.Destroy(shot.gameObject);
            }
            camera.enabled = false;
            Check(Mathf.Abs(rig.Lens.FieldOfView - 60) < .01f, "Disabling the camera clears its dash FOV without a lasting lens change");
            yield return Clear();
            Check(Object.FindObjectsByType<QuickDashFeedback>().Length == 0
                && !SceneManager.GetActiveScene().GetRootGameObjects().Any(o => o.name == "Quick Dash Dust Pool"), "Actor/scene cleanup leaves no orphan dash dust pools");
        }
        MechLocomotionChecks.Run(Check); MechCameraCollisionChecks.Run(Check);
    }
    static IEnumerator RunScene()
    {
        Time.captureFramerate = 60; Application.runInBackground = true;
        yield return Frames(12);
        var motor = Object.FindAnyObjectByType<MechMovementController>(); var player = motor.gameObject;
        var input = player.GetComponent<MechPlayerInput>(); var feedback = player.GetComponent<QuickDashFeedback>();
        Check(feedback != null && feedback.isActiveAndEnabled && feedback.PoolSize == 12, "A33 combat test scene inherits dash feedback from the actual heavy player prefab");
        foreach (var movingTarget in Object.FindObjectsByType<AutoMoveTarget>()) movingTarget.enabled = false;
        input.enabled = false; Reset(player, new Vector3(57, 4, -25));
        var camera = player.GetComponent<MechPlayerRig>().GameplayCamera.GetComponent<CameraControlThirdPerson>();
        Set(camera, "cameraYaw", 0f); var rig = Get<CinemachineCamera>(camera, "cinemachineCamera"); rig.PreviousStateIsValid = false;
        var enemy = Object.FindObjectsByType<MechTargetable>().First(); enemy.transform.position = player.transform.position + Vector3.forward * 16;
        Physics.SyncTransforms(); yield return Frames(6); // Let the live camera settle after teleporting to the runway.
        var targeting = player.GetComponent<MechTargetLockController>(); targeting.ResumeTargeting(); targeting.ToggleHardLock();
        Check(targeting.IsHardLocked, "The real scene enters hard lock before the input-driven dash");
        var oldBackground = InputSystem.settings.backgroundBehavior;
        var oldEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        KeyboardState desiredKeys = default; bool keysPending = false;
        void Feed()
        {
            if (InputState.currentUpdateType != InputUpdateType.Dynamic || !keysPending) return;
            InputSystem.QueueStateEvent(keyboard, desiredKeys); keysPending = false;
        }
        InputSystem.onBeforeUpdate += Feed;
        try
        {
            input.enabled = true; input.AssumeCursorCaptured = true; yield return Frames(2);
            int starts = 0; motor.QuickBoostStarted += _ => starts++;
            int dust = feedback.DustBurstCount; Vector3 origin = player.transform.position;
            desiredKeys = new KeyboardState(Key.W, Key.LeftShift); keysPending = true;
            int limit = Time.frameCount + 5; while (starts == 0 && Time.frameCount < limit) yield return Frames(1);
            Check(starts == 1 && motor.IsQuickBoosting && Vector3.Distance(origin, player.transform.position) > .7f && feedback.DustBurstCount == dust + 1,
                $"Physical Shift input starts movement, dust and camera feedback in its accepted frame (starts={starts}, state={motor.State}, ground={motor.IsGrounded}, distance={Vector3.Distance(origin, player.transform.position)}, dust={feedback.DustBurstCount - dust})");
            yield return Frames(2); CaptureScene(camera.GetComponent<Camera>(), "start");
            Check(rig.Lens.FieldOfView > 63 && rig.Lens.FieldOfView <= 65.1f && targeting.IsHardLocked,
                "The live camera shows the short FOV pulse while retaining hard lock");
            yield return Frames(5); CaptureScene(camera.GetComponent<Camera>(), "middle");
            Check(feedback.DustBurstCount >= dust + 4 && feedback.DustBurstCount <= dust + 6,
                "Live movement leaves several separate ground puffs as the player advances");
            yield return Frames(45);
            Check(starts == 1 && !motor.IsQuickBoosting && Mathf.Abs(rig.Lens.FieldOfView - 60) < .1f,
                "Holding Shift does not repeat the dash and the live lens returns to normal");
            Check(motor.IsGrounded && !float.IsNaN(camera.transform.position.x), "Existing runway geometry and live camera remain stable after the dash");
            desiredKeys = new KeyboardState(); keysPending = true; yield return Frames(2);
            desiredKeys = new KeyboardState(Key.LeftShift); keysPending = true; yield return Frames(2);
            Check(starts == 2 && feedback.DustBurstCount >= dust + 7 && feedback.DustBurstCount <= dust + 8 && feedback.PoolSize == 12,
                "A released and re-pressed Shift reuses the fixed dust pool after cooldown");
            CaptureScene(camera.GetComponent<Camera>(), "repeat");
            desiredKeys = new KeyboardState(); keysPending = true; yield return Frames(35);
            CaptureScene(camera.GetComponent<Camera>(), "recovered");
        }
        finally
        {
            InputSystem.onBeforeUpdate -= Feed; InputSystem.RemoveDevice(keyboard);
            InputSystem.settings.backgroundBehavior = oldBackground; InputSystem.settings.editorInputBehaviorInPlayMode = oldEditorInput;
        }
    }
    static void CheckBreadcrumbs(GameObject player, Transform heading)
    {
        var motor = player.GetComponent<MechMovementController>(); var feedback = player.GetComponent<QuickDashFeedback>();
        foreach (int fps in new[] { 30, 60, 120 })
        {
            feedback.enabled = false; Reset(player, new Vector3(0, 4, 0)); feedback.enabled = true;
            int before = feedback.DustBurstCount; float elapsed = 0;
            while (elapsed < .25f - .000001f)
            {
                float dt = Mathf.Min(1f / fps, .25f - elapsed);
                motor.Tick(new MechLocomotionInput { quickBoost = elapsed == 0 }, heading, dt);
                Call(feedback, "LateUpdate"); elapsed += dt;
            }
            var points = feedback.DustSystems.Where(s => s.particleCount > 0).Select(s => s.transform.position).OrderBy(p => p.z).ToArray();
            Check(feedback.DustBurstCount - before == 6 && points.Length == 6
                && Enumerable.Range(1, points.Length - 1).All(i => Mathf.Abs(points[i].z - points[i - 1].z - 1.6f) < .01f),
                fps + " FPS leaves six independent puffs spaced 1.6 units along resolved movement");
            Check(Vector3.ProjectOnPlane(points.Last() - player.transform.position, Vector3.up).magnitude < .5f
                && points.All(p => Mathf.Abs(p.y - .025f) < .04f)
                && feedback.DustSystems.All(s => s.GetComponent<TrailRenderer>() == null && !s.emission.enabled),
                "Puffs reach the end of the dash, sit on the floor and use no continuous emission or trail renderer");
        }
        feedback.enabled = false; Reset(player, new Vector3(0, 4, 0)); feedback.enabled = true;
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.transform.position = new Vector3(0, 3, 2.5f); wall.transform.localScale = new Vector3(20, 8, .2f);
        Physics.SyncTransforms(); int dust = feedback.DustBurstCount;
        for (int i = 0; i < 16; i++) { motor.Tick(new MechLocomotionInput { quickBoost = i == 0 }, heading, .02f); Call(feedback, "LateUpdate"); }
        int stopped = feedback.DustBurstCount;
        for (int i = 0; i < 10; i++) { motor.Tick(default(MechLocomotionInput), heading, .02f); Call(feedback, "LateUpdate"); }
        Check(stopped - dust <= 2 && feedback.DustBurstCount == stopped && feedback.DustSystems.Where(s => s.particleCount > 0).All(s => s.transform.position.z < 2.4f),
            "Blocking geometry limits actual dust travel; a stopped dash does not stack puffs or emit through the wall");
        Object.DestroyImmediate(wall); feedback.enabled = false; feedback.enabled = true;
    }
    static void CaptureScene(Camera camera, string label)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        var target = new RenderTexture(1280, 720, 24);
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var prior = RenderTexture.active; RenderTexture.active = target;
        var picture = new Texture2D(1280, 720, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); picture.Apply();
        File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, "QuickDashScene-" + label + ".png"), picture.EncodeToPNG());
        RenderTexture.active = prior; target.Release(); Object.DestroyImmediate(picture); Object.DestroyImmediate(target);
    }
    static IEnumerator Capture(GameObject player, QuickDashFeedback feedback, Vector3 launch)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) yield break;
        foreach (var system in feedback.DustSystems) if (system.IsAlive(true)) system.Simulate(.12f, true, false, false);
        var light = new GameObject("Dash review light").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(45, -25, 0);
        var review = new GameObject("Dash dust review camera").AddComponent<Camera>(); review.transform.position = launch + new Vector3(7, 5, -9);
        review.transform.LookAt(launch + Vector3.forward * 1.5f + Vector3.up); review.clearFlags = CameraClearFlags.SolidColor; review.backgroundColor = new Color(.06f, .075f, .085f);
        var target = new RenderTexture(960, 540, 24);
        RenderPipeline.SubmitRenderRequest(review, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        var prior = RenderTexture.active; RenderTexture.active = target;
        var picture = new Texture2D(960, 540, TextureFormat.RGB24, false); picture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); picture.Apply();
        string path = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "QuickDashDust.png"); File.WriteAllBytes(path, picture.EncodeToPNG());
        RenderTexture.active = prior; review.targetTexture = null; target.Release(); Object.DestroyImmediate(picture); Object.DestroyImmediate(target);
        Object.DestroyImmediate(review.gameObject); Object.DestroyImmediate(light.gameObject); yield break;
    }
}
