using System;
using UnityEngine;

/// <summary>Scores valid drift lifecycles and exposes live/banked values for UI consumers.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DriftController))]
public sealed class DriftScoringSystem : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private DriftScoreConfiguration configuration;
    [SerializeField] private DriftController driftController;
    [SerializeField] private RoadBoundaryProximityDetector boundaryDetector;
    [SerializeField] private ComboSystem comboSystem;

    private bool driftInProgress;
    private bool driftInvalidated;
    private bool hadValidScoringSample;
    private float driftStartTime;
    private float accumulatedDriftScore;
    private int totalScore;

    public event Action<DriftScoringSystem> ScoreChanged;
    public event Action<int, int> DriftScoreBanked;

    /// <summary>Unbanked score earned by the current drift.</summary>
    public int CurrentDriftScore => Mathf.FloorToInt(accumulatedDriftScore);
    public int TotalScore => totalScore;
    public int ComboCount => comboSystem != null ? comboSystem.ComboCount : 0;
    public float ComboMultiplier => comboSystem != null ? comboSystem.CurrentComboMultiplier : 1f;
    public bool IsScoringDrift => driftInProgress && !driftInvalidated;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (driftController != null)
        {
            driftController.DriftStarted += HandleDriftStarted;
            driftController.DriftEnded += HandleDriftEnded;
        }
        if (boundaryDetector != null)
            boundaryDetector.BoundaryCollision += HandleBoundaryCollision;
    }

    private void OnDisable()
    {
        if (driftController != null)
        {
            driftController.DriftStarted -= HandleDriftStarted;
            driftController.DriftEnded -= HandleDriftEnded;
        }
        if (boundaryDetector != null)
            boundaryDetector.BoundaryCollision -= HandleBoundaryCollision;
    }

    private void FixedUpdate()
    {
        if (!driftInProgress || driftInvalidated || configuration == null
            || driftController == null || !driftController.IsDriftValid)
            return;

        float speed = driftController.VehicleSpeed;
        float angle = driftController.DriftAngle;
        if (speed < configuration.MinimumSpeed || angle < configuration.MinimumDriftAngle)
            return;

        float duration = Mathf.Max(0f, Time.time - driftStartTime);
        hadValidScoringSample = true;
        float rate = configuration.BasePointsPerSecond
            * GetAngleMultiplier(angle)
            * GetSpeedMultiplier(speed)
            * GetDurationMultiplier(duration)
            * GetProximityMultiplier()
            * ComboMultiplier;
        rate = Mathf.Min(rate, configuration.MaximumPointsPerSecond);

        float previousScore = accumulatedDriftScore;
        accumulatedDriftScore = Mathf.Min(configuration.MaximumPointsPerDrift,
            accumulatedDriftScore + rate * Time.fixedDeltaTime);
        if (!Mathf.Approximately(previousScore, accumulatedDriftScore))
            ScoreChanged?.Invoke(this);
    }

    /// <summary>Clears the current run and combo; intended for a new session or explicit UI reset.</summary>
    public void ResetAllScores()
    {
        totalScore = 0;
        ResetCurrentDrift();
        if (comboSystem != null) comboSystem.ResetSession();
        ScoreChanged?.Invoke(this);
    }

    /// <summary>Clears only the combo, preserving the banked total.</summary>
    public void ResetCombo()
    {
        if (comboSystem != null) comboSystem.ResetCombo();
        ScoreChanged?.Invoke(this);
    }

    private void HandleDriftStarted(DriftController.DriftEventData drift)
    {
        driftInProgress = true;
        driftInvalidated = !drift.IsValid;
        driftStartTime = drift.InitiationTime;
        accumulatedDriftScore = 0f;
        hadValidScoringSample = false;

        ScoreChanged?.Invoke(this);
    }

    private void HandleDriftEnded(DriftController.DriftEventData drift)
    {
        if (!driftInProgress) return;

        bool qualifies = !driftInvalidated && hadValidScoringSample && configuration != null
            && drift.DriftDuration >= configuration.MinimumDriftDuration
            && accumulatedDriftScore >= 1f;

        int banked = qualifies ? Mathf.FloorToInt(accumulatedDriftScore) : 0;
        if (qualifies)
        {
            totalScore = SaturatingAdd(totalScore, banked);
            DriftScoreBanked?.Invoke(banked, totalScore);
        }
        if (comboSystem != null)
            comboSystem.ReportDriftResult(qualifies);

        ResetCurrentDrift();
        ScoreChanged?.Invoke(this);
    }

    private void HandleBoundaryCollision(RoadBoundaryProximityDetector.BoundaryCollisionEventData collision)
    {
        if (driftInProgress)
        {
            driftInvalidated = true;
            accumulatedDriftScore = 0f;
        }
        if (comboSystem != null) comboSystem.ResetCombo();
        ScoreChanged?.Invoke(this);
    }

    private float GetAngleMultiplier(float angle)
    {
        float quality = Mathf.Clamp01(1f - Mathf.Abs(angle - configuration.IdealDriftAngle)
            / configuration.AngleTolerance);
        return Mathf.Lerp(configuration.MinimumAngleMultiplier,
            configuration.MaximumAngleMultiplier, quality);
    }

    private float GetSpeedMultiplier(float speed)
    {
        float speedRange = Mathf.Max(0.01f, configuration.SpeedForMaximumMultiplier
            - configuration.MinimumSpeed);
        float quality = Mathf.Clamp01((speed - configuration.MinimumSpeed) / speedRange);
        return Mathf.Lerp(configuration.MinimumSpeedMultiplier,
            configuration.MaximumSpeedMultiplier, quality);
    }

    private float GetDurationMultiplier(float duration)
    {
        float progress = Mathf.Clamp01(duration / configuration.DurationForMaximumMultiplier);
        return Mathf.Lerp(1f, configuration.MaximumDurationMultiplier, progress);
    }

    private float GetProximityMultiplier()
    {
        if (boundaryDetector == null || boundaryDetector.IsCollidingWithBoundary
            || float.IsInfinity(boundaryDetector.DistanceToNearestBoundary))
            return 1f;

        float proximity = Mathf.InverseLerp(configuration.ProximityBonusStartDistance,
            configuration.ProximityBonusFullDistance, boundaryDetector.DistanceToNearestBoundary);
        return 1f + proximity * configuration.MaximumProximityBonus;
    }

    private void ResolveReferences()
    {
        if (driftController == null) driftController = GetComponent<DriftController>();
        if (boundaryDetector == null) boundaryDetector = GetComponent<RoadBoundaryProximityDetector>();
        if (comboSystem == null) comboSystem = GetComponent<ComboSystem>();
    }

    private void ResetCurrentDrift()
    {
        driftInProgress = false;
        driftInvalidated = false;
        hadValidScoringSample = false;
        driftStartTime = 0f;
        accumulatedDriftScore = 0f;
    }

    private static int SaturatingAdd(int a, int b)
    {
        return a > int.MaxValue - b ? int.MaxValue : a + b;
    }

    private void OnValidate()
    {
        if (driftController == null) driftController = GetComponent<DriftController>();
        if (boundaryDetector == null) boundaryDetector = GetComponent<RoadBoundaryProximityDetector>();
        if (comboSystem == null) comboSystem = GetComponent<ComboSystem>();
    }
}
