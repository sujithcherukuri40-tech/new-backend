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
/// ViewModel for Serial Configuration page.
/// Manages serial port protocol and baud rate settings.
/// </summary>
public partial class SerialConfigPageViewModel : ViewModelBase
{
    private readonly ILogger<SerialConfigPageViewModel> _logger;
    private readonly ISerialConfigService _serialConfigService;
    private readonly IConnectionService _connectionService;

    #region Status Properties

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _hasValidationWarnings;

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    #endregion

    #region Serial0 (USB / Console) Properties

    [ObservableProperty]
    private SerialProtocolOption? _serial0Protocol;

    [ObservableProperty]
    private SerialBaudRateOption? _serial0BaudRate;

    #endregion

    #region Serial1 (TELEM1) Properties

    [ObservableProperty]
    private SerialProtocolOption? _serial1Protocol;

    [ObservableProperty]
    private SerialBaudRateOption? _serial1BaudRate;

    #endregion

    #region Serial2 (TELEM2) Properties

    [ObservableProperty]
    private SerialProtocolOption? _serial2Protocol;

    [ObservableProperty]
    private SerialBaudRateOption? _serial2BaudRate;

    #endregion

    #region Serial3 (GPS) Properties

    [ObservableProperty]
    private SerialProtocolOption? _serial3Protocol;

    [ObservableProperty]
    private SerialBaudRateOption? _serial3BaudRate;

    #endregion

    #region Serial4 (GPS2) Properties

    [ObservableProperty]
    private SerialProtocolOption? _serial4Protocol;

    [ObservableProperty]
    private SerialBaudRateOption? _serial4BaudRate;

    #endregion

    #region Serial5 Properties

    [ObservableProperty]
    private SerialProtocolOption? _serial5Protocol;

    [ObservableProperty]
    private SerialBaudRateOption? _serial5BaudRate;

    #endregion

    #region Serial6 Properties

    [ObservableProperty]
    private SerialProtocolOption? _serial6Protocol;

    [ObservableProperty]
    private SerialBaudRateOption? _serial6BaudRate;

    #endregion

    #region Collections

    public ObservableCollection<SerialProtocolOption> ProtocolOptions { get; } = new();
    public ObservableCollection<SerialBaudRateOption> BaudRateOptions { get; } = new();

    #endregion

    #region Internal State

    private SerialConfiguration? _currentConfiguration;

    #endregion

    public SerialConfigPageViewModel(
        ILogger<SerialConfigPageViewModel> logger,
        ISerialConfigService serialConfigService,
        IConnectionService connectionService)
    {
        _logger = logger;
        _serialConfigService = serialConfigService;
        _connectionService = connectionService;

        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _serialConfigService.ParameterUpdated += OnParameterUpdated;

        InitializeOptions();
        IsConnected = _connectionService.IsConnected;
    }

    private void InitializeOptions()
    {
        // Load protocol options
        foreach (var option in _serialConfigService.GetProtocolOptions())
        {
            ProtocolOptions.Add(option);
        }

        // Load baud rate options
        foreach (var option in _serialConfigService.GetBaudRateOptions())
        {
            BaudRateOptions.Add(option);
        }

        // Set defaults
        SetDefaultSelections();
    }

    private void SetDefaultSelections()
    {
        // Serial0 - USB / Console
        Serial0Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.MAVLink2);
        Serial0BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud115200)
                          ?? BaudRateOptions.FirstOrDefault();

        // Serial1 - TELEM1
        Serial1Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.MAVLink2);
        Serial1BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud57600);

        // Serial2 - TELEM2
        Serial2Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.MAVLink2);
        Serial2BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud57600);

        // Serial3 - GPS
        Serial3Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.GPS);
        Serial3BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud38400);

        // Serial4 - GPS2
        Serial4Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.GPS);
        Serial4BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud38400);

        // Serial5
        Serial5Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.MAVLink1);
        Serial5BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud57600);

        // Serial6
        Serial6Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.None);
        Serial6BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud1200);
    }

    #region Event Handlers

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            if (connected)
            {
                _ = RefreshAsync();
            }
        });
    }

    private void OnParameterUpdated(object? sender, string parameterName)
    {
        _logger.LogDebug("Serial parameter updated: {Parameter}", parameterName);
    }

    #endregion

    #region Property Change Handlers

    partial void OnSerial0ProtocolChanged(SerialProtocolOption? value) => ValidateConfiguration();
    partial void OnSerial1ProtocolChanged(SerialProtocolOption? value) => ValidateConfiguration();
    partial void OnSerial2ProtocolChanged(SerialProtocolOption? value) => ValidateConfiguration();
    partial void OnSerial3ProtocolChanged(SerialProtocolOption? value) => ValidateConfiguration();
    partial void OnSerial4ProtocolChanged(SerialProtocolOption? value) => ValidateConfiguration();
    partial void OnSerial5ProtocolChanged(SerialProtocolOption? value) => ValidateConfiguration();
    partial void OnSerial6ProtocolChanged(SerialProtocolOption? value) => ValidateConfiguration();

    #endregion

    #region Validation

    private void ValidateConfiguration()
    {
        var config = BuildConfigurationFromUI();
        var warnings = _serialConfigService.ValidateConfiguration(config);

        HasValidationWarnings = warnings.Count > 0;
        ValidationMessage = warnings.Count > 0
            ? string.Join("\n", warnings.Take(3))
            : "Configuration looks good!";
    }

    #endregion

    #region Configuration Building

    private SerialConfiguration BuildConfigurationFromUI()
    {
        var config = _currentConfiguration ?? new SerialConfiguration();

        // Serial0
        config.Serial0.Protocol = Serial0Protocol?.Protocol ?? SerialProtocol.MAVLink2;
        config.Serial0.BaudRate = Serial0BaudRate?.BaudRate ?? SerialBaudRate.Baud115200;

        // Serial1
        config.Serial1.Protocol = Serial1Protocol?.Protocol ?? SerialProtocol.MAVLink2;
        config.Serial1.BaudRate = Serial1BaudRate?.BaudRate ?? SerialBaudRate.Baud57600;

        // Serial2
        config.Serial2.Protocol = Serial2Protocol?.Protocol ?? SerialProtocol.MAVLink2;
        config.Serial2.BaudRate = Serial2BaudRate?.BaudRate ?? SerialBaudRate.Baud57600;

        // Serial3
        config.Serial3.Protocol = Serial3Protocol?.Protocol ?? SerialProtocol.GPS;
        config.Serial3.BaudRate = Serial3BaudRate?.BaudRate ?? SerialBaudRate.Baud38400;

        // Serial4
        config.Serial4.Protocol = Serial4Protocol?.Protocol ?? SerialProtocol.GPS;
        config.Serial4.BaudRate = Serial4BaudRate?.BaudRate ?? SerialBaudRate.Baud38400;

        // Serial5
        config.Serial5.Protocol = Serial5Protocol?.Protocol ?? SerialProtocol.MAVLink1;
        config.Serial5.BaudRate = Serial5BaudRate?.BaudRate ?? SerialBaudRate.Baud57600;

        // Serial6
        config.Serial6.Protocol = Serial6Protocol?.Protocol ?? SerialProtocol.None;
        config.Serial6.BaudRate = Serial6BaudRate?.BaudRate ?? SerialBaudRate.Baud1200;

        return config;
    }

    private void LoadConfigurationToUI(SerialConfiguration config)
    {
        _currentConfiguration = config;

        // Serial0
        Serial0Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == config.Serial0.Protocol);
        Serial0BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == config.Serial0.BaudRate);

        // Serial1
        Serial1Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == config.Serial1.Protocol);
        Serial1BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == config.Serial1.BaudRate);

        // Serial2
        Serial2Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == config.Serial2.Protocol);
        Serial2BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == config.Serial2.BaudRate);

        // Serial3
        Serial3Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == config.Serial3.Protocol);
        Serial3BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == config.Serial3.BaudRate);

        // Serial4
        Serial4Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == config.Serial4.Protocol);
        Serial4BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == config.Serial4.BaudRate);

        // Serial5
        Serial5Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == config.Serial5.Protocol);
        Serial5BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == config.Serial5.BaudRate);

        // Serial6
        Serial6Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == config.Serial6.Protocol);
        Serial6BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == config.Serial6.BaudRate);

        ValidateConfiguration();
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
            StatusMessage = "Loading serial configuration...";

            var config = await _serialConfigService.GetSerialConfigurationAsync();
            if (config != null)
            {
                LoadConfigurationToUI(config);
                StatusMessage = "Serial configuration loaded successfully";
            }
            else
            {
                StatusMessage = "Failed to load serial configuration";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing serial configuration");
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to vehicle";
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Updating serial configuration...";

            var config = BuildConfigurationFromUI();
            var success = await _serialConfigService.UpdateSerialConfigurationAsync(config);

            if (success)
            {
                StatusMessage = "Serial configuration updated successfully";
                _currentConfiguration = config;
                ValidateConfiguration();
            }
            else
            {
                StatusMessage = "Failed to update serial configuration";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating serial configuration");
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

            // Set UI to PDRL defaults
            Serial0Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.MAVLink2);
            Serial0BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud115200)
                              ?? BaudRateOptions.FirstOrDefault();

            Serial1Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.MAVLink2);
            Serial1BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud57600);

            Serial2Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.MAVLink2);
            Serial2BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud57600);

            Serial3Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.GPS);
            Serial3BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud115200);

            Serial4Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.GPS);
            Serial4BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud115200);

            Serial5Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.None);
            Serial5BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud57600);

            Serial6Protocol = ProtocolOptions.FirstOrDefault(p => p.Protocol == SerialProtocol.None);
            Serial6BaudRate = BaudRateOptions.FirstOrDefault(b => b.BaudRate == SerialBaudRate.Baud57600);

            if (IsConnected)
            {
                var success = await _serialConfigService.ApplyPDRLDefaultsAsync();
                StatusMessage = success ? "PDRL defaults applied successfully" : "Failed to apply defaults";
            }
            else
            {
                StatusMessage = "Defaults set (connect to upload)";
            }

            ValidateConfiguration();
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

    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _serialConfigService.ParameterUpdated -= OnParameterUpdated;
        }
        base.Dispose(disposing);
    }
}
