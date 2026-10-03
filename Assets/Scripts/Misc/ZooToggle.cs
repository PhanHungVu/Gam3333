using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class ZooToggle : MonoBehaviour
{
    public KeyCode interactKey = KeyCode.F;
    Key inputKey;
    TMP_Text text;
    [SerializeField] bool activated = false;
    public GameObject toggleObject;
    void Start()
    {
        Enum.TryParse(interactKey.ToString(), out inputKey);
        text = GetComponentInChildren<TMP_Text>();
        if (toggleObject != null)
        {
            text.text = "Step on me and press " + interactKey.ToString() + "\n" + " to toggle " + toggleObject.name;
            toggleObject.SetActive(false);
        }
        else
        {
            text.text = "No GameObject Assigned";
        }
    }

    void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current[inputKey].wasPressedThisFrame && activated == true)
        {
            Debug.Log("Toggling " + toggleObject.name);
            toggleObject.SetActive(!toggleObject.activeSelf);
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
