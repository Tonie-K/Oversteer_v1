using UnityEngine;

/// <summary>Shared generation, shape, collision, and visual settings for procedural roads.</summary>
[CreateAssetMenu(menuName = "OVERSTEER/Road Configuration", fileName = "RoadConfiguration")]
public sealed class RoadConfiguration : ScriptableObject
{
    [Header("Generation")]
    [SerializeField] private int generationSeed = 271828;
    [SerializeField, Min(4)] private int pooledSegmentCount = 10;
    [SerializeField, Min(1)] private int segmentsAhead = 8;
    [SerializeField, Min(0)] private int segmentsBehind = 2;
    [SerializeField, Min(1)] private int initialStraightSegmentCount = 1;
    [SerializeField, Min(1)] private int samplesPerSegment = 16;
    [SerializeField, Min(0f)] private float recycleDistanceBehindPlayer = 28f;
    [SerializeField, Range(20f, 150f)] private float maximumHeadingDeviation = 105f;

    [Header("Road dimensions")]
    [SerializeField, Min(10f)] private float minimumSegmentLength = 32f;
    [SerializeField, Min(10f)] private float maximumSegmentLength = 48f;
    [SerializeField, Min(3f)] private float roadWidth = 12f;
    [SerializeField, Min(0f)] private float surfaceHeightOffset = 0.025f;
    [SerializeField, Min(1f)] private float textureMetersPerTile = 8f;

    [Header("Curve shape")]
    [SerializeField, Min(1f)] private float minimumCurveRadius = 48f;
    [SerializeField, Min(1f)] private float maximumCurveRadius = 115f;
    [SerializeField, Range(1f, 45f)] private float gentleCurveMinimumAngle = 8f;
    [SerializeField, Range(1f, 60f)] private float gentleCurveMaximumAngle = 17f;
    [SerializeField, Range(1f, 60f)] private float sharpCurveMinimumAngle = 24f;
    [SerializeField, Range(1f, 75f)] private float sharpCurveMaximumAngle = 38f;

    [Header("Roadside boundaries")]
    [SerializeField] private bool generateBoundaries = true;
    [SerializeField, Min(0.1f)] private float boundaryHeight = 0.65f;
    [SerializeField, Min(0.05f)] private float boundaryThickness = 0.4f;

    [Header("Rendering")]
    [SerializeField] private Material roadMaterial;
    [SerializeField] private Material boundaryMaterial;
    [SerializeField] private Color roadTint = new Color(0.27f, 0.29f, 0.32f, 1f);
    [SerializeField] private Color boundaryTint = new Color(0.42f, 0.44f, 0.47f, 1f);

    public int GenerationSeed => generationSeed;
    /// <summary>Includes the section containing the player plus sections beyond it.</summary>
    public int SegmentsAhead => Mathf.Max(1, segmentsAhead);
    public int SegmentsBehind => Mathf.Max(0, segmentsBehind);
    public int PooledSegmentCount => Mathf.Max(4, Mathf.Max(pooledSegmentCount, SegmentsAhead + SegmentsBehind));
    public int InitialStraightSegmentCount => Mathf.Clamp(initialStraightSegmentCount, 1, PooledSegmentCount);
    public int SamplesPerSegment => Mathf.Max(4, samplesPerSegment);
    public float RecycleDistanceBehindPlayer => Mathf.Max(0f, recycleDistanceBehindPlayer);
    public float MaximumHeadingDeviation => Mathf.Clamp(maximumHeadingDeviation, 20f, 150f);
    public float MinimumSegmentLength => Mathf.Max(10f, minimumSegmentLength);
    public float MaximumSegmentLength => Mathf.Max(MinimumSegmentLength, maximumSegmentLength);
    public float RoadWidth => Mathf.Max(3f, roadWidth);
    public float SurfaceHeightOffset => Mathf.Max(0f, surfaceHeightOffset);
    public float TextureMetersPerTile => Mathf.Max(1f, textureMetersPerTile);
    public float MinimumCurveRadius => Mathf.Max(RoadWidth * 1.75f, Mathf.Min(minimumCurveRadius, maximumCurveRadius));
    public float MaximumCurveRadius => Mathf.Max(MinimumCurveRadius, maximumCurveRadius);
    public float GentleCurveMinimumAngle => Mathf.Min(gentleCurveMinimumAngle, GentleCurveMaximumAngle);
    public float GentleCurveMaximumAngle => Mathf.Max(gentleCurveMinimumAngle, gentleCurveMaximumAngle);
    public float SharpCurveMinimumAngle => Mathf.Min(sharpCurveMinimumAngle, SharpCurveMaximumAngle);
    public float SharpCurveMaximumAngle => Mathf.Max(sharpCurveMinimumAngle, sharpCurveMaximumAngle);
    public bool GenerateBoundaries => generateBoundaries;
    public float BoundaryHeight => Mathf.Max(0.1f, boundaryHeight);
    public float BoundaryThickness => Mathf.Max(0.05f, boundaryThickness);
    public Material RoadMaterial => roadMaterial;
    public Material BoundaryMaterial => boundaryMaterial != null ? boundaryMaterial : roadMaterial;
    public Color RoadTint => roadTint;
    public Color BoundaryTint => boundaryTint;

    private void OnValidate()
    {
        pooledSegmentCount = Mathf.Max(4, pooledSegmentCount);
        segmentsAhead = Mathf.Max(1, segmentsAhead);
        segmentsBehind = Mathf.Max(0, segmentsBehind);
        pooledSegmentCount = Mathf.Max(pooledSegmentCount, segmentsAhead + segmentsBehind);
        samplesPerSegment = Mathf.Max(4, samplesPerSegment);
        minimumSegmentLength = Mathf.Max(10f, minimumSegmentLength);
        maximumSegmentLength = Mathf.Max(minimumSegmentLength, maximumSegmentLength);
        roadWidth = Mathf.Max(3f, roadWidth);
        minimumCurveRadius = Mathf.Max(roadWidth * 1.75f, minimumCurveRadius);
        maximumCurveRadius = Mathf.Max(minimumCurveRadius, maximumCurveRadius);
        gentleCurveMaximumAngle = Mathf.Max(gentleCurveMinimumAngle, gentleCurveMaximumAngle);
        sharpCurveMaximumAngle = Mathf.Max(sharpCurveMinimumAngle, sharpCurveMaximumAngle);
    }
}
