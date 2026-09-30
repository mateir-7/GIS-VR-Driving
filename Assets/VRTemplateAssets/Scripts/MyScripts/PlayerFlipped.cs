using UnityEngine;

public class PlayerFlipped : MonoBehaviour
{
    public float flipCheckDelay = 3f;
    public float flipRecoveryHeight = 1.5f;

    private float flippedTimer = 0f;
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        bool isFlipped = Vector3.Dot(transform.up, Vector3.up) < 0.3f;

        if (isFlipped)
        {
            flippedTimer += Time.fixedDeltaTime;
            if (flippedTimer >= flipCheckDelay)
            {
                Vector3 forwardFlat = transform.forward;
                forwardFlat.y = 0;
                transform.rotation = Quaternion.LookRotation(forwardFlat.normalized, Vector3.up);
                transform.position += Vector3.up * flipRecoveryHeight;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                flippedTimer = 0f;
            }
        }
        else
        {
            flippedTimer = 0f;
        }
    }
}