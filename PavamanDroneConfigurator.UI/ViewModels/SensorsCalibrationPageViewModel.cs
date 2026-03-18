using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.Services;
using PavamanDroneConfigurator.UI.Views;

// Use explicit namespace for AccelCalPositionRequestedEventArgs to avoid ambiguity
using AccelCalPositionRequestedEventArgs = PavamanDroneConfigurator.Core.Interfaces.AccelCalPositionRequestedEventArgs;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for Sensors Calibration page.
/// Implements MissionPlanner-style accelerometer calibration:
/// - Single button: "Calibrate Accel" -> "Click when Done" -> "Done"
/// - First click starts calibration
/// - Subsequent clicks confirm vehicle position
/// - FC controls the flow via STATUSTEXT and COMMAND_LONG messages
/// </summary>
public partial class SensorsCalibrationPageViewModel : ViewModelBase
{
    private readonly ILogger<SensorsCalibrationPageViewModel> _logger;
    private readonly ICalibrationService _calibrationService;
    private readonly ISensorConfigService _sensorConfigService;
    private readonly IConnectionService _connectionService;
    private readonly IMavLinkMessageLogger _mavLinkLogger;

    #region Status Properties

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isCalibrating;

    [ObservableProperty]
    private string _connectionStatusColor = "#EF4444";

    [ObservableProperty]
    private string _connectionStatusText = "Disconnected";

    [ObservableProperty]
    private bool _showErrorDialog;

    [ObservableProperty]
    private string _errorDialogTitle = "Error";

    [ObservableProperty]
    private string _errorDialogMessage = string.Empty;

    [ObservableProperty]
    private bool _showDebugLogs = false;

    [ObservableProperty]
    private ObservableCollection<string> _debugLogs = new();
    
    [ObservableProperty]
    private bool _showMavLinkLog = false;

    [ObservableProperty]
    private ObservableCollection<MavLinkLogEntry> _mavLinkMessages = new();

    [ObservableProperty]
    private bool _showRebootPrompt;

    [ObservableProperty]
    private string _rebootPromptMessage = "Calibration complete! Please reboot the autopilot to apply changes.";

    [ObservableProperty]
    private bool _showCalibrationFailedDialog;

    [ObservableProperty]
    private string _calibrationFailedMessage = "The calibration could not be completed.";

    #endregion

    #region Calibration Active States

    [ObservableProperty]
    private bool _isAccelCalibrationActive;

    [ObservableProperty]
    private bool _isCompassCalibrationActive;

    [ObservableProperty]
    private bool _isLevelCalibrationActive;

    [ObservableProperty]
    private bool _isPressureCalibrationActive;

    #endregion

    #region Sensor Availability Properties

    [ObservableProperty]
    private bool _isAccelerometerAvailable;

    [ObservableProperty]
    private bool _isGyroscopeAvailable;

    [ObservableProperty]
    private bool _isCompassAvailable;

    [ObservableProperty]
    private bool _isBarometerAvailable;

    [ObservableProperty]
    private bool _isFlowSensorAvailable;

    [ObservableProperty]
    private string _accelSensorStatus = "Checking...";

    [ObservableProperty]
    private string _gyroSensorStatus = "Checking...";

    [ObservableProperty]
    private string _compassSensorStatus = "Checking...";

    [ObservableProperty]
    private string _baroSensorStatus = "Checking...";

    public bool CanCalibrateAccelerometer => IsConnected && IsAccelerometerAvailable && !IsCalibrating;
    public bool CanCalibrateCompass => IsConnected && IsCompassAvailable && !IsCalibrating;
    public bool CanCalibrateLevelHorizon => IsConnected && IsAccelerometerAvailable && !IsCalibrating;
    public bool CanCalibrateBarometer => IsConnected && IsBarometerAvailable && !IsCalibrating;

    #endregion

    #region Tab Selection

    [ObservableProperty]
    private SensorCalibrationTab _selectedTab = SensorCalibrationTab.Accelerometer;

    [ObservableProperty]
    private bool _isAccelerometerTabSelected = true;

    [ObservableProperty]
    private bool _isCompassTabSelected;

    [ObservableProperty]
    private bool _isLevelHorizonTabSelected;

    [ObservableProperty]
    private bool _isPressureTabSelected;

    [ObservableProperty]
    private bool _isFlowTabSelected;

    #endregion

    #region Accelerometer Properties (MissionPlanner Style)

    [ObservableProperty]
    private string _accelCalibrationStatus = "Not calibrated";

    [ObservableProperty]
    private bool _isAccelCalibrated;

    [ObservableProperty]
    private string _accelInstructions = "Click 'Calibrate Accel' to start 6-position accelerometer calibration.";

    [ObservableProperty]
    private int _accelCalibrationProgress;

    [ObservableProperty]
    private int _accelStepNumber;

    [ObservableProperty]
    private bool _canClickWhenInPosition;
    
    [ObservableProperty]
    private string _accelCurrentStep = string.Empty;

    [ObservableProperty]
    private string _currentCalibrationImage = "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Level.png";

    /// <summary>
    /// Mission Planner-style button text:
    /// - "Calibrate Accel" when idle
    /// - "Click when Done" during calibration
    /// - "Done" when calibration finished
    /// </summary>
    [ObservableProperty]
    private string _accelButtonText = "Calibrate Accel";

    /// <summary>
    /// Button enabled state:
    /// - True when idle and connected
    /// - True during calibration (to confirm positions)
    /// - False when calibration complete (until reset)
    /// </summary>
    [ObservableProperty]
    private bool _isAccelButtonEnabled = true;

    /// <summary>
    /// Current position requested by FC (from MAV_CMD_ACCELCAL_VEHICLE_POS)
    /// </summary>
    private AccelCalVehiclePosition _currentRequestedPosition;

    // Step border and background colors for UI
    [ObservableProperty] private string _step1BorderColor = "#E2E8F0";
    [ObservableProperty] private string _step1BackgroundColor = "#F8FAFC";
    [ObservableProperty] private string _step2BorderColor = "#E2E8F0";
    [ObservableProperty] private string _step2BackgroundColor = "#F8FAFC";
    [ObservableProperty] private string _step3BorderColor = "#E2E8F0";
    [ObservableProperty] private string _step3BackgroundColor = "#F8FAFC";
    [ObservableProperty] private string _step4BorderColor = "#E2E8F0";
    [ObservableProperty] private string _step4BackgroundColor = "#F8FAFC";
    [ObservableProperty] private string _step5BorderColor = "#E2E8F0";
    [ObservableProperty] private string _step5BackgroundColor = "#F8FAFC";
    [ObservableProperty] private string _step6BorderColor = "#E2E8F0";
    [ObservableProperty] private string _step6BackgroundColor = "#F8FAFC";

    // Colors for step indicators
    private const string PendingBorderColor = "#E2E8F0";
    private const string PendingBackgroundColor = "#F8FAFC";
    private const string WaitingBorderColor = "#EF4444"; // Red - waiting for user
    private const string WaitingBackgroundColor = "#FEE2E2";
    private const string CompleteBorderColor = "#10B981"; // Green - completed
    private const string CompleteBackgroundColor = "#D1FAE5";

    #endregion

    #region Compass Properties

    [ObservableProperty]
    private ObservableCollection<CompassInfo> _compasses = new();

    [ObservableProperty]
    private CompassInfo? _selectedCompass;

    [ObservableProperty]
    private string _compassCalibrationStatus = "Calibration required";

    [ObservableProperty]
    private int _compassCalibrationProgress;

    [ObservableProperty]
    private string _compassInstructions = "Click 'Start Calibration' and rotate the vehicle in all directions.";

    /// <summary>
    /// Tracks whether compass priority order has been changed in the UI but not yet saved to FC
    /// </summary>
    [ObservableProperty]
    private bool _hasCompassPriorityChanges;

    /// <summary>
    /// Stores the original compass order to detect changes
    /// </summary>
    private List<int> _originalCompassOrder = new();

    #endregion

    #region Level Horizon Properties

    [ObservableProperty]
    private string _levelCalibrationStatus = "Not calibrated";

    [ObservableProperty]
    private bool _isLevelCalibrated;

    [ObservableProperty]
    private string _levelInstructions = "Place vehicle on a level surface and click Calibrate.";

    #endregion

    #region Pressure Properties

    [ObservableProperty]
    private string _pressureCalibrationStatus = "Not calibrated";

    [ObservableProperty]
    private bool _isPressureCalibrated;

    [ObservableProperty]
    private string _pressureInstructions = "Click Calibrate to calibrate barometer";

    [ObservableProperty]
    private int _pressureCalibrationProgress;

    #endregion

    #region Flow Sensor Properties

    [ObservableProperty]
    private FlowType _selectedFlowType = FlowType.Disabled;

    [ObservableProperty]
    private float _flowXAxisScale;

    [ObservableProperty]
    private float _flowYAxisScale;

    [ObservableProperty]
    private float _flowYawAlignment;

    [ObservableProperty]
    private bool _isFlowEnabled;

    public ObservableCollection<FlowTypeOption> FlowTypeOptions { get; } = new();

    #endregion

    private SensorCalibrationConfiguration? _currentConfiguration;

    #region Compass Calibration Properties (MissionPlanner Onboard Style)

    [ObservableProperty]
    private bool _isOnboardCompassCalActive;

    [ObservableProperty]
    private int _compass1Progress;

    [ObservableProperty]
    private int _compass2Progress;

    [ObservableProperty]
    private int _compass3Progress;

    [ObservableProperty]
    private string _compass1Status = "Not started";

    [ObservableProperty]
    private string _compass2Status = "Not started";

    [ObservableProperty]
    private string _compass3Status = "Not started";

    [ObservableProperty]
    private string _compass1Result = string.Empty;

    [ObservableProperty]
    private string _compass2Result = string.Empty;

    [ObservableProperty]
    private string _compass3Result = string.Empty;

    [ObservableProperty]
    private string _compass1OffsetColor = "#6B7280"; // Gray

    [ObservableProperty]
    private string _compass2OffsetColor = "#6B7280";

    [ObservableProperty]
    private string _compass3OffsetColor = "#6B7280";

    [ObservableProperty]
    private bool _canAcceptCompassCal;

    [ObservableProperty]
    private bool _canCancelCompassCal;

    [ObservableProperty]
    private bool _canStartCompassCal = true;

    [ObservableProperty]
    private string _compassCalStatus = "Ready to calibrate";

    [ObservableProperty]
    private string _compassCalButtonText = "Start Calibration";

    private int _compassCount;
    private int _completedCompassCount;

    #endregion

    public SensorsCalibrationPageViewModel(
        ILogger<SensorsCalibrationPageViewModel> logger,
        ICalibrationService calibrationService,
        ISensorConfigService sensorConfigService,
        IConnectionService connectionService,
        IMavLinkMessageLogger mavLinkLogger)
    {
        _logger = logger;
        _calibrationService = calibrationService;
        _sensorConfigService = sensorConfigService;
        _connectionService = connectionService;
        _mavLinkLogger = mavLinkLogger;

        // Subscribe to connection events
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _mavLinkLogger.MessageLogged += OnMavLinkMessageLogged;

        // Subscribe to calibration service events
        _calibrationService.CalibrationStateChanged += OnCalibrationStateChanged;
        _calibrationService.CalibrationProgressChanged += OnCalibrationProgressChanged;
        _calibrationService.StatusTextReceived += OnCalibrationStatusTextReceived;
        _calibrationService.AccelCalPositionRequested += OnAccelCalPositionRequested;
        
        // Subscribe to compass calibration events
        _calibrationService.CompassCalProgressReceived += OnCompassCalProgressReceived;
        _calibrationService.CompassCalReportReceived += OnCompassCalReportReceived;
        _calibrationService.CompassCalibrationStateChanged += OnCompassCalStateChanged;

        InitializeFlowTypeOptions();
        UpdateConnectionStatus(_connectionService.IsConnected);
        
        // Initialize sensor data if already connected
        if (_connectionService.IsConnected)
        {
            _ = RefreshAsync();
        }
        
        AddDebugLog("ViewModel initialized - MissionPlanner-style IMU and Compass calibration ready");
    }

    #region Event Handlers

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateConnectionStatus(connected);
            if (connected)
                _ = RefreshAsync();
            else
            {
                IsAccelerometerAvailable = false;
                IsGyroscopeAvailable = false;
                IsCompassAvailable = false;
                IsBarometerAvailable = false;
                AccelSensorStatus = "Not connected";
                GyroSensorStatus = "Not connected";
                CompassSensorStatus = "Not connected";
                BaroSensorStatus = "Not connected";
            }
        });
    }

    private void OnMavLinkMessageLogged(object? sender, MavLinkLogEntry e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MavLinkMessages.Insert(0, e);
            if (MavLinkMessages.Count > 100)
                MavLinkMessages.RemoveAt(MavLinkMessages.Count - 1);
        });
    }

    /// <summary>
    /// Handle calibration state changes from CalibrationService
    /// </summary>
    private void OnCalibrationStateChanged(object? sender, CalibrationStateModel e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Type == CalibrationType.Accelerometer)
            {
                HandleAccelCalibrationStateChange(e);
            }
            else if (e.Type == CalibrationType.Level)
            {
                LevelCalibrationStatus = e.Message;
                IsLevelCalibrationActive = e.State == CalibrationState.InProgress ||
                                           e.State == CalibrationState.WaitingForUserAction;
                
                if (e.State == CalibrationState.Completed)
                {
                    IsLevelCalibrated = true;
                    IsLevelCalibrationActive = false;
                    LevelInstructions = "Level calibration complete!";
                    StatusMessage = "Level horizon calibration completed successfully";
                    ShowRebootPromptDialog("Level calibration complete! Please reboot the autopilot to apply changes.");
                }
                else if (e.State == CalibrationState.Failed)
                {
                    IsLevelCalibrated = false;
                    IsLevelCalibrationActive = false;
                    LevelInstructions = $"Level calibration failed: {e.Message}. Place vehicle on a level surface and try again.";
                    StatusMessage = "Level horizon calibration failed";
                    ShowError("Level Calibration Failed", e.Message);
                }
            }
            else if (e.Type == CalibrationType.Barometer)
            {
                PressureCalibrationStatus = e.Message;
                IsPressureCalibrationActive = e.State == CalibrationState.InProgress;
                
                if (e.State == CalibrationState.Completed)
                {
                    IsPressureCalibrated = true;
                    IsPressureCalibrationActive = false;
                    PressureInstructions = "Barometer calibration complete!";
                    PressureCalibrationProgress = 100;
                    StatusMessage = "Barometer calibration completed successfully";
                    // Show reboot prompt like we do for other calibrations
                    ShowRebootPromptDialog("Barometer calibration complete! Please reboot the autopilot to apply changes.");
                }
                else if (e.State == CalibrationState.Failed)
                {
                    IsPressureCalibrated = false;
                    IsPressureCalibrationActive = false;
                    PressureInstructions = $"Barometer calibration failed: {e.Message}. Keep vehicle still and try again.";
                    StatusMessage = "Barometer calibration failed";
                    ShowError("Barometer Calibration Failed", e.Message);
                }
            }
            
            // Update overall calibrating flag
            IsCalibrating = _calibrationService.IsCalibrating;
            UpdateButtonStates();
        });
    }

    /// <summary>
    /// Handle accelerometer calibration state changes (MissionPlanner style)
    /// </summary>
    private void HandleAccelCalibrationStateChange(CalibrationStateModel e)
    {
        AddDebugLog($"[AccelCal] State: {e.State} SM: {e.StateMachine} Pos: {e.CurrentPosition} Completed: [{string.Join(",", e.CompletedPositions)}] Msg: {e.Message}");

        AccelCalibrationStatus = e.Message;
        AccelCalibrationProgress = e.Progress;
        IsAccelCalibrationActive = e.State == CalibrationState.InProgress ||
                                   e.State == CalibrationState.WaitingForUserAction;

        // Update step indicators based on completed positions and current position
        UpdateStepIndicatorsFromState(e.CompletedPositions, e.CurrentPosition, e.CanConfirmPosition);

        switch (e.State)
        {
            case CalibrationState.InProgress:
            case CalibrationState.WaitingForUserAction:
                // MissionPlanner: button text changes to "Click when Done"
                AccelButtonText = "Click when Done";
                IsAccelButtonEnabled = e.CanConfirmPosition;
                AccelInstructions = e.Message;
                break;

            case CalibrationState.Completed:
                // MissionPlanner: button text changes to "Done", button disabled
                AccelButtonText = "Done";
                IsAccelButtonEnabled = false;
                IsAccelCalibrated = true;
                AccelInstructions = "Calibration successful! Reboot required to apply changes.";
                ResetAllStepIndicatorsToComplete();
                ShowRebootPromptDialog("Accelerometer calibration complete! Please reboot the autopilot to apply changes.");
                break;

            case CalibrationState.Failed:
                // Reset to allow retry and show failed dialog
                AccelButtonText = "Calibrate Accel";
                IsAccelButtonEnabled = true;
                AccelInstructions = $"Calibration failed: {e.Message}. Click to retry.";
                ResetAllStepIndicators();
                
                // Show calibration failed popup
                CalibrationFailedMessage = !string.IsNullOrEmpty(e.Message) 
                    ? e.Message 
                    : "The accelerometer calibration could not be completed. Please ensure the vehicle is held steady in each position and try again.";
                ShowCalibrationFailedDialog = true;
                break;

            case CalibrationState.Idle:
            case CalibrationState.NotStarted:
                AccelButtonText = "Calibrate Accel";
                IsAccelButtonEnabled = IsConnected && IsAccelerometerAvailable;
                ResetAllStepIndicators();
                break;
        }
    }

    /// <summary>
    /// Update step indicators based on completed positions and current position.
    /// This is the SINGLE SOURCE OF TRUTH for step colors.
    /// - Completed positions -> GREEN
    /// - Current waiting position -> RED
    /// - Other positions -> GRAY (pending)
    /// </summary>
    private void UpdateStepIndicatorsFromState(List<int> completedPositions, int currentPosition, bool isWaiting)
    {
        // Position enum values: Level=1, Left=2, Right=3, NoseDown=4, NoseUp=5, Back=6
        
        // Step 1 = Level (position 1)
        if (completedPositions.Contains(1))
        {
            Step1BorderColor = CompleteBorderColor;
            Step1BackgroundColor = CompleteBackgroundColor;
        }
        else if (currentPosition == 1 && isWaiting)
        {
            Step1BorderColor = WaitingBorderColor;
            Step1BackgroundColor = WaitingBackgroundColor;
        }
        else
        {
            Step1BorderColor = PendingBorderColor;
            Step1BackgroundColor = PendingBackgroundColor;
        }

        // Step 2 = Left (position 2)
        if (completedPositions.Contains(2))
        {
            Step2BorderColor = CompleteBorderColor;
            Step2BackgroundColor = CompleteBackgroundColor;
        }
        else if (currentPosition == 2 && isWaiting)
        {
            Step2BorderColor = WaitingBorderColor;
            Step2BackgroundColor = WaitingBackgroundColor;
        }
        else
        {
            Step2BorderColor = PendingBorderColor;
            Step2BackgroundColor = PendingBackgroundColor;
        }

        // Step 3 = Right (position 3)
        if (completedPositions.Contains(3))
        {
            Step3BorderColor = CompleteBorderColor;
            Step3BackgroundColor = CompleteBackgroundColor;
        }
        else if (currentPosition == 3 && isWaiting)
        {
            Step3BorderColor = WaitingBorderColor;
            Step3BackgroundColor = WaitingBackgroundColor;
        }
        else
        {
            Step3BorderColor = PendingBorderColor;
            Step3BackgroundColor = PendingBackgroundColor;
        }

        // Step 4 = NoseDown (position 4)
        if (completedPositions.Contains(4))
        {
            Step4BorderColor = CompleteBorderColor;
            Step4BackgroundColor = CompleteBackgroundColor;
        }
        else if (currentPosition == 4 && isWaiting)
        {
            Step4BorderColor = WaitingBorderColor;
            Step4BackgroundColor = WaitingBackgroundColor;
        }
        else
        {
            Step4BorderColor = PendingBorderColor;
            Step4BackgroundColor = PendingBackgroundColor;
        }

        // Step 5 = NoseUp (position 5)
        if (completedPositions.Contains(5))
        {
            Step5BorderColor = CompleteBorderColor;
            Step5BackgroundColor = CompleteBackgroundColor;
        }
        else if (currentPosition == 5 && isWaiting)
        {
            Step5BorderColor = WaitingBorderColor;
            Step5BackgroundColor = WaitingBackgroundColor;
        }
        else
        {
            Step5BorderColor = PendingBorderColor;
            Step5BackgroundColor = PendingBackgroundColor;
        }

        // Step 6 = Back (position 6)
        if (completedPositions.Contains(6))
        {
            Step6BorderColor = CompleteBorderColor;
            Step6BackgroundColor = CompleteBackgroundColor;
        }
        else if (currentPosition == 6 && isWaiting)
        {
            Step6BorderColor = WaitingBorderColor;
            Step6BackgroundColor = WaitingBackgroundColor;
        }
        else
        {
            Step6BorderColor = PendingBorderColor;
            Step6BackgroundColor = PendingBackgroundColor;
        }
    }

    /// <summary>
    /// Handle progress updates from CalibrationService
    /// </summary>
    private void OnCalibrationProgressChanged(object? sender, CalibrationProgressEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Type == CalibrationType.Accelerometer)
            {
                AccelCalibrationProgress = e.ProgressPercent;
                if (e.CurrentStep.HasValue)
                {
                    AccelStepNumber = e.CurrentStep.Value;
                }
            }
            else if (e.Type == CalibrationType.Barometer)
            {
                PressureCalibrationProgress = e.ProgressPercent;
            }
        });
    }

    /// <summary>
    /// Handle STATUSTEXT messages during calibration for debugging
    /// </summary>
    private void OnCalibrationStatusTextReceived(object? sender, CalibrationStatusTextEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AddDebugLog($"[FC STATUSTEXT] [{e.Severity}] {e.Text}");
            
            // Update instructions if it's a placement instruction
            var lower = e.Text.ToLowerInvariant();
            if (lower.Contains("place vehicle") || lower.Contains("calibration"))
            {
                AccelInstructions = e.Text;
            }
            
            // Check for compass calibration messages
            if (lower.Contains("compass") && lower.Contains("calibrat"))
            {
                CompassCalStatus = e.Text;
            }
        });
    }

    /// <summary>
    /// Handle FC requesting a specific vehicle position (MissionPlanner style).
    /// FC sends MAV_CMD_ACCELCAL_VEHICLE_POS with param1 = position (1-6).
    /// 
    /// NOTE: This handler should NOT update step indicator colors directly.
    /// Step colors are managed by UpdateStepIndicatorsFromState which is called
    /// from OnCalibrationStateChanged - that is the SINGLE SOURCE OF TRUTH.
    /// This handler only updates calibration image and other non-color UI elements.
    /// 
    /// NOTE: Button state is managed by OnCalibrationStateChanged via CanConfirmPosition.
    /// This handler should NOT override button state - the CalibrationService is the
    /// single source of truth for when the user can click.
    /// </summary>
    private void OnAccelCalPositionRequested(object? sender, AccelCalPositionRequestedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AddDebugLog($"[AccelCal] FC requested position: {e.Position} ({e.PositionName}) - Step {e.StepNumber}/6");

            _currentRequestedPosition = e.Position;
            AccelStepNumber = e.StepNumber;
            AccelCurrentStep = e.PositionName;
            AccelInstructions = $"Place vehicle {e.PositionName} and click 'Click when Done'";

            // ONLY update calibration image - DO NOT override step indicator colors here!
            // Step colors are managed by UpdateStepIndicatorsFromState in OnCalibrationStateChanged
            // which uses the actual CompletedPositions list from CalibrationService (single source of truth)
            UpdateCalibrationImage(e.Position);

            // NOTE: Button state (IsAccelButtonEnabled) is managed by OnCalibrationStateChanged
            // via the CanConfirmPosition property. We set CanClickWhenInPosition here for UI feedback
            // but DO NOT set IsAccelButtonEnabled - that's controlled by CalibrationService state.
            CanClickWhenInPosition = true;
        });
    }

    private void OnCompassCalProgressReceived(object? sender, CompassCalProgressEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AddDebugLog($"[CompassCal] Progress: compass={e.CompassId} status={e.Status} pct={e.CompletionPercent}%");

            // Update progress bars and status based on compass_id
            switch (e.CompassId)
            {
                case 0:
                    Compass1Progress = e.CompletionPercent;
                    Compass1Status = GetCalStatusText(e.Status, e.CompletionPercent);
                    break;
                case 1:
                    Compass2Progress = e.CompletionPercent;
                    Compass2Status = GetCalStatusText(e.Status, e.CompletionPercent);
                    break;
                case 2:
                    Compass3Progress = e.CompletionPercent;
                    Compass3Status = GetCalStatusText(e.Status, e.CompletionPercent);
                    break;
            }

            _compassCount = Math.Max(_compassCount, e.CompassId + 1);
            
            // Update overall status
            CompassCalStatus = $"Calibrating... Rotate vehicle in all directions. ({e.CompletionPercent}%)";
            CompassInstructions = "Rotate the vehicle slowly in all directions to cover all orientations.";
        });
    }

    private void OnCompassCalReportReceived(object? sender, CompassCalReportEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var resultText = $"Fitness: {e.Fitness:F1}, Offsets: X:{e.Offsets.X:F1} Y:{e.Offsets.Y:F1} Z:{e.Offsets.Z:F1}";
            var statusText = e.Status == MagCalStatus.Success ? "SUCCESS" : e.Status.ToString();
            AddDebugLog($"[CompassCal] Report: compass={e.CompassId} status={statusText} {resultText}");

            // Determine offset color based on magnitude
            var maxOffset = Math.Max(Math.Max(Math.Abs(e.Offsets.X), Math.Abs(e.Offsets.Y)), Math.Abs(e.Offsets.Z));
            var offsetColor = CompassOffsetThresholds.GetColorForOffset(maxOffset);

            // Update result text, progress to 100%, and color
            switch (e.CompassId)
            {
                case 0:
                    Compass1Progress = 100;
                    Compass1Result = resultText;
                    Compass1Status = statusText;
                    Compass1OffsetColor = offsetColor;
                    break;
                case 1:
                    Compass2Progress = 100;
                    Compass2Result = resultText;
                    Compass2Status = statusText;
                    Compass2OffsetColor = offsetColor;
                    break;
                case 2:
                    Compass3Progress = 100;
                    Compass3Result = resultText;
                    Compass3Status = statusText;
                    Compass3OffsetColor = offsetColor;
                    break;
            }

            if (e.IsAutosaved)
            {
                _completedCompassCount++;
                AddDebugLog($"[CompassCal] Compass {e.CompassId} autosaved. Completed: {_completedCompassCount}/{_compassCount}");
                
                if (_completedCompassCount >= _compassCount && _compassCount > 0)
                {
                    // All compasses complete and autosaved
                    CanAcceptCompassCal = false;
                    CanCancelCompassCal = false;
                    CanStartCompassCal = true;
                    CompassCalButtonText = "Start Calibration";
                    CompassCalStatus = "Calibration complete and saved! Please reboot the autopilot.";
                    CompassInstructions = "Compass calibration successful. Reboot required to apply changes.";
                    IsOnboardCompassCalActive = false;
                    IsCompassCalibrationActive = false;
                    
                    // Show reboot prompt
                    ShowRebootPromptDialog("Compass calibration complete! Please reboot the autopilot to apply the new calibration values.");
                }
            }
            else if (e.Status == MagCalStatus.Success && !e.IsAutosaved)
            {
                // Calibration successful but needs acceptance
                CanAcceptCompassCal = true;
                CompassCalStatus = "Calibration successful. Click Accept to save.";
            }
        });
    }

    private void OnCompassCalStateChanged(object? sender, CompassCalibrationStateModel e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AddDebugLog($"[CompassCal] State changed: {e.State} - {e.Message}");
            
            CompassCalStatus = e.Message;
            IsOnboardCompassCalActive = e.IsCalibrating;
            IsCompassCalibrationActive = e.IsCalibrating;
            CanAcceptCompassCal = e.CanAccept;
            CanCancelCompassCal = e.CanCancel;
            CanStartCompassCal = !e.IsCalibrating && !e.CanAccept;

            // Keep instructions aligned with current state to avoid stale "Preparing..." text
            switch (e.State)
            {
                case CompassCalibrationState.Starting:
                    CompassInstructions = "Preparing calibration. Keep the vehicle steady for a moment.";
                    break;
                case CompassCalibrationState.RunningSphereFit:
                case CompassCalibrationState.RunningEllipsoidFit:
                    CompassInstructions = "Rotate the vehicle slowly in all directions to cover all orientations.";
                    break;
                case CompassCalibrationState.WaitingForAccept:
                    CompassInstructions = "Calibration data is ready. Click Accept to save or Cancel to discard.";
                    break;
                case CompassCalibrationState.Accepted:
                    CompassInstructions = "Calibration accepted. Reboot required to apply changes.";
                    break;
                case CompassCalibrationState.Cancelled:
                    CompassInstructions = "Calibration cancelled.";
                    break;
            }

            // Update button text based on state
            if (e.IsCalibrating)
            {
                CompassCalButtonText = "Calibrating...";
            }
            else if (e.CanAccept)
            {
                CompassCalButtonText = "Accept";
            }
            else
            {
                CompassCalButtonText = "Start Calibration";
            }

            // Update progress from state
            foreach (var kvp in e.CompassProgress)
            {
                switch (kvp.Key)
                {
                    case 0:
                        Compass1Progress = kvp.Value;
                        break;
                    case 1:
                        Compass2Progress = kvp.Value;
                        break;
                    case 2:
                        Compass3Progress = kvp.Value;
                        break;
                }
            }

            // Check for completion states
            if (e.State == CompassCalibrationState.Accepted || e.State == CompassCalibrationState.Cancelled)
            {
                IsOnboardCompassCalActive = false;
                IsCompassCalibrationActive = false;
                CanStartCompassCal = true;
                CompassCalButtonText = "Start Calibration";
                
                if (e.State == CompassCalibrationState.Accepted)
                {
                    ShowRebootPromptDialog("Compass calibration accepted! Please reboot the autopilot to apply changes.");
                }
            }
            else if (e.State == CompassCalibrationState.Failed)
            {
                IsOnboardCompassCalActive = false;
                IsCompassCalibrationActive = false;
                CanStartCompassCal = true;
                CompassCalButtonText = "Start Calibration";
                CompassInstructions = "Calibration failed. Please try again.";
            }

            IsCalibrating = _calibrationService.IsCalibrating;
            UpdateButtonStates();
        });
    }

    private string GetCalStatusText(MagCalStatus status, int progress)
    {
        return status switch
        {
            MagCalStatus.NotStarted => "Not started",
            MagCalStatus.WaitingToStart => "Waiting...",
            MagCalStatus.RunningStepOne => $"Sphere fit: {progress}%", // Placeholder for actual step text
            MagCalStatus.RunningStepTwo => $"Ellipsoid fit: {progress}%", // Placeholder for actual step text
            MagCalStatus.Success => "Success",
            MagCalStatus.Failed => "Failed",
            MagCalStatus.BadOrientation => "Bad orientation",
            MagCalStatus.BadRadius => "Bad radius",
            _ => status.ToString()
        };
    }

    private void ShowRebootPromptDialog(string message)
    {
        RebootPromptMessage = message;
        ShowRebootPrompt = true;
    }

    #endregion

    #region Step Indicator Helpers

    /// <summary>
    /// DEPRECATED: This method uses hardcoded assumptions about position order.
    /// Use UpdateStepIndicatorsFromState instead, which uses the actual CompletedPositions
    /// from CalibrationService as the single source of truth.
    /// 
    /// Kept for reference but should not be called.
    /// </summary>
    [Obsolete("Use UpdateStepIndicatorsFromState instead - this method uses hardcoded position order assumptions")]
    private void UpdateStepIndicatorForPosition(AccelCalVehiclePosition position)
    {
        // This method is deprecated and should not be used.
        // Step colors should be derived from CompletedPositions + CurrentPosition
        // in UpdateStepIndicatorsFromState, which is the single source of truth.
        
        // The old implementation assumed positions are always completed in order,
        // which is not always true (FC may request positions in different orders
        // or retry a position).
    }

    private void ResetAllStepIndicatorsToPending()
    {
        Step1BorderColor = PendingBorderColor;
        Step1BackgroundColor = PendingBackgroundColor;
        Step2BorderColor = PendingBorderColor;
        Step2BackgroundColor = PendingBackgroundColor;
        Step3BorderColor = PendingBorderColor;
        Step3BackgroundColor = PendingBackgroundColor;
        Step4BorderColor = PendingBorderColor;
        Step4BackgroundColor = PendingBackgroundColor;
        Step5BorderColor = PendingBorderColor;
        Step5BackgroundColor = PendingBackgroundColor;
        Step6BorderColor = PendingBorderColor;
        Step6BackgroundColor = PendingBackgroundColor;
    }

    private void ResetAllStepIndicators()
    {
        ResetAllStepIndicatorsToPending();
        AccelCurrentStep = string.Empty;
        CurrentCalibrationImage = "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Level.png";
    }

    private void ResetAllStepIndicatorsToComplete()
    {
        Step1BorderColor = CompleteBorderColor;
        Step1BackgroundColor = CompleteBackgroundColor;
        Step2BorderColor = CompleteBorderColor;
        Step2BackgroundColor = CompleteBackgroundColor;
        Step3BorderColor = CompleteBorderColor;
        Step3BackgroundColor = CompleteBackgroundColor;
        Step4BorderColor = CompleteBorderColor;
        Step4BackgroundColor = CompleteBackgroundColor;
        Step5BorderColor = CompleteBorderColor;
        Step5BackgroundColor = CompleteBackgroundColor;
        Step6BorderColor = CompleteBorderColor;
        Step6BackgroundColor = CompleteBackgroundColor;
    }

    private void UpdateCalibrationImage(AccelCalVehiclePosition position)
    {
        CurrentCalibrationImage = position switch
        {
            AccelCalVehiclePosition.Level => "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Level.png",
            AccelCalVehiclePosition.Left => "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Left-Side.png",
            AccelCalVehiclePosition.Right => "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Right-Side.png",
            AccelCalVehiclePosition.NoseDown => "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Nose-Down.png",
            AccelCalVehiclePosition.NoseUp => "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Nose-Up.png",
            AccelCalVehiclePosition.Back => "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Back-Side.png",
            _ => "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Level.png"
        };
    }

    #endregion

    #region Helper Methods

    private void AddDebugLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] {message}";
        
        Dispatcher.UIThread.Post(() =>
        {
            DebugLogs.Add(logEntry);
            while (DebugLogs.Count > 100)
                DebugLogs.RemoveAt(0);
        });
        
        _logger.LogDebug("{Message}", message);
    }

    private void ShowError(string title, string message)
    {
        AddDebugLog($"ERROR: {title} - {message}");
        Dispatcher.UIThread.Post(() =>
        {
            ErrorDialogTitle = title;
            ErrorDialogMessage = message;
            ShowErrorDialog = true;
        });
    }

    /// <summary>
    /// Shows the calibration disclaimer dialog and returns whether the user accepted.
    /// </summary>
    private async Task<bool> ShowCalibrationDisclaimerAsync(string calibrationType)
    {
        try
        {
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                AddDebugLog("Warning: Could not find main window for disclaimer dialog");
                return true; // Allow calibration to proceed if window not found
            }

            var disclaimerViewModel = DisclaimerDialogViewModel.CreateForCalibration(calibrationType);
            var disclaimerDialog = new DisclaimerDialog
            {
                DataContext = disclaimerViewModel
            };

            var result = await disclaimerDialog.ShowDialog<bool>(mainWindow);
            AddDebugLog($"[Disclaimer] {calibrationType} calibration disclaimer result: {result}");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing calibration disclaimer dialog");
            AddDebugLog($"Error showing disclaimer: {ex.Message}");
            return true; // Allow calibration to proceed on error
        }
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    private void InitializeFlowTypeOptions()
    {
        FlowTypeOptions.Add(new FlowTypeOption { Type = FlowType.Disabled, Label = "Disable" });
        FlowTypeOptions.Add(new FlowTypeOption { Type = FlowType.RawSensor, Label = "Enable" });
        FlowTypeOptions.Add(new FlowTypeOption { Type = FlowType.PX4FlowMAVLink, Label = "PX4Flow MAVLink" });
        FlowTypeOptions.Add(new FlowTypeOption { Type = FlowType.PMW3901, Label = "PMW3901" });
    }

    private void UpdateConnectionStatus(bool connected)
    {
        IsConnected = connected;
        ConnectionStatusColor = connected ? "#10B981" : "#EF4444";
        ConnectionStatusText = connected ? "Connected" : "Disconnected";
        AddDebugLog($"Connection: {ConnectionStatusText}");
        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        OnPropertyChanged(nameof(CanCalibrateAccelerometer));
        OnPropertyChanged(nameof(CanCalibrateCompass));
        OnPropertyChanged(nameof(CanCalibrateLevelHorizon));
        OnPropertyChanged(nameof(CanCalibrateBarometer));
        
        // Update accel button enabled state based on current calibration state
        // IMPORTANT: During calibration, button state is managed by CalibrationService
        // via CanConfirmPosition in HandleAccelCalibrationStateChange. Do NOT override here!
        if (!IsConnected || !IsAccelerometerAvailable)
        {
            IsAccelButtonEnabled = false;
        }
        else if (AccelButtonText == "Done")
        {
            // Calibration completed - keep button disabled
            IsAccelButtonEnabled = false;
        }
        else if (!IsCalibrating && !IsAccelCalibrationActive)
        {
            // Only enable button when NOT calibrating (to start new calibration)
            // During calibration, HandleAccelCalibrationStateChange manages button via CanConfirmPosition
            IsAccelButtonEnabled = true;
        }
        // NOTE: When IsCalibrating or IsAccelCalibrationActive is true, 
        // button state is managed by HandleAccelCalibrationStateChange - don't touch it here
        
        // Update compass button state
        CanStartCompassCal = IsConnected && IsCompassAvailable && !IsOnboardCompassCalActive && !CanAcceptCompassCal;
    }

    private void ResetCompassCalibrationUI()
    {
        Compass1Progress = 0;
        Compass2Progress = 0;
        Compass3Progress = 0;
        Compass1Status = "Not started";
        Compass2Status = "Not started";
        Compass3Status = "Not started";
        Compass1Result = string.Empty;
        Compass2Result = string.Empty;
        Compass3Result = string.Empty;
        Compass1OffsetColor = "#6B7280";
        Compass2OffsetColor = "#6B7280";
        Compass3OffsetColor = "#6B7280";
        _compassCount = 0;
        _completedCompassCount = 0;
    }

    partial void OnIsCalibratingChanged(bool value) => UpdateButtonStates();

    partial void OnSelectedTabChanged(SensorCalibrationTab value)
    {
        IsAccelerometerTabSelected = value == SensorCalibrationTab.Accelerometer;
        IsCompassTabSelected = value == SensorCalibrationTab.Compass;
        IsLevelHorizonTabSelected = value == SensorCalibrationTab.LevelHorizon;
        IsPressureTabSelected = value == SensorCalibrationTab.Pressure;
        IsFlowTabSelected = value == SensorCalibrationTab.Flow;
    }

    #endregion

    #region Commands - General

    [RelayCommand]
    private void CloseErrorDialog() => ShowErrorDialog = false;

    [RelayCommand]
    private void CloseRebootPrompt() => ShowRebootPrompt = false;

    /// <summary>
    /// Reboot from reboot prompt dialog
    /// </summary>
    [RelayCommand]
    private async Task RebootFromPromptAsync()
    {
        ShowRebootPrompt = false;
        await RebootAsync();
    }

    [RelayCommand]
    private void CloseCalibrationFailedDialog() => ShowCalibrationFailedDialog = false;

    [RelayCommand]
    private async Task RetryCalibrationAsync()
    {
        ShowCalibrationFailedDialog = false;
        
        // Reset state and retry accelerometer calibration
        ResetAllStepIndicators();
        AccelButtonText = "Calibrate Accel";
        IsAccelButtonEnabled = true;
        AccelInstructions = "Click 'Calibrate Accel' to start 6-position accelerometer calibration.";
        
        await CalibrateAccelerometerAsync();
    }

    [RelayCommand]
    private void ToggleDebugLogs() => ShowDebugLogs = !ShowDebugLogs;

    [RelayCommand]
    private void ClearDebugLogs()
    {
        DebugLogs.Clear();
        AddDebugLog("Logs cleared");
    }
    
    [RelayCommand]
    private void ClearMavLinkLog()
    {
        MavLinkMessages.Clear();
        _mavLinkLogger.ClearLog();
        AddDebugLog("MAVLink log cleared");
    }

    [RelayCommand]
    private async Task CopyDebugLogsAsync()
    {
        try
        {
            var allLogs = string.Join(Environment.NewLine, DebugLogs);
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(allLogs);
                AddDebugLog($"Copied {DebugLogs.Count} log entries to clipboard");
                StatusMessage = $"Copied {DebugLogs.Count} logs to clipboard";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy logs");
            ShowError("Copy Failed", $"Failed to copy logs: {ex.Message}");
        }
    }

    #endregion

    #region Commands - Tab Selection

    [RelayCommand]
    private void SelectAccelerometerTab() => SelectedTab = SensorCalibrationTab.Accelerometer;

    [RelayCommand]
    private void SelectCompassTab() => SelectedTab = SensorCalibrationTab.Compass;

    [RelayCommand]
    private void SelectLevelHorizonTab() => SelectedTab = SensorCalibrationTab.LevelHorizon;

    [RelayCommand]
    private void SelectPressureTab() => SelectedTab = SensorCalibrationTab.Pressure;

    [RelayCommand]
    private void SelectFlowTab() => SelectedTab = SensorCalibrationTab.Flow;

    #endregion

    #region Commands - Calibration

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect to a vehicle first.");
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Loading sensor configuration...";
            AddDebugLog("Refreshing sensor configuration...");

            _currentConfiguration = await _sensorConfigService.GetSensorConfigurationAsync();

            if (_currentConfiguration != null)
            {
                IsAccelerometerAvailable = _currentConfiguration.IsAccelAvailable;
                IsGyroscopeAvailable = _currentConfiguration.IsGyroAvailable;
                IsCompassAvailable = _currentConfiguration.Compasses.Any();
                IsBarometerAvailable = _currentConfiguration.IsBaroAvailable;

                AccelSensorStatus = IsAccelerometerAvailable ? "Available" : "Not detected";
                GyroSensorStatus = IsGyroscopeAvailable ? "Available" : "Not detected";
                CompassSensorStatus = IsCompassAvailable ? $"{_currentConfiguration.Compasses.Count} detected" : "Not detected";
                BaroSensorStatus = IsBarometerAvailable ? "Available" : "Not detected";

                IsAccelCalibrated = _currentConfiguration.IsAccelCalibrated;
                AccelCalibrationStatus = IsAccelCalibrated ? "Calibrated" : "Not calibrated";

                Compasses.Clear();
                foreach (var compass in _currentConfiguration.Compasses)
                    Compasses.Add(compass);

                IsLevelCalibrated = _currentConfiguration.IsLevelCalibrated;
                LevelCalibrationStatus = IsLevelCalibrated ? "Calibrated" : "Not calibrated";

                IsPressureCalibrated = _currentConfiguration.IsBaroCalibrated;
                PressureCalibrationStatus = IsPressureCalibrated ? "Calibrated" : "Not calibrated";

                var flow = _currentConfiguration.FlowSensor;
                SelectedFlowType = flow.FlowType;
                FlowXAxisScale = flow.XAxisScaleFactor;
                FlowYAxisScale = flow.YAxisScaleFactor;
                FlowYawAlignment = flow.SensorYawAlignment;
                IsFlowEnabled = flow.FlowType != FlowType.Disabled;

                StatusMessage = "Configuration loaded";
                AddDebugLog($"Loaded: Accel={IsAccelCalibrated}, Level={IsLevelCalibrated}, Compasses={Compasses.Count}");
            }

            UpdateButtonStates();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing");
            ShowError("Load Error", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// MissionPlanner-style accelerometer calibration button.
    /// - First click: Starts calibration (sends MAV_CMD_PREFLIGHT_CALIBRATION param5=1)
    /// - Subsequent clicks: Confirms position (sends MAV_CMD_ACCELCAL_VEHICLE_POS)
    /// </summary>
    [RelayCommand]
    private async Task CalibrateAccelerometerAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect to a vehicle first.");
            return;
        }

        if (!IsAccelerometerAvailable)
        {
            ShowError("Sensor Not Available", "Accelerometer sensor not detected.");
            return;
        }

        AddDebugLog($"[AccelCal] Button clicked - current button text: '{AccelButtonText}'");

        // Show disclaimer only when starting a NEW calibration session.
        // Using state is more reliable than matching button text.
        if (!IsAccelCalibrationActive)
        {
            var disclaimerResult = await ShowCalibrationDisclaimerAsync("Accelerometer");
            if (!disclaimerResult)
            {
                StatusMessage = "Calibration cancelled - disclaimer not accepted.";
                return;
            }
        }

        var success = await _calibrationService.StartAccelerometerCalibrationAsync(true);

        if (!success && !IsAccelCalibrationActive)
        {
            ShowError("Calibration Failed", "Failed to start accelerometer calibration. Check connection and try again.");
        }
    }

    /// <summary>
    /// Start onboard compass calibration (MissionPlanner style)
    /// </summary>
    [RelayCommand]
    private async Task StartOnboardCompassCalAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect to a vehicle first.");
            return;
        }

        if (!IsCompassAvailable)
        {
            ShowError("No Compass", "No compass sensors detected.");
            return;
        }

        // Show disclaimer before starting compass calibration
        var disclaimerResult = await ShowCalibrationDisclaimerAsync("Compass");
        if (!disclaimerResult)
        {
            StatusMessage = "Calibration cancelled - disclaimer not accepted.";
            return;
        }

        AddDebugLog("[CompassCal] Starting onboard compass calibration...");

        // Reset progress and UI
        ResetCompassCalibrationUI();

        CompassCalStatus = "Starting calibration...";
        CompassInstructions = "Preparing to calibrate. Please wait...";
        IsOnboardCompassCalActive = true;
        IsCompassCalibrationActive = true;
        CanAcceptCompassCal = false;
        CanCancelCompassCal = true;
        CanStartCompassCal = false;
        CompassCalButtonText = "Calibrating...";

        var success = await _calibrationService.StartOnboardCompassCalibrationAsync(0, true, true);

        if (!success)
        {
            ShowError("Calibration Failed", "Failed to start compass calibration. Check connection and try again.");
            IsOnboardCompassCalActive = false;
            IsCompassCalibrationActive = false;
            CanStartCompassCal = true;
            CompassCalButtonText = "Start Calibration";
            CompassCalStatus = "Failed to start calibration";
        }
    }

    /// <summary>
    /// Accept compass calibration
    /// </summary>
    [RelayCommand]
    private async Task AcceptCompassCalAsync()
    {
        AddDebugLog("[CompassCal] Accepting calibration...");
        var success = await _calibrationService.AcceptCompassCalibrationAsync();
        
        if (success)
        {
            CompassCalStatus = "Calibration accepted. Please reboot the autopilot.";
            CanAcceptCompassCal = false;
            CanCancelCompassCal = false;
            CanStartCompassCal = true;
            CompassCalButtonText = "Start Calibration";
            ShowRebootPromptDialog("Compass calibration accepted! Please reboot the autopilot to apply the new calibration values.");
        }
        else
        {
            ShowError("Accept Failed", "Failed to accept calibration.");
        }
    }

    /// <summary>
    /// Cancel compass calibration
    /// </summary>
    [RelayCommand]
    private async Task CancelCompassCalAsync()
    {
        AddDebugLog("[CompassCal] Cancelling calibration...");
        var success = await _calibrationService.CancelCompassCalibrationAsync();
        
        if (success)
        {
            CompassCalStatus = "Calibration cancelled.";
            IsOnboardCompassCalActive = false;
            IsCompassCalibrationActive = false;
            CanAcceptCompassCal = false;
            CanCancelCompassCal = false;
            CanStartCompassCal = true;
            CompassCalButtonText = "Start Calibration";
            
            // Reset progress
            ResetCompassCalibrationUI();
        }
    }

    /// <summary>
    /// Compass calibration (implementation pending)
    /// </summary>
    [RelayCommand]
    private async Task CalibrateCompassAsync()
    {
        await StartOnboardCompassCalAsync();
    }

    /// <summary>
    /// Cancel the current calibration
    /// </summary>
    [RelayCommand]
    private async Task CancelCalibrationAsync()
    {
        AddDebugLog("Cancelling calibration...");
        
        try
        {
            await _calibrationService.CancelCalibrationAsync();
            
            // Reset accelerometer calibration state
            IsAccelCalibrationActive = false;
            AccelButtonText = "Calibrate Accel";
            IsAccelButtonEnabled = true;
            AccelInstructions = "Calibration cancelled. Click 'Calibrate Accel' to start again.";
            ResetAllStepIndicators();
            
            // Reset level horizon calibration state
            IsLevelCalibrationActive = false;
            LevelCalibrationStatus = IsLevelCalibrated ? "Calibrated" : "Not calibrated";
            LevelInstructions = "Calibration cancelled. Place vehicle on a level surface and click Calibrate.";
            
            // Reset pressure/barometer calibration state
            IsPressureCalibrationActive = false;
            PressureCalibrationStatus = IsPressureCalibrated ? "Calibrated" : "Not calibrated";
            PressureInstructions = "Calibration cancelled. Click Calibrate to calibrate barometer.";
            PressureCalibrationProgress = 0;
            
            StatusMessage = "Calibration cancelled";
            UpdateButtonStates();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel calibration");
            ShowError("Cancel Failed", ex.Message);
        }
    }

    /// <summary>
    /// Reboot the flight controller.
    /// Similar to ResetParametersPageViewModel.RebootDroneAsync - sends reboot command
    /// then disconnects so App.axaml.cs can navigate to ConnectionShell.
    /// </summary>
    [RelayCommand]
    private async Task RebootAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect to a vehicle first.");
            return;
        }

        AddDebugLog("Sending reboot command...");
        StatusMessage = "Sending reboot command to drone...";
        
        try
        {
            // Send reboot command - this calls PrepareForReboot() internally
            // which stops all timers and notifies components
            _connectionService.SendPreflightReboot(1, 0);
            
            StatusMessage = "Reboot command sent. Disconnecting...";
            AddDebugLog("Reboot command sent, waiting before disconnect...");
            
            // Small delay to let the command be processed, then disconnect
            // The disconnect will be handled by App.axaml.cs which will navigate to ConnectionShell
            await Task.Delay(500);
            
            // Disconnect - this will trigger ConnectionStateChanged which App.axaml.cs handles
            await _connectionService.DisconnectAsync();
            
            AddDebugLog("Disconnected after reboot command");
            // Note: Navigation to ConnectionShell is handled by App.axaml.cs
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reboot command");
            StatusMessage = $"Reboot failed: {ex.Message}";
            ShowError("Reboot Failed", ex.Message);
        }
    }

    /// <summary>
    /// Set compass enabled/disabled state
    /// </summary>
    [RelayCommand]
    private async Task SetCompassEnabledAsync(CompassInfo? compass)
    {
        if (compass == null || !IsConnected)
            return;

        AddDebugLog($"Setting compass {compass.Priority} enabled={compass.IsEnabled}");
        
        try
        {
            await _sensorConfigService.SetCompassEnabledAsync(compass.Priority, compass.IsEnabled);
            StatusMessage = $"Compass {compass.Priority} {(compass.IsEnabled ? "enabled" : "disabled")}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set compass enabled state");
            ShowError("Error", ex.Message);
        }
    }

    /// <summary>
    /// Move compass up in priority order (UI only - does not write to FC immediately)
    /// </summary>
    [RelayCommand]
    private void MoveCompassUp(CompassInfo? compass)
    {
        if (compass == null || !IsConnected)
            return;

        var index = Compasses.IndexOf(compass);
        if (index <= 0)
            return;

        AddDebugLog($"Moving compass {compass.DisplayName} (DeviceID: {compass.DeviceId}) up in priority");
        
        try
        {
            Compasses.Move(index, index - 1);
            
            // Update displayed priorities
            for (int i = 0; i < Compasses.Count; i++)
            {
                Compasses[i].Priority = i + 1;
            }
            
            HasCompassPriorityChanges = true;
            StatusMessage = "Priority changed - click 'Update Priority' to save";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move compass up");
            ShowError("Error", ex.Message);
        }
    }

    /// <summary>
    /// Move compass down in priority order (UI only - does not write to FC immediately)
    /// </summary>
    [RelayCommand]
    private void MoveCompassDown(CompassInfo? compass)
    {
        if (compass == null || !IsConnected)
            return;

        var index = Compasses.IndexOf(compass);
        if (index < 0 || index >= Compasses.Count - 1)
            return;

        AddDebugLog($"Moving compass {compass.DisplayName} (DeviceID: {compass.DeviceId}) down in priority");
        
        try
        {
            Compasses.Move(index, index + 1);
            
            // Update displayed priorities
            for (int i = 0; i < Compasses.Count; i++)
            {
                Compasses[i].Priority = i + 1;
            }
            
            HasCompassPriorityChanges = true;
            StatusMessage = "Priority changed - click 'Update Priority' to save";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to move compass down");
            ShowError("Error", ex.Message);
        }
    }

    /// <summary>
    /// Update compass priorities on the flight controller.
    /// Writes COMPASS_PRIO1_ID, COMPASS_PRIO2_ID, COMPASS_PRIO3_ID parameters
    /// with the Device IDs of the compasses in the new priority order.
    /// </summary>
    [RelayCommand]
    private async Task UpdateCompassPriorityAsync()
    {
        if (!IsConnected || !HasCompassPriorityChanges)
            return;

        AddDebugLog("Updating compass priorities on FC...");
        AddDebugLog($"New order: {string.Join(", ", Compasses.Select(c => $"Prio{c.Priority}={c.DeviceId}"))}");
        
        try
        {
            IsBusy = true;
            StatusMessage = "Writing compass priorities to flight controller...";

            // Write new priorities to FC using Device IDs
            // The SensorConfigService.SetCompassPriorityAsync writes COMPASS_PRIOx_ID = DeviceId
            bool allSuccess = true;
            for (int i = 0; i < Compasses.Count && i < 3; i++)
            {
                var compass = Compasses[i];
                var success = await _sensorConfigService.SetCompassPriorityAsync(compass.Index, i);
                
                if (success)
                {
                    AddDebugLog($"Set COMPASS_PRIO{i + 1}_ID = {compass.DeviceId} (Compass {compass.Index})");
                }
                else
                {
                    AddDebugLog($"FAILED to set COMPASS_PRIO{i + 1}_ID for Compass {compass.Index}");
                    allSuccess = false;
                }
            }

            if (allSuccess)
            {
                // Mark changes as saved
                HasCompassPriorityChanges = false;
                StatusMessage = "Compass priorities saved to flight controller";
                AddDebugLog("All compass priorities updated successfully");

                // Prompt reboot
                ShowRebootPromptDialog("Compass priority changes saved!\n\nThe following parameters have been updated:\n" +
                    string.Join("\n", Compasses.Take(3).Select((c, i) => $"• COMPASS_PRIO{i + 1}_ID = {c.DeviceId}")) +
                    "\n\nPlease reboot the autopilot to apply the new settings.");
            }
            else
            {
                StatusMessage = "Some priorities failed to update";
                ShowError("Partial Failure", "Some compass priorities could not be updated. Check the debug log for details.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update compass prioridades");
            ShowError("Update Failed", $"Failed to update compass prioridades: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Calibrate level horizon (simple level calibration)
    /// </summary>
    [RelayCommand]
    private async Task CalibrateLevelHorizonAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect to a vehicle first.");
            return;
        }

        if (!IsAccelerometerAvailable)
        {
            ShowError("Sensor Not Available", "Accelerometer required for level horizon calibration.");
            return;
        }

        // Show disclaimer dialog before starting calibration
        var disclaimerResult = await ShowCalibrationDisclaimerAsync("Level Horizon");
        if (!disclaimerResult)
        {
            StatusMessage = "Calibration cancelled - disclaimer not accepted.";
            return;
        }

        AddDebugLog("Starting level horizon calibration...");
        LevelCalibrationStatus = "Calibrating...";
        LevelInstructions = "Keep the vehicle on a level surface - do not move...";
        IsLevelCalibrationActive = true;
        
        try
        {
            // Send the calibration command - result will come via CalibrationStateChanged event
            var commandSent = await _calibrationService.StartLevelHorizonCalibrationAsync();
            
            if (!commandSent)
            {
                LevelCalibrationStatus = "Failed to start";
                LevelInstructions = "Failed to send calibration command. Check connection and try again.";
                IsLevelCalibrationActive = false;
                ShowError("Calibration Failed", "Failed to start level horizon calibration.");
            }
            // If command was sent successfully, wait for FC response via CalibrationStateChanged event
            // The completion will be handled in OnCalibrationStateChanged handler
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Level horizon calibration failed");
            LevelCalibrationStatus = "Calibration failed";
            LevelInstructions = $"Error: {ex.Message}";
            IsLevelCalibrationActive = false;
            ShowError("Calibration Error", ex.Message);
        }
    }

    /// <summary>
    /// Calibrate barometer/pressure sensor
    /// </summary>
    [RelayCommand]
    private async Task CalibratePressureAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect to a vehicle first.");
            return;
        }

        if (!IsBarometerAvailable)
        {
            ShowError("Sensor Not Available", "Barometer sensor not detected.");
            return;
        }

        // Show disclaimer dialog before starting calibration
        var disclaimerResult = await ShowCalibrationDisclaimerAsync("Barometer");
        if (!disclaimerResult)
        {
            StatusMessage = "Calibration cancelled - disclaimer not accepted.";
            return;
        }

        AddDebugLog("Starting barometer calibration...");
        PressureCalibrationStatus = "Calibrating...";
        PressureInstructions = "Keep the vehicle still - calibrating barometer...";
        IsPressureCalibrationActive = true;
        PressureCalibrationProgress = 0;
        
        try
        {
            // Send the calibration command - result will come via CalibrationStateChanged event
            var commandSent = await _calibrationService.StartBarometerCalibrationAsync();
            
            if (!commandSent)
            {
                PressureCalibrationStatus = "Failed to start";
                PressureInstructions = "Failed to send calibration command. Check connection and try again.";
                IsPressureCalibrationActive = false;
                ShowError("Calibration Failed", "Failed to start barometer calibration.");
            }
            // If command was sent successfully, wait for FC response via CalibrationStateChanged event
            // The completion will be handled in OnCalibrationStateChanged handler
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Barometer calibration failed");
            PressureCalibrationStatus = "Calibration failed";
            PressureInstructions = $"Error: {ex.Message}";
            IsPressureCalibrationActive = false;
            ShowError("Calibration Error", ex.Message);
        }
    }

    /// <summary>
    /// Update flow sensor settings
    /// </summary>
    [RelayCommand]
    private async Task UpdateFlowSettingsAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect to a vehicle first.");
            return;
        }

        AddDebugLog("Updating flow sensor settings...");
        
        try
        {
            var settings = new FlowSensorSettings
            {
                FlowType = SelectedFlowType,
                XAxisScaleFactor = FlowXAxisScale,
                YAxisScaleFactor = FlowYAxisScale,
                SensorYawAlignment = FlowYawAlignment
            };

            await _sensorConfigService.UpdateFlowSettingsAsync(settings);
            StatusMessage = "Flow settings updated";
            AddDebugLog("Flow settings updated successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update flow settings");
            ShowError("Update Failed", ex.Message);
        }
    }

    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _mavLinkLogger.MessageLogged -= OnMavLinkMessageLogged;
            _calibrationService.CalibrationStateChanged -= OnCalibrationStateChanged;
            _calibrationService.CalibrationProgressChanged -= OnCalibrationProgressChanged;
            _calibrationService.StatusTextReceived -= OnCalibrationStatusTextReceived;
            _calibrationService.AccelCalPositionRequested -= OnAccelCalPositionRequested;
            _calibrationService.CompassCalProgressReceived -= OnCompassCalProgressReceived;
            _calibrationService.CompassCalReportReceived -= OnCompassCalReportReceived;
            _calibrationService.CompassCalibrationStateChanged -= OnCompassCalStateChanged;
        }
        base.Dispose(disposing);
    }
}

public class FlowTypeOption
{
    public FlowType Type { get; set; }
    public string Label { get; set; } = string.Empty;
}
