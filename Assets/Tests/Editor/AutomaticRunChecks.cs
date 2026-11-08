using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Real player prefabs, equipment, combat/damage events and CharacterController movement.
public static class AutomaticRunChecks
{
    const string Pending = "AutomaticRunChecks.Pending";
    static int checks;
    [InitializeOnLoadMethod] static void Register()
    {
        EditorApplication.playModeStateChanged += state =>
        { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) Run(); };
    }
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Run()
    {
        SessionState.SetBool(Pending, false);
        try
        {
            Time.timeScale = 1;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.transform.position = Vector3.down * .25f; floor.transform.localScale = new Vector3(5000, .5f, 5000);
            var view = new GameObject("Automatic run view"); view.tag = "MainCamera"; view.AddComponent<Camera>();
            Physics.SyncTransforms();
            VerifyPlayer("Assets/Prefabs/Mech/Specialist/FullPlayer_mech_specialist.prefab", new Vector3(-500, 10, 0), view.transform);
            VerifyPlayer("Assets/Prefabs/Mech/Base/FullPlayer_mech_heavy.prefab", new Vector3(500, 10, 0), view.transform);
            Object.Destroy(floor); Object.Destroy(view);
            Debug.Log("AUTOMATIC RUN CHECKS COMPLETE: " + checks); EditorApplication.Exit(0);
        }
        catch (Exception error) { Time.timeScale = 1; Debug.LogException(error); EditorApplication.Exit(1); }
    }
    static void VerifyPlayer(string path, Vector3 position, Transform view)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var authored = new SerializedObject(prefab.GetComponent<MechMovementController>());
        Check(authored.FindProperty("autoRunDelay").floatValue == 3 && authored.FindProperty("autoRunSpeedMultiplier").floatValue == 2,
            prefab.name + " authors a three-second delay and double walking speed");
        var player = Object.Instantiate(prefab, position, Quaternion.identity);
        try
        {
            TestMech.StopPlayerInput(player.GetComponent<MechMovementController>());
            var motor = player.GetComponent<MechMovementController>();
            var combat = player.GetComponent<MechCombat>();
            var health = player.GetComponent<CombatHealth>();
            var controller = player.GetComponent<CharacterController>();
            var feedback = player.GetComponent<CombatFeedback>(); if (feedback != null) feedback.enabled = false;
            Physics.SyncTransforms();
            float feet = controller.bounds.min.y;
            controller.enabled = false;
            player.transform.position -= Vector3.up * (feet - .02f);
            controller.enabled = true; Physics.SyncTransforms();
            Step(motor, view, 50, Vector2.zero);
            Check(motor.IsGrounded, prefab.name + " settles on the test floor");
            Step(motor, view, 140, Vector2.up);
            Check(!motor.IsAutoRunning && Planar(motor) <= motor.WalkSpeed + .05f, "Before three uninterrupted seconds the player walks");
            Step(motor, view, 60, Vector2.up);
            Check(motor.IsAutoRunning && Mathf.Abs(Planar(motor) - motor.WalkSpeed * 2) < .05f,
                "After three seconds actual movement reaches exactly twice walking speed");
            Step(motor, view, 1, Vector2.zero);
            Check(!motor.IsAutoRunning && motor.AutoRunProgress == 0 && Planar(motor) <= motor.WalkSpeed + .05f,
                "Stopping immediately cancels running and resets the three-second timer");
            Step(motor, view, 300, Vector2.up); TestMech.Advance(player, 1);
            Check(combat.TryAttack(PartCategory.ArmL).Started, "The equipped left arm starts a real attack");
            Check(!motor.IsAutoRunning && motor.AutoRunProgress == 0, "Attacking immediately cancels running");
            Step(motor, view, 140, Vector2.up);
            Check(!motor.IsAutoRunning && Planar(motor) <= motor.WalkSpeed + .05f, "An attack requires a new uninterrupted three-second walk");
            Step(motor, view, 60, Vector2.up);
            Check(motor.IsAutoRunning && health.ApplyDamage(1), "The running player takes actual damage");
            Check(!motor.IsAutoRunning && motor.AutoRunProgress == 0, "Taking damage immediately cancels running");
            Step(motor, view, 1, Vector2.up);
            Check(Planar(motor) <= motor.WalkSpeed + .05f, "Movement returns to walking speed after damage");
            for (int i = 0; i < 4; i++)
            {
                Step(motor, view, 100, Vector2.up); TestMech.Advance(player, 1);
                Check(combat.TryAttack(PartCategory.ArmL).Started && !motor.IsAutoRunning,
                    "Repeated real attacks keep resetting the run timer");
            }
            for (int i = 0; i < 4; i++)
            {
                Step(motor, view, 100, Vector2.up);
                Check(health.ApplyDamage(1) && !motor.IsAutoRunning && motor.AutoRunProgress == 0,
                    "Repeated incoming damage keeps resetting the run timer");
            }
            Step(motor, view, 100, Vector2.up);
            float progress = motor.AutoRunProgress; Time.timeScale = 0;
            motor.Tick(new MechLocomotionInput { move = Vector2.up }, view, 0);
            Check(motor.AutoRunProgress == progress, "Paused gameplay does not advance the run timer"); Time.timeScale = 1;

            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = controller.bounds.center + Vector3.forward * 2;
            wall.transform.localScale = new Vector3(20, 20, .5f); Physics.SyncTransforms();
            Step(motor, view, 350, Vector2.up);
            Check(!motor.IsAutoRunning && motor.AutoRunProgress == 0, "Pushing into a wall cannot build automatic run");
            wall.SetActive(false); Object.Destroy(wall);
            Step(motor, view, 300, Vector2.up);
            Check(motor.IsAutoRunning, "Clear-path walking can restart running after an obstruction");
            motor.enabled = false; motor.enabled = true;
            Check(!motor.IsAutoRunning && motor.AutoRunProgress == 0, "Disabling the motor clears running");
            var equipment = player.GetComponent<EquipManager>();
            foreach (var name in new[] { "Hover", "Tank", "Biped" })
            {
                var part = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(TestMech.LocomotionFolder + "/" + name + "Module.prefab")).GetComponent<Part>();
                Check(equipment.TryEquip(part, PartCategory.Legs), name + " legs equip through the real equipment system");
                Step(motor, view, 50, Vector2.zero);
                Check(Mathf.Abs(motor.RunSpeed - 2 * motor.WalkSpeed) < .001f, name + " running target remains twice the part-modified walking speed");
                Step(motor, view, 330, Vector2.up);
                Check(motor.IsAutoRunning && Mathf.Abs(Planar(motor) - 2 * motor.WalkSpeed) < .08f,
                    name + " actual travel reaches twice walking speed after three seconds");
            }
        }
        finally { Object.Destroy(player); }
    }
    static float Planar(MechMovementController motor) => Vector3.ProjectOnPlane(motor.Velocity, Vector3.up).magnitude;
    static void Step(MechMovementController motor, Transform view, int frames, Vector2 move)
    { for (int i = 0; i < frames; i++) motor.Tick(new MechLocomotionInput { move = move }, view, .02f); }
    static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; Debug.Log("AUTOMATIC RUN PASS: " + message); }
}
