using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Accelerometer calibration service - EXACT MissionPlanner implementation.
/// 
/// KEY DIFFERENCES FROM PREVIOUS IMPLEMENTATION:
/// 1. Position confirmations use fire-and-forget (no ACK wait) - Mission Planner's sendPacket() style
/// 2. Position value comes from FC's COMMAND_LONG (authoritative) - we echo back EXACTLY what FC requested
/// 3. Don't reset position after confirming - FC will send next position via STATUSTEXT/COMMAND_LONG
/// </summary>
public class AccelerometerCalibrationService : IDisposable
{
    private readonly ILogger<AccelerometerCalibrationService> _logger;
    private readonly IConnectionService _connectionService;
    
    // MissionPlanner exact state variables
    private bool _incalibrate = false;  // Matches MissionPlanner's _incalibrate
    private int _pos = 0;               // Position FROM FC (via COMMAND_LONG) - we echo this back
    private int _count = 0;             // Matches MissionPlanner's count (click counter)
    private readonly object _lock = new();
    
    // Timeout tracking
    private DateTime _lastPositionAcceptedTime;
    private System.Timers.Timer? _positionTimeoutTimer;
    private const int POSITION_TIMEOUT_SECONDS = 15; // If no new position after 15s, warn user
    
    // Diagnostics
    private DateTime _calibrationStartTime;
    private readonly List<string> _statusTextLog = new();
    
    private bool _disposed;
    
    // Events
    public event EventHandler<AccelCalibrationStateChangedEventArgs>? StateChanged;
    public event EventHandler<AccelPositionRequestedEventArgs>? PositionRequested;
    public event EventHandler<AccelCalibrationCompletedEventArgs>? CalibrationCompleted;
    public event EventHandler<AccelStatusTextEventArgs>? StatusTextReceived;
    
    public bool IsCalibrating
    {
        get { lock (_lock) return _incalibrate; }
    }
    
    public int CurrentPosition
    {
        get { lock (_lock) return _pos; }
    }
    
    public bool CanConfirmPosition
    {
        get { lock (_lock) return _incalibrate && _pos >= 1 && _pos <= 6; }
    }
    
    public AccelerometerCalibrationService(
        ILogger<AccelerometerCalibrationService> logger,
        IConnectionService connectionService)
    {
        _logger = logger;
        _connectionService = connectionService;
        
        _logger.LogInformation("[AccelCal] Service initialized - Mission Planner exact implementation");
        
        _connectionService.StatusTextReceived += OnStatusTextReceived;
        _connectionService.CommandAckReceived += OnCommandAckReceived;
        _connectionService.CommandLongReceived += OnCommandLongReceived;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        
        // Initialize timeout timer
        _positionTimeoutTimer = new System.Timers.Timer(POSITION_TIMEOUT_SECONDS * 1000);
        _positionTimeoutTimer.Elapsed += OnPositionTimeout;
        _positionTimeoutTimer.AutoReset = false;
    }
    
    private void OnPositionTimeout(object? sender, System.Timers.ElapsedEventArgs e)
    {
        bool isCalibrating;
        int currentPos;
        lock (_lock)
        {
            isCalibrating = _incalibrate;
            currentPos = _pos;
        }
        
        if (!isCalibrating)
            return;
        
        _logger.LogWarning("[AccelCal] TIMEOUT: No new position received for {Seconds}s after position {Pos} was accepted", 
            POSITION_TIMEOUT_SECONDS, currentPos);
        _logger.LogWarning("[AccelCal] This usually means the calibration was aborted by FC due to PreArm issues.");
        
        // Notify user
        StatusTextReceived?.Invoke(this, new AccelStatusTextEventArgs
        {
            Severity = 4, // WARNING
            Text = $"Timeout waiting for next position. Calibration may have been aborted. Check PreArm status (RC, safety switch, compass).",
            Timestamp = DateTime.UtcNow
        });
        
        // Finish calibration as failed
        FinishCalibration(false, "Calibration timed out - likely aborted due to PreArm violations");
    }
    
    #region Public API
    
    public bool HandleButtonClick()
    {
        lock (_lock)
        {
            if (_incalibrate)
                return ConfirmPositionInternal();
            else
                return StartCalibrationInternal();
        }
    }
    
    public bool StartCalibration()
    {
        lock (_lock)
        {
            if (_incalibrate)
            {
                _logger.LogWarning("[AccelCal] Already calibrating");
                return false;
            }
            return StartCalibrationInternal();
        }
    }
    
    public bool ConfirmPosition()
    {
        lock (_lock)
        {
            if (!_incalibrate)
            {
                _logger.LogWarning("[AccelCal] Not in calibration mode");
                return false;
            }
            return ConfirmPositionInternal();
        }
    }
    
    public void CancelCalibration()
    {
        lock (_lock)
        {
            if (!_incalibrate)
                return;
            
            _logger.LogInformation("[AccelCal] Calibration CANCELLED by user");
            _incalibrate = false;
            _pos = 0;
            _count = 0;
        }
        
        RaiseCalibrationCompleted(AccelCalibrationResult.Cancelled, "Cancelled by user");
    }
    
    #endregion
    
    #region Internal Methods
    
    private bool StartCalibrationInternal()
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogError("[AccelCal] Not connected to FC");
            return false;
        }
        
        _calibrationStartTime = DateTime.UtcNow;
        _count = 0;
        _pos = 0;
        _statusTextLog.Clear();
        
        _logger.LogInformation("[AccelCal] === STARTING ACCEL CALIBRATION ===");
        _logger.LogInformation("[AccelCal] Sending MAV_CMD_PREFLIGHT_CALIBRATION (param5=1)");
        
        // Send the standard 6-axis accelerometer calibration command
        // param5=1 = Full 6-axis accelerometer calibration
        _connectionService.SendPreflightCalibration(
            gyro: 0,
            mag: 0,
            groundPressure: 0,
            airspeed: 0,
            accel: 1);
        
        _incalibrate = true;
        
        _logger.LogInformation("[AccelCal] _incalibrate = true, waiting for FC position request via COMMAND_LONG/STATUSTEXT");
        _logger.LogWarning("[AccelCal] NOTE: If PreArm messages appear (RC not found, safety switch, compass), calibration may abort.");
        _logger.LogWarning("[AccelCal] TIP: Press safety switch and ensure vehicle is stationary during calibration.");
        
        RaiseStateChanged(false, true);
        
        return true;
    }
    
    /// <summary>
    /// Confirm position - Mission Planner style (fire-and-forget)
    /// 
    /// KEY: We echo back the EXACT position that FC requested via COMMAND_LONG
    /// We do NOT wait for ACK - FC will send next position or completion via STATUSTEXT/COMMAND_LONG
    /// </summary>
    private bool ConfirmPositionInternal()
    {
        if (_pos < 1 || _pos > 6)
        {
            _logger.LogWarning("[AccelCal] Cannot confirm - no valid position requested (pos={Pos})", _pos);
            return false;
        }
        
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("[AccelCal] Cannot confirm - not connected");
            return false;
        }
        
        _count++;
        
        _logger.LogInformation("[AccelCal] >>> User confirming position {Pos} ({Name}), count={Count}", 
            _pos, GetPositionName(_pos), _count);
        _logger.LogInformation("[AccelCal] >>> Sending MAV_CMD_ACCELCAL_VEHICLE_POS(param1={Pos}) [FIRE-AND-FORGET]", _pos);
        
        // CRITICAL: Use fire-and-forget (Mission Planner's sendPacket() style)
        // We echo back the EXACT position that FC requested
        // DO NOT wait for ACK - FC responds via STATUSTEXT/COMMAND_LONG
        _connectionService.SendAccelCalVehiclePos(_pos);
        
        // DO NOT RESET _pos HERE!
        // Mission Planner keeps the position until FC sends the next one
        // FC will send next position request via COMMAND_LONG or STATUSTEXT
        
        _logger.LogInformation("[AccelCal] Position {Pos} sent, waiting for FC response (next position or completion)...", _pos);
        
        return true;
    }
    
    #endregion
    
    #region Event Handlers
    
    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        if (!connected)
        {
            bool wasCalibrating;
            lock (_lock)
            {
                wasCalibrating = _incalibrate;
                _incalibrate = false;
                _pos = 0;
                _count = 0;
            }
            
            if (wasCalibrating)
            {
                _logger.LogWarning("[AccelCal] Connection lost during calibration!");
                RaiseCalibrationCompleted(AccelCalibrationResult.Failed, "Connection lost");
            }
        }
    }
    
    /// <summary>
    /// Handle COMMAND_ACK - for PREFLIGHT_CALIBRATION only
    /// Note: For ACCELCAL_VEHICLE_POS, we use fire-and-forget so we may or may not get ACK
    /// </summary>
    private void OnCommandAckReceived(object? sender, CommandAckEventArgs e)
    {
        _logger.LogInformation("[AccelCal] COMMAND_ACK: cmd={Command}, result={Result}", e.Command, e.Result);
        
        bool isCalibrating;
        int currentPos;
        lock (_lock) 
        { 
            isCalibrating = _incalibrate;
            currentPos = _pos;
        }
        
        if (!isCalibrating)
            return;
        
        // MAV_CMD_PREFLIGHT_CALIBRATION = 241
        if (e.Command == 241)
        {
            _logger.LogInformation("[AccelCal] COMMAND_ACK for PREFLIGHT_CALIBRATION: result={Result}, success={Success}", 
                e.Result, e.IsSuccess);
            
            if (!e.IsSuccess)
            {
                _logger.LogError("[AccelCal] FC REJECTED calibration command!");
                
                lock (_lock)
                {
                    _incalibrate = false;
                    _pos = 0;
                }
                
                string errorMsg = e.Result switch
                {
                    1 => "Temporarily rejected - try again",
                    2 => "Denied - vehicle must be disarmed",
                    3 => "Not supported by firmware",
                    4 => "Failed",
                    _ => $"Rejected (code: {e.Result})"
                };
                
                RaiseCalibrationCompleted(AccelCalibrationResult.Failed, errorMsg);
            }
            else
            {
                _logger.LogInformation("[AccelCal] Calibration started, waiting for FC to send position request...");
            }
        }
        // MAV_CMD_ACCELCAL_VEHICLE_POS = 42429
        else if (e.Command == 42429)
        {
            _logger.LogInformation("[AccelCal] COMMAND_ACK for ACCELCAL_VEHICLE_POS: result={Result}, pos={Pos}", 
                e.Result, currentPos);
            
            if (e.IsSuccess)
            {
                _logger.LogInformation("[AccelCal] *** Position {Pos} ({Name}) ACCEPTED by FC - sampling... ***", 
                    currentPos, GetPositionName(currentPos));
                
                // Start/reset timeout timer - if we don't get a new position soon, calibration may have aborted
                _lastPositionAcceptedTime = DateTime.UtcNow;
                _positionTimeoutTimer?.Stop();
                _positionTimeoutTimer?.Start();
                
                // Raise event to update UI - show "Sampling..." state
                StatusTextReceived?.Invoke(this, new AccelStatusTextEventArgs
                {
                    Severity = 6, // INFO
                    Text = $"Position {currentPos} ({GetPositionName(currentPos)}) accepted. FC is sampling...",
                    Timestamp = DateTime.UtcNow
                });
            }
            else
            {
                // CRITICAL: Position was REJECTED - calibration has likely been aborted by FC
                _logger.LogError("[AccelCal] *** Position {Pos} REJECTED (result={Result}) - CALIBRATION ABORTED ***", 
                    currentPos, e.Result);
                
                string failReason = e.Result switch
                {
                    1 => "Temporarily rejected - click again quickly",
                    2 => "Denied - check PreArm status",
                    3 => "Not supported",
                    4 => "Failed - calibration may have timed out. PreArm issues (RC, safety switch, compass) can cause this.",
                    _ => $"Failed (code: {e.Result})"
                };
                
                // Notify user but don't end calibration yet - they might want to retry
                StatusTextReceived?.Invoke(this, new AccelStatusTextEventArgs
                {
                    Severity = 3, // ERROR
                    Text = $"Position rejected: {failReason}. Try starting calibration again.",
                    Timestamp = DateTime.UtcNow
                });
                
                // If this is the second+ failure, abort the calibration
                if (_count > 1)
                {
                    _logger.LogError("[AccelCal] Multiple failures detected - aborting calibration");
                    FinishCalibration(false, $"Calibration failed: {failReason}");
                }
            }
        }
    }
    
    /// <summary>
    /// Handle STATUSTEXT - detect position requests and completion messages
    /// </summary>
    private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
    {
        bool isCalibrating;
        lock (_lock) { isCalibrating = _incalibrate; }
        
        if (!isCalibrating)
            return;
        
        var text = e.Text;
        var lower = text.ToLowerInvariant();
        
        lock (_lock)
        {
            _statusTextLog.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [{e.Severity}] {text}");
        }
        
        _logger.LogInformation("[AccelCal] <<< STATUSTEXT: [{Severity}] \"{Text}\"", e.Severity, text);
        
        StatusTextReceived?.Invoke(this, new AccelStatusTextEventArgs
        {
            Severity = e.Severity,
            Text = text,
            Timestamp = DateTime.UtcNow
        });
        
        // Check for calibration completion
        if (lower.Contains("calibration successful") || lower.Contains("accel calibration complete"))
        {
            _logger.LogInformation("[AccelCal] *** CALIBRATION SUCCESSFUL ***");
            FinishCalibration(true, text);
            return;
        }
        
        if (lower.Contains("calibration failed") || lower.Contains("accel calibration failed"))
        {
            _logger.LogError("[AccelCal] *** CALIBRATION FAILED ***");
            FinishCalibration(false, text);
            return;
        }
        
        if (lower.Contains("calibration cancelled") || lower.Contains("calibration timeout"))
        {
            _logger.LogWarning("[AccelCal] *** CALIBRATION CANCELLED/TIMEOUT ***");
            FinishCalibration(false, text);
            return;
        }
        
        // Detect position requests from STATUSTEXT
        // Note: COMMAND_LONG is the authoritative source, but STATUSTEXT can also trigger position detection
        var detectedPos = DetectPositionFromStatusText(lower);
        if (detectedPos.HasValue)
        {
            _logger.LogInformation("[AccelCal] Position {Pos} ({Name}) detected from STATUSTEXT", 
                detectedPos.Value, GetPositionName(detectedPos.Value));
            HandlePositionRequest(detectedPos.Value, text);
        }
    }
    
    /// <summary>
    /// Handle COMMAND_LONG from FC - THIS IS THE AUTHORITATIVE SOURCE FOR POSITION REQUESTS
    /// 
    /// Mission Planner stores pos from incoming COMMAND_LONG and echoes it back exactly.
    /// This is the primary way FC tells us which position to calibrate.
    /// </summary>
    private void OnCommandLongReceived(object? sender, CommandLongEventArgs e)
    {
        bool isCalibrating;
        lock (_lock) { isCalibrating = _incalibrate; }
        
        if (!isCalibrating)
            return;
        
        // MAV_CMD_ACCELCAL_VEHICLE_POS = 42429
        if (e.Command == 42429)
        {
            int position = (int)e.Param1;
            
            _logger.LogInformation("[AccelCal] <<< COMMAND_LONG: MAV_CMD_ACCELCAL_VEHICLE_POS param1={Position}", position);
            
            // Special completion values (from ArduPilot)
            if (position == 16777215) // ACCELCAL_VEHICLE_POS_SUCCESS
            {
                _logger.LogInformation("[AccelCal] *** SUCCESS via COMMAND_LONG (16777215) ***");
                FinishCalibration(true, "Calibration successful");
                return;
            }
            
            if (position == 16777216) // ACCELCAL_VEHICLE_POS_FAILED
            {
                _logger.LogError("[AccelCal] *** FAILED via COMMAND_LONG (16777216) ***");
                FinishCalibration(false, "Calibration failed");
                return;
            }
            
            // Normal position request (1-6)
            // THIS IS THE AUTHORITATIVE POSITION - store exactly what FC requested
            if (position >= 1 && position <= 6)
            {
                _logger.LogInformation("[AccelCal] <<< FC requesting position {Position} ({Name}) - STORING AS AUTHORITATIVE", 
                    position, GetPositionName(position));
                HandlePositionRequest(position, $"Please place vehicle {GetPositionName(position)}");
            }
            else
            {
                _logger.LogWarning("[AccelCal] Unknown position value: {Position}", position);
            }
        }
    }
    
    /// <summary>
    /// Handle position request from FC (via COMMAND_LONG or STATUSTEXT)
    /// Stores the position that FC requested - we will echo this back when user confirms
    /// </summary>
    private void HandlePositionRequest(int position, string message)
    {
        // Stop timeout timer - we got a new position!
        _positionTimeoutTimer?.Stop();
        
        lock (_lock)
        {
            _pos = position;  // Store FC's requested position - we echo this back exactly
        }
        
        _logger.LogInformation("[AccelCal] ========================================");
        _logger.LogInformation("[AccelCal] === POSITION {Position}/6: {Name} ===", position, GetPositionName(position));
        _logger.LogInformation("[AccelCal] ========================================");
        _logger.LogInformation("[AccelCal] _pos = {Position} (will echo this back when user confirms)", position);
        
        PositionRequested?.Invoke(this, new AccelPositionRequestedEventArgs
        {
            Position = position,
            PositionName = GetPositionName(position),
            FcMessage = message
        });
    }
    
    private void FinishCalibration(bool success, string message)
    {
        // Stop timeout timer
        _positionTimeoutTimer?.Stop();
        
        bool wasCalibrating;
        int finalPos;
        
        lock (_lock)
        {
            wasCalibrating = _incalibrate;
            finalPos = _pos;
            _incalibrate = false;
            _pos = 0;
            _count = 0;
        }
        
        if (!wasCalibrating)
            return;
        
        var duration = DateTime.UtcNow - _calibrationStartTime;
        
        _logger.LogInformation("[AccelCal] ========================================");
        _logger.LogInformation("[AccelCal] === CALIBRATION {Result} ===", success ? "SUCCESSFUL" : "FAILED");
        _logger.LogInformation("[AccelCal] ========================================");
        _logger.LogInformation("[AccelCal] Duration: {Duration:F1}s, Final position: {Position}", duration.TotalSeconds, finalPos);
        
        RaiseStateChanged(true, false);
        RaiseCalibrationCompleted(
            success ? AccelCalibrationResult.Success : AccelCalibrationResult.Failed,
            message);
    }
    
    #endregion
    
    #region Helper Methods
    
    /// <summary>
    /// Detect position from STATUSTEXT keywords.
    /// ArduPilot sends messages like:
    /// - "Place vehicle level and press any key"
    /// - "Place vehicle on its LEFT side and press any key"
    /// - "Place vehicle on its RIGHT side and press any key"
    /// - "Place vehicle nose DOWN and press any key"
    /// - "Place vehicle nose UP and press any key"
    /// - "Place vehicle on its BACK and press any key"
    /// </summary>
    private static int? DetectPositionFromStatusText(string lower)
    {
        // Must contain keywords indicating a position request
        if (!lower.Contains("place") && !lower.Contains("position") && !lower.Contains("press"))
            return null;
        
        // Skip completion/error messages
        if (lower.Contains("calibration") && (lower.Contains("successful") || lower.Contains("failed") || lower.Contains("complete")))
            return null;
        
        // Check specific positions (order matters - check compound terms first)
        if (lower.Contains("nose") && lower.Contains("down"))
            return 4;  // NOSEDOWN
        if (lower.Contains("nose") && lower.Contains("up"))
            return 5;  // NOSEUP
        if (lower.Contains("left") && !lower.Contains("right"))
            return 2;  // LEFT
        if (lower.Contains("right") && !lower.Contains("left"))
            return 3;  // RIGHT
        if (lower.Contains("back") || lower.Contains("upside") || lower.Contains("inverted"))
            return 6;  // BACK
        if (lower.Contains("level") || (lower.Contains("flat") && lower.Contains("surface")))
            return 1;  // LEVEL
        
        return null;
    }
    
    private static string GetPositionName(int position)
    {
        return position switch
        {
            1 => "LEVEL",
            2 => "LEFT",
            3 => "RIGHT",
            4 => "NOSEDOWN",
            5 => "NOSEUP",
            6 => "BACK",
            _ => $"UNKNOWN({position})"
        };
    }
    
    #endregion
    
    #region Event Raising
    
    private void RaiseStateChanged(bool wasCalibrating, bool isCalibrating)
    {
        StateChanged?.Invoke(this, new AccelCalibrationStateChangedEventArgs
        {
            OldState = wasCalibrating ? AccelCalibrationState.WaitingForUserConfirmation : AccelCalibrationState.Idle,
            NewState = isCalibrating ? AccelCalibrationState.WaitingForFirstPosition : AccelCalibrationState.Idle
        });
    }
    
    private void RaiseCalibrationCompleted(AccelCalibrationResult result, string message)
    {
        CalibrationCompleted?.Invoke(this, new AccelCalibrationCompletedEventArgs
        {
            Result = result,
            Message = message,
            Duration = DateTime.UtcNow - _calibrationStartTime
        });
    }
    
    #endregion
    
    #region IDisposable
    
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        
        CancelCalibration();
        
        // Clean up timeout timer
        _positionTimeoutTimer?.Stop();
        _positionTimeoutTimer?.Dispose();
        _positionTimeoutTimer = null;
        
        _connectionService.StatusTextReceived -= OnStatusTextReceived;
        _connectionService.CommandAckReceived -= OnCommandAckReceived;
        _connectionService.CommandLongReceived -= OnCommandLongReceived;
        _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
    }
    
    #endregion

}

#region Event Args

public class AccelCalibrationStateChangedEventArgs : EventArgs
{
    public AccelCalibrationState OldState { get; set; }
    public AccelCalibrationState NewState { get; set; }
}

public class AccelPositionRequestedEventArgs : EventArgs
{
    public int Position { get; set; }
    public string PositionName { get; set; } = "";
    public string FcMessage { get; set; } = "";
}

public class AccelStatusTextEventArgs : EventArgs
{
    public byte Severity { get; set; }
    public string Text { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

public class AccelCalibrationCompletedEventArgs : EventArgs
{
    public AccelCalibrationResult Result { get; set; }
    public string Message { get; set; } = "";
    public TimeSpan Duration { get; set; }
}

#endregion

#region Diagnostics

public class AccelCalibrationDiagnostics
{
    public AccelCalibrationState State { get; set; }
    public int CurrentPosition { get; set; }
    public DateTime StartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> StatusTextLog { get; set; } = new();
    public Dictionary<int, PositionAttempt> PositionAttempts { get; set; } = new();
}

public class PositionAttempt
{
    public int Position { get; set; }
    public string PositionName { get; set; } = "";
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptTime { get; set; }
    public bool LastAttemptSuccess { get; set; }
    public string LastAttemptMessage { get; set; } = "";
    public DateTime? SuccessTime { get; set; }
}

#endregion
