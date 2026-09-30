using UnityEngine;

public class FinishLine : MonoBehaviour
{
    public string playerTag = "Player";

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag(playerTag))
        {
            PlayerUI counter = other.GetComponent<PlayerUI>();
            if (counter == null)
                counter = other.GetComponentInParent<PlayerUI>();

            if (counter != null)
                counter.IncrementLap();
        }
    }
}