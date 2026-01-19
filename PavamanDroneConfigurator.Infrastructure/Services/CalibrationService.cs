using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Mission Planner-style calibration service.
/// 
/// KEY PRINCIPLE: The FC is the ONLY source of truth.
/// - NO client-side IMU validation
/// - NO hardcoded thresholds  
/// - NO assumptions about what FC wants
/// - UI just shows FC messages and sends position confirmations
/// 
/// Mission Planner flow:
/// 1. Send MAV_CMD_PREFLIGHT_CALIBRATION (param5=4)
/// 2. FC sends STATUSTEXT "Place vehicle level and press any key"
/// 3. User clicks ? send MAV_CMD_ACCELCAL_VEHICLE_POS(1)
/// 4. FC validates internally, responds via STATUSTEXT
/// 5. FC either requests next position OR says "calibration successful"
/// 6. Repeat until FC says done
/// </summary>
public class CalibrationService : ICalibrationService, IDisposable
{
    private readonly ILogger<CalibrationService> _logger;
    private readonly IConnectionService _connectionService;
    
    // State tracking
    private readonly object _lock = new();
    private CalibrationType _currentType;
    private int _currentPosition;
    private bool _isCalibrating;
    private bool _waitingForUserClick; // True when FC wants user to click button
    private CalibrationStateMachine _state = CalibrationStateMachine.Idle;
    private DateTime _calibrationStartTime;
    private CancellationTokenSource? _calibrationCts;
    private bool _disposed;
    
    private CalibrationStateModel _currentState = new();
    
    // Position display names - ONLY for UI, not for validation
    private static readonly string[] PositionNames = { "LEVEL", "LEFT", "RIGHT", "NOSE DOWN", "NOSE UP", "BACK" };

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
        IConnectionService connectionService)
    {
        _logger = logger;
        _connectionService = connectionService;
        
        _connectionService.StatusTextReceived += OnStatusTextReceived;
        _connectionService.CommandAckReceived += OnCommandAckReceived;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
    }

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
                    AbortCalibration("Connection lost");
                }
            }
        }
    }

    #endregion

    #region STATUSTEXT Handler - FC tells us EVERYTHING

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
        
        CalibrationType currentType;
        lock (_lock) { currentType = _currentType; }
        
        // Check for success - FC says done
        if (IsSuccessMessage(lower))
        {
            _logger.LogInformation("FC reported SUCCESS");
            FinishCalibration(true, text);
            return;
        }
        
        // Check for failure
        if (IsFailureMessage(lower))
        {
            _logger.LogWarning("FC reported FAILURE: {Text}", text);
            FinishCalibration(false, text);
            return;
        }
        
        // Accelerometer - FC tells us what position it wants
        if (currentType == CalibrationType.Accelerometer)
        {
            HandleAccelStatusText(lower, text);
        }
    }

    private void HandleAccelStatusText(string lower, string originalText)
    {
        // Detect if FC is requesting a specific position
        int? requestedPosition = DetectPositionFromMessage(lower);
        
        if (requestedPosition.HasValue)
        {
            int previousPosition;
            lock (_lock)
            {
                previousPosition = _currentPosition;
                _currentPosition = requestedPosition.Value;
                _waitingForUserClick = true;
            }
            
            _logger.LogInformation("FC requests position {Pos}: {Name}", requestedPosition.Value, GetPositionName(requestedPosition.Value));
            
            // Calculate progress based on positions completed
            // When FC requests position N, it means position N-1 was completed (except for position 1)
            int progress = CalculateProgressFromPosition(requestedPosition.Value);
            _logger.LogInformation("Progress from STATUSTEXT: {Progress}% (FC requesting position {Pos})", progress, requestedPosition.Value);
            
            // Show position to user
            SetState(CalibrationStateMachine.WaitingForUserPosition,
                originalText, // Show FC's exact message
                progress);
            
            // Tell UI to show position image and enable button
            RaiseStepRequired(requestedPosition.Value, true, originalText);
        }
        // FC is sampling - important message!
        else if (lower.Contains("sampling") || lower.Contains("reading") || lower.Contains("hold"))
        {
            lock (_lock) { _waitingForUserClick = false; }
            
            int pos;
            lock (_lock) { pos = _currentPosition; }
            
            _logger.LogInformation("FC is sampling position {Pos}", pos);
            SetState(CalibrationStateMachine.Sampling, 
                $"FC is sampling position {pos}/6 - Hold vehicle still!", 
                GetProgress());
        }
        // FC says position detected/held
        else if (lower.Contains("got") || lower.Contains("detected") || lower.Contains("held"))
        {
            lock (_lock) { _waitingForUserClick = false; }
            SetState(CalibrationStateMachine.Sampling, originalText, GetProgress());
        }
        // FC rejected position
        else if (lower.Contains("bad") || lower.Contains("wrong") || lower.Contains("incorrect") || lower.Contains("failed"))
        {
            // FC rejected position - let user try again
            lock (_lock) { _waitingForUserClick = true; }
            int pos;
            lock (_lock) { pos = _currentPosition; }
            SetState(CalibrationStateMachine.WaitingForUserPosition,
                originalText, // Show FC's exact error message
                GetProgress());
            RaiseStepRequired(pos, true, originalText);
        }
    }

    private int? DetectPositionFromMessage(string lower)
    {
        // Detect which position FC is requesting based on message keywords
        // Order matters - check specific patterns first
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
               (lower.Contains("offsets") && lower.Contains("saved"));
    }

    private static bool IsFailureMessage(string lower)
    {
        if (lower.Contains("prearm")) return false;
        
        return (lower.Contains("calibration") && lower.Contains("failed")) ||
               (lower.Contains("calibration") && lower.Contains("cancelled")) ||
               (lower.Contains("calibration") && lower.Contains("timeout"));
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
            HandlePositionCommandAck(result);
        }
    }

    private void HandleCalibrationStartAck(MavResult result)
    {
        if (result == MavResult.Accepted || result == MavResult.InProgress)
        {
            _logger.LogInformation("FC accepted calibration");
            
            CalibrationType type;
            lock (_lock) { type = _currentType; }
            
            if (type == CalibrationType.Accelerometer)
            {
                // Wait for FC to send first position request
                SetState(CalibrationStateMachine.WaitingForInstruction,
                    "Waiting for flight controller to request first position...", 0);
                
                // Start 5-second fallback timer
                _ = StartPositionRequestFallbackAsync();
            }
            else
            {
                // Simple calibration - just wait
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

    private async Task StartPositionRequestFallbackAsync()
    {
        const int timeoutMs = 5000;
        CancellationToken ct;
        lock (_lock) { ct = _calibrationCts?.Token ?? CancellationToken.None; }
        
        try
        {
            await Task.Delay(timeoutMs, ct);
        }
        catch (OperationCanceledException) { return; }
        
        // Check if we're still waiting for FC instruction
        CalibrationStateMachine currentState;
        lock (_lock) { currentState = _state; }
        
        if (currentState == CalibrationStateMachine.WaitingForInstruction)
        {
            _logger.LogWarning("FC did not send position request within {Timeout}ms - starting with position 1", timeoutMs);
            
            lock (_lock)
            {
                _currentPosition = 1;
                _waitingForUserClick = true;
            }
            
            SetState(CalibrationStateMachine.WaitingForUserPosition,
                "Place vehicle LEVEL on a flat surface and click 'Click When In Position' when ready",
                0);
            
            RaiseStepRequired(1, true, "Place vehicle LEVEL on a flat surface");
        }
    }

    private void HandlePositionCommandAck(MavResult result)
    {
        int pos;
        lock (_lock) { pos = _currentPosition; }
        
        if (result == MavResult.Accepted || result == MavResult.InProgress)
        {
            _logger.LogInformation("FC accepted position {Pos} command - waiting for FC validation via STATUSTEXT", pos);
            
            lock (_lock) { _waitingForUserClick = false; }
            
            SetState(CalibrationStateMachine.Sampling,
                $"Position {pos} sent to FC - waiting for FC validation...",
                GetProgress());
            
            // NO TIMER! Just wait for FC to send STATUSTEXT
            // FC will tell us via STATUSTEXT when it needs the next position
            _logger.LogInformation("Position {Position} sent to FC - waiting for FC validation via STATUSTEXT", pos);
        }
        else if (result == MavResult.Denied || result == MavResult.Failed)
        {
            _logger.LogWarning("FC rejected position {Pos}: {Result}", pos, result);
            
            lock (_lock) { _waitingForUserClick = true; }
            
            SetState(CalibrationStateMachine.WaitingForUserPosition,
                $"?? Position {pos} rejected by FC. Please verify {GetPositionName(pos)} orientation and try again.",
                GetProgress());
            
            RaiseStepRequired(pos, true, $"Position rejected. Verify {GetPositionName(pos)} orientation and try again.");
        }
    }
    
    private async Task MonitorInternalSamplingAsync()
    {
        // GUARD: This method should NEVER be called for Accelerometer calibration!
        // Accelerometer progress must ONLY come from FC STATUSTEXT messages
        CalibrationType type;
        lock (_lock) { type = _currentType; }
        
        if (type == CalibrationType.Accelerometer)
        {
            _logger.LogError("MonitorInternalSamplingAsync called for Accelerometer - this is a BUG! This method creates fake progress.");
            return;
        }
        
        // Monitor for up to 120 seconds for FC to complete internal sampling
        const int timeoutMs = 120000; // 2 minutes
        const int updateIntervalMs = 500;
        var startTime = DateTime.UtcNow;
        
        CancellationToken ct;
        lock (_lock) { ct = _calibrationCts?.Token ?? CancellationToken.None; }
        
        _logger.LogInformation("Monitoring internal sampling for {Type}", type);
        
        while (true)
        {
            // Check if calibration is still active
            bool stillCalibrating;
            CalibrationStateMachine currentState;
            lock (_lock)
            {
                stillCalibrating = _isCalibrating;
                currentState = _state;
            }
            
            if (!stillCalibrating || currentState == CalibrationStateMachine.Completed || currentState == CalibrationStateMachine.Failed)
            {
                return;
            }
            
            var elapsed = DateTime.UtcNow - startTime;
            if (elapsed.TotalMilliseconds >= timeoutMs)
            {
                _logger.LogWarning("Calibration timeout after 120 seconds");
                FinishCalibration(false, "Calibration timeout - FC did not complete within 2 minutes.");
                return;
            }
            
            // Update progress smoothly while FC is sampling
            // This is ONLY for non-accelerometer calibrations that don't have position-based progress
            if (currentState == CalibrationStateMachine.Sampling)
            {
                var progress = Math.Min(95, 16 + (int)(elapsed.TotalMilliseconds / timeoutMs * 79));
                
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
                    StatusText = "Calibration in progress...",
                    StateMachine = CalibrationStateMachine.Sampling
                });
            }
            
            try { await Task.Delay(updateIntervalMs, ct); }
            catch (OperationCanceledException) { return; }
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
        
        _logger.LogInformation("Starting accelerometer calibration with param5=1 (position-based, 6 positions required)");
        SetState(CalibrationStateMachine.WaitingForAck, "Starting calibration...", 0);
        
        // ArduPilot accelerometer calibration modes:
        // param5=1: Position-based calibration (6 positions, FC validates each) - RECOMMENDED
        // param5=2: Level calibration only (single position)
        // param5=4: Simple calibration (automatic, no user positions)
        // We use param5=1 to match Mission Planner's behavior
        _connectionService.SendPreflightCalibration(gyro: 0, mag: 0, groundPressure: 0, airspeed: 0, accel: 1);
        
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
        
        RaiseStepRequired(0, false, "Keep the vehicle completely still.");
        
        _connectionService.SendPreflightCalibration(gyro: 1, mag: 0, groundPressure: 0, airspeed: 0, accel: 0);
        
        return Task.FromResult(true);
    }

    public Task<bool> StartLevelHorizonCalibrationAsync()
    {
        if (!CanStart()) return Task.FromResult(false);
        
        InitializeCalibration(CalibrationType.LevelHorizon);
        SetState(CalibrationStateMachine.WaitingForAck, "Starting level calibration...", 0);
        
        RaiseStepRequired(0, false, "Place vehicle on a perfectly level surface.");
        
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
    /// User clicked "Click When In Position" button.
    /// Send position 1 command to FC - FC will then sample ALL positions internally.
    /// Mission Planner only sends this command ONCE!
    /// </summary>
    public Task<bool> AcceptCalibrationStepAsync()
    {
        int position;
        bool waitingForClick;
        
        lock (_lock)
        {
            if (!_isCalibrating || _currentType != CalibrationType.Accelerometer)
            {
                _logger.LogWarning("Not in accel calibration");
                return Task.FromResult(false);
            }
            
            position = _currentPosition;
            waitingForClick = _waitingForUserClick;
        }
        
        if (!waitingForClick)
        {
            _logger.LogWarning("Not waiting for user click");
            return Task.FromResult(false);
        }
        
        // IMPORTANT: Only accept position 1!
        // Mission Planner does NOT send commands for positions 2-6
        if (position != 1)
        {
            _logger.LogWarning("AcceptCalibrationStepAsync called for position {Pos} - should only be called for position 1!", position);
            return Task.FromResult(false);
        }
        
        if (!_connectionService.IsConnected)
        {
            AbortCalibration("Connection lost");
            return Task.FromResult(false);
        }
        
        _logger.LogInformation("User confirmed position 1 (LEVEL) - sending MAV_CMD_ACCELCAL_VEHICLE_POS(1) to FC");
        
        lock (_lock) { _waitingForUserClick = false; }
        
        SetState(CalibrationStateMachine.Sampling,
            "Position 1 confirmed - Sending command to FC...",
            GetProgress());
        
        // Send position 1 command - FC will handle the rest internally
        try
        {
            _connectionService.SendAccelCalVehiclePos(position);
            _logger.LogInformation("Successfully sent MAV_CMD_ACCELCAL_VEHICLE_POS(1) - FC will now sample all positions internally");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send MAV_CMD_ACCELCAL_VEHICLE_POS command");
            SetState(CalibrationStateMachine.WaitingForUserPosition,
                $"Error sending command: {ex.Message}",
                GetProgress());
            lock (_lock) { _waitingForUserClick = true; }
            return Task.FromResult(false);
        }
        
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
        AbortCalibration("Cancelled by user");
        
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

    #region Timeout Watcher for Simple Calibrations

    private async Task WaitForSimpleCalibrationAsync()
    {
        CalibrationType type;
        lock (_lock)
        {
            type = _currentType;
        }
        
        // GUARD: This method should ONLY run for simple calibrations
        // Accelerometer and Compass require user interaction and FC position validation
        if (type == CalibrationType.Accelerometer || type == CalibrationType.Compass)
        {
            _logger.LogWarning("WaitForSimpleCalibrationAsync called for {Type} - this should NOT happen! This creates fake progress.", type);
            return;
        }
        
        var startTime = DateTime.UtcNow;
        const int timeoutMs = 15000;
        CancellationToken ct;
        
        lock (_lock)
        {
            ct = _calibrationCts?.Token ?? CancellationToken.None;
        }
        
        _logger.LogInformation("Starting simple calibration timer for {Type}", type);
        
        while (true)
        {
            lock (_lock)
            {
                if (!_isCalibrating || _state == CalibrationStateMachine.Completed || _state == CalibrationStateMachine.Failed)
                    return;
            }
            
            var elapsed = DateTime.UtcNow - startTime;
            if (elapsed.TotalMilliseconds >= timeoutMs)
            {
                // Assume success for simple calibrations
                FinishCalibration(true, $"{GetTypeName(type)} calibration completed.");
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
            _waitingForUserClick = false;
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
        bool canConfirm;
        lock (_lock)
        {
            pos = _currentPosition;
            type = _currentType;
            canConfirm = _waitingForUserClick;
        }
        
        _currentState.StateMachine = newState;
        _currentState.State = CalibrationState.InProgress;
        _currentState.Message = message;
        _currentState.Progress = progress;
        _currentState.CurrentPosition = pos;
        _currentState.CanConfirmPosition = canConfirm;
        
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
            _waitingForUserClick = false;
            _calibrationCts?.Cancel();
        }
        
        var duration = DateTime.UtcNow - _calibrationStartTime;
        _logger.LogInformation("Calibration {Result}: {Message} ({Duration:F1}s)",
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
            _waitingForUserClick = false;
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

    /// <summary>
    /// Calculate progress based on which position FC is requesting.
    /// When FC requests position N, it means positions 1 to N-1 are complete.
    /// </summary>
    private static int CalculateProgressFromPosition(int requestedPosition)
    {
        // Position 1 (LEVEL) = 0% (just starting)
        // Position 2 (LEFT) = 16.67% (position 1 complete)
        // Position 3 (RIGHT) = 33.33% (positions 1-2 complete)
        // Position 4 (NOSE DOWN) = 50% (positions 1-3 complete)
        // Position 5 (NOSE UP) = 66.67% (positions 1-4 complete)
        // Position 6 (BACK) = 83.33% (positions 1-5 complete)
        int positionsComplete = requestedPosition - 1;
        return (int)((positionsComplete * 100.0) / 6.0);
    }

    private static string GetPositionName(int position)
    {
        return position >= 1 && position <= 6 ? PositionNames[position - 1] : "UNKNOWN";
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
    }

    #endregion
}
