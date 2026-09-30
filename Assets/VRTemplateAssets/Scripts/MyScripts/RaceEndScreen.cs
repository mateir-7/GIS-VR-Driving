using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class RaceEndScreen : MonoBehaviour
{
    public Button restartButton;

    void Start()
    {
        if (restartButton != null)
            restartButton.onClick.AddListener(RestartRace);
    }

    void RestartRace()
    {

        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}