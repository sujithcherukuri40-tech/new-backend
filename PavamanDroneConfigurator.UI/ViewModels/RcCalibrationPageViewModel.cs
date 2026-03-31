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
/// ViewModel for RC Calibration page.
/// Manages RC channel calibration, monitoring, and attitude channel settings.
/// </summary>
public partial class RcCalibrationPageViewModel : ViewModelBase
{
    private readonly ILogger<RcCalibrationPageViewModel> _logger;
    private readonly IRcCalibrationService _rcCalibrationService;
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
    private bool _calibrationRequired = true;

    [ObservableProperty]
    private string _calibrationStatusText = "Radio calibration required";
    
    [ObservableProperty]
    private bool _isRcConnected;
    
    [ObservableProperty]
    private string _rcConnectionStatus = "Checking RC connection...";
    
    [ObservableProperty]
    private DateTime _lastRcDataReceived = DateTime.MinValue;

    #endregion
    
    /// <summary>
    /// True when flight controller is connected but RC receiver is not connected
    /// </summary>
    public bool IsConnectedButRcNotConnected => IsConnected && !IsRcConnected;

    #region Main Attitude Channels (Roll, Pitch, Yaw, Throttle)

    [ObservableProperty]
    private int _rollValue;

    [ObservableProperty]
    private int _pitchValue;

    [ObservableProperty]
    private int _yawValue;

    [ObservableProperty]
    private int _throttleValue;

    #endregion

    #region Calibration Min / Max / Center Values (captured during calibration)

    [ObservableProperty]
    private int _rollMin = 1000;

    [ObservableProperty]
    private int _rollMax = 2000;

    [ObservableProperty]
    private int _rollCenter = 1500;

    [ObservableProperty]
    private int _pitchMin = 1000;

    [ObservableProperty]
    private int _pitchMax = 2000;

    [ObservableProperty]
    private int _pitchCenter = 1500;

    [ObservableProperty]
    private int _yawMin = 1000;

    [ObservableProperty]
    private int _yawMax = 2000;

    [ObservableProperty]
    private int _yawCenter = 1500;

    [ObservableProperty]
    private int _throttleMin = 1000;

    [ObservableProperty]
    private int _throttleMax = 2000;

    [ObservableProperty]
    private int _throttleCenter = 1500;

    #endregion

    #region All 16 Channel Values

    [ObservableProperty]
    private int _channel1Value;

    [ObservableProperty]
    private int _channel2Value;

    [ObservableProperty]
    private int _channel3Value;

    [ObservableProperty]
    private int _channel4Value;

    [ObservableProperty]
    private int _channel5Value;

    [ObservableProperty]
    private int _channel6Value;

    [ObservableProperty]
    private int _channel7Value;

    [ObservableProperty]
    private int _channel8Value;

    [ObservableProperty]
    private int _channel9Value;

    [ObservableProperty]
    private int _channel10Value;

    [ObservableProperty]
    private int _channel11Value;

    [ObservableProperty]
    private int _channel12Value;

    [ObservableProperty]
    private int _channel13Value;

    [ObservableProperty]
    private int _channel14Value;

    [ObservableProperty]
    private int _channel15Value;

    [ObservableProperty]
    private int _channel16Value;

    #endregion

    #region Attitude Channel Mapping

    [ObservableProperty]
    private RcChannelOption? _selectedThrottleChannel;

    [ObservableProperty]
    private RcChannelOption? _selectedRollChannel;

    [ObservableProperty]
    private RcChannelOption? _selectedPitchChannel;

    [ObservableProperty]
    private RcChannelOption? _selectedYawChannel;

    #endregion

    #region Collections

    public ObservableCollection<ChannelMappingOption> ChannelOptions { get; } = new();

    #endregion

    #region Internal State

    private RcCalibrationConfiguration? _currentConfiguration;
    private AttitudeChannelMapping? _currentMapping;

    #endregion

    public RcCalibrationPageViewModel(
        ILogger<RcCalibrationPageViewModel> logger,
        IRcCalibrationService rcCalibrationService,
        IConnectionService connectionService)
    {
        _logger = logger;
        _rcCalibrationService = rcCalibrationService;
        _connectionService = connectionService;

        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _rcCalibrationService.RcChannelsUpdated += OnRcChannelsUpdated;
        _rcCalibrationService.CalibrationStateChanged += OnCalibrationStateChanged;
        _rcCalibrationService.CalibrationCompleted += OnCalibrationCompleted;

        InitializeChannelOptions();
        IsConnected = _connectionService.IsConnected;
    }

    private void InitializeChannelOptions()
    {
        // Initialize channel mapping options (1-16)
        for (int i = 1; i <= 16; i++)
        {
            ChannelOptions.Add(new ChannelMappingOption
            {
                Channel = (RcChannel)i,
                Label = $"Channel {i}"
            });
        }

        // Set defaults
        SelectedThrottleChannel = ChannelOptions.FirstOrDefault(c => c.Channel == RcChannel.Channel3)?.Option;
        SelectedRollChannel = ChannelOptions.FirstOrDefault(c => c.Channel == RcChannel.Channel1)?.Option;
        SelectedPitchChannel = ChannelOptions.FirstOrDefault(c => c.Channel == RcChannel.Channel2)?.Option;
        SelectedYawChannel = ChannelOptions.FirstOrDefault(c => c.Channel == RcChannel.Channel4)?.Option;
    }

    #region Event Handlers

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            OnPropertyChanged(nameof(IsConnectedButRcNotConnected));
            if (connected)
            {
                _ = RefreshAsync();
            }
            else
            {
                ResetChannelValues();
                IsRcConnected = false;
                RcConnectionStatus = "Not connected to flight controller";
            }
        });
    }

    private void OnRcChannelsUpdated(object? sender, RcChannelsUpdateEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateChannelValues(e);
        });
    }

    private void OnCalibrationStateChanged(object? sender, RcCalibrationProgress e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsCalibrating = e.IsCalibrating;
            CalibrationStatusText = e.StatusMessage;
            StatusMessage = e.Instructions;
        });
    }

    private void OnCalibrationCompleted(object? sender, bool success)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsCalibrating = false;
            CalibrationRequired = !success;
            CalibrationStatusText = success ? "Calibration completed" : "Calibration failed";
            StatusMessage = success ? "RC calibration completed successfully" : "RC calibration failed";
        });
    }

    private void UpdateChannelValues(RcChannelsUpdateEventArgs e)
    {
        // Update individual channel values
        Channel1Value = e.GetChannel(1)?.PwmValue ?? 0;
        Channel2Value = e.GetChannel(2)?.PwmValue ?? 0;
        Channel3Value = e.GetChannel(3)?.PwmValue ?? 0;
        Channel4Value = e.GetChannel(4)?.PwmValue ?? 0;
        Channel5Value = e.GetChannel(5)?.PwmValue ?? 0;
        Channel6Value = e.GetChannel(6)?.PwmValue ?? 0;
        Channel7Value = e.GetChannel(7)?.PwmValue ?? 0;
        Channel8Value = e.GetChannel(8)?.PwmValue ?? 0;
        Channel9Value = e.GetChannel(9)?.PwmValue ?? 0;
        Channel10Value = e.GetChannel(10)?.PwmValue ?? 0;
        Channel11Value = e.GetChannel(11)?.PwmValue ?? 0;
        Channel12Value = e.GetChannel(12)?.PwmValue ?? 0;
        Channel13Value = e.GetChannel(13)?.PwmValue ?? 0;
        Channel14Value = e.GetChannel(14)?.PwmValue ?? 0;
        Channel15Value = e.GetChannel(15)?.PwmValue ?? 0;
        Channel16Value = e.GetChannel(16)?.PwmValue ?? 0;

        // Determine attitude channel values
        int roll     = _currentMapping != null ? e.GetChannel(_currentMapping.RollChannel)?.PwmValue     ?? 0 : Channel1Value;
        int pitch    = _currentMapping != null ? e.GetChannel(_currentMapping.PitchChannel)?.PwmValue    ?? 0 : Channel2Value;
        int throttle = _currentMapping != null ? e.GetChannel(_currentMapping.ThrottleChannel)?.PwmValue ?? 0 : Channel3Value;
        int yaw      = _currentMapping != null ? e.GetChannel(_currentMapping.YawChannel)?.PwmValue      ?? 0 : Channel4Value;

        RollValue     = roll;
        PitchValue    = pitch;
        ThrottleValue = throttle;
        YawValue      = yaw;

        // Track min/max for calibration display
        if (IsCalibrating)
        {
            static int Valid(int v) => v > 800 && v < 2200 ? v : 0;
            int rv = Valid(roll), pv = Valid(pitch), tv = Valid(throttle), yv = Valid(yaw);
            if (rv > 0) { if (rv < RollMin) RollMin = rv; if (rv > RollMax) RollMax = rv; RollCenter = rv; }
            if (pv > 0) { if (pv < PitchMin) PitchMin = pv; if (pv > PitchMax) PitchMax = pv; PitchCenter = pv; }
            if (tv > 0) { if (tv < ThrottleMin) ThrottleMin = tv; if (tv > ThrottleMax) ThrottleMax = tv; ThrottleCenter = tv; }
            if (yv > 0) { if (yv < YawMin) YawMin = yv; if (yv > YawMax) YawMax = yv; YawCenter = yv; }
        }

        // Update RC connection status
        UpdateRcConnectionStatus(e);
    }
    
    /// <summary>
    /// Check if RC is connected by validating channel data
    /// </summary>
    private void UpdateRcConnectionStatus(RcChannelsUpdateEventArgs e)
    {
        // RC is considered connected if:
        // 1. We're receiving channel data
        // 2. At least the first 4 channels have valid PWM values (not 0 or 65535)
        // 3. Values are in reasonable range (800-2200 �s)
        
        var ch1 = e.GetChannel(1);
        var ch2 = e.GetChannel(2);
        var ch3 = e.GetChannel(3);
        var ch4 = e.GetChannel(4);
        
        bool hasValidChannels = ch1 != null && ch2 != null && ch3 != null && ch4 != null &&
                                 IsValidPwmValue(ch1.PwmValue) &&
                                 IsValidPwmValue(ch2.PwmValue) &&
                                 IsValidPwmValue(ch3.PwmValue) &&
                                 IsValidPwmValue(ch4.PwmValue);
        
        if (hasValidChannels)
        {
            LastRcDataReceived = DateTime.Now;
            if (!IsRcConnected)
            {
                IsRcConnected = true;
                OnPropertyChanged(nameof(IsConnectedButRcNotConnected));
            }
            RcConnectionStatus = $"RC Connected - {e.ChannelCount} channels, RSSI: {e.Rssi}";
        }
        else
        {
            // Check if we've lost RC connection (no valid data for 2 seconds)
            if ((DateTime.Now - LastRcDataReceived).TotalSeconds > 2)
            {
                if (IsRcConnected)
                {
                    IsRcConnected = false;
                    OnPropertyChanged(nameof(IsConnectedButRcNotConnected));
                }
                RcConnectionStatus = "No RC signal detected - Check transmitter power and receiver connection";
            }
        }
    }
    
    /// <summary>
    /// Validate PWM value is in acceptable range
    /// </summary>
    private bool IsValidPwmValue(int value)
    {
        return value > 800 && value < 2200 && value != 65535;
    }

    private void ResetChannelValues()
    {
        Channel1Value = Channel2Value = Channel3Value = Channel4Value = 0;
        Channel5Value = Channel6Value = Channel7Value = Channel8Value = 0;
        Channel9Value = Channel10Value = Channel11Value = Channel12Value = 0;
        Channel13Value = Channel14Value = Channel15Value = Channel16Value = 0;
        RollValue = PitchValue = YawValue = ThrottleValue = 0;
    }

    #endregion

    #region Commands

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
            StatusMessage = "Loading RC configuration...";

            // Get current configuration
            _currentConfiguration = await _rcCalibrationService.GetRcConfigurationAsync();

            // Get attitude mapping
            _currentMapping = await _rcCalibrationService.GetAttitudeMappingAsync();

            if (_currentMapping != null)
            {
                // Update UI with current mapping
                // Note: These would need proper binding if using ChannelMappingOption
            }

            // Check if calibration is needed
            CalibrationRequired = !await _rcCalibrationService.IsRcCalibratedAsync();
            CalibrationStatusText = CalibrationRequired ? "Radio calibration required" : "Radio calibrated";

            StatusMessage = "RC configuration loaded";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing RC configuration");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CalibrateAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to vehicle";
            return;
        }
        
        // Check if RC is connected before starting calibration
        if (!IsRcConnected && !IsCalibrating)
        {
            StatusMessage = "RC not connected - Turn on transmitter and ensure receiver is connected";
            RcConnectionStatus = "No RC signal - Cannot calibrate without RC input";
            _logger.LogWarning("Calibration blocked: RC receiver not connected");
            return;
        }

        if (IsCalibrating)
        {
            // Complete/stop calibration
            await _rcCalibrationService.CompleteCalibrationAsync();
        }
        else
        {
            // Reset calibration min/max/center tracking
            RollMin = PitchMin = ThrottleMin = YawMin = 2200;
            RollMax = PitchMax = ThrottleMax = YawMax = 800;
            RollCenter = PitchCenter = ThrottleCenter = YawCenter = 1500;

            // Start calibration
            _logger.LogInformation("Starting RC calibration - RC is connected");
            await _rcCalibrationService.StartCalibrationAsync();
            StatusMessage = "Move all sticks to their extreme positions...";
        }
    }

    [RelayCommand]
    private async Task UpdateMappingAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to vehicle";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Updating attitude channel mapping...";

            var mapping = new AttitudeChannelMapping
            {
                ThrottleChannel = GetSelectedChannel(SelectedThrottleChannel) ?? RcChannel.Channel3,
                RollChannel = GetSelectedChannel(SelectedRollChannel) ?? RcChannel.Channel1,
                PitchChannel = GetSelectedChannel(SelectedPitchChannel) ?? RcChannel.Channel2,
                YawChannel = GetSelectedChannel(SelectedYawChannel) ?? RcChannel.Channel4
            };

            var success = await _rcCalibrationService.UpdateAttitudeMappingAsync(mapping);

            if (success)
            {
                _currentMapping = mapping;
                StatusMessage = "Attitude channel mapping updated successfully";
            }
            else
            {
                StatusMessage = "Failed to update channel mapping";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating channel mapping");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ApplyDefaultsAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Applying PDRL-compliant defaults...";

            if (IsConnected)
            {
                var success = await _rcCalibrationService.ApplyPDRLDefaultsAsync();
                StatusMessage = success ? "PDRL defaults applied successfully" : "Failed to apply defaults";
            }
            else
            {
                // Set UI to defaults
                SelectedThrottleChannel = ChannelOptions.FirstOrDefault(c => c.Channel == RcChannel.Channel3)?.Option;
                SelectedRollChannel = ChannelOptions.FirstOrDefault(c => c.Channel == RcChannel.Channel1)?.Option;
                SelectedPitchChannel = ChannelOptions.FirstOrDefault(c => c.Channel == RcChannel.Channel2)?.Option;
                SelectedYawChannel = ChannelOptions.FirstOrDefault(c => c.Channel == RcChannel.Channel4)?.Option;
                StatusMessage = "Defaults set (connect to upload)";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying defaults");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private RcChannel? GetSelectedChannel(RcChannelOption? option)
    {
        // Convert option back to channel - this is a simplified approach
        // In a real implementation, you'd have proper two-way mapping
        return option switch
        {
            _ => null
        };
    }

    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _rcCalibrationService.RcChannelsUpdated -= OnRcChannelsUpdated;
            _rcCalibrationService.CalibrationStateChanged -= OnCalibrationStateChanged;
            _rcCalibrationService.CalibrationCompleted -= OnCalibrationCompleted;
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Helper class for channel mapping dropdown options
/// </summary>
public class ChannelMappingOption
{
    public RcChannel Channel { get; set; }
    public string Label { get; set; } = string.Empty;
    public RcChannelOption? Option { get; set; }
}
