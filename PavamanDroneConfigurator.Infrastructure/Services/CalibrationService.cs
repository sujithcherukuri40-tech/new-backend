using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Calibration service implementing MissionPlanner-style IMU and Compass calibration.
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

    // Accel calibration state
    private bool _inAccelCalibrate;
    private AccelCalVehiclePosition _currentRequestedPosition;
    private AccelCalVehiclePosition _lastConfirmedPosition;
    private int _stepCount;
    private readonly HashSet<AccelCalVehiclePosition> _completedPositions = new();
    private bool _waitingForNextPosition;

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

        if (_isCalibrating)
        {
            var lower = e.Text.ToLowerInvariant();
            if (lower.Contains("calibration successful") || lower.Contains("calibration complete"))
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
            else if (!lower.Contains("prearm") && (lower.Contains("calibration failed") || lower.Contains("cal failed")))
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

        // Only process "place vehicle" messages
        if (!lower.Contains("place vehicle"))
        {
            return;
        }

        var detectedPosition = ParsePositionFromStatusText(lower);
        
        if (detectedPosition != 0)
        {
            _logger.LogInformation("[AccelCal] Detected position from STATUSTEXT: {Position} (value={Value}, lastConfirmed={LastConfirmed}, waiting={Waiting})", 
                detectedPosition, (int)detectedPosition, _lastConfirmedPosition, _waitingForNextPosition);
            
            // Only update if this is a NEW position request
            if (detectedPosition != _lastConfirmedPosition || !_waitingForNextPosition)
            {
                _currentRequestedPosition = detectedPosition;
                _waitingForNextPosition = false;
                _stepCount = _completedPositions.Count;
                
                AccelCalPositionRequested?.Invoke(this, new AccelCalPositionRequestedEventArgs
                {
                    Position = detectedPosition,
                    PositionName = GetPositionName(detectedPosition),
                    StepNumber = _stepCount + 1,
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
                    Progress = (_completedPositions.Count * 100) / 6
                });
            }
            else
            {
                _logger.LogDebug("[AccelCal] Ignoring repeat position request for {Position}", detectedPosition);
            }
        }
    }

    private AccelCalVehiclePosition ParsePositionFromStatusText(string lowerText)
    {
        // Order matters - check more specific patterns first
        if (lowerText.Contains("nose down") || lowerText.Contains("nosedown")) return AccelCalVehiclePosition.NoseDown;
        if (lowerText.Contains("nose up") || lowerText.Contains("noseup")) return AccelCalVehiclePosition.NoseUp;
        if (lowerText.Contains("left")) return AccelCalVehiclePosition.Left;
        if (lowerText.Contains("right")) return AccelCalVehiclePosition.Right;
        if (lowerText.Contains("back") || lowerText.Contains("upside")) return AccelCalVehiclePosition.Back;
        if (lowerText.Contains("level")) return AccelCalVehiclePosition.Level;
        return 0;
    }

    private void OnCommandAckReceived(object? sender, CommandAckEventArgs e)
    {
        _logger.LogInformation("[CalibService] COMMAND_ACK: cmd={Command} result={Result} success={Success}",
            e.Command, e.Result, e.IsSuccess);

        if (_isCalibrating)
        {
            // MAV_CMD_PREFLIGHT_CALIBRATION = 241
            // CRITICAL: Only transition to calibration state AFTER FC acknowledges
            if (e.Command == 241)
            {
                if (e.IsSuccess)
                {
                    // FC accepted calibration command - NOW we can update state
                    _logger.LogInformation("[CalibService] PREFLIGHT_CALIBRATION ACCEPTED by FC - entering calibration mode");
                    
                    if (_inAccelCalibrate)
                    {
                        UpdateState(new CalibrationStateModel
                        {
                            Type = CalibrationType.Accelerometer,
                            State = CalibrationState.InProgress,
                            StateMachine = CalibrationStateMachine.WaitingForInstruction,
                            Message = "Waiting for FC to request position...",
                            CanConfirmPosition = false,
                            Progress = 0
                        });
                    }
                }
                else
                {
                    // FC rejected calibration - abort
                    _logger.LogWarning("[CalibService] PREFLIGHT_CALIBRATION REJECTED by FC: result={Result}", e.Result);
                    
                    _isCalibrating = false;
                    _inAccelCalibrate = false;
                    
                    UpdateState(new CalibrationStateModel
                    {
                        Type = _activeCalibrationType,
                        State = CalibrationState.Failed,
                        StateMachine = CalibrationStateMachine.Rejected,
                        Message = $"Calibration rejected by FC (result: {e.Result}). Check PreArm conditions.",
                        CanConfirmPosition = false
                    });
                }
                return;
            }
            
            // MAV_CMD_ACCELCAL_VEHICLE_POS = 42429
            if (e.Command == 42429 && _inAccelCalibrate)
            {
                if (e.IsSuccess)
                {
                    // CRITICAL FIX: Mark position as completed IMMEDIATELY on ACK ACCEPTED
                    // ArduPilot does NOT send a "sampling done" message - it silently samples
                    // then sends the next position request. We must NOT wait in WaitingForSampling.
                    _logger.LogInformation("[AccelCal] Position {Position} ACK ACCEPTED. Completed, waiting for next.", 
                        _lastConfirmedPosition);
                    
                    // _currentRequestedPosition is already cleared in AcceptCalibrationStepAsync
                    // _waitingForNextPosition remains true until FC sends next position request
                    
                    // UX IMPROVEMENT: Clear messaging about what's happening
                    // FC will take 1-3 seconds to prepare and send the next position request
                    var stepsDone = _completedPositions.Count;
                    var stepsRemaining = 6 - stepsDone;
                    var positionName = GetPositionName(_lastConfirmedPosition);
                    
                    UpdateState(new CalibrationStateModel
                    {
                        Type = CalibrationType.Accelerometer,
                        State = CalibrationState.InProgress,
                        StateMachine = CalibrationStateMachine.WaitingForInstruction,
                        CurrentPosition = (int)_lastConfirmedPosition,
                        Message = stepsRemaining > 0 
                            ? $"? {positionName} complete! Preparing next position... ({stepsDone}/6)"
                            : $"? {positionName} complete! Finalizing calibration...",
                        CanConfirmPosition = false, // Button stays disabled until FC requests next position
                        Progress = (stepsDone * 100) / 6
                    });
                }
                else
                {
                    // Position REJECTED by FC - allow retry
                    _logger.LogWarning("[AccelCal] Position {Position} ACK REJECTED (result={Result}). Allow retry.", 
                        _lastConfirmedPosition, e.Result);
                    
                    // CRITICAL: Remove from completed positions since it was rejected
                    _completedPositions.Remove(_lastConfirmedPosition);
                    
                    // CRITICAL: Restore _currentRequestedPosition so user can retry
                    _currentRequestedPosition = _lastConfirmedPosition;
                    _waitingForNextPosition = false;
                    
                    UpdateState(new CalibrationStateModel
                    {
                        Type = CalibrationType.Accelerometer,
                        State = CalibrationState.WaitingForUserAction,
                        StateMachine = CalibrationStateMachine.PositionRejected,
                        CurrentPosition = (int)_currentRequestedPosition,
                        Message = $"Position rejected (result: {e.Result}). Reposition and try again.",
                        CanConfirmPosition = true, // Re-enable button for retry
                        Progress = (_completedPositions.Count * 100) / 6
                    });
                }
            }
        }
    }

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

            _currentRequestedPosition = position;
            _waitingForNextPosition = false;
            
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
                Progress = (_completedPositions.Count * 100) / 6
            });

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
    /// Accept the current calibration step.
    /// 
    /// CRITICAL: ArduPilot MAVLink ACCELCAL_VEHICLE_POS enum uses values 1-6:
    /// - ACCELCAL_VEHICLE_POS_LEVEL = 1
    /// - ACCELCAL_VEHICLE_POS_LEFT = 2
    /// - ACCELCAL_VEHICLE_POS_RIGHT = 3
    /// - ACCELCAL_VEHICLE_POS_NOSEDOWN = 4
    /// - ACCELCAL_VEHICLE_POS_NOSEUP = 5
    /// - ACCELCAL_VEHICLE_POS_BACK = 6
    /// 
    /// We must send the POSITION ENUM VALUE (1-6) directly!
    /// 
    /// MISSION PLANNER RULE: User can ONLY click when FC has requested a position.
    /// If no position is requested (currentRequestedPosition == 0), ignore the click.
    /// </summary>
    public Task<bool> AcceptCalibrationStepAsync()
    {
        if (!_inAccelCalibrate)
        {
            _logger.LogWarning("[CalibService] AcceptCalibrationStepAsync called but not in accel calibration");
            return Task.FromResult(false);
        }

        // CRITICAL GUARD: Block if no position is requested by FC
        // This prevents user from clicking again after a position is completed
        // but before FC requests the next position
        if (_currentRequestedPosition == 0)
        {
            _logger.LogInformation("[AccelCal] Ignoring click - no position requested by FC. Waiting for next position request.");
            return Task.FromResult(false);
        }

        // GUARD: Block if we're already waiting for sampling to complete
        if (_waitingForNextPosition)
        {
            _logger.LogInformation("[AccelCal] Ignoring click - already waiting for FC to complete sampling of {Position}", 
                _lastConfirmedPosition);
            return Task.FromResult(false);
        }

        // GUARD: Block if this position is already completed
        if (_completedPositions.Contains(_currentRequestedPosition))
        {
            _logger.LogInformation("[AccelCal] Ignoring click - position {Position} already completed", 
                _currentRequestedPosition);
            return Task.FromResult(false);
        }

        // Use the POSITION ENUM VALUE (1-6) directly - DO NOT convert!
        // AccelCalVehiclePosition.Level = 1, Left = 2, Right = 3, NoseDown = 4, NoseUp = 5, Back = 6
        int positionParam = (int)_currentRequestedPosition;
        
        _logger.LogInformation("[AccelCal] User confirmed position: {Position}, sending param1={PositionValue} (MAVLink enum value 1-6)", 
            _currentRequestedPosition, positionParam);

        _completedPositions.Add(_currentRequestedPosition);
        _lastConfirmedPosition = _currentRequestedPosition;
        _waitingForNextPosition = true;
        _stepCount = _completedPositions.Count;

        // CRITICAL: Clear currentRequestedPosition IMMEDIATELY after confirming
        // This prevents double-sends if user clicks again before ACK is received
        var confirmedPosition = _currentRequestedPosition;
        _currentRequestedPosition = 0;

        // Send MAV_CMD_ACCELCAL_VEHICLE_POS with position enum value (1-6)
        _logger.LogInformation("[AccelCal] Sending MAV_CMD_ACCELCAL_VEHICLE_POS param1={PositionValue}", positionParam);
        _connectionService.SendAccelCalVehiclePos(positionParam);

        // UX IMPROVEMENT: Set clear expectations about timing
        // ArduPilot samples 400-500 readings at 100-200Hz = 2-5 seconds per position
        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Accelerometer,
            State = CalibrationState.InProgress,
            StateMachine = CalibrationStateMachine.WaitingForSampling,
            CurrentPosition = (int)confirmedPosition,
            Message = $"Sampling {GetPositionName(confirmedPosition)}... do not move (this takes a few seconds)",
            CanConfirmPosition = false, // CRITICAL: Disable button while sampling
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

        _logger.LogInformation("[AccelCal] Starting full 6-position accelerometer calibration");

        // CRITICAL: Disable arming checks for bench testing without RC/safety switch
        _logger.LogInformation("[AccelCal] Disabling arming checks (ARMING_CHECK=0, BRD_SAFETY_DEFLT=0)...");

        try
        {
            // Disable all arming checks (allows calibration without RC, safety switch, etc.)
            await _parameterService.SetParameterAsync("ARMING_CHECK", 0);
            await Task.Delay(100);
            
            // Disable safety switch requirement
            await _parameterService.SetParameterAsync("BRD_SAFETY_DEFLT", 0);
            await Task.Delay(100);
            
            _logger.LogInformation("[AccelCal] Arming checks disabled successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AccelCal] Failed to disable arming checks - continuing anyway");
        }

        // Reset state - but DO NOT update UI state yet!
        // State will be updated ONLY when we receive COMMAND_ACK from FC
        _stepCount = 0;
        _completedPositions.Clear();
        _currentRequestedPosition = 0;
        _lastConfirmedPosition = 0;
        _waitingForNextPosition = false;
        _activeCalibrationType = CalibrationType.Accelerometer;
        _isCalibrating = true;
        _inAccelCalibrate = true;

        // Send MAV_CMD_PREFLIGHT_CALIBRATION
        // DO NOT update state here - wait for COMMAND_ACK!
        int accelParam = fullSixAxis ? 1 : 4;
        
        _logger.LogInformation("[AccelCal] Sending MAV_CMD_PREFLIGHT_CALIBRATION with param5={Accel} - waiting for ACK...", accelParam);
        _connectionService.SendPreflightCalibration(0, 0, 0, 0, accelParam);

        // Note: State will be updated in OnCommandAckReceived when FC responds
        // This is the correct Mission Planner behavior - don't assume success!
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
        // Route to onboard calibration by default
        return StartOnboardCompassCalibrationAsync(0, true, true);
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
        // CRITICAL: Always include completed positions for UI to show green indicators
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

    private static bool IsSuccessMessage(string lower) =>
        lower.Contains("calibration successful") || lower.Contains("calibration complete");

    private static bool IsFailureMessage(string lower) =>
        !lower.Contains("prearm") && (lower.Contains("calibration failed") || lower.Contains("cal failed"));

    #endregion

    #region Compass Calibration

    private void OnMagCalProgressReceived(object? sender, MagCalProgressEventArgs e)
    {
        if (!_inCompassCalibrate)
            return;

        _logger.LogDebug("[CompassCal] Progress: compass={CompassId} status={Status} pct={Pct}%",
            e.CompassId, e.CalStatus, e.CompletionPct);

        lock (_compassLock)
        {
            // Update progress for this compass
            _compassCalState.CompassProgress[e.CompassId] = e.CompletionPct;
            
            // Update overall state based on status
            var status = (MagCalStatus)e.CalStatus;
            if (status == MagCalStatus.RunningStepOne)
                _compassCalState.State = Core.Enums.CompassCalibrationState.RunningSphereFit;
            else if (status == MagCalStatus.RunningStepTwo)
                _compassCalState.State = Core.Enums.CompassCalibrationState.RunningEllipsoidFit;
            
            _compassCalState.Message = $"Calibrating compass {e.CompassId}: {e.CompletionPct}%";
        }

        // Raise event
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

        // Reset state
        lock (_compassLock)
        {
            _compassCalState = new CompassCalibrationStateModel
            {
                State = Core.Enums.CompassCalibrationState.Starting,
                Message = "Starting compass calibration..."
            };
        }

        _inCompassCalibrate = true;
        _isCalibrating = true;
        _activeCalibrationType = CalibrationType.Compass;

        // Start UI update timer (like MissionPlanner's timer1)
        StartCompassUiTimer();

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

            NotifyCompassStateChanged();
            return true;
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
        _compassUiTimer = new System.Timers.Timer(100); // 100ms like MissionPlanner
        _compassUiTimer.Elapsed += (_, _) => NotifyCompassStateChanged();
        _compassUiTimer.Start();
    }

    private void StopCompassUiTimer()
    {
        _compassUiTimer?.Stop();
        _compassUiTimer?.Dispose();
        _compassUiTimer = null;
    }

    private void NotifyCompassStateChanged()
    {
        CompassCalibrationStateModel? state;
        lock (_compassLock)
        {
            state = new CompassCalibrationStateModel
            {
                State = _compassCalState.State,
                Message = _compassCalState.Message,
                CompassProgress = new Dictionary<byte, int>(_compassCalState.CompassProgress),
                CompassReports = new Dictionary<byte, MagCalReportData>(_compassCalState.CompassReports)
            };
        }
        CompassCalibrationStateChanged?.Invoke(this, state);
    }

    #endregion
}
