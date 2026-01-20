using System;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// Represents a baud rate option for the connection ComboBox.
/// Provides strongly-typed binding to avoid InvalidCastException.
/// </summary>
public class BaudRateOption
{
    public int Value { get; set; }
    public string Display => Value.ToString();
    
    public override string ToString() => Display;
    
    public override bool Equals(object? obj)
    {
        if (obj is BaudRateOption other)
            return Value == other.Value;
        return false;
    }
    
    public override int GetHashCode() => Value.GetHashCode();
}

public partial class ConnectionPageViewModel : ViewModelBase
{
    private readonly IConnectionService _connectionService;
    private readonly IParameterService _parameterService;
    private bool _downloadInProgress;

    [ObservableProperty]
    private ObservableCollection<SerialPortInfo> _availableSerialPorts = new();

    [ObservableProperty]
    private SerialPortInfo? _selectedSerialPort;

    [ObservableProperty]
    private ObservableCollection<BluetoothDeviceInfo> _availableBluetoothDevices = new();

    [ObservableProperty]
    private BluetoothDeviceInfo? _selectedBluetoothDevice;

    /// <summary>
    /// Available baud rate options for the ComboBox.
    /// Using strongly-typed objects avoids InvalidCastException from ComboBoxItem.
    /// </summary>
    public ObservableCollection<BaudRateOption> AvailableBaudRates { get; } = new()
    {
        new BaudRateOption { Value = 9600 },
        new BaudRateOption { Value = 57600 },
        new BaudRateOption { Value = 115200 },
        new BaudRateOption { Value = 230400 },
        new BaudRateOption { Value = 460800 },
        new BaudRateOption { Value = 921600 }
    };

    [ObservableProperty]
    private BaudRateOption? _selectedBaudRate;

    /// <summary>
    /// Gets the actual baud rate value for connection settings.
    /// </summary>
    public int BaudRate => SelectedBaudRate?.Value ?? 115200;

    [ObservableProperty]
    private string _ipAddress = "127.0.0.1";

    [ObservableProperty]
    private int _tcpPort = 5760;

    [ObservableProperty]
    private ConnectionType _connectionType = ConnectionType.Serial;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusMessage = "Ready to connect";

    [ObservableProperty]
    private string _connectionStatusText = "Disconnected";

    [ObservableProperty]
    private IBrush _connectionStatusBrush = Brushes.Red;

    [ObservableProperty]
    private bool _isDownloadingParameters;

    [ObservableProperty]
    private string _parameterProgressText = "0/0";

    [ObservableProperty]
    private double _parameterProgressPercentage;

    [ObservableProperty]
    private int _systemId;

    [ObservableProperty]
    private int _componentId;

    [ObservableProperty]
    private int _parameterCount;

    // Connection type radio button bindings
    public bool IsSerialConnection
    {
        get => ConnectionType == ConnectionType.Serial;
        set { if (value) ConnectionType = ConnectionType.Serial; }
    }

    public bool IsTcpConnection
    {
        get => ConnectionType == ConnectionType.Tcp;
        set { if (value) ConnectionType = ConnectionType.Tcp; }
    }

    public bool IsBluetoothConnection
    {
        get => ConnectionType == ConnectionType.Bluetooth;
        set { if (value) ConnectionType = ConnectionType.Bluetooth; }
    }

    public ConnectionPageViewModel(
        IConnectionService connectionService, 
        IParameterService parameterService)
    {
        _connectionService = connectionService;
        _parameterService = parameterService;

        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _connectionService.AvailableSerialPortsChanged += OnAvailableSerialPortsChanged;
        _parameterService.ParameterDownloadProgressChanged += OnParameterDownloadProgressChanged;
        _downloadInProgress = _parameterService.IsParameterDownloadInProgress;

        var ports = _connectionService.GetAvailableSerialPorts().ToList();
        AvailableSerialPorts = new ObservableCollection<SerialPortInfo>(ports);
        if (ports.Any())
        {
            SelectedSerialPort = ports.First();
        }
        
        // Set default baud rate (115200)
        SelectedBaudRate = AvailableBaudRates.FirstOrDefault(b => b.Value == 115200);
    }

    private void OnAvailableSerialPortsChanged(object? sender, IEnumerable<SerialPortInfo> ports)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AvailableSerialPorts.Clear();
            foreach (var port in ports)
            {
                AvailableSerialPorts.Add(port);
            }

            var selectedPortName = SelectedSerialPort?.PortName;
            if (AvailableSerialPorts.Any() && (selectedPortName == null || !AvailableSerialPorts.Any(p => p.PortName == selectedPortName)))
            {
                SelectedSerialPort = AvailableSerialPorts.First();
            }
        });
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                IsConnected = connected;
                StatusMessage = connected ? "Connection established successfully" : "Disconnected";
                SetConnectionIndicator(connected ? "Connected" : "Disconnected", connected ? new SolidColorBrush(Color.Parse("#10B981")) : new SolidColorBrush(Color.Parse("#EF4444")));

                if (connected)
                {
                    // Trigger parameter download when connection is established
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _parameterService.RefreshParametersAsync();
                        }
                        catch (Exception ex)
                        {
                            await Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                StatusMessage = $"Parameter download failed: {ex.Message}";
                            });
                        }
                    });
                    
                    _downloadInProgress = true;
                    IsDownloadingParameters = true;
                    StatusMessage = "Connected - Downloading parameters...";
                }
                else
                {
                    var interruptedDownload = _downloadInProgress && !_parameterService.IsParameterDownloadComplete;
                    StatusMessage = interruptedDownload
                        ? "Disconnected during parameter download"
                        : "Disconnected";
                    _downloadInProgress = false;
                    IsDownloadingParameters = false;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during connection state change: {ex.Message}";
            }
        });
    }

    private void SetConnectionIndicator(string text, IBrush brush)
    {
        ConnectionStatusText = text;
        ConnectionStatusBrush = brush;
    }

    private void OnParameterDownloadProgressChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _downloadInProgress = _parameterService.IsParameterDownloadInProgress;
            IsDownloadingParameters = _parameterService.IsParameterDownloadInProgress;
            
            var received = _parameterService.ReceivedParameterCount;
            var expected = _parameterService.ExpectedParameterCount ?? 0;
            
            ParameterProgressText = $"{received}/{expected}";
            ParameterProgressPercentage = expected > 0 ? (received * 100.0 / expected) : 0;
            ParameterCount = received;

            if (_parameterService.IsParameterDownloadInProgress)
            {
                StatusMessage = $"Downloading parameters... {received}/{expected}";
            }
            else if (_parameterService.IsParameterDownloadComplete && _connectionService.IsConnected)
            {
                StatusMessage = $"Connected - {received} parameters loaded successfully";
                IsDownloadingParameters = false;
            }
        });
    }

    [RelayCommand]
    private async Task ScanBluetoothDevicesAsync()
    {
        try
        {
            StatusMessage = "Scanning for Bluetooth devices...";
            var devices = await _connectionService.GetAvailableBluetoothDevicesAsync();
            
            Dispatcher.UIThread.Post(() =>
            {
                AvailableBluetoothDevices.Clear();
                foreach (var device in devices)
                {
                    AvailableBluetoothDevices.Add(device);
                }

                if (AvailableBluetoothDevices.Any())
                {
                    SelectedBluetoothDevice = AvailableBluetoothDevices.First();
                    StatusMessage = $"Found {AvailableBluetoothDevices.Count} Bluetooth device(s)";
                }
                else
                {
                    StatusMessage = "No Bluetooth devices found. Ensure Bluetooth is enabled.";
                }
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Bluetooth scan failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        var settings = new ConnectionSettings
        {
            Type = ConnectionType,
            PortName = SelectedSerialPort?.PortName ?? string.Empty,
            BaudRate = BaudRate,
            IpAddress = IpAddress,
            Port = TcpPort,
            BluetoothDeviceAddress = SelectedBluetoothDevice?.DeviceAddress,
            BluetoothDeviceName = SelectedBluetoothDevice?.DeviceName
        };

        StatusMessage = "Connecting...";
        SetConnectionIndicator("Connecting", new SolidColorBrush(Color.Parse("#F59E0B")));
        var result = await _connectionService.ConnectAsync(settings);
        
        if (!result)
        {
            StatusMessage = "Connection failed. Please check your settings and try again.";
            SetConnectionIndicator("Disconnected", new SolidColorBrush(Color.Parse("#EF4444")));
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        StatusMessage = "Disconnecting...";
        await _connectionService.DisconnectAsync();
        SetConnectionIndicator("Disconnected", new SolidColorBrush(Color.Parse("#EF4444")));
        IsDownloadingParameters = false;
        ParameterProgressPercentage = 0;
        ParameterProgressText = "0/0";
    }

    partial void OnConnectionTypeChanged(ConnectionType value)
    {
        OnPropertyChanged(nameof(IsSerialConnection));
        OnPropertyChanged(nameof(IsTcpConnection));
        OnPropertyChanged(nameof(IsBluetoothConnection));
    }
}
