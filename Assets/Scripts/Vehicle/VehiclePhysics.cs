using UnityEngine;

/// <summary>Configurable rigidbody forces for a controllable arcade-drift vehicle.</summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class VehiclePhysics : MonoBehaviour
{
    [Header("Powertrain")]
    [SerializeField, Min(1f)] private float maximumSpeed = 55f;
    [SerializeField, Min(1f)] private float maximumReverseSpeed = 12f;
    [SerializeField, Min(0f)] private float acceleration = 9000f;
    [SerializeField, Min(0f)] private float brakingForce = 15000f;

    [Header("Steering")]
    [SerializeField, Range(0f, 50f)] private float steeringSensitivity = 22f;
    [SerializeField, Min(0.01f)] private float steeringResponse = 3.5f;
    [SerializeField, Range(0.1f, 1f)] private float highSpeedSteeringFactor = 0.28f;
    [SerializeField, Min(1f)] private float steeringFadeSpeed = 20f;
    [SerializeField, Min(0.5f)] private float wheelbase = 2.65f;
    [SerializeField, Min(0f)] private float yawResponse = 4.5f;
    [SerializeField, Min(0.1f)] private float maximumYawRate = 2.4f;
    [SerializeField, Range(0f, 1f)] private float handbrakeYawBoost = 0.4f;
    [SerializeField, Range(0f, 2f)] private float countersteeringAssistance = 0.65f;

    [Header("Traction and drift")]
    [SerializeField, Min(0f)] private float frontTireGrip = 4.8f;
    [SerializeField, Min(0f)] private float rearTireGrip = 4f;
    [SerializeField, Range(0.1f, 2f)] private float normalTraction = 1.08f;
    [SerializeField, Range(0.05f, 1f)] private float driftTraction = 0.62f;
    [SerializeField, Range(0f, 1f)] private float handbrakeTractionReduction = 0.74f;
    [SerializeField, Min(0f)] private float lateralFriction = 0.92f;
    [SerializeField, Range(0f, 1f)] private float driftSlipStartRatio = 0.14f;
    [SerializeField, Min(0f)] private float rearTractionEngagementRate = 6f;
    [SerializeField, Min(0f)] private float driftRecoveryStrength = 2.2f;
    [SerializeField, Range(0f, 1f)] private float handbrakeStrength = 0.82f;
    [SerializeField, Range(0f, 1f)] private float handbrakeBrakeScale = 0.08f;
    [SerializeField, Min(0f)] private float rollingResistance = 1.6f;

    [Header("Stability")]
    [SerializeField, Min(0f)] private float yawDamping = 2.2f;
    [SerializeField, Min(0f)] private float uprightStrength = 12f;
    [SerializeField, Min(0f)] private float uprightDamping = 4f;
    [SerializeField, Min(0f)] private float downforce = 35f;
    [SerializeField, Min(0f)] private float groundAngularDrag = 1.3f;

    private Rigidbody body;
    private float currentSteerAngle;
    private float currentRearTractionScale = 1f;

    public float CurrentSteerAngle => currentSteerAngle;
    public float ForwardSpeed { get; private set; }
    public float SpeedMetersPerSecond => body == null ? 0f : body.linearVelocity.magnitude;
    public float SpeedKilometersPerHour => SpeedMetersPerSecond * 3.6f;

    public void SetDownforce(float value)
    {
        downforce = Mathf.Max(0f, value);
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    public void Simulate(VehicleInputHandler.VehicleInput input, bool grounded)
    {
        if (body == null) return;

        Vector3 velocity = body.linearVelocity;
        ForwardSpeed = Vector3.Dot(velocity, transform.forward);
        float speed01 = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / steeringFadeSpeed);
        float steeringScale = Mathf.Lerp(1f, highSpeedSteeringFactor, speed01);
        float targetAngle = input.Steering * steeringSensitivity * steeringScale;
        currentSteerAngle = Mathf.MoveTowards(currentSteerAngle, targetAngle, steeringResponse * 90f * Time.fixedDeltaTime);

        if (!grounded)
        {
            ApplyAirStability();
            return;
        }

        ApplyLongitudinalForces(input);
        UpdateRearTraction(input.Handbrake);
        ApplyTireGrip();
        ApplySteering(input);
        ApplyStability();
        body.AddForce(-transform.up * (downforce * velocity.sqrMagnitude * 0.01f), ForceMode.Force);
        body.angularDamping = Mathf.Max(groundAngularDrag, 0f);
    }

    private void ApplyLongitudinalForces(VehicleInputHandler.VehicleInput input)
    {
        float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
        if (input.Accelerate && forwardSpeed < maximumSpeed)
        {
            body.AddForce(transform.forward * acceleration, ForceMode.Force);
        }

        if (input.BrakeOrReverse)
        {
            if (forwardSpeed > 0.5f)
            {
                Vector3 forwardVelocity = Vector3.Project(body.linearVelocity, transform.forward);
                body.AddForce(-forwardVelocity.normalized * brakingForce, ForceMode.Force);
            }
            else if (forwardSpeed > -maximumReverseSpeed)
            {
                // S brakes while moving forward, then applies a distinct reverse drive force.
                // Do not use a signed brake force here: after stopping, it must continue to
                // accelerate the vehicle in the negative-forward direction.
                body.AddForce(-transform.forward * acceleration * 0.58f, ForceMode.Force);
            }
        }

        if (!input.Accelerate && !input.BrakeOrReverse)
        {
            Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
            body.AddForce(-planarVelocity * rollingResistance, ForceMode.Force);
        }

        if (input.Handbrake)
        {
            Vector3 forwardVelocity = Vector3.Project(body.linearVelocity, transform.forward);
            body.AddForce(-forwardVelocity.normalized * brakingForce * handbrakeStrength * handbrakeBrakeScale, ForceMode.Force);
        }
    }

    private void UpdateRearTraction(bool handbrake)
    {
        Vector3 planarVelocity = Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
        float speed = planarVelocity.magnitude;
        float lateralRatio = speed > 0.1f
            ? Mathf.Abs(Vector3.Dot(planarVelocity, transform.right)) / speed
            : 0f;

        float targetScale;
        if (handbrake)
        {
            targetScale = 1f - handbrakeTractionReduction;
        }
        else
        {
            // Keep rear grip reduced while meaningful slip remains, then blend back to
            // normal traction as the vehicle settles instead of snapping on release.
            float driftAmount = Mathf.InverseLerp(driftSlipStartRatio, 0.55f, lateralRatio);
            targetScale = Mathf.Lerp(1f, driftTraction, driftAmount);
        }

        float changeRate = handbrake ? rearTractionEngagementRate : driftRecoveryStrength;
        currentRearTractionScale = Mathf.MoveTowards(
            currentRearTractionScale, targetScale, changeRate * Time.fixedDeltaTime);
    }

    private void ApplySteering(VehicleInputHandler.VehicleInput input)
    {
        float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
        if (Mathf.Abs(forwardSpeed) < 0.15f || Mathf.Abs(currentSteerAngle) < 0.05f)
            return;

        float steerRadians = currentSteerAngle * Mathf.Deg2Rad;
        float desiredYawRate = (forwardSpeed / Mathf.Max(0.5f, wheelbase)) * Mathf.Tan(steerRadians);

        // A handbrake reduces rear lateral grip (above) so the rear can rotate outward;
        // a small yaw-rate increase preserves a controllable, steer-directed drift.
        if (input.Handbrake)
            desiredYawRate *= 1f + handbrakeStrength * handbrakeYawBoost;

        desiredYawRate = Mathf.Clamp(desiredYawRate, -maximumYawRate, maximumYawRate);
        float currentYawRate = Vector3.Dot(body.angularVelocity, transform.up);
        body.AddTorque(transform.up * ((desiredYawRate - currentYawRate) * yawResponse), ForceMode.Acceleration);
    }

    private void ApplyTireGrip()
    {
        Vector3 frontPoint = transform.position + transform.forward * 1.25f;
        Vector3 rearPoint = transform.position - transform.forward * 1.2f;
        Vector3 frontVelocity = body.GetPointVelocity(frontPoint);
        Vector3 rearVelocity = body.GetPointVelocity(rearPoint);

        Vector3 steeredRight = Quaternion.AngleAxis(currentSteerAngle, transform.up) * transform.right;
        float frontLateralSpeed = Vector3.Dot(frontVelocity, steeredRight);
        float rearLateralSpeed = Vector3.Dot(rearVelocity, transform.right);

        float lateralSpeed = Vector3.Dot(body.linearVelocity, transform.right);
        bool countersteering = Mathf.Abs(currentSteerAngle) > 0.05f
            && currentSteerAngle * lateralSpeed < 0f;
        float countersteerAmount = countersteering
            ? Mathf.Clamp01(Mathf.Abs(currentSteerAngle) / Mathf.Max(1f, steeringSensitivity))
                * Mathf.Clamp01(Mathf.Abs(lateralSpeed) / 3f)
            : 0f;
        float countersteerGrip = 1f + countersteeringAssistance * countersteerAmount;

        float frontShare = 0.56f;
        float rearShare = 1f - frontShare;
        float frontTractionScale = normalTraction * lateralFriction * countersteerGrip;
        float rearTractionScale = normalTraction * lateralFriction * currentRearTractionScale;
        float frontGripResponse = frontTireGrip * body.mass * frontShare * frontTractionScale;
        float frontMaximumForce = body.mass * 9.81f * frontShare * 1.25f * frontTractionScale;
        float rearGripResponse = rearTireGrip * body.mass * rearShare * rearTractionScale;
        float rearMaximumForce = body.mass * 9.81f * rearShare * 1.2f * rearTractionScale;

        Vector3 frontForce = Vector3.ClampMagnitude(-steeredRight * frontLateralSpeed * frontGripResponse, frontMaximumForce);
        Vector3 rearForce = Vector3.ClampMagnitude(-transform.right * rearLateralSpeed * rearGripResponse, rearMaximumForce);
        body.AddForceAtPosition(frontForce, frontPoint, ForceMode.Force);
        body.AddForceAtPosition(rearForce, rearPoint, ForceMode.Force);
    }

    private void ApplyStability()
    {
        Vector3 angularVelocity = body.angularVelocity;
        body.AddTorque(-transform.up * (angularVelocity.y * yawDamping), ForceMode.Acceleration);

        Vector3 tiltAxis = Vector3.Cross(transform.up, Vector3.up);
        Vector3 tiltAngularVelocity = Vector3.ProjectOnPlane(angularVelocity, Vector3.up);
        body.AddTorque((tiltAxis * uprightStrength) - (tiltAngularVelocity * uprightDamping), ForceMode.Acceleration);
    }

    private void ApplyAirStability()
    {
        Vector3 angularVelocity = body.angularVelocity;
        body.AddTorque(-Vector3.ProjectOnPlane(angularVelocity, Vector3.up) * uprightDamping, ForceMode.Acceleration);
    }

    private void OnValidate()
    {
        steeringFadeSpeed = Mathf.Max(1f, steeringFadeSpeed);
        driftSlipStartRatio = Mathf.Clamp(driftSlipStartRatio, 0f, 0.54f);
    }
}
