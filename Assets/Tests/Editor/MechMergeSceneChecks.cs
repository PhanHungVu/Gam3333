using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

// Verify the teammate's actual scene alongside the restored player, without
// replacing enemies or navigation with test doubles. (The scene's old enemy spawners were
// retired with EnemySpawnerManager; the enemy checks need a Spawn Director in Merge Test.)
public static class MechMergeSceneChecks
{
    const string Pending = "MechMergeSceneChecks.Pending";
    static float next = -1f;
    static int phase;
    static MechMovementController motor;
    static Dictionary<EnemyAgent, Vector3> positions;

    [InitializeOnLoadMethod]
    static void Register() => EditorApplication.update += Update;

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene("Assets/Scenes/Merge Test.unity");
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        Debug.Log("MERGE SCENE PASS: " + message);
    }

    static void Update()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (next < 0f) next = Time.time + 1.2f;
        if (Time.time < next) return;
        try
        {
            if (phase == 0)
            {
                Time.captureDeltaTime = 1f / 60f;
                var motors = Object.FindObjectsByType<MechMovementController>();
                Check(motors.Length == 1, "Merged scene retains exactly one player motor");
                motor = motors[0];
                Check(motor.CompareTag("Player"), "Player tag remains available to enemy target resolution");
                Check(Camera.main.GetComponent<CameraControlThirdPerson>().Target == motor.transform,
                    "Existing scene camera follows the recovered player");
                var equipment = motor.GetComponent<EquipManager>();
                Check(equipment.GetEquipped(PartCategory.Legs).GetComponent<MechLegAnimator>() != null,
                    "Merged scene inherits animated Fast legs");
                Check(equipment.GetEquipped(PartCategory.ArmL).name == "arm_sword"
                    && equipment.GetEquipped(PartCategory.ArmR).name == "arm_throwingaxe",
                    "Teammate's new starting arm prefabs and left/right layout survive");
                Check(equipment.GetEquipped(PartCategory.Torso).GetComponent<MechPartPoseReceiver>() != null
                    && equipment.GetEquipped(PartCategory.Head).GetComponent<MechPartPoseReceiver>() != null,
                    "Body and head pose receivers are present alongside teammate weapons");
                var agents = Object.FindObjectsByType<EnemyAgent>();
                Check(agents.Any(a => a.Pathing == PathingMode.FlowField) && agents.Any(a => a.Pathing == PathingMode.Zigzag),
                    "Both teammate pathing variants spawn");
                Check(Object.FindAnyObjectByType<NavGrid>() != null && Object.FindAnyObjectByType<FlowField>() != null,
                    "Teammate navigation grid and flow field remain in the scene");
                foreach (var agent in agents)
                {
                    Check(agent.HasTarget && Vector3.Distance(agent.TargetPosition, motor.transform.position) < 0.01f,
                        "Spawned enemy resolves the recovered player as its target");
                    Check(agent.GetComponent<CombatHealth>() != null && agent.GetComponent<MechTargetable>() != null,
                        "Spawned enemy retains combat health and player targeting");
                }
                var input = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/InputSystem_Actions.inputactions");
                Check(Bound(input, "Cannon", "<Keyboard>/q") && Bound(input, "Crossbow", "<Keyboard>/e")
                    && Bound(input, "Melee", "<Mouse>/leftButton") && Bound(input, "Attack", "<Mouse>/rightButton"),
                    "Teammate's Q/E and mouse weapon controls remain intact");
                positions = agents.ToDictionary(a => a, a => a.transform.position);
                phase++; next = Time.time + 1.5f;
            }
            else
            {
                foreach (var pair in positions)
                    Check(pair.Key != null && Vector3.Distance(pair.Key.transform.position, pair.Value) > 0.05f,
                        "Spawned mech enemy moves under teammate AI");
                var combat = motor.GetComponent<MechCombat>();
                foreach (var slot in new[] { PartCategory.ShoulderL, PartCategory.ShoulderR, PartCategory.ArmR })
                {
                    int ammo = combat.AmmoRemaining(slot);
                    var projectile = combat.TryAttack(slot).Projectile;
                    Check(projectile != null && combat.AmmoRemaining(slot) == ammo - 1,
                        "Recovered animated sockets retain functioning " + slot);
                    if (projectile != null) Object.DestroyImmediate(projectile.gameObject);
                }
                Check(Object.FindObjectsByType<MechMovementController>().Length == 1,
                    "Enemy spawning does not create extra player controllers");
                MechMerge2SelectionChecks.Run(motor);
                SessionState.SetBool(Pending, false);
                Debug.Log("MERGE SCENE CHECKS COMPLETE");
                EditorApplication.Exit(0);
            }
        }
        catch (Exception e)
        {
            SessionState.SetBool(Pending, false);
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static bool Bound(InputActionAsset asset, string action, string path) =>
        asset.FindAction("Player/" + action).bindings.Any(b => b.effectivePath == path);
}
