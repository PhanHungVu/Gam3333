using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class ZooBacktoMain : MonoBehaviour
{
    public KeyCode interactKey = KeyCode.F;
    Key inputKey;
    TMP_Text text;
    [SerializeField] bool activated = false;
    public GameObject toggleObject;
    SceneManagerScript sceneManager;
    void Start()
    {
        Enum.TryParse(interactKey.ToString(), out inputKey);
        text = GetComponentInChildren<TMP_Text>();
        if (toggleObject != null)
        {
            text.text = "Back to Menu";
            sceneManager = toggleObject.GetComponent<SceneManagerScript>();
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
            sceneManager.LoadScene("MainMenuScene");
        }
    }
    private void OnTriggerEnter(Collider other)
    {
        if (other is CharacterController)
        {
            activated = true;
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
