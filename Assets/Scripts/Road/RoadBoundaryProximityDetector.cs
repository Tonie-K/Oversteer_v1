using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Measures clearance to generated road barriers and reports proximity/contact events.</summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public sealed class RoadBoundaryProximityDetector : MonoBehaviour
{
    public enum BoundaryState
    {
        NormalRoadDriving,
        NearBoundary,
        DangerousProximity,
        ActualCollision
    }

    public struct BoundaryStateEventData
    {
        public readonly BoundaryState PreviousState;
        public readonly BoundaryState State;
        public readonly float DistanceToBoundary;
        public readonly float VehicleSpeed;
        public readonly int BoundarySide;
        public readonly int SegmentSequence;

        internal BoundaryStateEventData(BoundaryState previousState, BoundaryState state,
            float distanceToBoundary, float vehicleSpeed, int boundarySide, int segmentSequence)
        {
            PreviousState = previousState;
            State = state;
            DistanceToBoundary = distanceToBoundary;
            VehicleSpeed = vehicleSpeed;
            BoundarySide = boundarySide;
            SegmentSequence = segmentSequence;
        }
    }

    public struct BoundaryCollisionEventData
    {
        public readonly RoadBoundaryGenerator Boundary;
        public readonly int BoundarySide;
        public readonly int SegmentSequence;
        public readonly float VehicleSpeed;
        public readonly float ImpactSpeed;
        public readonly float ImpactImpulse;
        public readonly Vector3 ContactPoint;

        internal BoundaryCollisionEventData(RoadBoundaryGenerator boundary, float vehicleSpeed,
            float impactSpeed, float impactImpulse, Vector3 contactPoint)
        {
            Boundary = boundary;
            BoundarySide = boundary != null ? boundary.Side : 0;
            RoadSegment segment = boundary != null ? boundary.Segment : null;
            SegmentSequence = segment != null ? segment.SequenceNumber : -1;
            VehicleSpeed = vehicleSpeed;
            ImpactSpeed = impactSpeed;
            ImpactImpulse = impactImpulse;
            ContactPoint = contactPoint;
        }
    }

    [Header("Proximity thresholds (vehicle-to-barrier surface clearance)")]
    [SerializeField, Min(0f)] private float nearBoundaryDistance = 3f;
    [SerializeField, Min(0f)] private float nearBoundaryExitDistance = 3.6f;
    [SerializeField, Min(0f)] private float dangerousProximityDistance = 1.1f;
    [SerializeField, Min(0f)] private float dangerousExitDistance = 1.55f;

    [Header("References")]
    [SerializeField] private ProceduralRoadGenerator roadGenerator;
    [SerializeField] private Collider vehicleCollider;
    [SerializeField] private Rigidbody vehicleBody;

    private readonly HashSet<Collider> boundaryContacts = new HashSet<Collider>();
    private BoundaryState state = BoundaryState.NormalRoadDriving;

    /// <summary>Raised when the boundary state changes. Intended for telemetry/scoring consumers.</summary>
    public event Action<BoundaryStateEventData> StateChanged;
    /// <summary>Raised once for each new physical contact with a generated roadside barrier.</summary>
    public event Action<BoundaryCollisionEventData> BoundaryCollision;

    public BoundaryState State => state;
    public float DistanceToNearestBoundary { get; private set; } = float.PositiveInfinity;
    public float NearestBoundaryDistance => DistanceToNearestBoundary;
    public RoadBoundaryGenerator NearestBoundary { get; private set; }
    public bool IsNearBoundary => state == BoundaryState.NearBoundary;
    public bool IsDangerousProximity => state == BoundaryState.DangerousProximity;
    public bool IsCollidingWithBoundary => boundaryContacts.Count > 0;
    public int BoundaryContactCount => boundaryContacts.Count;
    public float VehicleSpeed => vehicleBody != null ? vehicleBody.linearVelocity.magnitude : 0f;
    public float Proximity01 => float.IsInfinity(DistanceToNearestBoundary)
        ? 0f
        : 1f - Mathf.Clamp01(DistanceToNearestBoundary / Mathf.Max(0.01f, nearBoundaryExitDistance));

    private void Awake()
    {
        ResolveReferences();
    }

    private void FixedUpdate()
    {
        ResolveReferences();
        UpdateNearestBoundary();
        UpdateState();
    }

    private void OnCollisionEnter(Collision collision)
    {
        RegisterBoundaryContact(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        RegisterBoundaryContact(collision);
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision.collider != null)
            boundaryContacts.Remove(collision.collider);
    }

    private void RegisterBoundaryContact(Collision collision)
    {
        if (collision == null || collision.collider == null) return;
        RoadBoundaryGenerator boundary = collision.collider.GetComponentInParent<RoadBoundaryGenerator>();
        if (boundary == null || !boundaryContacts.Add(collision.collider)) return;

        Vector3 contactPoint = collision.contactCount > 0
            ? collision.GetContact(0).point
            : transform.position;
        BoundaryCollision?.Invoke(new BoundaryCollisionEventData(
            boundary, VehicleSpeed, collision.relativeVelocity.magnitude,
            collision.impulse.magnitude, contactPoint));
        SetState(BoundaryState.ActualCollision);
    }

    private void ResolveReferences()
    {
        if (vehicleBody == null) vehicleBody = GetComponent<Rigidbody>();
        if (vehicleCollider == null) vehicleCollider = GetComponent<Collider>();
        if (roadGenerator != null) return;

        GameObject generatorObject = GameObject.Find("ProceduralRoadGenerator");
        if (generatorObject != null)
            roadGenerator = generatorObject.GetComponent<ProceduralRoadGenerator>();
    }

    private void UpdateNearestBoundary()
    {
        DistanceToNearestBoundary = float.PositiveInfinity;
        NearestBoundary = null;
        if (roadGenerator == null || vehicleCollider == null) return;

        Vector3 vehicleCenter = vehicleCollider.bounds.center;
        IReadOnlyList<RoadSegment> segments = roadGenerator.ActiveSegments;
        RoadSegment nearestSegment = null;
        Vector3 nearestCenterlinePoint = Vector3.zero;
        Vector3 nearestForward = Vector3.forward;
        float nearestPathDistanceSquared = float.PositiveInfinity;

        for (int i = 0; i < segments.Count; i++)
        {
            RoadSegment segment = segments[i];
            if (segment == null || !segment.isActiveAndEnabled || !segment.HasBoundaries) continue;

            segment.FindNearestCenterlinePoint(vehicleCenter,
                out Vector3 centerlinePoint, out Vector3 forward, out float pathDistanceSquared);
            if (pathDistanceSquared >= nearestPathDistanceSquared) continue;

            nearestPathDistanceSquared = pathDistanceSquared;
            nearestSegment = segment;
            nearestCenterlinePoint = centerlinePoint;
            nearestForward = forward;
        }

        if (nearestSegment == null) return;

        Vector3 right = Vector3.Cross(Vector3.up, nearestForward).normalized;
        Vector3 planarOffset = vehicleCenter - nearestCenterlinePoint;
        planarOffset.y = 0f;
        float lateralOffset = Vector3.Dot(planarOffset, right);
        Bounds bounds = vehicleCollider.bounds;
        float vehicleHalfWidth = Mathf.Abs(right.x) * bounds.extents.x
            + Mathf.Abs(right.z) * bounds.extents.z;
        float combinedHalfWidth = nearestSegment.BoundaryThickness * 0.5f + vehicleHalfWidth;
        float halfRoadWidth = nearestSegment.RoadWidth * 0.5f;

        if (nearestSegment.LeftBoundaryCollider != null)
            ConsiderBoundary(nearestSegment.LeftBoundaryCollider, Mathf.Abs(-halfRoadWidth - lateralOffset)
                - combinedHalfWidth);
        if (nearestSegment.RightBoundaryCollider != null)
            ConsiderBoundary(nearestSegment.RightBoundaryCollider, Mathf.Abs(halfRoadWidth - lateralOffset)
                - combinedHalfWidth);
    }

    private void ConsiderBoundary(MeshCollider boundaryCollider, float surfaceClearance)
    {
        if (boundaryCollider == null || !boundaryCollider.enabled
            || !boundaryCollider.gameObject.activeInHierarchy || boundaryCollider.sharedMesh == null)
            return;

        float distance = Mathf.Max(0f, surfaceClearance);
        if (distance >= DistanceToNearestBoundary) return;

        DistanceToNearestBoundary = distance;
        NearestBoundary = boundaryCollider.GetComponent<RoadBoundaryGenerator>();
    }

    private void UpdateState()
    {
        if (boundaryContacts.Count > 0)
        {
            SetState(BoundaryState.ActualCollision);
            return;
        }

        BoundaryState targetState;
        switch (state)
        {
            case BoundaryState.NearBoundary:
                if (DistanceToNearestBoundary <= dangerousProximityDistance)
                    targetState = BoundaryState.DangerousProximity;
                else if (DistanceToNearestBoundary > nearBoundaryExitDistance)
                    targetState = BoundaryState.NormalRoadDriving;
                else
                    targetState = BoundaryState.NearBoundary;
                break;

            case BoundaryState.DangerousProximity:
                if (DistanceToNearestBoundary <= dangerousExitDistance)
                    targetState = BoundaryState.DangerousProximity;
                else if (DistanceToNearestBoundary <= nearBoundaryExitDistance)
                    targetState = BoundaryState.NearBoundary;
                else
                    targetState = BoundaryState.NormalRoadDriving;
                break;

            default:
                if (DistanceToNearestBoundary <= dangerousProximityDistance)
                    targetState = BoundaryState.DangerousProximity;
                else if (DistanceToNearestBoundary <= nearBoundaryDistance)
                    targetState = BoundaryState.NearBoundary;
                else
                    targetState = BoundaryState.NormalRoadDriving;
                break;
        }

        SetState(targetState);
    }

    private void SetState(BoundaryState nextState)
    {
        if (state == nextState) return;
        BoundaryState previous = state;
        state = nextState;
        int side = NearestBoundary != null ? NearestBoundary.Side : 0;
        RoadSegment segment = NearestBoundary != null ? NearestBoundary.Segment : null;
        StateChanged?.Invoke(new BoundaryStateEventData(previous, nextState,
            DistanceToNearestBoundary, VehicleSpeed, side,
            segment != null ? segment.SequenceNumber : -1));
    }

    private void OnValidate()
    {
        nearBoundaryDistance = Mathf.Max(0f, nearBoundaryDistance);
        nearBoundaryExitDistance = Mathf.Max(nearBoundaryDistance, nearBoundaryExitDistance);
        dangerousProximityDistance = Mathf.Clamp(dangerousProximityDistance, 0f, nearBoundaryDistance);
        dangerousExitDistance = Mathf.Max(dangerousProximityDistance, dangerousExitDistance);
    }
}
