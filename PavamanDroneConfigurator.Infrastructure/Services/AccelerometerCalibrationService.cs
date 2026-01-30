using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Accelerometer calibration service implementing Mission Planner's exact architecture.
/// 
/// MISSION PLANNER ARCHITECTURE (REFERENCE):
/// 1. FC drives the entire calibration workflow
/// 2. Position validation is performed ENTIRELY on the Flight Controller
/// 3. Mission Planner is a "dumb" relay - it just shows instructions and forwards confirmations
/// 4. NO client-side IMU validation, NO threshold checks, NO hardcoded values
/// 5. FC sends STATUSTEXT for position requests, progress, and completion
/// 6. FC may also send COMMAND_LONG (MAV_CMD_ACCELCAL_VEHICLE_POS) for position requests
/// 
/// SIMPLIFIED STATE MACHINE (Mission Planner style):
/// - _inCalibration = false: Idle, button shows "Calibrate Accel"
/// - _inCalibration = true: Calibrating, button shows "Click When Done"
/// 
/// FLOW:
/// 1. User clicks "Calibrate Accel" button
/// 2. Send MAV_CMD_PREFLIGHT_CALIBRATION (param5=1 for 6-axis)
/// 3. Subscribe to STATUSTEXT and COMMAND_LONG packets
/// 4. FC sends position request: "Place vehicle level and press any key"
/// 5. UI shows position image, enables "Click When In Position" button
/// 6. User places vehicle, clicks button
/// 7. Send MAV_CMD_ACCELCAL_VEHICLE_POS (param1=1-6)
/// 8. FC validates internally, samples IMU data
/// 9. FC sends next position request or "Calibration successful/failed"
/// 10. Repeat for all 6 positions
/// 11. FC sends completion STATUSTEXT
/// 
/// CRITICAL: This service does NOT:
/// - Collect IMU samples (FC does this internally)
/// - Validate gravity magnitude (FC does this)
/// - Check axis orientation (FC does this)
/// - Use hardcoded thresholds (FC has its own validation)
/// - Auto-complete or use timeouts (FC drives timing)
/// </summary>
public class AccelerometerCalibrationService : IDisposable
{
    private readonly ILogger<AccelerometerCalibrationService> _logger;
    private readonly IConnectionService _connectionService;
    
    // Simple two-state model (Mission Planner style)
    private bool _inCalibration = false;
    private readonly object _lock = new();
    
    // Current position being calibrated (1-6, from FC)
    private AccelCalibrationPosition _currentPosition = AccelCalibrationPosition.Level;
    
    // Click counter for position confirmations
    private int _positionClickCount = 0;
    
    // Diagnostics
    private DateTime _calibrationStartTime;
    private readonly List<string> _statusTextLog = new();
    
    // Cancellation support
    private CancellationTokenSource? _calibrationCts;
    private bool _disposed;
    
    // Position names for display (matches Mission Planner ACCELCAL_VEHICLE_POS enum)
    private static readonly string[] PositionNames = { "", "LEVEL", "LEFT", "RIGHT", "NOSE DOWN", "NOSE UP", "BACK" };
    
    // Events (for UI binding)
    public event EventHandler<AccelCalibrationStateChangedEventArgs>? StateChanged;
    public event EventHandler<AccelPositionRequestedEventArgs>? PositionRequested;
    public event EventHandler<AccelCalibrationCompletedEventArgs>? CalibrationCompleted;
    public event EventHandler<AccelStatusTextEventArgs>? StatusTextReceived;
    
    /// <summary>
    /// Whether calibration is currently in progress (Mission Planner's _incalibrate)
    /// </summary>
    public bool IsCalibrating
    {
        get { lock (_lock) return _inCalibration; }
    }
    
    /// <summary>
    /// Current position requested by FC (1-6)
    /// </summary>
    public AccelCalibrationPosition CurrentPosition
    {
        get { lock (_lock) return _currentPosition; }
    }
    
    /// <summary>
    /// Current position as integer (1-6)
    /// </summary>
    public int CurrentPositionIndex
    {
        get { lock (_lock) return (int)_currentPosition; }
    }
    
    public AccelerometerCalibrationService(
        ILogger<AccelerometerCalibrationService> logger,
        IConnectionService connectionService)
    {
        _logger = logger;
        _connectionService = connectionService;
        
        // Subscribe to MAVLink events (Mission Planner's SubscribeToPacketType equivalent)
        _connectionService.StatusTextReceived += OnStatusTextReceived;
        _connectionService.CommandAckReceived += OnCommandAckReceived;
        _connectionService.CommandLongReceived += OnCommandLongReceived;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
    }
    
    #region Public API (Mission Planner Button Handlers)
    
    /// <summary>
    /// Start 6-position accelerometer calibration.
    /// Called when user clicks "Calibrate Accel" button (first click).
    /// Sends MAV_CMD_PREFLIGHT_CALIBRATION with param5=1.
    /// </summary>
    public bool StartCalibration()
    {
        lock (_lock)
        {
            if (_inCalibration)
            {
                _logger.LogWarning("StartCalibration: Already calibrating");
                return false;
            }
            
            if (!_connectionService.IsConnected)
            {
                _logger.LogError("StartCalibration: Not connected to FC");
                return false;
            }
            
            // Initialize calibration session
            _calibrationCts?.Cancel();
            _calibrationCts?.Dispose();
            _calibrationCts = new CancellationTokenSource();
            
            _calibrationStartTime = DateTime.UtcNow;
            _currentPosition = AccelCalibrationPosition.Level;
            _positionClickCount = 0;
            _statusTextLog.Clear();
            _inCalibration = true;
        }
        
        _logger.LogInformation("=== ACCELEROMETER CALIBRATION STARTED ===");
        _logger.LogInformation("Sending MAV_CMD_PREFLIGHT_CALIBRATION (param5=1) - 6-axis accel cal");
        
        // Notify UI of state change
        RaiseStateChanged(false, true);
        
        // Send calibration command to FC
        // param5=1 triggers 6-position accelerometer calibration (Mission Planner behavior)
        _connectionService.SendPreflightCalibration(
            gyro: 0,
            mag: 0,
            groundPressure: 0,
            airspeed: 0,
            accel: 1);  // 1 = 6-axis accel calibration (matches Mission Planner)
        
        return true;
    }
    
    /// <summary>
    /// User confirms vehicle is in the requested position.
    /// Called when user clicks "Click When In Position" button.
    /// Sends MAV_CMD_ACCELCAL_VEHICLE_POS with current position (1-6).
    /// 
    /// MISSION PLANNER BEHAVIOR:
    /// - Does NOT validate IMU data
    /// - Does NOT check orientation
    /// - Just sends position to FC immediately
    /// - FC decides if position is correct
    /// </summary>
    public bool ConfirmPosition()
    {
        int position;
        
        lock (_lock)
        {
            if (!_inCalibration)
            {
                _logger.LogWarning("ConfirmPosition: Not in calibration mode");
                return false;
            }
            
            position = (int)_currentPosition;
            _positionClickCount++;
        }
        
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("ConfirmPosition: Not connected to FC");
            return false;
        }
        
        // Validate position range (1-6)
        if (position < 1 || position > 6)
        {
            _logger.LogWarning("ConfirmPosition: Invalid position {Position} (expected 1-6)", position);
            return false;
        }
        
        _logger.LogInformation("User confirmed position {Position} ({Name}) - sending MAV_CMD_ACCELCAL_VEHICLE_POS({Position})",
            position, GetPositionName(position), position);
        
        // Send position confirmation to FC (Mission Planner's sendPacket equivalent)
        // FC will validate the position internally and respond via STATUSTEXT
        _connectionService.SendAccelCalVehiclePos(position);
        
        return true;
    }
    
    /// <summary>
    /// Start simple accelerometer calibration (automatic, no user positions).
    /// Vehicle must be on level surface. FC performs automatic calibration.
    /// </summary>
    public bool StartSimpleCalibration()
    {
        lock (_lock)
        {
            if (_inCalibration)
            {
                _logger.LogWarning("StartSimpleCalibration: Already calibrating");
                return false;
            }
            
            if (!_connectionService.IsConnected)
            {
                _logger.LogError("StartSimpleCalibration: Not connected to FC");
                return false;
            }
            
            _calibrationCts?.Cancel();
            _calibrationCts?.Dispose();
            _calibrationCts = new CancellationTokenSource();
            
            _calibrationStartTime = DateTime.UtcNow;
            _statusTextLog.Clear();
            _inCalibration = true;
        }
        
        _logger.LogInformation("Starting simple accelerometer calibration (param5=4)");
        
        RaiseStateChanged(false, true);
        
        // param5=4 for simple accelerometer calibration
        _connectionService.SendPreflightCalibration(
            gyro: 0,
            mag: 0,
            groundPressure: 0,
            airspeed: 0,
            accel: 4);
        
        return true;
    }
    
    /// <summary>
    /// Start level-only calibration (AHRS trims).
    /// Vehicle must be on perfectly level surface.
    /// </summary>
    public bool StartLevelCalibration()
    {
        lock (_lock)
        {
            if (_inCalibration)
            {
                _logger.LogWarning("StartLevelCalibration: Already calibrating");
                return false;
            }
            
            if (!_connectionService.IsConnected)
            {
                _logger.LogError("StartLevelCalibration: Not connected to FC");
                return false;
            }
            
            _calibrationCts?.Cancel();
            _calibrationCts?.Dispose();
            _calibrationCts = new CancellationTokenSource();
            
            _calibrationStartTime = DateTime.UtcNow;
            _statusTextLog.Clear();
            _inCalibration = true;
        }
        
        _logger.LogInformation("Starting level-only calibration (param5=2)");
        
        RaiseStateChanged(false, true);
        
        // param5=2 for level-only calibration
        _connectionService.SendPreflightCalibration(
            gyro: 0,
            mag: 0,
            groundPressure: 0,
            airspeed: 0,
            accel: 2);
        
        return true;
    }
    
    /// <summary>
    /// Cancel calibration.
    /// </summary>
    public void CancelCalibration()
    {
        lock (_lock)
        {
            if (!_inCalibration)
                return;
            
            _logger.LogInformation("Calibration cancelled by user");
            
            _calibrationCts?.Cancel();
            _inCalibration = false;
        }
        
        RaiseStateChanged(true, false);
        RaiseCalibrationCompleted(AccelCalibrationResult.Cancelled, "Cancelled by user");
    }
    
    /// <summary>
    /// Get diagnostics for current/last calibration session.
    /// </summary>
    public AccelCalibrationDiagnostics GetDiagnostics()
    {
        lock (_lock)
        {
            return new AccelCalibrationDiagnostics
            {
                State = _inCalibration ? AccelCalibrationState.WaitingForUserConfirmation : AccelCalibrationState.Idle,
                CurrentPosition = (int)_currentPosition,
                StartTime = _calibrationStartTime,
                Duration = DateTime.UtcNow - _calibrationStartTime,
                StatusTextLog = new List<string>(_statusTextLog),
                PositionAttempts = new Dictionary<int, PositionAttempt>()
            };
        }
    }
    
    #endregion
    
    #region Event Handlers (Mission Planner's receivedPacket equivalent)
    
    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        if (!connected)
        {
            lock (_lock)
            {
                if (_inCalibration)
                {
                    _logger.LogWarning("Connection lost during calibration");
                    _calibrationCts?.Cancel();
                    _inCalibration = false;
                }
            }
            
            RaiseCalibrationCompleted(AccelCalibrationResult.Failed, "Connection lost during calibration");
        }
    }
    
    /// <summary>
    /// Handle COMMAND_ACK responses from FC.
    /// Mission Planner uses this to detect command acceptance/rejection.
    /// </summary>
    private void OnCommandAckReceived(object? sender, CommandAckEventArgs e)
    {
        if (!IsCalibrating)
            return;
        
        // MAV_CMD_PREFLIGHT_CALIBRATION = 241
        if (e.Command == 241)
        {
            HandleCalibrationCommandAck(e.Result);
        }
        // MAV_CMD_ACCELCAL_VEHICLE_POS = 42429
        else if (e.Command == 42429)
        {
            HandlePositionCommandAck(e.Result);
        }
    }
    
    /// <summary>
    /// Handle STATUSTEXT messages from FC.
    /// THIS IS THE PRIMARY COMMUNICATION CHANNEL.
    /// FC sends position requests, progress updates, and completion via STATUSTEXT.
    /// </summary>
    private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
    {
        if (!IsCalibrating)
            return;
        
        var text = e.Text;
        var lower = text.ToLowerInvariant();
        
        // Log all STATUSTEXT for diagnostics (Mission Planner does this)
        lock (_lock)
        {
            _statusTextLog.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [{e.Severity}] {text}");
        }
        
        _logger.LogInformation("FC STATUSTEXT: [{Severity}] {Text}", e.Severity, text);
        
        // Raise event for UI to display raw FC message
        StatusTextReceived?.Invoke(this, new AccelStatusTextEventArgs
        {
            Severity = e.Severity,
            Text = text,
            Timestamp = DateTime.UtcNow
        });
        
        // MISSION PLANNER BEHAVIOR:
        // Only process messages that contain position keywords or calibration status
        // Filter out PreArm, EKF, GPS, etc. messages - they don't affect calibration
        
        // Check for completion first
        if (IsSuccessMessage(lower))
        {
            _logger.LogInformation("FC reported CALIBRATION SUCCESS: {Text}", text);
            FinishCalibration(true, text);
            return;
        }
        
        // Check for failure
        if (IsFailureMessage(lower))
        {
            _logger.LogWarning("FC reported CALIBRATION FAILURE: {Text}", text);
            FinishCalibration(false, text);
            return;
        }
        
        // Check for position request (FC telling us which position it wants)
        var requestedPosition = DetectPositionRequest(lower);
        if (requestedPosition.HasValue)
        {
            HandlePositionRequest(requestedPosition.Value, text);
            return;
        }
        
        // Other messages are informational - just display to user
        // Examples: "Rotation bad", sampling progress, etc.
        _logger.LogDebug("FC informational message: {Text}", text);
    }
    
    /// <summary>
    /// Handle COMMAND_LONG messages from FC.
    /// FC may send MAV_CMD_ACCELCAL_VEHICLE_POS to request positions.
    /// This is more reliable than parsing STATUSTEXT.
    /// </summary>
    private void OnCommandLongReceived(object? sender, CommandLongEventArgs e)
    {
        if (!IsCalibrating)
            return;
        
        // MAV_CMD_ACCELCAL_VEHICLE_POS = 42429
        if (e.Command == 42429)
        {
            // param1 contains position enum (1-6)
            int position = (int)e.Param1;
            
            _logger.LogInformation("FC requesting position via COMMAND_LONG: MAV_CMD_ACCELCAL_VEHICLE_POS param1={Position}", position);
            
            if (position >= 1 && position <= 6)
            {
                HandlePositionRequest(position, $"Place vehicle {GetPositionName(position)}");
            }
        }
    }
    
    #endregion
    
    #region Message Processing (Mission Planner style)
    
    private void HandleCalibrationCommandAck(byte result)
    {
        var mavResult = (MavResult)result;
        
        if (mavResult == MavResult.Accepted || mavResult == MavResult.InProgress)
        {
            _logger.LogInformation("FC accepted calibration command (MAV_RESULT={Result})", mavResult);
            // Now waiting for FC to send STATUSTEXT with first position request
        }
        else
        {
            _logger.LogError("FC rejected calibration command: MAV_RESULT={Result}", mavResult);
            
            string errorMessage = mavResult switch
            {
                MavResult.TemporarilyRejected => "Calibration temporarily rejected - FC may be busy. Try again.",
                MavResult.Denied => "Calibration denied - check vehicle is disarmed and stationary.",
                MavResult.Unsupported => "Calibration not supported by this firmware version.",
                MavResult.Failed => "Calibration failed to start - check FC console for errors.",
                _ => $"Calibration rejected (code: {result})"
            };
            
            FinishCalibration(false, errorMessage);
        }
    }
    
    private void HandlePositionCommandAck(byte result)
    {
        var mavResult = (MavResult)result;
        int position;
        
        lock (_lock)
        {
            position = (int)_currentPosition;
        }
        
        if (mavResult == MavResult.Accepted || mavResult == MavResult.InProgress)
        {
            _logger.LogInformation("FC accepted position {Position} - FC is now sampling IMU data", position);
            // FC will send STATUSTEXT for next position or completion
        }
        else
        {
            _logger.LogWarning("FC rejected position {Position}: MAV_RESULT={Result}", position, mavResult);
            // FC may send "Rotation bad" or similar via STATUSTEXT
            // Just log it - FC will tell us what to do next
        }
    }
    
    /// <summary>
    /// FC is requesting a specific position.
    /// Called when we detect a position request in STATUSTEXT or COMMAND_LONG.
    /// </summary>
    private void HandlePositionRequest(int position, string fcMessage)
    {
        lock (_lock)
        {
            _currentPosition = (AccelCalibrationPosition)position;
        }
        
        _logger.LogInformation("FC requesting position {Position}: {Name}", position, GetPositionName(position));
        _logger.LogInformation("FC message: {Message}", fcMessage);
        
        // Notify UI to show position image and enable button
        RaisePositionRequested(position, fcMessage);
    }
    
    private void FinishCalibration(bool success, string message)
    {
        bool wasCalibrating;
        
        lock (_lock)
        {
            wasCalibrating = _inCalibration;
            _inCalibration = false;
            _calibrationCts?.Cancel();
        }
        
        if (!wasCalibrating)
            return;
        
        var duration = DateTime.UtcNow - _calibrationStartTime;
        
        _logger.LogInformation("=== ACCELEROMETER CALIBRATION {Result} ===", success ? "COMPLETED" : "FAILED");
        _logger.LogInformation("Duration: {Duration:F1}s", duration.TotalSeconds);
        _logger.LogInformation("FC message: {Message}", message);
        
        RaiseStateChanged(true, false);
        RaiseCalibrationCompleted(
            success ? AccelCalibrationResult.Success : AccelCalibrationResult.Failed,
            message);
    }
    
    #endregion
    
    #region STATUSTEXT Parsing (Mission Planner style - inline, simple)
    
    /// <summary>
    /// Detect which position FC is requesting based on STATUSTEXT keywords.
    /// Mission Planner does this inline in receivedPacket().
    /// </summary>
    private static int? DetectPositionRequest(string lower)
    {
        // Must contain "place" or similar to be a position request
        if (!lower.Contains("place") && !lower.Contains("position"))
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
        if (lower.Contains("back") || lower.Contains("upside"))
            return 6;  // BACK
        if (lower.Contains("level"))
            return 1;  // LEVEL
        
        return null;
    }
    
    /// <summary>
    /// Check if STATUSTEXT indicates calibration success.
    /// </summary>
    private static bool IsSuccessMessage(string lower)
    {
        return lower.Contains("calibration successful") ||
               lower.Contains("calibration complete") ||
               lower.Contains("calibration done") ||
               lower.Contains("accel calibration successful") ||
               (lower.Contains("accel") && lower.Contains("offsets") && lower.Contains("saved"));
    }
    
    /// <summary>
    /// Check if STATUSTEXT indicates calibration failure.
    /// Note: "PreArm" messages are NOT failures - they're filtered.
    /// </summary>
    private static bool IsFailureMessage(string lower)
    {
        // Filter out PreArm messages (not calibration failures)
        if (lower.Contains("prearm"))
            return false;
        
        // Check for actual calibration failures
        return lower.Contains("calibration failed") ||
               lower.Contains("accel cal failed") ||
               lower.Contains("calibration cancelled") ||
               lower.Contains("calibration timeout");
    }
    
    private static string GetPositionName(int position)
    {
        return position >= 1 && position <= 6 ? PositionNames[position] : "UNKNOWN";
    }
    
    #endregion
    
    #region Event Raising
    
    private void RaiseStateChanged(bool oldInCalibration, bool newInCalibration)
    {
        StateChanged?.Invoke(this, new AccelCalibrationStateChangedEventArgs
        {
            OldState = oldInCalibration ? AccelCalibrationState.WaitingForUserConfirmation : AccelCalibrationState.Idle,
            NewState = newInCalibration ? AccelCalibrationState.WaitingForUserConfirmation : AccelCalibrationState.Idle
        });
    }
    
    private void RaisePositionRequested(int position, string fcMessage)
    {
        PositionRequested?.Invoke(this, new AccelPositionRequestedEventArgs
        {
            Position = position,
            PositionName = GetPositionName(position),
            FcMessage = fcMessage
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
        
        _calibrationCts?.Cancel();
        _calibrationCts?.Dispose();
        
        _connectionService.StatusTextReceived -= OnStatusTextReceived;
        _connectionService.CommandAckReceived -= OnCommandAckReceived;
        _connectionService.CommandLongReceived -= OnCommandLongReceived;
        _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
    }
    
    #endregion
}

#region Event Args

/// <summary>
/// Event args for calibration state changes.
/// </summary>
public class AccelCalibrationStateChangedEventArgs : EventArgs
{
    public AccelCalibrationState OldState { get; set; }
    public AccelCalibrationState NewState { get; set; }
}

/// <summary>
/// Event args when FC requests a position.
/// </summary>
public class AccelPositionRequestedEventArgs : EventArgs
{
    /// <summary>Position number (1-6)</summary>
    public int Position { get; set; }
    /// <summary>Position name for display</summary>
    public string PositionName { get; set; } = "";
    /// <summary>Original FC message</summary>
    public string FcMessage { get; set; } = "";
}

/// <summary>
/// Event args for FC STATUSTEXT during calibration.
/// </summary>
public class AccelStatusTextEventArgs : EventArgs
{
    public byte Severity { get; set; }
    public string Text { get; set; } = "";
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Event args when calibration completes.
/// </summary>
public class AccelCalibrationCompletedEventArgs : EventArgs
{
    public AccelCalibrationResult Result { get; set; }
    public string Message { get; set; } = "";
    public TimeSpan Duration { get; set; }
}

#endregion

#region Diagnostics Models

/// <summary>
/// Diagnostics for calibration session.
/// </summary>
public class AccelCalibrationDiagnostics
{
    public AccelCalibrationState State { get; set; }
    public int CurrentPosition { get; set; }
    public DateTime StartTime { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> StatusTextLog { get; set; } = new();
    public Dictionary<int, PositionAttempt> PositionAttempts { get; set; } = new();
}

/// <summary>
/// Diagnostics for individual position attempts.
/// </summary>
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
