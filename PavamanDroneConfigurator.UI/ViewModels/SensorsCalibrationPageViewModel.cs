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

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for Sensors Calibration page with Mission Planner-style interface.
/// </summary>
public partial class SensorsCalibrationPageViewModel : ViewModelBase
{
    private readonly ILogger<SensorsCalibrationPageViewModel> _logger;
    private readonly ICalibrationService _calibrationService;
    private readonly ISensorConfigService _sensorConfigService;
    private readonly IConnectionService _connectionService;

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
    private bool _showDebugLogs;

    [ObservableProperty]
    private ObservableCollection<string> _debugLogs = new();

    #endregion

    #region Calibration Active States (Type-Specific)

    [ObservableProperty]
    private bool _isAccelCalibrationActive;

    [ObservableProperty]
    private bool _isCompassCalibrationActive;

    [ObservableProperty]
    private bool _isLevelCalibrationActive;

    [ObservableProperty]
    private bool _isPressureCalibrationActive;

    // The current calibration type being performed
    private CalibrationType? _activeCalibrationTyp;

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

    // Computed property for showing calibrate button
    public bool CanCalibrateAccelerometer => IsConnected && IsAccelerometerAvailable && !IsCalibrating;
    public bool CanCalibrateCompass => IsConnected && IsCompassAvailable && !IsCalibrating;
    public bool CanCalibrateLevelHorizon => IsConnected && IsAccelerometerAvailable && !IsCalibrating;
    public bool CanCalibrateBarometer => IsConnected && IsBarometerAvailable && !IsCalibrating;

    // Show calibration progress/controls for each type
    public bool ShowAccelCalibrationControls => IsAccelCalibrationActive;
    public bool ShowCompassCalibrationControls => IsCompassCalibrationActive;
    public bool ShowLevelCalibrationControls => IsLevelCalibrationActive;
    public bool ShowPressureCalibrationControls => IsPressureCalibrationActive;

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
    private string _accelInstructions = "To calibrate accelerometer please click on Calibrate button";

    [ObservableProperty]
    private int _accelCalibrationProgress;

    [ObservableProperty]
    private string _accelCurrentStep = string.Empty;

    [ObservableProperty]
    private int _accelStepNumber;

    [ObservableProperty]
    private string _currentCalibrationImage = "avares://PavamanDroneConfigurator.UI/Assets/Images/Caliberation-images/Level.png";

    // Step indicator properties
    [ObservableProperty] private bool _isStep1Active;
    [ObservableProperty] private bool _isStep1Complete;
    [ObservableProperty] private string _step1BorderColor = "#E2E8F0";
    [ObservableProperty] private string _step1BackgroundColor = "#F8FAFC";

    [ObservableProperty] private bool _isStep2Active;
    [ObservableProperty] private bool _isStep2Complete;
    [ObservableProperty] private string _step2BorderColor = "#E2E8F0";
    [ObservableProperty] private string _step2BackgroundColor = "#F8FAFC";

    [ObservableProperty] private bool _isStep3Active;
    [ObservableProperty] private bool _isStep3Complete;
    [ObservableProperty] private string _step3BorderColor = "#E2E8F0";
    [ObservableProperty] private string _step3BackgroundColor = "#F8FAFC";

    [ObservableProperty] private bool _isStep4Active;
    [ObservableProperty] private bool _isStep4Complete;
    [ObservableProperty] private string _step4BorderColor = "#E2E8F0";
    [ObservableProperty] private string _step4BackgroundColor = "#F8FAFC";

    [ObservableProperty] private bool _isStep5Active;
    [ObservableProperty] private bool _isStep5Complete;
    [ObservableProperty] private string _step5BorderColor = "#E2E8F0";
    [ObservableProperty] private string _step5BackgroundColor = "#F8FAFC";

    [ObservableProperty] private bool _isStep6Active;
    [ObservableProperty] private bool _isStep6Complete;
    [ObservableProperty] private string _step6BorderColor = "#E2E8F0";
    [ObservableProperty] private string _step6BackgroundColor = "#F8FAFC";

    // Track which steps have been validated/completed by FC
    private readonly HashSet<int> _validatedSteps = new();

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
    private string _compassInstructions = "Rotate vehicle in all directions during calibration";

    #endregion

    #region Level Horizon Properties

    [ObservableProperty]
    private string _levelCalibrationStatus = "Not calibrated";

    [ObservableProperty]
    private bool _isLevelCalibrated;

    [ObservableProperty]
    private string _levelInstructions = "To calibrate level please click on Calibrate button";

    #endregion

    #region Pressure Properties

    [ObservableProperty]
    private string _pressureCalibrationStatus = "Not calibrated";

    [ObservableProperty]
    private bool _isPressureCalibrated;

    [ObservableProperty]
    private string _pressureInstructions = "To calibrate pressure/barometer please click on Calibrate button";

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
        IConnectionService connectionService)
    {
        _logger = logger;
        _calibrationService = calibrationService;
        _sensorConfigService = sensorConfigService;
        _connectionService = connectionService;

        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _calibrationService.CalibrationStateChanged += OnCalibrationStateChanged;
        _calibrationService.CalibrationProgressChanged += OnCalibrationProgressChanged;
        _calibrationService.CalibrationStepRequired += OnCalibrationStepRequired;

        InitializeFlowTypeOptions();
        UpdateConnectionStatus(_connectionService.IsConnected);
        
        AddDebugLog("SensorsCalibrationPageViewModel initialized");
    }

    private void AddDebugLog(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var logEntry = $"[{timestamp}] {message}";
        
        Dispatcher.UIThread.Post(() =>
        {
            DebugLogs.Add(logEntry);
            // Keep only last 100 entries
            while (DebugLogs.Count > 100)
            {
                DebugLogs.RemoveAt(0);
            }
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
    private void CloseErrorDialog()
    {
        ShowErrorDialog = false;
    }

    [RelayCommand]
    private void ToggleDebugLogs()
    {
        ShowDebugLogs = !ShowDebugLogs;
    }

    [RelayCommand]
    private void ClearDebugLogs()
    {
        DebugLogs.Clear();
        AddDebugLog("Debug logs cleared");
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
        AddDebugLog($"Connection status: {ConnectionStatusText}");
        
        // Update button availability
        OnPropertyChanged(nameof(CanCalibrateAccelerometer));
        OnPropertyChanged(nameof(CanCalibrateCompass));
        OnPropertyChanged(nameof(CanCalibrateLevelHorizon));
        OnPropertyChanged(nameof(CanCalibrateBarometer));
    }

    private void UpdateSensorAvailability()
    {
        OnPropertyChanged(nameof(CanCalibrateAccelerometer));
        OnPropertyChanged(nameof(CanCalibrateCompass));
        OnPropertyChanged(nameof(CanCalibrateLevelHorizon));
        OnPropertyChanged(nameof(CanCalibrateBarometer));
    }

    partial void OnIsAccelerometerAvailableChanged(bool value) => UpdateSensorAvailability();
    partial void OnIsCompassAvailableChanged(bool value) => UpdateSensorAvailability();
    partial void OnIsBarometerAvailableChanged(bool value) => UpdateSensorAvailability();
    partial void OnIsCalibratingChanged(bool value) => UpdateSensorAvailability();
    
    // Update calibration control visibility when active states change
    partial void OnIsAccelCalibrationActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowAccelCalibrationControls));
        UpdateSensorAvailability();
    }
    
    partial void OnIsCompassCalibrationActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowCompassCalibrationControls));
        UpdateSensorAvailability();
    }
    
    partial void OnIsLevelCalibrationActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLevelCalibrationControls));
        UpdateSensorAvailability();
    }
    
    partial void OnIsPressureCalibrationActiveChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowPressureCalibrationControls));
        UpdateSensorAvailability();
    }

    private void UpdateStepIndicators(int step, bool markCurrentAsComplete = false)
    {
        var clampedStep = Math.Clamp(step, 0, 6);
        AccelStepNumber = clampedStep;
        
        // Mark previous step as complete if requested (when FC validates it)
        if (markCurrentAsComplete && clampedStep >= 1 && clampedStep <= 6)
        {
            _validatedSteps.Add(clampedStep);
            AddDebugLog($"Step {clampedStep} validated and marked complete by FC");
        }
        
        // Update active and complete states based on validated steps
        IsStep1Active = clampedStep == 1;
        IsStep1Complete = _validatedSteps.Contains(1);
        IsStep2Active = clampedStep == 2;
        IsStep2Complete = _validatedSteps.Contains(2);
        IsStep3Active = clampedStep == 3;
        IsStep3Complete = _validatedSteps.Contains(3);
        IsStep4Active = clampedStep == 4;
        IsStep4Complete = _validatedSteps.Contains(4);
        IsStep5Active = clampedStep == 5;
        IsStep5Complete = _validatedSteps.Contains(5);
        IsStep6Active = clampedStep == 6;
        IsStep6Complete = _validatedSteps.Contains(6);

        // Update colors: Red for active (waiting), Green for complete, Gray for pending
        // Step 1: Level
        Step1BorderColor = IsStep1Complete ? "#10B981" : (IsStep1Active ? "#EF4444" : "#E2E8F0");
        Step1BackgroundColor = IsStep1Complete ? "#D1FAE5" : (IsStep1Active ? "#FEE2E2" : "#F8FAFC");

        // Step 2: Left
        Step2BorderColor = IsStep2Complete ? "#10B981" : (IsStep2Active ? "#EF4444" : "#E2E8F0");
        Step2BackgroundColor = IsStep2Complete ? "#D1FAE5" : (IsStep2Active ? "#FEE2E2" : "#F8FAFC");

        // Step 3: Right
        Step3BorderColor = IsStep3Complete ? "#10B981" : (IsStep3Active ? "#EF4444" : "#E2E8F0");
        Step3BackgroundColor = IsStep3Complete ? "#D1FAE5" : (IsStep3Active ? "#FEE2E2" : "#F8FAFC");

        // Step 4: Nose Down
        Step4BorderColor = IsStep4Complete ? "#10B981" : (IsStep4Active ? "#EF4444" : "#E2E8F0");
        Step4BackgroundColor = IsStep4Complete ? "#D1FAE5" : (IsStep4Active ? "#FEE2E2" : "#F8FAFC");

        // Step 5: Nose Up
        Step5BorderColor = IsStep5Complete ? "#10B981" : (IsStep5Active ? "#EF4444" : "#E2E8F0");
        Step5BackgroundColor = IsStep5Complete ? "#D1FAE5" : (IsStep5Active ? "#FEE2E2" : "#F8FAFC");

        // Step 6: Back
        Step6BorderColor = IsStep6Complete ? "#10B981" : (IsStep6Active ? "#EF4444" : "#E2E8F0");
        Step6BackgroundColor = IsStep6Complete ? "#D1FAE5" : (IsStep6Active ? "#FEE2E2" : "#F8FAFC");

        // Update calibration image based on current step
        if (clampedStep >= 1 && clampedStep <= 6)
        {
            CurrentCalibrationImage = CalibrationImagePaths[clampedStep - 1];
        }
        
        AddDebugLog($"Step indicators updated: Step {clampedStep}, Validated: {string.Join(",", _validatedSteps)}, Image: {CurrentCalibrationImage}");
    }

    #region Tab Selection Changed

    partial void OnSelectedTabChanged(SensorCalibrationTab value)
    {
        IsAccelerometerTabSelected = value == SensorCalibrationTab.Accelerometer;
        IsCompassTabSelected = value == SensorCalibrationTab.Compass;
        IsLevelHorizonTabSelected = value == SensorCalibrationTab.LevelHorizon;
        IsPressureTabSelected = value == SensorCalibrationTab.Pressure;
        IsFlowTabSelected = value == SensorCalibrationTab.Flow;
        AddDebugLog($"Tab changed to: {value}");
    }

    #endregion

    #region Event Handlers

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateConnectionStatus(connected);
            if (connected)
            {
                _ = RefreshAsync();
            }
            else
            {
                // Reset sensor availability when disconnected
                IsAccelerometerAvailable = false;
                IsGyroscopeAvailable = false;
                IsCompassAvailable = false;
                IsBarometerAvailable = false;
                IsFlowSensorAvailable = false;
                
                AccelSensorStatus = "Not connected";
                GyroSensorStatus = "Not connected";
                CompassSensorStatus = "Not connected";
                BaroSensorStatus = "Not connected";
                
                if (IsCalibrating)
                {
                    ShowError("Connection Lost", "Connection lost during calibration. Please reconnect and try again.");
                    IsCalibrating = false;
                }
            }
        });
    }

    private void OnCalibrationStateChanged(object? sender, CalibrationStateModel state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsCalibrating = state.State == CalibrationState.InProgress;
            StatusMessage = state.Message ?? "Ready";
            AddDebugLog($"Calibration state: {state.State}, Type: {state.Type}, Progress: {state.Progress}%, StateMachine: {state.StateMachine}");

            // Check for position rejection
            if (state.Type == CalibrationType.Accelerometer && 
                state.StateMachine == CalibrationStateMachine.PositionRejected)
            {
                AddDebugLog("Position REJECTED by FC - showing error");
                ShowError("Incorrect Position", 
                    $"The flight controller rejected the position. Please ensure the drone is correctly placed in the {GetPositionName(state.CurrentPosition)} position as shown in the image, then click 'Click When In Position' again.");
            }

            // Update type-specific active states - ONLY the current type should be active
            if (state.State == CalibrationState.InProgress)
            {
                _activeCalibrationTyp = state.Type;
                IsAccelCalibrationActive = state.Type == CalibrationType.Accelerometer;
                IsCompassCalibrationActive = state.Type == CalibrationType.Compass;
                IsLevelCalibrationActive = state.Type == CalibrationType.LevelHorizon;
                IsPressureCalibrationActive = state.Type == CalibrationType.Barometer;
            }
            else
            {
                // Calibration ended - reset all active states
                _activeCalibrationTyp = null;
                IsAccelCalibrationActive = false;
                IsCompassCalibrationActive = false;
                IsLevelCalibrationActive = false;
                IsPressureCalibrationActive = false;
            }

            if (state.State == CalibrationState.Completed)
            {
                UpdateCalibrationStatusForType(state.Type, true);
                StatusMessage = state.Message ?? "Calibration completed successfully!";
                AddDebugLog($"Calibration {state.Type} completed successfully");
            }
            else if (state.State == CalibrationState.Failed)
            {
                UpdateCalibrationStatusForType(state.Type, false);
                StatusMessage = state.Message ?? "Calibration failed";
                
                // Check if it's a position error
                var message = state.Message?.ToLowerInvariant() ?? "";
                if (message.Contains("position") || message.Contains("orientation") || message.Contains("not level") || message.Contains("wrong"))
                {
                    ShowError("Incorrect Position", 
                        "The vehicle is not in the correct position. Please carefully place the drone in the required orientation as shown in the image and try again.");
                }
                else
                {
                    ShowError("Calibration Failed", state.Message ?? "Calibration failed. Please check the vehicle position and try again.");
                }
            }
        });
    }

    private string GetPositionName(int position)
    {
        return position switch
        {
            1 => "LEVEL",
            2 => "LEFT",
            3 => "RIGHT",
            4 => "NOSE DOWN",
            5 => "NOSE UP",
            6 => "BACK",
            _ => $"Position {position}"
        };
    }

    private void OnCalibrationProgressChanged(object? sender, CalibrationProgressEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AddDebugLog($"Calibration progress: {e.Type} - {e.ProgressPercent}% - Step {e.CurrentStep}/{e.TotalSteps} - StateMachine: {e.StateMachine}");
            
            if (e.Type == CalibrationType.Accelerometer)
            {
                AccelCalibrationProgress = e.ProgressPercent;
                AccelCurrentStep = e.StatusText ?? string.Empty;
                
                // Mark step as complete when FC is sampling (FC has validated the current position)
                // The sampling state indicates the FC accepted the position and is collecting data
                bool shouldMarkComplete = e.StateMachine == CalibrationStateMachine.Sampling;
                
                // If FC is sampling, the current step's position was validated, mark it complete
                if (shouldMarkComplete && e.CurrentStep.HasValue)
                {
                    UpdateStepIndicators(e.CurrentStep.Value, markCurrentAsComplete: true);
                }
                else
                {
                    UpdateStepIndicators(e.CurrentStep ?? 0, markCurrentAsComplete: false);
                }
            }
            else if (e.Type == CalibrationType.Compass)
            {
                CompassCalibrationProgress = e.ProgressPercent;
            }
            else if (e.Type == CalibrationType.Barometer)
            {
                // Show progress for barometer calibration
                PressureCalibrationProgress = e.ProgressPercent;
                StatusMessage = $"Barometer calibration: {e.ProgressPercent}% complete";
            }
        });
    }

    private void OnCalibrationStepRequired(object? sender, CalibrationStepEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AddDebugLog($"FC requesting step: {e.Type} - {e.Step} - {e.Instructions}");
            
            if (e.Type == CalibrationType.Accelerometer)
            {
                AccelInstructions = e.Instructions ?? "Follow the instructions from flight controller";
                
                // Update step number and image based on what FC is requesting
                int stepNumber = e.Step switch
                {
                    CalibrationStep.Level => 1,
                    CalibrationStep.LeftSide => 2,
                    CalibrationStep.RightSide => 3,
                    CalibrationStep.NoseDown => 4,
                    CalibrationStep.NoseUp => 5,
                    CalibrationStep.Back => 6,
                    _ => AccelStepNumber
                };
                
                UpdateStepIndicators(stepNumber);
            }
            else if (e.Type == CalibrationType.Compass)
            {
                CompassInstructions = e.Instructions ?? "Rotate vehicle in all directions";
            }
            else if (e.Type == CalibrationType.LevelHorizon)
            {
                LevelInstructions = e.Instructions ?? "Place vehicle on level surface";
            }
        });
    }

    private void UpdateCalibrationStatusForType(CalibrationType type, bool success)
    {
        switch (type)
        {
            case CalibrationType.Accelerometer:
                IsAccelCalibrated = success;
                AccelCalibrationStatus = success ? "Accelerometer is calibrated" : "Calibration failed";
                AccelInstructions = success
                    ? "Calibration completed successfully! Reboot recommended."
                    : "Calibration failed. Please try again.";
                AccelCalibrationProgress = success ? 100 : 0;
                if (success) UpdateStepIndicators(6, markCurrentAsComplete: true);
                break;

            case CalibrationType.Compass:
                CompassCalibrationStatus = success ? "Calibrated" : "Calibration failed";
                CompassInstructions = success
                    ? "Compass calibration completed!"
                    : "Compass calibration failed. Please try again.";
                CompassCalibrationProgress = success ? 100 : 0;
                break;

            case CalibrationType.LevelHorizon:
                IsLevelCalibrated = success;
                LevelCalibrationStatus = success ? "Level horizon calibrated" : "Calibration failed";
                LevelInstructions = success
                    ? "Level horizon calibration completed!"
                    : "Level calibration failed. Please try again.";
                break;

            case CalibrationType.Barometer:
                IsPressureCalibrated = success;
                PressureCalibrationStatus = success ? "Barometer calibrated" : "Calibration failed";
                PressureInstructions = success
                    ? "Barometer calibration completed!"
                    : "Barometer calibration failed. Please try again.";
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
            AddDebugLog("Refreshing sensor configuration...");

            _currentConfiguration = await _sensorConfigService.GetSensorConfigurationAsync();

            if (_currentConfiguration != null)
            {
                // Update sensor availability
                IsAccelerometerAvailable = _currentConfiguration.IsAccelAvailable;
                IsGyroscopeAvailable = _currentConfiguration.IsGyroAvailable;
                IsCompassAvailable = _currentConfiguration.Compasses.Any();
                IsBarometerAvailable = _currentConfiguration.IsBaroAvailable;
                IsFlowSensorAvailable = _currentConfiguration.FlowSensor.FlowType != FlowType.Disabled;

                // Update sensor status strings
                AccelSensorStatus = IsAccelerometerAvailable ? "Available" : "Not detected";
                GyroSensorStatus = IsGyroscopeAvailable ? "Available" : "Not detected";
                CompassSensorStatus = IsCompassAvailable ? $"{_currentConfiguration.Compasses.Count} detected" : "Not detected";
                BaroSensorStatus = IsBarometerAvailable ? "Available" : "Not detected";

                AddDebugLog($"Sensors: Accel={IsAccelerometerAvailable}, Gyro={IsGyroscopeAvailable}, Compass={IsCompassAvailable}, Baro={IsBarometerAvailable}");

                // Update calibration status
                IsAccelCalibrated = _currentConfiguration.IsAccelCalibrated;
                AccelCalibrationStatus = !IsAccelerometerAvailable 
                    ? "Sensor not available" 
                    : (IsAccelCalibrated ? "Accelerometer is calibrated" : "Not calibrated");
                AccelInstructions = !IsAccelerometerAvailable
                    ? "Accelerometer sensor not detected on this vehicle"
                    : (IsAccelCalibrated 
                        ? "Accelerometer calibration data found" 
                        : "To calibrate accelerometer please click on Calibrate button");

                Compasses.Clear();
                foreach (var compass in _currentConfiguration.Compasses)
                {
                    Compasses.Add(compass);
                }

                CompassCalibrationStatus = !IsCompassAvailable 
                    ? "No compass detected" 
                    : "Calibration required";

                IsLevelCalibrated = _currentConfiguration.IsLevelCalibrated;
                LevelCalibrationStatus = !IsAccelerometerAvailable
                    ? "Sensor not available"
                    : (IsLevelCalibrated ? "Level calibrated" : "Not calibrated");

                IsPressureCalibrated = _currentConfiguration.IsBaroCalibrated;
                PressureCalibrationStatus = !IsBarometerAvailable
                    ? "Sensor not available"
                    : (IsPressureCalibrated ? "Barometer calibrated" : "Not calibrated");
                PressureInstructions = !IsBarometerAvailable
                    ? "Barometer sensor not detected on this vehicle"
                    : "To calibrate pressure/barometer please click on Calibrate button";

                var flow = _currentConfiguration.FlowSensor;
                SelectedFlowType = flow.FlowType;
                FlowXAxisScale = flow.XAxisScaleFactor;
                FlowYAxisScale = flow.YAxisScaleFactor;
                FlowYawAlignment = flow.SensorYawAlignment;
                IsFlowEnabled = flow.FlowType != FlowType.Disabled;

                StatusMessage = "Sensor configuration loaded";
                AddDebugLog($"Configuration loaded: Accel={IsAccelCalibrated}, Level={IsLevelCalibrated}, Baro={IsPressureCalibrated}, Compasses={Compasses.Count}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing sensor configuration");
            ShowError("Load Error", $"Failed to load sensor configuration: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

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
            ShowError("Sensor Not Available", "Accelerometer sensor is not detected on this vehicle. Please check hardware connections.");
            return;
        }

        if (IsCalibrating)
        {
            ShowError("Calibration In Progress", "A calibration is already in progress. Please wait or cancel it first.");
            return;
        }

        try
        {
            AddDebugLog("Starting accelerometer calibration (6-axis)...");
            
            // Clear validated steps for new calibration
            _validatedSteps.Clear();
            
            AccelCalibrationProgress = 0;
            UpdateStepIndicators(1, markCurrentAsComplete: false);
            AccelInstructions = "Place vehicle LEVEL on a flat surface and click 'Click When In Position' when ready";
            
            var success = await _calibrationService.StartAccelerometerCalibrationAsync(fullSixAxis: true);
            if (!success)
            {
                ShowError("Calibration Error", "Failed to start accelerometer calibration. Check connection and try again.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting accelerometer calibration");
            ShowError("Calibration Error", $"Error starting accelerometer calibration: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task NextAccelStepAsync()
    {
        if (!IsCalibrating)
            return;

        AddDebugLog($"Advancing to next calibration step from step {AccelStepNumber}");
        await _calibrationService.AcceptCalibrationStepAsync();
    }

    [RelayCommand]
    private async Task CalibrateCompassAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect to a vehicle first.");
            return;
        }

        if (!IsCompassAvailable)
        {
            ShowError("Sensor Not Available", "No compass sensor is detected on this vehicle. Please check hardware connections.");
            return;
        }

        if (IsCalibrating)
        {
            ShowError("Calibration In Progress", "A calibration is already in progress. Please wait or cancel it first.");
            return;
        }

        try
        {
            AddDebugLog("Starting compass calibration...");
            CompassCalibrationProgress = 0;
            CompassCalibrationStatus = "Calibrating...";
            CompassInstructions = "Rotate vehicle slowly in all directions. Cover all orientations.";
            
            var success = await _calibrationService.StartCompassCalibrationAsync(onboardCalibration: false);
            if (!success)
            {
                ShowError("Calibration Error", "Failed to start compass calibration. Check connection and try again.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting compass calibration");
            ShowError("Calibration Error", $"Error starting compass calibration: {ex.Message}");
        }
    }

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
            ShowError("Sensor Not Available", "Accelerometer sensor is required for level horizon calibration but is not detected.");
            return;
        }

        if (IsCalibrating)
        {
            ShowError("Calibration In Progress", "A calibration is already in progress. Please wait or cancel it first.");
            return;
        }

        try
        {
            AddDebugLog("Starting level horizon calibration...");
            LevelCalibrationStatus = "Calibrating...";
            LevelInstructions = "Keep vehicle perfectly level on a flat surface...";
            
            var success = await _calibrationService.StartLevelHorizonCalibrationAsync();
            if (!success)
            {
                ShowError("Calibration Error", "Failed to start level horizon calibration. Check connection and try again.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting level horizon calibration");
            ShowError("Calibration Error", $"Error starting level horizon calibration: {ex.Message}");
        }
    }

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
            ShowError("Sensor Not Available", "Barometer sensor is not detected on this vehicle. Please check hardware connections.");
            return;
        }

        if (IsCalibrating)
        {
            ShowError("Calibration In Progress", "A calibration is already in progress. Please wait or cancel it first.");
            return;
        }

        try
        {
            AddDebugLog("Starting barometer calibration...");
            PressureCalibrationStatus = "Calibrating...";
            PressureInstructions = "Calibrating barometer ground pressure...";
            
            var success = await _calibrationService.StartBarometerCalibrationAsync();
            if (!success)
            {
                ShowError("Calibration Error", "Failed to start barometer calibration. Check connection and try again.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting barometer calibration");
            ShowError("Calibration Error", $"Error starting barometer calibration: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task CancelCalibrationAsync()
    {
        if (!IsCalibrating)
            return;

        AddDebugLog("Cancelling calibration...");
        await _calibrationService.CancelCalibrationAsync();
        StatusMessage = "Calibration cancelled";
        
        // Clear validated steps
        _validatedSteps.Clear();
        UpdateStepIndicators(0, markCurrentAsComplete: false);
        
        AccelInstructions = "Calibration cancelled. Click Calibrate to try again.";
        CompassInstructions = "Calibration cancelled. Click Calibrate to try again.";
    }

    [RelayCommand]
    private async Task RebootAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect to a vehicle first.");
            return;
        }

        try
        {
            AddDebugLog("Sending reboot command...");
            StatusMessage = "Rebooting flight controller...";
            var success = await _calibrationService.RebootFlightControllerAsync();
            if (success)
            {
                StatusMessage = "Reboot command sent - reconnect after reboot";
                AddDebugLog("Reboot command sent successfully");
            }
            else
            {
                ShowError("Reboot Error", "Failed to send reboot command. Check connection and try again.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rebooting");
            ShowError("Reboot Error", $"Error sending reboot command: {ex.Message}");
        }
    }

    #endregion

    #region Commands - Compass

    [RelayCommand]
    private async Task SetCompassEnabledAsync(CompassInfo? compass)
    {
        if (compass == null || !IsConnected) return;

        try
        {
            var newState = !compass.IsEnabled;
            AddDebugLog($"Setting compass {compass.Index} enabled: {newState}");
            var success = await _sensorConfigService.SetCompassEnabledAsync(compass.Index, newState);
            if (success)
            {
                compass.IsEnabled = newState;
                StatusMessage = $"Compass {compass.Index} {(newState ? "enabled" : "disabled")}";
            }
            else
            {
                ShowError("Compass Error", $"Failed to {(newState ? "enable" : "disable")} compass {compass.Index}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting compass enabled state");
            ShowError("Compass Error", $"Error: {ex.Message}");
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
            StatusMessage = $"Compass {compass.Index} priority increased";
            AddDebugLog($"Compass {compass.Index} moved up in priority");
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
            StatusMessage = $"Compass {compass.Index} priority decreased";
            AddDebugLog($"Compass {compass.Index} moved down in priority");
        }
    }

    #endregion

    #region Commands - Flow Sensor

    [RelayCommand]
    private async Task UpdateFlowSettingsAsync()
    {
        if (!IsConnected)
        {
            ShowError("Not Connected", "Please connect to a vehicle first.");
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Updating flow sensor settings...";
            AddDebugLog("Updating flow sensor settings...");

            var settings = new FlowSensorSettings
            {
                FlowType = SelectedFlowType,
                XAxisScaleFactor = FlowXAxisScale,
                YAxisScaleFactor = FlowYAxisScale,
                SensorYawAlignment = FlowYawAlignment
            };

            var success = await _sensorConfigService.UpdateFlowSettingsAsync(settings);
            if (success)
            {
                StatusMessage = "Flow settings updated successfully";
                AddDebugLog("Flow settings updated successfully");
            }
            else
            {
                ShowError("Update Error", "Failed to update flow settings");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating flow settings");
            ShowError("Update Error", $"Error updating flow settings: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
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
        }
        base.Dispose(disposing);
    }
}

public class FlowTypeOption
{
    public FlowType Type { get; set; }
    public string Label { get; set; } = string.Empty;
}
