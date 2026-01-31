using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using System.Text;
using System.Text.RegularExpressions;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Calibration service implementing MissionPlanner-style IMU calibration.
/// 
/// KEY BEHAVIOR (from MissionPlanner analysis):
/// 1. First button click: Sends MAV_CMD_PREFLIGHT_CALIBRATION (param5=1)
/// 2. Subscribes to STATUSTEXT and COMMAND_LONG messages
/// 3. FC sends COMMAND_LONG with MAV_CMD_ACCELCAL_VEHICLE_POS requesting position
///    OR FC sends STATUSTEXT with position instruction (fallback for older firmware)
/// 4. User places vehicle and clicks button again
/// 5. GCS sends COMMAND_LONG with MAV_CMD_ACCELCAL_VEHICLE_POS confirming position
/// 6. Repeat 6 times for all positions
/// 7. FC sends STATUSTEXT "calibration successful" or "calibration failed"
/// </summary>
public class CalibrationService : ICalibrationService
{
    private readonly ILogger<CalibrationService> _logger;
    private readonly IConnectionService _connectionService;
    
    // State machine variables (MissionPlanner style)
    private CalibrationStateModel? _currentState;
    private CalibrationDiagnostics? _currentDiagnostics;
    private CalibrationType _activeCalibrationType;
    private bool _isCalibrating;
    private CalibrationStateMachine _stateMachineState = CalibrationStateMachine.Idle;

    // MissionPlanner-style accel calibration state
    private bool _inAccelCalibrate;
    private AccelCalVehiclePosition _currentRequestedPosition;
    private AccelCalVehiclePosition _lastConfirmedPosition; // Track what we last confirmed
    private int _stepCount;
    private readonly HashSet<AccelCalVehiclePosition> _completedPositions = new();
    private bool _waitingForNextPosition; // Flag to track if we're waiting for FC to send next position

    public CalibrationStateModel? CurrentState => _currentState;
    public bool IsCalibrating => _isCalibrating;
    public CalibrationStateMachine StateMachineState => _stateMachineState;
    public CalibrationDiagnostics? CurrentDiagnostics => _currentDiagnostics;

    public event EventHandler<CalibrationStateModel>? CalibrationStateChanged;
    public event EventHandler<CalibrationProgressEventArgs>? CalibrationProgressChanged;
    public event EventHandler<CalibrationStepEventArgs>? CalibrationStepRequired;
    public event EventHandler<CalibrationStatusTextEventArgs>? StatusTextReceived;
    public event EventHandler<AccelCalPositionRequestedEventArgs>? AccelCalPositionRequested;

    public CalibrationService(
        ILogger<CalibrationService> logger,
        IConnectionService connectionService)
    {
        _logger = logger;
        _connectionService = connectionService;

        // Subscribe to MAVLink events
        _connectionService.StatusTextReceived += OnStatusTextReceived;
        _connectionService.CommandAckReceived += OnCommandAckReceived;
        _connectionService.CommandLongReceived += OnCommandLongReceived;
    }

    #region Connection Service Event Handlers

    /// <summary>
    /// Handle STATUSTEXT messages from FC.
    /// MissionPlanner checks for:
    /// - "place vehicle" or "calibration" -> update user instructions
    /// - "calibration successful" -> calibration complete (success)
    /// - "calibration failed" -> calibration complete (failure)
    /// </summary>
    private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
    {
        _logger.LogInformation("[CalibService] STATUSTEXT: [{Severity}] {Text}", e.Severity, e.Text);

        // Always forward STATUSTEXT during any calibration
        if (_isCalibrating)
        {
            StatusTextReceived?.Invoke(this, new CalibrationStatusTextEventArgs
            {
                Severity = e.Severity,
                Text = e.Text,
                Timestamp = DateTime.UtcNow
            });
        }

        // Handle accel calibration STATUSTEXT messages specifically
        if (_inAccelCalibrate)
        {
            HandleAccelCalStatusText(e.Text);
            return;
        }

        // Handle other calibration types
        if (_isCalibrating)
        {
            var lower = e.Text.ToLowerInvariant();
            if (IsSuccessMessage(lower))
            {
                _isCalibrating = false;
                UpdateState(new CalibrationStateModel
                {
                    Type = _activeCalibrationType,
                    State = CalibrationState.Completed,
                    Progress = 100,
                    Message = e.Text
                });
            }
            else if (IsFailureMessage(lower))
            {
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

    /// <summary>
    /// Handle STATUSTEXT during accelerometer calibration (MissionPlanner style).
    /// IMPORTANT: Some firmware versions don't send COMMAND_LONG for position requests,
    /// they only send STATUSTEXT. We need to parse the position from the text as fallback.
    /// </summary>
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

        // Skip PreArm messages - they're not calibration instructions
        if (lower.Contains("prearm"))
        {
            _logger.LogDebug("[AccelCal] Ignoring PreArm message: {Text}", text);
            return;
        }

        // CRITICAL: Parse position from STATUSTEXT if FC doesn't send COMMAND_LONG
        // Only process "place vehicle" messages
        if (!lower.Contains("place vehicle"))
        {
            return;
        }

        var detectedPosition = ParsePositionFromStatusText(lower);
        
        if (detectedPosition != 0)
        {
            _logger.LogInformation("[AccelCal] Detected position from STATUSTEXT: {Position} (last confirmed: {LastConfirmed})", 
                detectedPosition, _lastConfirmedPosition);
            
            // Only update if this is a NEW position request (not the same as what we just confirmed)
            // This prevents re-triggering the same position after we confirm it
            if (detectedPosition != _lastConfirmedPosition || !_waitingForNextPosition)
            {
                _currentRequestedPosition = detectedPosition;
                _waitingForNextPosition = false;
                
                // Raise event for ViewModel to handle
                AccelCalPositionRequested?.Invoke(this, new AccelCalPositionRequestedEventArgs
                {
                    Position = detectedPosition,
                    PositionName = GetPositionName(detectedPosition),
                    StepNumber = _completedPositions.Count + 1,
                    TotalSteps = 6
                });

                UpdateState(new CalibrationStateModel
                {
                    Type = CalibrationType.Accelerometer,
                    State = CalibrationState.WaitingForUserAction,
                    StateMachine = CalibrationStateMachine.WaitingForUserPosition,
                    CurrentPosition = (int)detectedPosition,
                    Message = text,
                    CanConfirmPosition = true,
                    Progress = CalculateAccelProgress()
                });
            }
            else
            {
                _logger.LogDebug("[AccelCal] Ignoring repeat position request for {Position} while waiting for next", detectedPosition);
            }
        }
    }

    /// <summary>
    /// Parse the vehicle position from STATUSTEXT message.
    /// ArduPilot sends messages like:
    /// - "Place vehicle level and press any key."
    /// - "Place vehicle on its LEFT side and press any key."
    /// - "Place vehicle on its RIGHT side and press any key."
    /// - "Place vehicle nose DOWN and press any key."
    /// - "Place vehicle nose UP and press any key."
    /// - "Place vehicle on its BACK and press any key."
    /// </summary>
    private AccelCalVehiclePosition ParsePositionFromStatusText(string lowerText)
    {
        // Order matters - check more specific patterns first
        if (lowerText.Contains("nose down") || lowerText.Contains("nosedown"))
            return AccelCalVehiclePosition.NoseDown;
        
        if (lowerText.Contains("nose up") || lowerText.Contains("noseup"))
            return AccelCalVehiclePosition.NoseUp;
        
        if (lowerText.Contains("left"))
            return AccelCalVehiclePosition.Left;
        
        if (lowerText.Contains("right"))
            return AccelCalVehiclePosition.Right;
        
        if (lowerText.Contains("back") || lowerText.Contains("upside"))
            return AccelCalVehiclePosition.Back;
        
        // Check level last since it's the most common word
        if (lowerText.Contains("level"))
            return AccelCalVehiclePosition.Level;
        
        return 0; // Unknown/not a position message
    }

    /// <summary>
    /// Handle COMMAND_ACK messages from FC.
    /// </summary>
    private void OnCommandAckReceived(object? sender, CommandAckEventArgs e)
    {
        _logger.LogInformation("[CalibService] COMMAND_ACK: cmd={Command} result={Result} success={Success}",
            e.Command, e.Result, e.IsSuccess);

        if (_isCalibrating)
        {
            // MAV_CMD_PREFLIGHT_CALIBRATION = 241
            if (e.Command == 241 && !e.IsSuccess)
            {
                _logger.LogWarning("[CalibService] PREFLIGHT_CALIBRATION rejected by FC: {Result}", e.Result);
                
                // Only fail if we're not in accel calibration (which handles its own flow)
                if (!_inAccelCalibrate)
                {
                    _isCalibrating = false;
                    UpdateState(new CalibrationStateModel
                    {
                        Type = _activeCalibrationType,
                        State = CalibrationState.Failed,
                        Message = $"Calibration rejected by FC (result: {e.Result})"
                    });
                }
            }
            
            // MAV_CMD_ACCELCAL_VEHICLE_POS = 42429
            if (e.Command == 42429 && _inAccelCalibrate)
            {
                if (e.IsSuccess)
                {
                    _logger.LogInformation("[AccelCal] Position {Position} ACK received - ACCEPTED. Waiting for FC to request next position...", 
                        _lastConfirmedPosition);
                    // Position was accepted, FC should now sample and then send next position request
                    // Don't do anything here - wait for STATUSTEXT or COMMAND_LONG with next position
                }
                else
                {
                    _logger.LogWarning("[AccelCal] Position {Position} ACK received - REJECTED (result={Result})", 
                        _currentRequestedPosition, e.Result);
                    // Position was rejected, allow user to retry
                    _waitingForNextPosition = false;
                    UpdateState(new CalibrationStateModel
                    {
                        Type = CalibrationType.Accelerometer,
                        State = CalibrationState.WaitingForUserAction,
                        StateMachine = CalibrationStateMachine.PositionRejected,
                        CurrentPosition = (int)_currentRequestedPosition,
                        Message = $"Position rejected (result: {e.Result}). Please reposition and try again.",
                        CanConfirmPosition = true,
                        Progress = CalculateAccelProgress()
                    });
                }
            }
        }
    }

    /// <summary>
    /// Handle COMMAND_LONG messages from FC.
    /// MissionPlanner: FC sends MAV_CMD_ACCELCAL_VEHICLE_POS to request specific position.
    /// NOTE: Some firmware versions don't send this, only STATUSTEXT.
    /// </summary>
    private void OnCommandLongReceived(object? sender, CommandLongEventArgs e)
    {
        _logger.LogInformation("[CalibService] COMMAND_LONG received: cmd={Command} param1={Param1}", 
            e.Command, e.Param1);
        
        // MAV_CMD_ACCELCAL_VEHICLE_POS = 42429
        if (e.Command == 42429 && _inAccelCalibrate)
        {
            var position = (AccelCalVehiclePosition)(int)e.Param1;
            
            _logger.LogInformation("[AccelCal] FC requested position via COMMAND_LONG: {Position} (param1={Param1})",
                position, e.Param1);

            // Handle terminal states
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

            // Normal position request (1-6) - this overrides any STATUSTEXT-based position
            _currentRequestedPosition = position;
            _waitingForNextPosition = false;
            
            // Raise event for ViewModel to handle
            AccelCalPositionRequested?.Invoke(this, new AccelCalPositionRequestedEventArgs
            {
                Position = position,
                PositionName = GetPositionName(position),
                StepNumber = _completedPositions.Count + 1,
                TotalSteps = 6
            });

            UpdateState(new CalibrationStateModel
            {
                Type = CalibrationType.Accelerometer,
                State = CalibrationState.WaitingForUserAction,
                StateMachine = CalibrationStateMachine.WaitingForUserPosition,
                CurrentPosition = (int)position,
                Message = $"Place vehicle {GetPositionName(position)} and click when done",
                CanConfirmPosition = true,
                Progress = CalculateAccelProgress()
            });

            // Raise step required event
            CalibrationStepRequired?.Invoke(this, new CalibrationStepEventArgs
            {
                Type = CalibrationType.Accelerometer,
                Step = MapPositionToCalibrationStep(position),
                Instructions = $"Place vehicle {GetPositionName(position)}",
                CanConfirm = true
            });
        }
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
        
        _isCalibrating = false;
        _inAccelCalibrate = false;
        _stepCount = 0;
        _completedPositions.Clear();
        _currentRequestedPosition = 0;
        _lastConfirmedPosition = 0;
        _waitingForNextPosition = false;
        
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
    /// Accept the current calibration step - MissionPlanner style.
    /// Sends MAV_CMD_ACCELCAL_VEHICLE_POS to FC confirming the vehicle is in position.
    /// </summary>
    public Task<bool> AcceptCalibrationStepAsync()
    {
        if (!_inAccelCalibrate)
        {
            _logger.LogWarning("[CalibService] AcceptCalibrationStepAsync called but not in accel calibration");
            return Task.FromResult(false);
        }

        if (_currentRequestedPosition == 0)
        {
            _logger.LogWarning("[CalibService] No position requested yet by FC - cannot confirm");
            return Task.FromResult(false);
        }

        _stepCount++;
        
        _logger.LogInformation("[AccelCal] User confirmed position: {Position} (step {Step})",
            _currentRequestedPosition, _stepCount);

        // Mark position as completed and remember what we confirmed
        _completedPositions.Add(_currentRequestedPosition);
        _lastConfirmedPosition = _currentRequestedPosition;
        _waitingForNextPosition = true;

        // MissionPlanner: sendPacket(new mavlink_command_long_t { param1 = (float)pos, command = ACCELCAL_VEHICLE_POS })
        // This is fire-and-forget (no ACK wait)
        _logger.LogInformation("[AccelCal] Sending MAV_CMD_ACCELCAL_VEHICLE_POS param1={Position}", 
            (int)_currentRequestedPosition);
        _connectionService.SendAccelCalVehiclePos((int)_currentRequestedPosition);

        // Update state to show we're waiting for FC to sample/accept
        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Accelerometer,
            State = CalibrationState.InProgress,
            StateMachine = CalibrationStateMachine.WaitingForSampling,
            CurrentPosition = (int)_currentRequestedPosition,
            Message = $"Sampling {GetPositionName(_currentRequestedPosition)}... waiting for FC",
            CanConfirmPosition = false,
            Progress = CalculateAccelProgress()
        });

        // DON'T reset _currentRequestedPosition here - wait for FC to send next position
        // The FC will send a new STATUSTEXT or COMMAND_LONG when it's ready for the next position

        return Task.FromResult(true);
    }

    /// <summary>
    /// Start full 6-position accelerometer calibration (MissionPlanner style).
    /// 
    /// MissionPlanner behavior:
    /// 1. If _incalibrate is true (already calibrating), this click confirms position
    /// 2. If _incalibrate is false, start new calibration
    /// 3. Send MAV_CMD_PREFLIGHT_CALIBRATION with param5=1
    /// 4. Subscribe to STATUSTEXT and COMMAND_LONG
    /// 5. FC will send position requests via COMMAND_LONG or STATUSTEXT
    /// </summary>
    public Task<bool> StartAccelerometerCalibrationAsync(bool fullSixAxis = true)
    {
        // MissionPlanner: if already in calibration, the button click confirms position
        if (_inAccelCalibrate)
        {
            _logger.LogInformation("[AccelCal] Already calibrating - treating click as position confirmation");
            return AcceptCalibrationStepAsync();
        }

        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("[AccelCal] Not connected - cannot start calibration");
            return Task.FromResult(false);
        }

        _logger.LogInformation("[AccelCal] Starting full 6-position accelerometer calibration (MissionPlanner style)");

        // Reset state
        _stepCount = 0;
        _completedPositions.Clear();
        _currentRequestedPosition = 0;
        _lastConfirmedPosition = 0;
        _waitingForNextPosition = false;
        _activeCalibrationType = CalibrationType.Accelerometer;
        _isCalibrating = true;
        _inAccelCalibrate = true;

        // Update UI state
        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Accelerometer,
            State = CalibrationState.InProgress,
            StateMachine = CalibrationStateMachine.WaitingForInstruction,
            Message = "Starting accelerometer calibration...",
            CanConfirmPosition = false,
            Progress = 0
        });

        // Send MAV_CMD_PREFLIGHT_CALIBRATION with param5=1 (full accel cal)
        // MissionPlanner: doCommand returns immediately for accel (no ACK wait)
        // param1=gyro, param2=mag, param3=baro, param4=airspeed, param5=accel
        int accelParam = fullSixAxis ? 1 : 4; // 1=full 6-axis, 4=simple
        _connectionService.SendPreflightCalibration(0, 0, 0, 0, accelParam);

        _logger.LogInformation("[AccelCal] Sent MAV_CMD_PREFLIGHT_CALIBRATION param5={Accel}", accelParam);

        return Task.FromResult(true);
    }

    /// <summary>
    /// Start simple accelerometer calibration (param5=4).
    /// Vehicle must be on level surface. FC performs automatic calibration.
    /// </summary>
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
        _inAccelCalibrate = false; // Simple cal doesn't use the interactive flow

        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Accelerometer,
            State = CalibrationState.InProgress,
            Message = "Simple accelerometer calibration in progress...",
            Progress = 0
        });

        // param5=4 for simple accel calibration
        _connectionService.SendPreflightCalibration(0, 0, 0, 0, 4);

        return Task.FromResult(true);
    }

    /// <summary>
    /// Compass calibration - IMPLEMENTATION PENDING
    /// </summary>
    public Task<bool> StartCompassCalibrationAsync(bool onboardCalibration = false)
    {
        _logger.LogWarning("[CalibService] Compass calibration not yet implemented");
        return Task.FromResult(false);
    }

    public Task<bool> StartGyroscopeCalibrationAsync()
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);
        
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

    /// <summary>
    /// Start level horizon calibration (AHRS trims).
    /// MissionPlanner: param5=2
    /// </summary>
    public Task<bool> StartLevelHorizonCalibrationAsync()
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);
        
        _activeCalibrationType = CalibrationType.Level;
        _isCalibrating = true;
        
        _logger.LogInformation("[CalibService] Starting level horizon calibration (param5=2)");
        
        UpdateState(new CalibrationStateModel 
        { 
            Type = CalibrationType.Level, 
            State = CalibrationState.InProgress, 
            Message = "Level calibration in progress - keep vehicle level..." 
        });
        
        _connectionService.SendPreflightCalibration(0, 0, 0, 0, 2);
        return Task.FromResult(true);
    }

    public Task<bool> StartBarometerCalibrationAsync()
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);
        
        _activeCalibrationType = CalibrationType.Barometer;
        _isCalibrating = true;
        
        UpdateState(new CalibrationStateModel 
        { 
            Type = CalibrationType.Barometer, 
            State = CalibrationState.InProgress, 
            Message = "Barometer calibration in progress..." 
        });
        
        _connectionService.SendPreflightCalibration(0, 0, 1, 0, 0);
        return Task.FromResult(true);
    }

    public Task<bool> StartAirspeedCalibrationAsync()
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);
        
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

    private void CompleteAccelCalibration(bool success, string message)
    {
        _logger.LogInformation("[AccelCal] Completing calibration: success={Success} message={Message}",
            success, message);

        _isCalibrating = false;
        _inAccelCalibrate = false;
        _currentRequestedPosition = 0;
        _lastConfirmedPosition = 0;
        _waitingForNextPosition = false;

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
        _currentState = state;
        _stateMachineState = state.StateMachine;
        
        _logger.LogDebug("[CalibService] State update: Type={Type} State={State} SM={SM} Msg={Msg}",
            state.Type, state.State, state.StateMachine, state.Message);
        
        CalibrationStateChanged?.Invoke(this, state);

        // Also raise progress event for UI binding
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

    private int CalculateAccelProgress()
    {
        // Progress based on completed positions (6 total)
        return (_completedPositions.Count * 100) / 6;
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

    private static bool IsSuccessMessage(string lower) =>
        lower.Contains("calibration successful") || lower.Contains("calibration complete");

    private static bool IsFailureMessage(string lower) =>
        !lower.Contains("prearm") && (lower.Contains("calibration failed") || lower.Contains("cal failed"));

    #endregion
}
