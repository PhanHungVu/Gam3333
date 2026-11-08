using UnityEngine;

// Single local-player binding. Reuses the scene camera/cursor; creates configured fallbacks only when absent.
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class MechPlayerRig : MonoBehaviour
{
    [SerializeField] GameObject cameraPrefab;
    [SerializeField] GameObject cursorPrefab;
    [Tooltip("Fallback only: scenes with an authored combat HUD keep it.")]
    [SerializeField] GameObject combatHudPrefab;
    GameObject ownedCamera, ownedCursor;
    GameObject ownedCombatHud;
    Camera replacedCamera;
    AudioListener replacedListener;
    bool cameraWasEnabled, listenerWasEnabled;
    public Camera GameplayCamera { get; private set; }
    void Awake()
    {
        GameplayCamera = Camera.main;
        var control = GameplayCamera != null ? GameplayCamera.GetComponent<CameraControlThirdPerson>() : null;
        if (control == null && cameraPrefab != null)
        {
            replacedCamera = GameplayCamera;
            if (replacedCamera != null)
            {
                cameraWasEnabled = replacedCamera.enabled; replacedCamera.enabled = false;
                replacedListener = replacedCamera.GetComponent<AudioListener>();
                if (replacedListener != null) { listenerWasEnabled = replacedListener.enabled; replacedListener.enabled = false; }
            }
            // Instantiate beneath an inactive holder so the target is bound before the camera's Awake.
            var staging = new GameObject("Camera Setup"); staging.SetActive(false);
            ownedCamera = Instantiate(cameraPrefab, staging.transform);
            control = ownedCamera.GetComponent<CameraControlThirdPerson>();
            control.BindTarget(transform);
            GameplayCamera = ownedCamera.GetComponent<Camera>();
            ownedCamera.transform.SetParent(null, true);
            Destroy(staging);
        }
        if (control != null) control.BindTarget(transform);
        GetComponent<MechCombat>().BindCamera(GameplayCamera);
        GetComponent<MechMovementController>().BindCamera(GameplayCamera);
        GetComponent<MechTargetLockController>().BindCamera(GameplayCamera);
    }
    void Start()
    {
        var authoredHud = FindAnyObjectByType<MechWeaponsHud>();
        var cursor = FindAnyObjectByType<LockOnCursor>();
        if (cursor == null && cursorPrefab != null)
        {
            ownedCursor = Instantiate(cursorPrefab);
            cursor = ownedCursor.GetComponentInChildren<LockOnCursor>(true);
            // The shared canvas includes CombatHUD. Keep a scene's authored HUD
            // when only its cursor is missing, rather than showing a second copy.
            if (authoredHud != null)
                foreach (var bundled in ownedCursor.GetComponentsInChildren<MechWeaponsHud>(true))
                {
                    var root = bundled.transform.parent.gameObject;
                    root.SetActive(false);
                    Destroy(root);
                }
        }
        if (cursor != null) cursor.Bind(GetComponent<MechTargetLockController>(), GameplayCamera);
        var cursorCanvas = cursor != null ? cursor.GetComponentInParent<Canvas>() : null;
        if (cursorCanvas != null)
        {
            var pause = cursorCanvas.rootCanvas.GetComponentInChildren<MissionPauseMenu>(true);
            if (pause != null) pause.Bind(GetComponent<MechPlayerInput>());
        }
        if (combatHudPrefab != null && FindAnyObjectByType<MechWeaponsHud>() == null && cursor != null)
        {
            var canvas = cursor.GetComponentInParent<Canvas>();
            if (canvas != null) ownedCombatHud = Instantiate(combatHudPrefab,canvas.transform,false);
        }
    }
    void OnEnable() { if (ownedCamera != null) ownedCamera.SetActive(true); if (ownedCursor != null) ownedCursor.SetActive(true); if (ownedCombatHud != null) ownedCombatHud.SetActive(true); }
    void OnDisable() { if (ownedCamera != null) ownedCamera.SetActive(false); if (ownedCursor != null) ownedCursor.SetActive(false); if (ownedCombatHud != null) ownedCombatHud.SetActive(false); }
    void OnDestroy()
    {
        if (ownedCamera != null) Destroy(ownedCamera);
        if (ownedCursor != null) Destroy(ownedCursor);
        if (ownedCombatHud != null) Destroy(ownedCombatHud);
        if (replacedCamera != null) replacedCamera.enabled = cameraWasEnabled;
        if (replacedListener != null) replacedListener.enabled = listenerWasEnabled;
    }
}
