using System;
using UnityEngine;

/// <summary>Maintains a timeout-based chain of successfully scored drifts for a single vehicle.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DriftController))]
public sealed class ComboSystem : MonoBehaviour
{
    public enum ComboState
    {
        Inactive,
        Maintaining,
        AwaitingLink,
        Expired
    }

    [Header("Configuration")]
    [SerializeField] private DriftScoreConfiguration configuration;
    [SerializeField] private DriftController driftController;
    [SerializeField] private RoadBoundaryProximityDetector boundaryDetector;

    private int comboCount;
    private int highestCombo;
    private float comboTimer;
    private bool driftInProgress;
    private bool driftInvalidated;
    private bool driftResultHandled;
    private ComboState state = ComboState.Inactive;

    public event Action<ComboSystem> ComboChanged;

    public int ComboCount => comboCount;
    public int HighestCombo => highestCombo;
    public float ComboTimer => comboTimer;
    public float ComboTimeout => configuration != null ? configuration.ComboWindow : 0f;
    public float CurrentComboMultiplier => configuration == null ? 1f
        : Mathf.Min(configuration.MaximumComboMultiplier,
            1f + comboCount * configuration.ComboMultiplierPerLink);
    public float HighestComboMultiplier => configuration == null ? 1f
        : Mathf.Min(configuration.MaximumComboMultiplier,
            1f + highestCombo * configuration.ComboMultiplierPerLink);
    public ComboState CurrentComboState => state;
    public bool IsComboActive => comboCount > 0;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (driftController != null)
            driftController.DriftStarted += HandleDriftStarted;
        if (boundaryDetector != null)
            boundaryDetector.BoundaryCollision += HandleBoundaryCollision;
    }

    private void OnDisable()
    {
        if (driftController != null)
            driftController.DriftStarted -= HandleDriftStarted;
        if (boundaryDetector != null)
            boundaryDetector.BoundaryCollision -= HandleBoundaryCollision;
    }

    private void Update()
    {
        if (state != ComboState.AwaitingLink || driftInProgress || comboTimer <= 0f)
            return;

        comboTimer = Mathf.Max(0f, comboTimer - Time.deltaTime);
        if (comboTimer <= 0f)
        {
            comboCount = 0;
            state = ComboState.Expired;
        }
        ComboChanged?.Invoke(this);
    }

    /// <summary>
    /// Called by the scoring system exactly once when the current drift lifecycle completes.
    /// A combo link is only added for a drift that the scorer has accepted and banked.
    /// </summary>
    internal void ReportDriftResult(bool successfullyScored)
    {
        if (!driftInProgress || driftResultHandled) return;
        driftResultHandled = true;
        driftInProgress = false;

        if (!successfullyScored || driftInvalidated || configuration == null)
        {
            ClearCombo(ComboState.Inactive);
            return;
        }

        comboCount = Mathf.Min(comboCount + 1, 1000000);
        highestCombo = Mathf.Max(highestCombo, comboCount);
        comboTimer = configuration.ComboWindow;
        state = ComboState.AwaitingLink;
        ComboChanged?.Invoke(this);
    }

    /// <summary>Clears the current chain while retaining the session's highest combo.</summary>
    public void ResetCombo()
    {
        driftInProgress = false;
        driftInvalidated = false;
        driftResultHandled = true;
        ClearCombo(ComboState.Inactive);
    }

    /// <summary>Starts a new session and clears both current and record combo values.</summary>
    public void ResetSession()
    {
        ResetCombo();
        highestCombo = 0;
        ComboChanged?.Invoke(this);
    }

    private void HandleDriftStarted(DriftController.DriftEventData drift)
    {
        if (!drift.IsValid)
        {
            ResetCombo();
            return;
        }

        bool linkedInTime = state == ComboState.AwaitingLink && comboTimer > 0f;
        if (!linkedInTime)
            comboCount = 0;

        driftInProgress = true;
        driftInvalidated = false;
        driftResultHandled = false;
        comboTimer = configuration != null ? configuration.ComboWindow : 0f;
        state = ComboState.Maintaining;
        ComboChanged?.Invoke(this);
    }

    private void HandleBoundaryCollision(RoadBoundaryProximityDetector.BoundaryCollisionEventData collision)
    {
        if (driftInProgress)
            driftInvalidated = true;
        ClearCombo(ComboState.Inactive);
    }

    private void ClearCombo(ComboState nextState)
    {
        bool changed = comboCount != 0 || comboTimer != 0f || state != nextState;
        comboCount = 0;
        comboTimer = 0f;
        state = nextState;
        if (changed)
            ComboChanged?.Invoke(this);
    }

    private void ResolveReferences()
    {
        if (driftController == null) driftController = GetComponent<DriftController>();
        if (boundaryDetector == null) boundaryDetector = GetComponent<RoadBoundaryProximityDetector>();
    }

    private void OnValidate()
    {
        if (driftController == null) driftController = GetComponent<DriftController>();
        if (boundaryDetector == null) boundaryDetector = GetComponent<RoadBoundaryProximityDetector>();
    }
}
