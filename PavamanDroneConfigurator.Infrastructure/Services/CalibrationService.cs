using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Main calibration service - delegates to AccelerometerCalibrationService for accel calibration.
/// Matches MissionPlanner's calibration handling philosophy.
/// </summary>
public class CalibrationService : ICalibrationService
{
    private readonly ILogger<CalibrationService> _logger;
    private readonly IConnectionService _connectionService;
    private readonly AccelerometerCalibrationService _accelService;
    
    private CalibrationStateModel? _currentState;
    private CalibrationDiagnostics? _currentDiagnostics;
    private CalibrationType _activeCalibrationType;
    private bool _isCalibrating;
    private CalibrationStateMachine _stateMachineState = CalibrationStateMachine.Idle;

    public CalibrationStateModel? CurrentState => _currentState;
    public bool IsCalibrating => _isCalibrating;
    public CalibrationStateMachine StateMachineState => _stateMachineState;
    public CalibrationDiagnostics? CurrentDiagnostics => _currentDiagnostics;

    public event EventHandler<CalibrationStateModel>? CalibrationStateChanged;
    public event EventHandler<CalibrationProgressEventArgs>? CalibrationProgressChanged;
    public event EventHandler<CalibrationStepEventArgs>? CalibrationStepRequired;
    public event EventHandler<CalibrationStatusTextEventArgs>? StatusTextReceived;

    public CalibrationService(
        ILogger<CalibrationService> logger,
        IConnectionService connectionService,
        AccelerometerCalibrationService accelService)
    {
        _logger = logger;
        _connectionService = connectionService;
        _accelService = accelService;

        _accelService.StateChanged += OnAccelStateChanged;
        _accelService.PositionRequested += OnAccelPositionRequested;
        _accelService.CalibrationCompleted += OnAccelCalibrationCompleted;
        _accelService.StatusTextReceived += OnAccelStatusTextReceived;

        _connectionService.StatusTextReceived += OnStatusTextReceived;
        _connectionService.CommandAckReceived += OnCommandAckReceived;
    }

    #region Accelerometer Calibration Event Handlers

    private void OnAccelStateChanged(object? sender, AccelCalibrationStateChangedEventArgs e)
    {
        _logger.LogInformation("[CalibService] Accel state: {Old} -> {New}", e.OldState, e.NewState);
        
        _isCalibrating = _accelService.IsCalibrating;
        var position = _accelService.CurrentPosition;
        var canConfirm = _accelService.CanConfirmPosition;
        
        string message = e.NewState switch
        {
            AccelCalibrationState.WaitingForFirstPosition => "Waiting for FC to request position...",
            AccelCalibrationState.WaitingForUserConfirmation => canConfirm 
                ? $"Place vehicle {GetPositionName(position)} and click button" 
                : "Waiting for FC...",
            AccelCalibrationState.Completed => "Calibration completed successfully!",
            AccelCalibrationState.Failed => "Calibration failed",
            AccelCalibrationState.Cancelled => "Calibration cancelled",
            _ => "Calibration in progress..."
        };
        
        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Accelerometer,
            State = MapAccelState(e.NewState),
            Message = message,
            CurrentPosition = position,
            CanConfirmPosition = canConfirm
        });
    }

    private void OnAccelPositionRequested(object? sender, AccelPositionRequestedEventArgs e)
    {
        _logger.LogInformation("[CalibService] FC requested position {Position} ({Name})", e.Position, e.PositionName);
        
        // Only allow confirmation when we have a valid position (1-6)
        bool canConfirm = e.Position >= 1 && e.Position <= 6;
        
        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Accelerometer,
            State = CalibrationState.InProgress,
            Message = e.FcMessage,
            CurrentPosition = e.Position,
            CanConfirmPosition = canConfirm  // Only true for valid positions
        });
        
        // Only raise step required for valid positions
        if (canConfirm)
        {
            CalibrationStepRequired?.Invoke(this, new CalibrationStepEventArgs
            {
                Type = CalibrationType.Accelerometer,
                Step = MapPositionToStep(e.Position),
                Instructions = e.FcMessage,
                CanConfirm = true
            });

            CalibrationProgressChanged?.Invoke(this, new CalibrationProgressEventArgs
            {
                Type = CalibrationType.Accelerometer,
                ProgressPercent = (int)((e.Position - 1) * 100.0 / 6),
                StatusText = e.FcMessage,
                CurrentStep = e.Position,
                TotalSteps = 6,
                StateMachine = CalibrationStateMachine.WaitingForUserPosition
            });
        }
        else
        {
            // Position 0 means we're waiting for FC to send next position
            CalibrationProgressChanged?.Invoke(this, new CalibrationProgressEventArgs
            {
                Type = CalibrationType.Accelerometer,
                ProgressPercent = -1, // Indeterminate
                StatusText = e.FcMessage,
                CurrentStep = 0,
                TotalSteps = 6,
                StateMachine = CalibrationStateMachine.Sampling
            });
        }
    }

    private void OnAccelCalibrationCompleted(object? sender, AccelCalibrationCompletedEventArgs e)
    {
        _logger.LogInformation("[CalibService] Calibration {Result}: {Message}", e.Result, e.Message);
        _isCalibrating = false;
        
        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Accelerometer,
            State = e.Result == AccelCalibrationResult.Success ? CalibrationState.Completed : CalibrationState.Failed,
            Progress = e.Result == AccelCalibrationResult.Success ? 100 : 0,
            Message = e.Message,
            CanConfirmPosition = false
        });
    }

    private void OnAccelStatusTextReceived(object? sender, AccelStatusTextEventArgs e)
    {
        StatusTextReceived?.Invoke(this, new CalibrationStatusTextEventArgs
        {
            Severity = e.Severity,
            Text = e.Text,
            Timestamp = e.Timestamp
        });
    }

    #endregion

    #region Connection Service Event Handlers

    private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
    {
        if (_isCalibrating && _activeCalibrationType != CalibrationType.Accelerometer)
        {
            StatusTextReceived?.Invoke(this, new CalibrationStatusTextEventArgs
            {
                Severity = e.Severity,
                Text = e.Text,
                Timestamp = DateTime.UtcNow
            });

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

    private void OnCommandAckReceived(object? sender, CommandAckEventArgs e)
    {
        if (_isCalibrating && _activeCalibrationType != CalibrationType.Accelerometer)
        {
            if (e.Command == 241 && !e.IsSuccess)
            {
                _isCalibrating = false;
                UpdateState(new CalibrationStateModel
                {
                    Type = _activeCalibrationType,
                    State = CalibrationState.Failed,
                    Message = $"Calibration rejected (code: {e.Result})"
                });
            }
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
        if (_activeCalibrationType == CalibrationType.Accelerometer)
            _accelService.CancelCalibration();
        
        _isCalibrating = false;
        UpdateState(new CalibrationStateModel
        {
            Type = _activeCalibrationType,
            State = CalibrationState.Idle,
            Message = "Cancelled",
            CanConfirmPosition = false
        });
        return Task.FromResult(true);
    }

    public Task<bool> AcceptCalibrationStepAsync()
    {
        _logger.LogInformation("[CalibService] AcceptCalibrationStepAsync");
        
        if (_activeCalibrationType == CalibrationType.Accelerometer)
        {
            if (!_accelService.CanConfirmPosition)
            {
                _logger.LogWarning("[CalibService] Cannot confirm - pos={Pos}", _accelService.CurrentPosition);
                return Task.FromResult(false);
            }
            return Task.FromResult(_accelService.ConfirmPosition());
        }
        return Task.FromResult(false);
    }

    public Task<bool> StartAccelerometerCalibrationAsync(bool fullSixAxis = true)
    {
        _logger.LogInformation("[CalibService] Starting accel calibration");
        _activeCalibrationType = CalibrationType.Accelerometer;
        _isCalibrating = true;
        
        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Accelerometer,
            State = CalibrationState.InProgress,
            Message = "Starting calibration... waiting for FC",
            CurrentPosition = 0,
            CanConfirmPosition = false
        });

        return Task.FromResult(_accelService.StartCalibration());
    }

    public Task<bool> StartSimpleAccelerometerCalibrationAsync()
    {
        _activeCalibrationType = CalibrationType.Accelerometer;
        _isCalibrating = true;
        UpdateState(new CalibrationStateModel
        {
            Type = CalibrationType.Accelerometer,
            State = CalibrationState.InProgress,
            Message = "Starting simple calibration...",
            CanConfirmPosition = false
        });
        return Task.FromResult(_accelService.StartCalibration());
    }

    public Task<bool> StartCompassCalibrationAsync(bool onboardCalibration = false)
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);
        _activeCalibrationType = CalibrationType.Compass;
        _isCalibrating = true;
        UpdateState(new CalibrationStateModel { Type = CalibrationType.Compass, State = CalibrationState.InProgress, Message = "Starting compass calibration..." });
        _connectionService.SendPreflightCalibration(0, onboardCalibration ? 76 : 1, 0, 0, 0);
        return Task.FromResult(true);
    }

    public Task<bool> StartGyroscopeCalibrationAsync()
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);
        _activeCalibrationType = CalibrationType.Gyroscope;
        _isCalibrating = true;
        UpdateState(new CalibrationStateModel { Type = CalibrationType.Gyroscope, State = CalibrationState.InProgress, Message = "Keep still - calibrating gyro..." });
        _connectionService.SendPreflightCalibration(1, 0, 0, 0, 0);
        return Task.FromResult(true);
    }

    public Task<bool> StartLevelHorizonCalibrationAsync()
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);
        _activeCalibrationType = CalibrationType.Level;
        _isCalibrating = true;
        UpdateState(new CalibrationStateModel { Type = CalibrationType.Level, State = CalibrationState.InProgress, Message = "Starting level calibration..." });
        _connectionService.SendPreflightCalibration(0, 0, 0, 0, 2);
        return Task.FromResult(true);
    }

    public Task<bool> StartBarometerCalibrationAsync()
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);
        _activeCalibrationType = CalibrationType.Barometer;
        _isCalibrating = true;
        UpdateState(new CalibrationStateModel { Type = CalibrationType.Barometer, State = CalibrationState.InProgress, Message = "Starting baro calibration..." });
        _connectionService.SendPreflightCalibration(0, 0, 1, 0, 0);
        return Task.FromResult(true);
    }

    public Task<bool> StartAirspeedCalibrationAsync()
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);
        _activeCalibrationType = CalibrationType.Airspeed;
        _isCalibrating = true;
        UpdateState(new CalibrationStateModel { Type = CalibrationType.Airspeed, State = CalibrationState.InProgress, Message = "Starting airspeed calibration..." });
        _connectionService.SendPreflightCalibration(0, 0, 0, 1, 0);
        return Task.FromResult(true);
    }

    public Task<bool> RebootFlightControllerAsync()
    {
        if (!_connectionService.IsConnected) return Task.FromResult(false);
        _connectionService.SendPreflightReboot(1, 0);
        return Task.FromResult(true);
    }

    #endregion

    #region Helpers

    private void UpdateState(CalibrationStateModel state)
    {
        _currentState = state;
        _stateMachineState = MapToStateMachine(state.State);
        CalibrationStateChanged?.Invoke(this, state);
    }

    private static string GetPositionName(int pos) => pos switch { 1 => "LEVEL", 2 => "LEFT", 3 => "RIGHT", 4 => "NOSEDOWN", 5 => "NOSEUP", 6 => "BACK", _ => $"pos {pos}" };
    private static CalibrationState MapAccelState(AccelCalibrationState s) => s switch { AccelCalibrationState.Idle => CalibrationState.Idle, AccelCalibrationState.Completed => CalibrationState.Completed, AccelCalibrationState.Failed or AccelCalibrationState.Cancelled => CalibrationState.Failed, _ => CalibrationState.InProgress };
    private static CalibrationStep MapPositionToStep(int p) => p switch { 1 => CalibrationStep.Level, 2 => CalibrationStep.LeftSide, 3 => CalibrationStep.RightSide, 4 => CalibrationStep.NoseDown, 5 => CalibrationStep.NoseUp, 6 => CalibrationStep.Back, _ => CalibrationStep.Level };
    private static CalibrationStateMachine MapToStateMachine(CalibrationState s) => s switch { CalibrationState.Idle => CalibrationStateMachine.Idle, CalibrationState.Completed => CalibrationStateMachine.Completed, CalibrationState.Failed => CalibrationStateMachine.Failed, _ => CalibrationStateMachine.Sampling };
    private static bool IsSuccessMessage(string l) => l.Contains("calibration successful") || l.Contains("calibration complete");
    private static bool IsFailureMessage(string l) => !l.Contains("prearm") && (l.Contains("calibration failed") || l.Contains("cal failed"));

    #endregion
}
