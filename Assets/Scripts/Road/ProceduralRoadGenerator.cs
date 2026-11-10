using System.Collections.Generic;
using UnityEngine;

/// <summary>Creates a seeded, continuously connected road and recycles a fixed segment pool.</summary>
[DefaultExecutionOrder(-200)]
public sealed class ProceduralRoadGenerator : MonoBehaviour
{
    [Header("Road setup")]
    [SerializeField] private RoadConfiguration configuration;
    [SerializeField] private Transform player;
    [SerializeField] private Vector3 startPosition = new Vector3(0f, 0f, -32f);
    [SerializeField] private Vector3 startDirection = Vector3.forward;
    [SerializeField] private GameObject[] legacyBoundariesToDisable;
    [SerializeField] private bool generateOnAwake = true;

    private readonly List<RoadSegment> segmentPool = new List<RoadSegment>();
    private readonly List<RoadSegment> activeSegments = new List<RoadSegment>();
    private bool[] originalBoundaryStates;
    private System.Random random;
    private Vector3 nextPosition;
    private Vector3 nextForward;
    private float headingDeviation;
    private int nextSequenceNumber;
    private RoadSegment.SegmentType previousType = RoadSegment.SegmentType.Straight;
    private bool initialized;

    public int PooledSegmentCount => segmentPool.Count;
    public int ActiveSegmentCount => activeSegments.Count;
    public int TotalSectionsGenerated => nextSequenceNumber;
    /// <summary>Active segments from the player's current section through the generated frontier.</summary>
    public int CurrentSegmentsAhead { get; private set; }
    /// <summary>Active sections behind the player's nearest section.</summary>
    public int CurrentSegmentsBehind { get; private set; }
    public int TargetSegmentsAhead => configuration != null ? configuration.SegmentsAhead : 0;
    public IReadOnlyList<RoadSegment> ActiveSegments => activeSegments;

    private void Awake()
    {
        if (generateOnAwake) GenerateRoad();
    }

    private void Update()
    {
        if (!initialized || player == null || activeSegments.Count == 0) return;
        MaintainRoadAroundPlayer();
    }

    public void GenerateRoad()
    {
        if (initialized || configuration == null) return;

        ResolvePlayer();
        DisableLegacyBoundaries();
        random = new System.Random(configuration.GenerationSeed);
        nextPosition = startPosition;
        nextForward = PlanarDirection(startDirection);
        headingDeviation = 0f;

        int poolCount = configuration.PooledSegmentCount;
        for (int i = 0; i < poolCount; i++)
        {
            var segmentObject = new GameObject($"RoadSegment_{i:00}");
            segmentObject.transform.SetParent(transform, false);
            segmentObject.transform.localPosition = Vector3.zero;
            segmentObject.transform.localRotation = Quaternion.identity;
            segmentObject.transform.localScale = Vector3.one;
            RoadSegment segment = segmentObject.AddComponent<RoadSegment>();
            segment.Initialize(configuration);
            segmentPool.Add(segment);
        }

        for (int i = 0; i < poolCount; i++)
        {
            RoadSegment segment = segmentPool[i];
            ConfigureNextSegment(segment, i < configuration.InitialStraightSegmentCount);
            activeSegments.Add(segment);
        }

        CurrentSegmentsAhead = activeSegments.Count;
        CurrentSegmentsBehind = 0;
        initialized = true;
    }

    /// <summary>Returns true when pooled sections form a continuous, correctly ordered path.</summary>
    public bool ValidateContinuity(float positionTolerance = 0.02f, float directionTolerance = 0.01f)
    {
        if (activeSegments.Count < 2) return false;
        for (int i = 0; i < activeSegments.Count - 1; i++)
        {
            RoadSegment current = activeSegments[i];
            RoadSegment next = activeSegments[i + 1];
            if (Vector3.Distance(current.EndPosition, next.StartPosition) > positionTolerance)
                return false;
            if (Vector3.Distance(current.EndForward, next.StartForward) > directionTolerance)
                return false;
        }
        return true;
    }

    private void MaintainRoadAroundPlayer()
    {
        int playerSegmentIndex = FindNearestSegmentIndex(player.position);
        int recycleLimit = segmentPool.Count;

        for (int recycled = 0; recycled < recycleLimit; recycled++)
        {
            if (playerSegmentIndex <= configuration.SegmentsBehind) break;

            RoadSegment oldest = activeSegments[0];
            float passedDistance = Vector3.Dot(player.position - oldest.EndPosition, oldest.EndForward);
            int segmentsAhead = activeSegments.Count - playerSegmentIndex;
            bool needsAheadCoverage = segmentsAhead < configuration.SegmentsAhead;
            if (!needsAheadCoverage && passedDistance < configuration.RecycleDistanceBehindPlayer) break;

            activeSegments.RemoveAt(0);
            RoadSegment last = activeSegments[activeSegments.Count - 1];
            ConfigureNextSegment(oldest, false, last.EndPosition, last.EndForward);
            activeSegments.Add(oldest);
            playerSegmentIndex--;
        }

        CurrentSegmentsBehind = playerSegmentIndex;
        CurrentSegmentsAhead = activeSegments.Count - playerSegmentIndex;
    }

    private int FindNearestSegmentIndex(Vector3 worldPosition)
    {
        int nearestIndex = 0;
        float nearestDistanceSquared = float.PositiveInfinity;
        for (int i = 0; i < activeSegments.Count; i++)
        {
            RoadSegment segment = activeSegments[i];
            if (segment == null) continue;
            segment.FindNearestCenterlinePoint(worldPosition,
                out _, out _, out float distanceSquared);
            if (distanceSquared >= nearestDistanceSquared) continue;
            nearestDistanceSquared = distanceSquared;
            nearestIndex = i;
        }
        return nearestIndex;
    }

    private void ConfigureNextSegment(RoadSegment segment, bool forceStraight,
        Vector3? connectionPosition = null, Vector3? connectionForward = null)
    {
        if (connectionPosition.HasValue) nextPosition = connectionPosition.Value;
        if (connectionForward.HasValue) nextForward = connectionForward.Value;

        RoadSegment.SegmentType type = forceStraight ? RoadSegment.SegmentType.Straight : ChooseSegmentType();
        float length = Range(configuration.MinimumSegmentLength, configuration.MaximumSegmentLength);
        float turnAngle = GetTurnAngle(type, length);

        if (Mathf.Abs(headingDeviation + turnAngle) > configuration.MaximumHeadingDeviation)
        {
            type = OppositeTurn(type);
            turnAngle = GetTurnAngle(type, length);
            if (Mathf.Abs(headingDeviation + turnAngle) > configuration.MaximumHeadingDeviation)
            {
                type = RoadSegment.SegmentType.Straight;
                turnAngle = 0f;
            }
        }

        segment.transform.position = nextPosition;
        segment.transform.rotation = Quaternion.identity;
        segment.Configure(configuration, nextPosition, nextForward, type, length,
            turnAngle, nextSequenceNumber++);

        nextPosition = segment.EndPosition;
        nextForward = segment.EndForward;
        headingDeviation += turnAngle;
        previousType = type;
    }

    private RoadSegment.SegmentType ChooseSegmentType()
    {
        // Ensure a new run visibly exercises all requested section families before
        // entering the fully seeded random sequence.
        int openingPatternIndex = nextSequenceNumber - configuration.InitialStraightSegmentCount;
        switch (openingPatternIndex)
        {
            case 0: return RoadSegment.SegmentType.GentleLeft;
            case 1: return RoadSegment.SegmentType.Straight;
            case 2: return RoadSegment.SegmentType.GentleRight;
            case 3: return RoadSegment.SegmentType.Straight;
            case 4: return RoadSegment.SegmentType.SharpLeft;
            case 5: return RoadSegment.SegmentType.Straight;
            case 6: return RoadSegment.SegmentType.SharpRight;
        }

        if (headingDeviation > configuration.MaximumHeadingDeviation * 0.72f)
            return RoadSegment.SegmentType.GentleLeft;
        if (headingDeviation < -configuration.MaximumHeadingDeviation * 0.72f)
            return RoadSegment.SegmentType.GentleRight;

        double choice = random.NextDouble();
        bool previousWasSharp = previousType == RoadSegment.SegmentType.SharpLeft
            || previousType == RoadSegment.SegmentType.SharpRight;

        if (choice < 0.42) return RoadSegment.SegmentType.Straight;
        if (choice < 0.67) return RoadSegment.SegmentType.GentleLeft;
        if (choice < 0.92) return RoadSegment.SegmentType.GentleRight;
        if (previousWasSharp) return choice < 0.96
            ? RoadSegment.SegmentType.GentleLeft
            : RoadSegment.SegmentType.GentleRight;
        return choice < 0.96 ? RoadSegment.SegmentType.SharpLeft : RoadSegment.SegmentType.SharpRight;
    }

    private float GetTurnAngle(RoadSegment.SegmentType type, float length)
    {
        bool gentle = type == RoadSegment.SegmentType.GentleLeft
            || type == RoadSegment.SegmentType.GentleRight;
        float sign = type == RoadSegment.SegmentType.GentleLeft || type == RoadSegment.SegmentType.SharpLeft
            ? -1f : 1f;
        if (type == RoadSegment.SegmentType.Straight) return 0f;

        float minAngle = gentle ? configuration.GentleCurveMinimumAngle : configuration.SharpCurveMinimumAngle;
        float maxAngle = gentle ? configuration.GentleCurveMaximumAngle : configuration.SharpCurveMaximumAngle;
        float radius = Range(configuration.MinimumCurveRadius, configuration.MaximumCurveRadius);
        float angleFromRadius = length / radius * Mathf.Rad2Deg;
        float magnitude = Mathf.Clamp(angleFromRadius, minAngle, maxAngle);
        return sign * magnitude;
    }

    private static RoadSegment.SegmentType OppositeTurn(RoadSegment.SegmentType type)
    {
        switch (type)
        {
            case RoadSegment.SegmentType.GentleLeft: return RoadSegment.SegmentType.GentleRight;
            case RoadSegment.SegmentType.GentleRight: return RoadSegment.SegmentType.GentleLeft;
            case RoadSegment.SegmentType.SharpLeft: return RoadSegment.SegmentType.SharpRight;
            case RoadSegment.SegmentType.SharpRight: return RoadSegment.SegmentType.SharpLeft;
            default: return RoadSegment.SegmentType.Straight;
        }
    }

    private float Range(float min, float max)
    {
        return min + (float)random.NextDouble() * (max - min);
    }

    private void ResolvePlayer()
    {
        if (player != null) return;
        GameObject candidate = GameObject.Find("PlayerCar");
        if (candidate == null)
        {
            try { candidate = GameObject.FindGameObjectWithTag("Player"); }
            catch (UnityException) { /* No Player tag is configured in this project. */ }
        }
        if (candidate != null) player = candidate.transform;
    }

    private void DisableLegacyBoundaries()
    {
        if (legacyBoundariesToDisable == null) return;
        originalBoundaryStates = new bool[legacyBoundariesToDisable.Length];
        for (int i = 0; i < legacyBoundariesToDisable.Length; i++)
        {
            GameObject boundary = legacyBoundariesToDisable[i];
            if (boundary == null) continue;
            originalBoundaryStates[i] = boundary.activeSelf;
            boundary.SetActive(false);
        }
    }

    private void RestoreLegacyBoundaries()
    {
        if (legacyBoundariesToDisable == null || originalBoundaryStates == null) return;
        for (int i = 0; i < legacyBoundariesToDisable.Length; i++)
        {
            if (legacyBoundariesToDisable[i] != null && originalBoundaryStates[i])
                legacyBoundariesToDisable[i].SetActive(true);
        }
    }

    private static Vector3 PlanarDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
    }

    private void OnDisable()
    {
        if (initialized) RestoreLegacyBoundaries();
    }
}
