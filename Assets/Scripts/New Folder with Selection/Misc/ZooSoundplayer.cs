using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class ZooSoundplayer : MonoBehaviour
{
    AudioSource audioSource;
    public AudioClip clip;
    public float volume = 1f;
    public KeyCode interactKey = KeyCode.G;
    Key inputKey;
    TMP_Text text;
    [SerializeField] bool activated = false;
    void Start()
    {
        Enum.TryParse(interactKey.ToString(), out inputKey);
        audioSource = GetComponent<AudioSource>();
        text = GetComponentInChildren<TMP_Text>();
        if (clip != null)
        {
            text.text = "Step on me and press " + interactKey.ToString() + "\n" + " to play " + clip.name;
        }
        else
        {
            text.text = "No Audio Clip Assigned";
        }
    }
    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[inputKey].wasPressedThisFrame && activated == true)
        {
            Debug.Log("Playing sound: " + clip.name);
            audioSource.PlayOneShot(clip, volume);
        }
    }
    private void OnTriggerEnter(Collider other)
    {
        if (other is CharacterController)
        {
            activated = true;
            Debug.Log("Collided With Soundcube");
        }

    }
    private void OnTriggerExit(Collider other)
    {
        if (other is CharacterController)
        {
            activated = false;
        }
    }
}
