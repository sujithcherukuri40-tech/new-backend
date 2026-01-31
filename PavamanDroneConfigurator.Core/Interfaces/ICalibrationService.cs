using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Mission Planner-equivalent calibration service interface.
// 
/// STRICT CALIBRATION RULES:
/// 1. Firmware is the SINGLE SOURCE OF TRUTH - UI never decides success
/// 2. All state changes are driven by STATUSTEXT messages from the flight controller
/// 3. Pre-conditions MUST pass before calibration can begin
/// 4. Abort conditions are monitored continuously during calibration
/// 5. Never invent sensor values
/// 6. Never skip validation
/// 7. Never continue after failure
/// 8. Flight safety always takes precedence
/// </summary>
public interface ICalibrationService
{
    #region Pre-Condition Validation

    /// <summary>
    /// Validates all pre-conditions before starting calibration.
    /// Returns detailed result with failure reasons if validation fails.
    /// Calibration MUST NOT proceed if this returns invalid.
    /// </summary>
    // Note: Pre-condition checking is handled by CalibrationPreConditionChecker service

    #endregion

    #region Calibration Operations

    /// <summary>
    /// Start a specific calibration type.
    /// Validates pre-conditions before sending MAV_CMD_PREFLIGHT_CALIBRATION.
    /// Returns false if pre-conditions fail or command rejected.
    /// </summary>
    Task<bool> StartCalibrationAsync(CalibrationType type);

    /// <summary>
    /// Cancel the current calibration in progress.
    /// Stops abort monitoring and resets state.
    /// </summary>
    Task<bool> CancelCalibrationAsync();

    /// <summary>
    /// Accept/confirm the current calibration step.
    /// For accelerometer: sends MAV_CMD_ACCELCAL_VEHICLE_POS to FC.
    /// FC validates the position and decides whether to accept.
    /// </summary>
    Task<bool> AcceptCalibrationStepAsync();

    /// <summary>
    /// Start accelerometer calibration (6-axis).
    /// REQUIRED ORIENTATIONS: Level, Left, Right, NoseDown, NoseUp, Back
    /// MAV_CMD_PREFLIGHT_CALIBRATION param5 = 1 (full 6-position calibration)
    /// </summary>
    Task<bool> StartAccelerometerCalibrationAsync(bool fullSixAxis = true);

    /// <summary>
    /// Start simple accelerometer calibration (automatic, no user positions).
    /// Vehicle must be on level surface. FC performs automatic calibration.
    /// MAV_CMD_PREFLIGHT_CALIBRATION param5 = 4
    /// </summary>
    Task<bool> StartSimpleAccelerometerCalibrationAsync();

    /// <summary>
    /// Start compass/magnetometer calibration.
    /// Requires rotation through all orientations for spherical coverage.
    /// MAV_CMD_PREFLIGHT_CALIBRATION param2 = 1 (mag) or 76 (onboard)
    /// </summary>
    Task<bool> StartCompassCalibrationAsync(bool onboardCalibration = false);

    /// <summary>
    /// Start gyroscope calibration.
    /// Vehicle must remain COMPLETELY STILL during entire calibration.
    /// MAV_CMD_PREFLIGHT_CALIBRATION param1 = 1
    /// </summary>
    Task<bool> StartGyroscopeCalibrationAsync();

    /// <summary>
    /// Start level horizon calibration (AHRS trims).
    /// Vehicle must be on PERFECTLY LEVEL surface.
    /// MAV_CMD_PREFLIGHT_CALIBRATION param5 = 2
    /// </summary>
    Task<bool> StartLevelHorizonCalibrationAsync();

    /// <summary>
    /// Start barometer calibration.
    /// Vehicle must be stationary with no airflow disturbance.
    /// MAV_CMD_PREFLIGHT_CALIBRATION param3 = 1
    /// </summary>
    Task<bool> StartBarometerCalibrationAsync();

    /// <summary>
    /// Start airspeed sensor calibration (for fixed-wing aircraft).
    /// MAV_CMD_PREFLIGHT_CALIBRATION param4 = 1
    /// </summary>
    Task<bool> StartAirspeedCalibrationAsync();

    /// <summary>
    /// Reboot the flight controller.
    /// Should be called after successful calibration.
    /// MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN param1 = 1
    /// </summary>
    Task<bool> RebootFlightControllerAsync();

    #endregion

    #region State Properties

    /// <summary>
    /// Get the current calibration state (for UI binding).
    /// </summary>
    CalibrationStateModel? CurrentState { get; }

    /// <summary>
    /// Whether a calibration is currently in progress.
    /// </summary>
    bool IsCalibrating { get; }

    /// <summary>
    /// Current state machine state (detailed).
    /// </summary>
    CalibrationStateMachine StateMachineState { get; }

    /// <summary>
    /// Get the current calibration diagnostics.
    /// Contains all STATUSTEXT history, COMMAND_ACK responses, and diagnostic messages.
    /// </summary>
    CalibrationDiagnostics? CurrentDiagnostics { get; }

    #endregion

    #region Events

    /// <summary>
    /// Event raised when calibration state changes.
    /// </summary>
    event EventHandler<CalibrationStateModel>? CalibrationStateChanged;

    /// <summary>
    /// Event raised when calibration progress updates.
    /// </summary>
    event EventHandler<CalibrationProgressEventArgs>? CalibrationProgressChanged;

    /// <summary>
    /// Event raised when a calibration step requires user action.
    /// FC is requesting a specific vehicle position.
    /// </summary>
    event EventHandler<CalibrationStepEventArgs>? CalibrationStepRequired;

    /// <summary>
    /// Event raised when FC sends a STATUSTEXT during calibration.
    /// For diagnostic logging and advanced UI.
    /// </summary>
    event EventHandler<CalibrationStatusTextEventArgs>? StatusTextReceived;

    /// <summary>
    /// Event raised when FC sends MAV_CMD_ACCELCAL_VEHICLE_POS requesting a specific position.
    /// MissionPlanner-style: FC tells GCS what position to place vehicle in.
    /// </summary>
    event EventHandler<AccelCalPositionRequestedEventArgs>? AccelCalPositionRequested;

    #endregion
}

/// <summary>
/// Event args for calibration progress updates.
/// </summary>
public class CalibrationProgressEventArgs : EventArgs
{
    public CalibrationType Type { get; set; }
    public int ProgressPercent { get; set; }
    public string? StatusText { get; set; }
    public int? CurrentStep { get; set; }
    public int? TotalSteps { get; set; }
    public CalibrationStateMachine StateMachine { get; set; }
}

/// <summary>
/// Event args for calibration step requirements.
/// Raised when FC requests a specific vehicle position.
/// </summary>
public class CalibrationStepEventArgs : EventArgs
{
    public CalibrationType Type { get; set; }
    public CalibrationStep Step { get; set; }
    public string? Instructions { get; set; }
    public bool CanConfirm { get; set; } = true;
}

/// <summary>
/// Event args for FC STATUSTEXT messages during calibration.
/// </summary>
public class CalibrationStatusTextEventArgs : EventArgs
{
    public byte Severity { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Calibration steps for accelerometer 6-axis calibration.
/// </summary>
public enum CalibrationStep
{
    /// <summary>Place vehicle level</summary>
    Level,
    /// <summary>Place vehicle on its left side</summary>
    LeftSide,
    /// <summary>Place vehicle on its right side</summary>
    RightSide,
    /// <summary>Place vehicle nose down</summary>
    NoseDown,
    /// <summary>Place vehicle nose up</summary>
    NoseUp,
    /// <summary>Place vehicle on its back (upside down)</summary>
    Back,
    /// <summary>Rotate vehicle for compass calibration</summary>
    Rotate,
    /// <summary>Keep vehicle still</summary>
    KeepStill
}

/// <summary>
/// Event args for FC requesting a specific vehicle position during accel calibration.
/// Raised when FC sends MAV_CMD_ACCELCAL_VEHICLE_POS to GCS.
/// </summary>
public class AccelCalPositionRequestedEventArgs : EventArgs
{
    /// <summary>
    /// The position FC is requesting (1-6, or SUCCESS/FAILED terminal states).
    /// </summary>
    public AccelCalVehiclePosition Position { get; set; }

    /// <summary>
    /// Human-readable position name (e.g., "LEVEL", "on its LEFT side").
    /// </summary>
    public string PositionName { get; set; } = string.Empty;

    /// <summary>
    /// Current step number (1-6).
    /// </summary>
    public int StepNumber { get; set; }

    /// <summary>
    /// Total number of steps (always 6 for accel calibration).
    /// </summary>
    public int TotalSteps { get; set; } = 6;
}
