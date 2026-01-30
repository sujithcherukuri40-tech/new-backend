namespace PavamanDroneConfigurator.Core.Enums;

/// <summary>
/// Explicit state machine for accelerometer calibration.
/// CRITICAL: This is a SAFETY-CRITICAL calibration.
/// States transition ONLY via FC STATUSTEXT messages.
/// NO timeouts, NO auto-completion, NO shortcuts.
/// </summary>
public enum AccelCalibrationState
{
    /// <summary>Not calibrating</summary>
    Idle,
    
    /// <summary>MAV_CMD_PREFLIGHT_CALIBRATION sent, waiting for COMMAND_ACK</summary>
    CommandSent,
    
    /// <summary>FC acknowledged command, waiting for first STATUSTEXT position request</summary>
    WaitingForFirstPosition,
    
    /// <summary>FC requested a position via STATUSTEXT, waiting for user to place vehicle</summary>
    WaitingForUserConfirmation,
    
    /// <summary>User clicked confirm, collecting IMU samples for validation</summary>
    ValidatingPosition,
    
    /// <summary>IMU validation passed, sending MAV_CMD_ACCELCAL_VEHICLE_POS to FC</summary>
    SendingPositionToFC,
    
    /// <summary>FC acknowledged position, sampling in progress</summary>
    FCSampling,
    
    /// <summary>Position rejected by IMU validator, user must reposition</summary>
    PositionRejected,
    
    /// <summary>FC reported calibration complete via STATUSTEXT</summary>
    Completed,
    
    /// <summary>FC reported calibration failed via STATUSTEXT</summary>
    Failed,
    
    /// <summary>User cancelled calibration</summary>
    Cancelled,
    
    /// <summary>FC rejected the calibration command (armed, busy, etc.)</summary>
    Rejected
}

/// <summary>
/// Accelerometer calibration position (1-6).
/// These MUST match MAVLink ACCELCAL_VEHICLE_POS enum parameter values.
/// 
/// From MAVLink enum definition:
///   ACCELCAL_VEHICLE_POS_LEVEL    = 1
///   ACCELCAL_VEHICLE_POS_LEFT     = 2
///   ACCELCAL_VEHICLE_POS_RIGHT    = 3
///   ACCELCAL_VEHICLE_POS_NOSEDOWN = 4
///   ACCELCAL_VEHICLE_POS_NOSEUP   = 5
///   ACCELCAL_VEHICLE_POS_BACK     = 6
///   ACCELCAL_VEHICLE_POS_SUCCESS  = 16777215 (0xFFFFFF)
///   ACCELCAL_VEHICLE_POS_FAILED   = 16777216 (0x1000000)
/// </summary>
public enum AccelCalibrationPosition
{
    /// <summary>Position 1: Vehicle level on flat surface (ACCELCAL_VEHICLE_POS_LEVEL)</summary>
    Level = 1,
    
    /// <summary>Position 2: Vehicle on left side (ACCELCAL_VEHICLE_POS_LEFT)</summary>
    Left = 2,
    
    /// <summary>Position 3: Vehicle on right side (ACCELCAL_VEHICLE_POS_RIGHT)</summary>
    Right = 3,
    
    /// <summary>Position 4: Vehicle nose pointing down 90° (ACCELCAL_VEHICLE_POS_NOSEDOWN)</summary>
    NoseDown = 4,
    
    /// <summary>Position 5: Vehicle nose pointing up 90° (ACCELCAL_VEHICLE_POS_NOSEUP)</summary>
    NoseUp = 5,
    
    /// <summary>Position 6: Vehicle upside down on its back (ACCELCAL_VEHICLE_POS_BACK)</summary>
    Back = 6
}

/// <summary>
/// Accelerometer calibration result.
/// </summary>
public enum AccelCalibrationResult
{
    /// <summary>Calibration still in progress</summary>
    InProgress,
    
    /// <summary>FC confirmed calibration success via STATUSTEXT</summary>
    Success,
    
    /// <summary>FC reported calibration failed via STATUSTEXT</summary>
    Failed,
    
    /// <summary>User cancelled calibration</summary>
    Cancelled,
    
    /// <summary>FC rejected calibration command (armed, busy, unsupported)</summary>
    Rejected
}
