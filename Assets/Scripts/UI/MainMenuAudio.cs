using UnityEngine;
using UnityEngine.UI;

/// <summary>Scene-owned menu music and shared hover/click audio. Never binds gameplay UI.</summary>
[DisallowMultipleComponent]
public sealed class MainMenuAudio : MonoBehaviour
{
    [SerializeField] AudioSource music;
    [SerializeField] MainMenuUiAudio uiAudio;
    [SerializeField] AudioClip hoverSound;
    [SerializeField] AudioClip clickSound;
    [SerializeField, Range(0f, 1f)] float hoverVolume = 1f;
    [SerializeField, Range(0f, 1f)] float clickVolume = 1f;
    float nextButtonScan;

    void Awake()
    {
        // Only the short UI voice survives navigation, so the final click isn't cut off.
        // The music source remains in this scene and stops when the scene unloads.
        if (uiAudio != null) uiAudio.Claim(this);
    }

    void Start()
    {
        Canvas.ForceUpdateCanvases();
        RegisterButtons();
    }

    void Update()
    {
        if (Time.unscaledTime < nextButtonScan) return;
        nextButtonScan = Time.unscaledTime + 0.5f;
        RegisterButtons();
    }

    /// <summary>Includes inactive nested options buttons and buttons created after startup.</summary>
    public void RegisterButtons()
    {
        foreach (var root in gameObject.scene.GetRootGameObjects())
        foreach (var button in root.GetComponentsInChildren<Button>(true))
        {
            var audio = button.GetComponent<MainMenuButtonAudio>();
            if (audio == null) audio = button.gameObject.AddComponent<MainMenuButtonAudio>();
            audio.Bind(this);
        }
    }

    public void PlayHover()
    {
        if (isActiveAndEnabled && uiAudio != null) uiAudio.Play(hoverSound, hoverVolume);
    }

    public void PlayClick()
    {
        if (isActiveAndEnabled && uiAudio != null) uiAudio.Play(clickSound, clickVolume);
    }
}
