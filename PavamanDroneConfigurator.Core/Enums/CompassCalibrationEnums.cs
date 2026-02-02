namespace PavamanDroneConfigurator.Core.Enums;

/// <summary>
/// Magnetometer calibration status from MAVLink MAG_CAL_STATUS enum.
/// Matches ArduPilotMega dialect values.
/// </summary>
public enum MagCalStatus
{
    /// <summary>MAG_CAL_NOT_STARTED - Calibration not started</summary>
    NotStarted = 0,
    
    /// <summary>MAG_CAL_WAITING_TO_START - Waiting for calibration to start</summary>
    WaitingToStart = 1,
    
    /// <summary>MAG_CAL_RUNNING_STEP_ONE - First calibration step (sphere fit)</summary>
    RunningStepOne = 2,
    
    /// <summary>MAG_CAL_RUNNING_STEP_TWO - Second calibration step (ellipsoid fit)</summary>
    RunningStepTwo = 3,
    
    /// <summary>MAG_CAL_SUCCESS - Calibration completed successfully</summary>
    Success = 4,
    
    /// <summary>MAG_CAL_FAILED - Calibration failed</summary>
    Failed = 5,
    
    /// <summary>MAG_CAL_BAD_ORIENTATION - Bad orientation detected during calibration</summary>
    BadOrientation = 6,
    
    /// <summary>MAG_CAL_BAD_RADIUS - Bad radius detected during calibration</summary>
    BadRadius = 7
}

/// <summary>
/// Compass calibration UI state machine
/// </summary>
public enum CompassCalibrationState
{
    /// <summary>No calibration in progress</summary>
    Idle,
    
    /// <summary>Calibration starting, waiting for FC response</summary>
    Starting,
    
    /// <summary>Running sphere fit (step 1)</summary>
    RunningSphereFit,
    
    /// <summary>Running ellipsoid fit (step 2)</summary>
    RunningEllipsoidFit,
    
    /// <summary>Calibration complete, waiting for user to accept/reject</summary>
    WaitingForAccept,
    
    /// <summary>Calibration accepted, reboot required</summary>
    Accepted,
    
    /// <summary>Calibration was cancelled</summary>
    Cancelled,
    
    /// <summary>Calibration failed</summary>
    Failed
}
