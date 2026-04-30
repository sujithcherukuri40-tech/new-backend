using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.UI.ViewModels;

public sealed partial class PowerPageViewModel : ViewModelBase
{
    private readonly IParameterService _parameterService;
    private readonly IConnectionService _connectionService;
    private readonly PavamanDroneConfigurator.UI.Services.ParameterChangeLogService? _paramLogService;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private bool _isSyncing;
    private bool _disposed;

    #region Parameter Names (ArduPilot)
    
    // Battery Monitor
    private const string ParamBattMonitor = "BATT_MONITOR";
    private const string ParamBattCapacity = "BATT_CAPACITY";
    private const string ParamBattArmVolt = "BATT_ARM_VOLT";
    
    // Voltage/Current Pins
    private const string ParamBattVoltPin = "BATT_VOLT_PIN";
    private const string ParamBattCurrPin = "BATT_CURR_PIN";
    
    // Calibration
    private const string ParamBattVoltMult = "BATT_VOLT_MULT";
    private const string ParamBattAmpPerVolt = "BATT_AMP_PERVLT";
    private const string ParamBattAmpOffset = "BATT_AMP_OFFSET";
    
    #endregion

    #region Option Collections - From ArduPilot/Mission Planner
    
    // BATT_MONITOR options from ArduPilot parameters
    private static readonly ReadOnlyCollection<PowerOption> BatteryMonitorOptions = new(
    [
        new PowerOption(0, "Disabled"),
        new PowerOption(3, "Analog Voltage Only"),
        new PowerOption(4, "Analog V and I"),
        new PowerOption(5, "Solo"),
        new PowerOption(6, "Bebop"),
        new PowerOption(7, "SMBus-Generic"),
        new PowerOption(8, "DroneCAN-BatteryInfo"),
        new PowerOption(9, "ESC"),
        new PowerOption(10, "Sum Of Selected Monitors"),
        new PowerOption(11, "FuelFlow"),
        new PowerOption(12, "FuelLevel (PWM)"),
        new PowerOption(13, "SMBUS-SUI3"),
        new PowerOption(14, "SMBUS-SUI6"),
        new PowerOption(15, "NeoDesign"),
        new PowerOption(16, "SMBus-Maxell"),
        new PowerOption(17, "Generator-Elec"),
        new PowerOption(18, "Generator-Fuel"),
        new PowerOption(19, "Rotoye"),
        new PowerOption(20, "MPPT"),
        new PowerOption(21, "INA2XX"),
        new PowerOption(22, "LTC2946"),
        new PowerOption(23, "Torqeedo"),
        new PowerOption(24, "FuelLevel (Analog)"),
        new PowerOption(25, "Synthetic Current and Analog Voltage"),
        new PowerOption(26, "INA239_SPI"),
        new PowerOption(27, "EFI"),
        new PowerOption(28, "AD7091R5"),
        new PowerOption(29, "Scripting")
    ]);

    // Power sensor presets (common configurations)
    private static readonly ReadOnlyCollection<PowerSensorPreset> PowerSensorPresets = new(
    [
        new PowerSensorPreset("Other", 0, 0, 0, 0, 0),
        new PowerSensorPreset("AttoPilot 45A", 13.6f, 27.3f, 0, -1, -1),
        new PowerSensorPreset("AttoPilot 90A", 13.6f, 13.7f, 0, -1, -1),
        new PowerSensorPreset("AttoPilot 180A", 13.6f, 7.37f, 0, -1, -1),
        new PowerSensorPreset("3DR Power Module", 10.1f, 17.0f, 0, -1, -1),
        new PowerSensorPreset("CUAV HV PM", 18.0f, 24.0f, 0, -1, -1),
        new PowerSensorPreset("CubeOrange", 11.0f, 40.0f, 0, 14, 13),
        new PowerSensorPreset("CubeBlack", 11.0f, 17.0f, 0, 3, 2),
        new PowerSensorPreset("Pixhawk1", 10.1f, 17.0f, 0, 3, 2),
        new PowerSensorPreset("Pixhawk4", 10.1f, 17.0f, 0, -1, -1),
        new PowerSensorPreset("Holybro PM02", 10.1f, 24.0f, 0, -1, -1),
        new PowerSensorPreset("Holybro PM06", 18.182f, 36.364f, 0, -1, -1),
        new PowerSensorPreset("Holybro PM07", 18.182f, 36.364f, 0, -1, -1),
        new PowerSensorPreset("Mauch HS 050A", 10.0f, 25.0f, 0, -1, -1),
        new PowerSensorPreset("Mauch HS 100A", 10.0f, 25.0f, 0, -1, -1),
        new PowerSensorPreset("Mauch HS 200A", 10.0f, 12.5f, 0, -1, -1),
        new PowerSensorPreset("Mauch PL 200A", 10.0f, 12.5f, 0, -1, -1)
    ]);

    // Voltage Pin options (hardware dependent)
    private static readonly ReadOnlyCollection<PowerOption> VoltagePinOptions = new(
    [
        new PowerOption(-1, "Disabled"),
        new PowerOption(0, "AUX 1"),
        new PowerOption(1, "AUX 2"),
        new PowerOption(2, "Pixhawk Power (BATT)"),
        new PowerOption(3, "Pixhawk Aux 1"),
        new PowerOption(13, "Cube Aux 1"),
        new PowerOption(14, "CubeOrange Power"),
        new PowerOption(100, "First 101 to check"),
        new PowerOption(101, "Board ID-specific")
    ]);

    // Current Pin options (hardware dependent)
    private static readonly ReadOnlyCollection<PowerOption> CurrentPinOptions = new(
    [
        new PowerOption(-1, "Disabled"),
        new PowerOption(0, "AUX 1"),
        new PowerOption(1, "AUX 2"),
        new PowerOption(3, "Pixhawk Aux 2"),
        new PowerOption(4, "Cube Aux 2"),
        new PowerOption(13, "CubeOrange Aux 2"),
        new PowerOption(14, "CubeOrange Current"),
        new PowerOption(100, "First 101 to check"),
        new PowerOption(101, "Board ID-specific")
    ]);

    #endregion

    #region Public Option Collections
    
    public IReadOnlyList<PowerOption> BatteryMonitorTypeOptions => BatteryMonitorOptions;
    public IReadOnlyList<PowerSensorPreset> PowerSensorOptions => PowerSensorPresets;
    public IReadOnlyList<PowerOption> VoltagePinTypeOptions => VoltagePinOptions;
    public IReadOnlyList<PowerOption> CurrentPinTypeOptions => CurrentPinOptions;
    
    #endregion

    #region Observable Properties - Page State
    
    [ObservableProperty] private bool _isPageEnabled;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Downloading parameters...";
    
    #endregion

    #region Observable Properties - Battery Monitor
    
    [ObservableProperty] private PowerOption? _selectedBatteryMonitor;
    [ObservableProperty] private float _batteryCapacity = 16000f;
    [ObservableProperty] private float _minimumArmingVoltage = 44.0f;
    
    #endregion

    #region Observable Properties - Power Sensor
    
    [ObservableProperty] private PowerSensorPreset? _selectedPowerSensor;
    [ObservableProperty] private PowerOption? _selectedCurrentPin;
    [ObservableProperty] private PowerOption? _selectedVoltagePin;
    
    #endregion

    #region Observable Properties - Calibration
    
    [ObservableProperty] private float _voltageMultiplier = 12.02f;
    [ObservableProperty] private float _ampsPerVolt = 39.877f;
    [ObservableProperty] private float _ampsOffset = 0f;
    
    #endregion

    #region Observable Properties - Measured Values (for calculation display)
    
    [ObservableProperty] private float _measuredVoltage = 0f;
    [ObservableProperty] private float _calculatedVoltage = 0f;
    [ObservableProperty] private float _measuredCurrent = 0f;
    [ObservableProperty] private float _calculatedCurrent = 0f;
    
    #endregion

    public PowerPageViewModel(
        IParameterService parameterService,
        IConnectionService connectionService,
        PavamanDroneConfigurator.UI.Services.ParameterChangeLogService? paramLogService = null)
    {
        _parameterService = parameterService;
        _connectionService = connectionService;
        _paramLogService = paramLogService;

        _parameterService.ParameterDownloadProgressChanged += OnParameterDownloadProgressChanged;
        _parameterService.ParameterUpdated += OnParameterUpdated;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;

        InitializeState();
    }

    private void InitializeState()
    {
        IsPageEnabled = _parameterService.IsParameterDownloadComplete && _connectionService.IsConnected;
        StatusMessage = IsPageEnabled ? "Power parameters loaded." : "Downloading parameters...";

        if (IsPageEnabled)
        {
            RunSafe(SyncAllFromCacheAsync);
        }
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsPageEnabled = connected && _parameterService.IsParameterDownloadComplete;
            if (IsPageEnabled)
            {
                StatusMessage = "Power parameters loaded.";
                RunSafe(SyncAllFromCacheAsync);
            }
            else
            {
                StatusMessage = connected ? "Downloading parameters..." : "Disconnected - parameters unavailable";
            }
        });
    }

    private void OnParameterDownloadProgressChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_parameterService.IsParameterDownloadComplete && _connectionService.IsConnected)
            {
                IsPageEnabled = true;
                StatusMessage = "Power parameters loaded.";
                RunSafe(SyncAllFromCacheAsync);
            }
            else if (_parameterService.IsParameterDownloadInProgress)
            {
                var expected = _parameterService.ExpectedParameterCount?.ToString() ?? "?";
                StatusMessage = $"Downloading parameters... {_parameterService.ReceivedParameterCount}/{expected}";
                IsPageEnabled = false;
            }
        });
    }

    private void OnParameterUpdated(object? sender, string parameterName)
    {
        if (!IsPageEnabled) return;
        
        Dispatcher.UIThread.Post(() =>
        {
            var syncTask = parameterName.ToUpperInvariant() switch
            {
                var p when p == ParamBattMonitor.ToUpperInvariant() => SyncBatteryMonitorAsync(),
                var p when p == ParamBattCapacity.ToUpperInvariant() => SyncBatterySettingsAsync(),
                var p when p == ParamBattArmVolt.ToUpperInvariant() => SyncBatterySettingsAsync(),
                var p when p == ParamBattVoltPin.ToUpperInvariant() => SyncPinSettingsAsync(),
                var p when p == ParamBattCurrPin.ToUpperInvariant() => SyncPinSettingsAsync(),
                var p when p == ParamBattVoltMult.ToUpperInvariant() => SyncCalibrationAsync(),
                var p when p == ParamBattAmpPerVolt.ToUpperInvariant() => SyncCalibrationAsync(),
                var p when p == ParamBattAmpOffset.ToUpperInvariant() => SyncCalibrationAsync(),
                _ => Task.CompletedTask
            };
            
            if (syncTask != Task.CompletedTask)
                RunSafe(() => syncTask);
        });
    }

    #region Sync Methods
    
    private async Task SyncAllFromCacheAsync()
    {
        await SyncBatteryMonitorAsync();
        await SyncBatterySettingsAsync();
        await SyncPinSettingsAsync();
        await SyncCalibrationAsync();
    }

    private async Task SyncBatteryMonitorAsync()
    {
        var param = await _parameterService.GetParameterAsync(ParamBattMonitor);
        if (param == null) return;

        _isSyncing = true;
        try
        {
            var value = (int)Math.Round(param.Value);
            SelectedBatteryMonitor = BatteryMonitorOptions.FirstOrDefault(o => o.Value == value);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncBatterySettingsAsync()
    {
        _isSyncing = true;
        try
        {
            var capacity = await _parameterService.GetParameterAsync(ParamBattCapacity);
            if (capacity != null) BatteryCapacity = capacity.Value;

            var armVolt = await _parameterService.GetParameterAsync(ParamBattArmVolt);
            if (armVolt != null) MinimumArmingVoltage = armVolt.Value;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncPinSettingsAsync()
    {
        _isSyncing = true;
        try
        {
            var voltPin = await _parameterService.GetParameterAsync(ParamBattVoltPin);
            if (voltPin != null)
            {
                var value = (int)Math.Round(voltPin.Value);
                SelectedVoltagePin = VoltagePinOptions.FirstOrDefault(o => o.Value == value);
            }

            var currPin = await _parameterService.GetParameterAsync(ParamBattCurrPin);
            if (currPin != null)
            {
                var value = (int)Math.Round(currPin.Value);
                SelectedCurrentPin = CurrentPinOptions.FirstOrDefault(o => o.Value == value);
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncCalibrationAsync()
    {
        _isSyncing = true;
        try
        {
            var voltMult = await _parameterService.GetParameterAsync(ParamBattVoltMult);
            if (voltMult != null) VoltageMultiplier = voltMult.Value;

            var ampPerVolt = await _parameterService.GetParameterAsync(ParamBattAmpPerVolt);
            if (ampPerVolt != null) AmpsPerVolt = ampPerVolt.Value;

            var ampOffset = await _parameterService.GetParameterAsync(ParamBattAmpOffset);
            if (ampOffset != null) AmpsOffset = ampOffset.Value;
        }
        finally
        {
            _isSyncing = false;
        }
    }
    
    #endregion

    #region Write Methods
    
    private bool CanWrite() => _connectionService.IsConnected && _parameterService.IsParameterDownloadComplete;

    private async Task<bool> WriteParameterAsync(string name, float value)
    {
        if (!CanWrite())
        {
            StatusMessage = "Cannot write - connection unavailable or download incomplete.";
            return false;
        }

        var result = await _parameterService.SetParameterAsync(name, value);
        if (result && _paramLogService != null)
            _ = _paramLogService.LogAsync(name, 0f, value, "Power");
        return result;
    }

    private async Task ExecuteWriteAsync(Func<Task> operation)
    {
        await _writeLock.WaitAsync();
        IsBusy = true;
        try
        {
            await operation();
        }
        finally
        {
            IsBusy = false;
            _writeLock.Release();
        }
    }
    
    #endregion

    #region Property Change Handlers
    
    partial void OnSelectedBatteryMonitorChanged(PowerOption? value)
    {
        if (_isSyncing || value == null || !IsPageEnabled) return;
        RunSafe(() => ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(ParamBattMonitor, value.Value);
            StatusMessage = success ? "Battery monitor updated." : "Failed to update battery monitor.";
        }));
    }

    partial void OnBatteryCapacityChanged(float value)
    {
        if (_isSyncing || !IsPageEnabled) return;
        RunSafe(() => ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(ParamBattCapacity, value);
            StatusMessage = success ? "Battery capacity updated." : "Failed to update battery capacity.";
        }));
    }

    partial void OnMinimumArmingVoltageChanged(float value)
    {
        if (_isSyncing || !IsPageEnabled) return;
        RunSafe(() => ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(ParamBattArmVolt, value);
            StatusMessage = success ? "Minimum arming voltage updated." : "Failed to update minimum arming voltage.";
        }));
    }

    partial void OnSelectedPowerSensorChanged(PowerSensorPreset? value)
    {
        if (_isSyncing || value == null || !IsPageEnabled) return;
        
        // Apply preset values
        RunSafe(async () =>
        {
            _isSyncing = true;
            try
            {
                // Only apply non-zero preset values
                if (value.VoltageMultiplier > 0)
                    VoltageMultiplier = value.VoltageMultiplier;
                if (value.AmpsPerVolt > 0)
                    AmpsPerVolt = value.AmpsPerVolt;
                
                AmpsOffset = value.AmpsOffset;
            }
            finally
            {
                _isSyncing = false;
            }

            await ExecuteWriteAsync(async () =>
            {
                if (value.VoltageMultiplier > 0)
                    await WriteParameterAsync(ParamBattVoltMult, value.VoltageMultiplier);
                if (value.AmpsPerVolt > 0)
                    await WriteParameterAsync(ParamBattAmpPerVolt, value.AmpsPerVolt);
                
                await WriteParameterAsync(ParamBattAmpOffset, value.AmpsOffset);
                
                // Apply pin settings if preset specifies them
                if (value.VoltagePin >= 0)
                    await WriteParameterAsync(ParamBattVoltPin, value.VoltagePin);
                if (value.CurrentPin >= 0)
                    await WriteParameterAsync(ParamBattCurrPin, value.CurrentPin);

                StatusMessage = $"Power sensor preset '{value.Name}' applied.";
            });

            // Refresh pin selections
            await SyncPinSettingsAsync();
        });
    }

    partial void OnSelectedCurrentPinChanged(PowerOption? value)
    {
        if (_isSyncing || value == null || !IsPageEnabled) return;
        RunSafe(() => ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(ParamBattCurrPin, value.Value);
            StatusMessage = success ? "Current pin updated." : "Failed to update current pin.";
        }));
    }

    partial void OnSelectedVoltagePinChanged(PowerOption? value)
    {
        if (_isSyncing || value == null || !IsPageEnabled) return;
        RunSafe(() => ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(ParamBattVoltPin, value.Value);
            StatusMessage = success ? "Voltage pin updated." : "Failed to update voltage pin.";
        }));
    }

    partial void OnVoltageMultiplierChanged(float value)
    {
        if (_isSyncing || !IsPageEnabled) return;
        RunSafe(() => ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(ParamBattVoltMult, value);
            StatusMessage = success ? "Voltage multiplier updated." : "Failed to update voltage multiplier.";
        }));
    }

    partial void OnAmpsPerVoltChanged(float value)
    {
        if (_isSyncing || !IsPageEnabled) return;
        RunSafe(() => ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(ParamBattAmpPerVolt, value);
            StatusMessage = success ? "Amps per volt updated." : "Failed to update amps per volt.";
        }));
    }

    partial void OnAmpsOffsetChanged(float value)
    {
        if (_isSyncing || !IsPageEnabled) return;
        RunSafe(() => ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(ParamBattAmpOffset, value);
            StatusMessage = success ? "Amps offset updated." : "Failed to update amps offset.";
        }));
    }
    
    #endregion

    #region Commands
    
    [RelayCommand]
    private void CalculateVoltage()
    {
        // Voltage calibration calculation
        // If user knows actual voltage, they can enter it to calculate the correct multiplier
        if (MeasuredVoltage > 0 && CalculatedVoltage > 0)
        {
            // New multiplier = (Measured/Calculated) * Current multiplier
            var newMultiplier = (MeasuredVoltage / CalculatedVoltage) * VoltageMultiplier;
            VoltageMultiplier = (float)Math.Round(newMultiplier, 4);
        }
        else
        {
            StatusMessage = "Enter measured and calculated voltages for calibration.";
        }
    }

    [RelayCommand]
    private void CalculateCurrent()
    {
        // Current calibration calculation
        if (MeasuredCurrent > 0 && CalculatedCurrent > 0)
        {
            // New AmpsPerVolt = (Measured/Calculated) * Current AmpsPerVolt
            var newAmpsPerVolt = (MeasuredCurrent / CalculatedCurrent) * AmpsPerVolt;
            AmpsPerVolt = (float)Math.Round(newAmpsPerVolt, 3);
        }
        else
        {
            StatusMessage = "Enter measured and calculated currents for calibration.";
        }
    }

    [RelayCommand]
    private async Task UpdateAllAsync()
    {
        if (!IsPageEnabled) return;

        await ExecuteWriteAsync(async () =>
        {
            StatusMessage = "Updating power settings...";

            var allSuccess = true;

            if (SelectedBatteryMonitor != null)
                allSuccess &= await WriteParameterAsync(ParamBattMonitor, SelectedBatteryMonitor.Value);
            
            allSuccess &= await WriteParameterAsync(ParamBattCapacity, BatteryCapacity);
            allSuccess &= await WriteParameterAsync(ParamBattArmVolt, MinimumArmingVoltage);

            if (SelectedVoltagePin != null)
                allSuccess &= await WriteParameterAsync(ParamBattVoltPin, SelectedVoltagePin.Value);
            if (SelectedCurrentPin != null)
                allSuccess &= await WriteParameterAsync(ParamBattCurrPin, SelectedCurrentPin.Value);

            allSuccess &= await WriteParameterAsync(ParamBattVoltMult, VoltageMultiplier);
            allSuccess &= await WriteParameterAsync(ParamBattAmpPerVolt, AmpsPerVolt);
            allSuccess &= await WriteParameterAsync(ParamBattAmpOffset, AmpsOffset);

            StatusMessage = allSuccess ? "All power settings updated." : "Some settings failed to update.";
        });
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsPageEnabled) return;

        IsBusy = true;
        StatusMessage = "Refreshing power parameters...";
        try
        {
            await SyncAllFromCacheAsync();
            StatusMessage = "Power parameters refreshed.";
        }
        finally
        {
            IsBusy = false;
        }
    }
    
    #endregion

    #region Helpers
    
    private void RunSafe(Func<Task> asyncAction)
    {
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await asyncAction();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
                StatusMessage = "Power operation failed. See logs for details.";
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            _parameterService.ParameterDownloadProgressChanged -= OnParameterDownloadProgressChanged;
            _parameterService.ParameterUpdated -= OnParameterUpdated;
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _writeLock.Dispose();
        }
        base.Dispose(disposing);
    }
    
    #endregion
}

public sealed record PowerOption(int Value, string Label);

public sealed record PowerSensorPreset(
    string Name,
    float VoltageMultiplier,
    float AmpsPerVolt,
    float AmpsOffset,
    int VoltagePin,
    int CurrentPin);
