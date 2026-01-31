using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.Services;

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
    private bool _showDebugLogs = true;

    [ObservableProperty]
    private ObservableCollection<string> _debugLogs = new();
    
    [ObservableProperty]
    private bool _showMavLinkLog = true;

    [ObservableProperty]
    private ObservableCollection<MavLinkLogEntry> _mavLinkMessages = new();

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
    /// MissionPlanner-style button text:
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
    private string _compassInstructions = "Compass calibration implementation pending";

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

        InitializeFlowTypeOptions();
        UpdateConnectionStatus(_connectionService.IsConnected);
        
        AddDebugLog("ViewModel initialized - MissionPlanner-style IMU calibration ready");
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
                    LevelInstructions = "Level calibration complete!";
                }
            }
            else if (e.Type == CalibrationType.Barometer)
            {
                PressureCalibrationStatus = e.Message;
                IsPressureCalibrationActive = e.State == CalibrationState.InProgress;
                
                if (e.State == CalibrationState.Completed)
                {
                    IsPressureCalibrated = true;
                    PressureInstructions = "Barometer calibration complete!";
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
        AddDebugLog($"[AccelCal] State: {e.State} SM: {e.StateMachine} Pos: {e.CurrentPosition} Msg: {e.Message}");

        AccelCalibrationStatus = e.Message;
        AccelCalibrationProgress = e.Progress;
        IsAccelCalibrationActive = e.State == CalibrationState.InProgress ||
                                   e.State == CalibrationState.WaitingForUserAction;

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
                break;

            case CalibrationState.Failed:
                // Reset to allow retry
                AccelButtonText = "Calibrate Accel";
                IsAccelButtonEnabled = true;
                AccelInstructions = $"Calibration failed: {e.Message}. Click to retry.";
                ResetAllStepIndicators();
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
        });
    }

    /// <summary>
    /// Handle FC requesting a specific vehicle position (MissionPlanner style).
    /// FC sends MAV_CMD_ACCELCAL_VEHICLE_POS with param1 = position (1-6).
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

            // Update step indicators to show current position as waiting
            UpdateStepIndicatorForPosition(e.Position);

            // Update calibration image based on position
            UpdateCalibrationImage(e.Position);

            // Enable button for user to confirm position
            IsAccelButtonEnabled = true;
            CanClickWhenInPosition = true;
        });
    }

    #endregion

    #region Step Indicator Helpers

    private void UpdateStepIndicatorForPosition(AccelCalVehiclePosition position)
    {
        // Reset all to pending first
        ResetAllStepIndicatorsToPending();

        // Set current position as waiting (red)
        switch (position)
        {
            case AccelCalVehiclePosition.Level:
                Step1BorderColor = WaitingBorderColor;
                Step1BackgroundColor = WaitingBackgroundColor;
                break;
            case AccelCalVehiclePosition.Left:
                Step1BorderColor = CompleteBorderColor;
                Step1BackgroundColor = CompleteBackgroundColor;
                Step2BorderColor = WaitingBorderColor;
                Step2BackgroundColor = WaitingBackgroundColor;
                break;
            case AccelCalVehiclePosition.Right:
                Step1BorderColor = CompleteBorderColor;
                Step1BackgroundColor = CompleteBackgroundColor;
                Step2BorderColor = CompleteBorderColor;
                Step2BackgroundColor = CompleteBackgroundColor;
                Step3BorderColor = WaitingBorderColor;
                Step3BackgroundColor = WaitingBackgroundColor;
                break;
            case AccelCalVehiclePosition.NoseDown:
                Step1BorderColor = CompleteBorderColor;
                Step1BackgroundColor = CompleteBackgroundColor;
                Step2BorderColor = CompleteBorderColor;
                Step2BackgroundColor = CompleteBackgroundColor;
                Step3BorderColor = CompleteBorderColor;
                Step3BackgroundColor = CompleteBackgroundColor;
                Step4BorderColor = WaitingBorderColor;
                Step4BackgroundColor = WaitingBackgroundColor;
                break;
            case AccelCalVehiclePosition.NoseUp:
                Step1BorderColor = CompleteBorderColor;
                Step1BackgroundColor = CompleteBackgroundColor;
                Step2BorderColor = CompleteBorderColor;
                Step2BackgroundColor = CompleteBackgroundColor;
                Step3BorderColor = CompleteBorderColor;
                Step3BackgroundColor = CompleteBackgroundColor;
                Step4BorderColor = CompleteBorderColor;
                Step4BackgroundColor = CompleteBackgroundColor;
                Step5BorderColor = WaitingBorderColor;
                Step5BackgroundColor = WaitingBackgroundColor;
                break;
            case AccelCalVehiclePosition.Back:
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
                Step6BorderColor = WaitingBorderColor;
                Step6BackgroundColor = WaitingBackgroundColor;
                break;
        }
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
        if (!IsConnected || !IsAccelerometerAvailable)
        {
            IsAccelButtonEnabled = false;
        }
        else if (AccelButtonText == "Done")
        {
            IsAccelButtonEnabled = false;
        }
        else if (!IsCalibrating)
        {
            IsAccelButtonEnabled = true;
        }
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

        // MissionPlanner behavior: If already calibrating, button click confirms position
        // CalibrationService.StartAccelerometerCalibrationAsync handles this internally
        var success = await _calibrationService.StartAccelerometerCalibrationAsync(true);

        if (!success && !IsAccelCalibrationActive)
        {
            ShowError("Calibration Failed", "Failed to start accelerometer calibration. Check connection and try again.");
        }
    }

    /// <summary>
    /// Compass calibration (implementation pending)
    /// </summary>
    [RelayCommand]
    private void CalibrateCompass()
    {
        AddDebugLog("Compass calibration not yet implemented");
        ShowError("Not Implemented", "Compass calibration implementation pending.");
    }

    /// <summary>
    /// Level horizon calibration (param5=2)
    /// </summary>
    [RelayCommand]
    private async Task CalibrateLevelHorizonAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect to a vehicle first.");
            return;
        }

        AddDebugLog("Starting level horizon calibration...");
        LevelInstructions = "Keep vehicle level and still during calibration...";
        
        var success = await _calibrationService.StartLevelHorizonCalibrationAsync();
        if (!success)
        {
            ShowError("Calibration Failed", "Failed to start level horizon calibration.");
        }
    }

    /// <summary>
    /// Barometer calibration (param3=1)
    /// </summary>
    [RelayCommand]
    private async Task CalibratePressureAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect to a vehicle first.");
            return;
        }

        AddDebugLog("Starting barometer calibration...");
        PressureInstructions = "Calibrating barometer... keep vehicle still.";
        
        var success = await _calibrationService.StartBarometerCalibrationAsync();
        if (!success)
        {
            ShowError("Calibration Failed", "Failed to start barometer calibration.");
        }
    }

    /// <summary>
    /// Cancel any active calibration
    /// </summary>
    [RelayCommand]
    private async Task CancelCalibrationAsync()
    {
        if (!IsCalibrating)
            return;

        AddDebugLog("Cancelling calibration...");
        await _calibrationService.CancelCalibrationAsync();
        
        // Reset UI
        AccelButtonText = "Calibrate Accel";
        IsAccelButtonEnabled = IsConnected && IsAccelerometerAvailable;
        AccelInstructions = "Calibration cancelled. Click 'Calibrate Accel' to start again.";
        ResetAllStepIndicators();
        
        StatusMessage = "Calibration cancelled";
    }

    /// <summary>
    /// Reboot flight controller (required after calibration)
    /// </summary>
    [RelayCommand]
    private async Task RebootAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect first.");
            return;
        }

        AddDebugLog("Rebooting flight controller...");
        StatusMessage = "Rebooting...";
        var success = await _calibrationService.RebootFlightControllerAsync();
        StatusMessage = success ? "Reboot command sent" : "Reboot failed";
    }

    #endregion

    #region Commands - Compass

    [RelayCommand]
    private async Task SetCompassEnabledAsync(CompassInfo? compass)
    {
        if (compass == null || !IsConnected) return;

        var newState = !compass.IsEnabled;
        var success = await _sensorConfigService.SetCompassEnabledAsync(compass.Index, newState);
        if (success)
        {
            compass.IsEnabled = newState;
            StatusMessage = $"Compass {compass.Index} {(newState ? "enabled" : "disabled")}";
        }
    }

    [RelayCommand]
    private async Task MoveCompassUpAsync(CompassInfo? compass)
    {
        if (compass == null || !IsConnected) return;

        var index = Compasses.IndexOf(compass);
        if (index > 0)
        {
            Compasses.Move(index, index - 1);
            await _sensorConfigService.SetCompassPriorityAsync(compass.Index, index - 1);
        }
    }

    [RelayCommand]
    private async Task MoveCompassDownAsync(CompassInfo? compass)
    {
        if (compass == null || !IsConnected) return;

        var index = Compasses.IndexOf(compass);
        if (index < Compasses.Count - 1)
        {
            Compasses.Move(index, index + 1);
            await _sensorConfigService.SetCompassPriorityAsync(compass.Index, index + 1);
        }
    }

    #endregion

    #region Commands - Flow Sensor

    [RelayCommand]
    private async Task UpdateFlowSettingsAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect first.");
            return;
        }

        IsBusy = true;
        var settings = new FlowSensorSettings
        {
            FlowType = SelectedFlowType,
            XAxisScaleFactor = FlowXAxisScale,
            YAxisScaleFactor = FlowYAxisScale,
            SensorYawAlignment = FlowYawAlignment
        };

        var success = await _sensorConfigService.UpdateFlowSettingsAsync(settings);
        StatusMessage = success ? "Flow settings updated" : "Failed to update";
        IsBusy = false;
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
        }
        base.Dispose(disposing);
    }
}

public class FlowTypeOption
{
    public FlowType Type { get; set; }
    public string Label { get; set; } = string.Empty;
}
