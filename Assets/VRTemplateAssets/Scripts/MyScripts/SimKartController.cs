using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class SimKartController : MonoBehaviour
{
    [Header("Wheels")]
    public WheelCollider frontLeft;
    public WheelCollider frontRight;
    public WheelCollider rearLeft;
    public WheelCollider rearRight;

    public Transform visualFrontLeft;
    public Transform visualFrontRight;
    public Transform visualRearLeft;
    public Transform visualRearRight;

    [Header("Cameras")]
    public Camera vrCameraLens;
    public Camera thirdPersonCameraLens;
    public InputActionReference switchCameraAction;
    private bool isThirdPerson = false;

    [Header("VR Controls")]
    public Transform steeringWheel;
    public Transform gearShift;
    public XRGrabInteractable steeringWheelGrab;
    public InputActionReference accelerateButton;
    public InputActionReference brakeButton;
    public HingeJoint wheelJoint;
    public XRGrabInteractable gearShiftGrab;
    public HingeJoint gearJoint;

    private float savedGearAngle;
    private bool gearWasGrabbed;

    public enum DriveType { FWD, RWD, AWD }

    public bool inputLocked = false;

    [Header("Car")]
    public DriveType driveType = DriveType.AWD;
    public float maxMotorTorque = 1500f;
    public float maxSteeringAngle = 35f;
    public float maxBrakeTorque = 3000f;
    public float topSpeed = 100f;
    public float downforce = 50f;

    [Header("Gears")]
    public float[] gearShiftSpeeds;
    public float shiftDelay = 0.3f;
    public int currentGear = 1;
    private bool isShifting = false;

    public Transform centerOfMass;
    private Rigidbody rb;

    [Header("Handling")]
    public float steeringSpeed = 5f;
    private float currentSteerAngle = 0f;
    public float steeringDeadzone = 5f;
    public float maxWheelRotation = 450f;

    private float previousRawAngle = 0f;
    private int revolutions = 0;

    public float slipThresholdSpeed = 50f;
    public float highSpeedGrip = 0.4f;

    private void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (centerOfMass != null)
        {
            rb.centerOfMass = centerOfMass.localPosition;
        }

        if (vrCameraLens != null) vrCameraLens.enabled = true;
        if (thirdPersonCameraLens != null) thirdPersonCameraLens.enabled = false;

        ToggleAudioListeners();
    }
    private void FixedUpdate()
    {
        HandleAntiJitter();
        HandleSteering();
        HandleMotor();
        HandleTraction();
        ApplyDownforce();
        UpdateVisualWheels();
    }

    private void OnEnable()
    {
        if (accelerateButton != null) accelerateButton.action.Enable();
        if (brakeButton != null) brakeButton.action.Enable();

        if (switchCameraAction != null)
        {
            switchCameraAction.action.Enable();
            switchCameraAction.action.performed += ToggleCamera;
        }
    }

    private void OnDisable()
    {
        if (switchCameraAction != null)
        {
            switchCameraAction.action.performed -= ToggleCamera;
            switchCameraAction.action.Disable();
        }
    }

    private void ToggleCamera(InputAction.CallbackContext context)
    {
        isThirdPerson = !isThirdPerson;

        if (vrCameraLens != null) vrCameraLens.enabled = !isThirdPerson;
        if (thirdPersonCameraLens != null) thirdPersonCameraLens.enabled = isThirdPerson;

        ToggleAudioListeners();
    }

    private void ToggleAudioListeners()
    {
        if (vrCameraLens != null && vrCameraLens.TryGetComponent<AudioListener>(out AudioListener vrAudio))
            vrAudio.enabled = !isThirdPerson;

        if (thirdPersonCameraLens != null && thirdPersonCameraLens.TryGetComponent<AudioListener>(out AudioListener tpAudio))
            tpAudio.enabled = isThirdPerson;
    }

    private void ApplyDownforce()
    {
        float speedInMetersPerSecond = rb.linearVelocity.magnitude;
        float speedInKmh = speedInMetersPerSecond * 3.6f;

        if (speedInKmh > 30f)
        {
            float aeroForce = downforce * (speedInMetersPerSecond * speedInMetersPerSecond) / 100f;
            rb.AddForce(-transform.up * aeroForce);
        }
    }

    private void HandleSteering()
    {
        Vector3 localRight = transform.InverseTransformDirection(steeringWheel.right);
        float rawSteerAngle = Mathf.Atan2(localRight.y, localRight.x) * Mathf.Rad2Deg;
        float delta = rawSteerAngle - previousRawAngle;

        if (delta > 180f) revolutions--;
        else if (delta < -180f) revolutions++;

        previousRawAngle = rawSteerAngle;

        float continuousAngle = rawSteerAngle + (revolutions * 360f);

        continuousAngle = Mathf.Clamp(continuousAngle, -maxWheelRotation, maxWheelRotation);

        if (Mathf.Abs(continuousAngle) < steeringDeadzone)
        {
            continuousAngle = 0f;
        }

        float turnPercentage = continuousAngle / maxWheelRotation;
        float targetSteerAngle = -turnPercentage * maxSteeringAngle;

        currentSteerAngle = Mathf.Lerp(currentSteerAngle, targetSteerAngle, Time.deltaTime * steeringSpeed);

        frontLeft.steerAngle = currentSteerAngle;
        frontRight.steerAngle = currentSteerAngle;
    }

    private void HandleMotor()
    {
        if (inputLocked)
        {

            frontLeft.brakeTorque = maxBrakeTorque;
            frontRight.brakeTorque = maxBrakeTorque;
            rearLeft.brakeTorque = maxBrakeTorque;
            rearRight.brakeTorque = maxBrakeTorque;
            ResetMotorTorque();
            return;
        }

        float gasInput = accelerateButton != null ? accelerateButton.action.ReadValue<float>() : 0f;
        float brakeInput = brakeButton != null ? brakeButton.action.ReadValue<float>() : 0f;

        float currentSpeed = rb.linearVelocity.magnitude * 3.6f;

        float gearMultiplier = 1f;

        float activeTopSpeed = topSpeed;

        if (gearJoint != null)
        {
            if (gearJoint.angle > 10f)
            {
                gearMultiplier = -1f;
                activeTopSpeed = topSpeed * 0.3f;
            }
        }

        int targetGear = 1;

        if (gearShiftSpeeds != null && gearShiftSpeeds.Length > 0)
        {
            for (int i = 0; i < gearShiftSpeeds.Length; i++)
            {
                if (currentSpeed >= gearShiftSpeeds[i])
                {
                    targetGear = i + 2;
                }
            }
        }

        if (targetGear < currentGear)
        {
            currentGear = targetGear;
        }

        if (targetGear > currentGear && !isShifting && gearMultiplier > 0)
        {
            StartCoroutine(ShiftGearUp(targetGear));
        }

        if (brakeInput > 0.05f)
        {

            float appliedBrakeForce = brakeInput * maxBrakeTorque;

            frontLeft.brakeTorque = appliedBrakeForce;
            frontRight.brakeTorque = appliedBrakeForce;
            rearLeft.brakeTorque = appliedBrakeForce;
            rearRight.brakeTorque = appliedBrakeForce;

            ResetMotorTorque();
        }
        else if (gasInput > 0.1f)
        {

            frontLeft.brakeTorque = 0f;
            frontRight.brakeTorque = 0f;
            rearLeft.brakeTorque = 0f;
            rearRight.brakeTorque = 0f;

            if (currentSpeed < activeTopSpeed)
            {

                float thrust = gasInput * maxMotorTorque * gearMultiplier;

                if (isShifting) thrust = 0f;

                if (driveType == DriveType.FWD || driveType == DriveType.AWD)
                {
                    frontLeft.motorTorque = thrust;
                    frontRight.motorTorque = thrust;
                }
                if (driveType == DriveType.RWD || driveType == DriveType.AWD)
                {
                    rearLeft.motorTorque = thrust;
                    rearRight.motorTorque = thrust;
                }
            }
            else
            {
                ResetMotorTorque();
            }
        }
        else
        {

            ResetMotorTorque();
            frontLeft.brakeTorque = 0f;
            frontRight.brakeTorque = 0f;
            rearLeft.brakeTorque = 0f;
            rearRight.brakeTorque = 0f;
        }
    }

    private void ResetMotorTorque()
    {
        frontLeft.motorTorque = 0f;
        frontRight.motorTorque = 0f;
        rearLeft.motorTorque = 0f;
        rearRight.motorTorque = 0f;
    }

    private void HandleAntiJitter()
    {

        if (steeringWheelGrab != null && wheelJoint != null)
        {
            if (steeringWheelGrab.isSelected)
            {
                wheelJoint.useSpring = false;
                wheelJoint.useMotor = false;
            }
            else
            {
                float continuousAngle = previousRawAngle + (revolutions * 360f);

                if (float.IsNaN(continuousAngle) || float.IsInfinity(continuousAngle))
                {
                    continuousAngle = 0f;
                }

                if (Mathf.Abs(continuousAngle) > 2f)
                {
                    wheelJoint.useSpring = false;

                    JointMotor motor = wheelJoint.motor;

                    motor.force = 0.5f;

                    float speed = continuousAngle * 5f;

                    motor.targetVelocity = Mathf.Clamp(speed, -400f, 400f);
                    motor.freeSpin = false;

                    wheelJoint.motor = motor;
                    wheelJoint.useMotor = true;
                }
                else
                {
                    wheelJoint.useMotor = false;

                    JointSpring spring = wheelJoint.spring;
                    spring.spring = 60f;
                    spring.damper = 20f;
                    spring.targetPosition = 0f;

                    wheelJoint.spring = spring;
                    wheelJoint.useSpring = true;
                }
            }
        }

        if (gearShiftGrab != null && gearJoint != null)
        {
            if (gearShiftGrab.isSelected)
            {
                gearJoint.useSpring = false;
                gearWasGrabbed = true;
            }
            else
            {
                if (gearWasGrabbed)
                {
                    savedGearAngle = gearJoint.angle;
                    gearWasGrabbed = false;
                }

                JointSpring gearSpring = gearJoint.spring;
                gearSpring.spring = 10000f;
                gearSpring.damper = 1000f;
                gearSpring.targetPosition = savedGearAngle;
                gearJoint.spring = gearSpring;
                gearJoint.useSpring = true;
            }
        }
    }

    private void HandleTraction()
    {
        float currentSpeed = rb.linearVelocity.magnitude * 3.6f;

        float currentGrip = 1f;

        if (currentSpeed > slipThresholdSpeed)
        {
            float speedRange = topSpeed - slipThresholdSpeed;
            float excessSpeed = currentSpeed - slipThresholdSpeed;
            float slipPercentage = Mathf.Clamp01(excessSpeed / speedRange);

            currentGrip = Mathf.Lerp(1f, highSpeedGrip, slipPercentage);
        }

        ApplyGripToWheel(frontLeft, currentGrip);
        ApplyGripToWheel(frontRight, currentGrip);
        ApplyGripToWheel(rearLeft, currentGrip);
        ApplyGripToWheel(rearRight, currentGrip);
    }

    private void ApplyGripToWheel(WheelCollider wheel, float gripFactor)
    {
        WheelFrictionCurve sidewaysFriction = wheel.sidewaysFriction;
        sidewaysFriction.stiffness = gripFactor;
        wheel.sidewaysFriction = sidewaysFriction;
    }

    private void UpdateVisualWheels()
    {
        SyncWheel(frontLeft, visualFrontLeft);
        SyncWheel(frontRight, visualFrontRight);
        SyncWheel(rearLeft, visualRearLeft);
        SyncWheel(rearRight, visualRearRight);
    }

    private void SyncWheel(WheelCollider collider, Transform visualMesh)
    {
        if (visualMesh == null) return;

        Vector3 position;
        Quaternion rotation;
        collider.GetWorldPose(out position, out rotation);

        visualMesh.position = position;
        visualMesh.rotation = rotation;
    }

    private System.Collections.IEnumerator ShiftGearUp(int targetGear)
    {
        isShifting = true;

        yield return new WaitForSeconds(shiftDelay);

        currentGear = targetGear;
        isShifting = false;
    }
}