using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Infrastructure.MAVLink;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Accelerometer calibration service implementing ASV-Mavlink architecture patterns.
/// 
/// CRITICAL SAFETY RULES (Mission Planner behavior):
/// 1. FC drives the workflow via STATUSTEXT messages
/// 2. NEVER auto-complete calibration
/// 3. NEVER use timeouts to finish
/// 4. User MUST confirm every position
/// 5. IMU validation MUST pass before sending to FC
/// 6. Finish ONLY when FC sends success STATUSTEXT
/// 
/// Flow:
/// 1. Send MAV_CMD_PREFLIGHT_CALIBRATION (param5=4)
/// 2. FC sends STATUSTEXT "Place vehicle level" (or similar)
/// 3. User positions vehicle and clicks button
/// 4. Service validates IMU data
/// 5. If valid: Send MAV_CMD_ACCELCAL_VEHICLE_POS (fire-and-forget)
/// 6. FC samples and sends next position request
/// 7. Repeat for all 6 positions
/// 8. FC sends "Calibration successful" STATUSTEXT
/// </summary>
public class AccelerometerCalibrationService : IDisposable
{
    private readonly ILogger<AccelerometerCalibrationService> _logger;
    private readonly IConnectionService _connectionService;
    private readonly AccelImuValidator _imuValidator;
    private readonly AccelStatusTextParser _statusTextParser;
    
    // State machine
    private AccelCalibrationState _state = AccelCalibrationState.Idle;
    private readonly object _stateLock = new();
    
    // Cancellation support (ASV-Mavlink pattern)
    private CancellationTokenSource? _calibrationCts;
    private bool _disposed;
    
    // Current position being calibrated (1-6)
    private int _currentPosition;
    
    // IMU sample collection
    private readonly List<RawImuData> _imuSamples = new();
    private RawImuData? _latestImuData;
    private const int IMU_SAMPLES_REQUIRED = 50; // 1 second at 50Hz
    
    // Diagnostics
    private DateTime _calibrationStartTime;
    private readonly List<string> _statusTextLog = new();
    private readonly Dictionary<int, PositionAttempt> _positionAttempts = new();
    
    // Events (matching ASV-Mavlink patterns)
    public event EventHandler<AccelCalibrationStateChangedEventArgs>? StateChanged;
    public event EventHandler<AccelPositionRequestedEventArgs>? PositionRequested;
    public event EventHandler<AccelPositionValidationEventArgs>? PositionValidated;
    public event EventHandler<AccelCalibrationCompletedEventArgs>? CalibrationCompleted;
    
    // Position names for logging
    private static readonly string[] PositionNames = { "", "LEVEL", "LEFT", "RIGHT", "NOSE DOWN", "NOSE UP", "BACK" };
    
    public AccelCalibrationState CurrentState
    {
        get { lock (_stateLock) return _state; }
    }
    
    public int CurrentPosition
    {
        get { lock (_stateLock) return _currentPosition; }
    }
    
    public bool IsCalibrating
    {
        get
        {
            lock (_stateLock)
            {
                return _state != AccelCalibrationState.Idle &&
                       _state != AccelCalibrationState.Completed &&
                       _state != AccelCalibrationState.Failed &&
                       _state != AccelCalibrationState.Cancelled &&
                       _state != AccelCalibrationState.Rejected;
            }
        }
    }
    
    public AccelerometerCalibrationService(
        ILogger<AccelerometerCalibrationService> logger,
        IConnectionService connectionService,
        AccelImuValidator imuValidator,
        AccelStatusTextParser statusTextParser)
    {
        _logger = logger;
        _connectionService = connectionService;
        _imuValidator = imuValidator;
        _statusTextParser = statusTextParser;
        
        // Subscribe to MAVLink events
        _connectionService.CommandAckReceived += OnCommandAckReceived;
        _connectionService.StatusTextReceived += OnStatusTextReceived;
        _connectionService.RawImuReceived += OnRawImuReceived;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
    }
    
    #region Public API
    
    /// <summary>
    /// Start full 6-position accelerometer calibration.
    /// Sends MAV_CMD_PREFLIGHT_CALIBRATION with param5=4.
    /// </summary>
    public bool StartCalibration()
    {
        lock (_stateLock)
        {
            if (IsCalibrating)
            {
                _logger.LogWarning("Cannot start accelerometer calibration - already in progress");
                return false;
            }
            
            if (!_connectionService.IsConnected)
            {
                _logger.LogError("Cannot start accelerometer calibration - not connected");
                return false;
            }
            
            // Initialize calibration session
            _calibrationCts?.Cancel();
            _calibrationCts?.Dispose();
            _calibrationCts = new CancellationTokenSource();
            
            _calibrationStartTime = DateTime.UtcNow;
            _currentPosition = 0;
            _statusTextLog.Clear();
            _positionAttempts.Clear();
            _imuSamples.Clear();
            
            // Transition to CommandSent
            TransitionState(AccelCalibrationState.CommandSent);
            
            // Send MAV_CMD_PREFLIGHT_CALIBRATION (param5 = 4 for 6-axis accel)
            _connectionService.SendPreflightCalibration(
                gyro: 0,
                mag: 0,
                groundPressure: 0,
                airspeed: 0,
                accel: 4);
            
            _logger.LogInformation("Accelerometer calibration started - MAV_CMD_PREFLIGHT_CALIBRATION sent (param5=4)");
            
            return true;
        }
    }
    
    /// <summary>
    /// User confirms vehicle is in the requested position.
    /// Triggers IMU validation before sending position to FC (fire-and-forget pattern).
    /// </summary>
    public async Task<bool> ConfirmPositionAsync(CancellationToken cancel = default)
    {
        AccelCalibrationState currentState;
        int position;
        CancellationToken combinedToken;
        
        lock (_stateLock)
        {
            currentState = _state;
            position = _currentPosition;
            
            if (_calibrationCts == null)
            {
                _logger.LogWarning("ConfirmPosition called without active calibration");
                return false;
            }
            
            combinedToken = CancellationTokenSource.CreateLinkedTokenSource(
                cancel, _calibrationCts.Token).Token;
        }
        
        if (currentState != AccelCalibrationState.WaitingForUserConfirmation &&
            currentState != AccelCalibrationState.PositionRejected)
        {
            _logger.LogWarning("ConfirmPosition called in invalid state: {State}", currentState);
            return false;
        }
        
        if (position < 1 || position > 6)
        {
            _logger.LogError("ConfirmPosition called with invalid position: {Position}", position);
            return false;
        }
        
        _logger.LogInformation("User confirmed position {Position} ({Name}) - starting IMU validation", 
            position, GetPositionName(position));
        
        // Transition to ValidatingPosition
        TransitionState(AccelCalibrationState.ValidatingPosition);
        
        // Clear samples for fresh collection
        lock (_imuSamples)
        {
            _imuSamples.Clear();
        }
        
        // Wait for IMU samples (up to 3 seconds)
        var samplesCollected = await WaitForImuSamplesAsync(3000, combinedToken);
        
        if (combinedToken.IsCancellationRequested)
        {
            _logger.LogInformation("Position confirmation cancelled");
            return false;
        }
        
        if (!samplesCollected)
        {
            _logger.LogWarning("Failed to collect IMU samples for position {Position}", position);
            
            TransitionState(AccelCalibrationState.PositionRejected);
            RaisePositionValidated(position, false, "Could not read IMU data. Check sensor connection.");
            
            // Return to waiting for user
            TransitionState(AccelCalibrationState.WaitingForUserConfirmation);
            return false;
        }
        
        // Validate position using averaged IMU data
        var avgImuData = AverageImuSamples();
        var validationResult = _imuValidator.ValidatePosition(position, avgImuData);
        
        // Record attempt for diagnostics
        RecordPositionAttempt(position, validationResult.IsValid, validationResult.ErrorMessage);
        
        RaisePositionValidated(position, validationResult.IsValid, validationResult.ErrorMessage);
        
        if (!validationResult.IsValid)
        {
            // Validation FAILED - reject position
            _logger.LogWarning("Position {Position} ({Name}) validation FAILED: {Message}", 
                position, GetPositionName(position), validationResult.ErrorMessage);
            
            TransitionState(AccelCalibrationState.PositionRejected);
            
            // Return to waiting for user to reposition
            TransitionState(AccelCalibrationState.WaitingForUserConfirmation);
            return false;
        }
        
        // Validation PASSED - send to FC (fire-and-forget pattern from ASV-Mavlink)
        _logger.LogInformation("Position {Position} ({Name}) validation PASSED - sending to FC", 
            position, GetPositionName(position));
        
        TransitionState(AccelCalibrationState.SendingPositionToFC);
        
        // Send MAV_CMD_ACCELCAL_VEHICLE_POS - fire and forget (ASV-Mavlink pattern)
        // FC will acknowledge via COMMAND_ACK and then send next position via STATUSTEXT
        _connectionService.SendAccelCalVehiclePos(position);
        
        return true;
    }
    
    /// <summary>
    /// Simple calibration (for large vehicles where 6-axis is impractical).
    /// Sends MAV_CMD_PREFLIGHT_CALIBRATION with param5=1.
    /// </summary>
    public bool StartSimpleCalibration()
    {
        lock (_stateLock)
        {
            if (IsCalibrating)
            {
                _logger.LogWarning("Cannot start simple calibration - already in progress");
                return false;
            }
            
            if (!_connectionService.IsConnected)
            {
                _logger.LogError("Cannot start simple calibration - not connected");
                return false;
            }
            
            _calibrationCts?.Cancel();
            _calibrationCts?.Dispose();
            _calibrationCts = new CancellationTokenSource();
            
            _calibrationStartTime = DateTime.UtcNow;
            _currentPosition = 0;
            _statusTextLog.Clear();
            
            TransitionState(AccelCalibrationState.CommandSent);
            
            // param5 = 1 for simple accelerometer calibration
            _connectionService.SendPreflightCalibration(
                gyro: 0,
                mag: 0,
                groundPressure: 0,
                airspeed: 0,
                accel: 1);
            
            _logger.LogInformation("Simple accelerometer calibration started (param5=1)");
            return true;
        }
    }
    
    /// <summary>
    /// Level-only calibration (sets AHRS trims).
    /// Sends MAV_CMD_PREFLIGHT_CALIBRATION with param5=2.
    /// </summary>
    public bool StartLevelCalibration()
    {
        lock (_stateLock)
        {
            if (IsCalibrating)
            {
                _logger.LogWarning("Cannot start level calibration - already in progress");
                return false;
            }
            
            if (!_connectionService.IsConnected)
            {
                _logger.LogError("Cannot start level calibration - not connected");
                return false;
            }
            
            _calibrationCts?.Cancel();
            _calibrationCts?.Dispose();
            _calibrationCts = new CancellationTokenSource();
            
            _calibrationStartTime = DateTime.UtcNow;
            _currentPosition = 0;
            _statusTextLog.Clear();
            
            TransitionState(AccelCalibrationState.CommandSent);
            
            // param5 = 2 for level-only calibration
            _connectionService.SendPreflightCalibration(
                gyro: 0,
                mag: 0,
                groundPressure: 0,
                airspeed: 0,
                accel: 2);
            
            _logger.LogInformation("Level-only calibration started (param5=2)");
            return true;
        }
    }
    
    /// <summary>
    /// Cancel ongoing calibration.
    /// </summary>
    public void CancelCalibration()
    {
        lock (_stateLock)
        {
            if (!IsCalibrating)
                return;
            
            _logger.LogInformation("Accelerometer calibration cancelled by user");
            
            _calibrationCts?.Cancel();
            
            TransitionState(AccelCalibrationState.Cancelled);
            
            RaiseCalibrationCompleted(AccelCalibrationResult.Cancelled, "Calibration cancelled by user");
        }
    }
    
    /// <summary>
    /// Get diagnostics for current/last calibration session.
    /// </summary>
    public AccelCalibrationDiagnostics GetDiagnostics()
    {
        lock (_stateLock)
        {
            return new AccelCalibrationDiagnostics
            {
                State = _state,
                CurrentPosition = _currentPosition,
                StartTime = _calibrationStartTime,
                Duration = DateTime.UtcNow - _calibrationStartTime,
                StatusTextLog = new List<string>(_statusTextLog),
                PositionAttempts = new Dictionary<int, PositionAttempt>(_positionAttempts)
            };
        }
    }
    
    #endregion
    
    #region Event Handlers
    
    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        if (!connected && IsCalibrating)
        {
            _logger.LogWarning("Connection lost during accelerometer calibration");
            
            lock (_stateLock)
            {
                _calibrationCts?.Cancel();
                TransitionState(AccelCalibrationState.Failed);
            }
            
            RaiseCalibrationCompleted(AccelCalibrationResult.Failed, "Connection lost during calibration");
        }
    }
    
    private void OnCommandAckReceived(object? sender, CommandAckEventArgs e)
    {
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
    
    private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
    {
        if (!IsCalibrating)
            return;
        
        // Log all STATUSTEXT during calibration for diagnostics
        lock (_stateLock)
        {
            _statusTextLog.Add($"[{DateTime.UtcNow:HH:mm:ss.fff}] [{e.Severity}] {e.Text}");
        }
        
        _logger.LogInformation("Accel cal FC: [{Severity}] {Text}", e.Severity, e.Text);
        
        // Parse STATUSTEXT for position request, completion, or failure
        var parseResult = _statusTextParser.Parse(e.Text);
        
        if (parseResult.IsPositionRequest && parseResult.RequestedPosition.HasValue)
        {
            HandlePositionRequest(parseResult.RequestedPosition.Value);
        }
        else if (parseResult.IsSuccess)
        {
            HandleCalibrationSuccess(e.Text);
        }
        else if (parseResult.IsFailure)
        {
            HandleCalibrationFailure(e.Text);
        }
        else if (parseResult.IsSampling)
        {
            HandleFCSampling();
        }
    }
    
    private void OnRawImuReceived(object? sender, RawImuEventArgs e)
    {
        // Convert from event args to RawImuData
        _latestImuData = new RawImuData
        {
            TimeUsec = e.TimeUsec,
            XAcc = (short)(e.AccelX / 0.00981), // Convert m/s² back to raw (approx)
            YAcc = (short)(e.AccelY / 0.00981),
            ZAcc = (short)(e.AccelZ / 0.00981),
            XGyro = (short)(e.GyroX / 0.001),
            YGyro = (short)(e.GyroY / 0.001),
            ZGyro = (short)(e.GyroZ / 0.001),
            Temperature = (short)(e.Temperature * 100),
            IsScaled = true
        };
        
        // Collect samples during validation phase
        if (_state == AccelCalibrationState.ValidatingPosition)
        {
            lock (_imuSamples)
            {
                _imuSamples.Add(_latestImuData);
            }
        }
    }
    
    #endregion
    
    #region State Machine Logic
    
    private void HandleCalibrationCommandAck(byte result)
    {
        var mavResult = (MavResult)result;
        
        lock (_stateLock)
        {
            if (_state != AccelCalibrationState.CommandSent)
                return;
            
            if (mavResult == MavResult.Accepted || mavResult == MavResult.InProgress)
            {
                _logger.LogInformation("FC accepted accelerometer calibration command");
                
                // Transition to waiting for first position request from FC
                TransitionState(AccelCalibrationState.WaitingForFirstPosition);
            }
            else
            {
                _logger.LogError("FC rejected accelerometer calibration: {Result}", mavResult);
                
                TransitionState(AccelCalibrationState.Rejected);
                
                string errorMessage = mavResult switch
                {
                    MavResult.TemporarilyRejected => "Calibration denied - vehicle may be armed or busy",
                    MavResult.Denied => "Calibration denied - check vehicle state",
                    MavResult.Unsupported => "Calibration not supported by this firmware",
                    MavResult.Failed => "Calibration command failed",
                    _ => $"Calibration rejected (code: {result})"
                };
                
                RaiseCalibrationCompleted(AccelCalibrationResult.Rejected, errorMessage);
            }
        }
    }
    
    private void HandlePositionRequest(int position)
    {
        lock (_stateLock)
        {
            _currentPosition = position;
            
            _logger.LogInformation("FC requesting position {Position}: {Name}", 
                position, GetPositionName(position));
            
            // Transition to waiting for user confirmation
            TransitionState(AccelCalibrationState.WaitingForUserConfirmation);
            
            // Raise event for UI
            RaisePositionRequested(position);
        }
    }
    
    private void HandlePositionCommandAck(byte result)
    {
        var mavResult = (MavResult)result;
        int position;
        
        lock (_stateLock)
        {
            if (_state != AccelCalibrationState.SendingPositionToFC)
                return;
            
            position = _currentPosition;
        }
        
        if (mavResult == MavResult.Accepted || mavResult == MavResult.InProgress)
        {
            _logger.LogInformation("FC acknowledged position {Position} - sampling in progress", position);
            
            TransitionState(AccelCalibrationState.FCSampling);
        }
        else
        {
            _logger.LogWarning("FC rejected position {Position}: {Result}", position, mavResult);
            
            // Return to waiting for user to reposition
            TransitionState(AccelCalibrationState.WaitingForUserConfirmation);
            
            RaisePositionValidated(position, false, $"FC rejected position: {mavResult}");
        }
    }
    
    private void HandleFCSampling()
    {
        lock (_stateLock)
        {
            if (_state == AccelCalibrationState.SendingPositionToFC ||
                _state == AccelCalibrationState.FCSampling)
            {
                TransitionState(AccelCalibrationState.FCSampling);
            }
        }
    }
    
    private void HandleCalibrationSuccess(string statusText)
    {
        lock (_stateLock)
        {
            _logger.LogInformation("FC reported accelerometer calibration SUCCESS: {Text}", statusText);
            
            _calibrationCts?.Cancel();
            
            TransitionState(AccelCalibrationState.Completed);
            
            RaiseCalibrationCompleted(AccelCalibrationResult.Success, statusText);
        }
    }
    
    private void HandleCalibrationFailure(string statusText)
    {
        lock (_stateLock)
        {
            _logger.LogError("FC reported accelerometer calibration FAILURE: {Text}", statusText);
            
            _calibrationCts?.Cancel();
            
            TransitionState(AccelCalibrationState.Failed);
            
            RaiseCalibrationCompleted(AccelCalibrationResult.Failed, statusText);
        }
    }
    
    private void TransitionState(AccelCalibrationState newState)
    {
        // Must be called inside lock
        var oldState = _state;
        _state = newState;
        
        _logger.LogDebug("Accel cal state: {Old} -> {New}", oldState, newState);
        
        RaiseStateChanged(oldState, newState);
    }
    
    #endregion
    
    #region IMU Sampling
    
    private async Task<bool> WaitForImuSamplesAsync(int timeoutMs, CancellationToken ct)
    {
        var startTime = DateTime.UtcNow;
        
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            if (ct.IsCancellationRequested)
                return false;
            
            lock (_imuSamples)
            {
                if (_imuSamples.Count >= IMU_SAMPLES_REQUIRED)
                    return true;
            }
            
            try
            {
                await Task.Delay(50, ct);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
        
        return false;
    }
    
    private RawImuData AverageImuSamples()
    {
        lock (_imuSamples)
        {
            if (_imuSamples.Count == 0)
                return _latestImuData ?? new RawImuData();
            
            return new RawImuData
            {
                XAcc = (short)_imuSamples.Average(s => s.XAcc),
                YAcc = (short)_imuSamples.Average(s => s.YAcc),
                ZAcc = (short)_imuSamples.Average(s => s.ZAcc),
                XGyro = (short)_imuSamples.Average(s => s.XGyro),
                YGyro = (short)_imuSamples.Average(s => s.YGyro),
                ZGyro = (short)_imuSamples.Average(s => s.ZGyro),
                Temperature = (short)_imuSamples.Average(s => s.Temperature),
                TimeUsec = _imuSamples.Last().TimeUsec,
                IsScaled = _imuSamples.First().IsScaled
            };
        }
    }
    
    #endregion
    
    #region Diagnostics
    
    private void RecordPositionAttempt(int position, bool success, string message)
    {
        lock (_stateLock)
        {
            if (!_positionAttempts.ContainsKey(position))
            {
                _positionAttempts[position] = new PositionAttempt
                {
                    Position = position,
                    PositionName = GetPositionName(position)
                };
            }
            
            var attempt = _positionAttempts[position];
            attempt.AttemptCount++;
            attempt.LastAttemptTime = DateTime.UtcNow;
            attempt.LastAttemptSuccess = success;
            attempt.LastAttemptMessage = message;
            
            if (success)
            {
                attempt.SuccessTime = DateTime.UtcNow;
            }
        }
    }
    
    private static string GetPositionName(int position)
    {
        return position >= 1 && position <= 6 ? PositionNames[position] : "UNKNOWN";
    }
    
    #endregion
    
    #region Event Raising
    
    private void RaiseStateChanged(AccelCalibrationState oldState, AccelCalibrationState newState)
    {
        StateChanged?.Invoke(this, new AccelCalibrationStateChangedEventArgs
        {
            OldState = oldState,
            NewState = newState
        });
    }
    
    private void RaisePositionRequested(int position)
    {
        PositionRequested?.Invoke(this, new AccelPositionRequestedEventArgs
        {
            Position = position,
            PositionName = GetPositionName(position)
        });
    }
    
    private void RaisePositionValidated(int position, bool success, string message)
    {
        PositionValidated?.Invoke(this, new AccelPositionValidationEventArgs
        {
            Position = position,
            PositionName = GetPositionName(position),
            IsValid = success,
            Message = message
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
        
        _connectionService.CommandAckReceived -= OnCommandAckReceived;
        _connectionService.StatusTextReceived -= OnStatusTextReceived;
        _connectionService.RawImuReceived -= OnRawImuReceived;
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
}

public class AccelPositionValidationEventArgs : EventArgs
{
    public int Position { get; set; }
    public string PositionName { get; set; } = "";
    public bool IsValid { get; set; }
    public string Message { get; set; } = "";
}

public class AccelCalibrationCompletedEventArgs : EventArgs
{
    public AccelCalibrationResult Result { get; set; }
    public string Message { get; set; } = "";
    public TimeSpan Duration { get; set; }
}

#endregion

#region Diagnostics Models

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
