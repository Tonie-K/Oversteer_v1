using UnityEngine;

/// <summary>Coordinates input, ground state, physics, telemetry, and reset behavior.</summary>
[RequireComponent(typeof(Rigidbody), typeof(VehicleInputHandler), typeof(VehiclePhysics))]
[RequireComponent(typeof(VehicleGroundCheck), typeof(WheelVisualController))]
public sealed class VehicleController : MonoBehaviour
{
    [Header("Vehicle setup")]
    [SerializeField, Min(100f)] private float vehicleMass = 1250f;
    [SerializeField, Min(0f)] private float drag = 0.08f;
    [SerializeField, Range(0f, 1f)] private float chassisFriction = 0.04f;
    [SerializeField] private bool captureStartPoseOnAwake = true;
    [SerializeField] private Vector3 resetPosition;
    [SerializeField] private Vector3 resetEulerAngles;

    private Rigidbody body;
    private VehicleInputHandler inputHandler;
    private VehiclePhysics vehiclePhysics;
    private VehicleGroundCheck groundCheck;
    private Vector3 startPosition;
    private Quaternion startRotation;
    private PhysicsMaterial runtimeChassisMaterial;

    public float SpeedMetersPerSecond => body == null ? 0f : body.linearVelocity.magnitude;
    public float SpeedKilometersPerHour => SpeedMetersPerSecond * 3.6f;
    public bool IsGrounded => groundCheck != null && groundCheck.IsGrounded;

private void Awake()
    {
        body = GetComponent<Rigidbody>();
        inputHandler = GetComponent<VehicleInputHandler>();
        vehiclePhysics = GetComponent<VehiclePhysics>();
        groundCheck = GetComponent<VehicleGroundCheck>();

        body.mass = vehicleMass;
        body.linearDamping = drag;
        body.angularDamping = 1.3f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.centerOfMass = new Vector3(0f, 0.55f, -0.08f);

        // This vehicle has no WheelColliders: tire grip is supplied by VehiclePhysics.
        // Keep the large chassis collider from fighting that model with default static friction.
        Collider chassisCollider = GetComponent<Collider>();
        bool hasDefaultMaterial = chassisCollider != null && chassisCollider.sharedMaterial != null &&
            string.IsNullOrEmpty(chassisCollider.sharedMaterial.name) &&
            Mathf.Approximately(chassisCollider.sharedMaterial.staticFriction, 0.6f) &&
            Mathf.Approximately(chassisCollider.sharedMaterial.dynamicFriction, 0.6f);
        if (chassisCollider != null && (chassisCollider.sharedMaterial == null || hasDefaultMaterial))
        {
            runtimeChassisMaterial = new PhysicsMaterial(name + " Runtime Chassis");
            runtimeChassisMaterial.staticFriction = chassisFriction;
            runtimeChassisMaterial.dynamicFriction = chassisFriction;
            runtimeChassisMaterial.bounciness = 0f;
            runtimeChassisMaterial.frictionCombine = PhysicsMaterialCombine.Minimum;
            runtimeChassisMaterial.bounceCombine = PhysicsMaterialCombine.Minimum;
            chassisCollider.sharedMaterial = runtimeChassisMaterial;
        }

        startPosition = captureStartPoseOnAwake ? transform.position : resetPosition;
        startRotation = captureStartPoseOnAwake ? transform.rotation : Quaternion.Euler(resetEulerAngles);
    }

    private void Update()
    {
        if (inputHandler != null && inputHandler.Current.ResetPressed)
            ResetVehicle();
    }

    private void FixedUpdate()
    {
        if (vehiclePhysics != null && inputHandler != null && groundCheck != null)
            vehiclePhysics.Simulate(inputHandler.Current, groundCheck.IsGrounded);
    }

    public void ResetVehicle()
    {
        if (body == null) return;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = startPosition;
        body.rotation = startRotation;
        transform.SetPositionAndRotation(startPosition, startRotation);
        Physics.SyncTransforms();
    }

    private void OnDestroy()
    {
        if (runtimeChassisMaterial == null) return;

        Collider chassisCollider = GetComponent<Collider>();
        if (chassisCollider != null && chassisCollider.sharedMaterial == runtimeChassisMaterial)
            chassisCollider.sharedMaterial = null;

        Destroy(runtimeChassisMaterial);
    }
}
