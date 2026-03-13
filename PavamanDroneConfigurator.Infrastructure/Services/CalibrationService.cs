using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Calibration service implementing MissionPlanner-style IMU and Compass calibration.
/// 
/// CRITICAL: Accelerometer calibration is GCS-DRIVEN, not FC-driven!
// The FC only provides the initial "Place vehicle level" prompt.
// After each position is sampled, the GCS must:
//   1. Know the 6-position sequence
//   2. Prompt the user for the next position
//   3. Send the next MAV_CMD_ACCELCAL_VEHICLE_POS
// The FC does NOT request subsequent positions - it just validates and samples.
/// </summary>
public class CalibrationService : ICalibrationService
{
    private readonly ILogger<CalibrationService> _logger;
    private readonly IConnectionService _connectionService;
    private readonly IParameterService _parameterService;

    // State machine variables
    private CalibrationStateModel? _currentState;
    private CalibrationDiagnostics? _currentDiagnostics;
    private CalibrationType _activeCalibrationType;
    private bool _isCalibrating;
    private CalibrationStateMachine _stateMachineState = CalibrationStateMachine.Idle;

    // Accel calibration state - GCS-driven sequence
    private bool _inAccelCalibrate;
    private int _currentPositionIndex; // 0-5 index into the position sequence
    private readonly HashSet<AccelCalVehiclePosition> _completedPositions = new();
    private bool _waitingForUserConfirmation; // True when UI should show confirm button
    private bool _waitingForFcAck; // True when waiting for COMMAND_ACK from FC

    // The 6-position sequence (GCS drives this order)
    private static readonly AccelCalVehiclePosition[] PositionSequence =
    [
        AccelCalVehiclePosition.Level,    // Position 1
        AccelCalVehiclePosition.Left,     // Position 2
        AccelCalVehiclePosition.Right,    // Position 3
        AccelCalVehiclePosition.NoseDown, // Position 4
        AccelCalVehiclePosition.NoseUp,   // Position 5
        AccelCalVehiclePosition.Back      // Position 6
    ];

    // Compass calibration state
    private bool _inCompassCalibrate;
    private CompassCalibrationStateModel _compassCalState = new();
    private readonly object _compassLock = new();
    private System.Timers.Timer? _compassUiTimer;

    public CalibrationStateModel? CurrentState => _currentState;
    public bool IsCalibrating => _isCalibrating;
    public CalibrationStateMachine StateMachineState => _stateMachineState;
    public CalibrationDiagnostics? CurrentDiagnostics => _currentDiagnostics;
    public CompassCalibrationStateModel? CompassCalibrationState => _compassCalState;

    public event EventHandler<CalibrationStateModel>? CalibrationStateChanged;
    public event EventHandler<CalibrationProgressEventArgs>? CalibrationProgressChanged;
    public event EventHandler<CalibrationStepEventArgs>? CalibrationStepRequired;
    public event EventHandler<CalibrationStatusTextEventArgs>? StatusTextReceived;
    public event EventHandler<AccelCalPositionRequestedEventArgs>? AccelCalPositionRequested;
    public event EventHandler<CompassCalProgressEventArgs>? CompassCalProgressReceived;
    public event EventHandler<CompassCalReportEventArgs>? CompassCalReportReceived;
    public event EventHandler<CompassCalibrationStateModel>? CompassCalibrationStateChanged;

    public CalibrationService(
        ILogger<CalibrationService> logger,
        IConnectionService connectionService,
        IParameterService parameterService)
    {
        _logger = logger;
        _connectionService = connectionService;
        _parameterService = parameterService;

        _connectionService.StatusTextReceived += OnStatusTextReceived;
        _connectionService.CommandAckReceived += OnCommandAckReceived;
        _connectionService.CommandLongReceived += OnCommandLongReceived;
        _connectionService.MagCalProgressReceived += OnMagCalProgressReceived;
        _connectionService.MagCalReportReceived += OnMagCalReportReceived;
    }

    #region Connection Service Event Handlers

    private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
    {
        _logger.LogInformation("[CalibService] STATUSTEXT: [{Severity}] {Text}", e.Severity, e.Text);

        if (_isCalibrating)
        {
            StatusTextReceived?.Invoke(this, new CalibrationStatusTextEventArgs
            {
                Severity = e.Severity,
                Text = e.Text,
                Timestamp = DateTime.UtcNow
            });
        }

        if (_inAccelCalibrate)
        {
            HandleAccelCalStatusText(e.Text);
            return;
        }

        // Compass calibration uses MAG_CAL_PROGRESS and MAG_CAL_REPORT messages
        // for its lifecycle, not generic STATUSTEXT. Skip the generic success/failure
        // detection to avoid prematurely ending compass calibration when the FC sends
        // an unrelated STATUSTEXT matching one of the patterns below.
        if (_inCompassCalibrate)
            return;

        if (_isCalibrating)
        {
            var lower = e.Text.ToLowerInvariant();

            // Check for successful calibration completion
            // ArduPilot sends various messages depending on version and calibration type:
            // Barometer: "Barometer calibration complete", "Updating barometer calibration", "Ground pressure calibration"
            // Level: "Level calibration complete", "Calibration successful"
            // Level: "Level calibration complete", "Calibration successful", "level complete", "AHRS: trim saved"
            // Gyro: "Gyro calibration complete"
            if (lower.Contains("calibration successful") ||
                lower.Contains("calibration complete") ||
                lower.Contains("calibration done") ||
                lower.Contains("updating barometer calibration") ||
                lower.Contains("baro calibration complete") ||
                lower.Contains("ground pressure calibrated") ||
                lower.Contains("level complete") ||
                lower.Contains("trim saved") ||
                lower.Contains("ins: level") ||  // ArduPilot 4.x sends "INS: Level" for level horizon cal
                lower.Contains("calibration ok") ||
                lower.Contains("ahrs trim saved") ||
                lower.Contains("simple accel cal")) // Simple accelerometer cal complete
            {
                _logger.LogInformation("[CalibService] Calibration SUCCESS detected via STATUSTEXT: {Text}", e.Text);
                _isCalibrating = false;
                UpdateState(new CalibrationStateModel
                {
                    Type = _activeCalibrationType,
                    State = CalibrationState.Completed,
                    Progress = 100,
                    Message = e.Text
                });
            }
            else if (!lower.Contains("prearm") && (lower.Contains("calibration failed") || lower.Contains("cal failed")))
            {
                _logger.LogInformation("[CalibService] Calibration FAILED detected via STATUSTEXT: {Text}", e.Text);
                _isCalibrating = false;
                UpdateState(new CalibrationStateModel
                {
                    Type = _activeCalibrationType,
                    State = CalibrationState.Failed,
                    Message = e.Text
                });
            }
        }
    }

    private void HandleAccelCalStatusText(string text)
    {
        var lower = text.ToLowerInvariant();

        _logger.LogInformation("[AccelCal] STATUSTEXT: {Text}", text);

        // Check for terminal conditions first
        if (lower.Contains("calibration successful") || lower.Contains("calibration complete"))
        {
            _logger.LogInformation("[AccelCal] Calibration SUCCESSFUL - terminal state from FC");
            CompleteAccelCalibration(true, text);
            return;
        }

        if (lower.Contains("calibration failed") || (lower.Contains("cal failed") && !lower.Contains("prearm")))
        {
            _logger.LogInformation("[AccelCal] Calibration FAILED - terminal state from FC");
            CompleteAccelCalibration(false, text);
            return;
        }

        // Skip PreArm messages
        if (lower.Contains("prearm"))
        {
            _logger.LogDebug("[AccelCal] Ignoring PreArm message: {Text}", text);
            return;
        }

        // FC sends "Place vehicle level" at the START only
        // After that, GCS drives the sequence - FC does NOT request positions
        if (lower.Contains("place vehicle") && lower.Contains("level") && _currentPositionIndex == 0 && !_waitingForUserConfirmation)
        {
            _logger.LogInformation("[AccelCal] FC requested initial LEVEL position - starting GCS-driven sequence");
            PromptForPosition(0); // Start with LEVEL
        }
    }

    private void OnCommandAckReceived(object? sender, CommandAckEventArgs e)
    {
        _logger.LogInformation("[CalibService] COMMAND_ACK: cmd={Command} result={Result} success={Success}",
            e.Command, e.Result, e.IsSuccess);

        if (_isCalibrating)
        {
            // MAV_CMD_PREFLIGHT_CALIBRATION = 241
            if (e.Command == 241)
            {
                if (e.IsSuccess)
                {
                    _logger.LogInformation("[CalibService] PREFLIGHT_CALIBRATION ACCEPTED by FC");

                    if (_inAccelCalibrate)
                    {
                        UpdateState(new CalibrationStateModel
                        {
                            Type = CalibrationType.Accelerometer,
                            State = CalibrationState.InProgress,
                            StateMachine = CalibrationStateMachine.WaitingForInstruction,
                            Message = "Waiting for FC to initialize...",
                            CanConfirmPosition = false,
                            Progress = 0
                        });
                    }
                    // For barometer, level, gyroscope and other calibration types,
                    else if (_activeCalibrationType == CalibrationType.Level)
                    {
                        // Level horizon calibration is a fast, synchronous operation on the FC.
                        // ArduPilot performs the calibration before sending the COMMAND_ACK,
                        // so result=0 means the calibration is DONE (not just accepted).
                        // Some firmware versions don't send a separate STATUSTEXT for level,
                        // which would leave the calibration stuck in "InProgress" forever.
                        _logger.LogInformation("[CalibService] Level calibration COMPLETED via COMMAND_ACK");
                        _isCalibrating = false;
                        UpdateState(new CalibrationStateModel
                        {
                            Type = CalibrationType.Level,
                            State = CalibrationState.Completed,
                            Progress = 100,
                            Message = "Level calibration complete"
                        });
                    }
                    // For barometer, gyroscope and other calibration types,
                    // COMMAND_ACK result=0 (MAV_RESULT_ACCEPTED) only means the FC accepted
                    // the command — not that the calibration is complete. The actual completion
                    // is signalled via STATUSTEXT messages (e.g. "Calibration successful").
                    // Keep _isCalibrating = true so OnStatusTextReceived can detect completion.
                }
                else if (e.Result == 5) // MAV_RESULT_IN_PROGRESS
                {
                    // IN_PROGRESS means the FC accepted the command and calibration is running.
                    // This is NOT a failure — keep _isCalibrating = true and wait for completion
                    // via STATUSTEXT or a subsequent COMMAND_ACK.
                    _logger.LogInformation("[CalibService] PREFLIGHT_CALIBRATION IN_PROGRESS for {Type}", _activeCalibrationType);
                }
                else
                {
                    // For accelerometer calibration, a rejected ACK is a hard failure.
                    // For barometer and level calibration, result != 0 might not mean failure;
                    // ArduPilot may send result=5 (MAV_RESULT_IN_PROGRESS) which means it's working.
                    // The actual result comes via STATUSTEXT messages.

                    if (_inAccelCalibrate)
                    {
                        _logger.LogWarning("[CalibService] PREFLIGHT_CALIBRATION REJECTED for accel: result={Result}", e.Result);

                        _isCalibrating = false;
                        _inAccelCalibrate = false;

                        UpdateState(new CalibrationStateModel
                        {
                            Type = CalibrationType.Accelerometer,
                            State = CalibrationState.Failed,
                            StateMachine = CalibrationStateMachine.Rejected,
                            Message = $"Calibration rejected by FC (result: {e.Result}). Check PreArm conditions.",
                            CanConfirmPosition = false
                        });
                    }
                    else
                    {
                        // For all non-accel calibration types, treat rejection as failure.
                        // This matches the original behavior that relied on STATUSTEXT for
                        // completion and only used COMMAND_ACK to detect rejection.
                        _logger.LogWarning("[CalibService] PREFLIGHT_CALIBRATION REJECTED for {Type}: result={Result}",
                            _activeCalibrationType, e.Result);

                        _isCalibrating = false;

                        UpdateState(new CalibrationStateModel
                        {
                            Type = _activeCalibrationType,
                            State = CalibrationState.Failed,
                            StateMachine = CalibrationStateMachine.Rejected,
                            Message = $"Calibration rejected by FC (result: {e.Result}). Check PreArm conditions.",
                            CanConfirmPosition = false
                        });
                    }
                }
                return;
            }

            // MAV_CMD_DO_START_MAG_CAL = 42424
            if (e.Command == 42424)
            {
                if (e.IsSuccess)
                {
                    _logger.LogInformation("[CompassCal] MAV_CMD_DO_START_MAG_CAL ACCEPTED by FC");
                    lock (_compassLock)
                    {
                        _compassCalState.State = Core.Enums.CompassCalibrationState.RunningSphereFit;
                        _compassCalState.Message = "Calibration started - rotate vehicle in all directions...";
                    }
                    NotifyCompassStateChanged();
                }
                else if (e.Result == 5) // MAV_RESULT_IN_PROGRESS
                {
                    // IN_PROGRESS means the FC accepted the command and calibration is running.
                    // ArduPilot commonly returns IN_PROGRESS for DO_START_MAG_CAL because the
                    // calibration is an ongoing process. This is NOT a failure - keep flags set
                    // and wait for MAG_CAL_PROGRESS and MAG_CAL_REPORT messages.
                    _logger.LogInformation("[CompassCal] MAV_CMD_DO_START_MAG_CAL IN_PROGRESS - calibration running");
                    lock (_compassLock)
                    {
                        _compassCalState.State = Core.Enums.CompassCalibrationState.RunningSphereFit;
                        _compassCalState.Message = "Calibration started - rotate vehicle in all directions...";
                    }
                    NotifyCompassStateChanged();
                }
                else
                {
                    _logger.LogWarning("[CompassCal] MAV_CMD_DO_START_MAG_CAL REJECTED by FC: result={Result}", e.Result);
                    lock (_compassLock)
                    {
                        _compassCalState.State = Core.Enums.CompassCalibrationState.Failed;
                        _compassCalState.Message = $"Calibration rejected (result: {e.Result}). Vehicle may need to be disarmed.";
                    }
                    _inCompassCalibrate = false;
                    _isCalibrating = false;
                    StopCompassUiTimer();
                    NotifyCompassStateChanged();
                }
                return;
            }

            // MAV_CMD_DO_ACCEPT_MAG_CAL = 42425
            if (e.Command == 42425)
            {
                _logger.LogInformation("[CompassCal] MAV_CMD_DO_ACCEPT_MAG_CAL result={Result}", e.Result);
                if (e.IsSuccess)
                {
                    lock (_compassLock)
                    {
                        _compassCalState.State = Core.Enums.CompassCalibrationState.Accepted;
                        _compassCalState.Message = "Calibration accepted! Please reboot the autopilot.";
                    }
                }
                NotifyCompassStateChanged();
                return;
            }

            // MAV_CMD_DO_CANCEL_MAG_CAL = 42426
            if (e.Command == 42426)
            {
                _logger.LogInformation("[CompassCal] MAV_CMD_DO_CANCEL_MAG_CAL result={Result}", e.Result);
                lock (_compassLock)
                {
                    _compassCalState.State = Core.Enums.CompassCalibrationState.Cancelled;
                    _compassCalState.Message = "Calibration cancelled";
                }
                _inCompassCalibrate = false;
                _isCalibrating = false;
                StopCompassUiTimer();
                NotifyCompassStateChanged();
                return;
            }

            // MAV_CMD_ACCELCAL_VEHICLE_POS = 42429
            if (e.Command == 42429 && _inAccelCalibrate && _waitingForFcAck)
            {
                _waitingForFcAck = false;

                if (e.IsSuccess)
                {
                    var completedPosition = PositionSequence[_currentPositionIndex];
                    _completedPositions.Add(completedPosition);

                    _logger.LogInformation("[AccelCal] Position {Position} (index {Index}) ACK ACCEPTED. Completed: {Count}/6",
                        completedPosition, _currentPositionIndex, _completedPositions.Count);

                    // Move to next position
                    _currentPositionIndex++;

                    if (_currentPositionIndex < 6)
                    {
                        // GCS DRIVES THE SEQUENCE: Prompt for next position immediately
                        _logger.LogInformation("[AccelCal] GCS driving sequence: advancing to position index {Index}", _currentPositionIndex);
                        PromptForPosition(_currentPositionIndex);
                    }
                    else
                    {
                        // All 6 positions complete - wait for FC to send success message
                        _logger.LogInformation("[AccelCal] All 6 positions sampled. Waiting for FC completion message...");
                        UpdateState(new CalibrationStateModel
                        {
                            Type = CalibrationType.Accelerometer,
                            State = CalibrationState.InProgress,
                            StateMachine = CalibrationStateMachine.WaitingForInstruction,
                            CurrentPosition = 6,
                            Message = "All positions sampled. Finalizing calibration...",
                            CanConfirmPosition = false,
                            Progress = 100
                        });
                    }
                }
                else
                {
                    // Position REJECTED by FC - allow retry
                    var rejectedPosition = PositionSequence[_currentPositionIndex];
                    _logger.LogWarning("[AccelCal] Position {Position} ACK REJECTED (result={Result}). Allow retry.",
                        rejectedPosition, e.Result);

                    // Stay on current position, re-prompt user
                    PromptForPosition(_currentPositionIndex, $"Position rejected (result: {e.Result}). Reposition and try again.");
                }
            }
        }
    }

    private void OnCommandLongReceived(object? sender, CommandLongEventArgs e)
    {
        _logger.LogInformation("[CalibService] COMMAND_LONG received: cmd={Command} param1={Param1}",
            e.Command, e.Param1);

        // MAV_CMD_ACCELCAL_VEHICLE_POS = 42429 from FC (terminal states)
        if (e.Command == 42429 && _inAccelCalibrate)
        {
            var position = (AccelCalVehiclePosition)(int)e.Param1;

            if (position == AccelCalVehiclePosition.Success)
            {
                _logger.LogInformation("[AccelCal] FC sent SUCCESS via COMMAND_LONG");
                CompleteAccelCalibration(true, "Calibration successful");
                return;
            }

            if (position == AccelCalVehiclePosition.Failed)
            {
                _logger.LogInformation("[AccelCal] FC sent FAILED via COMMAND_LONG");
                CompleteAccelCalibration(false, "Calibration failed");
                return;
            }
        }
    }

    #endregion

    #region GCS-Driven Position Prompting

    /// <summary>
    /// GCS prompts user for the position at the given index.
    /// This is the core of the GCS-driven state machine.
    /// </summary>
    private void PromptForPosition(int positionIndex, string? errorMessage = null)
    {
        if (positionIndex < 0 || positionIndex >= 6)
        {
            _logger.LogError("[AccelCal] Invalid position index: {Index}", positionIndex);
            return;
        }

        var position = PositionSequence[positionIndex];
        var positionName = GetPositionName(position);

        _logger.LogInformation("[AccelCal] GCS prompting for position {Index}: {Position} ({Name})",
            positionIndex, position, positionName);

        _waitingForUserConfirmation = true;
        _waitingForFcAck = false;

        // Raise event for UI
        AccelCalPositionRequested?.Invoke(this, new AccelCalPositionRequestedEventArgs
        {
            Position = position,
            PositionName = positionName,
            StepNumber = positionIndex + 1,
            TotalSteps = 6
        });

        var message = string.IsNullOrEmpty(errorMessage)
            ? $"Place vehicle {positionName} and click when done"
            : errorMessage;

        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Accelerometer,
            State = CalibrationState.WaitingForUserAction,
            StateMachine = CalibrationStateMachine.WaitingForUserPosition,
            CurrentPosition = (int)position,
            Message = message,
            CanConfirmPosition = true,
            Progress = (_completedPositions.Count * 100) / 6
        });

        CalibrationStepRequired?.Invoke(this, new CalibrationStepEventArgs
        {
            Type = CalibrationType.Accelerometer,
            Step = MapPositionToCalibrationStep(position),
            Instructions = $"Place vehicle {positionName}",
            CanConfirm = true
        });
    }

    #endregion

    #region ICalibrationService Implementation

    public Task<bool> StartCalibrationAsync(CalibrationType type) => type switch
    {
        CalibrationType.Accelerometer => StartAccelerometerCalibrationAsync(true),
        CalibrationType.Compass => StartCompassCalibrationAsync(false),
        CalibrationType.Gyroscope => StartGyroscopeCalibrationAsync(),
        CalibrationType.Level => StartLevelHorizonCalibrationAsync(),
        CalibrationType.Barometer => StartBarometerCalibrationAsync(),
        CalibrationType.Airspeed => StartAirspeedCalibrationAsync(),
        _ => Task.FromResult(false)
    };

    public Task<bool> CancelCalibrationAsync()
    {
        _logger.LogInformation("[CalibService] Cancelling calibration");

        // Send cancel command to the flight controller
        if (_connectionService.IsConnected)
        {
            _logger.LogInformation("[CalibService] Sending MAV_CMD_PREFLIGHT_CALIBRATION cancel to FC");
            _connectionService.SendCancelPreflightCalibration();
        }

        // Also stop compass calibration if active
        if (_inCompassCalibrate)
        {
            _logger.LogInformation("[CalibService] Cancelling compass calibration");
            StopCompassUiTimer();
            _ = _connectionService.SendCancelMagCalAsync(0);
        }

        // Reset all local state
        _isCalibrating = false;
        _inAccelCalibrate = false;
        _inCompassCalibrate = false;
        _currentPositionIndex = 0;
        _completedPositions.Clear();
        _waitingForUserConfirmation = false;
        _waitingForFcAck = false;

        // Reset compass state
        lock (_compassLock)
        {
            _compassCalState = new CompassCalibrationStateModel
            {
                State = Core.Enums.CompassCalibrationState.Cancelled,
                Message = "Calibration cancelled"
            };
        }

        UpdateState(new CalibrationStateModel
        {
            Type = _activeCalibrationType,
            State = CalibrationState.Idle,
            StateMachine = CalibrationStateMachine.Cancelled,
            Message = "Cancelled",
            CanConfirmPosition = false
        });

        return Task.FromResult(true);
    }

    /// <summary>
    /// Accept the current calibration step.
    /// Sends MAV_CMD_ACCELCAL_VEHICLE_POS with the current position value.
    /// </summary>
    public Task<bool> AcceptCalibrationStepAsync()
    {
        if (!_inAccelCalibrate)
        {
            _logger.LogWarning("[CalibService] AcceptCalibrationStepAsync called but not in accel calibration");
            return Task.FromResult(false);
        }

        if (!_waitingForUserConfirmation)
        {
            _logger.LogInformation("[AccelCal] Ignoring click - not waiting for user confirmation");
            return Task.FromResult(false);
        }

        if (_waitingForFcAck)
        {
            _logger.LogInformation("[AccelCal] Ignoring click - already waiting for FC ACK");
            return Task.FromResult(false);
        }

        if (_currentPositionIndex < 0 || _currentPositionIndex >= 6)
        {
            _logger.LogWarning("[AccelCal] Invalid position index: {Index}", _currentPositionIndex);
            return Task.FromResult(false);
        }

        var position = PositionSequence[_currentPositionIndex];
        int positionParam = (int)position; // 1-6 enum value

        _logger.LogInformation("[AccelCal] User confirmed position {Index}: {Position}, sending param1={Value}",
            _currentPositionIndex, position, positionParam);

        _waitingForUserConfirmation = false;
        _waitingForFcAck = true;

        // Send MAV_CMD_ACCELCAL_VEHICLE_POS with position enum value (1-6)
        _connectionService.SendAccelCalVehiclePos(positionParam);

        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Accelerometer,
            State = CalibrationState.InProgress,
            StateMachine = CalibrationStateMachine.WaitingForSampling,
            CurrentPosition = (int)position,
            Message = $"Sampling {GetPositionName(position)}... do not move",
            CanConfirmPosition = false,
            Progress = (_completedPositions.Count * 100) / 6
        });

        return Task.FromResult(true);
    }

    public async Task<bool> StartAccelerometerCalibrationAsync(bool fullSixAxis = true)
    {
        if (_inAccelCalibrate)
        {
            _logger.LogInformation("[AccelCal] Already calibrating - treating click as position confirmation");
            return await AcceptCalibrationStepAsync();
        }

        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("[AccelCal] Not connected - cannot start calibration");
            return false;
        }

        _logger.LogInformation("[AccelCal] Starting full 6-position accelerometer calibration (GCS-driven)");

        // Reset any stale state from previously interrupted calibrations
        ResetStaleCalibrationState();

        // Reset state for GCS-driven sequence
        _currentPositionIndex = 0;
        _completedPositions.Clear();
        _waitingForUserConfirmation = false;
        _waitingForFcAck = false;
        _activeCalibrationType = CalibrationType.Accelerometer;
        _isCalibrating = true;
        _inAccelCalibrate = true;

        // Send MAV_CMD_PREFLIGHT_CALIBRATION
        int accelParam = fullSixAxis ? 1 : 4;

        _logger.LogInformation("[AccelCal] Sending MAV_CMD_PREFLIGHT_CALIBRATION with param5={Accel}", accelParam);
        _connectionService.SendPreflightCalibration(0, 0, 0, 0, accelParam);

        return true;
    }

    public Task<bool> StartSimpleAccelerometerCalibrationAsync()
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("[AccelCal] Not connected - cannot start simple calibration");
            return Task.FromResult(false);
        }

        _logger.LogInformation("[AccelCal] Starting simple accelerometer calibration (param5=4)");

        _activeCalibrationType = CalibrationType.Accelerometer;
        _isCalibrating = true;
        _inAccelCalibrate = false;

        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Accelerometer,
            State = CalibrationState.InProgress,
            Message = "Simple accelerometer calibration in progress...",
            Progress = 0
        });

        _connectionService.SendPreflightCalibration(0, 0, 0, 0, 4);

        return Task.FromResult(true);
    }

    public Task<bool> StartCompassCalibrationAsync(bool onboardCalibration = false)
    {
        return StartOnboardCompassCalibrationAsync(0, true, true);
    }

    public Task<bool> StartGyroscopeCalibrationAsync()
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);

        ResetStaleCalibrationState();
        _activeCalibrationType = CalibrationType.Gyroscope;
        _isCalibrating = true;

        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Gyroscope,
            State = CalibrationState.InProgress,
            Message = "Keep still - calibrating gyro..."
        });

        _connectionService.SendPreflightCalibration(1, 0, 0, 0, 0);
        return Task.FromResult(true);
    }

    public async Task<bool> StartLevelHorizonCalibrationAsync()
    {
        if (!_connectionService.IsConnected) return false;

        ResetStaleCalibrationState();
        _activeCalibrationType = CalibrationType.Level;
        _isCalibrating = true;

        _logger.LogInformation("[CalibService] Starting level horizon calibration (param5=2)");

        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Level,
            State = CalibrationState.InProgress,
            Message = "Level calibration in progress - keep vehicle level..."
        });

        // Level calibration uses the async path (SendCommandLongAsync) to properly
        // await the COMMAND_ACK. The ACK result=0 signals completion since level is
        // a fast, synchronous operation on the FC.
        // MAV_CMD_PREFLIGHT_CALIBRATION with param5=2 triggers level horizon calibration.
        // As per MissionPlanner: param1=0, param2=0, param3=0, param4=0, param5=2, param6=0, param7=0
        try
        {
            await _connectionService.SendPreflightCalibrationAsync(0, 0, 0, 0, 2);
            // COMMAND_ACK handling (including completion) is done in OnCommandAckReceived
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CalibService] Failed to send level horizon calibration command");
            _isCalibrating = false;
            UpdateState(new CalibrationStateModel
            {
                Type = CalibrationType.Level,
                State = CalibrationState.Failed,
                Message = $"Failed to send calibration command: {ex.Message}"
            });
            return false;
        }
    }

    public Task<bool> StartBarometerCalibrationAsync()
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);

        ResetStaleCalibrationState();
        _activeCalibrationType = CalibrationType.Barometer;
        _isCalibrating = true;

        _logger.LogInformation("[CalibService] Starting barometer calibration (param3=1)");

        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Barometer,
            State = CalibrationState.InProgress,
            Message = "Barometer calibration in progress - keep vehicle still..."
        });

        // MAV_CMD_PREFLIGHT_CALIBRATION with param3=1 triggers ground pressure/barometer calibration
        // As per MissionPlanner: param1=0 (or 1 for gyro), param2=0, param3=1, param4=0, param5=0, param6=0, param7=0
        _connectionService.SendPreflightCalibration(0, 0, 1, 0, 0);

        // The calibration result will be received via STATUSTEXT or COMMAND_ACK
        // OnStatusTextReceived and OnCommandAckReceived will handle the completion
        return Task.FromResult(true);
    }

    public Task<bool> StartAirspeedCalibrationAsync()
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);

        ResetStaleCalibrationState();
        _activeCalibrationType = CalibrationType.Airspeed;
        _isCalibrating = true;

        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Airspeed,
            State = CalibrationState.InProgress,
            Message = "Airspeed calibration in progress..."
        });

        _connectionService.SendPreflightCalibration(0, 0, 0, 1, 0);
        return Task.FromResult(true);
    }

    public Task<bool> RebootFlightControllerAsync()
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);

        _logger.LogInformation("[CalibService] Sending reboot command");
        _connectionService.SendPreflightReboot(1, 0);
        return Task.FromResult(true);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Resets stale calibration flags from any previously interrupted calibration.
    /// Must be called at the start of each new calibration to prevent state leakage.
    /// Without this, a previously interrupted accel calibration would leave _inAccelCalibrate=true,
    /// causing STATUSTEXT messages for subsequent calibrations to be routed to the wrong handler.
    /// </summary>
    private void ResetStaleCalibrationState()
    {
        if (_inAccelCalibrate)
        {
            _logger.LogInformation("[CalibService] Resetting stale accel calibration state");
            _inAccelCalibrate = false;
            _currentPositionIndex = 0;
            _completedPositions.Clear();
            _waitingForUserConfirmation = false;
            _waitingForFcAck = false;
        }

        if (_inCompassCalibrate)
        {
            _logger.LogInformation("[CalibService] Resetting stale compass calibration state");
            _inCompassCalibrate = false;
            StopCompassUiTimer();
            lock (_compassLock)
            {
                _compassCalState = new CompassCalibrationStateModel();
            }
        }
    }

    private void CompleteAccelCalibration(bool success, string message)
    {
        _logger.LogInformation("[AccelCal] Completing calibration: success={Success} message={Message}",
            success, message);

        _isCalibrating = false;
        _inAccelCalibrate = false;
        _currentPositionIndex = 0;
        _waitingForUserConfirmation = false;
        _waitingForFcAck = false;

        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Accelerometer,
            State = success ? CalibrationState.Completed : CalibrationState.Failed,
            StateMachine = success ? CalibrationStateMachine.Completed : CalibrationStateMachine.Failed,
            Message = message,
            Progress = success ? 100 : 0,
            CanConfirmPosition = false
        });
    }

    private void UpdateState(CalibrationStateModel state)
    {
        // Include completed positions for UI to show green indicators
        state.CompletedPositions = _completedPositions.Select(p => (int)p).ToList();

        _currentState = state;
        _stateMachineState = state.StateMachine;

        _logger.LogDebug("[CalibService] State update: Type={Type} State={State} SM={SM} Msg={Msg} Completed={Completed}",
            state.Type, state.State, state.StateMachine, state.Message, string.Join(",", state.CompletedPositions));

        CalibrationStateChanged?.Invoke(this, state);

        CalibrationProgressChanged?.Invoke(this, new CalibrationProgressEventArgs
        {
            Type = state.Type,
            ProgressPercent = state.Progress,
            StatusText = state.Message,
            CurrentStep = state.CurrentPosition,
            TotalSteps = 6,
            StateMachine = state.StateMachine
        });
    }

    private static string GetPositionName(AccelCalVehiclePosition position) => position switch
    {
        AccelCalVehiclePosition.Level => "LEVEL",
        AccelCalVehiclePosition.Left => "on its LEFT side",
        AccelCalVehiclePosition.Right => "on its RIGHT side",
        AccelCalVehiclePosition.NoseDown => "NOSE DOWN",
        AccelCalVehiclePosition.NoseUp => "NOSE UP",
        AccelCalVehiclePosition.Back => "on its BACK (upside down)",
        AccelCalVehiclePosition.Success => "SUCCESS",
        AccelCalVehiclePosition.Failed => "FAILED",
        _ => position.ToString()
    };

    private static CalibrationStep MapPositionToCalibrationStep(AccelCalVehiclePosition position) => position switch
    {
        AccelCalVehiclePosition.Level => CalibrationStep.Level,
        AccelCalVehiclePosition.Left => CalibrationStep.LeftSide,
        AccelCalVehiclePosition.Right => CalibrationStep.RightSide,
        AccelCalVehiclePosition.NoseDown => CalibrationStep.NoseDown,
        AccelCalVehiclePosition.NoseUp => CalibrationStep.NoseUp,
        AccelCalVehiclePosition.Back => CalibrationStep.Back,
        _ => CalibrationStep.Level
    };

    #endregion

    #region Compass Calibration

    private void OnMagCalProgressReceived(object? sender, MagCalProgressEventArgs e)
    {
        // Accept progress updates even if we didn't think calibration was active
        // This handles cases where the FC started calibration before we updated state
        if (!_inCompassCalibrate)
        {
            _logger.LogInformation("[CompassCal] Received progress but _inCompassCalibrate=false - auto-activating");
            _inCompassCalibrate = true;
            _isCalibrating = true;
            _activeCalibrationType = CalibrationType.Compass;
            StartCompassUiTimer();
        }

        _logger.LogInformation("[CompassCal] Progress: compass={CompassId} status={Status} pct={Pct}%",
            e.CompassId, e.CalStatus, e.CompletionPct);

        lock (_compassLock)
        {
            // Update progress for this compass
            _compassCalState.CompassProgress[e.CompassId] = e.CompletionPct;

            // Update overall state based on status
            var status = (MagCalStatus)e.CalStatus;
            if (status == MagCalStatus.RunningStepOne)
            {
                _compassCalState.State = Core.Enums.CompassCalibrationState.RunningSphereFit;
                _compassCalState.Message = $"Calibrating... Rotate vehicle in all directions";
            }
            else if (status == MagCalStatus.RunningStepTwo)
            {
                _compassCalState.State = Core.Enums.CompassCalibrationState.RunningEllipsoidFit;
                _compassCalState.Message = $"Compass Calibration in progress...";
            }
            else if (status == MagCalStatus.WaitingToStart)
            {
                _compassCalState.State = Core.Enums.CompassCalibrationState.Starting;
                _compassCalState.Message = "Waiting to start...";
            }
        }

        // Immediately raise the progress event (don't rely only on timer)
        CompassCalProgressReceived?.Invoke(this, new CompassCalProgressEventArgs
        {
            CompassId = e.CompassId,
            Status = (MagCalStatus)e.CalStatus,
            Attempt = e.Attempt,
            CompletionPercent = e.CompletionPct,
            Direction = (e.DirectionX, e.DirectionY, e.DirectionZ)
        });
    }

    private void OnMagCalReportReceived(object? sender, MagCalReportEventArgs e)
    {
        if (!_inCompassCalibrate)
            return;

        _logger.LogInformation("[CompassCal] Report: compass={CompassId} status={Status} fitness={Fitness} autosaved={Autosaved}",
            e.CompassId, e.CalStatus, e.Fitness, e.Autosaved);

        // Skip reports with zero offsets (invalid)
        if (e.CompassId == 0 && e.OfsX == 0 && e.OfsY == 0 && e.OfsZ == 0)
        {
            _logger.LogDebug("[CompassCal] Ignoring report with zero offsets");
            return;
        }

        var status = (MagCalStatus)e.CalStatus;
        var isAcceptable = status == MagCalStatus.Success && e.Fitness < 50.0f;

        lock (_compassLock)
        {
            // Store report for this compass - cast CalStatus to MagCalStatus
            _compassCalState.CompassReports[e.CompassId] = new MagCalReportData
            {
                CompassId = e.CompassId,
                CalMask = e.CalMask,
                CalStatus = status,  // Cast byte to MagCalStatus
                Autosaved = e.Autosaved,
                Fitness = e.Fitness,
                OfsX = e.OfsX,
                OfsY = e.OfsY,
                OfsZ = e.OfsZ,
                DiagX = e.DiagX,
                DiagY = e.DiagY,
                DiagZ = e.DiagZ,
                OffdiagX = e.OffdiagX,
                OffdiagY = e.OffdiagY,
                OffdiagZ = e.OffdiagZ,
                OrientationConfidence = e.OrientationConfidence,
                OldOrientation = e.OldOrientation,
                NewOrientation = e.NewOrientation,
                ScaleFactor = e.ScaleFactor
            };

            // Set progress to 100% for this compass
            _compassCalState.CompassProgress[e.CompassId] = 100;

            // Check if calibration is complete
            if (e.Autosaved == 1)
            {
                // Check if all compasses are complete
                if (_compassCalState.CompletedCount == _compassCalState.CompassCount &&
                    _compassCalState.CompassCount > 0)
                {
                    _compassCalState.State = Core.Enums.CompassCalibrationState.Accepted;
                    _compassCalState.Message = "Calibration complete! Please reboot the autopilot.";
                    _inCompassCalibrate = false;
                    _isCalibrating = false;
                    StopCompassUiTimer();
                }
            }
            else if (status == MagCalStatus.Success)
            {
                _compassCalState.State = Core.Enums.CompassCalibrationState.WaitingForAccept;
                _compassCalState.Message = "Calibration successful. Accept or cancel.";
            }
            else if (status == MagCalStatus.Failed)
            {
                _compassCalState.State = Core.Enums.CompassCalibrationState.Failed;
                _compassCalState.Message = $"Calibration failed for compass {e.CompassId}";
                _inCompassCalibrate = false;
                _isCalibrating = false;
                StopCompassUiTimer();
            }
        }

        // Raise events
        CompassCalReportReceived?.Invoke(this, new CompassCalReportEventArgs
        {
            CompassId = e.CompassId,
            Status = status,
            IsAutosaved = e.Autosaved == 1,
            Fitness = e.Fitness,
            Offsets = (e.OfsX, e.OfsY, e.OfsZ),
            IsAcceptable = isAcceptable
        });

        NotifyCompassStateChanged();
    }

    public async Task<bool> StartOnboardCompassCalibrationAsync(int magMask = 0, bool retryOnFailure = true, bool autosave = true)
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("[CompassCal] Not connected - cannot start calibration");
            return false;
        }

        _logger.LogInformation("[CompassCal] Starting onboard compass calibration: mask={Mask} retry={Retry} autosave={Autosave}",
            magMask, retryOnFailure, autosave);

        // Reset any stale calibration state from previously interrupted calibrations
        ResetStaleCalibrationState();

        // Reset compass state
        lock (_compassLock)
        {
            _compassCalState = new CompassCalibrationStateModel
            {
                State = Core.Enums.CompassCalibrationState.Starting,
                Message = "Starting compass calibration..."
            };
            // Do NOT pre-initialize CompassProgress entries here.
            // CompassCount is derived from CompassProgress.Count, and the completion
            // check (CompletedCount == CompassCount) requires both to reflect only
            // the compasses that actually reported. Pre-populating all 3 entries would
            // cause CompassCount=3 on single-compass drones, preventing completion.
        }

        // Set flags BEFORE sending command to avoid race condition where
        // FC's COMMAND_ACK arrives before flags are set
        _inCompassCalibrate = true;
        _isCalibrating = true;
        _activeCalibrationType = CalibrationType.Compass;

        // Start UI update timer BEFORE sending command (like MissionPlanner's timer1)
        // so that progress updates are captured even if FC responds very quickly
        StartCompassUiTimer();
        NotifyCompassStateChanged();

        try
        {
            // Send MAV_CMD_DO_START_MAG_CAL
            await _connectionService.SendStartMagCalAsync(
                magMask,
                retryOnFailure ? 1 : 0,
                autosave ? 1 : 0,
                0,  // no delay
                0   // no autoreboot
            );

            // Don't override compass state here - the OnCommandAckReceived handler
            // for cmd 42424 already sets the state based on the ACK result.
            // Just notify so UI picks up the current state.
            NotifyCompassStateChanged();
            _logger.LogInformation("[CompassCal] Calibration command sent successfully - waiting for FC progress updates");
            return true;
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "[CompassCal] Compass calibration command timed out - FC may not have responded");
            lock (_compassLock)
            {
                _compassCalState.State = Core.Enums.CompassCalibrationState.Failed;
                _compassCalState.Message = "Command timed out. Check connection and try again.";
            }
            _inCompassCalibrate = false;
            _isCalibrating = false;
            StopCompassUiTimer();
            NotifyCompassStateChanged();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CompassCal] Failed to start calibration");
            lock (_compassLock)
            {
                _compassCalState.State = Core.Enums.CompassCalibrationState.Failed;
                _compassCalState.Message = $"Failed to start: {ex.Message}";
            }
            _inCompassCalibrate = false;
            _isCalibrating = false;
            StopCompassUiTimer();
            NotifyCompassStateChanged();
            return false;
        }
    }

    public async Task<bool> AcceptCompassCalibrationAsync()
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("[CompassCal] Not connected - cannot accept calibration");
            return false;
        }

        _logger.LogInformation("[CompassCal] Accepting compass calibration");

        try
        {
            await _connectionService.SendAcceptMagCalAsync(0);

            lock (_compassLock)
            {
                _compassCalState.State = Core.Enums.CompassCalibrationState.Accepted;
                _compassCalState.Message = "Calibration accepted. Please reboot the autopilot.";
            }

            _inCompassCalibrate = false;
            _isCalibrating = false;
            StopCompassUiTimer();
            NotifyCompassStateChanged();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CompassCal] Failed to accept calibration");
            return false;
        }
    }

    public async Task<bool> CancelCompassCalibrationAsync()
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("[CompassCal] Not connected - cannot cancel calibration");
            return false;
        }

        _logger.LogInformation("[CompassCal] Cancelling compass calibration");

        try
        {
            await _connectionService.SendCancelMagCalAsync(0);

            lock (_compassLock)
            {
                _compassCalState.State = Core.Enums.CompassCalibrationState.Cancelled;
                _compassCalState.Message = "Calibration cancelled";
            }

            _inCompassCalibrate = false;
            _isCalibrating = false;
            StopCompassUiTimer();
            NotifyCompassStateChanged();
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CompassCal] Failed to cancel calibration");
            return false;
        }
    }

    private void StartCompassUiTimer()
    {
        StopCompassUiTimer();
        // Use 50ms interval for smoother UI updates (20 FPS)
        _compassUiTimer = new System.Timers.Timer(50);
        _compassUiTimer.Elapsed += (_, _) =>
        {
            // Only notify if calibration is active to reduce overhead
            if (_inCompassCalibrate)
            {
                NotifyCompassStateChanged();
            }
        };
        _compassUiTimer.AutoReset = true;
        _compassUiTimer.Start();
        _logger.LogDebug("[CompassCal] UI timer started (50ms interval)");
    }

    private void StopCompassUiTimer()
    {
        if (_compassUiTimer != null)
        {
            _compassUiTimer.Stop();
            _compassUiTimer.Elapsed -= null!;
            _compassUiTimer.Dispose();
            _compassUiTimer = null;
            _logger.LogDebug("[CompassCal] UI timer stopped");
        }
    }

    /// <summary>
    /// Notifies subscribers that compass calibration state has changed.
    /// Creates a thread-safe copy of the current state for event dispatch.
    /// </summary>
    private void NotifyCompassStateChanged()
    {
        // Create a lightweight copy for the event
        CompassCalibrationStateModel state;
        lock (_compassLock)
        {
            state = new CompassCalibrationStateModel
            {
                State = _compassCalState.State,
                Message = _compassCalState.Message,
                // Use same dictionary references for speed when not modified
                CompassProgress = new Dictionary<byte, int>(_compassCalState.CompassProgress),
                CompassReports = new Dictionary<byte, MagCalReportData>(_compassCalState.CompassReports)
            };
        }

        // Fire event on background thread - let ViewModel handle UI thread dispatch
        CompassCalibrationStateChanged?.Invoke(this, state);
    }

    #endregion
}