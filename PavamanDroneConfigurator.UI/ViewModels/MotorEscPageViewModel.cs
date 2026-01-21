using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for Motor Test and ESC Calibration page.
/// Provides individual motor testing with safety controls.
/// </summary>
public partial class MotorEscPageViewModel : ViewModelBase
{
    private readonly ILogger<MotorEscPageViewModel> _logger;
    private readonly IMotorEscService _motorEscService;
    private readonly IConnectionService _connectionService;

    #region Observable Properties

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _safetyAcknowledged;

    [ObservableProperty]
    private bool _slidersEnabled;
    
    [ObservableProperty]
    private bool _propellersRemoved;
    
    [ObservableProperty]
    private bool _vehicleSecured;
    
    [ObservableProperty]
    private bool _testAreaClear;
    
    [ObservableProperty]
    private bool _emergencyStopReady;
    
    [ObservableProperty]
    private bool _isVehicleArmed;
    
    [ObservableProperty]
    private int _motorCount = 6;

    [ObservableProperty]
    private float _globalThrottle = 5;

    [ObservableProperty]
    private float _testDuration = 2.0f;

    // Individual motor throttles
    [ObservableProperty]
    private float _motor1Throttle;

    [ObservableProperty]
    private float _motor2Throttle;

    [ObservableProperty]
    private float _motor3Throttle;

    [ObservableProperty]
    private float _motor4Throttle;

    [ObservableProperty]
    private float _motor5Throttle;

    [ObservableProperty]
    private float _motor6Throttle;

    [ObservableProperty]
    private float _motor7Throttle;

    [ObservableProperty]
    private float _motor8Throttle;

    // Motor test states
    [ObservableProperty]
    private bool _motor1Testing;

    [ObservableProperty]
    private bool _motor2Testing;

    [ObservableProperty]
    private bool _motor3Testing;

    [ObservableProperty]
    private bool _motor4Testing;

    [ObservableProperty]
    private bool _motor5Testing;

    [ObservableProperty]
    private bool _motor6Testing;

    [ObservableProperty]
    private bool _motor7Testing;

    [ObservableProperty]
    private bool _motor8Testing;

    // Motor/ESC Settings
    [ObservableProperty]
    private MotorOutputTypeOption? _selectedOutputType;

    [ObservableProperty]
    private int _pwmMin = 1000;

    [ObservableProperty]
    private int _pwmMax = 2000;

    [ObservableProperty]
    private float _spinArmed = 0.1f;

    [ObservableProperty]
    private float _spinMin = 0.15f;

    [ObservableProperty]
    private float _spinMax = 0.95f;

    [ObservableProperty]
    private float _thrustHover = 0.35f;

    [ObservableProperty]
    private bool _escCalibrationPending;

    [ObservableProperty]
    private bool _hasValidationWarnings;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    #endregion

    #region Collections

    public ObservableCollection<MotorOutputTypeOption> OutputTypeOptions { get; } = new();
    public ObservableCollection<string> ValidationWarnings { get; } = new();

    #endregion

    public MotorEscPageViewModel(
        ILogger<MotorEscPageViewModel> logger,
        IMotorEscService motorEscService,
        IConnectionService connectionService)
    {
        _logger = logger;
        _motorEscService = motorEscService;
        _connectionService = connectionService;

        // Subscribe to events
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _motorEscService.SettingsChanged += OnSettingsChanged;
        _motorEscService.MotorStatusChanged += OnMotorStatusChanged;
        _motorEscService.MotorTestCompleted += OnMotorTestCompleted;

        // Initialize options
        InitializeOptions();

        // Update connection state
        IsConnected = _connectionService.IsConnected;
    }

    private void InitializeOptions()
    {
        OutputTypeOptions.Add(new MotorOutputTypeOption { Type = MotorOutputType.Normal, Label = "Normal PWM" });
        OutputTypeOptions.Add(new MotorOutputTypeOption { Type = MotorOutputType.OneShot, Label = "OneShot125" });
        OutputTypeOptions.Add(new MotorOutputTypeOption { Type = MotorOutputType.OneShot42, Label = "OneShot42" });
        OutputTypeOptions.Add(new MotorOutputTypeOption { Type = MotorOutputType.DShot150, Label = "DShot150" });
        OutputTypeOptions.Add(new MotorOutputTypeOption { Type = MotorOutputType.DShot300, Label = "DShot300" });
        OutputTypeOptions.Add(new MotorOutputTypeOption { Type = MotorOutputType.DShot600, Label = "DShot600" });
        OutputTypeOptions.Add(new MotorOutputTypeOption { Type = MotorOutputType.DShot1200, Label = "DShot1200" });

        SelectedOutputType = OutputTypeOptions.FirstOrDefault();
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        IsConnected = connected;
        if (connected)
        {
            _ = RefreshAsync();
        }
        else
        {
            // Reset safety when disconnected
            SafetyAcknowledged = false;
            SlidersEnabled = false;
            PropellersRemoved = false;
            VehicleSecured = false;
            TestAreaClear = false;
            EmergencyStopReady = false;
        }
    }

    private void OnSettingsChanged(object? sender, MotorEscSettings settings)
    {
        UpdateFromSettings(settings);
    }

    private void OnMotorStatusChanged(object? sender, MotorStatus status)
    {
        // Update motor testing state
        switch (status.MotorNumber)
        {
            case 1: Motor1Testing = status.IsTesting; break;
            case 2: Motor2Testing = status.IsTesting; break;
            case 3: Motor3Testing = status.IsTesting; break;
            case 4: Motor4Testing = status.IsTesting; break;
            case 5: Motor5Testing = status.IsTesting; break;
            case 6: Motor6Testing = status.IsTesting; break;
            case 7: Motor7Testing = status.IsTesting; break;
            case 8: Motor8Testing = status.IsTesting; break;
        }
    }

    private void OnMotorTestCompleted(object? sender, (int MotorNumber, bool Success, string Message) result)
    {
        StatusMessage = result.Success 
            ? $"Motor {result.MotorNumber} test completed" 
            : $"Motor {result.MotorNumber} test failed: {result.Message}";
    }

    private void UpdateFromSettings(MotorEscSettings settings)
    {
        MotorCount = settings.MotorCount;
        SelectedOutputType = OutputTypeOptions.FirstOrDefault(o => o.Type == settings.PwmType);
        PwmMin = settings.PwmMin;
        PwmMax = settings.PwmMax;
        SpinArmed = settings.SpinArmed;
        SpinMin = settings.SpinMin;
        SpinMax = settings.SpinMax;
        ThrustHover = settings.ThrustHover;
        EscCalibrationPending = settings.EscCalibration != EscCalibrationMode.Disabled;

        ValidateSettings();
    }

    private void ValidateSettings()
    {
        var settings = BuildSettingsFromUI();
        var warnings = _motorEscService.ValidateSettings(settings);

        ValidationWarnings.Clear();
        foreach (var warning in warnings)
        {
            ValidationWarnings.Add(warning);
        }

        HasValidationWarnings = warnings.Count > 0;
        ValidationMessage = warnings.Count > 0
            ? string.Join("\n", warnings.Take(3))
            : "Settings are valid";
    }

    private MotorEscSettings BuildSettingsFromUI()
    {
        return new MotorEscSettings
        {
            MotorCount = MotorCount,
            PwmType = SelectedOutputType?.Type ?? MotorOutputType.Normal,
            PwmMin = PwmMin,
            PwmMax = PwmMax,
            SpinArmed = SpinArmed,
            SpinMin = SpinMin,
            SpinMax = SpinMax,
            ThrustHover = ThrustHover
        };
    }

    partial void OnSafetyAcknowledgedChanged(bool value)
    {
        _motorEscService.AcknowledgeSafetyWarning(value);
        SlidersEnabled = value;
        
        if (!value)
        {
            // Reset all throttles when safety is disabled
            Motor1Throttle = 0;
            Motor2Throttle = 0;
            Motor3Throttle = 0;
            Motor4Throttle = 0;
            Motor5Throttle = 0;
            Motor6Throttle = 0;
            Motor7Throttle = 0;
            Motor8Throttle = 0;
        }
    }
    
    partial void OnPropellersRemovedChanged(bool value) => OnPropertyChanged(nameof(CanAcknowledgeSafety));
    partial void OnVehicleSecuredChanged(bool value) => OnPropertyChanged(nameof(CanAcknowledgeSafety));
    partial void OnTestAreaClearChanged(bool value) => OnPropertyChanged(nameof(CanAcknowledgeSafety));
    partial void OnEmergencyStopReadyChanged(bool value) => OnPropertyChanged(nameof(CanAcknowledgeSafety));
    partial void OnIsVehicleArmedChanged(bool value)
    {
        OnPropertyChanged(nameof(CanAcknowledgeSafety));
        OnPropertyChanged(nameof(CanStartEscCalibration));
        
        // Auto-disable sliders if vehicle becomes armed
        if (value && SafetyAcknowledged)
        {
            SafetyAcknowledged = false;
            SlidersEnabled = false;
        }
    }
    
    partial void OnHasValidationWarningsChanged(bool value) => OnPropertyChanged(nameof(CanSaveSettings));
    
    [RelayCommand]
    private void AcknowledgeSafety()
    {
        if (!CanAcknowledgeSafety) return;
        
        SafetyAcknowledged = true;
        SlidersEnabled = true;
        _motorEscService.AcknowledgeSafetyWarning(true);
        StatusMessage = "Safety acknowledged - Motor testing enabled";
    }

    #region Motor Test Commands

    [RelayCommand]
    private async Task TestMotorAsync(int motorNumber)
    {
        if (!SlidersEnabled)
        {
            StatusMessage = "Enable motor sliders first (acknowledge safety)";
            return;
        }

        var throttle = GetMotorThrottle(motorNumber);
        if (throttle <= 0)
        {
            throttle = GlobalThrottle;
        }

        var request = new MotorTestRequest
        {
            MotorNumber = motorNumber,
            ThrottleType = MotorTestThrottleType.ThrottlePercent,
            ThrottleValue = throttle,
            DurationSeconds = TestDuration
        };

        StatusMessage = $"Testing motor {motorNumber} at {throttle}%...";
        await _motorEscService.StartMotorTestAsync(request);
    }

    [RelayCommand]
    private async Task StopMotorAsync(int motorNumber)
    {
        StatusMessage = $"Stopping motor {motorNumber}...";
        await _motorEscService.StopMotorTestAsync(motorNumber);
    }

    [RelayCommand]
    private async Task StopAllMotorsAsync()
    {
        StatusMessage = "Stopping all motors...";
        
        // Disable slider events temporarily to prevent re-triggering
        var wasSlidersEnabled = SlidersEnabled;
        SlidersEnabled = false;
        
        try
        {
            // Stop all motors via service
            await _motorEscService.StopAllMotorTestsAsync();
            
            // Reset all testing states
            Motor1Testing = false;
            Motor2Testing = false;
            Motor3Testing = false;
            Motor4Testing = false;
            Motor5Testing = false;
            Motor6Testing = false;
            
            // Reset all throttle values
            Motor1Throttle = 0;
            Motor2Throttle = 0;
            Motor3Throttle = 0;
            Motor4Throttle = 0;
            Motor5Throttle = 0;
            Motor6Throttle = 0;
            Motor7Throttle = 0;
            Motor8Throttle = 0;
            
            StatusMessage = "All motors stopped";
        }
        finally
        {
            // Re-enable sliders after a short delay to ensure stop commands are sent
            await Task.Delay(100);
            SlidersEnabled = wasSlidersEnabled && SafetyAcknowledged && !IsVehicleArmed;
        }
    }

    [RelayCommand]
    private async Task TestAllMotorsAsync()
    {
        if (!SlidersEnabled)
        {
            StatusMessage = "Enable motor sliders first (acknowledge safety)";
            return;
        }

        StatusMessage = "Testing all motors sequentially...";
        await _motorEscService.TestAllMotorsSequentialAsync(GlobalThrottle, TestDuration, 500);
        StatusMessage = "All motor tests complete";
    }

    private float GetMotorThrottle(int motorNumber)
    {
        return motorNumber switch
        {
            1 => Motor1Throttle,
            2 => Motor2Throttle,
            3 => Motor3Throttle,
            4 => Motor4Throttle,
            5 => Motor5Throttle,
            6 => Motor6Throttle,
            7 => Motor7Throttle,
            8 => Motor8Throttle,
            _ => 0
        };
    }

    // Motor slider value changed handlers - immediately start test when slider moves
    partial void OnMotor1ThrottleChanged(float value) => OnMotorThrottleChanged(1, value);
    partial void OnMotor2ThrottleChanged(float value) => OnMotorThrottleChanged(2, value);
    partial void OnMotor3ThrottleChanged(float value) => OnMotorThrottleChanged(3, value);
    partial void OnMotor4ThrottleChanged(float value) => OnMotorThrottleChanged(4, value);
    partial void OnMotor5ThrottleChanged(float value) => OnMotorThrottleChanged(5, value);
    partial void OnMotor6ThrottleChanged(float value) => OnMotorThrottleChanged(6, value);
    partial void OnMotor7ThrottleChanged(float value) => OnMotorThrottleChanged(7, value);
    partial void OnMotor8ThrottleChanged(float value) => OnMotorThrottleChanged(8, value);

    private void OnMotorThrottleChanged(int motorNumber, float value)
    {
        if (!SlidersEnabled || !IsConnected) return;
        
        // Don't send commands if sliders are being reset programmatically
        if (value == 0) return;
        
        // Send motor test command when slider value changes
        var request = new MotorTestRequest
        {
            MotorNumber = motorNumber,
            ThrottleType = MotorTestThrottleType.ThrottlePercent,
            ThrottleValue = value,
            DurationSeconds = 0.5f // Short duration, will be extended by continuous slider movement
        };

        _ = _motorEscService.StartMotorTestAsync(request);
    }

    #endregion

    #region ESC Calibration Commands

    [RelayCommand]
    private async Task StartEscCalibrationAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Starting ESC calibration...";

            var success = await _motorEscService.StartEscCalibrationAsync();
            
            if (success)
            {
                EscCalibrationPending = true;
                StatusMessage = "ESC calibration armed. Follow the steps below to complete.";
            }
            else
            {
                StatusMessage = "Failed to start ESC calibration";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting ESC calibration");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CancelEscCalibrationAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Cancelling ESC calibration...";

            var success = await _motorEscService.CancelEscCalibrationAsync();
            
            if (success)
            {
                EscCalibrationPending = false;
                StatusMessage = "ESC calibration cancelled";
            }
            else
            {
                StatusMessage = "Failed to cancel ESC calibration";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling ESC calibration");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region Settings Commands

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to vehicle";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Loading motor/ESC settings...";

            var settings = await _motorEscService.GetSettingsAsync();
            if (settings != null)
            {
                UpdateFromSettings(settings);
                StatusMessage = "Settings loaded successfully";
            }
            else
            {
                StatusMessage = "Failed to load settings";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing settings");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to vehicle";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Saving motor/ESC settings...";

            var settings = BuildSettingsFromUI();
            var success = await _motorEscService.UpdateSettingsAsync(settings);

            if (success)
            {
                StatusMessage = "Settings saved successfully";
            }
            else
            {
                StatusMessage = "Failed to save settings";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    /// <summary>
    /// Can acknowledge safety if all checks are completed and vehicle is disarmed
    /// </summary>
    public bool CanAcknowledgeSafety => PropellersRemoved && VehicleSecured && TestAreaClear && EmergencyStopReady && !IsVehicleArmed;
    
    /// <summary>
    /// Can start ESC calibration if vehicle is disarmed and not already calibrating
    /// </summary>
    public bool CanStartEscCalibration => IsConnected && !IsVehicleArmed && !EscCalibrationPending;
    
    /// <summary>
    /// Can save settings if validation passes
    /// </summary>
    public bool CanSaveSettings => IsConnected && !HasValidationWarnings;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _motorEscService.SettingsChanged -= OnSettingsChanged;
            _motorEscService.MotorStatusChanged -= OnMotorStatusChanged;
            _motorEscService.MotorTestCompleted -= OnMotorTestCompleted;
        }
        base.Dispose(disposing);
    }
}

#region Option Classes

public class MotorOutputTypeOption
{
    public MotorOutputType Type { get; set; }
    public string Label { get; set; } = string.Empty;
}

#endregion
