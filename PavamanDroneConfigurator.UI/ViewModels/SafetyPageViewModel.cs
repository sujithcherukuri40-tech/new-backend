using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Enums;
using System.Collections.Generic;

namespace PavamanDroneConfigurator.UI.ViewModels;

public sealed partial class SafetyPageViewModel : ViewModelBase
{
    private readonly IParameterService _parameterService;
    private readonly IConnectionService _connectionService;
    private readonly PavamanDroneConfigurator.UI.Services.ParameterChangeLogService? _paramLogService;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private bool _isSyncing;
    private bool _disposed;

    #region Parameter Names
    
    // Arming
    private const string ParamArmingCheck = "ARMING_CHECK";
    
    // Battery Failsafe
    private const string ParamBattMonitor = "BATT_MONITOR";
    private const string ParamBattLowVolt = "BATT_LOW_VOLT";
    private const string ParamBattCritVolt = "BATT_CRT_VOLT";
    private const string ParamBattLowMah = "BATT_LOW_MAH";
    private const string ParamBattCritMah = "BATT_CRT_MAH";
    private const string ParamBattCapacity = "BATT_CAPACITY";
    private const string ParamBattFsLowAct = "BATT_FS_LOW_ACT";
    private const string ParamBattFsCritAct = "BATT_FS_CRT_ACT";
    
    // RC Failsafe
    private const string ParamRcFailsafe = "FS_THR_ENABLE";
    private const string ParamRcFailsafePwm = "FS_THR_VALUE";
    
    // GCS Failsafe
    private const string ParamGcsFailsafe = "FS_GCS_ENABLE";
    private const string ParamGcsTimeout = "FS_GCS_TIMEOUT";
    
    // Geofence
    private const string ParamFenceEnable = "FENCE_ENABLE";
    private const string ParamFenceType = "FENCE_TYPE";
    private const string ParamFenceAction = "FENCE_ACTION";
    private const string ParamFenceAltMax = "FENCE_ALT_MAX";
    private const string ParamFenceAltMin = "FENCE_ALT_MIN";
    private const string ParamFenceRadius = "FENCE_RADIUS";
    private const string ParamFenceMargin = "FENCE_MARGIN";
    
    // EKF Failsafe
    private const string ParamEkfAction = "FS_EKF_ACTION";
    private const string ParamEkfThresh = "FS_EKF_THRESH";
    
    // Vibration Failsafe
    private const string ParamVibeEnable = "FS_VIBE_ENABLE";
    
    // Crash Check
    private const string ParamCrashCheck = "FS_CRASH_CHECK";
    
    // Motor Safety
    private const string ParamDisarmDelay = "DISARM_DELAY";
    
    // RTL Settings
    private const string ParamRtlAlt = "RTL_ALT";
    private const string ParamRtlAltFinal = "RTL_ALT_FINAL";
    private const string ParamRtlLoitTime = "RTL_LOIT_TIME";
    private const string ParamRtlSpeed = "RTL_SPEED";
    
    // Land Settings
    private const string ParamLandSpeed = "LAND_SPEED";
    private const string ParamLandSpeedHigh = "LAND_SPEED_HIGH";
    
    #endregion

    #region Arming Check Bits
    
    private const int ArmingBitAll = 1;
    private const int ArmingBitBarometer = 2;
    private const int ArmingBitCompass = 4;
    private const int ArmingBitGps = 8;
    private const int ArmingBitIns = 16;
    private const int ArmingBitParams = 32;
    private const int ArmingBitRc = 64;
    private const int ArmingBitVoltage = 128;
    private const int ArmingBitBattery = 256;
    private const int ArmingBitAirspeed = 512;
    private const int ArmingBitLogging = 1024;
    private const int ArmingBitSafetySwitch = 2048;
    private const int ArmingBitGpsConfig = 4096;
    private const int ArmingBitSystem = 8192;
    
    #endregion

    #region Option Collections
    
    private static readonly ReadOnlyCollection<SafetyOption> BatteryFailsafeOptions = new(
    [
        new SafetyOption(0, "Disabled"),
        new SafetyOption(1, "Land"),
        new SafetyOption(2, "RTL"),
        new SafetyOption(3, "SmartRTL or Land"),
        new SafetyOption(4, "SmartRTL or RTL"),
        new SafetyOption(5, "Terminate")
    ]);

    private static readonly ReadOnlyCollection<SafetyOption> RcFailsafeOptions = new(
    [
        new SafetyOption(0, "Disabled"),
        new SafetyOption(1, "Always RTL"),
        new SafetyOption(2, "Continue Mission (Auto)"),
        new SafetyOption(3, "Always Land"),
        new SafetyOption(4, "SmartRTL or RTL"),
        new SafetyOption(5, "SmartRTL or Land"),
        new SafetyOption(6, "Terminate")
    ]);

    private static readonly ReadOnlyCollection<SafetyOption> GcsFailsafeOptions = new(
    [
        new SafetyOption(0, "Disabled"),
        new SafetyOption(1, "RTL"),
        new SafetyOption(2, "Continue Mission (Auto)"),
        new SafetyOption(3, "SmartRTL or RTL"),
        new SafetyOption(4, "SmartRTL or Land"),
        new SafetyOption(5, "Terminate")
    ]);

    private static readonly ReadOnlyCollection<SafetyOption> FenceActionOptions = new(
    [
        new SafetyOption(0, "Report Only"),
        new SafetyOption(1, "RTL or Land"),
        new SafetyOption(2, "Always Land"),
        new SafetyOption(3, "SmartRTL or RTL or Land"),
        new SafetyOption(4, "Brake or Land"),
        new SafetyOption(5, "SmartRTL or Land"),
        new SafetyOption(6, "Terminate")
    ]);

    private static readonly ReadOnlyCollection<SafetyOption> EkfFailsafeOptions = new(
    [
        new SafetyOption(0, "Disabled"),
        new SafetyOption(1, "Land"),
        new SafetyOption(2, "AltHold"),
        new SafetyOption(3, "Land (even in Stabilize)")
    ]);

    private static readonly ReadOnlyCollection<SafetyOption> VibeFailsafeOptions = new(
    [
        new SafetyOption(0, "Disabled"),
        new SafetyOption(1, "Warn Only"),
        new SafetyOption(2, "Land")
    ]);

    private static readonly ReadOnlyCollection<SafetyOption> CrashCheckOptions = new(
    [
        new SafetyOption(0, "Disabled"),
        new SafetyOption(1, "Disarm")
    ]);

    #endregion

    #region Public Option Collections
    
    public IReadOnlyList<SafetyOption> BatteryLowActionOptions => BatteryFailsafeOptions;
    public IReadOnlyList<SafetyOption> BatteryCritActionOptions => BatteryFailsafeOptions;
    public IReadOnlyList<SafetyOption> RcFailsafeActionOptions => RcFailsafeOptions;
    public IReadOnlyList<SafetyOption> GcsFailsafeActionOptions => GcsFailsafeOptions;
    public IReadOnlyList<SafetyOption> FenceBreachActionOptions => FenceActionOptions;
    public IReadOnlyList<SafetyOption> EkfFailsafeActionOptions => EkfFailsafeOptions;
    public IReadOnlyList<SafetyOption> VibeFailsafeActionOptions => VibeFailsafeOptions;
    public IReadOnlyList<SafetyOption> CrashCheckActionOptions => CrashCheckOptions;
    
    #endregion

    #region Observable Properties - Page State
    
    [ObservableProperty] private bool _isPageEnabled;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Downloading parameters...";
    [ObservableProperty] private bool _hasValidationWarnings;
    [ObservableProperty] private string _validationMessage = string.Empty;
    
    #endregion

    #region Observable Properties - Arming Checks
    
    [ObservableProperty] private bool _armingCheckBarometer;
    [ObservableProperty] private bool _armingCheckCompass;
    [ObservableProperty] private bool _armingCheckGps;
    [ObservableProperty] private bool _armingCheckIns;
    [ObservableProperty] private bool _armingCheckParams;
    [ObservableProperty] private bool _armingCheckRc;
    [ObservableProperty] private bool _armingCheckVoltage;
    [ObservableProperty] private bool _armingCheckBattery;
    [ObservableProperty] private bool _armingCheckLogging;
    [ObservableProperty] private bool _armingCheckSafetySwitch;
    [ObservableProperty] private bool _armingCheckGpsConfig;
    [ObservableProperty] private bool _armingCheckSystem;
    
    #endregion

    #region Observable Properties - Battery Failsafe
    
    [ObservableProperty] private float _battLowVoltage = 10.5f;
    [ObservableProperty] private float _battCritVoltage = 10.0f;
    [ObservableProperty] private float _battCapacity = 3300f;
    [ObservableProperty] private SafetyOption? _selectedBattLowAction;
    [ObservableProperty] private SafetyOption? _selectedBattCritAction;
    
    #endregion

    #region Observable Properties - RC Failsafe
    
    [ObservableProperty] private SafetyOption? _selectedRcFailsafeAction;
    [ObservableProperty] private float _rcFailsafePwm = 975f;
    
    #endregion

    #region Observable Properties - GCS Failsafe
    
    [ObservableProperty] private SafetyOption? _selectedGcsFailsafeAction;
    [ObservableProperty] private float _gcsFailsafeTimeout = 5.0f;
    
    #endregion

    #region Observable Properties - Geofence
    
    [ObservableProperty] private bool _fenceEnabled;
    [ObservableProperty] private bool _fenceTypeAltMax = true;
    [ObservableProperty] private bool _fenceTypeCircle = true;
    [ObservableProperty] private bool _fenceTypePolygon;
    [ObservableProperty] private bool _fenceTypeAltMin;
    [ObservableProperty] private SafetyOption? _selectedFenceAction;
    [ObservableProperty] private float _fenceAltMax = 120f;
    [ObservableProperty] private float _fenceAltMin = -10f;
    [ObservableProperty] private float _fenceRadius = 300f;
    [ObservableProperty] private float _fenceMargin = 2f;
    
    #endregion

    #region Observable Properties - EKF Failsafe
    
    [ObservableProperty] private SafetyOption? _selectedEkfFailsafeAction;
    [ObservableProperty] private float _ekfThreshold = 0.8f;
    
    #endregion

    #region Observable Properties - Vibration & Crash
    
    [ObservableProperty] private SafetyOption? _selectedVibeFailsafeAction;
    [ObservableProperty] private SafetyOption? _selectedCrashCheckAction;
    
    #endregion

    #region Observable Properties - Motor Safety
    
    [ObservableProperty] private float _disarmDelay = 10f;
    
    #endregion

    #region Observable Properties - RTL Settings
    
    [ObservableProperty] private float _rtlAltitude = 15f;
    [ObservableProperty] private float _rtlFinalAltitude = 0f;
    [ObservableProperty] private float _rtlLoiterTime = 5f;
    [ObservableProperty] private float _rtlSpeed = 0f;
    
    #endregion

    #region Observable Properties - Land Settings
    
    [ObservableProperty] private float _landSpeed = 50f;
    [ObservableProperty] private float _landSpeedHigh = 0f;
    
    #endregion

    public SafetyPageViewModel(
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
        StatusMessage = IsPageEnabled ? "Safety parameters loaded." : "Downloading parameters...";

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
                StatusMessage = "Safety parameters loaded.";
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
                StatusMessage = "Safety parameters loaded.";
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
                var p when p == ParamArmingCheck.ToUpperInvariant() => SyncArmingChecksAsync(),
                var p when p == ParamBattFsLowAct.ToUpperInvariant() => SyncBatteryFailsafeAsync(),
                var p when p == ParamBattFsCritAct.ToUpperInvariant() => SyncBatteryFailsafeAsync(),
                var p when p == ParamBattLowVolt.ToUpperInvariant() => SyncBatteryFailsafeAsync(),
                var p when p == ParamBattCritVolt.ToUpperInvariant() => SyncBatteryFailsafeAsync(),
                var p when p == ParamRcFailsafe.ToUpperInvariant() => SyncRcFailsafeAsync(),
                var p when p == ParamGcsFailsafe.ToUpperInvariant() => SyncGcsFailsafeAsync(),
                var p when p == ParamFenceEnable.ToUpperInvariant() => SyncGeofenceAsync(),
                var p when p == ParamFenceAction.ToUpperInvariant() => SyncGeofenceAsync(),
                var p when p == ParamRtlAlt.ToUpperInvariant() => SyncRtlSettingsAsync(),
                _ => Task.CompletedTask
            };
            
            if (syncTask != Task.CompletedTask)
                RunSafe(() => syncTask);
        });
    }

    #region Sync Methods
    
    private async Task SyncAllFromCacheAsync()
    {
        await SyncArmingChecksAsync();
        await SyncBatteryFailsafeAsync();
        await SyncRcFailsafeAsync();
        await SyncGcsFailsafeAsync();
        await SyncGeofenceAsync();
        await SyncEkfFailsafeAsync();
        await SyncVibeFailsafeAsync();
        await SyncCrashCheckAsync();
        await SyncMotorSafetyAsync();
        await SyncRtlSettingsAsync();
        await SyncLandSettingsAsync();
        
        ValidatePDRLCompliance();
    }

    private async Task SyncArmingChecksAsync()
    {
        var param = await _parameterService.GetParameterAsync(ParamArmingCheck);
        if (param == null) return;

        var mask = (int)Math.Round(param.Value);
        
        _isSyncing = true;
        try
        {
            ArmingCheckBarometer = HasBit(mask, ArmingBitBarometer);
            ArmingCheckCompass = HasBit(mask, ArmingBitCompass);
            ArmingCheckGps = HasBit(mask, ArmingBitGps);
            ArmingCheckIns = HasBit(mask, ArmingBitIns);
            ArmingCheckParams = HasBit(mask, ArmingBitParams);
            ArmingCheckRc = HasBit(mask, ArmingBitRc);
            ArmingCheckVoltage = HasBit(mask, ArmingBitVoltage);
            ArmingCheckBattery = HasBit(mask, ArmingBitBattery);
            ArmingCheckLogging = HasBit(mask, ArmingBitLogging);
            ArmingCheckSafetySwitch = HasBit(mask, ArmingBitSafetySwitch);
            ArmingCheckGpsConfig = HasBit(mask, ArmingBitGpsConfig);
            ArmingCheckSystem = HasBit(mask, ArmingBitSystem);
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncBatteryFailsafeAsync()
    {
        _isSyncing = true;
        try
        {
            var lowVolt = await _parameterService.GetParameterAsync(ParamBattLowVolt);
            if (lowVolt != null) BattLowVoltage = lowVolt.Value;

            var critVolt = await _parameterService.GetParameterAsync(ParamBattCritVolt);
            if (critVolt != null) BattCritVoltage = critVolt.Value;

            var capacity = await _parameterService.GetParameterAsync(ParamBattCapacity);
            if (capacity != null) BattCapacity = capacity.Value;

            var lowAct = await _parameterService.GetParameterAsync(ParamBattFsLowAct);
            if (lowAct != null)
                SelectedBattLowAction = BatteryFailsafeOptions.FirstOrDefault(o => o.Value == (int)Math.Round(lowAct.Value));

            var critAct = await _parameterService.GetParameterAsync(ParamBattFsCritAct);
            if (critAct != null)
                SelectedBattCritAction = BatteryFailsafeOptions.FirstOrDefault(o => o.Value == (int)Math.Round(critAct.Value));
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncRcFailsafeAsync()
    {
        _isSyncing = true;
        try
        {
            var action = await _parameterService.GetParameterAsync(ParamRcFailsafe);
            if (action != null)
                SelectedRcFailsafeAction = RcFailsafeOptions.FirstOrDefault(o => o.Value == (int)Math.Round(action.Value));

            var pwm = await _parameterService.GetParameterAsync(ParamRcFailsafePwm);
            if (pwm != null) RcFailsafePwm = pwm.Value;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncGcsFailsafeAsync()
    {
        _isSyncing = true;
        try
        {
            var action = await _parameterService.GetParameterAsync(ParamGcsFailsafe);
            if (action != null)
                SelectedGcsFailsafeAction = GcsFailsafeOptions.FirstOrDefault(o => o.Value == (int)Math.Round(action.Value));

            var timeout = await _parameterService.GetParameterAsync(ParamGcsTimeout);
            if (timeout != null) GcsFailsafeTimeout = timeout.Value;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncGeofenceAsync()
    {
        _isSyncing = true;
        try
        {
            var enable = await _parameterService.GetParameterAsync(ParamFenceEnable);
            if (enable != null) FenceEnabled = enable.Value > 0;

            var type = await _parameterService.GetParameterAsync(ParamFenceType);
            if (type != null)
            {
                var typeMask = (int)Math.Round(type.Value);
                FenceTypeAltMax = HasBit(typeMask, 1);
                FenceTypeCircle = HasBit(typeMask, 2);
                FenceTypePolygon = HasBit(typeMask, 4);
                FenceTypeAltMin = HasBit(typeMask, 8);
            }

            var action = await _parameterService.GetParameterAsync(ParamFenceAction);
            if (action != null)
                SelectedFenceAction = FenceActionOptions.FirstOrDefault(o => o.Value == (int)Math.Round(action.Value));

            var altMax = await _parameterService.GetParameterAsync(ParamFenceAltMax);
            if (altMax != null) FenceAltMax = altMax.Value;

            var altMin = await _parameterService.GetParameterAsync(ParamFenceAltMin);
            if (altMin != null) FenceAltMin = altMin.Value;

            var radius = await _parameterService.GetParameterAsync(ParamFenceRadius);
            if (radius != null) FenceRadius = radius.Value;

            var margin = await _parameterService.GetParameterAsync(ParamFenceMargin);
            if (margin != null) FenceMargin = margin.Value;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncEkfFailsafeAsync()
    {
        _isSyncing = true;
        try
        {
            var action = await _parameterService.GetParameterAsync(ParamEkfAction);
            if (action != null)
                SelectedEkfFailsafeAction = EkfFailsafeOptions.FirstOrDefault(o => o.Value == (int)Math.Round(action.Value));

            var thresh = await _parameterService.GetParameterAsync(ParamEkfThresh);
            if (thresh != null) EkfThreshold = thresh.Value;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncVibeFailsafeAsync()
    {
        _isSyncing = true;
        try
        {
            var action = await _parameterService.GetParameterAsync(ParamVibeEnable);
            if (action != null)
                SelectedVibeFailsafeAction = VibeFailsafeOptions.FirstOrDefault(o => o.Value == (int)Math.Round(action.Value));
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncCrashCheckAsync()
    {
        _isSyncing = true;
        try
        {
            var action = await _parameterService.GetParameterAsync(ParamCrashCheck);
            if (action != null)
                SelectedCrashCheckAction = CrashCheckOptions.FirstOrDefault(o => o.Value == (int)Math.Round(action.Value));
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncMotorSafetyAsync()
    {
        _isSyncing = true;
        try
        {
            var delay = await _parameterService.GetParameterAsync(ParamDisarmDelay);
            if (delay != null) DisarmDelay = delay.Value;
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncRtlSettingsAsync()
    {
        _isSyncing = true;
        try
        {
            var alt = await _parameterService.GetParameterAsync(ParamRtlAlt);
            if (alt != null) RtlAltitude = alt.Value / 100f; // Convert cm to m

            var altFinal = await _parameterService.GetParameterAsync(ParamRtlAltFinal);
            if (altFinal != null) RtlFinalAltitude = altFinal.Value / 100f;

            var loitTime = await _parameterService.GetParameterAsync(ParamRtlLoitTime);
            if (loitTime != null) RtlLoiterTime = loitTime.Value / 1000f; // Convert ms to s

            var speed = await _parameterService.GetParameterAsync(ParamRtlSpeed);
            if (speed != null) RtlSpeed = speed.Value / 100f; // Convert cm/s to m/s
        }
        finally
        {
            _isSyncing = false;
        }
    }

    private async Task SyncLandSettingsAsync()
    {
        _isSyncing = true;
        try
        {
            var speed = await _parameterService.GetParameterAsync(ParamLandSpeed);
            if (speed != null) LandSpeed = speed.Value;

            var speedHigh = await _parameterService.GetParameterAsync(ParamLandSpeedHigh);
            if (speedHigh != null) LandSpeedHigh = speedHigh.Value;
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
            _ = _paramLogService.LogAsync(name, 0f, value, "Safety");
        return result;
    }

    private async Task ExecuteWriteAsync(Func<Task> operation)
    {
        await _writeLock.WaitAsync();
        IsBusy = true;
        try
        {
            await operation();
            ValidatePDRLCompliance();
        }
        finally
        {
            IsBusy = false;
            _writeLock.Release();
        }
    }
    
    #endregion

    #region PDRL Validation
    
    private void ValidatePDRLCompliance()
    {
        var warnings = new List<string>();

        // Check arming requirements
        if (!ArmingCheckGps)
            warnings.Add("GPS arming check disabled - PDRL requires GPS lock");
        if (!ArmingCheckBattery && !ArmingCheckVoltage)
            warnings.Add("Battery checks disabled - PDRL requires battery monitoring");
        if (!ArmingCheckCompass)
            warnings.Add("Compass check disabled - recommended for PDRL");

        // Check battery failsafe
        if (SelectedBattLowAction?.Value == 0)
            warnings.Add("Low battery failsafe disabled - PDRL requires failsafe action");
        if (SelectedBattCritAction?.Value == 0)
            warnings.Add("Critical battery failsafe disabled - dangerous!");

        // Check RC failsafe
        if (SelectedRcFailsafeAction?.Value == 0)
            warnings.Add("RC failsafe disabled - PDRL requires RC loss protection");

        // Check geofence
        if (!FenceEnabled)
            warnings.Add("Geofence disabled - PDRL recommends altitude/radius limits");
        else if (FenceAltMax > 120)
            warnings.Add($"Fence altitude {FenceAltMax}m exceeds PDRL limit of 120m AGL");

        // Check RTL altitude
        if (RtlAltitude > 120)
            warnings.Add($"RTL altitude {RtlAltitude}m exceeds PDRL limit");

        HasValidationWarnings = warnings.Count > 0;
        ValidationMessage = warnings.Count > 0 
            ? string.Join("\n", warnings.Take(3)) + (warnings.Count > 3 ? $"\n... and {warnings.Count - 3} more" : "")
            : "All PDRL safety requirements met ?";
    }
    
    #endregion

    #region Property Change Handlers - Arming Checks
    
    partial void OnArmingCheckBarometerChanged(bool value) => RunSafe(UpdateArmingChecksAsync);
    partial void OnArmingCheckCompassChanged(bool value) => RunSafe(UpdateArmingChecksAsync);
    partial void OnArmingCheckGpsChanged(bool value) => RunSafe(UpdateArmingChecksAsync);
    partial void OnArmingCheckInsChanged(bool value) => RunSafe(UpdateArmingChecksAsync);
    partial void OnArmingCheckParamsChanged(bool value) => RunSafe(UpdateArmingChecksAsync);
    partial void OnArmingCheckRcChanged(bool value) => RunSafe(UpdateArmingChecksAsync);
    partial void OnArmingCheckVoltageChanged(bool value) => RunSafe(UpdateArmingChecksAsync);
    partial void OnArmingCheckBatteryChanged(bool value) => RunSafe(UpdateArmingChecksAsync);
    partial void OnArmingCheckLoggingChanged(bool value) => RunSafe(UpdateArmingChecksAsync);
    partial void OnArmingCheckSafetySwitchChanged(bool value) => RunSafe(UpdateArmingChecksAsync);
    partial void OnArmingCheckGpsConfigChanged(bool value) => RunSafe(UpdateArmingChecksAsync);
    partial void OnArmingCheckSystemChanged(bool value) => RunSafe(UpdateArmingChecksAsync);

    private async Task UpdateArmingChecksAsync()
    {
        if (_isSyncing || !IsPageEnabled) return;

        var mask = BuildArmingMask();
        await ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(ParamArmingCheck, mask);
            StatusMessage = success ? "Arming checks updated." : "Failed to update arming checks.";
            if (!success) await SyncArmingChecksAsync();
        });
    }

    private int BuildArmingMask()
    {
        var mask = 0;
        if (ArmingCheckBarometer) mask |= ArmingBitBarometer;
        if (ArmingCheckCompass) mask |= ArmingBitCompass;
        if (ArmingCheckGps) mask |= ArmingBitGps;
        if (ArmingCheckIns) mask |= ArmingBitIns;
        if (ArmingCheckParams) mask |= ArmingBitParams;
        if (ArmingCheckRc) mask |= ArmingBitRc;
        if (ArmingCheckVoltage) mask |= ArmingBitVoltage;
        if (ArmingCheckBattery) mask |= ArmingBitBattery;
        if (ArmingCheckLogging) mask |= ArmingBitLogging;
        if (ArmingCheckSafetySwitch) mask |= ArmingBitSafetySwitch;
        if (ArmingCheckGpsConfig) mask |= ArmingBitGpsConfig;
        if (ArmingCheckSystem) mask |= ArmingBitSystem;
        return mask;
    }
    
    #endregion

    #region Property Change Handlers - Failsafes
    
    partial void OnSelectedBattLowActionChanged(SafetyOption? value) => RunSafe(() => ApplyBatteryFailsafeAsync(ParamBattFsLowAct, value));
    partial void OnSelectedBattCritActionChanged(SafetyOption? value) => RunSafe(() => ApplyBatteryFailsafeAsync(ParamBattFsCritAct, value));
    partial void OnSelectedRcFailsafeActionChanged(SafetyOption? value) => RunSafe(() => ApplyFailsafeOptionAsync(ParamRcFailsafe, value, "RC failsafe"));
    partial void OnSelectedGcsFailsafeActionChanged(SafetyOption? value) => RunSafe(() => ApplyFailsafeOptionAsync(ParamGcsFailsafe, value, "GCS failsafe"));
    partial void OnSelectedEkfFailsafeActionChanged(SafetyOption? value) => RunSafe(() => ApplyFailsafeOptionAsync(ParamEkfAction, value, "EKF failsafe"));
    partial void OnSelectedVibeFailsafeActionChanged(SafetyOption? value) => RunSafe(() => ApplyFailsafeOptionAsync(ParamVibeEnable, value, "Vibration failsafe"));
    partial void OnSelectedCrashCheckActionChanged(SafetyOption? value) => RunSafe(() => ApplyFailsafeOptionAsync(ParamCrashCheck, value, "Crash check"));

    private async Task ApplyBatteryFailsafeAsync(string param, SafetyOption? option)
    {
        if (_isSyncing || option == null || !IsPageEnabled) return;

        await ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(param, option.Value);
            StatusMessage = success ? "Battery failsafe updated." : "Failed to update battery failsafe.";
            if (!success) await SyncBatteryFailsafeAsync();
        });
    }

    private async Task ApplyFailsafeOptionAsync(string param, SafetyOption? option, string name)
    {
        if (_isSyncing || option == null || !IsPageEnabled) return;

        await ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(param, option.Value);
            StatusMessage = success ? $"{name} updated." : $"Failed to update {name}.";
        });
    }
    
    #endregion

    #region Property Change Handlers - Geofence
    
    partial void OnFenceEnabledChanged(bool value) => RunSafe(ApplyFenceEnabledAsync);
    partial void OnFenceTypeAltMaxChanged(bool value) => RunSafe(ApplyFenceTypeAsync);
    partial void OnFenceTypeCircleChanged(bool value) => RunSafe(ApplyFenceTypeAsync);
    partial void OnFenceTypePolygonChanged(bool value) => RunSafe(ApplyFenceTypeAsync);
    partial void OnFenceTypeAltMinChanged(bool value) => RunSafe(ApplyFenceTypeAsync);
    partial void OnSelectedFenceActionChanged(SafetyOption? value) => RunSafe(() => ApplyFailsafeOptionAsync(ParamFenceAction, value, "Fence action"));

    private async Task ApplyFenceEnabledAsync()
    {
        if (_isSyncing || !IsPageEnabled) return;

        await ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(ParamFenceEnable, FenceEnabled ? 1f : 0f);
            StatusMessage = success 
                ? (FenceEnabled ? "Geofence enabled." : "Geofence disabled.")
                : "Failed to update geofence.";
            if (!success) await SyncGeofenceAsync();
        });
    }

    private async Task ApplyFenceTypeAsync()
    {
        if (_isSyncing || !IsPageEnabled) return;

        var typeMask = 0;
        if (FenceTypeAltMax) typeMask |= 1;
        if (FenceTypeCircle) typeMask |= 2;
        if (FenceTypePolygon) typeMask |= 4;
        if (FenceTypeAltMin) typeMask |= 8;

        await ExecuteWriteAsync(async () =>
        {
            var success = await WriteParameterAsync(ParamFenceType, typeMask);
            StatusMessage = success ? "Fence type updated." : "Failed to update fence type.";
        });
    }
    
    #endregion

    #region Property Change Handlers - Numeric Values
    
    partial void OnBattLowVoltageChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamBattLowVolt, value, "Low battery voltage"));
    partial void OnBattCritVoltageChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamBattCritVolt, value, "Critical battery voltage"));
    partial void OnBattCapacityChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamBattCapacity, value, "Battery capacity"));
    partial void OnRcFailsafePwmChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamRcFailsafePwm, value, "RC failsafe PWM"));
    partial void OnGcsFailsafeTimeoutChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamGcsTimeout, value, "GCS timeout"));
    partial void OnFenceAltMaxChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamFenceAltMax, value, "Fence max altitude"));
    partial void OnFenceAltMinChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamFenceAltMin, value, "Fence min altitude"));
    partial void OnFenceRadiusChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamFenceRadius, value, "Fence radius"));
    partial void OnFenceMarginChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamFenceMargin, value, "Fence margin"));
    partial void OnEkfThresholdChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamEkfThresh, value, "EKF threshold"));
    partial void OnDisarmDelayChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamDisarmDelay, value, "Disarm delay"));
    partial void OnRtlAltitudeChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamRtlAlt, value * 100f, "RTL altitude")); // m to cm
    partial void OnRtlFinalAltitudeChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamRtlAltFinal, value * 100f, "RTL final altitude"));
    partial void OnRtlLoiterTimeChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamRtlLoitTime, value * 1000f, "RTL loiter time")); // s to ms
    partial void OnRtlSpeedChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamRtlSpeed, value * 100f, "RTL speed")); // m/s to cm/s
    partial void OnLandSpeedChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamLandSpeed, value, "Land speed"));
    partial void OnLandSpeedHighChanged(float value) => RunSafe(() => ApplyNumericAsync(ParamLandSpeedHigh, value, "Land speed high"));

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
    private async Task ApplyPDRLDefaultsAsync()
    {
        if (!IsPageEnabled) return;

        await ExecuteWriteAsync(async () =>
        {
            StatusMessage = "Applying PDRL safety defaults...";

            // PDRL recommended arming checks
            var armingMask = ArmingBitAll | ArmingBitBarometer | ArmingBitCompass | ArmingBitGps | 
                            ArmingBitIns | ArmingBitParams | ArmingBitRc | ArmingBitBattery;
            await WriteParameterAsync(ParamArmingCheck, armingMask);

            // Battery failsafe - RTL on low, Land on critical
            await WriteParameterAsync(ParamBattFsLowAct, 2); // RTL
            await WriteParameterAsync(ParamBattFsCritAct, 1); // Land

            // RC failsafe - Always RTL
            await WriteParameterAsync(ParamRcFailsafe, 1);

            // Geofence - Enable with 120m max altitude, 300m radius
            await WriteParameterAsync(ParamFenceEnable, 1);
            await WriteParameterAsync(ParamFenceType, 3); // Alt max + Circle
            await WriteParameterAsync(ParamFenceAction, 1); // RTL or Land
            await WriteParameterAsync(ParamFenceAltMax, 120);
            await WriteParameterAsync(ParamFenceRadius, 300);

            // RTL - 15m altitude
            await WriteParameterAsync(ParamRtlAlt, 1500); // 15m in cm

            // Crash check enabled
            await WriteParameterAsync(ParamCrashCheck, 1);

            await SyncAllFromCacheAsync();
            StatusMessage = "PDRL safety defaults applied successfully.";
        });
    }

    [RelayCommand]
    private async Task RefreshAllAsync()
    {
        if (!IsPageEnabled) return;

        IsBusy = true;
        StatusMessage = "Refreshing safety parameters...";
        try
        {
            await SyncAllFromCacheAsync();
            StatusMessage = "Safety parameters refreshed.";
        }
        finally
        {
            IsBusy = false;
        }
    }
    
    #endregion

    #region Helpers
    
    private static bool HasBit(int mask, int bit) => (mask & bit) != 0;

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
                StatusMessage = "Safety operation failed. See logs for details.";
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

public sealed record SafetyOption(int Value, string Label);
