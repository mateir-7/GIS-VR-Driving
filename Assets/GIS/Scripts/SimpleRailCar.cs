using UnityEngine;

namespace GIS
{

    public class SimpleRailCar : MonoBehaviour
    {
        public enum SpinAxis { X, Z }

        public Transform waypointsParent;

        public float speedKmh = 30f;

        public float rideHeight = 1.11f;

        public float startOffsetMeters = 0f;

        public SpinAxis spinAxis = SpinAxis.X;

        public float wheelRadius = 0.4f;

        Transform[] waypoints;
        float[] segLengths;
        float totalLength;
        float pathDistance;

        Transform wheelFL, wheelFR, wheelBL, wheelBR;
        Quaternion wheelFLInitial, wheelFRInitial, wheelBLInitial, wheelBRInitial;
        float wheelSpinAngle;

        void Awake()
        {

            var rb = GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity  = false;
            }
        }

        void Start()
        {
            TryAutoFindWheels();
            CacheInitialWheelRotations();
            RebuildPath();
        }

        void RebuildPath()
        {
            if (waypointsParent == null) { waypoints = null; return; }

            int n = waypointsParent.childCount;
            waypoints = new Transform[n];
            for (int i = 0; i < n; i++) waypoints[i] = waypointsParent.GetChild(i);

            if (n < 2) { segLengths = null; totalLength = 0f; return; }

            segLengths = new float[n];
            totalLength = 0f;
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                Vector3 a = waypoints[i].position;     a.y = 0f;
                Vector3 b = waypoints[next].position;  b.y = 0f;
                float len = Vector3.Distance(a, b);
                segLengths[i] = len;
                totalLength += len;
            }

            pathDistance = totalLength > 1e-3f ? Mathf.Repeat(startOffsetMeters, totalLength) : 0f;
        }

        void Update()
        {
            if (waypoints == null || waypoints.Length < 2 || totalLength < 0.01f) return;

            float speedMs = Mathf.Max(0f, speedKmh) / 3.6f;
            pathDistance = (pathDistance + speedMs * Time.deltaTime) % totalLength;
            if (pathDistance < 0f) pathDistance += totalLength;

            float walked = pathDistance;
            int segIdx = waypoints.Length - 1;
            for (int i = 0; i < segLengths.Length; i++)
            {
                if (walked <= segLengths[i]) { segIdx = i; break; }
                walked -= segLengths[i];
            }
            float t = segLengths[segIdx] > 1e-3f ? walked / segLengths[segIdx] : 0f;

            Vector3 pos = CatmullRomPos(segIdx - 1, segIdx, segIdx + 1, segIdx + 2, t);
            pos.y = rideHeight;

            Vector3 tangent = CatmullRomDerivative(segIdx - 1, segIdx, segIdx + 1, segIdx + 2, t);
            tangent.y = 0f;

            transform.position = pos;
            if (tangent.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);

            SpinWheels(speedMs);
        }

        Vector3 CatmullRomPos(int i0, int i1, int i2, int i3, float t)
        {
            int n = waypoints.Length;
            Vector3 P0 = waypoints[Wrap(i0, n)].position;
            Vector3 P1 = waypoints[Wrap(i1, n)].position;
            Vector3 P2 = waypoints[Wrap(i2, n)].position;
            Vector3 P3 = waypoints[Wrap(i3, n)].position;

            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * P1) +
                (-P0 + P2) * t +
                (2f * P0 - 5f * P1 + 4f * P2 - P3) * t2 +
                (-P0 + 3f * P1 - 3f * P2 + P3) * t3
            );
        }

        Vector3 CatmullRomDerivative(int i0, int i1, int i2, int i3, float t)
        {
            int n = waypoints.Length;
            Vector3 P0 = waypoints[Wrap(i0, n)].position;
            Vector3 P1 = waypoints[Wrap(i1, n)].position;
            Vector3 P2 = waypoints[Wrap(i2, n)].position;
            Vector3 P3 = waypoints[Wrap(i3, n)].position;

            float t2 = t * t;
            return 0.5f * (
                (-P0 + P2) +
                2f * (2f * P0 - 5f * P1 + 4f * P2 - P3) * t +
                3f * (-P0 + 3f * P1 - 3f * P2 + P3) * t2
            );
        }

        static int Wrap(int i, int n) => ((i % n) + n) % n;

        void TryAutoFindWheels()
        {
            if (wheelFL == null) wheelFL = FindAnyChildNamed("FL", "wheelFL", "wheelFrontLeft", "WheelFrontLeft", "FrontLeft");
            if (wheelFR == null) wheelFR = FindAnyChildNamed("FR", "wheelFR", "wheelFrontRight", "WheelFrontRight", "FrontRight");
            if (wheelBL == null) wheelBL = FindAnyChildNamed("BL", "wheelBL", "wheelBackLeft", "WheelBackLeft", "BackLeft", "RearLeft");
            if (wheelBR == null) wheelBR = FindAnyChildNamed("BR", "wheelBR", "wheelBackRight", "WheelBackRight", "BackRight", "RearRight");
        }

        Transform FindAnyChildNamed(params string[] candidates)
        {
            foreach (var n in candidates)
            {
                var t = FindChildRecursive(transform, n);
                if (t != null) return t;
            }
            return null;
        }

        static Transform FindChildRecursive(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform c = parent.GetChild(i);
                if (c.name.Equals(name, System.StringComparison.OrdinalIgnoreCase)) return c;
                var deep = FindChildRecursive(c, name);
                if (deep != null) return deep;
            }
            return null;
        }

        void CacheInitialWheelRotations()
        {
            if (wheelFL != null) wheelFLInitial = wheelFL.localRotation;
            if (wheelFR != null) wheelFRInitial = wheelFR.localRotation;
            if (wheelBL != null) wheelBLInitial = wheelBL.localRotation;
            if (wheelBR != null) wheelBRInitial = wheelBR.localRotation;
        }

        void SpinWheels(float speedMs)
        {
            float r = Mathf.Max(0.05f, wheelRadius);
            wheelSpinAngle += (speedMs / r) * Mathf.Rad2Deg * Time.deltaTime;

            Vector3 axis = spinAxis == SpinAxis.X ? Vector3.right : Vector3.forward;
            Quaternion spin = Quaternion.AngleAxis(wheelSpinAngle, axis);

            if (wheelFL != null) wheelFL.localRotation = wheelFLInitial * spin;
            if (wheelFR != null) wheelFR.localRotation = wheelFRInitial * spin;
            if (wheelBL != null) wheelBL.localRotation = wheelBLInitial * spin;
            if (wheelBR != null) wheelBR.localRotation = wheelBRInitial * spin;
        }
    }
}
