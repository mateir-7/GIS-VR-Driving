using UnityEngine;
using TMPro;

public class PlayerUI : MonoBehaviour
{
    public TextMeshProUGUI lapText;
    public int maxLaps = 3;
    public GameObject endScreen;
    public SimKartController playerController;

    public TextMeshProUGUI[] checkpointLabels;
    public Color notHitColor = Color.gray;
    public Color hitColor = Color.green;
    public Color flashColor = Color.red;
    public float flashDuration = 1f;

    private int currentLap = 1;
    private bool raceFinished = false;

    public int requiredCheckpoints = 3;
    private int checkpointsHit = 0;

    void Start()
    {
        UpdateLapDisplay();
        if (endScreen != null) endScreen.SetActive(false);
        if (checkpointLabels != null)
            foreach (var label in checkpointLabels)
                if (label != null) label.color = notHitColor;
    }

    public void HitCheckpoint(int checkpointIndex)
    {
        if (checkpointIndex == checkpointsHit)
        {
            checkpointsHit++;
            FlashCheckpoint(checkpointIndex);
        }
    }

    void FlashCheckpoint(int index)
    {
        if (checkpointLabels == null || index >= checkpointLabels.Length) return;
        StartCoroutine(FlashRoutine(checkpointLabels[index]));
    }

    void FlashLapText()
    {
        if (lapText != null)
            StartCoroutine(FlashLapRoutine());
    }

    System.Collections.IEnumerator FlashRoutine(TextMeshProUGUI label)
    {
        label.color = flashColor;
        yield return new WaitForSeconds(flashDuration / 3);
        label.color = hitColor;
        yield return new WaitForSeconds(flashDuration / 3);
        label.color = flashColor;
        yield return new WaitForSeconds(flashDuration / 3);
        label.color = hitColor;
    }

    System.Collections.IEnumerator FlashLapRoutine()
    {
        Color original = lapText.color;
        lapText.color = flashColor;
        yield return new WaitForSeconds(flashDuration / 3);
        lapText.color = original;
        yield return new WaitForSeconds(flashDuration / 3);
        lapText.color = flashColor;
        yield return new WaitForSeconds(flashDuration / 3);
        lapText.color = original;
    }

    public void IncrementLap()
    {
        if (raceFinished) return;

        if (checkpointsHit < requiredCheckpoints) return;

        checkpointsHit = 0;
        ResetCheckpointColors();
        currentLap++;

        if (currentLap > maxLaps)
            FinishRace();
        else
        {
            UpdateLapDisplay();
            FlashLapText();
        }
    }

    void ResetCheckpointColors()
    {
        if (checkpointLabels == null) return;
        for (int i = 0; i < checkpointLabels.Length; i++)
            checkpointLabels[i].color = notHitColor;
    }

    void UpdateLapDisplay()
    {
        if (lapText != null)
            lapText.text = "Lap " + currentLap + " / " + maxLaps;
    }

    void FinishRace()
    {
        raceFinished = true;
        if (lapText != null) lapText.text = "Finished!";
        if (playerController != null) playerController.inputLocked = true;
        if (endScreen != null) endScreen.SetActive(true);

        if (checkpointLabels != null)
            foreach (var label in checkpointLabels)
                if (label != null) label.gameObject.SetActive(false);
    }
}