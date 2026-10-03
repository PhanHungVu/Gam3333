using UnityEngine;
using UnityEditor.SceneManagement;

public class MainMenuScript : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void LoadScene(string sceneName)
    {
        EditorSceneManager.LoadScene("Assets/Scenes/" + sceneName + ".unity");
    }
    public void ExitGame()
    {
        Application.Quit();
    }
}
