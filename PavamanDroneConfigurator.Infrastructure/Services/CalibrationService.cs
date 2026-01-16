using Microsoft.Extensions.Logging;
using System;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.MAVLink;
using System.Text.RegularExpressions;

namespace PavamanDroneConfigurator.Infrastructure.Services;

public class CalibrationService : ICalibrationService
{
    private readonly ILogger<CalibrationService> _logger;
    private readonly IConnectionService _connectionService;
    private readonly CalibrationPreConditionChecker _preConditionChecker;
    private readonly CalibrationAbortMonitor _abortMonitor;
    private readonly AccelPositionValidator _positionValidator;
    private readonly AccelerometerCalibrationService _accelCalibrationService;
    
    // State
    private CalibrationStateModel _currentState = new();
    private CalibrationDiagnostics? _currentDiagnostics;
    private CalibrationStateMachine _stateMachine = CalibrationStateMachine.Idle;
    private CalibrationType _currentCalibrationType;
    private int _currentPositionNumber;
    private bool _isCalibrating;
    private readonly object _lock = new();
    
    // IMU data collection
    private RawImuData? _latestImuData;
    private readonly List<RawImuData> _imuSamples = new();
    private const int IMU_SAMPLES_REQUIRED = 50; // Collect 50 samples (~1 second at 50Hz)
    
    // Timeouts
    private const int COMMAND_ACK_TIMEOUT_MS = 5000;
    private const int POSITION_SAMPLE_TIMEOUT_MS = 30000;
    private const int CALIBRATION_TIMEOUT_MS = 300000; // 5 minutes max
    
    // Position constants
    private static readonly string[] AccelPositionNames = 
        { "LEVEL", "LEFT", "RIGHT", "NOSE DOWN", "NOSE UP", "BACK" };
    
    private static readonly string[] AccelPositionInstructions =
    {
        "Place vehicle LEVEL on a flat surface, then click 'Click When In Position'",
        "Place vehicle on its LEFT side, then click 'Click When In Position'",
        "Place vehicle on its RIGHT side, then click 'Click When In Position'",
        "Place vehicle with NOSE DOWN, then click 'Click When In Position'",
        "Place vehicle with NOSE UP, then click 'Click When In Position'",
        "Place vehicle on its BACK (upside down), then click 'Click When In Position'"
    };

    // ArduPilot STATUSTEXT keywords for pattern matching
    private static class StatusKeywords
    {
        // Completion - expanded list matching ArduPilot actual messages
        public static readonly string[] Complete = { 
            "calibration successful", "calibration complete", "calibration done", 
            "cal complete", "cal done", "calibration finished",
            "level calibration", "level complete", "ahrs", "trim",
            "accel offsets", "ins", "gyro", "gyros calibrated",
            "baro", "ground pressure", "pressure calibration",
            "compass", "mag offsets", "offsets saved"
        };
        
        // Failure
        public static readonly string[] Failed = { 
            "calibration failed", "calibration cancelled", "calibration timeout",
            "cal failed", "failed", "error", "timeout", "cancelled"
        };
        
        // Position requests (for accel only)
        public const string Place = "place";
        public const string Level = "level";
        public const string Left = "left";
        public const string Right = "right";
        public const string NoseDown = "nose down";
        public const string NoseUp = "nose up";
        public const string Back = "back";
        public const string Upside = "upside";
        
        // Sampling/progress
        public static readonly string[] Sampling = { "sampling", "reading", "detected", "hold still", "calibrating" };
        
        // Progress
        public const string Progress = "progress";
        public const string Percent = "%";
    }

    public CalibrationStateModel? CurrentState => _currentState;
    public bool IsCalibrating => _isCalibrating;
    public CalibrationStateMachine StateMachineState => _stateMachine;
    public CalibrationDiagnostics? CurrentDiagnostics => _currentDiagnostics;

    public event EventHandler<CalibrationStateModel>? CalibrationStateChanged;
    public event EventHandler<CalibrationProgressEventArgs>? CalibrationProgressChanged;
    public event EventHandler<CalibrationStepEventArgs>? CalibrationStepRequired;
    public event EventHandler<CalibrationStatusTextEventArgs>? StatusTextReceived;

    public CalibrationService(
        ILogger<CalibrationService> logger,
        IConnectionService connectionService,
        CalibrationPreConditionChecker preConditionChecker,
        CalibrationAbortMonitor abortMonitor,
        AccelPositionValidator positionValidator,
        AccelerometerCalibrationService accelCalibrationService)
    {
        _logger = logger;
        _connectionService = connectionService;
        _preConditionChecker = preConditionChecker;
        _abortMonitor = abortMonitor;
        _positionValidator = positionValidator;
        _accelCalibrationService = accelCalibrationService;
        
        _connectionService.StatusTextReceived += OnStatusTextReceived;
        _connectionService.CommandAckReceived += OnCommandAckReceived;
        _connectionService.RawImuReceived += OnRawImuReceived;
        _abortMonitor.AbortTriggered += OnAbortTriggered;
        
        // Wire up accelerometer calibration events
        _accelCalibrationService.StateChanged += OnAccelStateChanged;
        _accelCalibrationService.PositionRequested += OnAccelPositionRequested;
        _accelCalibrationService.PositionValidated += OnAccelPositionValidated;
        _accelCalibrationService.CalibrationCompleted += OnAccelCalibrationCompleted;
    }

    #region Event Handlers

    private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
    {
        // Always log STATUSTEXT during calibration
        if (_isCalibrating)
        {
            _currentDiagnostics?.AddStatusText(e.Severity, e.Text);
            
            StatusTextReceived?.Invoke(this, new CalibrationStatusTextEventArgs
            {
                Severity = e.Severity,
                Text = e.Text
            });
        }
        
        HandleStatusText(e.Severity, e.Text);
    }

    private void OnCommandAckReceived(object? sender, CommandAckEventArgs e)
    {
        if (!_isCalibrating)
            return;
        
        _currentDiagnostics?.AddCommandAck(e.Command, e.Result);
        _logger.LogInformation("COMMAND_ACK: cmd={Command} result={Result}", e.Command, e.Result);

        // MAV_CMD_PREFLIGHT_CALIBRATION = 241
        if (e.Command == 241)
        {
            HandleCalibrationCommandAck(e.Result);
        }
        // MAV_CMD_ACCELCAL_VEHICLE_POS = 42429
        else if (e.Command == 42429)
        {
            HandleAccelCalVehiclePosAck(e.Result);
        }
    }

    private void OnAbortTriggered(object? sender, EventArgs e)
    {
        if (_isCalibrating)
        {
            _logger.LogWarning("Calibration aborted by monitor");
            FinishCalibration(CalibrationResult.Cancelled, "Calibration aborted by monitor");
        }
    }

    private void OnRawImuReceived(object? sender, RawImuEventArgs imuData)
    {
        // Convert to internal format for position validation
        var rawImuData = new RawImuData
        {
            TimeUsec = imuData.TimeUsec,
            // Back-convert from m/s� to raw format (approximate)
            XAcc = (short)(imuData.AccelX / 0.00981), // Assuming SCALED_IMU format
            YAcc = (short)(imuData.AccelY / 0.00981),
            ZAcc = (short)(imuData.AccelZ / 0.00981),
            XGyro = (short)(imuData.GyroX / 0.001),
            YGyro = (short)(imuData.GyroY / 0.001),
            ZGyro = (short)(imuData.GyroZ / 0.001),
            Temperature = (short)(imuData.Temperature * 100),
            IsScaled = true
        };
        
        // Store latest IMU data for calibration validation
        _latestImuData = rawImuData;
        
        // During position validation, collect samples
        if (_isCalibrating && 
            _currentCalibrationType == CalibrationType.Accelerometer &&
            _stateMachine == CalibrationStateMachine.ValidatingPosition)
        {
            lock (_imuSamples)
            {
                _imuSamples.Add(rawImuData);
            }
        }
    }

    private void HandleCalibrationCommandAck(byte result)
    {
        var mavResult = (MavResult)result;
        
        if (mavResult == MavResult.Accepted || mavResult == MavResult.InProgress)
        {
            _logger.LogInformation("Calibration command accepted by FC (result={Result})", mavResult);
            _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Info, 
                $"FC accepted calibration command (result={mavResult})");
            
            TransitionState(CalibrationStateMachine.WaitingForInstruction);
            
            // For simple calibrations (gyro, baro, level), ACCEPTED means calibration started
            // These calibrations complete very quickly - we need to wait for STATUSTEXT
            // but also handle the case where FC completes without explicit success message
            if (_currentCalibrationType == CalibrationType.Gyroscope ||
                _currentCalibrationType == CalibrationType.Barometer ||
                _currentCalibrationType == CalibrationType.LevelHorizon)
            {
                // Update UI to show calibration is in progress
                UpdateState(CalibrationState.InProgress, 50, 
                    $"{GetCalibrationTypeName(_currentCalibrationType)} calibration in progress... Keep vehicle still.",
                    canConfirm: false);
                
                // Start a completion timer for simple calibrations
                // These typically complete in 1-3 seconds
                _ = WaitForSimpleCalibrationCompletion();
            }
            else if (_currentCalibrationType == CalibrationType.Accelerometer)
            {
                // Accelerometer calibration must wait for FC STATUSTEXT position requests
                UpdateState(CalibrationState.InProgress, 0,
                    "Waiting for flight controller position requests...", canConfirm: false);
            }
        }
        else
        {
            _logger.LogWarning("Calibration command rejected: {Result}", mavResult);
            
            string errorMessage = mavResult switch
            {
                MavResult.TemporarilyRejected => "Calibration temporarily denied. Vehicle may be armed or busy.",
                MavResult.Denied => "Calibration denied. Check vehicle state.",
                MavResult.Unsupported => "Calibration not supported by this firmware.",
                MavResult.Failed => "Calibration failed. Check vehicle position and sensor hardware.",
                _ => $"Calibration rejected by flight controller (code: {result})"
            };
            
            _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Error, errorMessage);
            _currentDiagnostics!.LastError = errorMessage;
            
            FinishCalibration(CalibrationResult.Rejected, errorMessage);
        }
    }

    /// <summary>
    /// For simple calibrations (gyro, baro, level), wait a short time then check if completed.
    /// If no failure message received, assume success.
    /// </summary>
    private async Task WaitForSimpleCalibrationCompletion()
    {
        var calibrationType = _currentCalibrationType;
        var startTime = DateTime.UtcNow;
        var maxWaitMs = 10000; // Max 10 seconds for simple calibrations
        var checkIntervalMs = 500;
        
        _logger.LogInformation("Waiting for {Type} calibration to complete...", calibrationType);
        
        while (_isCalibrating && 
               _currentCalibrationType == calibrationType &&
               (DateTime.UtcNow - startTime).TotalMilliseconds < maxWaitMs)
        {
            await Task.Delay(checkIntervalMs);
            
            // Update progress
            var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            var progress = Math.Min(95, (int)(elapsed / maxWaitMs * 100));
            
            if (_isCalibrating && _stateMachine != CalibrationStateMachine.Completed && 
                _stateMachine != CalibrationStateMachine.Failed)
            {
                UpdateState(CalibrationState.InProgress, progress,
                    $"{GetCalibrationTypeName(calibrationType)} calibration in progress...",
                    canConfirm: false);
            }
        }
        
        // If still calibrating and no failure detected, assume success
        // (FC may not send explicit success message for simple calibrations)
        if (_isCalibrating && 
            _currentCalibrationType == calibrationType &&
            _stateMachine != CalibrationStateMachine.Failed &&
            _stateMachine != CalibrationStateMachine.Completed)
        {
            _logger.LogInformation("{Type} calibration completed (no failure detected)", calibrationType);
            FinishCalibration(CalibrationResult.Success, 
                $"{GetCalibrationTypeName(calibrationType)} calibration completed successfully. Reboot recommended.");
        }
    }

    private static string GetCalibrationTypeName(CalibrationType type)
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

    private void HandleAccelCalVehiclePosAck(byte result)
    {
        var mavResult = (MavResult)result;
        
        if (mavResult == MavResult.Accepted || mavResult == MavResult.InProgress)
        {
            _logger.LogInformation("Position {Position} acknowledged by FC, waiting for sampling", _currentPositionNumber);
            _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Info,
                $"FC acknowledged position {_currentPositionNumber}");
            
            TransitionState(CalibrationStateMachine.Sampling);
            
            // Record position confirmation
            var posResult = _currentDiagnostics?.AccelPositionResults
                .FirstOrDefault(p => p.Position == _currentPositionNumber);
            if (posResult != null)
            {
                posResult.FcAcceptedTime = DateTime.UtcNow;
            }
        }
        else
        {
            _logger.LogWarning("Position {Position} rejected by FC: {Result}", _currentPositionNumber, mavResult);
            
            var posName = GetPositionName(_currentPositionNumber);
            var message = $"Position {_currentPositionNumber}/6: {posName} - FC rejected. Adjust position and try again.";
            
            _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Warning, message);
            
            // Record rejection
            var posResult = _currentDiagnostics?.AccelPositionResults
                .FirstOrDefault(p => p.Position == _currentPositionNumber);
            if (posResult != null)
            {
                posResult.Accepted = false;
                posResult.FcMessage = $"Rejected: {mavResult}";
            }
            
            TransitionState(CalibrationStateMachine.PositionRejected);
            UpdateState(CalibrationState.InProgress, _currentState.Progress, message, canConfirm: true);
        }
    }

    private void OnAccelStateChanged(object? sender, AccelCalibrationStateChangedEventArgs e)
    {
        // No-op: can be used for logging or future enhancements
    }

    private void OnAccelPositionRequested(object? sender, AccelPositionRequestedEventArgs e)
    {
        _logger.LogInformation("FC requested position {Position}: {Name}", e.Position, e.PositionName);
        
        lock (_lock) { _currentPositionNumber = e.Position; }
        
        var step = GetCalibrationStep(e.Position);
        var instruction = GetPositionInstruction(e.Position);
        
        RaiseCalibrationStepRequired(step, instruction);
    }

    private void OnAccelPositionValidated(object? sender, AccelPositionValidationEventArgs e)
    {
        if (!e.IsValid)
        {
            UpdateState(CalibrationState.InProgress, _currentState.Progress,
                $"? {e.Message}", canConfirm: true);
        }
    }

    private void OnAccelCalibrationCompleted(object? sender, AccelCalibrationCompletedEventArgs e)
    {
        _logger.LogInformation("Accelerometer calibration completed: {Result} - {Message} ({Duration})",
            e.Result, e.Message, e.Duration);
        
        var result = e.Result switch
        {
            AccelCalibrationResult.Success => CalibrationResult.Success,
            AccelCalibrationResult.Failed => CalibrationResult.Failed,
            AccelCalibrationResult.Cancelled => CalibrationResult.Cancelled,
            AccelCalibrationResult.Rejected => CalibrationResult.Rejected,
            _ => CalibrationResult.Failed
        };
        
        FinishCalibration(result, e.Message);
    }

    #endregion

    #region STATUSTEXT Handling

    private void HandleStatusText(byte severity, string text)
    {
        if (!_isCalibrating)
            return;

        _logger.LogDebug("Calibration STATUSTEXT [{Severity}]: {Text}", severity, text);
        var lowerText = text.ToLowerInvariant();

        // Check for completion - FIRMWARE IS THE SOURCE OF TRUTH
        if (IsCompletionMessage(lowerText))
        {
            HandleCalibrationComplete(text);
            return;
        }

        // Check for failure
        if (IsFailureMessage(lowerText))
        {
            HandleCalibrationFailed(text);
            return;
        }

        // Handle based on calibration type
        switch (_currentCalibrationType)
        {
            case CalibrationType.Accelerometer:
                HandleAccelStatusText(lowerText, text);
                break;
            case CalibrationType.Compass:
                HandleCompassStatusText(lowerText, text);
                break;
            case CalibrationType.Gyroscope:
            case CalibrationType.Barometer:
            case CalibrationType.LevelHorizon:
                HandleSimpleCalStatusText(lowerText, text);
                break;
        }
    }

    private bool IsCompletionMessage(string lowerText)
    {
        // Accelerometer requires explicit success confirmation
        if (_currentCalibrationType == CalibrationType.Accelerometer)
        {
            return lowerText.Contains("calibration") && lowerText.Contains("successful");
        }

        // Check for explicit completion keywords
        if (StatusKeywords.Complete.Any(kw => lowerText.Contains(kw)))
            return true;
        
        // For simple calibrations, check for type-specific success indicators
        switch (_currentCalibrationType)
        {
            case CalibrationType.LevelHorizon:
                // ArduPilot sends "Level Calibration" or "AHRS: " messages on completion
                if (lowerText.Contains("level") && (lowerText.Contains("complete") || lowerText.Contains("done") || lowerText.Contains("saved")))
                    return true;
                if (lowerText.Contains("ahrs") && lowerText.Contains("trim"))
                    return true;
                break;
                
            case CalibrationType.Gyroscope:
                // ArduPilot sends "Gyro" related messages
                if (lowerText.Contains("gyro") && (lowerText.Contains("complete") || lowerText.Contains("done") || lowerText.Contains("calibrated")))
                    return true;
                break;
                
            case CalibrationType.Barometer:
                // ArduPilot sends "Baro" or "pressure" related messages
                if ((lowerText.Contains("baro") || lowerText.Contains("pressure")) && 
                    (lowerText.Contains("complete") || lowerText.Contains("done") || lowerText.Contains("calibrated")))
                    return true;
                break;
                
            case CalibrationType.Compass:
                // For compass, need explicit completion or offsets saved
                if ((lowerText.Contains("compass") || lowerText.Contains("mag")) && 
                    (lowerText.Contains("complete") || lowerText.Contains("done") || lowerText.Contains("saved")))
                    return true;
                break;
        }
        
        return false;
    }

    private bool IsFailureMessage(string lowerText)
    {
        // "failed" but not "not failed" 
        if (StatusKeywords.Failed.Any(kw => lowerText.Contains(kw)))
        {
            return !lowerText.Contains("not failed") && !lowerText.Contains("didn't fail");
        }
        return false;
    }

    private void HandleAccelStatusText(string lowerText, string originalText)
    {
        // Check for position request from FC
        var requestedPosition = DetectRequestedPosition(lowerText);
        if (requestedPosition.HasValue)
        {
            lock (_lock)
            {
                _currentPositionNumber = requestedPosition.Value;
            }

            _logger.LogInformation("FC requesting position {Position}: {Name}", 
                _currentPositionNumber, GetPositionName(_currentPositionNumber));
            
            _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Info,
                $"FC requested position {_currentPositionNumber}: {GetPositionName(_currentPositionNumber)}");
            
            // Add position result entry
            _currentDiagnostics?.AccelPositionResults.Add(new AccelPositionResult
            {
                Position = _currentPositionNumber,
                PositionName = GetPositionName(_currentPositionNumber),
                Attempts = 1
            });

            TransitionState(CalibrationStateMachine.WaitingForUserPosition);
            
            int progress = (int)Math.Round(((_currentPositionNumber - 1) * 100.0) / 6.0);
            var instruction = GetPositionInstruction(_currentPositionNumber);
            var step = GetCalibrationStep(_currentPositionNumber);
            
            UpdateState(CalibrationState.InProgress, progress, 
                $"Position {_currentPositionNumber}/6: {GetPositionName(_currentPositionNumber)}", 
                canConfirm: true);
            
            RaiseCalibrationStepRequired(step, instruction);
            return;
        }

        // Check for sampling message (FC accepted and is reading)
        if (StatusKeywords.Sampling.Any(kw => lowerText.Contains(kw)))
        {
            _logger.LogInformation("FC is sampling position {Position}", _currentPositionNumber);
            
            TransitionState(CalibrationStateMachine.Sampling);
            
            int progress = (int)Math.Round((_currentPositionNumber * 100.0) / 6.0);
            UpdateState(CalibrationState.InProgress, progress,
                $"Position {_currentPositionNumber}/6: {GetPositionName(_currentPositionNumber)} - Sampling... Hold still!",
                canConfirm: false);
            
            // Update position result
            var posResult = _currentDiagnostics?.AccelPositionResults
                .FirstOrDefault(p => p.Position == _currentPositionNumber);
            if (posResult != null)
            {
                posResult.Accepted = true;
                posResult.FcMessage = originalText;
            }
            return;
        }

        // Update message but don't change state
        _currentState.Message = originalText;
        CalibrationStateChanged?.Invoke(this, _currentState);
    }

    private void HandleCompassStatusText(string lowerText, string originalText)
    {
        // Extract progress percentage
        if (lowerText.Contains(StatusKeywords.Progress) || lowerText.Contains(StatusKeywords.Percent))
        {
            var match = Regex.Match(originalText, @"(\d+)%");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
            {
                UpdateState(CalibrationState.InProgress, percent, originalText, canConfirm: false);
                
                // Update compass coverage
                if (_currentDiagnostics?.CompassCoverage != null)
                {
                    _currentDiagnostics.CompassCoverage.CompletionPercent = percent;
                }
                return;
            }
        }

        // Extract compass fitness if present
        var fitnessMatch = Regex.Match(originalText, @"fitness[:\s]+(\d+\.?\d*)", RegexOptions.IgnoreCase);
        if (fitnessMatch.Success && float.TryParse(fitnessMatch.Groups[1].Value, out var fitness))
        {
            if (_currentDiagnostics?.CompassCoverage != null)
            {
                _currentDiagnostics.CompassCoverage.Fitness = fitness;
            }
        }

        _currentState.Message = originalText;
        CalibrationStateChanged?.Invoke(this, _currentState);
    }

    private void HandleSimpleCalStatusText(string lowerText, string originalText)
    {
        // For gyro, baro, level - check for progress indicators and update message
        _logger.LogInformation("Simple cal STATUSTEXT: {Text}", originalText);
        
        // Check for "calibrating" type messages
        if (StatusKeywords.Sampling.Any(kw => lowerText.Contains(kw)))
        {
            UpdateState(CalibrationState.InProgress, 50,
                $"{GetCalibrationTypeName(_currentCalibrationType)}: {originalText}",
                canConfirm: false);
            return;
        }
        
        // Update message and state
        _currentState.Message = originalText;
        CalibrationStateChanged?.Invoke(this, _currentState);
    }

    private int? DetectRequestedPosition(string lowerText)
    {
        // Must contain "place" to be a position request
        if (!lowerText.Contains(StatusKeywords.Place))
            return null;

        // Check positions in order of specificity
        if (lowerText.Contains(StatusKeywords.Left) && !lowerText.Contains(StatusKeywords.Right))
            return 2;
        
        if (lowerText.Contains(StatusKeywords.Right) && !lowerText.Contains(StatusKeywords.Left))
            return 3;
        
        if (lowerText.Contains(StatusKeywords.NoseDown) || 
            (lowerText.Contains("nose") && lowerText.Contains("down")))
            return 4;
        
        if (lowerText.Contains(StatusKeywords.NoseUp) || 
            (lowerText.Contains("nose") && lowerText.Contains("up")))
            return 5;
        
        if (lowerText.Contains(StatusKeywords.Back) || lowerText.Contains(StatusKeywords.Upside))
            return 6;
        
        if (lowerText.Contains(StatusKeywords.Level))
            return 1;

        return null;
    }

    private void HandleCalibrationComplete(string text)
    {
        _logger.LogInformation("Calibration completed via FC STATUSTEXT: {Text}", text);
        
        _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Info,
            $"FC reported calibration complete: {text}");
        
        if (_currentCalibrationType == CalibrationType.Accelerometer)
        {
            lock (_lock) { _currentPositionNumber = Math.Clamp(_currentPositionNumber, 1, 6); }
        }
        
        FinishCalibration(CalibrationResult.Success, 
            _currentCalibrationType == CalibrationType.Accelerometer 
                ? "Accelerometer calibration complete! Reboot recommended." 
                : text);
    }

    private void HandleCalibrationFailed(string text)
    {
        _logger.LogWarning("Calibration failed via FC STATUSTEXT: {Text}", text);
        
        _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Error,
            $"FC reported calibration failed: {text}");
        _currentDiagnostics!.LastError = text;
        
        FinishCalibration(CalibrationResult.Failed, text);
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
            CalibrationType.Barometer => StartBarometerCalibrationAsync(),
            CalibrationType.LevelHorizon => StartLevelHorizonCalibrationAsync(),
            _ => Task.FromResult(false)
        };
    }

    public async Task<bool> StartAccelerometerCalibrationAsync(bool fullSixAxis = true)
    {
        if (!CanStartCalibration())
            return false;

        try
        {
            InitializeCalibration(CalibrationType.Accelerometer);
            
            _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Info,
                $"Starting accelerometer calibration (6-axis: {fullSixAxis})");

            UpdateState(CalibrationState.InProgress, 0, "Starting accelerometer calibration...", canConfirm: false);
            RaiseCalibrationStepRequired(CalibrationStep.Level, AccelPositionInstructions[0]);

            TransitionState(CalibrationStateMachine.WaitingForAck);
            
            // Send MAV_CMD_PREFLIGHT_CALIBRATION: param5 = 4 for 6-axis
            _connectionService.SendPreflightCalibration(
                gyro: 0, mag: 0, groundPressure: 0, airspeed: 0,
                accel: fullSixAxis ? 4 : 1);

            _logger.LogInformation("Sent MAV_CMD_PREFLIGHT_CALIBRATION (accel={Accel})", fullSixAxis ? 4 : 1);
            
            await Task.Delay(100);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting accelerometer calibration");
            FinishCalibration(CalibrationResult.Failed, $"Failed to start: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StartCompassCalibrationAsync(bool onboardCalibration = false)
    {
        if (!CanStartCalibration())
            return false;

        try
        {
            InitializeCalibration(CalibrationType.Compass);
            
            _currentDiagnostics!.CompassCoverage = new CompassCoverageInfo { CompassIndex = 1 };
            _currentDiagnostics.AddDiagnostic(CalibrationDiagnosticSeverity.Info,
                $"Starting compass calibration (onboard: {onboardCalibration})");

            UpdateState(CalibrationState.InProgress, 0, "Starting compass calibration...", canConfirm: false);
            RaiseCalibrationStepRequired(CalibrationStep.Rotate,
                "Rotate the vehicle slowly in all directions. Cover all orientations until calibration completes.");

            TransitionState(CalibrationStateMachine.WaitingForAck);
            
            // MAV_CMD_PREFLIGHT_CALIBRATION: param2 = 1 (mag) or 76 (onboard)
            _connectionService.SendPreflightCalibration(
                gyro: 0, mag: onboardCalibration ? 76 : 1, groundPressure: 0, airspeed: 0, accel: 0);

            _logger.LogInformation("Sent MAV_CMD_PREFLIGHT_CALIBRATION (mag={Mag})", onboardCalibration ? 76 : 1);
            
            await Task.Delay(100);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting compass calibration");
            FinishCalibration(CalibrationResult.Failed, $"Failed to start: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StartGyroscopeCalibrationAsync()
    {
        if (!CanStartCalibration())
            return false;

        try
        {
            InitializeCalibration(CalibrationType.Gyroscope);
            
            _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Info,
                "Starting gyroscope calibration");

            UpdateState(CalibrationState.InProgress, 0, "Starting gyroscope calibration...", canConfirm: false);
            RaiseCalibrationStepRequired(CalibrationStep.KeepStill,
                "Keep the vehicle completely still. Do not move or touch it.");

            TransitionState(CalibrationStateMachine.WaitingForAck);
            
            _connectionService.SendPreflightCalibration(
                gyro: 1, mag: 0, groundPressure: 0, airspeed: 0, accel: 0);

            _logger.LogInformation("Sent MAV_CMD_PREFLIGHT_CALIBRATION (gyro=1)");
            
            await Task.Delay(100);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting gyroscope calibration");
            FinishCalibration(CalibrationResult.Failed, $"Failed to start: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StartLevelHorizonCalibrationAsync()
    {
        if (!CanStartCalibration())
            return false;

        try
        {
            InitializeCalibration(CalibrationType.LevelHorizon);
            
            _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Info,
                "Starting level horizon calibration");

            UpdateState(CalibrationState.InProgress, 0, "Starting level horizon calibration...", canConfirm: false);
            RaiseCalibrationStepRequired(CalibrationStep.Level,
                "Place vehicle on a perfectly level surface. Keep it completely still.");

            TransitionState(CalibrationStateMachine.WaitingForAck);
            
            // param5 = 2 for level/trim calibration
            _connectionService.SendPreflightCalibration(
                gyro: 0, mag: 0, groundPressure: 0, airspeed: 0, accel: 2);

            _logger.LogInformation("Sent MAV_CMD_PREFLIGHT_CALIBRATION (accel=2 for level)");
            
            await Task.Delay(100);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting level horizon calibration");
            FinishCalibration(CalibrationResult.Failed, $"Failed to start: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StartBarometerCalibrationAsync()
    {
        if (!CanStartCalibration())
            return false;

        try
        {
            InitializeCalibration(CalibrationType.Barometer);
            
            _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Info,
                "Starting barometer calibration");

            UpdateState(CalibrationState.InProgress, 0, "Starting barometer calibration...", canConfirm: false);

            TransitionState(CalibrationStateMachine.WaitingForAck);
            
            _connectionService.SendPreflightCalibration(
                gyro: 0, mag: 0, groundPressure: 1, airspeed: 0, accel: 0);

            _logger.LogInformation("Sent MAV_CMD_PREFLIGHT_CALIBRATION (groundPressure=1)");
            
            await Task.Delay(100);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting barometer calibration");
            FinishCalibration(CalibrationResult.Failed, $"Failed to start: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StartAirspeedCalibrationAsync()
    {
        if (!CanStartCalibration())
            return false;

        try
        {
            InitializeCalibration(CalibrationType.Airspeed);
            
            _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Info,
                "Starting airspeed calibration");

            UpdateState(CalibrationState.InProgress, 0, "Starting airspeed calibration...", canConfirm: false);

            TransitionState(CalibrationStateMachine.WaitingForAck);
            
            _connectionService.SendPreflightCalibration(
                gyro: 0, mag: 0, groundPressure: 0, airspeed: 1, accel: 0);

            _logger.LogInformation("Sent MAV_CMD_PREFLIGHT_CALIBRATION (airspeed=1)");
            
            await Task.Delay(100);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting airspeed calibration");
            FinishCalibration(CalibrationResult.Failed, $"Failed to start: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// User confirms vehicle is in position.
    /// VALIDATES position using actual IMU readings before sending to FC.
    /// This is CRITICAL for flight safety - prevents bad calibration data.
    /// </summary>
    public async Task<bool> AcceptCalibrationStepAsync()
    {
        if (!_isCalibrating)
        {
            _logger.LogWarning("AcceptCalibrationStepAsync called but no calibration in progress");
            return false;
        }

        if (_stateMachine != CalibrationStateMachine.WaitingForUserPosition &&
            _stateMachine != CalibrationStateMachine.PositionRejected)
        {
            _logger.LogWarning("AcceptCalibrationStepAsync called in invalid state: {State}", _stateMachine);
            return false;
        }

        if (_currentCalibrationType == CalibrationType.Accelerometer)
        {
            _logger.LogInformation("User confirmed position {Position}, validating with IMU data...", 
                _currentPositionNumber);
            
            _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Info,
                $"User confirmed position {_currentPositionNumber}: {GetPositionName(_currentPositionNumber)} - Validating...");
            
            // Record user confirmation time
            var posResult = _currentDiagnostics?.AccelPositionResults
                .FirstOrDefault(p => p.Position == _currentPositionNumber);
            if (posResult != null)
            {
                posResult.UserConfirmedTime = DateTime.UtcNow;
                posResult.Attempts++;
            }

            // Transition to validating state
            TransitionState(CalibrationStateMachine.ValidatingPosition);
            
            var posName = GetPositionName(_currentPositionNumber);
            UpdateState(CalibrationState.InProgress, _currentState.Progress,
                $"Position {_currentPositionNumber}/6: {posName} - Reading sensors...",
                canConfirm: false);
            
            // Collect IMU samples for validation
            lock (_imuSamples)
            {
                _imuSamples.Clear();
            }
            
            // Wait for samples (up to 3 seconds)
            var samplesCollected = await WaitForImuSamples(3000);
            
            if (!samplesCollected)
            {
                _logger.LogWarning("Failed to collect enough IMU samples for position {Position}", _currentPositionNumber);
                
                _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Warning,
                    $"Position {_currentPositionNumber}: Not enough IMU data - check sensor connection");
                
                TransitionState(CalibrationStateMachine.PositionRejected);
                UpdateState(CalibrationState.InProgress, _currentState.Progress,
                    $"Position {_currentPositionNumber}/6: {posName} - Could not read IMU data. Try again.",
                    canConfirm: true);
                
                return false;
            }
            
            // Average the IMU samples
            var avgImuData = AverageImuSamples(_imuSamples);
            
            // Validate position using actual sensor data
            var accel = avgImuData.GetAcceleration();
            var imuData = new ImuData
            {
                AccelX = accel.X,
                AccelY = accel.Y,
                AccelZ = accel.Z,
                TimeBootMs = (uint)(avgImuData.TimeUsec / 1000)
            };
            
            var validation = _positionValidator.ValidatePosition(_currentPositionNumber, imuData);
            
            // Log validation result
            _currentDiagnostics?.AddDiagnostic(
                validation.IsValid ? CalibrationDiagnosticSeverity.Info : CalibrationDiagnosticSeverity.Warning,
                $"Position {_currentPositionNumber} validation: {validation.ErrorMessage}");
            
            if (posResult != null)
            {
                posResult.Accepted = validation.IsValid;
                posResult.FcMessage = validation.ErrorMessage;
                posResult.ActualAccelX = validation.ActualAccelX;
                posResult.ActualAccelY = validation.ActualAccelY;
                posResult.ActualAccelZ = validation.ActualAccelZ;
            }
            
            if (!validation.IsValid)
            {
                // Position validation FAILED - reject it
                _logger.LogWarning(
                    "Position {Position} validation FAILED: {Message}",
                    _currentPositionNumber, validation.ErrorMessage);
                
                TransitionState(CalibrationStateMachine.PositionRejected);
                UpdateState(CalibrationState.InProgress, _currentState.Progress,
                    $"? {validation.ErrorMessage}",
                    canConfirm: true);
                
                return false;
            }
            
            // Position validation PASSED - send to FC
            _logger.LogInformation(
                "Position {Position} validation PASSED - sending to FC",
                _currentPositionNumber);
            
            TransitionState(CalibrationStateMachine.WaitingForSampling);
            UpdateState(CalibrationState.InProgress, _currentState.Progress,
                $"Position {_currentPositionNumber}/6: {posName} - ? Position verified! Sending to FC...",
                canConfirm: false);
            
            // Send MAV_CMD_ACCELCAL_VEHICLE_POS - FC will do final sampling
            _connectionService.SendAccelCalVehiclePos(_currentPositionNumber);
            
            _logger.LogInformation("Sent MAV_CMD_ACCELCAL_VEHICLE_POS({Position})", _currentPositionNumber);
        }

        return true;
    }
    
    private async Task<bool> WaitForImuSamples(int timeoutMs)
    {
        var startTime = DateTime.UtcNow;
        
        while ((DateTime.UtcNow - startTime).TotalMilliseconds < timeoutMs)
        {
            lock (_imuSamples)
            {
                if (_imuSamples.Count >= IMU_SAMPLES_REQUIRED)
                    return true;
            }
            
            await Task.Delay(50);
        }
        
        return false;
    }
    
    private RawImuData AverageImuSamples(List<RawImuData> samples)
    {
        if (samples.Count == 0)
            return _latestImuData ?? new RawImuData();
        
        var avgXAcc = samples.Average(s => s.XAcc);
        var avgYAcc = samples.Average(s => s.YAcc);
        var avgZAcc = samples.Average(s => s.ZAcc);
        var avgXGyro = samples.Average(s => s.XGyro);
        var avgYGyro = samples.Average(s => s.YGyro);
        var avgZGyro = samples.Average(s => s.ZGyro);
        var avgTemp = samples.Average(s => s.Temperature);
        
        return new RawImuData
        {
            XAcc = (short)avgXAcc,
            YAcc = (short)avgYAcc,
            ZAcc = (short)avgZAcc,
            XGyro = (short)avgXGyro,
            YGyro = (short)avgYGyro,
            ZGyro = (short)avgZGyro,
            Temperature = (short)avgTemp,
            TimeUsec = samples.Last().TimeUsec,
            IsScaled = samples.First().IsScaled
        };
    }

    #endregion

    #region Calibration Management

    public Task<bool> CancelCalibrationAsync()
    {
        if (!_isCalibrating)
            return Task.FromResult(true);
        
        _logger.LogInformation("Calibration cancelled by user");
        _currentDiagnostics?.AddDiagnostic(CalibrationDiagnosticSeverity.Info, "Calibration cancelled by user");
        
        FinishCalibration(CalibrationResult.Cancelled, "Calibration cancelled by user");
        
        return Task.FromResult(true);
    }

    public Task<bool> RebootFlightControllerAsync()
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("Cannot reboot - not connected");
            return Task.FromResult(false);
        }

        try
        {
            _logger.LogInformation("Sending reboot command");
            _connectionService.SendPreflightReboot(autopilot: 1, companion: 0);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending reboot command");
            return Task.FromResult(false);
        }
    }

    #endregion

    #region State Management

    private bool CanStartCalibration()
    {
        // Use relaxed pre-condition checker (Mission Planner behavior)
        // Only requires: connected + heartbeat stable + disarmed
        // Does NOT require: EKF ready, GPS lock, full params, SYS_STATUS health
        
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("Cannot start calibration - not connected");
            return false;
        }

        if (_isCalibrating)
        {
            _logger.LogWarning("Calibration already in progress");
            return false;
        }

        // Quick check via pre-condition checker
        if (!_preConditionChecker.CanStartCalibration())
        {
            _logger.LogWarning("Pre-conditions not met for calibration");
            return false;
        }

        return true;
    }

    private void InitializeCalibration(CalibrationType type)
    {
        lock (_lock)
        {
            _isCalibrating = true;
            _currentCalibrationType = type;
            _currentPositionNumber = 1;
            _stateMachine = CalibrationStateMachine.Idle;
        }

        _currentDiagnostics = new CalibrationDiagnostics
        {
            CalibrationType = type,
            StartTime = DateTime.UtcNow
        };

        _currentState = new CalibrationStateModel
        {
            Type = type,
            State = CalibrationState.InProgress,
            StateMachine = _stateMachine,
            Diagnostics = _currentDiagnostics
        };

        // Start abort monitoring
        _abortMonitor.StartMonitoring(type);

        _logger.LogInformation("Initialized {Type} calibration session {SessionId}", 
            type, _currentDiagnostics.SessionId);
    }

    private void TransitionState(CalibrationStateMachine newState)
    {
        var oldState = _stateMachine;
        _stateMachine = newState;
        _currentState.StateMachine = newState;
        _currentDiagnostics!.CurrentState = newState;
        
        _logger.LogDebug("State transition: {Old} -> {New}", oldState, newState);
    }

    private void UpdateState(CalibrationState state, int progress, string message, bool canConfirm)
    {
        _currentState.State = state;
        _currentState.Progress = progress;
        _currentState.Message = message;
        var clampedPosition = _currentCalibrationType == CalibrationType.Accelerometer
            ? Math.Clamp(_currentPositionNumber, 0, 6)
            : _currentPositionNumber;
        _currentState.CurrentPosition = clampedPosition;
        _currentState.CanConfirmPosition = canConfirm;

        CalibrationStateChanged?.Invoke(this, _currentState);

        CalibrationProgressChanged?.Invoke(this, new CalibrationProgressEventArgs
        {
            Type = _currentCalibrationType,
            ProgressPercent = progress,
            StatusText = message,
            CurrentStep = _currentPositionNumber,
            TotalSteps = _currentCalibrationType == CalibrationType.Accelerometer ? 6 : 1,
            StateMachine = _stateMachine
        });
    }

    private void FinishCalibration(CalibrationResult result, string message)
    {
        // Stop abort monitoring
        _abortMonitor.StopMonitoring();

        var finalState = result switch
        {
            CalibrationResult.Success => CalibrationStateMachine.Completed,
            CalibrationResult.Failed => CalibrationStateMachine.Failed,
            CalibrationResult.Cancelled => CalibrationStateMachine.Cancelled,
            CalibrationResult.TimedOut => CalibrationStateMachine.TimedOut,
            CalibrationResult.Rejected => CalibrationStateMachine.Rejected,
            _ => CalibrationStateMachine.Failed
        };

        TransitionState(finalState);

        _currentDiagnostics!.EndTime = DateTime.UtcNow;
        _currentDiagnostics.Result = result;
        
        var calState = result == CalibrationResult.Success 
            ? CalibrationState.Completed 
            : CalibrationState.Failed;
        
        var progress = result == CalibrationResult.Success ? 100 : 0;
        
        UpdateState(calState, progress, message, canConfirm: false);
        
        lock (_lock)
        {
            _isCalibrating = false;
        }

        _logger.LogInformation("Calibration finished: {Result} - {Message} (Duration: {Duration})", 
            result, message, _currentDiagnostics.Duration);
    }

    private void RaiseCalibrationStepRequired(CalibrationStep step, string instructions)
    {
        CalibrationStepRequired?.Invoke(this, new CalibrationStepEventArgs
        {
            Type = _currentCalibrationType,
            Step = step,
            Instructions = instructions,
            CanConfirm = _stateMachine == CalibrationStateMachine.WaitingForUserPosition ||
                         _stateMachine == CalibrationStateMachine.PositionRejected
        });
    }

    #endregion

    #region Helpers

    private static string GetPositionName(int position)
    {
        return position >= 1 && position <= 6 
            ? AccelPositionNames[position - 1] 
            : "UNKNOWN";
    }

    private static string GetPositionInstruction(int position)
    {
        return position >= 1 && position <= 6 
            ? AccelPositionInstructions[position - 1] 
            : "Follow FC instructions";
    }

    private static CalibrationStep GetCalibrationStep(int position)
    {
        return position switch
        {
            1 => CalibrationStep.Level,
            2 => CalibrationStep.LeftSide,
            3 => CalibrationStep.RightSide,
            4 => CalibrationStep.NoseDown,
            5 => CalibrationStep.NoseUp,
            6 => CalibrationStep.Back,
            _ => CalibrationStep.Level
        };
    }

    #endregion
}
