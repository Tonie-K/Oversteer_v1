using UnityEngine;

/// <summary>Third-person follow camera with speed response, drift framing, and collision avoidance.</summary>
[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Camera))]
public sealed class VehicleCameraController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [SerializeField] private Rigidbody targetBody;

    [Header("Follow")]
    [SerializeField, Min(0.5f)] private float followDistance = 7.5f;
    [SerializeField, Min(0f)] private float followHeight = 3.6f;
    [SerializeField, Min(0f)] private float pivotHeight = 1.15f;
    [SerializeField, Min(0f)] private float lookAtHeight = 1.1f;
    [SerializeField, Min(0f)] private float lookAheadDistance = 4.5f;
    [SerializeField, Min(0f)] private float positionSmoothTime = 0.16f;
    [SerializeField, Min(0f)] private float rotationDamping = 6f;
    [SerializeField, Min(0f)] private float lookTargetDamping = 8f;

    [Header("Speed response")]
    [SerializeField, Min(1f)] private float speedForMaximumResponse = 30f;
    [SerializeField, Min(0f)] private float highSpeedDistanceIncrease = 2f;
    [SerializeField, Min(0f)] private float highSpeedHeightIncrease = 0.5f;
    [SerializeField, Min(0f)] private float highSpeedLookAheadIncrease = 5f;
    [SerializeField, Min(0f)] private float highSpeedFieldOfViewIncrease = 8f;

    [Header("Drift framing")]
    [SerializeField, Min(0f)] private float driftSpeedForMaximumOffset = 7f;
    [SerializeField, Min(0f)] private float driftCameraSideOffset = 0.65f;
    [SerializeField, Min(0f)] private float driftLookOffset = 0.55f;
    [SerializeField, Range(0f, 1f)] private float driftMovementLookInfluence = 0.35f;

    [Header("Collision avoidance")]
    [SerializeField] private LayerMask collisionLayers = ~0;
    [SerializeField, Min(0.05f)] private float collisionProbeRadius = 0.25f;
    [SerializeField, Min(0f)] private float collisionPadding = 0.2f;
    [SerializeField, Min(0.1f)] private float minimumCollisionDistance = 0.45f;

    private Camera controlledCamera;
    private Vector3 positionVelocity;
    private Vector3 smoothedLookTarget;
    private bool initialized;
    private float baseFieldOfView;
    private readonly RaycastHit[] collisionHits = new RaycastHit[16];

    private void Awake()
    {
        controlledCamera = GetComponent<Camera>();
        baseFieldOfView = controlledCamera.fieldOfView;
        ResolveTarget();
    }

    private void Start()
    {
        if (target == null) return;

        Vector3 desiredPosition = GetDesiredPosition(out Vector3 lookTarget);
        transform.position = desiredPosition;
        transform.rotation = Quaternion.LookRotation(lookTarget - desiredPosition, Vector3.up);
        smoothedLookTarget = lookTarget;
        initialized = true;
    }

    private void LateUpdate()
    {
        if (target == null)
        {
            ResolveTarget();
            if (target == null) return;
        }

        Vector3 desiredPosition = GetDesiredPosition(out Vector3 desiredLookTarget);
        if (!initialized)
        {
            transform.position = desiredPosition;
            smoothedLookTarget = desiredLookTarget;
            initialized = true;
        }
        else
        {
            // Tighten position damping while an obstacle has pushed the camera inward.
            float smoothing = positionSmoothTime;
            if (Vector3.Distance(desiredPosition, transform.position) > followDistance * 0.5f)
                smoothing *= 0.55f;

            transform.position = Vector3.SmoothDamp(
                transform.position, desiredPosition, ref positionVelocity,
                Mathf.Max(0.001f, smoothing));
            smoothedLookTarget = Vector3.Lerp(
                smoothedLookTarget, desiredLookTarget,
                1f - Mathf.Exp(-lookTargetDamping * Time.deltaTime));
        }

        Vector3 lookDirection = smoothedLookTarget - transform.position;
        if (lookDirection.sqrMagnitude > 0.001f)
        {
            Quaternion wantedRotation = Quaternion.LookRotation(lookDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, wantedRotation,
                1f - Mathf.Exp(-rotationDamping * Time.deltaTime));
        }

        if (controlledCamera != null)
        {
            float speed01 = GetSpeed01();
            float wantedFov = baseFieldOfView + highSpeedFieldOfViewIncrease * speed01;
            controlledCamera.fieldOfView = Mathf.Lerp(
                controlledCamera.fieldOfView, wantedFov,
                1f - Mathf.Exp(-rotationDamping * Time.deltaTime));
        }
    }

    private Vector3 GetDesiredPosition(out Vector3 lookTarget)
    {
        Vector3 heading = Vector3.ProjectOnPlane(target.forward, Vector3.up).normalized;
        if (heading.sqrMagnitude < 0.001f) heading = Vector3.forward;

        Quaternion headingRotation = Quaternion.LookRotation(heading, Vector3.up);
        Vector3 planarVelocity = targetBody != null
            ? Vector3.ProjectOnPlane(targetBody.linearVelocity, Vector3.up)
            : Vector3.zero;
        float speed = planarVelocity.magnitude;
        float speed01 = Mathf.Clamp01(speed / speedForMaximumResponse);
        float lateralSpeed = Vector3.Dot(planarVelocity, target.right);
        float drift01 = Mathf.Clamp(lateralSpeed / driftSpeedForMaximumOffset, -1f, 1f);
        float driftMagnitude = Mathf.Abs(drift01);

        float distance = followDistance + highSpeedDistanceIncrease * speed01;
        float height = followHeight + highSpeedHeightIncrease * speed01;
        Vector3 pivot = target.position + Vector3.up * pivotHeight;
        Vector3 cameraSideOffset = target.right * (drift01 * driftCameraSideOffset);
        Vector3 wantedPosition = pivot + headingRotation * new Vector3(0f, height - pivotHeight, -distance)
            + cameraSideOffset;

        Vector3 movementDirection = speed > 0.1f ? planarVelocity / speed : heading;
        Vector3 lookDirection = Vector3.Slerp(heading, movementDirection,
            driftMagnitude * driftMovementLookInfluence).normalized;
        float lookAhead = lookAheadDistance + highSpeedLookAheadIncrease * speed01;
        lookTarget = target.position + Vector3.up * lookAtHeight
            + lookDirection * lookAhead
            + target.right * (drift01 * driftLookOffset);

        return PreventCollision(pivot, wantedPosition);
    }

    private Vector3 PreventCollision(Vector3 pivot, Vector3 wantedPosition)
    {
        Vector3 offset = wantedPosition - pivot;
        float wantedDistance = offset.magnitude;
        if (wantedDistance < 0.001f) return wantedPosition;

        Vector3 direction = offset / wantedDistance;
        int hitCount = Physics.SphereCastNonAlloc(
            pivot, collisionProbeRadius, direction, collisionHits, wantedDistance,
            collisionLayers, QueryTriggerInteraction.Ignore);

        float nearestDistance = wantedDistance;
        bool foundObstacle = false;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = collisionHits[i].collider;
            if (hitCollider == null || IsPartOfTarget(hitCollider.transform)) continue;
            if (collisionHits[i].distance < nearestDistance)
            {
                nearestDistance = collisionHits[i].distance;
                foundObstacle = true;
            }
        }

        if (!foundObstacle) return wantedPosition;

        float safeDistance = Mathf.Max(minimumCollisionDistance, nearestDistance - collisionPadding);
        return pivot + direction * Mathf.Min(safeDistance, wantedDistance);
    }

    private bool IsPartOfTarget(Transform candidate)
    {
        return candidate == target || candidate.IsChildOf(target);
    }

    private float GetSpeed01()
    {
        if (targetBody == null) return 0f;
        return Mathf.Clamp01(Vector3.ProjectOnPlane(targetBody.linearVelocity, Vector3.up).magnitude
            / speedForMaximumResponse);
    }

    private void ResolveTarget()
    {
        if (target == null)
        {
            GameObject player = GameObject.Find("PlayerCar");
            if (player == null)
            {
                try { player = GameObject.FindGameObjectWithTag("Player"); }
                catch (UnityException) { /* The project may not define a Player tag. */ }
            }

            if (player != null) target = player.transform;
        }

        if (target != null && targetBody == null)
            targetBody = target.GetComponent<Rigidbody>();
    }

    private void OnValidate()
    {
        followDistance = Mathf.Max(0.5f, followDistance);
        speedForMaximumResponse = Mathf.Max(1f, speedForMaximumResponse);
        driftSpeedForMaximumOffset = Mathf.Max(0.1f, driftSpeedForMaximumOffset);
    }
}
