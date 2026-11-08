using System;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// Run in an isolated Unity project: -batchmode -executeMethod MainMenuAudioChecks.RunBatch
public static class MainMenuAudioChecks
{
    const string Pending = "MainMenuAudioChecks.Pending";
    static int checks;

    [InitializeOnLoadMethod]
    static void Register()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Pending, false)) Run();
        };
    }

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch-mode project.");
        EditorSceneManager.OpenScene("Assets/Scenes/MainMenuScene.unity");
        // Author the fixture before Play Mode so batch rendering initializes UI geometry.
        var canvas = Object.FindAnyObjectByType<Canvas>();
        var nested = new GameObject("Menu Audio Test Options", typeof(RectTransform));
        nested.transform.SetParent(canvas.transform, false);
        MakeButton(nested.transform, "Audio Test Done", Vector2.zero);
        var slider = new GameObject("Audio Test Slider", typeof(RectTransform), typeof(Slider)).GetComponent<Slider>();
        slider.transform.SetParent(nested.transform, false);
        Canvas.ForceUpdateCanvases();
        SessionState.SetBool(Pending, true);
        EditorApplication.EnterPlaymode();
    }

    static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException("MENU AUDIO FAIL: " + message);
        checks++;
        Debug.Log("MENU AUDIO PASS: " + message);
    }

    static async Task Wait(float seconds)
    {
        double end = Time.realtimeSinceStartupAsDouble + seconds;
        while (Time.realtimeSinceStartupAsDouble < end) await Task.Delay(10);
    }

    static async void Run()
    {
        SessionState.SetBool(Pending, false);
        Mouse mouse = null;
        try
        {
            Application.runInBackground = true;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            mouse = InputSystem.AddDevice<Mouse>("MenuAudioTestMouse");
            mouse.MakeCurrent();
            var module = EventSystem.current.GetComponent<InputSystemUIInputModule>();
            module.actionsAsset.devices = new InputDevice[] { mouse };
            var canvas = Object.FindAnyObjectByType<Canvas>();
            var nested = canvas.transform.Find("Menu Audio Test Options").gameObject;
            var button = nested.GetComponentInChildren<Button>(true);
            int successfulClicks = 0;
            button.onClick.AddListener(() => successfulClicks++);
            var slider = nested.GetComponentInChildren<Slider>(true);
            Canvas.ForceUpdateCanvases();
            var center = (Vector2)button.transform.position;
            Move(mouse, center);

            var menu = Object.FindAnyObjectByType<MainMenuAudio>();
            Check(menu != null, "MainMenuScene contains the authored menu audio prefab");
            menu.RegisterButtons();
            var ui = Object.FindAnyObjectByType<MainMenuUiAudio>().GetComponent<AudioSource>();
            var music = menu.GetComponentInChildren<AudioSource>();
            Check(music != null && music.clip.name == "main_menu" && music.playOnAwake && music.loop && music.isPlaying,
                "Main menu music starts automatically and loops");
            Check(music.volume == 1f && music.pitch == 1f && music.spatialBlend == 0f && music.outputAudioMixerGroup.name == "Music",
                "Music uses volume 1, pitch 1, 2D playback and the Music mixer bus");
            Check(ui.volume == 0.155f && ui.pitch == 1f && !ui.playOnAwake && !ui.loop && ui.spatialBlend == 0f &&
                ui.outputAudioMixerGroup.name == "SFX" && ui.outputAudioMixerGroup.audioMixer == music.outputAudioMixerGroup.audioMixer,
                "Hover and click share one 2D SFX voice at volume 0.155 and pitch 1");
            var fields = new SerializedObject(menu);
            Check(fields.FindProperty("hoverSound").objectReferenceValue.name == "button_hover" &&
                fields.FindProperty("clickSound").objectReferenceValue.name == "button_click" &&
                fields.FindProperty("hoverVolume").floatValue == 1f && fields.FindProperty("clickVolume").floatValue == 1f,
                "Correct hover/click clips and unit multipliers are assigned");
            foreach (var name in new[] { "Gym", "Zoo", "Quest Board", "Exit" })
            {
                var original = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Button>(true)).First(b => b.name == name);
                Check(original.GetComponent<MainMenuButtonAudio>() != null && original.onClick.GetPersistentEventCount() == 1 &&
                    original.onClick.GetPersistentMethodName(0) == (name == "Exit" ? "ExitGame" : "LoadScene"),
                    name + " retains its native navigation callback and gains button sounds");
            }

            // The synthetic device is installed after scene Awake. Capture its initial
            // position before the native module delivers the first pointer entry.
            var buttonAudio = button.GetComponent<MainMenuButtonAudio>();
            buttonAudio.enabled = false;
            buttonAudio.enabled = true;
            await Wait(0.15f);
            Canvas.ForceUpdateCanvases();
            module.Process();
            if (module.GetLastRaycastResult(mouse.deviceId).gameObject != button.gameObject)
            {
                var hits = new System.Collections.Generic.List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = center }, hits);
                Debug.Log("MENU AUDIO INPUT DIAGNOSTIC: screen " + Screen.width + "x" + Screen.height + " center " + center +
                    " button now " + button.transform.position + " mouse " + mouse.position.ReadValue() + " point " + module.point.action.ReadValue<Vector2>() +
                    " enabled " + module.point.action.enabled + " focused " + EventSystem.current.isFocused + " cursor " + Cursor.lockState +
                    " hits " + string.Join(",", hits.Select(h => h.gameObject.name)) + " native hit " + module.GetLastRaycastResult(mouse.deviceId).gameObject +
                    " image depth " + button.GetComponent<Image>().depth + " canvas " + button.GetComponent<Image>().canvas +
                    " scale " + canvas.transform.localScale + " rect " + ((RectTransform)canvas.transform).rect + " frame " + Time.frameCount);
            }
            Check(module.GetLastRaycastResult(mouse.deviceId).gameObject == button.gameObject,
                "The native Input System UI module raycasts the test button");
            Check(!ui.isPlaying, "A pointer already resting on a newly shown button produces no initial hover sound");
            Move(mouse, center + Vector2.up * 200f); module.Process();
            Move(mouse, center); module.Process();
            Check(ui.isPlaying, "Entering a nested options button plays the hover one-shot");
            ui.Stop(); module.Process();
            Check(!ui.isPlaying, "Holding the pointer over the same button does not repeat hover audio");

            Move(mouse, center, true); module.Process();
            Check(!ui.isPlaying && successfulClicks == 0, "Pressing a button does not play the successful-click sound early");
            Move(mouse, center, false); module.Process();
            Check(ui.isPlaying && successfulClicks == 1, "Release over the pressed button plays one successful native click");
            ui.Stop(); module.Process();
            Check(!ui.isPlaying, "Releasing a click does not replay hover audio");
            Move(mouse, center, true); module.Process();
            Move(mouse, center + Vector2.up * 200f, true); module.Process();
            ui.Stop();
            Move(mouse, center + Vector2.up * 200f, false); module.Process();
            Check(!ui.isPlaying && successfulClicks == 1, "Dragging off and releasing cancels the click and its sound");

            button.interactable = false;
            Move(mouse, center); module.Process();
            Move(mouse, center, true); module.Process();
            Move(mouse, center, false); module.Process();
            Check(!ui.isPlaying && successfulClicks == 1, "Disabled buttons remain silent");
            button.interactable = true;
            slider.value = 0.75f;
            Check(!ui.isPlaying && slider.GetComponent<MainMenuButtonAudio>() == null, "Slider value changes have no sound trigger");

            nested.SetActive(false);
            menu.RegisterButtons();
            Check(button.GetComponent<MainMenuButtonAudio>() != null, "Inactive nested options buttons are included");
            float before = music.time;
            Time.timeScale = 0f;
            await Wait(0.15f);
            Check(music.isPlaying && music.time != before, "Music continues while menu panels change and gameplay time is paused");
            Time.timeScale = 1f;
            music.time = music.clip.length - 0.1f;
            await Wait(0.3f);
            Check(music.isPlaying && music.time < 1f, "Music actually wraps at the end of the clip");

            var later = MakeButton(canvas.transform, "Runtime Options Button", new Vector2(-250f, 0f));
            await Wait(0.6f);
            Check(later.GetComponent<MainMenuButtonAudio>() != null, "Buttons created later receive audio automatically");
            var clipRefs = AssetDatabase.GetDependencies("Assets/Prefabs/Audio/MainMenuAudio.prefab", true)
                .Where(p => p.EndsWith(".mp3") || p.EndsWith(".wav")).OrderBy(p => p).ToArray();
            Check(clipRefs.SequenceEqual(new[] { "Assets/Audio/button_click.mp3", "Assets/Audio/button_hover.mp3", "Assets/Audio/main_menu.mp3" }),
                "The menu prefab references only its three menu clips");

            // Use the same immediate scene-load callback order as the existing menu buttons.
            Scene oldScene = menu.gameObject.scene;
            var destination = SceneManager.CreateScene("Menu Audio Destination");
            later.onClick.AddListener(() => { SceneManager.SetActiveScene(destination); SceneManager.UnloadSceneAsync(oldScene); });
            // Rebind so the audio runtime listener is after the navigation listener, as on authored buttons.
            Object.DestroyImmediate(later.GetComponent<MainMenuButtonAudio>());
            menu.RegisterButtons();
            later.onClick.Invoke();
            Check(ui.isPlaying, "Navigation clicks begin playback even with the scene-load callback first");
            await Wait(0.1f);
            Check(menu == null && music == null && ui != null && ui.isPlaying,
                "Unloading the menu stops its music while allowing the click tail to finish");
            await Wait(AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/button_click.mp3").length + 0.2f);
            Check(ui == null, "The UI voice cleans itself up after its final one-shot");
            Debug.Log("MENU AUDIO CHECKS PASSED: " + checks);
            InputSystem.RemoveDevice(mouse);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Time.timeScale = 1f;
            if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse);
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static Button MakeButton(Transform parent, string name, Vector2 offset)
    {
        var button = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)).GetComponent<Button>();
        button.transform.SetParent(parent, false);
        var rect = (RectTransform)button.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * 0.5f;
        rect.anchoredPosition = offset;
        rect.sizeDelta = new Vector2(160f, 80f);
        button.targetGraphic = button.GetComponent<Image>();
        return button;
    }

    static void Move(Mouse mouse, Vector2 position, bool down = false)
    {
        var state = new MouseState { position = position };
        state = state.WithButton(MouseButton.Left, down);
        InputSystem.QueueStateEvent(mouse, state);
        InputSystem.Update();
    }
}
