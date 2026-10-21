using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public static class MechAbilitySceneChecks
{
    const string Key = "MechAbilitySceneChecks.Pending";
    static readonly string[] Names = { "ResonancePulse", "ZigzagBarrage", "BloomMissile" };
    static MechCombatController combat;
    static CombatFeedback feedback;
    static MechPartAbility ability;
    static GameObject part;
    static readonly GameObject[] enemies = new GameObject[2];
    static int pattern, phase, ammo, completed, hits;
    static float next = -1, deadline, applied;
    static void Check(bool ok, string message)
    { if (!ok) throw new Exception(message); Debug.Log("ABILITY SCENE PASS: " + message); }
    static void Capture(string label) => typeof(CombatFeedbackSceneChecks)
        .GetMethod("Capture", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { "ability-" + label });
    [InitializeOnLoadMethod]
    static void Register() => EditorApplication.update += Tick;

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated scene checks only.");
        EditorSceneManager.OpenScene("Assets/Scenes/A33.unity");
        SessionState.SetBool(Key, true);
        EditorApplication.EnterPlaymode();
    }

    public static void RunMergeBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Isolated scene checks only.");
        EditorSceneManager.OpenScene("Assets/Scenes/Merge Test.unity");
        SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (next < 0) { next = Time.time + 2; deadline = Time.time + 40; }
        if (Time.time < next) return;
        try
        {
            if (Time.time > deadline) throw new Exception("Ability scene timeout: " + pattern + "/" + phase);
            if (combat == null)
            {
                Time.captureDeltaTime = 1f / 60;
                combat = Object.FindAnyObjectByType<MechCombatController>();
                feedback = combat.GetComponent<CombatFeedback>();
                Check(combat.GetComponent<MechPlayerRig>() != null && Object.FindObjectsByType<MechCombatController>().Length == 1,
                    "Merged scene has exactly one integrated player");
                Check(Camera.main.GetComponent<CameraControlThirdPerson>().Target == combat.transform,
                    "Merged scene camera tracks the parts-based player");
                foreach (var enemy in Object.FindObjectsByType<MechTargetable>()) enemy.gameObject.SetActive(false);
                ammo = combat.AmmoRemaining(MechWeaponType.Cannon);
            }
            if (phase == 0)
            {
                applied = 0; completed = hits = 0;
                part = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Abilities/" + Names[pattern] + " Part.prefab"), combat.transform);
                ability = part.GetComponent<MechPartAbility>();
                ability.Resolved += result => { hits += result.Complete ? 0 : result.Hits; if (result.Complete) completed++; };
                Vector3 origin = pattern == 2 ? combat.MuzzlePosition : combat.transform.position;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/CubeTest_Enemy.prefab");
                for (int i = 0; i < 2; i++)
                {
                    enemies[i] = Object.Instantiate(prefab);
                    if (enemies[i].TryGetComponent<AutoMoveTarget>(out var movement)) movement.enabled = false;
                    Vector3 offset = pattern == 0 ? new Vector3(i == 0 ? -1.5f : 1.5f, 0, 5)
                        : pattern == 1 ? new Vector3(i == 0 ? -1.5f : 1.5f, 0, (i + 1) * 2.5f)
                        : new Vector3(i == 0 ? 0 : 5, 0, i == 0 ? 12 : 7);
                    enemies[i].transform.position += origin + offset - enemies[i].GetComponent<MechTargetable>().AimPosition;
                    enemies[i].GetComponent<CombatHealth>().Damaged += amount => applied += amount;
                }
                Physics.SyncTransforms();
                combat.GetComponent<MechTargetLockController>().ResumeTargeting();
                phase = 1; next = Time.time + 0.3f;
            }
            else if (phase == 1)
            {
                Check(ability.TryActivate(new MechAbilityContext(combat.transform, pattern == 2 ? combat.MuzzlePosition : combat.transform.position,
                    Vector3.forward, feedback: feedback)), Names[pattern] + " prefab activates independently in A33");
                phase = 2; next = Time.time + (pattern == 0 ? 0.45f : pattern == 1 ? 0.4f : 0.5f);
            }
            else if (phase == 2)
            {
                Check(feedback.ActiveEffects > 0, Names[pattern] + " has live pooled presentation");
                Capture(Names[pattern]);
                phase = 3;
            }
            else if (phase == 3)
            {
                if (ability.IsActive) return;
                Check(completed == 1 && hits > 0 && applied > 0 && applied <= 250.001f,
                    Names[pattern] + $" resolves visible damage within its budget and completes exactly once (hits {hits}, damage {applied}, completions {completed})");
                phase = 4; next = Time.time + 1;
            }
            else
            {
                Check(feedback.ActiveEffects == 0 && feedback.PoolSize <= 32
                    && combat.AmmoRemaining(MechWeaponType.Cannon) == ammo,
                    Names[pattern] + " releases pooled visuals and preserves weapon ammunition");
                Object.Destroy(part);
                foreach (var enemy in enemies) Object.Destroy(enemy);
                if (++pattern < Names.Length) { phase = 0; next = Time.time + 0.2f; return; }
                Debug.Log("ABILITY SCENE CHECKS COMPLETE");
                SessionState.SetBool(Key, false);
                EditorApplication.Exit(0);
            }
        }
        catch (Exception e) { Debug.LogException(e); SessionState.SetBool(Key, false); EditorApplication.Exit(1); }
    }
}
