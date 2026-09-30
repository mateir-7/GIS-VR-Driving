using UnityEngine;

namespace GIS
{

    [RequireComponent(typeof(Rigidbody))]
    public class CityTrafficAI : MonoBehaviour
    {
        public enum SpinAxis { X, Y, Z }

        [Header("Path")]
        public Transform waypointsParent;
        public float lookaheadDistance = 8f;
        public float waypointReachDistance = 5f;
        public bool forceLoop = true;

        [Header("Driving")]
        public float fastSpeedKmh   = 40f;
        public float mediumSpeedKmh = 25f;
        public float slowSpeedKmh   = 18f;

        public float acceleration         = 600f;
        public float brakingStrength      = 800f;
        public float steeringStrength     = 1.8f;
        public float steerSmoothing       = 0.15f;
        public float corneringSpeedFactor = 0.3f;
        public float curveBrakingStrength = 2.5f;

        public float obstacleLookAhead = 10f;
        public LayerMask carLayer;

        [Header("Recovery")]
        public float flipCheckDelay    = 2f;
        public float flipRecoveryHeight = 1.5f;
        public float stuckCheckDelay   = 4f;
        public float stuckMinHorizontalMovePerSecond = 0.5f;

        public bool autoConfigurePhysics = true;
        public float lateralFrictionStrength = 8f;
        public float maxUpwardJoltSpeed = 0.6f;

        public Transform wheelFL, wheelFR, wheelBL, wheelBR;
        public float wheelRadius = 0.4f;
        public SpinAxis spinAxis = SpinAxis.X;
        public float maxCosmeticSteerDeg = 22f;

        Rigidbody rb;
        Transform[] waypoints;
        int currentWaypoint = 0;
        bool chainIsLoop;
        string highwayType = "";
        float maxSpeedMs;
        float smoothedSteerInput;
        float flippedTimer;
        float stuckTimer;
        Vector3 lastStuckCheckPos;
        float lastStuckCheckTime;
        float wheelSpinFrontAngle, wheelSpinRearAngle;
        Quaternion wheelFLInitial, wheelFRInitial, wheelBLInitial, wheelBRInitial;

        void Start()
        {
            rb = GetComponent<Rigidbody>();
            if (autoConfigurePhysics) ConfigureCarPhysics();
            TryAutoFindWheels();
            CacheInitialWheelRotations();
            ReloadChain();
        }

        void ConfigureCarPhysics()
        {
            if (rb == null) return;
            if (rb.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic)
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            if (rb.interpolation != RigidbodyInterpolation.Interpolate)
                rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        public void ReloadChain()
        {
            if (waypointsParent == null) { waypoints = null; return; }

            var chain = waypointsParent.GetComponent<WaypointChain>();
            highwayType = chain != null ? chain.highwayType : "";
            chainIsLoop = chain != null && chain.isLoop;

            int n = waypointsParent.childCount;
            waypoints = new Transform[n];
            for (int i = 0; i < n; i++) waypoints[i] = waypointsParent.GetChild(i);

            maxSpeedMs = TargetSpeedKmh(highwayType) / 3.6f;

            currentWaypoint = 0;
            if (n > 0)
            {
                Vector3 carPos = transform.position;
                float bestSq = float.PositiveInfinity;
                int closest = 0;
                for (int i = 0; i < n; i++)
                {
                    if (waypoints[i] == null) continue;
                    Vector3 d = waypoints[i].position - carPos;
                    d.y = 0f;
                    float sq = d.sqrMagnitude;
                    if (sq < bestSq) { bestSq = sq; closest = i; }
                }
                currentWaypoint = (closest + 1) % Mathf.Max(1, n);
            }

            lastStuckCheckPos = transform.position;
            lastStuckCheckTime = Time.fixedTime;
            stuckTimer = 0f;
        }

        bool IsLoop() => forceLoop || chainIsLoop;

        int WrapIdx(int i)
        {
            int n = waypoints.Length;
            if (n == 0) return 0;
            if (IsLoop()) return ((i % n) + n) % n;
            return Mathf.Clamp(i, 0, n - 1);
        }

        float TargetSpeedKmh(string h)
        {
            string b = h ?? "";
            if (b.EndsWith("_link")) b = b.Substring(0, b.Length - 5);
            switch (b)
            {
                case "motorway":
                case "trunk":
                case "primary":
                case "secondary":     return fastSpeedKmh;
                case "tertiary":
                case "unclassified":  return mediumSpeedKmh;
                case "residential":
                case "service":
                case "living_street": return slowSpeedKmh;
                default:              return mediumSpeedKmh;
            }
        }

        void FixedUpdate()
        {
            CheckFlipped();
            CheckStuck();
            DampenSmallVerticalJolts();

            if (waypoints == null || waypoints.Length < 2) return;

            Vector3 carPos = transform.position;

            AdvanceIfPassed(carPos);

            Vector3 target = ComputePursuitTarget(carPos, Mathf.Max(0.5f, lookaheadDistance));
            Vector3 toTarget = target - carPos;
            toTarget.y = 0f;

            float angleToTarget = Vector3.SignedAngle(transform.forward, toTarget, Vector3.up);
            float steerRaw = Mathf.Clamp(angleToTarget / 45f, -1f, 1f);
            smoothedSteerInput = Mathf.Lerp(smoothedSteerInput, steerRaw, steerSmoothing);
            transform.Rotate(0f, smoothedSteerInput * steeringStrength, 0f);

            float absAngleDeg     = Mathf.Abs(angleToTarget);
            float turnSharpness   = Mathf.Clamp01(absAngleDeg / 45f);
            float scaledSharpness = Mathf.Pow(turnSharpness, 1f / Mathf.Max(0.1f, curveBrakingStrength));
            float corneringMul    = Mathf.Lerp(1f, corneringSpeedFactor, scaledSharpness);
            float targetSpeed     = maxSpeedMs * corneringMul;

            Vector3 rayStart = transform.position + transform.forward * 1.5f + Vector3.up * 0.5f;
            if (Physics.Raycast(rayStart, transform.forward, out RaycastHit hit, obstacleLookAhead, carLayer))
            {
                float distanceFactor = hit.distance / obstacleLookAhead;
                targetSpeed *= distanceFactor;
            }

            Vector3 hVel = rb.linearVelocity; hVel.y = 0f;
            float currentHSpeed = hVel.magnitude;

            if (currentHSpeed > targetSpeed + 0.1f)
            {
                rb.AddForce(-transform.forward * brakingStrength * Time.fixedDeltaTime, ForceMode.Acceleration);
            }
            else if (currentHSpeed < targetSpeed - 0.1f)
            {
                rb.AddForce(transform.forward * acceleration * Time.fixedDeltaTime, ForceMode.Acceleration);
            }

            DampLateralVelocity();
        }

        void AdvanceIfPassed(Vector3 carPos)
        {
            int n = waypoints.Length;
            int prev = WrapIdx(currentWaypoint - 1);
            Vector3 A = waypoints[prev].position;            A.y = 0f;
            Vector3 B = waypoints[currentWaypoint].position; B.y = 0f;
            Vector3 C = carPos;                              C.y = 0f;

            Vector3 ab = B - A;
            float abLenSq = ab.sqrMagnitude;

            bool passedProjection = false;
            if (abLenSq >= 1e-4f)
            {
                float t = Vector3.Dot(C - A, ab) / abLenSq;
                if (t > 1f) passedProjection = true;
            }
            float dToB = Vector3.Distance(C, B);

            if (passedProjection || dToB < waypointReachDistance)
                TryAdvance();
        }

        void TryAdvance()
        {
            int n = waypoints.Length;
            int next = currentWaypoint + 1;
            if (next >= n)
            {
                if (IsLoop()) next = 0;
                else return;
            }
            currentWaypoint = next;
        }

        Vector3 ComputePursuitTarget(Vector3 carPos, float lookahead)
        {
            int n = waypoints.Length;
            if (n < 2) return carPos + transform.forward * Mathf.Max(1f, lookahead);

            Vector3 carFlat = carPos; carFlat.y = 0f;
            int prev = WrapIdx(currentWaypoint - 1);
            int curr = currentWaypoint;

            Vector3 A = waypoints[prev].position; A.y = 0f;
            Vector3 B = waypoints[curr].position; B.y = 0f;
            Vector3 ab = B - A;
            float abLen = ab.magnitude;
            float s = abLen > 1e-3f ? Mathf.Clamp01(Vector3.Dot(carFlat - A, ab) / (abLen * abLen)) : 0f;
            float remaining = lookahead;

            float distInSeg = (1f - s) * abLen;
            if (remaining <= distInSeg)
            {
                float t = abLen > 1e-3f ? s + remaining / abLen : 1f;
                return CatmullRomPos(prev - 1, prev, curr, curr + 1, Mathf.Clamp01(t));
            }
            remaining -= distInSeg;

            int from = curr;
            for (int safety = 0; safety < n + 4; safety++)
            {
                int to = from + 1;
                if (to >= n)
                {
                    if (IsLoop()) to = 0;
                    else return waypoints[n - 1].position;
                }
                Vector3 segA = waypoints[from].position; segA.y = 0f;
                Vector3 segB = waypoints[to].position;   segB.y = 0f;
                float len = Vector3.Distance(segA, segB);
                if (len < 0.01f) { from = to; continue; }

                if (remaining <= len)
                {
                    float t = remaining / len;
                    return CatmullRomPos(from - 1, from, to, to + 1, t);
                }
                remaining -= len;
                from = to;
            }
            return waypoints[currentWaypoint].position;
        }

        Vector3 CatmullRomPos(int i0, int i1, int i2, int i3, float t)
        {
            int n = waypoints.Length;
            Vector3 P0, P1, P2, P3;
            if (IsLoop())
            {
                P0 = waypoints[((i0 % n) + n) % n].position;
                P1 = waypoints[((i1 % n) + n) % n].position;
                P2 = waypoints[((i2 % n) + n) % n].position;
                P3 = waypoints[((i3 % n) + n) % n].position;
            }
            else
            {
                P0 = waypoints[Mathf.Clamp(i0, 0, n - 1)].position;
                P1 = waypoints[Mathf.Clamp(i1, 0, n - 1)].position;
                P2 = waypoints[Mathf.Clamp(i2, 0, n - 1)].position;
                P3 = waypoints[Mathf.Clamp(i3, 0, n - 1)].position;
            }
            P0.y = 0f; P1.y = 0f; P2.y = 0f; P3.y = 0f;

            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * (
                (2f * P1) +
                (-P0 + P2) * t +
                (2f * P0 - 5f * P1 + 4f * P2 - P3) * t2 +
                (-P0 + 3f * P1 - 3f * P2 + P3) * t3
            );
        }

        void DampLateralVelocity()
        {
            if (rb == null || lateralFrictionStrength <= 0f) return;
            Vector3 forward = transform.forward; forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) return;
            forward.Normalize();

            Vector3 v  = rb.linearVelocity;
            Vector3 vH = v; vH.y = 0f;
            float forwardSpeed   = Vector3.Dot(vH, forward);
            Vector3 lateralVel   = vH - forward * forwardSpeed;

            float damp = Mathf.Exp(-lateralFrictionStrength * Time.fixedDeltaTime);
            Vector3 newLateral = lateralVel * damp;
            Vector3 newVH = forward * forwardSpeed + newLateral;
            rb.linearVelocity = new Vector3(newVH.x, v.y, newVH.z);
        }

        void DampenSmallVerticalJolts()
        {
            if (rb == null || maxUpwardJoltSpeed <= 0f) return;
            Vector3 v = rb.linearVelocity;
            Vector3 hv = v; hv.y = 0f;
            if (hv.magnitude > 1f && v.y > 0f && v.y < maxUpwardJoltSpeed)
            {
                v.y *= 0.3f;
                rb.linearVelocity = v;
            }
        }

        void CheckFlipped()
        {
            bool isFlipped = Vector3.Dot(transform.up, Vector3.up) < 0.3f;
            if (isFlipped)
            {
                flippedTimer += Time.fixedDeltaTime;
                if (flippedTimer >= flipCheckDelay)
                {
                    Vector3 forwardFlat = transform.forward; forwardFlat.y = 0f;
                    if (forwardFlat.sqrMagnitude < 1e-3f) forwardFlat = Vector3.forward;
                    transform.rotation = Quaternion.LookRotation(forwardFlat.normalized, Vector3.up);
                    transform.position += Vector3.up * flipRecoveryHeight;
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    flippedTimer = 0f;
                }
            }
            else flippedTimer = 0f;
        }

        void CheckStuck()
        {
            if (waypoints == null || waypoints.Length == 0) return;

            if (Time.fixedTime - lastStuckCheckTime > 1f)
            {
                Vector3 disp = transform.position - lastStuckCheckPos;
                disp.y = 0f;
                if (disp.magnitude < stuckMinHorizontalMovePerSecond)
                {
                    stuckTimer += 1f;
                    if (stuckTimer >= stuckCheckDelay)
                    {
                        Transform target = waypoints[currentWaypoint];
                        int peek = WrapIdx(currentWaypoint + 1);
                        Vector3 lookDir = waypoints[peek].position - target.position;
                        lookDir.y = 0f;
                        if (lookDir.sqrMagnitude < 1e-3f) lookDir = transform.forward;

                        transform.position = target.position + Vector3.up * flipRecoveryHeight;
                        transform.rotation = Quaternion.LookRotation(lookDir.normalized, Vector3.up);
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        stuckTimer = 0f;
                    }
                }
                else stuckTimer = 0f;

                lastStuckCheckPos = transform.position;
                lastStuckCheckTime = Time.fixedTime;
            }
        }

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

        void Update()
        {
            if (rb == null) return;
            SpinWheels();
        }

        void SpinWheels()
        {
            float r = Mathf.Max(0.05f, wheelRadius);
            float speed = rb.linearVelocity.magnitude;
            float spinDelta = (speed / r) * Mathf.Rad2Deg * Time.deltaTime;
            wheelSpinFrontAngle += spinDelta;
            wheelSpinRearAngle  += spinDelta;

            Vector3 axisV = spinAxis switch
            {
                SpinAxis.X => Vector3.right,
                SpinAxis.Y => Vector3.up,
                _          => Vector3.forward,
            };

            Quaternion steerYaw  = Quaternion.AngleAxis(smoothedSteerInput * maxCosmeticSteerDeg, Vector3.up);
            Quaternion spinFront = Quaternion.AngleAxis(wheelSpinFrontAngle, axisV);
            Quaternion spinRear  = Quaternion.AngleAxis(wheelSpinRearAngle, axisV);

            if (wheelFL != null) wheelFL.localRotation = wheelFLInitial * steerYaw * spinFront;
            if (wheelFR != null) wheelFR.localRotation = wheelFRInitial * steerYaw * spinFront;
            if (wheelBL != null) wheelBL.localRotation = wheelBLInitial * spinRear;
            if (wheelBR != null) wheelBR.localRotation = wheelBRInitial * spinRear;
        }
    }
}
