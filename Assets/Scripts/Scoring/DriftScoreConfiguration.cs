using UnityEngine;

/// <summary>Tunable score, validity, proximity, and combo rules for drift scoring.</summary>
[CreateAssetMenu(menuName = "OVERSTEER/Drift Score Configuration", fileName = "DriftScoreConfiguration")]
public sealed class DriftScoreConfiguration : ScriptableObject
{
    [Header("Valid drift requirements")]
    [SerializeField, Min(0f)] private float minimumSpeed = 8f;
    [SerializeField, Range(0f, 90f)] private float minimumDriftAngle = 12f;
    [SerializeField, Range(0f, 90f)] private float idealDriftAngle = 30f;
    [SerializeField, Min(0f)] private float minimumDriftDuration = 0.5f;

    [Header("Base scoring")]
    [SerializeField, Min(0f)] private float basePointsPerSecond = 100f;
    [SerializeField, Min(0f)] private float maximumPointsPerSecond = 900f;
    [SerializeField, Min(0f)] private float maximumPointsPerDrift = 5000f;

    [Header("Angle multiplier")]
    [SerializeField, Min(0f)] private float minimumAngleMultiplier = 0.35f;
    [SerializeField, Min(0f)] private float maximumAngleMultiplier = 1.5f;
    [SerializeField, Range(1f, 90f)] private float angleTolerance = 35f;

    [Header("Speed multiplier")]
    [SerializeField, Min(0f)] private float speedForMaximumMultiplier = 32f;
    [SerializeField, Min(0f)] private float minimumSpeedMultiplier = 0.5f;
    [SerializeField, Min(0f)] private float maximumSpeedMultiplier = 1.35f;

    [Header("Duration multiplier")]
    [SerializeField, Min(0.1f)] private float durationForMaximumMultiplier = 6f;
    [SerializeField, Min(1f)] private float maximumDurationMultiplier = 2f;

    [Header("Boundary proximity bonus")]
    [SerializeField, Min(0f)] private float proximityBonusStartDistance = 3f;
    [SerializeField, Min(0f)] private float proximityBonusFullDistance = 0.8f;
    [SerializeField, Min(0f)] private float maximumProximityBonus = 0.5f;

    [Header("Combo")]
    [SerializeField, Min(0f)] private float comboWindow = 3f;
    [SerializeField, Min(0f)] private float comboMultiplierPerLink = 0.2f;
    [SerializeField, Min(1f)] private float maximumComboMultiplier = 2f;

    public float MinimumSpeed => minimumSpeed;
    public float MinimumDriftAngle => minimumDriftAngle;
    public float IdealDriftAngle => idealDriftAngle;
    public float MinimumDriftDuration => minimumDriftDuration;
    public float BasePointsPerSecond => basePointsPerSecond;
    public float MaximumPointsPerSecond => maximumPointsPerSecond;
    public float MaximumPointsPerDrift => maximumPointsPerDrift;
    public float MinimumAngleMultiplier => minimumAngleMultiplier;
    public float MaximumAngleMultiplier => maximumAngleMultiplier;
    public float AngleTolerance => angleTolerance;
    public float SpeedForMaximumMultiplier => speedForMaximumMultiplier;
    public float MinimumSpeedMultiplier => minimumSpeedMultiplier;
    public float MaximumSpeedMultiplier => maximumSpeedMultiplier;
    public float DurationForMaximumMultiplier => durationForMaximumMultiplier;
    public float MaximumDurationMultiplier => maximumDurationMultiplier;
    public float ProximityBonusStartDistance => proximityBonusStartDistance;
    public float ProximityBonusFullDistance => proximityBonusFullDistance;
    public float MaximumProximityBonus => maximumProximityBonus;
    public float ComboWindow => comboWindow;
    public float ComboMultiplierPerLink => comboMultiplierPerLink;
    public float MaximumComboMultiplier => maximumComboMultiplier;

    private void OnValidate()
    {
        minimumSpeed = Mathf.Max(0f, minimumSpeed);
        minimumDriftAngle = Mathf.Clamp(minimumDriftAngle, 0f, 90f);
        idealDriftAngle = Mathf.Clamp(idealDriftAngle, minimumDriftAngle, 90f);
        minimumDriftDuration = Mathf.Max(0f, minimumDriftDuration);
        basePointsPerSecond = Mathf.Max(0f, basePointsPerSecond);
        maximumPointsPerSecond = Mathf.Max(basePointsPerSecond, maximumPointsPerSecond);
        maximumPointsPerDrift = Mathf.Max(0f, maximumPointsPerDrift);
        minimumAngleMultiplier = Mathf.Max(0f, minimumAngleMultiplier);
        maximumAngleMultiplier = Mathf.Max(minimumAngleMultiplier, maximumAngleMultiplier);
        angleTolerance = Mathf.Max(1f, angleTolerance);
        speedForMaximumMultiplier = Mathf.Max(minimumSpeed, speedForMaximumMultiplier);
        minimumSpeedMultiplier = Mathf.Max(0f, minimumSpeedMultiplier);
        maximumSpeedMultiplier = Mathf.Max(minimumSpeedMultiplier, maximumSpeedMultiplier);
        durationForMaximumMultiplier = Mathf.Max(0.1f, durationForMaximumMultiplier);
        maximumDurationMultiplier = Mathf.Max(1f, maximumDurationMultiplier);
        proximityBonusStartDistance = Mathf.Max(0f, proximityBonusStartDistance);
        proximityBonusFullDistance = Mathf.Clamp(proximityBonusFullDistance, 0f, proximityBonusStartDistance);
        maximumProximityBonus = Mathf.Max(0f, maximumProximityBonus);
        comboWindow = Mathf.Max(0f, comboWindow);
        comboMultiplierPerLink = Mathf.Max(0f, comboMultiplierPerLink);
        maximumComboMultiplier = Mathf.Max(1f, maximumComboMultiplier);
    }
}
