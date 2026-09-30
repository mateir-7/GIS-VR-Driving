using UnityEngine;

public class CheckpointTrigger : MonoBehaviour
{
    public int checkpointIndex = 0;

    void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player"))
        {
            PlayerUI ui = other.GetComponentInParent<PlayerUI>();
            if (ui != null) ui.HitCheckpoint(checkpointIndex);
        }
    }
}