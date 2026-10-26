using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Exercise the merged menu, objective completion and scene HUDs in a disposable project.
public static class MechSceneFlowChecks
{
    const string Pending = "MechSceneFlowChecks.Pending";
    static int phase;
    static double next;
    static Keyboard keyboard;
    static TempLevelObjective objective;
    static Transform player, item, returnPoint;
    static GameObject winScreen;
    [InitializeOnLoadMethod] static void Register() => EditorApplication.update += Update;
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Disposable project only.");
        EditorSceneManager.OpenScene("Assets/Scenes/JhMapScene.unity");
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Check(bool ok, string message)
    {
        if (!ok) throw new Exception(message);
        Debug.Log("SCENE FLOW PASS: " + message);
    }
    static void CheckMeters()
    {
        var health = Object.FindAnyObjectByType<MechHealthHud>();
        var energy = Object.FindAnyObjectByType<MechEnergyHud>();
        Check(health != null && energy != null && health.transform.parent == energy.transform.parent
            && health.Canvas == energy.GetComponentInParent<Canvas>() && health.IsVisible
            && health.transform.childCount == 3 && energy.transform.childCount == 3,
            SceneManager.GetActiveScene().name + " has both three-layer meters on the same Canvas");
        Check(health.Health == Object.FindAnyObjectByType<MechPlayerRig>().GetComponent<CombatHealth>(),
            "Health meter binds the current scene player");
    }
    static void QuietEnemies()
    {
        foreach (var director in Object.FindObjectsByType<SpawnDirector>()) director.enabled = false;
        foreach (var enemy in Object.FindObjectsByType<EnemyAgent>()) enemy.gameObject.SetActive(false);
    }
    static void PressF(MonoBehaviour consumer)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.F)); InputSystem.Update();
        Check(keyboard.fKey.wasPressedThisFrame, "F reaches the real Input System");
        // Consume the edge in the same input frame, including while the level is paused.
        consumer.GetType().GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(consumer, null);
    }
    static void ClickScene(string name)
    {
        var button = Object.FindObjectsByType<Button>().Single(b =>
        {
            var calls = new SerializedObject(b).FindProperty("m_OnClick.m_PersistentCalls.m_Calls");
            for (int i = 0; i < calls.arraySize; i++)
            {
                var call = calls.GetArrayElementAtIndex(i);
                if (call.FindPropertyRelative("m_MethodName").stringValue == "LoadScene"
                    && call.FindPropertyRelative("m_Arguments.m_StringArgument").stringValue == name) return true;
            }
            return false;
        });
        button.onClick.Invoke();
    }
    static void Update()
    {
        if (!SessionState.GetBool(Pending, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        if (next == 0) next = EditorApplication.timeSinceStartup + 1;
        if (EditorApplication.timeSinceStartup < next) return;
        try
        {
            switch (phase)
            {
                case 0:
                    CheckMeters(); QuietEnemies();
                    Application.runInBackground = true;
                    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    objective = Object.FindAnyObjectByType<TempLevelObjective>();
                    Check(objective != null && objective.sceneManager != null && objective.toolTipText != null,
                        "Merged map keeps the objective, menu manager and tooltip references");
                    var data = new SerializedObject(objective);
                    item = (Transform)data.FindProperty("item").objectReferenceValue;
                    returnPoint = (Transform)data.FindProperty("returnPoint").objectReferenceValue;
                    winScreen = (GameObject)data.FindProperty("winScreen").objectReferenceValue;
                    Check(item != null && returnPoint != null, "Objective item and return marker survive the merge");
                    player = Object.FindAnyObjectByType<MechPlayerRig>().transform;
                    player.GetComponent<MechMovementController>().enabled = false;
                    player.GetComponent<CharacterController>().enabled = false;
                    keyboard = InputSystem.AddDevice<Keyboard>(); player.position = item.position; break;
                case 1:
                    Check(!item.gameObject.activeSelf && returnPoint.gameObject.activeSelf, "Collecting item reveals the return marker");
                    player.position = returnPoint.position; break;
                case 2:
                    Check((winScreen == null || winScreen.activeSelf) && Time.timeScale == 0f
                        && objective.toolTipText.text.StartsWith("Level complete!"), "Returning completes and pauses the level");
                    PressF(objective); break;
                case 3:
                    Check(SceneManager.GetActiveScene().name == "MainMenuScene" && Time.timeScale == 1f,
                        "F after winning returns to the menu and clears the pause");
                    ClickScene("ZooScene"); break;
                case 4:
                    Check(SceneManager.GetActiveScene().name == "ZooScene" && Time.timeScale == 1f, "Zoo button loads the built scene normally");
                    CheckMeters(); QuietEnemies();
                    var back = Object.FindAnyObjectByType<ZooBacktoMain>();
                    Check(back != null && back.sceneManager != null, "Zoo retains its return-to-menu interaction");
                    typeof(ZooBacktoMain).GetMethod("OnTriggerEnter", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(back, new object[] { Object.FindAnyObjectByType<MechPlayerRig>().GetComponent<CharacterController>() });
                    PressF(back); break;
                case 5:
                    Check(SceneManager.GetActiveScene().name == "MainMenuScene" && Time.timeScale == 1f, "Zoo F interaction returns to the menu");
                    ClickScene("JhMapScene"); break;
                case 6:
                    Check(SceneManager.GetActiveScene().name == "JhMapScene" && Time.timeScale == 1f,
                        "Map button starts a fresh level after completing the previous one");
                    CheckMeters();
                    SessionState.SetBool(Pending, false); InputSystem.RemoveDevice(keyboard);
                    Debug.Log("SCENE FLOW CHECKS COMPLETE"); EditorApplication.Exit(0); return;
            }
            phase++; next = EditorApplication.timeSinceStartup + 0.5;
        }
        catch (Exception e)
        {
            SessionState.SetBool(Pending, false); Debug.LogException(e); EditorApplication.Exit(1);
        }
    }
}
