using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Calibration service matching Mission Planner's exact behavior.
/// 
/// CRITICAL: For accelerometer calibration, the FC validates positions using its IMU.
/// We do NOT auto-advance. We wait for FC to confirm each position via STATUSTEXT.
/// 
/// Mission Planner flow:
/// 1. Send MAV_CMD_PREFLIGHT_CALIBRATION (param5=4)
/// 2. FC sends STATUSTEXT "Place vehicle level and press any key"
/// 3. User clicks button -> send MAV_CMD_ACCELCAL_VEHICLE_POS(1)
/// 4. FC validates using IMU, if correct: sends "Place vehicle on its left side..."
/// 5. If wrong: FC sends "rotation bad" or similar, user must retry
/// 6. Repeat for all 6 positions
/// 7. FC sends "Calibration successful" when done
/// </summary>
public class CalibrationService : ICalibrationService, IDisposable
{
    private readonly ILogger<CalibrationService> _logger;
    private readonly IConnectionService _connectionService;
    private readonly AccelerometerCalibrationService? _accelCalService;
    
    // State tracking
    private readonly object _lock = new();
    private CalibrationType _currentType;
    private int _currentPosition;
    private int _expectedPosition; // Position FC is waiting for
    private bool _isCalibrating;
    private bool _waitingForFcResponse; // True after sending position, waiting for FC
    private CalibrationStateMachine _state = CalibrationStateMachine.Idle;
    private DateTime _calibrationStartTime;
    private DateTime _positionSentTime;
    private CancellationTokenSource? _calibrationCts;
    private bool _disposed;
    
    // Timeouts
    private const int FC_RESPONSE_TIMEOUT_MS = 10000; // Wait up to 10s for FC to validate position
    private const int COMMAND_ACK_TIMEOUT_MS = 5000;
    
    private CalibrationStateModel _currentState = new();
    
    private static readonly string[] PositionNames = { "LEVEL", "LEFT", "RIGHT", "NOSE DOWN", "NOSE UP", "BACK" };
    private static readonly string[] PositionInstructions = {
        "Place vehicle LEVEL on a flat surface",
        "Place vehicle on its LEFT side",
        "Place vehicle on its RIGHT side",
        "Place vehicle NOSE DOWN",
        "Place vehicle NOSE UP",
        "Place vehicle on its BACK (upside down)"
    };

    public CalibrationStateModel? CurrentState => _currentState;
    public bool IsCalibrating { get { lock (_lock) return _isCalibrating; } }
    public CalibrationStateMachine StateMachineState { get { lock (_lock) return _state; } }
    public CalibrationDiagnostics? CurrentDiagnostics => null;

    public event EventHandler<CalibrationStateModel>? CalibrationStateChanged;
    public event EventHandler<CalibrationProgressEventArgs>? CalibrationProgressChanged;
    public event EventHandler<CalibrationStepEventArgs>? CalibrationStepRequired;
    public event EventHandler<CalibrationStatusTextEventArgs>? StatusTextReceived;

    public CalibrationService(
        ILogger<CalibrationService> logger,
        IConnectionService connectionService,
        AccelerometerCalibrationService? accelCalService = null)
    {
        _logger = logger;
        _connectionService = connectionService;
        _accelCalService = accelCalService;
        
        _connectionService.StatusTextReceived += OnStatusTextReceived;
        _connectionService.CommandAckReceived += OnCommandAckReceived;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        
        // Subscribe to AccelerometerCalibrationService events if available
        if (_accelCalService != null)
        {
            _accelCalService.StateChanged += OnAccelStateChanged;
            _accelCalService.PositionRequested += OnAccelPositionRequested;
            _accelCalService.PositionValidated += OnAccelPositionValidated;
            _accelCalService.CalibrationCompleted += OnAccelCalibrationCompleted;
        }
    }
    
    #region AccelerometerCalibrationService Event Handlers
    
    private void OnAccelStateChanged(object? sender, AccelCalibrationStateChangedEventArgs e)
    {
        // Map AccelCalibrationState to CalibrationStateMachine
        var mappedState = e.NewState switch
        {
            AccelCalibrationState.Idle => CalibrationStateMachine.Idle,
            AccelCalibrationState.CommandSent => CalibrationStateMachine.WaitingForAck,
            AccelCalibrationState.WaitingForFirstPosition => CalibrationStateMachine.WaitingForInstruction,
            AccelCalibrationState.WaitingForUserConfirmation => CalibrationStateMachine.WaitingForUserPosition,
            AccelCalibrationState.ValidatingPosition => CalibrationStateMachine.ValidatingPosition,
            AccelCalibrationState.SendingPositionToFC => CalibrationStateMachine.WaitingForSampling,
            AccelCalibrationState.FCSampling => CalibrationStateMachine.Sampling,
            AccelCalibrationState.PositionRejected => CalibrationStateMachine.PositionRejected,
            AccelCalibrationState.Completed => CalibrationStateMachine.Completed,
            AccelCalibrationState.Failed => CalibrationStateMachine.Failed,
            AccelCalibrationState.Cancelled => CalibrationStateMachine.Cancelled,
            AccelCalibrationState.Rejected => CalibrationStateMachine.Rejected,
            _ => CalibrationStateMachine.Idle
        };
        
        lock (_lock)
        {
            _state = mappedState;
            _isCalibrating = e.NewState != AccelCalibrationState.Idle &&
                            e.NewState != AccelCalibrationState.Completed &&
                            e.NewState != AccelCalibrationState.Failed &&
                            e.NewState != AccelCalibrationState.Cancelled &&
                            e.NewState != AccelCalibrationState.Rejected;
        }
        
        _currentState.StateMachine = mappedState;
        CalibrationStateChanged?.Invoke(this, _currentState);
    }
    
    private void OnAccelPositionRequested(object? sender, AccelPositionRequestedEventArgs e)
    {
        _logger.LogInformation("FC requested position {Position}: {Name}", e.Position, e.PositionName);
        
        lock (_lock)
        {
            _currentPosition = e.Position;
            _expectedPosition = e.Position;
            _waitingForFcResponse = false;
        }
        
        SetState(CalibrationStateMachine.WaitingForUserPosition,
            $"Position {e.Position}/6: {e.PositionName}",
            GetProgress());
        
        RaiseStepRequired(e.Position, true, GetPositionInstruction(e.Position));
    }
    
    private void OnAccelPositionValidated(object? sender, AccelPositionValidationEventArgs e)
    {
        if (!e.IsValid)
        {
            _logger.LogWarning("Position {Position} validation failed: {Message}", e.Position, e.Message);
            
            SetState(CalibrationStateMachine.PositionRejected,
                $"?? Position {e.Position} ({e.PositionName}): {e.Message}",
                GetProgress());
            
            RaiseStepRequired(e.Position, true, $"?? {e.Message}\nPlease reposition and try again.");
        }
        else
        {
            _logger.LogInformation("Position {Position} validated successfully", e.Position);
        }
    }
    
    private void OnAccelCalibrationCompleted(object? sender, AccelCalibrationCompletedEventArgs e)
    {
        _logger.LogInformation("Accelerometer calibration completed: {Result} - {Message} ({Duration:F1}s)",
            e.Result, e.Message, e.Duration.TotalSeconds);
        
        var success = e.Result == AccelCalibrationResult.Success;
        FinishCalibration(success, e.Message);
    }
    
    #endregion

    #region Connection Monitoring

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        if (!connected)
        {
            lock (_lock)
            {
                if (_isCalibrating)
                {
                    _logger.LogWarning("Connection lost during calibration");
                    AbortCalibration("Connection lost during calibration");
                }
            }
        }
    }

    #endregion

    #region STATUSTEXT Handler - FC drives the calibration flow

    private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
    {
        lock (_lock)
        {
            if (!_isCalibrating) return;
        }
        
        var text = e.Text;
        var lower = text.ToLowerInvariant();
        
        _logger.LogInformation("FC: {Text}", text);
        StatusTextReceived?.Invoke(this, new CalibrationStatusTextEventArgs { Severity = e.Severity, Text = text });
        
        // Skip non-calibration messages
        if (lower.Contains("prearm") || lower.Contains("ekf") || lower.Contains("gps") ||
            lower.Contains("initialising") || lower.Contains("initializing"))
        {
            return;
        }
        
        CalibrationType currentType;
        lock (_lock) { currentType = _currentType; }
        
        // Check for success - FC says calibration is done
        if (IsSuccessMessage(lower))
        {
            _logger.LogInformation("FC reported calibration SUCCESS");
            FinishCalibration(true, text);
            return;
        }
        
        // Check for failure
        if (IsFailureMessage(lower))
        {
            _logger.LogWarning("FC reported calibration FAILURE: {Text}", text);
            FinishCalibration(false, text);
            return;
        }
        
        // Accelerometer specific - FC tells us what position it wants
        if (currentType == CalibrationType.Accelerometer)
        {
            HandleAccelStatusText(lower, text);
        }
    }

    private void HandleAccelStatusText(string lower, string originalText)
    {
        // FC tells us which position to put the vehicle in
        // This is the ONLY way we know to advance - FC drives the flow
        
        int? requestedPosition = null;
        
        // Mission Planner / ArduPilot messages:
        // "Place vehicle level and press any key" -> Position 1
        // "Place vehicle on its left side and press any key" -> Position 2
        // etc.
        
        if (lower.Contains("place") || lower.Contains("level") || lower.Contains("left") || 
            lower.Contains("right") || lower.Contains("nose") || lower.Contains("back") ||
            lower.Contains("upside"))
        {
            requestedPosition = DetectPositionFromMessage(lower);
        }
        
        // FC confirms position was good - "got it" or moves to next
        if (lower.Contains("got it") || lower.Contains("position") && lower.Contains("ok"))
        {
            lock (_lock)
            {
                _waitingForFcResponse = false;
                _logger.LogInformation("FC confirmed position {Position} was correct", _currentPosition);
            }
            // FC will send next position request
            return;
        }
        
        // FC says position was bad
        if (lower.Contains("bad") || lower.Contains("wrong") || lower.Contains("incorrect") ||
            lower.Contains("try again") || lower.Contains("retry") || lower.Contains("rotation"))
        {
            lock (_lock)
            {
                _waitingForFcResponse = false;
            }
            
            _logger.LogWarning("FC rejected position: {Text}", originalText);
            
            int pos;
            lock (_lock) { pos = _currentPosition; }
            
            SetState(CalibrationStateMachine.WaitingForUserPosition,
                $"?? Position {pos} incorrect! Please reposition to {GetPositionName(pos)} and try again.",
                GetProgress());
            
            RaiseStepRequired(pos, true, $"?? {originalText}\nPlease reposition correctly.");
            return;
        }
        
        // If FC requested a specific position, update our state
        if (requestedPosition.HasValue)
        {
            lock (_lock)
            {
                _expectedPosition = requestedPosition.Value;
                _currentPosition = requestedPosition.Value;
                _waitingForFcResponse = false;
            }
            
            _logger.LogInformation("FC requesting position {Position}: {Name}", 
                requestedPosition.Value, GetPositionName(requestedPosition.Value));
            
            SetState(CalibrationStateMachine.WaitingForUserPosition,
                $"Position {requestedPosition.Value}/6: {GetPositionName(requestedPosition.Value)}",
                GetProgress());
            
            RaiseStepRequired(requestedPosition.Value, true, GetPositionInstruction(requestedPosition.Value));
        }
    }

    private int? DetectPositionFromMessage(string lower)
    {
        // Order matters - check more specific patterns first
        if (lower.Contains("left") && !lower.Contains("right"))
            return 2;
        if (lower.Contains("right") && !lower.Contains("left"))
            return 3;
        if (lower.Contains("nose") && lower.Contains("down"))
            return 4;
        if (lower.Contains("nose") && lower.Contains("up"))
            return 5;
        if (lower.Contains("back") || lower.Contains("upside"))
            return 6;
        if (lower.Contains("level"))
            return 1;
        
        return null;
    }

    private static bool IsSuccessMessage(string lower)
    {
        return lower.Contains("calibration successful") ||
               lower.Contains("calibration complete") ||
               lower.Contains("calibration done") ||
               lower.Contains("accel cal complete") ||
               (lower.Contains("offsets") && lower.Contains("saved"));
    }

    private static bool IsFailureMessage(string lower)
    {
        if (lower.Contains("prearm")) return false;
        
        return (lower.Contains("calibration") && lower.Contains("failed")) ||
               (lower.Contains("calibration") && lower.Contains("cancelled")) ||
               (lower.Contains("calibration") && lower.Contains("timeout")) ||
               lower.Contains("cal failed");
    }

    #endregion

    #region COMMAND_ACK Handler

    private void OnCommandAckReceived(object? sender, CommandAckEventArgs e)
    {
        lock (_lock)
        {
            if (!_isCalibrating) return;
        }
        
        var result = (MavResult)e.Result;
        _logger.LogDebug("COMMAND_ACK: cmd={Command} result={Result}", e.Command, result);
        
        if (e.Command == 241) // MAV_CMD_PREFLIGHT_CALIBRATION
        {
            HandleCalibrationStartAck(result);
        }
        else if (e.Command == 42429) // MAV_CMD_ACCELCAL_VEHICLE_POS
        {
            HandlePositionAck(result);
        }
    }

    private void HandleCalibrationStartAck(MavResult result)
    {
        if (result == MavResult.Accepted || result == MavResult.InProgress)
        {
            _logger.LogInformation("FC accepted calibration command");
            
            CalibrationType type;
            lock (_lock) { type = _currentType; }
            
            if (type == CalibrationType.Accelerometer)
            {
                // Wait for FC to send first position request via STATUSTEXT
                SetState(CalibrationStateMachine.WaitingForInstruction,
                    "Waiting for flight controller...", 0);
                
                // Start timeout watcher
                _ = WatchForFcInstructionAsync();
            }
            else
            {
                SetState(CalibrationStateMachine.Sampling,
                    $"{GetTypeName(type)} calibration in progress...", 50);
                _ = WaitForSimpleCalibrationAsync();
            }
        }
        else
        {
            string msg = result switch
            {
                MavResult.Denied => "Calibration denied - vehicle may be armed",
                MavResult.TemporarilyRejected => "Temporarily rejected - try again",
                MavResult.Unsupported => "Not supported by firmware",
                _ => $"Rejected (code: {(int)result})"
            };
            FinishCalibration(false, msg);
        }
    }

    private void HandlePositionAck(MavResult result)
    {
        int pos;
        lock (_lock) { pos = _currentPosition; }
        
        if (result == MavResult.Accepted || result == MavResult.InProgress)
        {
            _logger.LogInformation("FC acknowledged position {Position} command", pos);
            // Now wait for FC to validate via STATUSTEXT
            SetState(CalibrationStateMachine.Sampling,
                $"Position {pos}/6: {GetPositionName(pos)} - FC validating... Hold still!",
                GetProgress());
        }
        else if (result == MavResult.Denied || result == MavResult.Failed)
        {
            _logger.LogWarning("FC rejected position command: {Result}", result);
            
            lock (_lock) { _waitingForFcResponse = false; }
            
            SetState(CalibrationStateMachine.WaitingForUserPosition,
                $"?? Position {pos} rejected. Please verify {GetPositionName(pos)} orientation.",
                GetProgress());
            
            RaiseStepRequired(pos, true, $"Position rejected. Please verify {GetPositionName(pos)} orientation and try again.");
        }
    }

    #endregion

    #region Calibration Operations

    public Task<bool> StartCalibrationAsync(CalibrationType type)
    {
        return type switch
        {
            CalibrationType.Accelerometer => StartAccelerometerCalibrationAsync(true),
            CalibrationType.Compass => StartCompassCalibrationAsync(false),
            CalibrationType.Gyroscope => StartGyroscopeCalibrationAsync(),
            CalibrationType.LevelHorizon => StartLevelHorizonCalibrationAsync(),
            CalibrationType.Barometer => StartBarometerCalibrationAsync(),
            CalibrationType.Airspeed => StartAirspeedCalibrationAsync(),
            _ => Task.FromResult(false)
        };
    }

    public Task<bool> StartAccelerometerCalibrationAsync(bool fullSixAxis = true)
    {
        if (!CanStart()) return Task.FromResult(false);
        
        InitializeCalibration(CalibrationType.Accelerometer);
        
        _logger.LogInformation("Starting 6-axis accelerometer calibration");
        SetState(CalibrationStateMachine.WaitingForAck, "Starting accelerometer calibration...", 0);
        
        // MAV_CMD_PREFLIGHT_CALIBRATION: param5 = 4 for 6-axis accel
        _connectionService.SendPreflightCalibration(gyro: 0, mag: 0, groundPressure: 0, airspeed: 0, accel: fullSixAxis ? 4 : 1);
        
        return Task.FromResult(true);
    }

    public Task<bool> StartCompassCalibrationAsync(bool onboardCalibration = false)
    {
        if (!CanStart()) return Task.FromResult(false);
        
        InitializeCalibration(CalibrationType.Compass);
        SetState(CalibrationStateMachine.WaitingForAck, "Starting compass calibration...", 0);
        
        RaiseStepRequired(0, false, "Rotate vehicle slowly in all directions until calibration completes.");
        
        _connectionService.SendPreflightCalibration(gyro: 0, mag: onboardCalibration ? 76 : 1, groundPressure: 0, airspeed: 0, accel: 0);
        
        return Task.FromResult(true);
    }

    public Task<bool> StartGyroscopeCalibrationAsync()
    {
        if (!CanStart()) return Task.FromResult(false);
        
        InitializeCalibration(CalibrationType.Gyroscope);
        SetState(CalibrationStateMachine.WaitingForAck, "Starting gyroscope calibration...", 0);
        
        RaiseStepRequired(0, false, "Keep the vehicle completely still. Do not move it.");
        
        _connectionService.SendPreflightCalibration(gyro: 1, mag: 0, groundPressure: 0, airspeed: 0, accel: 0);
        
        return Task.FromResult(true);
    }

    public Task<bool> StartLevelHorizonCalibrationAsync()
    {
        if (!CanStart()) return Task.FromResult(false);
        
        InitializeCalibration(CalibrationType.LevelHorizon);
        SetState(CalibrationStateMachine.WaitingForAck, "Starting level horizon calibration...", 0);
        
        RaiseStepRequired(0, false, "Place vehicle on a perfectly level surface. Keep it still.");
        
        _connectionService.SendPreflightCalibration(gyro: 0, mag: 0, groundPressure: 0, airspeed: 0, accel: 2);
        
        return Task.FromResult(true);
    }

    public Task<bool> StartBarometerCalibrationAsync()
    {
        if (!CanStart()) return Task.FromResult(false);
        
        InitializeCalibration(CalibrationType.Barometer);
        SetState(CalibrationStateMachine.WaitingForAck, "Starting barometer calibration...", 0);
        
        _connectionService.SendPreflightCalibration(gyro: 0, mag: 0, groundPressure: 1, airspeed: 0, accel: 0);
        
        return Task.FromResult(true);
    }

    public Task<bool> StartAirspeedCalibrationAsync()
    {
        if (!CanStart()) return Task.FromResult(false);
        
        InitializeCalibration(CalibrationType.Airspeed);
        SetState(CalibrationStateMachine.WaitingForAck, "Starting airspeed calibration...", 0);
        
        _connectionService.SendPreflightCalibration(gyro: 0, mag: 0, groundPressure: 0, airspeed: 1, accel: 0);
        
        return Task.FromResult(true);
    }

    /// <summary>
    /// User confirms vehicle is in position.
    /// Sends MAV_CMD_ACCELCAL_VEHICLE_POS to FC.
    /// FC will validate using IMU and respond via STATUSTEXT.
    /// We do NOT auto-advance - we wait for FC confirmation.
    /// </summary>
    public Task<bool> AcceptCalibrationStepAsync()
    {
        int position;
        CalibrationStateMachine currentState;
        
        lock (_lock)
        {
            if (!_isCalibrating || _currentType != CalibrationType.Accelerometer)
            {
                _logger.LogWarning("AcceptCalibrationStep: not in accel calibration");
                return Task.FromResult(false);
            }
            
            currentState = _state;
            position = _currentPosition;
            
            if (_waitingForFcResponse)
            {
                _logger.LogWarning("Already waiting for FC response");
                return Task.FromResult(false);
            }
        }
        
        if (currentState != CalibrationStateMachine.WaitingForUserPosition)
        {
            _logger.LogWarning("Not in WaitingForUserPosition state: {State}", currentState);
            return Task.FromResult(false);
        }
        
        if (!_connectionService.IsConnected)
        {
            AbortCalibration("Connection lost");
            return Task.FromResult(false);
        }
        
        _logger.LogInformation("User confirmed position {Position} - sending to FC for validation", position);
        
        lock (_lock)
        {
            _waitingForFcResponse = true;
            _positionSentTime = DateTime.UtcNow;
        }
        
        SetState(CalibrationStateMachine.Sampling,
            $"Position {position}/6: {GetPositionName(position)} - FC validating...",
            GetProgress());
        
        // Send position to FC - FC will validate using its IMU
        _connectionService.SendAccelCalVehiclePos(position);
        
        // Start timeout watcher for FC response
        _ = WatchForFcPositionResponseAsync(position);
        
        return Task.FromResult(true);
    }

    public Task<bool> CancelCalibrationAsync()
    {
        lock (_lock)
        {
            if (!_isCalibrating)
                return Task.FromResult(true);
        }
        
        _logger.LogInformation("Calibration cancelled by user");
        AbortCalibration("Calibration cancelled by user");
        
        return Task.FromResult(true);
    }

    public Task<bool> RebootFlightControllerAsync()
    {
        if (!_connectionService.IsConnected)
            return Task.FromResult(false);
        
        _logger.LogInformation("Sending reboot command");
        _connectionService.SendPreflightReboot(autopilot: 1, companion: 0);
        
        return Task.FromResult(true);
    }

    #endregion

    #region Timeout Watchers

    private async Task WatchForFcInstructionAsync()
    {
        CancellationToken ct;
        lock (_lock) { ct = _calibrationCts?.Token ?? CancellationToken.None; }
        
        try
        {
            await Task.Delay(COMMAND_ACK_TIMEOUT_MS, ct);
        }
        catch (OperationCanceledException) { return; }
        
        // Check if we're still waiting for first instruction
        CalibrationStateMachine state;
        lock (_lock) { state = _state; }
        
        if (state == CalibrationStateMachine.WaitingForInstruction || state == CalibrationStateMachine.WaitingForAck)
        {
            // FC didn't send position request - start with position 1 as fallback
            _logger.LogWarning("No instruction from FC - starting with position 1 (fallback)");
            
            lock (_lock)
            {
                _currentPosition = 1;
                _expectedPosition = 1;
            }
            
            SetState(CalibrationStateMachine.WaitingForUserPosition,
                "Position 1/6: LEVEL - Place vehicle level",
                0);
            
            RaiseStepRequired(1, true, GetPositionInstruction(1));
        }
    }

    private async Task WatchForFcPositionResponseAsync(int position)
    {
        CancellationToken ct;
        lock (_lock) { ct = _calibrationCts?.Token ?? CancellationToken.None; }
        
        try
        {
            await Task.Delay(FC_RESPONSE_TIMEOUT_MS, ct);
        }
        catch (OperationCanceledException) { return; }
        
        // Check if we're still waiting for FC response for this position
        bool stillWaiting;
        int currentPos;
        lock (_lock)
        {
            stillWaiting = _waitingForFcResponse && _currentPosition == position;
            currentPos = _currentPosition;
        }
        
        if (stillWaiting)
        {
            _logger.LogWarning("FC did not respond to position {Position} within timeout", position);
            
            lock (_lock) { _waitingForFcResponse = false; }
            
            // Show error - user needs to try again
            SetState(CalibrationStateMachine.WaitingForUserPosition,
                $"?? No response from FC. Please verify {GetPositionName(currentPos)} and try again.",
                GetProgress());
            
            RaiseStepRequired(currentPos, true, 
                $"No response from flight controller. Please verify vehicle is in {GetPositionName(currentPos)} position and click again.");
        }
    }

    private async Task WaitForSimpleCalibrationAsync()
    {
        var startTime = DateTime.UtcNow;
        const int timeoutMs = 15000;
        CancellationToken ct;
        CalibrationType type;
        
        lock (_lock)
        {
            ct = _calibrationCts?.Token ?? CancellationToken.None;
            type = _currentType;
        }
        
        while (true)
        {
            lock (_lock)
            {
                if (!_isCalibrating || _state == CalibrationStateMachine.Completed || _state == CalibrationStateMachine.Failed)
                    return;
            }
            
            if (!_connectionService.IsConnected)
            {
                AbortCalibration("Connection lost");
                return;
            }
            
            var elapsed = DateTime.UtcNow - startTime;
            if (elapsed.TotalMilliseconds >= timeoutMs)
            {
                // Assume success for simple calibrations (FC may not send explicit message)
                FinishCalibration(true, $"{GetTypeName(type)} calibration completed. Reboot recommended.");
                return;
            }
            
            var progress = Math.Min(95, (int)(elapsed.TotalMilliseconds / timeoutMs * 100));
            
            lock (_lock)
            {
                if (_isCalibrating && _state == CalibrationStateMachine.Sampling)
                {
                    _currentState.Progress = progress;
                }
            }
            
            CalibrationProgressChanged?.Invoke(this, new CalibrationProgressEventArgs
            {
                Type = type,
                ProgressPercent = progress,
                StatusText = $"{GetTypeName(type)} calibration in progress...",
                StateMachine = CalibrationStateMachine.Sampling
            });
            
            try { await Task.Delay(500, ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    #endregion

    #region State Management

    private bool CanStart()
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("Not connected");
            return false;
        }
        
        lock (_lock)
        {
            if (_isCalibrating)
            {
                _logger.LogWarning("Calibration already in progress");
                return false;
            }
        }
        
        return true;
    }

    private void InitializeCalibration(CalibrationType type)
    {
        lock (_lock)
        {
            _calibrationCts?.Cancel();
            _calibrationCts?.Dispose();
            _calibrationCts = new CancellationTokenSource();
            
            _isCalibrating = true;
            _currentType = type;
            _currentPosition = 1;
            _expectedPosition = 1;
            _waitingForFcResponse = false;
            _state = CalibrationStateMachine.Idle;
            _calibrationStartTime = DateTime.UtcNow;
        }
        
        _currentState = new CalibrationStateModel
        {
            Type = type,
            State = CalibrationState.InProgress,
            StateMachine = CalibrationStateMachine.Idle
        };
    }

    private void SetState(CalibrationStateMachine newState, string message, int progress)
    {
        lock (_lock)
        {
            if (!_isCalibrating) return;
            _state = newState;
        }
        
        int pos;
        CalibrationType type;
        lock (_lock)
        {
            pos = _currentPosition;
            type = _currentType;
        }
        
        _currentState.StateMachine = newState;
        _currentState.State = CalibrationState.InProgress;
        _currentState.Message = message;
        _currentState.Progress = progress;
        _currentState.CurrentPosition = pos;
        _currentState.CanConfirmPosition = newState == CalibrationStateMachine.WaitingForUserPosition;
        
        CalibrationStateChanged?.Invoke(this, _currentState);
        
        CalibrationProgressChanged?.Invoke(this, new CalibrationProgressEventArgs
        {
            Type = type,
            ProgressPercent = progress,
            StatusText = message,
            CurrentStep = pos,
            TotalSteps = type == CalibrationType.Accelerometer ? 6 : 1,
            StateMachine = newState
        });
    }

    private void RaiseStepRequired(int position, bool canConfirm, string instructions)
    {
        CalibrationType type;
        lock (_lock) { type = _currentType; }
        
        var step = position switch
        {
            1 => CalibrationStep.Level,
            2 => CalibrationStep.LeftSide,
            3 => CalibrationStep.RightSide,
            4 => CalibrationStep.NoseDown,
            5 => CalibrationStep.NoseUp,
            6 => CalibrationStep.Back,
            _ => CalibrationStep.Level
        };
        
        if (type == CalibrationType.Compass) step = CalibrationStep.Rotate;
        if (type == CalibrationType.Gyroscope || type == CalibrationType.LevelHorizon) step = CalibrationStep.KeepStill;
        
        CalibrationStepRequired?.Invoke(this, new CalibrationStepEventArgs
        {
            Type = type,
            Step = step,
            Instructions = instructions,
            CanConfirm = canConfirm
        });
    }

    private void FinishCalibration(bool success, string message)
    {
        lock (_lock)
        {
            if (!_isCalibrating) return;
            
            _state = success ? CalibrationStateMachine.Completed : CalibrationStateMachine.Failed;
            _isCalibrating = false;
            _waitingForFcResponse = false;
            _calibrationCts?.Cancel();
        }
        
        var duration = DateTime.UtcNow - _calibrationStartTime;
        _logger.LogInformation("Calibration {Result}: {Message} (Duration: {Duration:F1}s)",
            success ? "SUCCESS" : "FAILED", message, duration.TotalSeconds);
        
        CalibrationType type;
        int pos;
        lock (_lock)
        {
            type = _currentType;
            pos = _currentPosition;
        }
        
        _currentState.State = success ? CalibrationState.Completed : CalibrationState.Failed;
        _currentState.StateMachine = success ? CalibrationStateMachine.Completed : CalibrationStateMachine.Failed;
        _currentState.Progress = success ? 100 : 0;
        _currentState.Message = message;
        _currentState.CanConfirmPosition = false;
        
        CalibrationStateChanged?.Invoke(this, _currentState);
        
        CalibrationProgressChanged?.Invoke(this, new CalibrationProgressEventArgs
        {
            Type = type,
            ProgressPercent = success ? 100 : 0,
            StatusText = message,
            CurrentStep = pos,
            TotalSteps = type == CalibrationType.Accelerometer ? 6 : 1,
            StateMachine = success ? CalibrationStateMachine.Completed : CalibrationStateMachine.Failed
        });
    }

    private void AbortCalibration(string reason)
    {
        _calibrationCts?.Cancel();
        
        lock (_lock)
        {
            if (!_isCalibrating) return;
            _state = CalibrationStateMachine.Failed;
            _isCalibrating = false;
            _waitingForFcResponse = false;
        }
        
        _logger.LogWarning("Calibration aborted: {Reason}", reason);
        
        CalibrationType type;
        int pos;
        lock (_lock)
        {
            type = _currentType;
            pos = _currentPosition;
        }
        
        _currentState.State = CalibrationState.Failed;
        _currentState.StateMachine = CalibrationStateMachine.Failed;
        _currentState.Progress = 0;
        _currentState.Message = reason;
        _currentState.CanConfirmPosition = false;
        
        CalibrationStateChanged?.Invoke(this, _currentState);
        
        CalibrationProgressChanged?.Invoke(this, new CalibrationProgressEventArgs
        {
            Type = type,
            ProgressPercent = 0,
            StatusText = reason,
            CurrentStep = pos,
            TotalSteps = type == CalibrationType.Accelerometer ? 6 : 1,
            StateMachine = CalibrationStateMachine.Failed
        });
    }

    #endregion

    #region Helpers

    private int GetProgress()
    {
        int pos;
        CalibrationType type;
        lock (_lock)
        {
            pos = _currentPosition;
            type = _currentType;
        }
        
        if (type != CalibrationType.Accelerometer) return 50;
        return (int)((pos - 1) * 100.0 / 6.0);
    }

    private static string GetPositionName(int position)
    {
        return position >= 1 && position <= 6 ? PositionNames[position - 1] : "UNKNOWN";
    }

    private static string GetPositionInstruction(int position)
    {
        return position >= 1 && position <= 6 ? PositionInstructions[position - 1] : "Follow FC instructions";
    }

    private static string GetTypeName(CalibrationType type)
    {
        return type switch
        {
            CalibrationType.Accelerometer => "Accelerometer",
            CalibrationType.Compass => "Compass",
            CalibrationType.Gyroscope => "Gyroscope",
            CalibrationType.LevelHorizon => "Level Horizon",
            CalibrationType.Barometer => "Barometer",
            CalibrationType.Airspeed => "Airspeed",
            _ => type.ToString()
        };
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _calibrationCts?.Cancel();
        _calibrationCts?.Dispose();
        
        _connectionService.StatusTextReceived -= OnStatusTextReceived;
        _connectionService.CommandAckReceived -= OnCommandAckReceived;
        _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
        
        // Unsubscribe from AccelerometerCalibrationService events
        if (_accelCalService != null)
        {
            _accelCalService.StateChanged -= OnAccelStateChanged;
            _accelCalService.PositionRequested -= OnAccelPositionRequested;
            _accelCalService.PositionValidated -= OnAccelPositionValidated;
            _accelCalService.CalibrationCompleted -= OnAccelCalibrationCompleted;
        }
    }

    #endregion
}
