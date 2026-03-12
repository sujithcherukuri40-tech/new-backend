using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.Services;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the connection shell window that appears after login.
/// Handles connection setup before showing the main application.
/// Supports auto-connect with saved connection settings.
/// </summary>
public partial class ConnectionShellViewModel : ViewModelBase
{
    private readonly IConnectionService _connectionService;
    private readonly IParameterService _parameterService;
    private readonly ConnectionSettingsStorage? _settingsStorage;
    private bool _hasNavigatedToMain;
    private readonly bool _skipAutoConnect;
    private bool _isManualDisconnect; // Track if disconnect was initiated by user

    /// <summary>
    /// Event raised when connection is successful and parameters are downloaded.
    /// </summary>
    public event EventHandler? ConnectionCompleted;

    /// <summary>
    /// Event raised when user cancels or closes the connection window.
    /// </summary>
    public event EventHandler? ConnectionCancelled;

    [ObservableProperty]
    private ObservableCollection<SerialPortInfo> _availableSerialPorts = new();

    [ObservableProperty]
    private SerialPortInfo? _selectedSerialPort;

    [ObservableProperty]
    private ObservableCollection<BluetoothDeviceInfo> _availableBluetoothDevices = new();

    [ObservableProperty]
    private BluetoothDeviceInfo? _selectedBluetoothDevice;

    public ObservableCollection<BaudRateOption> AvailableBaudRates { get; } =
    [
        new BaudRateOption { Value = 9600 },
        new BaudRateOption { Value = 57600 },
        new BaudRateOption { Value = 115200 },
        new BaudRateOption { Value = 230400 },
        new BaudRateOption { Value = 460800 },
        new BaudRateOption { Value = 921600 }
    ];

    [ObservableProperty]
    private BaudRateOption? _selectedBaudRate;

    public int BaudRate => SelectedBaudRate?.Value ?? 115200;

    [ObservableProperty]
    private string _ipAddress = "127.0.0.1";

    [ObservableProperty]
    private int _tcpPort = 5760;

    [ObservableProperty]
    private ConnectionType _connectionType = ConnectionType.Serial;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    private bool _isConnecting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    private bool _isConnected;

    [ObservableProperty]
    private string _statusMessage = "Select connection type and configure settings";

    [ObservableProperty]
    private bool _isDownloadingParameters;

    [ObservableProperty]
    private string _parameterProgressText = "0/0";

    [ObservableProperty]
    private double _parameterProgressPercentage;

    [ObservableProperty]
    private bool _enableAutoConnect;

    /// <summary>
    /// Determines if the Connect button should be enabled
    /// </summary>
    public bool CanConnect => !IsConnecting && !IsConnected;

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

    public ConnectionShellViewModel(
        IConnectionService connectionService,
        IParameterService parameterService,
        ConnectionSettingsStorage? settingsStorage = null)
    {
        _connectionService = connectionService;
        _parameterService = parameterService;
        _settingsStorage = settingsStorage;
        
        // Check if we're returning from a disconnection - skip auto-connect in this case
        // The parameter service will have data if we were previously connected
        _skipAutoConnect = _parameterService.ReceivedParameterCount > 0 || _parameterService.IsParameterDownloadInProgress;

        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _connectionService.AvailableSerialPortsChanged += OnAvailableSerialPortsChanged;
        _parameterService.ParameterDownloadProgressChanged += OnParameterDownloadProgressChanged;
        _parameterService.ParameterDownloadCompleted += OnParameterDownloadCompleted;

        // Load available ports
        var ports = _connectionService.GetAvailableSerialPorts().ToList();
        AvailableSerialPorts = new ObservableCollection<SerialPortInfo>(ports);
        if (ports.Count > 0)
        {
            SelectedSerialPort = ports[0];
        }

        SelectedBaudRate = AvailableBaudRates.FirstOrDefault(b => b.Value == 115200);

        // Load saved settings if available
        LoadSavedSettings();
    }

    /// <summary>
    /// Load saved connection settings for auto-connect
    /// </summary>
    private void LoadSavedSettings()
    {
        if (_settingsStorage == null) return;

        try
        {
            var (settings, autoConnect) = _settingsStorage.LoadSettings();

            if (settings != null)
            {
                ConnectionType = settings.Type;
                EnableAutoConnect = autoConnect;

                switch (settings.Type)
                {
                    case ConnectionType.Serial:
                        if (!string.IsNullOrEmpty(settings.PortName))
                        {
                            SelectedSerialPort = AvailableSerialPorts.FirstOrDefault(p => p.PortName == settings.PortName);
                        }
                        SelectedBaudRate = AvailableBaudRates.FirstOrDefault(b => b.Value == settings.BaudRate);
                        break;

                    case ConnectionType.Tcp:
                        IpAddress = settings.IpAddress ?? "127.0.0.1";
                        TcpPort = settings.Port;
                        break;

                    case ConnectionType.Bluetooth:
                        // Bluetooth device will be loaded when devices are scanned
                        break;
                }

                // Show appropriate status message based on whether we're returning from disconnection
                if (_skipAutoConnect)
                {
                    StatusMessage = "Disconnected. Click Connect to reconnect.";
                }
                else
                {
                    StatusMessage = "Last connection settings loaded";
                }
                
                // Trigger auto-connect if enabled AND we're not returning from a disconnection
                // Skip auto-connect when drone disconnected/rebooted to avoid spinning up background threads
                if (autoConnect && !_skipAutoConnect)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000); // Small delay to let UI settle
                        await Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            StatusMessage = "Auto-connecting...";
                            await ConnectAsync();
                        });
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load saved settings: {ex.Message}");
        }
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
            if (AvailableSerialPorts.Count > 0 && (selectedPortName == null || !AvailableSerialPorts.Any(p => p.PortName == selectedPortName)))
            {
                SelectedSerialPort = AvailableSerialPorts[0];
            }
        });
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Console.WriteLine($"[ConnectionShell] Connection state changed: {connected}");
        
        Dispatcher.UIThread.Post(() =>
        {
            var wasConnected = IsConnected;
            IsConnected = connected;
            IsConnecting = false;

            if (connected)
            {
                IsDownloadingParameters = true;
                StatusMessage = "Connected - Downloading parameters...";
                _isManualDisconnect = false; // Reset flag on new connection

                // Save connection settings for auto-connect
                SaveConnectionSettings();

                // Trigger parameter download using async/await (not Task.Run background thread)
                // This ensures proper synchronization and avoids issues with disconnection/reconnection
                _ = DownloadParametersAsync();
            }
            else
            {
                IsDownloadingParameters = false;
                StatusMessage = "Disconnected";
                _hasNavigatedToMain = false;
                
                // If disconnection was NOT manual (drone lost connection or rebooted),
                // disable auto-connect to prevent unwanted reconnection on next app start
                if (wasConnected && !_isManualDisconnect && _settingsStorage != null)
                {
                    try
                    {
                        var (settings, _) = _settingsStorage.LoadSettings();
                        if (settings != null)
                        {
                            _settingsStorage.SaveSettings(settings, enableAutoConnect: false);
                            EnableAutoConnect = false;
                            Console.WriteLine("[ConnectionShell] Auto-connect disabled after unexpected disconnect");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[ConnectionShell] Failed to disable auto-connect: {ex.Message}");
                    }
                }
                
                // Reset the manual disconnect flag for next connection
                _isManualDisconnect = false;
            }
        });
    }

    /// <summary>
    /// Download parameters from the connected drone.
    /// Uses async/await to avoid creating separate background threads that can cause issues during disconnection.
    /// </summary>
    private async Task DownloadParametersAsync()
    {
        try
        {
            Console.WriteLine("[ConnectionShell] Starting parameter download...");
            await _parameterService.RefreshParametersAsync();
            Console.WriteLine("[ConnectionShell] Parameter download completed via RefreshParametersAsync");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConnectionShell] Parameter download error: {ex.Message}");
            // Show error message and let user retry
            Dispatcher.UIThread.Post(() =>
            {
                IsDownloadingParameters = false;
                StatusMessage = $"Parameter download failed: {ex.Message}. Please reconnect.";
            });
        }
    }

    /// <summary>
    /// Save current connection settings after successful connection
    /// </summary>
    private void SaveConnectionSettings()
    {
        if (_settingsStorage == null) return;

        try
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

            _settingsStorage.SaveSettings(settings, EnableAutoConnect);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save connection settings: {ex.Message}");
        }
    }

    private void OnParameterDownloadProgressChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsDownloadingParameters = _parameterService.IsParameterDownloadInProgress;

            var received = _parameterService.ReceivedParameterCount;
            var expected = _parameterService.ExpectedParameterCount ?? 0;

            ParameterProgressText = $"{received}/{expected}";
            ParameterProgressPercentage = expected > 0 ? (received * 100.0 / expected) : 0;

            if (_parameterService.IsParameterDownloadInProgress)
            {
                StatusMessage = $"Downloading parameters... {received}/{expected}";
            }
        });
    }

    private void OnParameterDownloadCompleted(object? sender, bool completedSuccessfully)
    {
        Console.WriteLine($"[ConnectionShell] Parameter download completed: {completedSuccessfully}, IsParameterDownloadComplete: {_parameterService.IsParameterDownloadComplete}");
        
        Dispatcher.UIThread.Post(() =>
        {
            IsDownloadingParameters = false;

            if (IsConnected)
            {
                // IMPORTANT: Only navigate to main when parameters are FULLY downloaded
                // This ensures auto-connect waits for all parameters before opening the app
                if (completedSuccessfully && _parameterService.IsParameterDownloadComplete)
                {
                    StatusMessage = "Connection successful! Opening configurator...";
                    NavigateToMainIfConnected();
                }
                else
                {
                    // Parameters not fully downloaded - stay on connection page and show status
                    var received = _parameterService.ReceivedParameterCount;
                    var expected = _parameterService.ExpectedParameterCount ?? 0;
                    StatusMessage = $"Parameter download incomplete ({received}/{expected}). Retrying...";
                    Console.WriteLine($"[ConnectionShell] Parameters incomplete - NOT navigating. Received: {received}, Expected: {expected}");
                    
                    // Do NOT navigate - user stays on connection page
                    // They can manually disconnect and reconnect if needed
                }
            }
        });
    }

    private void NavigateToMainIfConnected()
    {
        if (_hasNavigatedToMain)
        {
            Console.WriteLine("[ConnectionShell] Already navigated to main, skipping");
            return;
        }
        
        if (!IsConnected)
        {
            Console.WriteLine("[ConnectionShell] Not connected, skipping navigation");
            return;
        }
        
        // Triple-check parameters are downloaded before navigating
        if (!_parameterService.IsParameterDownloadComplete)
        {
            Console.WriteLine("[ConnectionShell] Parameters not fully downloaded, skipping navigation");
            StatusMessage = "Waiting for parameters to download...";
            return;
        }
        
        // Additional check: Ensure we have received parameters
        if (_parameterService.ReceivedParameterCount == 0)
        {
            Console.WriteLine("[ConnectionShell] No parameters received yet, skipping navigation");
            StatusMessage = "Waiting for parameters to download...";
            return;
        }

        _hasNavigatedToMain = true;
        Console.WriteLine($"[ConnectionShell] Navigating to main window with {_parameterService.ReceivedParameterCount} parameters downloaded");

        StatusMessage = "Parameters loaded! Opening configurator...";
        
        // Fire the connection completed event - App.axaml.cs will handle the actual window transition
        ConnectionCompleted?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (IsConnecting || IsConnected) return;

        IsConnecting = true;
        _hasNavigatedToMain = false;
        StatusMessage = "Connecting...";

        Console.WriteLine($"[ConnectionShell] Attempting connection: Type={ConnectionType}, Port={SelectedSerialPort?.PortName}, BaudRate={BaudRate}");

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

        var result = await _connectionService.ConnectAsync(settings);

        Console.WriteLine($"[ConnectionShell] Connection result: {result}");

        if (!result)
        {
            IsConnecting = false;
            StatusMessage = "Connection failed. Please check your settings and try again.";
        }
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        // Mark this as a manual disconnect to distinguish from unexpected disconnections
        _isManualDisconnect = true;
        
        // Disable auto-connect when user manually disconnects
        if (_settingsStorage != null)
        {
            try
            {
                // Load existing settings
                var (settings, _) = _settingsStorage.LoadSettings();
                
                if (settings != null)
                {
                    // Save settings with auto-connect disabled
                    _settingsStorage.SaveSettings(settings, enableAutoConnect: false);
                    Console.WriteLine("[ConnectionShell] Auto-connect disabled after manual disconnect");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ConnectionShell] Failed to disable auto-connect: {ex.Message}");
            }
        }
        
        await _connectionService.DisconnectAsync();
        IsDownloadingParameters = false;
        ParameterProgressPercentage = 0;
        ParameterProgressText = "0/0";
        StatusMessage = "Disconnected";
        _hasNavigatedToMain = false;
        
        // Also update the UI checkbox state
        EnableAutoConnect = false;
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

                if (AvailableBluetoothDevices.Count > 0)
                {
                    SelectedBluetoothDevice = AvailableBluetoothDevices[0];
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
    private void Cancel()
    {
        ConnectionCancelled?.Invoke(this, EventArgs.Empty);
    }

    partial void OnConnectionTypeChanged(ConnectionType value)
    {
        OnPropertyChanged(nameof(IsSerialConnection));
        OnPropertyChanged(nameof(IsTcpConnection));
        OnPropertyChanged(nameof(IsBluetoothConnection));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _connectionService.AvailableSerialPortsChanged -= OnAvailableSerialPortsChanged;
            _parameterService.ParameterDownloadProgressChanged -= OnParameterDownloadProgressChanged;
            _parameterService.ParameterDownloadCompleted -= OnParameterDownloadCompleted;
        }
        base.Dispose(disposing);
    }
}
