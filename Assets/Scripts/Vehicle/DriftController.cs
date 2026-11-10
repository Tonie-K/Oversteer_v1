using System;
using UnityEngine;

/// <summary>Detects controlled vehicle drift and publishes state/data events for consumers.</summary>
[RequireComponent(typeof(Rigidbody))]
public sealed class DriftController : MonoBehaviour
{
    public enum DriftState
    {
        NormalDriving,
        DriftInitiation,
        ActiveDrifting,
        DriftRecovery
    }

    public enum DriftDirection
    {
        None,
        Left,
        Right
    }

    /// <summary>A point-in-time drift snapshot passed to state and drift lifecycle listeners.</summary>
    public struct DriftEventData
    {
        public readonly DriftState PreviousState;
        public readonly DriftState State;
        public readonly DriftDirection Direction;
        public readonly float DriftAngle;
        public readonly float SignedDriftAngle;
        public readonly float DriftDuration;
        public readonly float VehicleSpeed;
        public readonly float InitiationTime;
        public readonly float LateralSpeed;
        public readonly bool IsValid;
        public readonly bool HandbrakeHeld;
        public readonly bool IsGrounded;

        internal DriftEventData(DriftState previousState, DriftState state, DriftDirection direction,
            float driftAngle, float signedDriftAngle, float driftDuration, float vehicleSpeed,
            float initiationTime, float lateralSpeed, bool isValid, bool handbrakeHeld, bool isGrounded)
        {
            PreviousState = previousState;
            State = state;
            Direction = direction;
            DriftAngle = driftAngle;
            SignedDriftAngle = signedDriftAngle;
            DriftDuration = driftDuration;
            VehicleSpeed = vehicleSpeed;
            InitiationTime = initiationTime;
            LateralSpeed = lateralSpeed;
            IsValid = isValid;
            HandbrakeHeld = handbrakeHeld;
            IsGrounded = isGrounded;
        }
    }

    [Header("Detection thresholds")]
    [SerializeField, Min(0f)] private float minimumSpeed = 5f;
    [SerializeField, Min(0f)] private float minimumForwardSpeed = 2f;
    [SerializeField, Range(0f, 90f)] private float minimumDriftAngle = 12f;
    [SerializeField, Range(0f, 90f)] private float minimumExitDriftAngle = 6f;
    [SerializeField, Min(0f)] private float minimumLateralSpeed = 1.6f;
    [SerializeField, Range(0f, 1f)] private float minimumLateralSpeedRatio = 0.18f;
    [SerializeField, Range(0f, 1f)] private float minimumForwardMotionRatio = 0.25f;
    [SerializeField, Min(0f)] private float handbrakeAngleAssist = 2f;

    [Header("State hysteresis")]
    [SerializeField, Min(0f)] private float initiationHoldTime = 0.12f;
    [SerializeField, Min(0f)] private float activeExitGraceTime = 0.18f;
    [SerializeField, Min(0f)] private float recoveryDuration = 0.25f;
    [SerializeField, Min(0f)] private float minimumExitLateralSpeed = 0.7f;

    [Header("References")]
    [SerializeField] private Rigidbody vehicleBody;
    [SerializeField] private VehicleGroundCheck groundCheck;
    [SerializeField] private VehicleInputHandler inputHandler;

    private DriftState state = DriftState.NormalDriving;
    private DriftDirection lastDriftDirection = DriftDirection.None;
    private float stateElapsed;
    private float driftInitiationTime;
    private float lastCompletedDriftDuration;
    private bool candidateValid;
    private bool grounded;
    private bool handbrakeHeld;

    /// <summary>Raised on every state transition. Does not depend on or implement scoring.</summary>
    public event Action<DriftEventData> StateChanged;
    /// <summary>Raised when a confirmed drift enters ActiveDrifting.</summary>
    public event Action<DriftEventData> DriftStarted;
    /// <summary>Raised when a drift has fully recovered to NormalDriving.</summary>
    public event Action<DriftEventData> DriftEnded;

    public DriftState State => state;
    public DriftDirection Direction { get; private set; } = DriftDirection.None;
    public float DriftAngle { get; private set; }
    public float SignedDriftAngle { get; private set; }
    public float DriftDuration => IsInDriftLifecycle
        ? Mathf.Max(0f, Time.time - driftInitiationTime)
        : lastCompletedDriftDuration;
    public float LastCompletedDriftDuration => lastCompletedDriftDuration;
    public float VehicleSpeed { get; private set; }
    public float ForwardSpeed { get; private set; }
    public float LateralSpeed { get; private set; }
    public float DriftInitiationTime => driftInitiationTime;
    public bool IsGrounded => grounded;
    public bool HandbrakeHeld => handbrakeHeld;
    public bool IsDriftValid => state == DriftState.ActiveDrifting && candidateValid;
    public bool IsInDriftLifecycle => state == DriftState.DriftInitiation
        || state == DriftState.ActiveDrifting || state == DriftState.DriftRecovery;
    public bool IsDrifting => state == DriftState.ActiveDrifting || state == DriftState.DriftRecovery;

    private void Awake()
    {
        if (vehicleBody == null) vehicleBody = GetComponent<Rigidbody>();
        if (groundCheck == null) groundCheck = GetComponent<VehicleGroundCheck>();
        if (inputHandler == null) inputHandler = GetComponent<VehicleInputHandler>();
    }

    private void FixedUpdate()
    {
        if (vehicleBody == null) return;

        Vector3 planarVelocity = Vector3.ProjectOnPlane(vehicleBody.linearVelocity, Vector3.up);
        VehicleSpeed = planarVelocity.magnitude;
        ForwardSpeed = Vector3.Dot(planarVelocity, transform.forward);
        LateralSpeed = Vector3.Dot(planarVelocity, transform.right);
        SignedDriftAngle = VehicleSpeed > 0.05f
            ? Mathf.Atan2(LateralSpeed, ForwardSpeed) * Mathf.Rad2Deg
            : 0f;
        DriftAngle = Mathf.Abs(SignedDriftAngle);

        if (Mathf.Abs(LateralSpeed) > 0.1f && VehicleSpeed >= minimumSpeed)
        {
            Direction = LateralSpeed > 0f ? DriftDirection.Right : DriftDirection.Left;
        }
        else
        {
            Direction = DriftDirection.None;
        }

        grounded = groundCheck != null && groundCheck.IsGrounded;
        handbrakeHeld = inputHandler != null && inputHandler.Current.Handbrake;
        candidateValid = IsEntryCandidate();

        switch (state)
        {
            case DriftState.NormalDriving:
                if (candidateValid)
                {
                    driftInitiationTime = Time.time;
                    lastCompletedDriftDuration = 0f;
                    lastDriftDirection = Direction;
                    TransitionTo(DriftState.DriftInitiation);
                }
                break;

            case DriftState.DriftInitiation:
                if (!candidateValid)
                {
                    driftInitiationTime = 0f;
                    TransitionTo(DriftState.NormalDriving);
                    break;
                }

                if (Direction != DriftDirection.None) lastDriftDirection = Direction;
                stateElapsed += Time.fixedDeltaTime;
                if (stateElapsed >= initiationHoldTime)
                    TransitionTo(DriftState.ActiveDrifting);
                break;

            case DriftState.ActiveDrifting:
                if (Direction != DriftDirection.None) lastDriftDirection = Direction;
                if (IsExitCandidate())
                {
                    stateElapsed = 0f;
                }
                else
                {
                    stateElapsed += Time.fixedDeltaTime;
                    if (stateElapsed >= activeExitGraceTime)
                        TransitionTo(DriftState.DriftRecovery);
                }
                break;

            case DriftState.DriftRecovery:
                if (candidateValid)
                {
                    if (Direction != DriftDirection.None) lastDriftDirection = Direction;
                    TransitionTo(DriftState.ActiveDrifting);
                    break;
                }

                stateElapsed += Time.fixedDeltaTime;
                if (stateElapsed >= recoveryDuration)
                    TransitionTo(DriftState.NormalDriving);
                break;
        }
    }

    private bool IsEntryCandidate()
    {
        if (!grounded || VehicleSpeed < minimumSpeed || ForwardSpeed < minimumForwardSpeed)
            return false;

        float forwardRatio = ForwardSpeed / Mathf.Max(0.01f, VehicleSpeed);
        float lateralRatio = Mathf.Abs(LateralSpeed) / Mathf.Max(0.01f, VehicleSpeed);
        float requiredAngle = Mathf.Max(0f,
            minimumDriftAngle - (handbrakeHeld ? handbrakeAngleAssist : 0f));

        return forwardRatio >= minimumForwardMotionRatio
            && Mathf.Abs(LateralSpeed) >= minimumLateralSpeed
            && lateralRatio >= minimumLateralSpeedRatio
            && DriftAngle >= requiredAngle;
    }

    private bool IsExitCandidate()
    {
        if (!grounded || VehicleSpeed < minimumSpeed * 0.85f
            || ForwardSpeed < minimumForwardSpeed * 0.5f)
            return false;

        float forwardRatio = ForwardSpeed / Mathf.Max(0.01f, VehicleSpeed);
        return forwardRatio >= minimumForwardMotionRatio * 0.6f
            && Mathf.Abs(LateralSpeed) >= minimumExitLateralSpeed
            && DriftAngle >= minimumExitDriftAngle;
    }

    private void TransitionTo(DriftState nextState)
    {
        if (nextState == state) return;

        DriftState previous = state;
        bool completedDrift = nextState == DriftState.NormalDriving
            && previous == DriftState.DriftRecovery;
        if (completedDrift)
            lastCompletedDriftDuration = Mathf.Max(0f, Time.time - driftInitiationTime);

        state = nextState;
        stateElapsed = 0f;
        if (nextState == DriftState.DriftInitiation || nextState == DriftState.NormalDriving)
            candidateValid = nextState == DriftState.DriftInitiation && candidateValid;

        DriftEventData eventData = CreateEventData(previous, nextState,
            completedDrift ? lastDriftDirection : Direction);
        StateChanged?.Invoke(eventData);

        if (nextState == DriftState.ActiveDrifting && previous == DriftState.DriftInitiation)
            DriftStarted?.Invoke(eventData);

        if (completedDrift)
        {
            DriftEnded?.Invoke(eventData);
            driftInitiationTime = 0f;
        }
    }

    private DriftEventData CreateEventData(DriftState previous, DriftState current, DriftDirection eventDirection)
    {
        float duration = current == DriftState.NormalDriving
            ? lastCompletedDriftDuration
            : DriftDuration;
        return new DriftEventData(previous, current, eventDirection, DriftAngle, SignedDriftAngle,
            duration, VehicleSpeed, driftInitiationTime, LateralSpeed, IsDriftValid, handbrakeHeld, grounded);
    }

    private void OnValidate()
    {
        minimumExitDriftAngle = Mathf.Min(minimumExitDriftAngle, minimumDriftAngle);
        minimumForwardMotionRatio = Mathf.Clamp01(minimumForwardMotionRatio);
        minimumLateralSpeedRatio = Mathf.Clamp01(minimumLateralSpeedRatio);
    }
}
