using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.MAVLink;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Service for managing drone connections via Serial, TCP, or Bluetooth.
/// Handles MAVLink communication and parameter transfer.
/// </summary>
public sealed class ConnectionService : IConnectionService, IDisposable
{
    private readonly ILogger<ConnectionService> _logger;
    private readonly IMavLinkMessageLogger _mavLinkLogger;
    private readonly object _lock = new();

    private SerialPort? _serialPort;
    private TcpClient? _tcpClient;
    private NetworkStream? _networkStream;
    private BluetoothMavConnection? _bluetoothConnection;
    private AsvMavlinkWrapper? _mavlink;

    private Stream? _inputStream;
    private Stream? _outputStream;

    private bool _isConnected;
    private bool _disposed;
    private bool _isDisconnecting;
    private ConnectionType _currentConnectionType;
    private bool _isArmed; // Track armed status from HEARTBEAT

    private readonly System.Timers.Timer _portScanTimer;
    private readonly System.Timers.Timer _connectionMonitorTimer;

    private List<SerialPortInfo> _cachedPorts = new();
    private DateTime _lastDataReceivedTime;
    private const int CONNECTION_MONITOR_INTERVAL_MS = 5000;
    private const int CONNECTION_TIMEOUT_SECONDS = 30;

    public bool IsConnected => _isConnected;
    public bool IsArmed => _isArmed; // Expose armed status

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<IEnumerable<SerialPortInfo>>? AvailableSerialPortsChanged;
    public event EventHandler<MavlinkParamValueEventArgs>? ParamValueReceived;
    public event EventHandler? HeartbeatReceived;
    public event EventHandler<HeartbeatDataEventArgs>? HeartbeatDataReceived;
    public event EventHandler<StatusTextEventArgs>? StatusTextReceived;
    public event EventHandler<RcChannelsEventArgs>? RcChannelsReceived;
    public event EventHandler<CommandAckEventArgs>? CommandAckReceived;
    public event EventHandler<CommandLongEventArgs>? CommandLongReceived;
    public event EventHandler<RawImuEventArgs>? RawImuReceived;

    public ConnectionService(ILogger<ConnectionService> logger, IMavLinkMessageLogger mavLinkLogger)
    {
        _logger = logger;
        _mavLinkLogger = mavLinkLogger;

        _portScanTimer = new System.Timers.Timer(3000);
        _portScanTimer.Elapsed += (_, _) => ScanSerialPorts();
        _portScanTimer.Start();

        _connectionMonitorTimer = new System.Timers.Timer(CONNECTION_MONITOR_INTERVAL_MS);
        _connectionMonitorTimer.Elapsed += MonitorConnection;

        ScanSerialPorts();
    }

    private void MonitorConnection(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!_isConnected || _isDisconnecting)
            return;

        try
        {
            var timeSinceLastData = DateTime.UtcNow - _lastDataReceivedTime;
            if (timeSinceLastData.TotalSeconds > CONNECTION_TIMEOUT_SECONDS)
            {
                _logger.LogWarning("Connection timeout - no MAVLink data received for {Seconds}s", timeSinceLastData.TotalSeconds);
                _ = Task.Run(async () => await HandleConnectionLostAsync());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in connection monitor");
        }
    }

    private async Task HandleConnectionLostAsync()
    {
        if (_isDisconnecting)
            return;

        _logger.LogWarning("Handling connection loss");
        await DisconnectAsync();
    }

    public IEnumerable<SerialPortInfo> GetAvailableSerialPorts()
    {
        lock (_lock)
        {
            return _cachedPorts.ToList();
        }
    }

    private void ScanSerialPorts()
    {
        try
        {
            var ports = new List<SerialPortInfo>();
            var portNames = SerialPort.GetPortNames();

            foreach (var portName in portNames)
            {
                var description = GetPortDescription(portName);
                ports.Add(new SerialPortInfo
                {
                    PortName = portName,
                    FriendlyName = description
                });
            }

            lock (_lock)
            {
                if (!ports.SequenceEqual(_cachedPorts, new SerialPortInfoComparer()))
                {
                    _cachedPorts = ports;
                    AvailableSerialPortsChanged?.Invoke(this, ports);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning serial ports");
        }
    }

    private static string GetPortDescription(string portName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%'");

            foreach (var obj in searcher.Get())
            {
                var caption = obj["Caption"]?.ToString();
                if (caption != null && caption.Contains(portName))
                {
                    return caption;
                }
            }
        }
        catch
        {
            // WMI not available
        }

        return portName;
    }

    public async Task<IEnumerable<BluetoothDeviceInfo>> GetAvailableBluetoothDevicesAsync()
    {
        var devices = new List<BluetoothDeviceInfo>();

        try
        {
            var connection = new BluetoothMavConnection(_logger);
            devices = (await connection.DiscoverDevicesAsync()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error scanning Bluetooth devices");
        }

        return devices;
    }

    public async Task<bool> ConnectAsync(ConnectionSettings settings)
    {
        if (_isConnected)
        {
            await DisconnectAsync();
        }

        _isDisconnecting = false;

        try
        {
            _currentConnectionType = settings.Type;

            switch (settings.Type)
            {
                case ConnectionType.Serial:
                    return await ConnectSerialAsync(settings);
                case ConnectionType.Tcp:
                    return await ConnectTcpAsync(settings);
                case ConnectionType.Bluetooth:
                    return await ConnectBluetoothAsync(settings);
                default:
                    _logger.LogError("Unsupported connection type: {Type}", settings.Type);
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection failed");
            await DisconnectAsync();
            return false;
        }
    }

    private async Task<bool> ConnectSerialAsync(ConnectionSettings settings)
    {
        _logger.LogInformation("Connecting to serial port {Port} at {Baud} baud",
            settings.PortName, settings.BaudRate);

        _serialPort = new SerialPort(settings.PortName, settings.BaudRate)
        {
            ReadTimeout = 5000,
            WriteTimeout = 5000,
            DtrEnable = true,
            RtsEnable = true
        };

        _serialPort.Open();

        _inputStream = _serialPort.BaseStream;
        _outputStream = _serialPort.BaseStream;

        var heartbeatTask = WaitForHeartbeatAsync(TimeSpan.FromSeconds(10));
        InitializeMavlink();
        var heartbeatReceived = await heartbeatTask;

        if (heartbeatReceived)
        {
            SetConnected(true);
            _connectionMonitorTimer.Start();
            _logger.LogInformation("Serial connection established");
            return true;
        }

        _logger.LogWarning("No heartbeat received on serial connection");
        await DisconnectAsync();
        return false;
    }

    private async Task<bool> ConnectTcpAsync(ConnectionSettings settings)
    {
        _logger.LogInformation("Connecting to TCP {Host}:{Port}",
            settings.IpAddress, settings.Port);

        try
        {
            _tcpClient = new TcpClient
            {
                NoDelay = true,
                ReceiveTimeout = 30000,
                SendTimeout = 5000,
                ReceiveBufferSize = 8192,
                SendBufferSize = 8192
            };

            var connectTask = _tcpClient.ConnectAsync(settings.IpAddress ?? "127.0.0.1", settings.Port);
            var timeoutTask = Task.Delay(10000);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _tcpClient?.Close();
                _tcpClient?.Dispose();
                _tcpClient = null;
                throw new TimeoutException("TCP connection timed out after 10 seconds");
            }

            if (!_tcpClient.Connected)
            {
                throw new SocketException((int)SocketError.ConnectionRefused);
            }

            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, 5);
            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, 1);
            _tcpClient.Client.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, 3);

            _networkStream = _tcpClient.GetStream();

            if (_networkStream == null)
            {
                throw new IOException("Failed to get network stream from TCP client");
            }

            _networkStream.ReadTimeout = 30000;
            _networkStream.WriteTimeout = 5000;

            _inputStream = _networkStream;
            _outputStream = _networkStream;

            _logger.LogInformation("TCP socket connected, initializing MAVLink");

            var heartbeatTask = WaitForHeartbeatAsync(TimeSpan.FromSeconds(15));
            InitializeMavlink();
            var heartbeatReceived = await heartbeatTask;

            if (heartbeatReceived)
            {
                SetConnected(true);
                _connectionMonitorTimer.Start();
                _logger.LogInformation("TCP connection established and verified");
                return true;
            }

            _logger.LogWarning("No heartbeat received on TCP connection within 15 seconds");
            await DisconnectAsync();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TCP connection failed");
            await DisconnectAsync();
            throw;
        }
    }

    private async Task<bool> ConnectBluetoothAsync(ConnectionSettings settings)
    {
        _logger.LogInformation("Connecting to Bluetooth device {Address}", settings.BluetoothDeviceAddress);

        _bluetoothConnection = new BluetoothMavConnection(_logger);

        try
        {
            var success = await _bluetoothConnection.ConnectAsync(settings.BluetoothDeviceAddress ?? string.Empty);

            if (!success)
            {
                _logger.LogWarning("Failed to connect to Bluetooth device");
                _bluetoothConnection.Dispose();
                _bluetoothConnection = null;
                return false;
            }

            _bluetoothConnection.HeartbeatReceived += OnBluetoothHeartbeat;
            _bluetoothConnection.ParamValueReceived += OnBluetoothParamValue;

            var heartbeatReceived = await WaitForHeartbeatAsync(TimeSpan.FromSeconds(15));

            if (heartbeatReceived)
            {
                SetConnected(true);
                _connectionMonitorTimer.Start();
                _logger.LogInformation("Bluetooth connection established");
                return true;
            }

            _logger.LogWarning("No heartbeat received on Bluetooth connection");
            _bluetoothConnection.Dispose();
            _bluetoothConnection = null;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bluetooth connection failed");
            _bluetoothConnection?.Dispose();
            _bluetoothConnection = null;
            throw;
        }
    }

    private void InitializeMavlink()
    {
        if (_inputStream == null || _outputStream == null)
        {
            _logger.LogError("Cannot initialize MAVLink - streams not available");
            return;
        }

        _mavlink = new AsvMavlinkWrapper(_logger, _mavLinkLogger);
        _mavlink.HeartbeatReceived += OnMavlinkHeartbeat;
        _mavlink.ParamValueReceived += OnMavlinkParamValue;
        _mavlink.HeartbeatDataReceived += OnMavlinkHeartbeatData;
        _mavlink.StatusTextReceived += OnMavlinkStatusText;
        _mavlink.RcChannelsReceived += OnMavlinkRcChannels;
        _mavlink.CommandAckReceived += OnMavlinkCommandAck;
        _mavlink.CommandLongReceived += OnMavlinkCommandLong;
        _mavlink.RawImuReceived += OnMavlinkRawImu;
        _mavlink.Initialize(_inputStream, _outputStream);

        _lastDataReceivedTime = DateTime.UtcNow;
        _logger.LogDebug("MAVLink initialized successfully");
    }

    private void OnMavlinkHeartbeat(object? sender, (byte SystemId, byte ComponentId) e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        HeartbeatReceived?.Invoke(this, EventArgs.Empty);
    }

    private void OnMavlinkParamValue(object? sender, (string Name, float Value, ushort Index, ushort Count) e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        var param = new DroneParameter
        {
            Name = e.Name,
            Value = e.Value
        };

        ParamValueReceived?.Invoke(this, new MavlinkParamValueEventArgs(param, e.Index, e.Count));
    }

    private void OnMavlinkHeartbeatData(object? sender, HeartbeatData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        
        // Track armed status from base_mode (bit 7 = MAV_MODE_FLAG_SAFETY_ARMED)
        _isArmed = e.IsArmed;
        
        HeartbeatDataReceived?.Invoke(this, new HeartbeatDataEventArgs
        {
            SystemId = e.SystemId,
            ComponentId = e.ComponentId,
            CustomMode = e.CustomMode,
            VehicleType = e.VehicleType,
            Autopilot = e.Autopilot,
            BaseMode = e.BaseMode,
            IsArmed = e.IsArmed
        });
    }

    private void OnMavlinkStatusText(object? sender, (byte Severity, string Text) e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        _logger.LogInformation("StatusText [{Severity}]: {Text}", e.Severity, e.Text);
        StatusTextReceived?.Invoke(this, new StatusTextEventArgs
        {
            Severity = e.Severity,
            Text = e.Text
        });
    }

    private void OnMavlinkRcChannels(object? sender, RcChannelsData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        RcChannelsReceived?.Invoke(this, new RcChannelsEventArgs
        {
            Channel1 = e.Channel1,
            Channel2 = e.Channel2,
            Channel3 = e.Channel3,
            Channel4 = e.Channel4,
            Channel5 = e.Channel5,
            Channel6 = e.Channel6,
            Channel7 = e.Channel7,
            Channel8 = e.Channel8,
            ChannelCount = e.ChannelCount,
            Rssi = e.Rssi
        });
    }

    private void OnMavlinkCommandAck(object? sender, (ushort Command, byte Result) e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        CommandAckReceived?.Invoke(this, new CommandAckEventArgs
        {
            Command = e.Command,
            Result = e.Result
        });
    }

    private void OnMavlinkCommandLong(object? sender, CommandLongData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        CommandLongReceived?.Invoke(this, new CommandLongEventArgs
        {
            SystemId = e.SystemId,
            ComponentId = e.ComponentId,
            Command = e.Command,
            Param1 = e.Param1,
            Param2 = e.Param2,
            Param3 = e.Param3,
            Param4 = e.Param4,
            Param5 = e.Param5,
            Param6 = e.Param6,
            Param7 = e.Param7,
            TargetSystem = e.TargetSystem,
            TargetComponent = e.TargetComponent,
            Confirmation = e.Confirmation
        });
    }

    private void OnMavlinkRawImu(object? sender, RawImuData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        var accel = e.GetAcceleration();
        var gyro = e.GetGyro();
        
        RawImuReceived?.Invoke(this, new RawImuEventArgs
        {
            AccelX = accel.X,
            AccelY = accel.Y,
            AccelZ = accel.Z,
            GyroX = gyro.X,
            GyroY = gyro.Y,
            GyroZ = gyro.Z,
            TimeUsec = e.TimeUsec,
            Temperature = e.GetTemperature()
        });
    }

    private void OnBluetoothHeartbeat(object? sender, (byte SystemId, byte ComponentId) e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        HeartbeatReceived?.Invoke(this, EventArgs.Empty);
    }

    private void OnBluetoothParamValue(object? sender, (string Name, float Value, ushort Index, ushort Count) e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        var param = new DroneParameter { Name = e.Name, Value = e.Value };
        ParamValueReceived?.Invoke(this, new MavlinkParamValueEventArgs(param, e.Index, e.Count));
    }

    private async Task<bool> WaitForHeartbeatAsync(TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<bool>();
        using var cts = new CancellationTokenSource(timeout);

        void OnHeartbeat(object? s, EventArgs e)
        {
            tcs.TrySetResult(true);
        }

        HeartbeatReceived += OnHeartbeat;
        cts.Token.Register(() => tcs.TrySetResult(false));

        try
        {
            return await tcs.Task;
        }
        finally
        {
            HeartbeatReceived -= OnHeartbeat;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_isDisconnecting)
            return;

        _isDisconnecting = true;
        _logger.LogInformation("Disconnecting...");

        _connectionMonitorTimer.Stop();

        try
        {
            if (_mavlink != null)
            {
                _mavlink.HeartbeatReceived -= OnMavlinkHeartbeat;
                _mavlink.ParamValueReceived -= OnMavlinkParamValue;
                _mavlink.HeartbeatDataReceived -= OnMavlinkHeartbeatData;
                _mavlink.StatusTextReceived -= OnMavlinkStatusText;
                _mavlink.RcChannelsReceived -= OnMavlinkRcChannels;
                _mavlink.CommandAckReceived -= OnMavlinkCommandAck;
                _mavlink.RawImuReceived -= OnMavlinkRawImu;
                _mavlink.Dispose();
                _mavlink = null;
            }

            if (_bluetoothConnection != null)
            {
                _bluetoothConnection.HeartbeatReceived -= OnBluetoothHeartbeat;
                _bluetoothConnection.ParamValueReceived -= OnBluetoothParamValue;
                _bluetoothConnection.Dispose();
                _bluetoothConnection = null;
            }

            try { await (_networkStream?.DisposeAsync() ?? ValueTask.CompletedTask); } catch { }
            _networkStream = null;

            try { _tcpClient?.Close(); } catch { }
            try { _tcpClient?.Dispose(); } catch { }
            _tcpClient = null;

            try { _serialPort?.Close(); } catch { }
            try { _serialPort?.Dispose(); } catch { }
            _serialPort = null;

            _inputStream = null;
            _outputStream = null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during disconnect cleanup");
        }

        SetConnected(false);
        _isDisconnecting = false;
        _logger.LogInformation("Disconnected");

        await Task.CompletedTask;
    }

    public Stream? GetTransportStream() => _inputStream;

    public void SendParamRequestList()
    {
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _ = _bluetoothConnection.SendParamRequestListAsync();
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send PARAM_REQUEST_LIST - not connected");
            return;
        }

        _ = _mavlink.SendParamRequestListAsync();
    }

    public void SendParamRequestRead(ushort paramIndex)
    {
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _ = _bluetoothConnection.SendParamRequestReadAsync(paramIndex);
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send PARAM_REQUEST_READ - not connected");
            return;
        }

        _ = _mavlink.SendParamRequestReadAsync(paramIndex);
    }

    public void SendParamSet(ParameterWriteRequest request)
    {
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _ = _bluetoothConnection.SendParamSetAsync(request.Name, request.Value);
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send PARAM_SET - not connected");
            return;
        }

        _ = _mavlink.SendParamSetAsync(request.Name, request.Value);
    }

    public void SendMotorTest(int motorInstance, int throttleType, float throttleValue, float timeout, int motorCount = 0, int testOrder = 0)
    {
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _ = _bluetoothConnection.SendMotorTestAsync(motorInstance, throttleType, throttleValue, timeout, motorCount, testOrder);
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send DO_MOTOR_TEST - not connected");
            return;
        }

        _ = _mavlink.SendMotorTestAsync(motorInstance, throttleType, throttleValue, timeout, motorCount, testOrder);
    }

    public void SendPreflightCalibration(int gyro, int mag, int groundPressure, int airspeed, int accel)
    {
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _ = _bluetoothConnection.SendPreflightCalibrationAsync(gyro, mag, groundPressure, airspeed, accel);
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send MAV_CMD_PREFLIGHT_CALIBRATION - not connected");
            return;
        }

        _ = _mavlink.SendPreflightCalibrationAsync(gyro, mag, groundPressure, airspeed, accel);
    }

    public void SendAccelCalVehiclePos(int position)
    {
        _logger.LogInformation("SendAccelCalVehiclePos called: position={Position}, connectionType={Type}, isConnected={Connected}", 
            position, _currentConnectionType, _isConnected);
        
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _logger.LogInformation("Sending via Bluetooth connection");
            _ = _bluetoothConnection.SendAccelCalVehiclePosAsync(position);
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send MAV_CMD_ACCELCAL_VEHICLE_POS - not connected (mavlink is null)");
            return;
        }

        _logger.LogInformation("Sending via MAVLink wrapper");
        _ = _mavlink.SendAccelCalVehiclePosAsync(position);
    }

    public void SendPreflightReboot(int autopilot, int companion)
    {
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _ = _bluetoothConnection.SendPreflightRebootAsync(autopilot, companion);
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN - not connected");
            return;
        }

        _ = _mavlink.SendPreflightRebootAsync(autopilot, companion);
    }

    public void SendFlashBootloaderCommand(int magicValue)
    {
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _logger.LogWarning("Flash bootloader command not supported via Bluetooth");
            throw new NotSupportedException("Flash bootloader command is not supported via Bluetooth connection");
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send MAV_CMD_FLASH_BOOTLOADER - not connected");
            throw new InvalidOperationException("Not connected to flight controller");
        }

        _ = _mavlink.SendFlashBootloaderAsync(magicValue);
    }

    public void SendArmDisarm(bool arm, bool force = false)
    {
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _ = _bluetoothConnection.SendArmDisarmAsync(arm, force);
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send MAV_CMD_COMPONENT_ARM_DISARM - not connected");
            return;
        }

        _ = _mavlink.SendArmDisarmAsync(arm, force);
    }

    public void SendResetParameters()
    {
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            // Bluetooth does not support reset - log warning
            _logger.LogWarning("Reset parameters via Bluetooth not yet supported");
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send MAV_CMD_PREFLIGHT_STORAGE - not connected");
            return;
        }

        _ = _mavlink.SendResetParametersAsync();
    }

    public void SendSetMessageInterval(int messageId, int intervalUs)
    {
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _logger.LogWarning("SET_MESSAGE_INTERVAL not supported via Bluetooth");
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send MAV_CMD_SET_MESSAGE_INTERVAL - not connected");
            return;
        }

        _ = _mavlink.SendSetMessageIntervalAsync(messageId, intervalUs);
    }

    public void SendRequestDataStream(int streamId, int rateHz, int startStop)
    {
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _logger.LogWarning("REQUEST_DATA_STREAM not supported via Bluetooth");
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send REQUEST_DATA_STREAM - not connected");
            return;
        }

        _ = _mavlink.SendRequestDataStreamAsync(streamId, rateHz, startStop);
    }

    private void SetConnected(bool connected)
    {
        if (_isConnected != connected)
        {
            _isConnected = connected;
            _logger.LogInformation("Connection state changed: {Connected}", connected);
            ConnectionStateChanged?.Invoke(this, connected);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _portScanTimer.Stop();
        _portScanTimer.Dispose();

        _connectionMonitorTimer.Stop();
        _connectionMonitorTimer.Dispose();

        DisconnectAsync().Wait(TimeSpan.FromSeconds(2));
    }

    private class SerialPortInfoComparer : IEqualityComparer<SerialPortInfo>
    {
        public bool Equals(SerialPortInfo? x, SerialPortInfo? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.PortName == y.PortName && x.FriendlyName == y.FriendlyName;
        }

        public int GetHashCode(SerialPortInfo obj)
        {
            return HashCode.Combine(obj.PortName, obj.FriendlyName);
        }
    }
}