using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class ResetButton : MonoBehaviour
{
    public Button resetButton;
    public TextMeshProUGUI buttonLabel;
    public float confirmWindow = 2f;

    private bool awaitingConfirm = false;
    private float confirmTimer = 0f;

    void Start()
    {
        if (resetButton != null)
            resetButton.onClick.AddListener(OnPressed);
    }

    void Update()
    {
        if (awaitingConfirm)
        {
            confirmTimer -= Time.deltaTime;
            if (confirmTimer <= 0f)
            {
                awaitingConfirm = false;
                if (buttonLabel != null) buttonLabel.text = "Reset";
            }
        }
    }

    void OnPressed()
    {
        if (awaitingConfirm)
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
        else
        {
            awaitingConfirm = true;
            confirmTimer = confirmWindow;
            if (buttonLabel != null) buttonLabel.text = "Confirm?";
        }
    }
}