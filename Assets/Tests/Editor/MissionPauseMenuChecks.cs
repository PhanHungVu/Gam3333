using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class MissionPauseMenuChecks
{
    const string Pending = "MissionPauseMenuChecks.Pending";
    const string MissionPath = "Assets/Tests/PauseMissionReview.unity";
    const string DestinationPath = "Assets/Tests/PauseDestinationReview.unity";
    static Keyboard keyboard;
    static Mouse mouse;
    static KeyboardState keys;
    static MouseState pointer;
    static bool keysPending, pointerPending;
    static int checks, attacks, abilities;
    static MechPlayerInput input;
    static MissionPauseMenu menu;
    static readonly List<InputDevice> disabledDevices = new List<InputDevice>();
    [InitializeOnLoadMethod] static void Register()
    {
        EditorApplication.playModeStateChanged += state =>
        { if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) Run(); };
    }
    public static void RunBatch()
    {
        if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Disposable Unity project only.");
        MissionPauseMenuSetup.Apply(); MissionPauseMenuSetup.Apply();
        var canvas = PrefabUtility.LoadPrefabContents(MissionPauseMenuSetup.CanvasPath);
        try
        {
            var menus = canvas.GetComponentsInChildren<MissionPauseMenu>(true);
            Check(menus.Length == 1 && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(menus[0].gameObject) == MissionPauseMenuSetup.MenuPath,
                "The shared canvas contains exactly one linked pause-menu prefab after repeat installation");
            var buttons = menus[0].GetComponentsInChildren<Button>(true);
            Check(buttons.Length == 3 && buttons.All(b => b.onClick.GetPersistentEventCount() == 1), "All three authored buttons have persistent click handlers");
            Check(!menus[0].transform.Find("Pause Overlay").gameObject.activeSelf, "The authored menu starts closed");
            Check(!menus[0].TryPause(), "The UI prefab cannot pause a menu/preview without a bound gameplay player");
        }
        finally { PrefabUtility.UnloadPrefabContents(canvas); }
        foreach (var path in new[] { SpecialistPlayerIntegrationSetup.PlayerPath, TestMech.PlayerPath })
        {
            var rig = AssetDatabase.LoadAssetAtPath<GameObject>(path).GetComponent<MechPlayerRig>();
            Check(new SerializedObject(rig).FindProperty("cursorPrefab").objectReferenceValue == AssetDatabase.LoadAssetAtPath<GameObject>(MissionPauseMenuSetup.CanvasPath),
                path + " uses the shared cursor canvas containing the pause menu");
        }
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene); EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), DestinationPath);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position = Vector3.down * .25f; floor.transform.localScale = new Vector3(300, .5f, 300);
        var player = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(SpecialistPlayerIntegrationSetup.PlayerPath));
        player.transform.position = Vector3.up * 2;
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), MissionPath);
        SessionState.SetBool(Pending, true); EditorApplication.EnterPlaymode();
    }
    static void Feed()
    {
        if (InputState.currentUpdateType != InputUpdateType.Dynamic) return;
        if (keysPending) { InputSystem.QueueStateEvent(keyboard, keys); keysPending = false; }
        if (pointerPending) { InputSystem.QueueStateEvent(mouse, pointer); pointerPending = false; }
    }
    static async Task Wait(float seconds)
    { double until = Time.realtimeSinceStartupAsDouble + seconds; while (Time.realtimeSinceStartupAsDouble < until) await Task.Delay(10); }
    static async Task Until(Func<bool> ready, float seconds = 20)
    { double until = Time.realtimeSinceStartupAsDouble + seconds; while (!ready()) { if (Time.realtimeSinceStartupAsDouble > until) throw new Exception("Pause review timed out"); await Task.Delay(10); } }
    static async Task Keys(params Key[] held)
    { keys = new KeyboardState(held); keysPending = true; await Until(() => !keysPending); await Wait(.06f); }
    static async Task Pointer(Vector2 position, bool down)
    { pointer = new MouseState { position = position }; if (down) pointer = pointer.WithButton(MouseButton.Left); pointerPending = true; await Until(() => !pointerPending); await Wait(.06f); }
    static async Task FindPlayer()
    {
        await Until(() => Object.FindAnyObjectByType<MechPlayerInput>() is MechPlayerInput player && player.PauseMenu != null);
        input = Object.FindAnyObjectByType<MechPlayerInput>(); input.AssumeCursorCaptured = true; menu = input.PauseMenu;
        input.GetComponent<MechCombat>().WeaponAttackStarted += _ => attacks++;
        input.AbilityRequested += (_, __) => abilities++;
        await Wait(.15f);
    }
    static Button Button(string name) => menu.GetComponentsInChildren<Button>(true).Single(b => b.name == name);
    static async void Run()
    {
        SessionState.SetBool(Pending, false);
        try
        {
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            foreach (var device in InputSystem.devices.Where(d => d.enabled && (d is Keyboard || d is Mouse || d is Gamepad)).ToArray())
            { disabledDevices.Add(device); InputSystem.DisableDevice(device); }
            keyboard = InputSystem.AddDevice<Keyboard>("Pause review keyboard"); mouse = InputSystem.AddDevice<Mouse>("Pause review mouse");
            InputSystem.onBeforeUpdate += Feed;
            await FindPlayer(); await Keys(); await Pointer(Vector2.zero, false);
            var layoutHost = new GameObject("Pause layout reuse review", typeof(RectTransform), typeof(Canvas)); layoutHost.SetActive(false);
            var reused = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(MissionPauseMenuSetup.MenuPath), layoutHost.transform);
            var reusedRect = (RectTransform)reused.transform; reusedRect.localScale = Vector3.zero; reusedRect.anchorMax = Vector2.zero;
            layoutHost.SetActive(true);
            Check(reusedRect.localScale == Vector3.one && reusedRect.anchorMax == Vector2.one && reused.GetComponent<Canvas>().overrideSorting,
                "The reusable prefab repairs standalone-canvas root layout when placed in a HUD canvas");
            Object.Destroy(layoutHost); await Wait(.04f);
            Check(!menu.IsOpen && Time.timeScale == 1 && Object.FindObjectsByType<MissionPauseMenu>().Length == 1,
                "The actual mission player creates and binds one hidden pause menu through its cursor canvas");
            var enemy = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemies/Small Enemies/Enemy_sword.prefab"), input.transform.position + Vector3.forward * 15, Quaternion.identity);
            await Wait(.15f);
            await Keys(Key.W); await Wait(.15f); await Keys(Key.W, Key.Escape);
            Check(menu.IsOpen && Time.timeScale == 0 && AudioListener.pause && Cursor.lockState == CursorLockMode.None && Cursor.visible,
                "Escape opens the menu, pauses gameplay/audio and exposes the mouse");
            Check(EventSystem.current != null && EventSystem.current.currentSelectedGameObject == Button("Resume").gameObject,
                "A missing EventSystem is supplied and Resume is selected for keyboard navigation");
            var position = input.transform.position; float clock = Time.time; int attacksBefore = attacks, abilitiesBefore = abilities;
            var enemyPosition = enemy.transform.position; int enemyReleases = enemy.GetComponent<EnemyAttack>().Releases;
            await Keys(Key.W, Key.Q, Key.E, Key.LeftShift, Key.Tab, Key.Space, Key.LeftCtrl, Key.Digit1, Key.Digit2);
            await Pointer(new Vector2(-100, -100), true); await Wait(.2f);
            Check(input.transform.position == position && Time.time == clock && attacks == attacksBefore && abilities == abilitiesBefore,
                "Movement, attacks, abilities and the gameplay clock stay frozen while menu input remains active");
            Check(!EventSystem.current.sendNavigationEvents, "Held movement/submit cannot accidentally navigate or activate an exit button");
            var zoneRoot = new GameObject("Paused hold zone", typeof(ChunkDefinition)); zoneRoot.transform.position = input.transform.position;
            var zoneNode = new GameObject("Hold"); zoneNode.transform.SetParent(zoneRoot.transform, false);
            var zone = zoneNode.AddComponent<KOTH_Zone>(); await Wait(.1f);
            Check(zone.State == HoldZoneState.Waiting, "A paused mission cannot start or advance an objective hold");
            Object.Destroy(zoneRoot);
            Check(enemy.transform.position == enemyPosition && enemy.GetComponent<EnemyAttack>().Releases == enemyReleases,
                "A live enemy graph cannot move or release an attack while the mission is paused");
            Object.Destroy(enemy);
            await Keys(); await Pointer(Vector2.zero, false);
            Check(EventSystem.current.sendNavigationEvents, "Menu keyboard navigation activates after the held controls are released");
            var button = Button("Resume"); var rect = (RectTransform)button.transform;
            Vector2 click = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = click }, hits);
            Check(hits.Count > 0 && hits[0].gameObject == button.gameObject, "Resume is the topmost real UI raycast at its screen position");
            string capture = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "mission-pause-menu.png");
            Capture(capture); await Wait(.05f);
            click = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            await Pointer(click, true); await Pointer(click, false);
            Check(!menu.IsOpen && Time.timeScale == 1 && !AudioListener.pause, "An actual mouse click on Resume restores gameplay and audio");
            Check(attacks == attacksBefore, "Clicking Resume does not fire the player's weapon");
            await Keys(Key.Escape);
            Debug.Log("PAUSE ESC SAMPLE: open=" + menu.IsOpen + "; scale=" + Time.timeScale + "; input=" + input.isActiveAndEnabled
                + "; bound=" + (input.PauseMenu == menu) + "; outcome=" + MissionOutcomeController.Find(input.gameObject.scene)?.Outcome
                + "; currentKeyboard=" + (Keyboard.current == keyboard));
            Check(menu.IsOpen, "Escape opens the menu again");
            await Keys(); await Keys(Key.Escape); Check(!menu.IsOpen && Time.timeScale == 1, "Escape also resumes gameplay"); await Keys();
            Time.timeScale = .5f; AudioListener.pause = true;
            Check(menu.TryPause(), "Pause accepts an existing positive slow-motion rate"); menu.Resume();
            Check(Time.timeScale == .5f && AudioListener.pause, "Resume restores the exact prior time rate and audio-pause state");
            Time.timeScale = 0; Check(!menu.TryPause(), "An unrelated zero-time pause is not stolen by this menu"); Time.timeScale = 1; AudioListener.pause = false;
            await Wait(.06f); Check(menu.TryPause(), "The menu can pause after an external pause ends");
            menu.gameObject.SetActive(false);
            Check(Time.timeScale == 1 && !AudioListener.pause && !MissionPauseMenu.IsPaused, "Disabling the menu cannot leave gameplay or audio paused");
            menu.gameObject.SetActive(true); Check(input.PauseMenu == menu, "Re-enabling the menu restores its player binding");
            await Wait(.06f); Check(menu.TryPause(), "Restart begins from an actually paused mission");
            var previous = input; Button("Restart Mission").onClick.Invoke();
            await Until(() => previous == null); await FindPlayer();
            Check(SceneManager.GetActiveScene().path == MissionPath && Time.timeScale == 1 && !AudioListener.pause && !menu.IsOpen,
                "Restart really reloads the mission with normal time, audio and a fresh hidden pause menu");
            Check(Object.FindObjectsByType<MissionPauseMenu>().Length == 1, "Restart does not duplicate pause menus");
            Check(menu.TryPause(), "Terminal-outcome review starts while paused");
            var mission = MissionOutcomeController.Find(input.gameObject.scene); Check(mission.ReportFailure("Pause review"), "The mission can report its terminal result");
            await Wait(.08f);
            Check(!menu.IsOpen && Time.timeScale == 1 && !AudioListener.pause && !menu.TryPause(), "Mission results dismiss pause, restore time and block reopening it");
            previous = input; mission.Retry(); await Until(() => previous == null); await FindPlayer();
            MissionOutcomeController.Find(input.gameObject.scene).ReturnScene = DestinationPath;
            Check(menu.TryPause(), "Exit begins from a paused mission"); Button("Exit Mission").onClick.Invoke();
            await Until(() => SceneManager.GetActiveScene().path == DestinationPath);
            Check(Time.timeScale == 1 && !AudioListener.pause && !MissionPauseMenu.IsPaused && Cursor.visible,
                "Exit uses the mission's configured return route and leaves the next scene unpaused");
            Check(File.Exists(capture), "A rendered pause-menu screenshot was saved for visual review");
            MissionChecks.Run(Check); SpawnDirectorChecks.Run(Check);
            Debug.Log("MISSION PAUSE CHECKS COMPLETE: " + checks); Finish(0);
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; Debug.Log("MISSION PAUSE PASS: " + message); }
    static void Finish(int code)
    {
        InputSystem.onBeforeUpdate -= Feed; Time.timeScale = 1; AudioListener.pause = false;
        if (keyboard != null) InputSystem.RemoveDevice(keyboard); if (mouse != null) InputSystem.RemoveDevice(mouse);
        foreach (var device in disabledDevices) if (device.added) InputSystem.EnableDevice(device);
        EditorApplication.Exit(code);
    }
    static void Capture(string path)
    {
        var canvas = menu.GetComponent<Canvas>().rootCanvas;
        var camera = input.GetComponent<MechPlayerRig>().GameplayCamera;
        var mode = canvas.renderMode; var priorCamera = canvas.worldCamera; float distance = canvas.planeDistance;
        var overlay = menu.GetComponent<Canvas>(); bool overrideSorting = overlay.overrideSorting;
        var target = new RenderTexture(1280, 720, 24); var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        var active = RenderTexture.active;
        try
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 2;
            overlay.overrideSorting = false;
            Canvas.ForceUpdateCanvases();
            RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            RenderTexture.active = target; texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            canvas.renderMode = mode; canvas.worldCamera = priorCamera; canvas.planeDistance = distance;
            overlay.overrideSorting = overrideSorting;
            RenderTexture.active = active; Object.Destroy(texture); Object.Destroy(target); Canvas.ForceUpdateCanvases();
        }
    }
}
