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

public sealed partial class SprayingConfigPageViewModel : ViewModelBase
{
    private readonly IParameterService _parameterService;
    private readonly IConnectionService _connectionService;
    private readonly PavamanDroneConfigurator.UI.Services.ParameterChangeLogService? _paramLogService;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private bool _isSyncing;
    private bool _disposed;

    #region Parameter Names

    // Spraying Enable
    private const string ParamSprayEnable = "SPRAY_ENABLE";
    private const string ParamSpraySpinner = "SPRAY_SPINNER";  // RC Switch
    private const string ParamBrdPwmCount = "BRD_PWM_COUNT";

    // Servo9 (AUX 1) - Typically for spray pump
    private const string ParamServo9Function = "SERVO9_FUNCTION";
    private const string ParamServo9Min = "SERVO9_MIN";
    private const string ParamServo9Max = "SERVO9_MAX";
    private const string ParamServo9Trim = "SERVO9_TRIM";
    private const string ParamServo9Reversed = "SERVO9_REVERSED";

    // Servo10 (AUX 2) - Typically for spinner/nozzle
    private const string ParamServo10Function = "SERVO10_FUNCTION";
    private const string ParamServo10Min = "SERVO10_MIN";
    private const string ParamServo10Max = "SERVO10_MAX";
    private const string ParamServo10Trim = "SERVO10_TRIM";
    private const string ParamServo10Reversed = "SERVO10_REVERSED";

    // Flow meter calibration
    private const string ParamSprayPumpRate = "SPRAY_PUMP_RATE";
    private const string ParamSpraySpinnerRate = "SPRAY_SPEED_MIN";

    #endregion

    #region Option Collections

    private static readonly ReadOnlyCollection<SprayingEnableOption> SprayingEnableOptions = new(
    [
        new SprayingEnableOption(0, "Disable"),
        new SprayingEnableOption(1, "Enable")
    ]);

    private static readonly ReadOnlyCollection<CalibrationVolumeOption> CalibrationVolumeOptions = new(
    [
        new CalibrationVolumeOption(1, "1 Litre"),
        new CalibrationVolumeOption(2, "2 Litres"),
        new CalibrationVolumeOption(5, "5 Litres"),
        new CalibrationVolumeOption(10, "10 Litres")
    ]);

    #endregion

    #region Public Option Collections

    public IReadOnlyList<SprayingEnableOption> EnableSprayingOptions => SprayingEnableOptions;
    public IReadOnlyList<CalibrationVolumeOption> FlowMeterCalibrationOptions => CalibrationVolumeOptions;

    #endregion

    #region Observable Properties - Page State

    [ObservableProperty] private bool _isPageEnabled;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Downloading parameters...";
    [ObservableProperty] private bool _isCalibrating;
    [ObservableProperty] private string _calibrationStatus = string.Empty;

    #endregion

    #region Observable Properties - Spraying Configuration

    [ObservableProperty] private SprayingEnableOption? _selectedEnableSpraying;
    [ObservableProperty] private float _rcSwitch;
    [ObservableProperty] private float _brdPwmCount;

    #endregion

    #region Observable Properties - Servo9 (AUX 1)

    [ObservableProperty] private float _servo9Function = 22;
    [ObservableProperty] private float _servo9PwmMin = 1000;
    [ObservableProperty] private float _servo9PwmMax = 2000;

    #endregion

    #region Observable Properties - Servo10 (AUX 2)

    [ObservableProperty] private float _servo10Function = -1;
    [ObservableProperty] private float _servo10PwmMin = 1000;
    [ObservableProperty] private float _servo10PwmMax = 2000;

    #endregion

    #region Observable Properties - Flow Meter Calibration

    [ObservableProperty] private CalibrationVolumeOption? _selectedCalibrationVolume;
    [ObservableProperty] private float _pumpRate;
    [ObservableProperty] private float _spinnerMinSpeed;

    #endregion

    public SprayingConfigPageViewModel(
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

        // Set default calibration volume
        _selectedCalibrationVolume = CalibrationVolumeOptions.First();

        InitializeState();
    }

    private void InitializeState()
    {
        IsPageEnabled = _parameterService.IsParameterDownloadComplete && _connectionService.IsConnected;
        StatusMessage = IsPageEnabled ? "Spraying parameters loaded." : "Downloading parameters...";

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
                StatusMessage = "Spraying parameters loaded.";
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
                StatusMessage = "Spraying parameters loaded.";
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
                var p when p == ParamSprayEnable.ToUpperInvariant() => SyncSprayingEnableAsync(),
                var p when p == ParamSpraySpinner.ToUpperInvariant() => SyncSprayingConfigAsync(),
                var p when p == ParamBrdPwmCount.ToUpperInvariant() => SyncSprayingConfigAsync(),
                var p when p == ParamServo9Function.ToUpperInvariant() => SyncServo9Async(),
                var p when p == ParamServo9Min.ToUpperInvariant() => SyncServo9Async(),
                var p when p == ParamServo9Max.ToUpperInvariant() => SyncServo9Async(),
                var p when p == ParamServo10Function.ToUpperInvariant() => SyncServo10Async(),
                var p when p == ParamServo10Min.ToUpperInvariant() => SyncServo10Async(),
                var p when p == ParamServo10Max.ToUpperInvariant() => SyncServo10Async(),
                _ => Task.CompletedTask
            };

            if (syncTask != Task.CompletedTask)
                RunSafe(() => syncTask);
        });
    }

    #region Sync Methods

    private async Task SyncAllFromCacheAsync()
    {
        await SyncSprayingEnableAsync();
        await SyncSprayingConfigAsync();
        await SyncServo9Async();
        await SyncServo10Async();
        await SyncFlowMeterAsync();
    }

    private async Task SyncSprayingEnableAsync()
    {
        var param = await _parameterService.GetParameterAsync(ParamSprayEnable);
        if (param == null) return;

        _isSyncing = true;
        try
        {
            var value = (int)Math.Round(param.Value);
            SelectedEnableSpraying = SprayingEnableOptions.FirstOrDefault(o => o.Value == value)
                ?? SprayingEnableOptions.First();
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncSprayingConfigAsync()
    {
        _isSyncing = true;
        try
        {
            var rcSwitch = await _parameterService.GetParameterAsync(ParamSpraySpinner);
            if (rcSwitch != null) RcSwitch = rcSwitch.Value;

            var pwmCount = await _parameterService.GetParameterAsync(ParamBrdPwmCount);
            if (pwmCount != null) BrdPwmCount = pwmCount.Value;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncServo9Async()
    {
        _isSyncing = true;
        try
        {
            var function = await _parameterService.GetParameterAsync(ParamServo9Function);
            if (function != null) Servo9Function = function.Value;

            var min = await _parameterService.GetParameterAsync(ParamServo9Min);
            if (min != null) Servo9PwmMin = min.Value;

            var max = await _parameterService.GetParameterAsync(ParamServo9Max);
            if (max != null) Servo9PwmMax = max.Value;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncServo10Async()
    {
        _isSyncing = true;
        try
        {
            var function = await _parameterService.GetParameterAsync(ParamServo10Function);
            if (function != null) Servo10Function = function.Value;

            var min = await _parameterService.GetParameterAsync(ParamServo10Min);
            if (min != null) Servo10PwmMin = min.Value;

            var max = await _parameterService.GetParameterAsync(ParamServo10Max);
            if (max != null) Servo10PwmMax = max.Value;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncFlowMeterAsync()
    {
        _isSyncing = true;
        try
        {
            var pumpRate = await _parameterService.GetParameterAsync(ParamSprayPumpRate);
            if (pumpRate != null) PumpRate = pumpRate.Value;

            var spinnerRate = await _parameterService.GetParameterAsync(ParamSpraySpinnerRate);
            if (spinnerRate != null) SpinnerMinSpeed = spinnerRate.Value;
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
            _ = _paramLogService.LogAsync(name, 0f, value, "SprayingConfig");
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

    #region Property Change Handlers - Spraying Enable

    partial void OnSelectedEnableSprayingChanged(SprayingEnableOption? value) => RunSafe(() => ApplySprayingEnableAsync(value));

    private async Task ApplySprayingEnableAsync(SprayingEnableOption? option)
    {
        if (_isSyncing || option == null || !IsPageEnabled) return;

        await ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(ParamSprayEnable, option.Value);
            StatusMessage = success ? "Spraying enable updated." : "Failed to update spraying enable.";
            if (!success) await SyncSprayingEnableAsync();
        });
    }

    #endregion

    #region Property Change Handlers - Spraying Config

    partial void OnRcSwitchChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamSpraySpinner, value, "RC Switch"));
    partial void OnBrdPwmCountChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamBrdPwmCount, value, "BRD PWM Count"));

    #endregion

    #region Property Change Handlers - Servo9

    partial void OnServo9FunctionChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamServo9Function, value, "Servo9 Function"));
    partial void OnServo9PwmMinChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamServo9Min, value, "Servo9 PWM Min"));
    partial void OnServo9PwmMaxChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamServo9Max, value, "Servo9 PWM Max"));

    #endregion

    #region Property Change Handlers - Servo10

    partial void OnServo10FunctionChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamServo10Function, value, "Servo10 Function"));
    partial void OnServo10PwmMinChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamServo10Min, value, "Servo10 PWM Min"));
    partial void OnServo10PwmMaxChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamServo10Max, value, "Servo10 PWM Max"));

    #endregion

    #region Numeric Apply Method

    private async Task ApplyNumericAsync(string param, float value, string name)
    {
        if (_isSyncing || !IsPageEnabled) return;

        await ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(param, value);
            StatusMessage = success ? $"{name} updated." : $"Failed to update {name}.";
        });
    }

    #endregion

    #region Commands

    [RelayCommand]
    private async Task UpdateConfigurationAsync()
    {
        if (!IsPageEnabled) return;

        await ExecuteWriteAsync(async () =>
        {
            StatusMessage = "Updating spraying configuration...";
            var allSuccess = true;

            // Update all spraying parameters
            if (SelectedEnableSpraying != null)
                allSuccess &= await WriteParameterAsync(ParamSprayEnable, SelectedEnableSpraying.Value);

            allSuccess &= await WriteParameterAsync(ParamSpraySpinner, RcSwitch);
            allSuccess &= await WriteParameterAsync(ParamBrdPwmCount, BrdPwmCount);
            allSuccess &= await WriteParameterAsync(ParamServo9Function, Servo9Function);
            allSuccess &= await WriteParameterAsync(ParamServo9Min, Servo9PwmMin);
            allSuccess &= await WriteParameterAsync(ParamServo9Max, Servo9PwmMax);
            allSuccess &= await WriteParameterAsync(ParamServo10Function, Servo10Function);
            allSuccess &= await WriteParameterAsync(ParamServo10Min, Servo10PwmMin);
            allSuccess &= await WriteParameterAsync(ParamServo10Max, Servo10PwmMax);

            StatusMessage = allSuccess
                ? "Spraying configuration updated successfully."
                : "Some parameters failed to update.";
        });
    }

    [RelayCommand]
    private async Task StartFlowMeterCalibrationAsync()
    {
        if (!IsPageEnabled || SelectedCalibrationVolume == null) return;

        IsCalibrating = true;
        CalibrationStatus = $"Starting flow meter calibration for {SelectedCalibrationVolume.Label}...";

        try
        {
            // Flow meter calibration typically involves:
            // 1. Starting the pump
            // 2. Measuring the flow for the specified volume
            // 3. Calculating the calibration factor
            
            // For now, we show the status and the user needs to manually observe
            // the flow and complete the calibration
            CalibrationStatus = $"Calibrating: Dispense {SelectedCalibrationVolume.Label} and press Stop when complete.";
            StatusMessage = "Flow meter calibration in progress...";
            
            // In a real implementation, this would interact with the drone
            // to start the pump and measure the flow
            await Task.Delay(500); // Brief delay to show UI update
        }
        catch (Exception ex)
        {
            CalibrationStatus = $"Calibration error: {ex.Message}";
            StatusMessage = "Flow meter calibration failed.";
        }
    }

    [RelayCommand]
    private void StopFlowMeterCalibration()
    {
        if (!IsCalibrating) return;

        IsCalibrating = false;
        CalibrationStatus = "Flow meter calibration completed.";
        StatusMessage = "Flow meter calibration stopped.";
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        if (!IsPageEnabled) return;

        IsBusy = true;
        StatusMessage = "Refreshing spraying parameters...";
        try
        {
            await SyncAllFromCacheAsync();
            StatusMessage = "Spraying parameters refreshed.";
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
                StatusMessage = "Spraying operation failed. See logs for details.";
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

public sealed record SprayingEnableOption(int Value, string Label);
public sealed record CalibrationVolumeOption(int Value, string Label);
