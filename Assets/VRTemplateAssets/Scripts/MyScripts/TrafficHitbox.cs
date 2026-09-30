using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class TrafficHitbox : MonoBehaviour
{
    public float brakeForce = 80f;
    public float lateralKick = 20f;
    public float upwardKick = 0.05f;
    public float minSpeedForBrake = 1f;

    void Reset()
    {
        var box = GetComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(2.0f, 1.4f, 4.5f);
        box.center = new Vector3(0f, 0.6f, 0f);
    }

    void OnTriggerStay(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;

        Vector3 vel = rb.linearVelocity;
        vel.y = 0f;
        float speed = vel.magnitude;

        if (speed > minSpeedForBrake)
        {
            Vector3 antiVel = -vel.normalized;
            rb.AddForce(antiVel * brakeForce, ForceMode.Acceleration);
        }

        Vector3 me = transform.position; me.y = 0f;
        Vector3 player = rb.position; player.y = 0f;
        Vector3 outward = player - me;
        if (outward.sqrMagnitude > 1e-4f)
        {
            outward.Normalize();
            if (Vector3.Dot(outward, vel) < 0f)
            {
                outward.y = upwardKick;
                rb.AddForce(outward * lateralKick, ForceMode.Acceleration);
            }
        }
    }
}