namespace PavamanDroneConfigurator.Core.Enums;

/// <summary>
/// Calibration state machine states - firmware is the single source of truth.
/// UI NEVER decides calibration success - only the FC via STATUSTEXT.
/// </summary>
public enum CalibrationStateMachine
{
    /// <summary>No calibration active</summary>
    Idle,
    
    /// <summary>Command sent, waiting for FC acknowledgment</summary>
    WaitingForAck,
    
    /// <summary>FC acknowledged, waiting for first STATUSTEXT instruction</summary>
    WaitingForInstruction,
    
    /// <summary>FC requested a position, waiting for user to place vehicle</summary>
    WaitingForUserPosition,
    
    /// <summary>User confirmed position, waiting for FC to sample</summary>
    WaitingForSampling,
    
    /// <summary>UI is validating position using IMU data</summary>
    ValidatingPosition,
    
    /// <summary>FC is actively sampling sensor data</summary>
    Sampling,
    
    /// <summary>FC reported position accepted, waiting for next instruction</summary>
    PositionAccepted,
    
    /// <summary>FC reported position rejected, user must reposition</summary>
    PositionRejected,
    
    /// <summary>Compass: FC requested rotation, user is rotating vehicle</summary>
    Rotating,
    
    /// <summary>Gyro/Baro: FC sampling, vehicle must remain still</summary>
    KeepingStill,
    
    /// <summary>FC reported calibration complete via STATUSTEXT</summary>
    Completed,
    
    /// <summary>FC reported calibration failed via STATUSTEXT</summary>
    Failed,
    
    /// <summary>Command was rejected by FC (armed, busy, unsupported)</summary>
    Rejected,
    
    /// <summary>Calibration timed out waiting for FC response</summary>
    TimedOut,
    
    /// <summary>User cancelled calibration</summary>
    Cancelled
}

/// <summary>
/// ArduPilot accelerometer calibration vehicle positions.
/// Maps to MAV_CMD_ACCELCAL_VEHICLE_POS parameter values.
/// FROM ARDUPILOTMEGA DIALECT - use exact values from MAVLink XML.
/// </summary>
public enum AccelCalVehiclePosition
{
    /// <summary>Position 1: Vehicle level on flat surface</summary>
    Level = 1,
    
    /// <summary>Position 2: Vehicle on left side</summary>
    Left = 2,
    
    /// <summary>Position 3: Vehicle on right side</summary>
    Right = 3,
    
    /// <summary>Position 4: Vehicle nose pointing down</summary>
    NoseDown = 4,
    
    /// <summary>Position 5: Vehicle nose pointing up</summary>
    NoseUp = 5,
    
    /// <summary>Position 6: Vehicle upside down (on its back)</summary>
    Back = 6,
    
    /// <summary>
    /// Calibration successful (terminal state from FC).
    /// ArduPilotMega dialect value: 16777215
    /// </summary>
    Success = 16777215,
    
    /// <summary>
    /// Calibration failed (terminal state from FC).
    /// ArduPilotMega dialect value: 16777216
    /// </summary>
    Failed = 16777216
}

/// <summary>
/// MAV_RESULT values for COMMAND_ACK responses.
/// </summary>
public enum MavResult : byte
{
    /// <summary>Command accepted and executed</summary>
    Accepted = 0,
    
    /// <summary>Command temporarily rejected (may retry)</summary>
    TemporarilyRejected = 1,
    
    /// <summary>Command denied (won't work)</summary>
    Denied = 2,
    
    /// <summary>Command not supported by this autopilot</summary>
    Unsupported = 3,
    
    /// <summary>Command failed to execute</summary>
    Failed = 4,
    
    /// <summary>Command execution in progress</summary>
    InProgress = 5,
    
    /// <summary>Command cancelled</summary>
    Cancelled = 6
}

/// <summary>
/// Compass calibration completion mask bits.
/// </summary>
[Flags]
public enum CompassCalCompletionMask : byte
{
    None = 0,
    Compass1 = 1 << 0,
    Compass2 = 1 << 1,
    Compass3 = 1 << 2,
    AllThree = Compass1 | Compass2 | Compass3
}

/// <summary>
/// Calibration severity levels for diagnostics.
/// </summary>
public enum CalibrationDiagnosticSeverity
{
    /// <summary>Informational message</summary>
    Info,
    
    /// <summary>Warning - calibration may proceed but results may be suboptimal</summary>
    Warning,
    
    /// <summary>Error - calibration should not proceed or has failed</summary>
    Error
}
