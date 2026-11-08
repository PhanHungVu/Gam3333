using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Always-active controller; only its authored panel is hidden. Escape is routed
// through the local player so it cannot also unlock/fire gameplay input.
[DisallowMultipleComponent]
public sealed class MissionPauseMenu : MonoBehaviour
{
    [SerializeField] GameObject panel;
    [SerializeField] Button resumeButton;
    [SerializeField] string mainMenuScene = "Assets/Scenes/MainMenuScene.unity";
    static MissionPauseMenu active;
    static int resumedFrame = -1;
    MechPlayerInput owner;
    GameObject ownedEventSystem;
    GameObject priorSelection;
    EventSystem navigationSystem;
    bool priorNavigationEvents, navigationGate;
    int openedFrame;
    float priorTimeScale = 1;
    bool priorAudioPause, priorCursorVisible, paused, leaving;
    CursorLockMode priorCursorLock;

    public bool IsOpen => paused;
    public MechPlayerInput Owner => owner;
    public static bool IsPaused => active != null && active.paused;
    public static bool BlocksGameplay => IsPaused || resumedFrame == Time.frameCount;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics() { active = null; resumedFrame = -1; }

    void Awake()
    {
        // A standalone Canvas can serialize display-driven root transforms.
        // Normalize the prefab when it is used beneath the shared HUD canvas.
        if (transform.parent != null && transform.parent.GetComponentInParent<Canvas>() != null)
        {
            var rect = (RectTransform)transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero; rect.pivot = new Vector2(.5f, .5f); rect.localScale = Vector3.one;
            var canvas = GetComponent<Canvas>();
            if (canvas != null) { canvas.overrideSorting = true; canvas.sortingOrder = 1000; }
        }
        if (panel != null) panel.SetActive(false);
    }
    void OnEnable()
    {
        if (owner != null) owner.BindPauseMenu(this);
        if (ownedEventSystem != null && EventSystem.current == null) ownedEventSystem.SetActive(true);
    }
    public void Bind(MechPlayerInput player)
    {
        if (owner != player && paused) Close(false);
        if (owner != null && owner.PauseMenu == this) owner.BindPauseMenu(null);
        owner = player;
        if (owner != null) owner.BindPauseMenu(this);
    }
    public bool Toggle()
    {
        if (paused) { Resume(); return true; }
        return TryPause();
    }
    public bool TryPause()
    {
        if (paused || leaving || active != null || panel == null || owner == null || !owner.isActiveAndEnabled
            || !owner.gameObject.scene.isLoaded || Time.timeScale <= 0
            || MissionOutcomeController.BlocksGameplay(owner.gameObject.scene)) return false;
        var health = owner.GetComponent<CombatHealth>();
        if (health != null && health.IsDead) return false;
        priorTimeScale = Time.timeScale; priorAudioPause = AudioListener.pause;
        priorCursorLock = Cursor.lockState; priorCursorVisible = Cursor.visible;
        paused = true; active = this;
        Time.timeScale = 0; AudioListener.pause = true;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        EnsureEventSystem();
        navigationSystem = EventSystem.current;
        if (navigationSystem != null)
        {
            priorNavigationEvents = navigationSystem.sendNavigationEvents;
            navigationSystem.sendNavigationEvents = false;
            navigationGate = true; openedFrame = Time.frameCount;
        }
        priorSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        panel.SetActive(true);
        if (EventSystem.current != null && resumeButton != null)
            EventSystem.current.SetSelectedGameObject(resumeButton.gameObject);
        return true;
    }
    void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        if (ownedEventSystem == null)
        {
            ownedEventSystem = new GameObject("Pause Menu Event System", typeof(EventSystem), typeof(InputSystemUIInputModule));
            ownedEventSystem.transform.SetParent(transform, false);
            ownedEventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }
        else ownedEventSystem.SetActive(true);
    }
    public void Resume() => Close(true);
    void Close(bool restoreCursor)
    {
        if (!paused) return;
        paused = false;
        if (active == this) active = null;
        resumedFrame = Time.frameCount;
        Time.timeScale = priorTimeScale; AudioListener.pause = priorAudioPause;
        if (navigationSystem != null) navigationSystem.sendNavigationEvents = priorNavigationEvents;
        navigationGate = false; navigationSystem = null;
        if (panel != null) panel.SetActive(false);
        bool gameplay = restoreCursor && owner != null && owner.isActiveAndEnabled
            && !MissionOutcomeController.BlocksGameplay(owner.gameObject.scene);
        Cursor.lockState = gameplay ? priorCursorLock : CursorLockMode.None;
        Cursor.visible = gameplay ? priorCursorVisible : true;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(priorSelection != null && priorSelection.activeInHierarchy ? priorSelection : null);
        priorSelection = null;
    }
    public void RestartMission()
    {
        if (!paused || owner == null || leaving) return;
        var scene = owner.gameObject.scene;
        var mission = MissionOutcomeController.Find(scene);
        leaving = true; Close(false);
        if (mission != null) mission.Retry(); else Load(scene.path);
    }
    public void ExitMission()
    {
        if (!paused || owner == null || leaving) return;
        var mission = MissionOutcomeController.Find(owner.gameObject.scene);
        leaving = true; Close(false);
        // Keep the mission's existing quest-board return/result handoff.
        if (mission != null) mission.ReturnToMenu(); else Load(mainMenuScene);
    }
    static void Load(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (Application.CanStreamedLevelBeLoaded(path)) { SceneManager.LoadSceneAsync(path); return; }
#if UNITY_EDITOR
        if (System.IO.File.Exists(path))
            UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(path, new LoadSceneParameters(LoadSceneMode.Single));
#endif
    }
    void Update()
    {
        if (paused && (owner == null || !owner.isActiveAndEnabled || MissionOutcomeController.BlocksGameplay(owner.gameObject.scene)))
            Close(false);
        // A held W/stick/submit from combat must not navigate to or activate an
        // exit button on opening. Mouse input remains available immediately.
        if (paused && navigationGate && Time.frameCount > openedFrame && !HeldNavigation())
        {
            if (navigationSystem != null) navigationSystem.sendNavigationEvents = priorNavigationEvents;
            navigationGate = false;
        }
    }
    bool HeldNavigation()
    {
        // Newly enabled UI actions can need an input update before reporting
        // their initial held value. Check the common controls as well.
        var keyboard = Keyboard.current;
        if (keyboard != null && (keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed || keyboard.dKey.isPressed
            || keyboard.upArrowKey.isPressed || keyboard.downArrowKey.isPressed || keyboard.leftArrowKey.isPressed || keyboard.rightArrowKey.isPressed
            || keyboard.enterKey.isPressed || keyboard.numpadEnterKey.isPressed || keyboard.spaceKey.isPressed)) return true;
        var gamepad = Gamepad.current;
        if (gamepad != null && (gamepad.leftStick.ReadValue().sqrMagnitude > .04f || gamepad.rightStick.ReadValue().sqrMagnitude > .04f
            || gamepad.dpad.ReadValue().sqrMagnitude > .04f || gamepad.buttonSouth.isPressed)) return true;
        if (navigationSystem == null) return false;
        var module = navigationSystem.GetComponent<InputSystemUIInputModule>();
        if (module == null) return false;
        var move = module.move != null ? module.move.action : null;
        var submit = module.submit != null ? module.submit.action : null;
        return move != null && move.ReadValue<Vector2>().sqrMagnitude > .01f || submit != null && submit.IsPressed();
    }
    void OnDisable()
    {
        Close(false);
        if (owner != null && owner.PauseMenu == this) owner.BindPauseMenu(null);
        if (ownedEventSystem != null) ownedEventSystem.SetActive(false);
    }
}
