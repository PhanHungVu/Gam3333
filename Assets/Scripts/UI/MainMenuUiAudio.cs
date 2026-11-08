using UnityEngine;

/// <summary>One shared UI voice; finishes pending one-shots after its menu is unloaded.</summary>
[DisallowMultipleComponent, RequireComponent(typeof(AudioSource))]
public sealed class MainMenuUiAudio : MonoBehaviour
{
    MainMenuAudio owner;
    AudioSource source;
    bool claimed;

    public void Claim(MainMenuAudio menu)
    {
        owner = menu;
        source = GetComponent<AudioSource>();
        claimed = true;
        transform.SetParent(null, true);
        DontDestroyOnLoad(gameObject);
    }

    public void Play(AudioClip clip, float volume = 1f)
    {
        if (source != null && clip != null) source.PlayOneShot(clip, volume);
    }

    void Update()
    {
        if (claimed && owner == null && !source.isPlaying) Destroy(gameObject);
    }
}
