using UnityEditor.SceneManagement;
using UnityEngine;

public class SceneManagerScript : MonoBehaviour
{
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

