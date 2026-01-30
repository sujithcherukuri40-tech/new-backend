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

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for Sensors Calibration page - Mission Planner style.
/// Simple and direct: FC drives the workflow, we just display and forward user actions.
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
    private bool _showMavLinkLog = true; // Default: visible

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

    #region Accelerometer Properties

    [ObservableProperty]
    private string _accelCalibrationStatus = "Not calibrated";

    [ObservableProperty]
    private bool _isAccelCalibrated;

    [ObservableProperty]
    private string _accelInstructions = "Click Calibrate Accel to start 6-axis accelerometer calibration";

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
    /// MissionPlanner's button text: "Calibrate Accel" → "Click when Done" → "Done"
    /// </summary>
    [ObservableProperty]
    private string _accelButtonText = "Calibrate Accel";

    /// <summary>
    /// MissionPlanner: Button is disabled only when calibration completes
    /// </summary>
    [ObservableProperty]
    private bool _isAccelButtonEnabled = true;

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
    private string _compassInstructions = "Click Start to begin compass calibration";

    #endregion

    #region Level Horizon Properties

    [ObservableProperty]
    private string _levelCalibrationStatus = "Not calibrated";

    [ObservableProperty]
    private bool _isLevelCalibrated;

    [ObservableProperty]
    private string _levelInstructions = "Click Calibrate Level to calibrate level horizon";

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

    private static readonly string[] CalibrationImagePaths =
    {
        "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Level.png",
        "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Left-Side.png",
        "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Right-Side.png",
        "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Nose-Down.png",
        "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Nose-Up.png",
        "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Back-Side.png"
    };

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

        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _calibrationService.CalibrationStateChanged += OnCalibrationStateChanged;
        _calibrationService.CalibrationProgressChanged += OnCalibrationProgressChanged;
        _calibrationService.CalibrationStepRequired += OnCalibrationStepRequired;
        _calibrationService.StatusTextReceived += OnStatusTextReceived;
        _mavLinkLogger.MessageLogged += OnMavLinkMessageLogged;

        InitializeFlowTypeOptions();
        UpdateConnectionStatus(_connectionService.IsConnected);
        
        AddDebugLog("ViewModel initialized");
    }

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

    private void OnCalibrationStateChanged(object? sender, CalibrationStateModel state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsCalibrating = state.State == CalibrationState.InProgress;
            StatusMessage = state.Message ?? "Ready";
            CanClickWhenInPosition = state.CanConfirmPosition;
            
            AddDebugLog($"State: {state.State}, Type: {state.Type}, Position: {state.CurrentPosition}, CanConfirm: {state.CanConfirmPosition}");

            // Update type-specific active states
            IsAccelCalibrationActive = IsCalibrating && state.Type == CalibrationType.Accelerometer;
            IsCompassCalibrationActive = IsCalibrating && state.Type == CalibrationType.Compass;
            IsLevelCalibrationActive = IsCalibrating && state.Type == CalibrationType.LevelHorizon;
            IsPressureCalibrationActive = IsCalibrating && state.Type == CalibrationType.Barometer;

            // MissionPlanner button text logic for accelerometer
            if (state.Type == CalibrationType.Accelerometer)
            {
                if (state.State == CalibrationState.InProgress)
                {
                    // MissionPlanner: Button text = "Click when Done" during calibration
                    AccelButtonText = "Click when Done";
                    IsAccelButtonEnabled = true;  // Button stays enabled during calibration
                }
                else if (state.State == CalibrationState.Completed)
                {
                    // MissionPlanner: Button text = "Done", button disabled
                    AccelButtonText = "Done";
                    IsAccelButtonEnabled = false;
                    UpdateCalibrationComplete(state.Type, true, state.Message ?? "Calibration completed!");
                }
                else if (state.State == CalibrationState.Failed)
                {
                    // Reset button on failure
                    AccelButtonText = "Calibrate Accel";
                    IsAccelButtonEnabled = true;
                    UpdateCalibrationComplete(state.Type, false, state.Message ?? "Calibration failed");
                }
                else if (state.State == CalibrationState.Idle)
                {
                    // Reset to initial state
                    AccelButtonText = "Calibrate Accel";
                    IsAccelButtonEnabled = true;
                }
            }
            else
            {
                // Non-accelerometer calibrations
                if (state.State == CalibrationState.Completed)
                {
                    UpdateCalibrationComplete(state.Type, true, state.Message ?? "Calibration completed!");
                }
                else if (state.State == CalibrationState.Failed)
                {
                    UpdateCalibrationComplete(state.Type, false, state.Message ?? "Calibration failed");
                }
            }
        });
    }

    private void OnCalibrationProgressChanged(object? sender, CalibrationProgressEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AddDebugLog($"Progress: {e.Type} - {e.ProgressPercent}% - Step {e.CurrentStep}/{e.TotalSteps}");
            
            if (e.Type == CalibrationType.Accelerometer)
            {
                AccelCalibrationProgress = e.ProgressPercent;
                if (e.CurrentStep.HasValue)
                {
                    UpdateAccelStepIndicator(e.CurrentStep.Value, e.StateMachine);
                }
            }
            else if (e.Type == CalibrationType.Compass)
            {
                CompassCalibrationProgress = e.ProgressPercent;
            }
            else if (e.Type == CalibrationType.Barometer)
            {
                PressureCalibrationProgress = e.ProgressPercent;
            }
        });
    }

    private void OnCalibrationStepRequired(object? sender, CalibrationStepEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AddDebugLog($"Step required: {e.Type} - {e.Step} - CanConfirm: {e.CanConfirm}");
            
            if (e.Type == CalibrationType.Accelerometer)
            {
                AccelInstructions = e.Instructions ?? "Follow the instructions";
                CanClickWhenInPosition = e.CanConfirm;
                
                int stepNum = e.Step switch
                {
                    CalibrationStep.Level => 1,
                    CalibrationStep.LeftSide => 2,
                    CalibrationStep.RightSide => 3,
                    CalibrationStep.NoseDown => 4,
                    CalibrationStep.NoseUp => 5,
                    CalibrationStep.Back => 6,
                    _ => AccelStepNumber
                };
                
                UpdateAccelStepIndicator(stepNum, CalibrationStateMachine.WaitingForUserPosition);
            }
            else if (e.Type == CalibrationType.Compass)
            {
                CompassInstructions = e.Instructions ?? "Rotate vehicle in all directions";
            }
            else if (e.Type == CalibrationType.LevelHorizon)
            {
                LevelInstructions = e.Instructions ?? "Keep vehicle level";
            }
        });
    }

    private void OnStatusTextReceived(object? sender, CalibrationStatusTextEventArgs e)
    {
        AddDebugLog($"FC: {e.Text}");
    }
    
    private void OnMavLinkMessageLogged(object? sender, MavLinkLogEntry e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MavLinkMessages.Insert(0, e); // Newest first
            if (MavLinkMessages.Count > 100)
                MavLinkMessages.RemoveAt(MavLinkMessages.Count - 1);
        });
    }

    private void UpdateAccelStepIndicator(int currentStep, CalibrationStateMachine state)
    {
        AccelStepNumber = currentStep;
        
        // Update current step name
        AccelCurrentStep = currentStep switch
        {
            1 => "LEVEL",
            2 => "LEFT",
            3 => "RIGHT",
            4 => "NOSE DOWN",
            5 => "NOSE UP",
            6 => "BACK",
            _ => ""
        };
        
        // Update image
        if (currentStep >= 1 && currentStep <= 6)
        {
            CurrentCalibrationImage = CalibrationImagePaths[currentStep - 1];
        }
        
        // Determine colors based on step state
        // Waiting (red): user needs to place drone
        // Complete (green): FC validated position
        // Pending (gray): not yet reached
        
        bool isWaiting = state == CalibrationStateMachine.WaitingForUserPosition ||
                        state == CalibrationStateMachine.PositionRejected;
        
        // Helper to get colors for a step
        void SetStepColors(int step, out string border, out string background)
        {
            if (step < currentStep)
            {
                // Completed step (green)
                border = "#10B981";
                background = "#D1FAE5";
            }
            else if (step == currentStep && isWaiting)
            {
                // Current waiting step (red)
                border = "#EF4444";
                background = "#FEE2E2";
            }
            else if (step == currentStep)
            {
                // Current sampling step (orange)
                border = "#F59E0B";
                background = "#FEF3C7";
            }
            else
            {
                // Pending step (gray)
                border = "#E2E8F0";
                background = "#F8FAFC";
            }
        }
        
        SetStepColors(1, out var b1, out var bg1);
        Step1BorderColor = b1; Step1BackgroundColor = bg1;
        
        SetStepColors(2, out var b2, out var bg2);
        Step2BorderColor = b2; Step2BackgroundColor = bg2;
        
        SetStepColors(3, out var b3, out var bg3);
        Step3BorderColor = b3; Step3BackgroundColor = bg3;
        
        SetStepColors(4, out var b4, out var bg4);
        Step4BorderColor = b4; Step4BackgroundColor = bg4;
        
        SetStepColors(5, out var b5, out var bg5);
        Step5BorderColor = b5; Step5BackgroundColor = bg5;
        
        SetStepColors(6, out var b6, out var bg6);
        Step6BorderColor = b6; Step6BackgroundColor = bg6;
    }

    private void UpdateCalibrationComplete(CalibrationType type, bool success, string message)
    {
        switch (type)
        {
            case CalibrationType.Accelerometer:
                IsAccelCalibrated = success;
                AccelCalibrationStatus = success ? "Calibrated" : "Failed";
                AccelInstructions = message;
                AccelCalibrationProgress = success ? 100 : 0;
                if (success) UpdateAccelStepIndicator(6, CalibrationStateMachine.Completed);
                break;
            case CalibrationType.Compass:
                CompassCalibrationStatus = success ? "Calibrated" : "Failed";
                CompassInstructions = message;
                CompassCalibrationProgress = success ? 100 : 0;
                break;
            case CalibrationType.LevelHorizon:
                IsLevelCalibrated = success;
                LevelCalibrationStatus = success ? "Calibrated" : "Failed";
                LevelInstructions = message;
                break;
            case CalibrationType.Barometer:
                IsPressureCalibrated = success;
                PressureCalibrationStatus = success ? "Calibrated" : "Failed";
                PressureInstructions = message;
                break;
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
            AddDebugLog("Refreshing...");

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

    [RelayCommand]
    private async Task CalibrateAccelerometerAsync()
    {
        if (!CanCalibrateAccelerometer && !IsCalibrating)
        {
            ShowError("Cannot Calibrate", "Check connection and sensor availability.");
            return;
        }

        // MissionPlanner behavior: Same button for start AND confirm
        // If calibrating AND can confirm → send position confirmation
        // If not calibrating → start calibration
        
        if (IsCalibrating && IsAccelCalibrationActive)
        {
            // Already calibrating - this is position confirmation (MissionPlanner's second click)
            if (CanClickWhenInPosition)
            {
                AddDebugLog($"User clicked 'Click when Done' - confirming position {AccelStepNumber}");
                AddDebugLog($"Sending COMMAND_LONG(ACCELCAL_VEHICLE_POS, param1={AccelStepNumber})");
                
                var success = await _calibrationService.AcceptCalibrationStepAsync();
                if (!success)
                {
                    AddDebugLog("Position confirmation failed - FC may not have requested a position yet");
                }
            }
            else
            {
                AddDebugLog("Cannot confirm - waiting for FC to request position");
            }
            return;
        }

        // Start new calibration (MissionPlanner's first click)
        AddDebugLog("=== Starting accelerometer calibration ===");
        AddDebugLog("Sending MAV_CMD_PREFLIGHT_CALIBRATION (param5=1)");
        
        // Reset UI
        AccelCalibrationProgress = 0;
        AccelStepNumber = 0;
        AccelCurrentStep = "";
        CanClickWhenInPosition = false;
        ResetStepColors();
        
        // Show waiting message (NOT a hardcoded position)
        AccelInstructions = "Starting calibration... waiting for FC instructions";
        AccelButtonText = "Click when Done";  // Change button text immediately
        
        var result = await _calibrationService.StartAccelerometerCalibrationAsync(fullSixAxis: true);
        if (!result)
        {
            ShowError("Start Failed", "Failed to start calibration. Check vehicle is disarmed.");
            AccelInstructions = "Calibration failed to start";
            AccelButtonText = "Calibrate Accel";  // Reset button
        }
        else
        {
            AddDebugLog("Calibration command sent, _incalibrate = true");
            AddDebugLog("Subscribed to STATUSTEXT and COMMAND_LONG");
        }
    }
    
    private void ResetStepColors()
    {
        // Reset all steps to pending (gray)
        Step1BorderColor = "#E2E8F0"; Step1BackgroundColor = "#F8FAFC";
        Step2BorderColor = "#E2E8F0"; Step2BackgroundColor = "#F8FAFC";
        Step3BorderColor = "#E2E8F0"; Step3BackgroundColor = "#F8FAFC";
        Step4BorderColor = "#E2E8F0"; Step4BackgroundColor = "#F8FAFC";
        Step5BorderColor = "#E2E8F0"; Step5BackgroundColor = "#F8FAFC";
        Step6BorderColor = "#E2E8F0"; Step6BackgroundColor = "#F8FAFC";
    }

    [RelayCommand]
    private async Task NextAccelStepAsync()
    {
        // This is now handled by CalibrateAccelerometerAsync when button is clicked during calibration
        // But keep this for backwards compatibility with any existing UI bindings
        if (!IsCalibrating || !CanClickWhenInPosition)
        {
            AddDebugLog("NextAccelStepAsync: Cannot confirm (IsCalibrating={IsCalibrating}, CanConfirm={CanClickWhenInPosition})");
            return;
        }

        AddDebugLog($"NextAccelStepAsync: Confirming position {AccelStepNumber}");
        var success = await _calibrationService.AcceptCalibrationStepAsync();
        
        if (!success)
        {
            AddDebugLog("Position confirmation failed");
        }
    }

    [RelayCommand]
    private async Task CalibrateCompassAsync()
    {
        if (!CanCalibrateCompass)
        {
            ShowError("Cannot Calibrate", "Check connection and sensor availability.");
            return;
        }

        AddDebugLog("Starting compass calibration...");
        CompassCalibrationProgress = 0;
        CompassInstructions = "Starting...";
        
        var success = await _calibrationService.StartCompassCalibrationAsync(onboardCalibration: false);
        if (!success)
        {
            ShowError("Start Failed", "Failed to start compass calibration.");
        }
    }

    [RelayCommand]
    private async Task CalibrateLevelHorizonAsync()
    {
        if (!CanCalibrateLevelHorizon)
        {
            ShowError("Cannot Calibrate", "Check connection and sensor availability.");
            return;
        }

        AddDebugLog("Starting level horizon calibration...");
        LevelInstructions = "Calibrating...";
        
        var success = await _calibrationService.StartLevelHorizonCalibrationAsync();
        if (!success)
        {
            ShowError("Start Failed", "Failed to start level horizon calibration.");
        }
    }

    [RelayCommand]
    private async Task CalibratePressureAsync()
    {
        if (!CanCalibrateBarometer)
        {
            ShowError("Cannot Calibrate", "Check connection and sensor availability.");
            return;
        }

        AddDebugLog("Starting barometer calibration...");
        PressureInstructions = "Calibrating...";
        
        var success = await _calibrationService.StartBarometerCalibrationAsync();
        if (!success)
        {
            ShowError("Start Failed", "Failed to start barometer calibration.");
        }
    }

    [RelayCommand]
    private async Task CancelCalibrationAsync()
    {
        if (!IsCalibrating)
            return;

        AddDebugLog("Cancelling calibration...");
        await _calibrationService.CancelCalibrationAsync();
        StatusMessage = "Cancelled";
    }

    [RelayCommand]
    private async Task RebootAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect first.");
            return;
        }

        AddDebugLog("Rebooting FC...");
        StatusMessage = "Rebooting...";
        var success = await _calibrationService.RebootFlightControllerAsync();
        StatusMessage = success ? "Reboot sent" : "Reboot failed";
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
            _calibrationService.CalibrationStateChanged -= OnCalibrationStateChanged;
            _calibrationService.CalibrationProgressChanged -= OnCalibrationProgressChanged;
            _calibrationService.CalibrationStepRequired -= OnCalibrationStepRequired;
            _calibrationService.StatusTextReceived -= OnStatusTextReceived;
            _mavLinkLogger.MessageLogged -= OnMavLinkMessageLogged;
        }
        base.Dispose(disposing);
    }
}

public class FlowTypeOption
{
    public FlowType Type { get; set; }
    public string Label { get; set; } = string.Empty;
}
