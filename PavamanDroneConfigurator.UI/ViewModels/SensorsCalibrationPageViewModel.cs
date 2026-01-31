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
/// ViewModel for Sensors Calibration page.
/// IMU and Compass calibration implementations have been removed for fresh implementation.
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

    #region Accelerometer Properties (UI Only - Implementation Removed)

    [ObservableProperty]
    private string _accelCalibrationStatus = "Not calibrated";

    [ObservableProperty]
    private bool _isAccelCalibrated;

    [ObservableProperty]
    private string _accelInstructions = "IMU calibration implementation pending";

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

    [ObservableProperty]
    private string _accelButtonText = "Calibrate Accel";

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

    #region Compass Properties (UI Only - Implementation Removed)

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
        _mavLinkLogger.MessageLogged += OnMavLinkMessageLogged;

        InitializeFlowTypeOptions();
        UpdateConnectionStatus(_connectionService.IsConnected);
        
        AddDebugLog("ViewModel initialized - IMU/Compass calibration implementations pending");
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

    private void OnMavLinkMessageLogged(object? sender, MavLinkLogEntry e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            MavLinkMessages.Insert(0, e);
            if (MavLinkMessages.Count > 100)
                MavLinkMessages.RemoveAt(MavLinkMessages.Count - 1);
        });
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

    /// <summary>
    /// IMU/Accelerometer calibration - IMPLEMENTATION REMOVED
    /// TODO: Implement fresh IMU calibration logic
    /// </summary>
    [RelayCommand]
    private void CalibrateAccelerometer()
    {
        AddDebugLog("IMU calibration not yet implemented - pending fresh implementation");
        ShowError("Not Implemented", "IMU calibration implementation has been removed. Fresh implementation pending.");
    }

    /// <summary>
    /// Compass calibration - IMPLEMENTATION REMOVED
    /// TODO: Implement fresh compass calibration logic
    /// </summary>
    [RelayCommand]
    private void CalibrateCompass()
    {
        AddDebugLog("Compass calibration not yet implemented - pending fresh implementation");
        ShowError("Not Implemented", "Compass calibration implementation has been removed. Fresh implementation pending.");
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

    #region Commands - Compass (UI Only - Implementation Removed)

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
        }
        base.Dispose(disposing);
    }
}

public class FlowTypeOption
{
    public FlowType Type { get; set; }
    public string Label { get; set; } = string.Empty;
}
