using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneManagerScript : MonoBehaviour
{
    public void LoadScene(string sceneName)
    {
        // The objective pauses on completion; the next scene must start normally.
        Time.timeScale = 1f;
        SceneManager.LoadScene(sceneName);
    }
    public void ExitGame()
    {
        Application.Quit();
    }
}

