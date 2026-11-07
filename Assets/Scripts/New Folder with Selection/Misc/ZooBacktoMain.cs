using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class ZooBacktoMain : MonoBehaviour
{
    public KeyCode interactKey = KeyCode.G;
    Key inputKey;
    TMP_Text text;
    [SerializeField] bool activated = false;
    public SceneManagerScript sceneManager;
    void Start()
    {
        Enum.TryParse(interactKey.ToString(), out inputKey);
        text = GetComponentInChildren<TMP_Text>();
        if (sceneManager != null)
        {
            text.text = "Back to Menu [" + interactKey + "]";
        }
        else
        {
            text.text = "No GameObject Assigned";
        }
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[inputKey].wasPressedThisFrame && activated == true)
        {
            //Debug.Log("Going back to menu via " + sceneManager.gameObject.name);
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
