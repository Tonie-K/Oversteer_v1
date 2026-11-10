using UnityEngine;
using System.Collections.Generic;

/// <summary>Animates wheel meshes without adding wheel colliders or vehicle physics.</summary>
public sealed class WheelVisualController : MonoBehaviour
{
    [System.Serializable]
    private sealed class WheelVisual
    {
        public Transform wheel;
        public bool steer;
        public bool invertSpin;
        [HideInInspector] public Quaternion initialLocalRotation;
    }

    [SerializeField] private WheelVisual[] wheels = new WheelVisual[0];
    [SerializeField] private Vector3 localAxleAxis = Vector3.forward;
    [SerializeField] private float wheelRadius = 0.43f;

    private Rigidbody body;
    private VehiclePhysics vehiclePhysics;
    private float spinAngle;

    private void Awake()
    {
        body = GetComponentInParent<Rigidbody>();
        vehiclePhysics = GetComponentInParent<VehiclePhysics>();

        if (wheels == null || wheels.Length == 0)
        {
            List<WheelVisual> discovered = new List<WheelVisual>();
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child == transform) continue;
                string objectName = child.name.ToLowerInvariant();
                if (!objectName.StartsWith("wheel_front_") && !objectName.StartsWith("wheel_rear_")) continue;

                discovered.Add(new WheelVisual
                {
                    wheel = child,
                    steer = objectName.StartsWith("wheel_front_")
                });
            }
            wheels = discovered.ToArray();
        }

        foreach (WheelVisual wheel in wheels)
        {
            if (wheel != null && wheel.wheel != null)
                wheel.initialLocalRotation = wheel.wheel.localRotation;
        }
    }

    private void LateUpdate()
    {
        if (body == null || wheels == null || wheels.Length == 0) return;

        float forwardSpeed = Vector3.Dot(body.linearVelocity, transform.forward);
        spinAngle += forwardSpeed / Mathf.Max(0.05f, wheelRadius) * Mathf.Rad2Deg * Time.deltaTime;
        float steerAngle = vehiclePhysics != null ? vehiclePhysics.CurrentSteerAngle : 0f;

        foreach (WheelVisual wheel in wheels)
        {
            if (wheel == null || wheel.wheel == null) continue;
            float wheelSpin = wheel.invertSpin ? -spinAngle : spinAngle;
            Quaternion steering = wheel.steer ? Quaternion.Euler(0f, steerAngle, 0f) : Quaternion.identity;
            Quaternion spin = Quaternion.AngleAxis(wheelSpin, localAxleAxis.normalized);
            wheel.wheel.localRotation = wheel.initialLocalRotation * steering * spin;
        }
    }
}
