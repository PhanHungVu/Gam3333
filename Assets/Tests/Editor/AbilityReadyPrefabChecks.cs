using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class AbilityReadyPrefabChecks
{
    const string Pending = "AbilityReadyPrefabChecks.Pending";
    static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
    static int frame = -1, checks;
    static bool running;
    static double deadline;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Tick;
    static void Check(bool valid, string message)
    { if (!valid) throw new Exception(message); checks++; Debug.Log("READY PREFAB PASS: " + message); }
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable project only.");
        AbilityReadyPrefabSetup.Apply();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AbilityReadyPrefabSetup.BurstPath);
        var effect = prefab.GetComponent<AbilityReadyVfx>();
        Check(effect != null && effect.Graphic != null && !effect.Graphic.enabled
            && !effect.Graphic.raycastTarget && !effect.Graphic.maskable,
            "Standalone burst prefab authors its controller and hidden, non-interactive column");
        var pod = AssetDatabase.LoadAssetAtPath<GameObject>(AbilityReadyPrefabSetup.PodPath);
        var nested = pod.GetComponentInChildren<AbilityReadyVfx>(true);
        Check(pod.GetComponent<AbilityReadyVfx>() == null && pod.GetComponentsInChildren<AbilityReadyVfx>(true).Length == 1
            && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(nested.gameObject) == AbilityReadyPrefabSetup.BurstPath,
            "Ability HUD replaces the old root component with exactly one linked burst prefab");
        var podFields = new SerializedObject(pod.GetComponent<AbilitiesHudPod>());
        var effectFields = new SerializedObject(nested);
        Check(podFields.FindProperty("readyVfx").objectReferenceValue == nested
            && effectFields.FindProperty("icon").objectReferenceValue == podFields.FindProperty("icon").objectReferenceValue,
            "Authored HUD references bind the nested burst to its ability icon");
        var hud = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Combat HUD/CombatHUD.prefab");
        Check(new SerializedObject(hud.GetComponentInChildren<MechAbilitiesHud>()).FindProperty("podPrefab").objectReferenceValue
            == pod.GetComponent<AbilitiesHudPod>(), "Shared CombatHUD uses the upgraded ability pod");
        // Reapplying the setup preserves the authored asset and creates no duplicates.
        AbilityReadyPrefabSetup.Apply();
        Check(AssetDatabase.LoadAssetAtPath<GameObject>(AbilityReadyPrefabSetup.PodPath).GetComponentsInChildren<AbilityReadyVfx>(true).Length == 1,
            "Reapplying prefab setup does not add a second effect");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Tick()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling || frame == Time.frameCount) return;
        frame = Time.frameCount;
        try
        {
            if (!running)
            {
                running = true; deadline = EditorApplication.timeSinceStartup + 120;
                PreMissionReliabilityChecks.BeginLogAudit(); steps.Push(Run());
            }
            PreMissionReliabilityChecks.AssertNoUnexpectedErrors();
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Ready prefab checks timed out.");
            while (steps.Count > 0)
            {
                var step = steps.Peek();
                if (!step.MoveNext()) { steps.Pop(); continue; }
                if (step.Current is IEnumerator nested) { steps.Push(nested); continue; }
                return;
            }
            Finish(null);
        }
        catch (Exception error) { Finish(error); }
    }
    static IEnumerator Run()
    {
        Application.runInBackground = true; Time.timeScale = 1;
        yield return AbilityReadyVfxChecks.Run(Check);
        yield return WeaponReadyVfxChecks.Run(Check);
    }
    static void Finish(Exception error)
    {
        SessionState.SetBool(Pending, false); PreMissionReliabilityChecks.EndLogAudit();
        if (error != null) Debug.LogException(error);
        Debug.Log("READY PREFAB CHECKS " + (error == null ? "COMPLETE: " : "FAILED: ") + checks);
        EditorApplication.Exit(error == null ? 0 : 1);
    }
}
