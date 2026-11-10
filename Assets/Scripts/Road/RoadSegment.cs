using System.Collections.Generic;
using UnityEngine;

/// <summary>One pooled, connected road section with a surface collider and optional edge barriers.</summary>
public sealed class RoadSegment : MonoBehaviour
{
    public enum SegmentType
    {
        Straight,
        GentleLeft,
        GentleRight,
        SharpLeft,
        SharpRight
    }

    [SerializeField] private SegmentType segmentType;
    [SerializeField] private int sequenceNumber;

    private readonly List<Vector3> centerline = new List<Vector3>();
    private RoadMeshGenerator roadMesh;
    private RoadBoundaryGenerator leftBoundary;
    private RoadBoundaryGenerator rightBoundary;
    private bool boundariesEnabled;

    public SegmentType Type => segmentType;
    public int SequenceNumber => sequenceNumber;
    public Vector3 StartPosition { get; private set; }
    public Vector3 EndPosition { get; private set; }
    public Vector3 StartForward { get; private set; }
    public Vector3 EndForward { get; private set; }
    public float Length { get; private set; }
    public float RoadWidth { get; private set; }
    public float BoundaryThickness { get; private set; }
    public bool HasBoundaries => boundariesEnabled;
    public IReadOnlyList<Vector3> Centerline => centerline;
    public MeshCollider SurfaceCollider => roadMesh != null ? roadMesh.GetComponent<MeshCollider>() : null;
    public MeshCollider LeftBoundaryCollider => leftBoundary != null ? leftBoundary.GetComponent<MeshCollider>() : null;
    public MeshCollider RightBoundaryCollider => rightBoundary != null ? rightBoundary.GetComponent<MeshCollider>() : null;

    public void Initialize(RoadConfiguration configuration)
    {
        RoadWidth = configuration.RoadWidth;
        BoundaryThickness = configuration.BoundaryThickness;
        roadMesh = CreateChild("RoadSurface").AddComponent<RoadMeshGenerator>();
        roadMesh.SetMaterial(configuration.RoadMaterial, configuration.RoadTint);

        boundariesEnabled = configuration.GenerateBoundaries;
        if (!boundariesEnabled) return;

        leftBoundary = CreateChild("RoadBoundary_Left").AddComponent<RoadBoundaryGenerator>();
        rightBoundary = CreateChild("RoadBoundary_Right").AddComponent<RoadBoundaryGenerator>();
        leftBoundary.SetMaterial(configuration.BoundaryMaterial, configuration.BoundaryTint);
        rightBoundary.SetMaterial(configuration.BoundaryMaterial, configuration.BoundaryTint);
    }

    public void Configure(RoadConfiguration configuration, Vector3 startPosition,
        Vector3 startForward, SegmentType type, float length, float signedTurnAngle,
        int sequenceNumber)
    {
        segmentType = type;
        this.sequenceNumber = sequenceNumber;
        StartPosition = startPosition;
        StartForward = PlanarDirection(startForward);
        Length = Mathf.Max(1f, length);

        BuildCenterline(configuration.SamplesPerSegment, signedTurnAngle);
        EndPosition = centerline[centerline.Count - 1];
        EndForward = signedTurnAngle == 0f
            ? StartForward
            : (Quaternion.AngleAxis(signedTurnAngle, Vector3.up) * StartForward).normalized;

        roadMesh.Build(centerline, StartForward, EndForward, configuration.RoadWidth,
            configuration.SurfaceHeightOffset, configuration.TextureMetersPerTile);

        if (boundariesEnabled)
        {
            leftBoundary.Build(centerline, StartForward, EndForward, configuration.RoadWidth,
                -1, configuration.BoundaryThickness, configuration.BoundaryHeight,
                configuration.SurfaceHeightOffset, configuration.TextureMetersPerTile);
            rightBoundary.Build(centerline, StartForward, EndForward, configuration.RoadWidth,
                1, configuration.BoundaryThickness, configuration.BoundaryHeight,
                configuration.SurfaceHeightOffset, configuration.TextureMetersPerTile);
        }
    }

    public Vector3 GetCenterlinePoint(int index)
    {
        return centerline[Mathf.Clamp(index, 0, centerline.Count - 1)];
    }

    public Vector3 GetCenterlineForward(int index)
    {
        int clamped = Mathf.Clamp(index, 0, centerline.Count - 1);
        if (clamped == 0) return StartForward;
        if (clamped == centerline.Count - 1) return EndForward;
        return PlanarDirection(centerline[clamped + 1] - centerline[clamped - 1]);
    }

    public void FindNearestCenterlinePoint(Vector3 position, out Vector3 nearestPoint,
        out Vector3 nearestForward, out float nearestDistanceSquared)
    {
        nearestPoint = StartPosition;
        nearestForward = StartForward;
        nearestDistanceSquared = float.PositiveInfinity;

        for (int i = 0; i < centerline.Count - 1; i++)
        {
            Vector3 start = centerline[i];
            Vector3 end = centerline[i + 1];
            Vector3 segment = end - start;
            segment.y = 0f;
            float segmentLengthSquared = segment.sqrMagnitude;
            float t = segmentLengthSquared > 0.0001f
                ? Mathf.Clamp01(Vector3.Dot(new Vector3(position.x - start.x, 0f, position.z - start.z), segment)
                    / segmentLengthSquared)
                : 0f;
            Vector3 candidate = Vector3.Lerp(start, end, t);
            Vector3 delta = position - candidate;
            delta.y = 0f;
            float distanceSquared = delta.sqrMagnitude;
            if (distanceSquared >= nearestDistanceSquared) continue;

            nearestDistanceSquared = distanceSquared;
            nearestPoint = candidate;
            nearestForward = PlanarDirection(segment);
        }
    }

    private void BuildCenterline(int sampleCount, float signedTurnAngle)
    {
        centerline.Clear();
        int count = Mathf.Max(4, sampleCount);
        Vector3 start = StartPosition;
        Vector3 forward = StartForward;

        if (Mathf.Abs(signedTurnAngle) < 0.01f)
        {
            for (int i = 0; i <= count; i++)
                centerline.Add(start + forward * (Length * (i / (float)count)));
            return;
        }

        float angleRadians = Mathf.Abs(signedTurnAngle) * Mathf.Deg2Rad;
        float radius = Length / angleRadians;
        float turnSign = Mathf.Sign(signedTurnAngle);
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 end = start + forward * (radius * Mathf.Sin(angleRadians))
            + right * (turnSign * radius * (1f - Mathf.Cos(angleRadians)));
        Vector3 endForward = (Quaternion.AngleAxis(signedTurnAngle, Vector3.up) * forward).normalized;
        float handleLength = 4f / 3f * radius * Mathf.Tan(angleRadians * 0.25f);
        Vector3 control1 = start + forward * handleLength;
        Vector3 control2 = end - endForward * handleLength;

        for (int i = 0; i <= count; i++)
        {
            float t = i / (float)count;
            float oneMinusT = 1f - t;
            Vector3 point = oneMinusT * oneMinusT * oneMinusT * start
                + 3f * oneMinusT * oneMinusT * t * control1
                + 3f * oneMinusT * t * t * control2
                + t * t * t * end;
            centerline.Add(point);
        }
    }

    private GameObject CreateChild(string childName)
    {
        var child = new GameObject(childName);
        child.transform.SetParent(transform, false);
        child.transform.localPosition = Vector3.zero;
        child.transform.localRotation = Quaternion.identity;
        child.transform.localScale = Vector3.one;
        return child;
    }

    private static Vector3 PlanarDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }
}
