using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class ZooSoundplayer : MonoBehaviour
{
    Collider hitbox;
    AudioSource audioSource;
    public AudioClip clip;
    public float volume = 1f;
    public KeyCode interactKey = KeyCode.F;
    Key inputKey;
    TMP_Text text;
    [SerializeField] bool activated = false;
    void Start()
    {
        Enum.TryParse(interactKey.ToString(), out inputKey);
        hitbox = GetComponent<Collider>();
        audioSource = GetComponent<AudioSource>();
        text = GetComponentInChildren<TMP_Text>();
        if (clip != null)
        {
            text.text = "Press " + interactKey.ToString() + " to play " + "\n"+ clip.name;
        }
        else
        {
            text.text = "No Audio Clip Assigned";
        }
    }
    void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current[inputKey].wasPressedThisFrame && activated == true)
        {
            Debug.Log("Playing sound: " + clip.name);
            audioSource.PlayOneShot(clip, volume);
        }
    }
    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            activated = true;
        }
    }
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            activated = false;
        }
    }
}
