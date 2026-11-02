using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechEnemyCombatSceneChecks
{
    const string Pending = "MechEnemyCombatSceneChecks.Pending";
    static int phase;
    static float next = -1f, deadline, before;
    static MechMovementController player;
    static CombatHealth health;
    static EnemyAttack enemy;
    static Vector3 start;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Update;
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene("Assets/Scenes/Merge Test.unity");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); Debug.Log("ENEMY SCENE PASS: " + message); }
    static void Update()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (next < 0f) { next = Time.time + 1f; deadline = Time.time + 30f; }
        if (Time.time < next) return;
        try
        {
            if (Time.time > deadline) throw new Exception("Enemy scene timeout at " + phase);
            float delay = 0.1f;
            switch (phase)
            {
                case 0:
                    Time.captureDeltaTime = 1f / 60f;
                    foreach (var director in Object.FindObjectsByType<SpawnDirector>()) director.enabled = false;
                    foreach (var agent in Object.FindObjectsByType<EnemyAgent>()) agent.gameObject.SetActive(false);
                    player = Object.FindAnyObjectByType<MechMovementController>(); TestMech.StopPlayerInput(player);
                    TestMech.StopPlayerInput(player.GetComponent<MechCombat>());
                    health = player.GetComponent<CombatHealth>();
                    Check(health != null && health.CurrentHealth == player.GetComponent<MechStats>().MaxHealth,
                        "Merge Test player starts with equipped health capacity");
                    Spawn("mace", 7f); delay = 0.45f; break;
                case 1:
                    Check(enemy.GetComponent<EnemyAgent>().Velocity.magnitude > 0.1f && enemy.transform.position != start,
                        "Existing AI chases the player with its original movement");
                    Check(enemy.GetComponent<EnemyAnimation>().Animator.GetFloat("MoveBlend") > 0.9f,
                        "Moving enemy drives the shared walk animation");
                    Capture("enemy-mace-walk"); break;
                case 2:
                    if (!enemy.IsAttacking || enemy.Weapon.NormalizedTime < 0.2f) { next = Time.time + 0.05f; return; }
                    Check(enemy.Weapon.CurrentPose == MechWeaponAnimator.PoseState.Fire,
                        "AI automatically starts Mace Fire at attack range");
                    Capture("enemy-mace-fire"); break;
                case 3:
                    if (health.CurrentHealth >= before) { next = Time.time + 0.05f; return; }
                    Check(Mathf.Abs(health.CurrentHealth - (before - enemy.DamageOf(enemy.Weapon))) < 0.002f, "Live enemy swing damages player at its release frame");
                    enemy.GetComponent<CombatHealth>().ApplyDamage(1000f);
                    Spawn("crossbow", 8f); break;
                case 4:
                    if (!enemy.IsAttacking || enemy.Weapon.NormalizedTime < 0.2f) { next = Time.time + 0.05f; return; }
                    Check(enemy.Weapon.CurrentPose == MechWeaponAnimator.PoseState.Fire,
                        "AI automatically starts CrossBowArm Fire at ranged distance");
                    Capture("enemy-crossbow-fire"); break;
                case 5:
                    if (health.CurrentHealth >= before) { next = Time.time + 0.05f; return; }
                    Check(Mathf.Abs(health.CurrentHealth - (before - enemy.DamageOf(enemy.Weapon))) < 0.002f, "Live enemy bolt travels to and damages the player");
                    break;
                case 6:
                    // A fast bolt can connect while Fire is still playing; Reload follows Fire.
                    if (enemy.Weapon.CurrentPose == MechWeaponAnimator.PoseState.Fire) { next = Time.time + 0.05f; return; }
                    Check(enemy.Weapon.CurrentPose == MechWeaponAnimator.PoseState.Reload,
                        "Enemy crossbow plays its own Reload after release");
                    Capture("enemy-crossbow-reload");
                    enemy.GetComponent<CombatHealth>().ApplyDamage(1000f); delay = 0.8f; break;
                default:
                    Check(player.gameObject.activeSelf && health.CurrentHealth > 0f
                        && Object.FindObjectsByType<MechMovementController>().Length == 1,
                        "Enemy damage retains the living player and existing controls/rig");
                    SessionState.SetBool(Pending, false); Debug.Log("ENEMY SCENE CHECKS COMPLETE"); EditorApplication.Exit(0); return;
            }
            phase++; next = Time.time + delay;
        }
        catch (Exception e) { SessionState.SetBool(Pending, false); Debug.LogException(e); EditorApplication.Exit(1); }
    }
    static void Spawn(string family, float distance)
    {
        var position = player.transform.position - Vector3.forward * distance;
        var root = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Small Enemies/Enemy_" + family + ".prefab"), position, Quaternion.identity);
        enemy = root.GetComponent<EnemyAttack>(); start = root.transform.position; before = health.CurrentHealth;
        // Isolate release timing and collision from the crossbow's intentional aim spread.
        var attackFields = new SerializedObject(enemy); attackFields.FindProperty("aimSpreadDegrees").floatValue = 0;
        attackFields.ApplyModifiedPropertiesWithoutUndo();
    }
    static void Capture(string name)
    {
        var camera = Camera.main; var position = camera.transform.position; var rotation = camera.transform.rotation; float fov = camera.fieldOfView;
        try
        {
            // View from behind the attacker so the player cannot obscure its swing.
            camera.transform.position = enemy.transform.position + new Vector3(-3.5f, 2f, -4f);
            camera.transform.LookAt(enemy.transform.position + Vector3.up * 0.1f); camera.fieldOfView = 42f;
            typeof(CombatFeedbackSceneChecks).GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { name });
        }
        finally { camera.transform.SetPositionAndRotation(position, rotation); camera.fieldOfView = fov; }
    }
}
