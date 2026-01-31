using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the Calibration Page implementing MissionPlanner-style IMU calibration.
/// 
/// MISSION PLANNER IMU CALIBRATION FLOW:
/// 1. First click: Send MAV_CMD_PREFLIGHT_CALIBRATION (param5=1) to start 6-axis accel cal
/// 2. FC sends STATUSTEXT with position instructions ("Place vehicle level and press any key")
/// 3. FC may also send COMMAND_LONG (MAV_CMD_ACCELCAL_VEHICLE_POS) with position enum
/// 4. User places vehicle, clicks button again
/// 5. Click sends MAV_CMD_ACCELCAL_VEHICLE_POS with current position (1-6)
/// 6. FC validates, samples, sends next position request
/// 7. Repeat until "Calibration successful" STATUSTEXT received
/// 
/// ACCELCAL_VEHICLE_POS enum values (from MAVLink):
///   1 = LEVEL     - Vehicle level on flat surface
///   2 = LEFT      - Vehicle on left side
///   3 = RIGHT     - Vehicle on right side
///   4 = NOSEDOWN  - Vehicle nose pointing down
///   5 = NOSEUP    - Vehicle nose pointing up
///   6 = BACK      - Vehicle upside down (on its back)
///   16777215 = SUCCESS - Calibration completed successfully
///   16777216 = FAILED  - Calibration failed
/// </summary>
public partial class CalibrationPageViewModel : ViewModelBase
{
    private readonly ICalibrationService _calibrationService;
    private readonly IConnectionService _connectionService;
    private readonly ILogger<CalibrationPageViewModel>? _logger;

    // MissionPlanner-style calibration state (_incalibrate equivalent)
    private bool _inCalibrate = false;
    private int _currentPositionIndex = 0; // 0 = not started, 1-6 = position being calibrated
    private readonly object _calibrationLock = new();

    [ObservableProperty]
    private CalibrationStateModel? _currentState;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isCalibrating;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private int _calibrationProgress;

    [ObservableProperty]
    private string _calibrationInstructions = string.Empty;

    [ObservableProperty]
    private CalibrationStep _currentStep;

    [ObservableProperty]
    private int _currentStepNumber;

    [ObservableProperty]
    private int _totalSteps;

    [ObservableProperty]
    private bool _requiresUserAction;

    /// <summary>
    /// Dynamic button text for IMU calibration (MissionPlanner style)
    /// Shows "Calibrate Accel" when not calibrating, "Click When Done" during calibration
    /// </summary>
    [ObservableProperty]
    private string _imuCalibrationButtonText = "Calibrate Accel";

    /// <summary>
    /// Current position name for display (e.g., "LEVEL", "LEFT", "RIGHT")
    /// </summary>
    [ObservableProperty]
    private string _currentPositionName = string.Empty;

    /// <summary>
    /// Raw FC message for display (STATUSTEXT content)
    /// </summary>
    [ObservableProperty]
    private string _fcMessage = string.Empty;

    /// <summary>
    /// Indicates if IMU calibration button should be enabled
    /// </summary>
    [ObservableProperty]
    private bool _isImuButtonEnabled = true;

    public CalibrationPageViewModel(
        ICalibrationService calibrationService, 
        IConnectionService connectionService,
        ILogger<CalibrationPageViewModel>? logger = null)
    {
        _calibrationService = calibrationService;
        _connectionService = connectionService;
        _logger = logger;

        LogInfo("CalibrationPageViewModel initialized");

        // Subscribe to connection events
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        IsConnected = _connectionService.IsConnected;

        // Subscribe to calibration events (existing)
        _calibrationService.CalibrationStateChanged += OnCalibrationStateChanged;
        _calibrationService.CalibrationProgressChanged += OnCalibrationProgressChanged;
        _calibrationService.CalibrationStepRequired += OnCalibrationStepRequired;
        
        // Subscribe to MAVLink events for MissionPlanner-style IMU calibration
        _connectionService.StatusTextReceived += OnStatusTextReceived;
        _connectionService.CommandLongReceived += OnCommandLongReceived;
        _connectionService.CommandAckReceived += OnCommandAckReceived;
    }

    #region Logging Helpers

    private void LogInfo(string message) => _logger?.LogInformation("[CalibVM] {Message}", message);
    private void LogDebug(string message) => _logger?.LogDebug("[CalibVM] {Message}", message);
    private void LogWarning(string message) => _logger?.LogWarning("[CalibVM] {Message}", message);
    private void LogError(string message) => _logger?.LogError("[CalibVM] {Message}", message);

    #endregion

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        LogInfo($"Connection state changed: {connected}");
        IsConnected = connected;
        if (!connected && IsCalibrating)
        {
            LogWarning("Connection lost during calibration!");
            StatusMessage = "Connection lost during calibration";
            IsCalibrating = false;
            ResetImuCalibrationState();
        }
    }

    private void OnCalibrationStateChanged(object? sender, CalibrationStateModel state)
    {
        LogDebug($"Calibration state changed: Type={state.Type}, State={state.State}, Progress={state.Progress}");
        CurrentState = state;
        StatusMessage = state.Message ?? "Ready";
        IsCalibrating = state.State == CalibrationState.InProgress;
        CalibrationProgress = state.Progress;

        if (state.State == CalibrationState.Completed)
        {
            LogInfo("Calibration completed successfully!");
            RequiresUserAction = false;
            CalibrationInstructions = "Calibration completed successfully!";
            ResetImuCalibrationState();
        }
        else if (state.State == CalibrationState.Failed)
        {
            LogError($"Calibration failed: {state.Message}");
            RequiresUserAction = false;
            CalibrationInstructions = state.Message ?? "Calibration failed";
            ResetImuCalibrationState();
        }
    }

    private void OnCalibrationProgressChanged(object? sender, CalibrationProgressEventArgs e)
    {
        LogDebug($"Progress: {e.ProgressPercent}% Step={e.CurrentStep}/{e.TotalSteps}");
        CalibrationProgress = e.ProgressPercent;
        StatusMessage = e.StatusText ?? StatusMessage;
        CurrentStepNumber = e.CurrentStep ?? 0;
        TotalSteps = e.TotalSteps ?? 1;
    }

    private void OnCalibrationStepRequired(object? sender, CalibrationStepEventArgs e)
    {
        LogInfo($"Step required: {e.Step} - {e.Instructions}");
        CurrentStep = e.Step;
        CalibrationInstructions = e.Instructions ?? GetStepInstructions(e.Step);
        RequiresUserAction = true;
    }

    #region MissionPlanner-Style MAVLink Event Handlers

    /// <summary>
    /// Handle STATUSTEXT messages from FC during IMU calibration.
    /// THIS IS THE PRIMARY COMMUNICATION CHANNEL.
    /// FC sends position requests, progress updates, and completion via STATUSTEXT.
    /// </summary>
    private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
    {
        lock (_calibrationLock)
        {
            if (!_inCalibrate)
                return;
        }

        var text = e.Text;
        var lower = text.ToLowerInvariant();

        LogInfo($"[STATUSTEXT] Severity={e.Severity} Text=\"{text}\"");

        // Store raw FC message for display
        FcMessage = text;
        StatusMessage = text;

        // Check for calibration completion
        if (IsSuccessMessage(lower))
        {
            LogInfo("*** CALIBRATION SUCCESS detected in STATUSTEXT ***");
            CalibrationInstructions = "Calibration completed successfully!";
            FinishImuCalibration(true, text);
            return;
        }

        // Check for calibration failure
        if (IsFailureMessage(lower))
        {
            LogError($"*** CALIBRATION FAILURE detected in STATUSTEXT: {text} ***");
            CalibrationInstructions = text;
            FinishImuCalibration(false, text);
            return;
        }

        // Check for position request
        var requestedPosition = DetectPositionRequest(lower);
        if (requestedPosition.HasValue)
        {
            LogInfo($"Position request detected: {requestedPosition.Value} ({GetPositionName(requestedPosition.Value)})");
            HandlePositionRequest(requestedPosition.Value, text);
            return;
        }

        // Update instructions with any other FC message
        CalibrationInstructions = text;
    }

    /// <summary>
    /// Handle COMMAND_LONG messages from FC.
    /// FC may send MAV_CMD_ACCELCAL_VEHICLE_POS to request positions or signal completion.
    /// This is more reliable than parsing STATUSTEXT.
    /// 
    /// Position values:
    ///   1-6: Position request
    ///   16777215: Success
    ///   16777216: Failed
    /// </summary>
    private void OnCommandLongReceived(object? sender, CommandLongEventArgs e)
    {
        lock (_calibrationLock)
        {
            if (!_inCalibrate)
                return;
        }

        // MAV_CMD_ACCELCAL_VEHICLE_POS = 42429
        int position = (int)e.Param1;

        LogInfo($"[COMMAND_LONG] MAV_CMD_ACCELCAL_VEHICLE_POS received: param1={position}");

        // Check for special completion values
        // ACCELCAL_VEHICLE_POS_SUCCESS = 16777215 (0xFFFFFF)
        if (position == 16777215)
        {
            LogInfo("*** CALIBRATION SUCCESS received via COMMAND_LONG (position=16777215) ***");
            FinishImuCalibration(true, "Calibration successful");
            return;
        }
        
        // ACCELCAL_VEHICLE_POS_FAILED = 16777216 (0x1000000)
        if (position == 16777216)
        {
            LogError("*** CALIBRATION FAILED received via COMMAND_LONG (position=16777216) ***");
            FinishImuCalibration(false, "Calibration failed");
            return;
        }

        // Normal position request (1-6)
        if (position >= 1 && position <= 6)
        {
            string posName = GetPositionName(position);
            string message = $"Please place vehicle {posName}";
            LogInfo($"Position request: {position} ({posName})");
            HandlePositionRequest(position, message);
        }
        else
        {
            LogWarning($"Unknown position value received: {position}");
        }
    }

    /// <summary>
    /// Handle COMMAND_ACK responses from FC.
    /// </summary>
    private void OnCommandAckReceived(object? sender, CommandAckEventArgs e)
    {
        lock (_calibrationLock)
        {
            if (!_inCalibrate)
                return;
        }

        LogInfo($"[COMMAND_ACK] Command={e.Command} Result={e.Result} (Success={e.IsSuccess})");

        // MAV_CMD_PREFLIGHT_CALIBRATION = 241
        if (e.Command == 241)
        {
            if (e.IsSuccess)
            {
                LogInfo("FC accepted MAV_CMD_PREFLIGHT_CALIBRATION - waiting for position request...");
                StatusMessage = "Calibration started, waiting for FC instructions...";
            }
            else
            {
                string errorMsg = e.Result switch
                {
                    1 => "Calibration temporarily rejected - try again",
                    2 => "Calibration denied - ensure vehicle is disarmed",
                    3 => "Calibration not supported",
                    4 => "Calibration failed to start",
                    _ => $"Calibration rejected (code: {e.Result})"
                };
                LogError($"FC rejected MAV_CMD_PREFLIGHT_CALIBRATION: {errorMsg}");
                FinishImuCalibration(false, errorMsg);
            }
        }
        // MAV_CMD_ACCELCAL_VEHICLE_POS = 42429
        else if (e.Command == 42429)
        {
            if (e.IsSuccess)
            {
                LogInfo($"FC accepted position {_currentPositionIndex} ({GetPositionName(_currentPositionIndex)}) - sampling...");
                StatusMessage = $"Position {GetPositionName(_currentPositionIndex)} accepted, sampling...";
            }
            else
            {
                LogWarning($"FC rejected position {_currentPositionIndex}: result={e.Result}");
                StatusMessage = "Position not accepted, check orientation and try again";
            }
        }
    }

    #endregion

    #region MissionPlanner-Style IMU Calibration

    /// <summary>
    /// Main IMU calibration button handler - MissionPlanner style.
    /// 
    /// First click: Start calibration (send MAV_CMD_PREFLIGHT_CALIBRATION param5=1)
    /// Subsequent clicks: Confirm position (send MAV_CMD_ACCELCAL_VEHICLE_POS)
    /// </summary>
    [RelayCommand]
    private void CalibrateImu()
    {
        if (!IsConnected)
        {
            LogWarning("CalibrateImu: Not connected to vehicle");
            StatusMessage = "Not connected to vehicle";
            return;
        }

        bool wasCalibrating;
        lock (_calibrationLock)
        {
            wasCalibrating = _inCalibrate;
        }

        if (wasCalibrating)
        {
            LogInfo("CalibrateImu: Already calibrating - confirming current position");
            ConfirmCurrentPosition();
        }
        else
        {
            LogInfo("CalibrateImu: Starting new calibration");
            StartImuCalibration();
        }
    }

    /// <summary>
    /// Start 6-axis accelerometer calibration.
    /// Sends MAV_CMD_PREFLIGHT_CALIBRATION (241) with param5=1.
    /// </summary>
    private void StartImuCalibration()
    {
        lock (_calibrationLock)
        {
            if (_inCalibrate)
            {
                LogWarning("StartImuCalibration: Already in calibration mode");
                return;
            }

            _inCalibrate = true;
            _currentPositionIndex = 0;
        }

        LogInfo("=== IMU CALIBRATION STARTED ===");
        LogInfo("Sending MAV_CMD_PREFLIGHT_CALIBRATION (241) with param5=1 (6-axis accel)");

        // Update UI state
        IsCalibrating = true;
        ImuCalibrationButtonText = "Click When Done";
        CalibrationInstructions = "Starting accelerometer calibration...";
        CurrentPositionName = string.Empty;
        FcMessage = string.Empty;
        RequiresUserAction = false;

        // Send MAV_CMD_PREFLIGHT_CALIBRATION (241) with param5=1 for 6-axis accel
        _connectionService.SendPreflightCalibration(
            gyro: 0,
            mag: 0,
            groundPressure: 0,
            airspeed: 0,
            accel: 1);  // param5=1 for 6-position accelerometer calibration

        StatusMessage = "Calibration command sent, waiting for FC response...";
    }

    /// <summary>
    /// Confirm vehicle is in the requested position.
    /// Sends MAV_CMD_ACCELCAL_VEHICLE_POS (42429) with current position (1-6).
    /// </summary>
    private void ConfirmCurrentPosition()
    {
        int position;
        lock (_calibrationLock)
        {
            position = _currentPositionIndex;
        }

        // Validate position (must be 1-6)
        if (position < 1 || position > 6)
        {
            LogWarning($"ConfirmCurrentPosition: Invalid position {position} (expected 1-6)");
            StatusMessage = "Waiting for FC to request position...";
            return;
        }

        string posName = GetPositionName(position);
        LogInfo($"Confirming position {position} ({posName})");
        LogInfo($"Sending MAV_CMD_ACCELCAL_VEHICLE_POS (42429) with param1={position}");

        // Send MAV_CMD_ACCELCAL_VEHICLE_POS (42429) with current position
        _connectionService.SendAccelCalVehiclePos(position);

        StatusMessage = $"Position {posName} confirmed, waiting for FC...";
        RequiresUserAction = false;
    }

    /// <summary>
    /// Handle position request from FC (via STATUSTEXT or COMMAND_LONG).
    /// </summary>
    private void HandlePositionRequest(int position, string fcMessage)
    {
        lock (_calibrationLock)
        {
            _currentPositionIndex = position;
        }

        string posName = GetPositionName(position);
        LogInfo($"=== Position {position}/6 requested: {posName} ===");
        LogInfo($"FC message: {fcMessage}");

        CurrentPositionName = posName;
        CalibrationInstructions = $"Place vehicle {posName}";
        FcMessage = fcMessage;
        StatusMessage = fcMessage;
        RequiresUserAction = true;

        // Update step tracking
        CurrentStepNumber = position;
        TotalSteps = 6;
        CalibrationProgress = (int)((position - 1) * 100.0 / 6);
    }

    /// <summary>
    /// Finish IMU calibration (success or failure).
    /// </summary>
    private void FinishImuCalibration(bool success, string message)
    {
        lock (_calibrationLock)
        {
            _inCalibrate = false;
            _currentPositionIndex = 0;
        }

        LogInfo($"=== IMU CALIBRATION {(success ? "COMPLETED" : "FAILED")} ===");
        LogInfo($"Message: {message}");

        IsCalibrating = false;
        ImuCalibrationButtonText = "Calibrate Accel";
        CurrentPositionName = string.Empty;
        RequiresUserAction = false;
        IsImuButtonEnabled = true;

        if (success)
        {
            CalibrationProgress = 100;
            StatusMessage = "Calibration completed successfully!";
            CalibrationInstructions = message;
        }
        else
        {
            CalibrationProgress = 0;
            StatusMessage = $"Calibration failed: {message}";
            CalibrationInstructions = message;
        }
    }

    /// <summary>
    /// Reset IMU calibration state (for connection loss or cancel).
    /// </summary>
    private void ResetImuCalibrationState()
    {
        LogInfo("Resetting IMU calibration state");
        
        lock (_calibrationLock)
        {
            _inCalibrate = false;
            _currentPositionIndex = 0;
        }

        ImuCalibrationButtonText = "Calibrate Accel";
        CurrentPositionName = string.Empty;
        FcMessage = string.Empty;
        IsImuButtonEnabled = true;
    }

    #endregion

    #region STATUSTEXT Parsing (MissionPlanner style)

    /// <summary>
    /// Detect position request from STATUSTEXT keywords.
    /// 
    /// Position mapping:
    ///   1 = LEVEL (flat surface)
    ///   2 = LEFT (on left side)
    ///   3 = RIGHT (on right side)
    ///   4 = NOSEDOWN (nose pointing down)
    ///   5 = NOSEUP (nose pointing up)
    ///   6 = BACK (upside down)
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
    /// </summary>
    private static bool IsFailureMessage(string lower)
    {
        // Filter out PreArm messages (not calibration failures)
        if (lower.Contains("prearm"))
            return false;

        return lower.Contains("calibration failed") ||
               lower.Contains("accel cal failed") ||
               lower.Contains("calibration cancelled") ||
               lower.Contains("calibration timeout");
    }

    /// <summary>
    /// Get position name for display.
    /// 
    /// Position enum values (from MAVLink ACCELCAL_VEHICLE_POS):
    ///   1 = LEVEL
    ///   2 = LEFT
    ///   3 = RIGHT
    ///   4 = NOSE DOWN (NOSEDOWN)
    ///   5 = NOSE UP (NOSEUP)
    ///   6 = BACK
    /// </summary>
    private static string GetPositionName(int position)
    {
        return position switch
        {
            1 => "LEVEL",
            2 => "LEFT",
            3 => "RIGHT",
            4 => "NOSE DOWN",
            5 => "NOSE UP",
            6 => "BACK",
            _ => "UNKNOWN"
        };
    }

    #endregion

    private static string GetStepInstructions(CalibrationStep step) => step switch
    {
        CalibrationStep.Level => "Place the vehicle LEVEL on a flat surface",
        CalibrationStep.LeftSide => "Place the vehicle on its LEFT SIDE",
        CalibrationStep.RightSide => "Place the vehicle on its RIGHT SIDE",
        CalibrationStep.NoseDown => "Place the vehicle NOSE DOWN",
        CalibrationStep.NoseUp => "Place the vehicle NOSE UP",
        CalibrationStep.Back => "Place the vehicle on its BACK (upside down)",
        CalibrationStep.Rotate => "Slowly rotate the vehicle in all directions",
        CalibrationStep.KeepStill => "Keep the vehicle completely still",
        _ => "Follow the instructions"
    };

    [RelayCommand]
    private async Task CalibrateAccelerometerAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to vehicle";
            return;
        }

        RequiresUserAction = false;
        CalibrationInstructions = "Starting accelerometer calibration...";
        await _calibrationService.StartAccelerometerCalibrationAsync(fullSixAxis: true);
    }

    [RelayCommand]
    private async Task CalibrateCompassAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to vehicle";
            return;
        }

        RequiresUserAction = false;
        CalibrationInstructions = "Starting compass calibration...";
        await _calibrationService.StartCompassCalibrationAsync(onboardCalibration: false);
    }

    [RelayCommand]
    private async Task CalibrateGyroscopeAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to vehicle";
            return;
        }

        RequiresUserAction = false;
        CalibrationInstructions = "Keep vehicle still - calibrating gyroscope...";
        await _calibrationService.StartGyroscopeCalibrationAsync();
    }

    [RelayCommand]
    private async Task CalibrateLevelHorizonAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to vehicle";
            return;
        }

        RequiresUserAction = false;
        CalibrationInstructions = "Level horizon calibration...";
        await _calibrationService.StartLevelHorizonCalibrationAsync();
    }

    [RelayCommand]
    private async Task CalibrateBarometerAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to vehicle";
            return;
        }

        RequiresUserAction = false;
        CalibrationInstructions = "Calibrating barometer...";
        await _calibrationService.StartBarometerCalibrationAsync();
    }

    [RelayCommand]
    private async Task AcceptStepAsync()
    {
        if (!IsCalibrating)
            return;

        RequiresUserAction = false;
        await _calibrationService.AcceptCalibrationStepAsync();
    }

    [RelayCommand]
    private async Task CancelCalibrationAsync()
    {
        LogInfo("User requested calibration cancel");
        await _calibrationService.CancelCalibrationAsync();
        RequiresUserAction = false;
        CalibrationInstructions = "Calibration cancelled";
        ResetImuCalibrationState();
    }

    [RelayCommand]
    private async Task RebootFlightControllerAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to vehicle";
            return;
        }

        LogInfo("Rebooting flight controller...");
        StatusMessage = "Rebooting flight controller...";
        var success = await _calibrationService.RebootFlightControllerAsync();
        StatusMessage = success ? "Reboot command sent" : "Failed to send reboot command";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            LogInfo("Disposing CalibrationPageViewModel");
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _connectionService.StatusTextReceived -= OnStatusTextReceived;
            _connectionService.CommandLongReceived -= OnCommandLongReceived;
            _connectionService.CommandAckReceived -= OnCommandAckReceived;
            _calibrationService.CalibrationStateChanged -= OnCalibrationStateChanged;
            _calibrationService.CalibrationProgressChanged -= OnCalibrationProgressChanged;
            _calibrationService.CalibrationStepRequired -= OnCalibrationStepRequired;
        }
        base.Dispose(disposing);
    }
}
