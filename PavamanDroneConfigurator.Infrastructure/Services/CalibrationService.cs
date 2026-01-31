using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Calibration service - IMU and Compass calibration implementations have been removed for fresh implementation.
/// Level horizon, barometer, gyroscope, and airspeed calibrations remain functional.
/// </summary>
public class CalibrationService : ICalibrationService
{
    private readonly ILogger<CalibrationService> _logger;
    private readonly IConnectionService _connectionService;
    
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
        IConnectionService connectionService)
    {
        _logger = logger;
        _connectionService = connectionService;

        _connectionService.StatusTextReceived += OnStatusTextReceived;
        _connectionService.CommandAckReceived += OnCommandAckReceived;
    }

    #region Connection Service Event Handlers

    private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
    {
        if (_isCalibrating)
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
        if (_isCalibrating)
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
        _logger.LogInformation("[CalibService] AcceptCalibrationStepAsync - IMU calibration not implemented");
        return Task.FromResult(false);
    }

    /// <summary>
    /// IMU/Accelerometer calibration - IMPLEMENTATION REMOVED
    /// TODO: Implement fresh IMU calibration logic
    /// </summary>
    public Task<bool> StartAccelerometerCalibrationAsync(bool fullSixAxis = true)
    {
        _logger.LogWarning("[CalibService] IMU calibration not yet implemented - pending fresh implementation");
        return Task.FromResult(false);
    }

    /// <summary>
    /// Simple accelerometer calibration - IMPLEMENTATION REMOVED
    /// TODO: Implement fresh IMU calibration logic
    /// </summary>
    public Task<bool> StartSimpleAccelerometerCalibrationAsync()
    {
        _logger.LogWarning("[CalibService] Simple IMU calibration not yet implemented - pending fresh implementation");
        return Task.FromResult(false);
    }

    /// <summary>
    /// Compass calibration - IMPLEMENTATION REMOVED
    /// TODO: Implement fresh compass calibration logic
    /// </summary>
    public Task<bool> StartCompassCalibrationAsync(bool onboardCalibration = false)
    {
        _logger.LogWarning("[CalibService] Compass calibration not yet implemented - pending fresh implementation");
        return Task.FromResult(false);
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

    private static CalibrationStateMachine MapToStateMachine(CalibrationState s) => s switch 
    { 
        CalibrationState.Idle => CalibrationStateMachine.Idle, 
        CalibrationState.Completed => CalibrationStateMachine.Completed, 
        CalibrationState.Failed => CalibrationStateMachine.Failed, 
        _ => CalibrationStateMachine.Sampling 
    };
    
    private static bool IsSuccessMessage(string l) => l.Contains("calibration successful") || l.Contains("calibration complete");
    private static bool IsFailureMessage(string l) => !l.Contains("prearm") && (l.Contains("calibration failed") || l.Contains("cal failed"));

    #endregion
}
