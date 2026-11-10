using UnityEngine;

/// <summary>Ground contact sampling for the vehicle chassis.</summary>
public sealed class VehicleGroundCheck : MonoBehaviour
{
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField, Min(0.05f)] private float probeDistance = 0.42f;
    [SerializeField, Min(0f)] private float probeStartHeight = 0.12f;
    [SerializeField, Min(0f)] private float suspensionClearance = 0.12f;

    private Rigidbody body;
    private Collider[] vehicleColliders;

    public bool IsGrounded { get; private set; }
    public Vector3 GroundNormal { get; private set; } = Vector3.up;
    public int GroundedProbeCount { get; private set; }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        vehicleColliders = GetComponentsInChildren<Collider>();
    }

    private void FixedUpdate()
    {
        Vector3 center = transform.position;
        Vector3 right = transform.right;
        Vector3 forward = transform.forward;
        float halfWidth = 0.72f;
        float halfLength = 1.45f;

        if (vehicleColliders != null)
        {
            foreach (Collider vehicleCollider in vehicleColliders)
            {
                if (vehicleCollider == null || vehicleCollider.isTrigger) continue;
                Bounds bounds = vehicleCollider.bounds;
                halfWidth = Mathf.Max(halfWidth, Vector3.Project(bounds.extents, right).magnitude * 0.8f);
                halfLength = Mathf.Max(halfLength, Vector3.Project(bounds.extents, forward).magnitude * 0.8f);
            }
        }

        Vector3[] offsets =
        {
            Vector3.zero,
            right * halfWidth + forward * halfLength,
            right * halfWidth - forward * halfLength,
            -right * halfWidth + forward * halfLength,
            -right * halfWidth - forward * halfLength
        };

        GroundedProbeCount = 0;
        Vector3 normalSum = Vector3.zero;
        float castLength = probeStartHeight + probeDistance + suspensionClearance;

        foreach (Vector3 offset in offsets)
        {
            Vector3 origin = center + offset + Vector3.up * probeStartHeight;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, castLength, groundLayers, QueryTriggerInteraction.Ignore)
                && !BelongsToVehicle(hit.collider))
            {
                GroundedProbeCount++;
                normalSum += hit.normal;
            }
        }

        IsGrounded = GroundedProbeCount >= 2;
        GroundNormal = IsGrounded && normalSum.sqrMagnitude > 0.001f ? normalSum.normalized : Vector3.up;
    }

    private bool BelongsToVehicle(Collider candidate)
    {
        if (candidate == null) return false;
        return candidate.transform == transform || candidate.transform.IsChildOf(transform);
    }
}
