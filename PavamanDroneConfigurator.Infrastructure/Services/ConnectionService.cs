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

// Resolve ambiguous types - use MAVLink namespace types for internal use
using MavLinkMagCalProgressData = PavamanDroneConfigurator.Infrastructure.MAVLink.MagCalProgressData;
using MavLinkMagCalReportData = PavamanDroneConfigurator.Infrastructure.MAVLink.MagCalReportData;
using MavLinkAutopilotVersionData = PavamanDroneConfigurator.Infrastructure.MAVLink.AutopilotVersionData;
using GlobalPositionIntData = PavamanDroneConfigurator.Infrastructure.MAVLink.GlobalPositionIntData;
using AttitudeData = PavamanDroneConfigurator.Infrastructure.MAVLink.AttitudeData;
using VfrHudData = PavamanDroneConfigurator.Infrastructure.MAVLink.VfrHudData;
using GpsRawIntData = PavamanDroneConfigurator.Infrastructure.MAVLink.GpsRawIntData;
using SysStatusData = PavamanDroneConfigurator.Infrastructure.MAVLink.SysStatusData;

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
    private bool _isArmed;

    private readonly System.Timers.Timer _portScanTimer;
    private readonly System.Timers.Timer _connectionMonitorTimer;

    private List<SerialPortInfo> _cachedPorts = new();
    private DateTime _lastDataReceivedTime;
    private const int CONNECTION_MONITOR_INTERVAL_MS = 1000;
    private const int CONNECTION_TIMEOUT_SECONDS = 1;

    public bool IsConnected => _isConnected;
    public bool IsArmed => _isArmed;

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
    public event EventHandler<MagCalProgressEventArgs>? MagCalProgressReceived;
    public event EventHandler<MagCalReportEventArgs>? MagCalReportReceived;
    public event EventHandler<AutopilotVersionDataEventArgs>? AutopilotVersionReceived;
    public event EventHandler? RebootInitiated;
    
    // Telemetry events
    public event EventHandler<GlobalPositionIntEventArgs>? GlobalPositionIntReceived;
    public event EventHandler<AttitudeEventArgs>? AttitudeReceived;
    public event EventHandler<VfrHudEventArgs>? VfrHudReceived;
    public event EventHandler<GpsRawIntEventArgs>? GpsRawIntReceived;
    public event EventHandler<SysStatusEventArgs>? SysStatusReceived;

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
            _logger.LogInformation("Scanning for Bluetooth devices...");
            var connection = new BluetoothMavConnection(_logger, _mavLinkLogger);
            devices = (await connection.DiscoverDevicesAsync()).ToList();
            
            if (devices.Count == 0)
            {
                _logger.LogWarning("No Bluetooth devices discovered");
            }
            else
            {
                _logger.LogInformation("Discovered {Count} Bluetooth device(s)", devices.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning Bluetooth devices");
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
            ReadTimeout = 2000,
            WriteTimeout = 2000,
            DtrEnable = true,
            RtsEnable = true
        };

        _serialPort.Open();

        _inputStream = _serialPort.BaseStream;
        _outputStream = _serialPort.BaseStream;

        var heartbeatTask = WaitForHeartbeatAsync(TimeSpan.FromSeconds(5));
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
                ReceiveTimeout = 5000,
                SendTimeout = 2000,
                ReceiveBufferSize = 8192,
                SendBufferSize = 8192
            };

            var connectTask = _tcpClient.ConnectAsync(settings.IpAddress ?? "127.0.0.1", settings.Port);
            var timeoutTask = Task.Delay(5000);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _tcpClient?.Close();
                _tcpClient?.Dispose();
                _tcpClient = null;
                throw new TimeoutException("TCP connection timed out after 5 seconds");
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

            _networkStream.ReadTimeout = 5000;
            _networkStream.WriteTimeout = 2000;

            _inputStream = _networkStream;
            _outputStream = _networkStream;

            _logger.LogInformation("TCP socket connected, initializing MAVLink");

            var heartbeatTask = WaitForHeartbeatAsync(TimeSpan.FromSeconds(5));
            InitializeMavlink();
            var heartbeatReceived = await heartbeatTask;

            if (heartbeatReceived)
            {
                SetConnected(true);
                _connectionMonitorTimer.Start();
                _logger.LogInformation("TCP connection established and verified");
                return true;
            }

            _logger.LogWarning("No heartbeat received on TCP connection within 5 seconds");
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

            // Subscribe to all MAVLink events from Bluetooth connection
            _bluetoothConnection.HeartbeatReceived += OnBluetoothHeartbeat;
            _bluetoothConnection.ParamValueReceived += OnBluetoothParamValue;
            _bluetoothConnection.HeartbeatDataReceived += OnBluetoothHeartbeatData;
            _bluetoothConnection.StatusTextReceived += OnBluetoothStatusText;
            _bluetoothConnection.RcChannelsReceived += OnBluetoothRcChannels;
            _bluetoothConnection.CommandAckReceived += OnBluetoothCommandAck;
            _bluetoothConnection.CommandLongReceived += OnBluetoothCommandLong;
            _bluetoothConnection.RawImuReceived += OnBluetoothRawImu;
            _bluetoothConnection.MagCalProgressReceived += OnBluetoothMagCalProgress;
            _bluetoothConnection.MagCalReportReceived += OnBluetoothMagCalReport;

            var heartbeatReceived = await WaitForHeartbeatAsync(TimeSpan.FromSeconds(5));

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
        _mavlink.MagCalProgressReceived += OnMavlinkMagCalProgress;
        _mavlink.MagCalReportReceived += OnMavlinkMagCalReport;
        _mavlink.AutopilotVersionReceived += OnMavlinkAutopilotVersion;
        
        // Telemetry events
        _mavlink.GlobalPositionIntReceived += OnMavlinkGlobalPositionInt;
        _mavlink.AttitudeReceived += OnMavlinkAttitude;
        _mavlink.VfrHudReceived += OnMavlinkVfrHud;
        _mavlink.GpsRawIntReceived += OnMavlinkGpsRawInt;
        _mavlink.SysStatusReceived += OnMavlinkSysStatus;
        
        _mavlink.Initialize(_inputStream, _outputStream);

        _lastDataReceivedTime = DateTime.UtcNow;
        _logger.LogDebug("MAVLink initialized successfully");
    }

    #region MAVLink Event Handlers

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
        _isArmed = e.IsArmed;
        
        HeartbeatDataReceived?.Invoke(this, new HeartbeatDataEventArgs
        {
            SystemId = e.SystemId,
            ComponentId = e.ComponentId,
            CustomMode = e.CustomMode,
            VehicleType = e.VehicleType,
            Autopilot = e.Autopilot,
            BaseMode = e.BaseMode,
            SystemStatus = e.SystemStatus,
            IsArmed = e.IsArmed,
            CrcValid = e.CrcValid
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
        
        _logger.LogInformation("COMMAND_LONG received: cmd={Command}, param1={Param1}, sysid={SysId}, compid={CompId}",
            e.Command, e.Param1, e.SystemId, e.ComponentId);
        
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

    private void OnMavlinkMagCalProgress(object? sender, MavLinkMagCalProgressData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        _logger.LogDebug("MAG_CAL_PROGRESS: compass={CompassId} status={Status} pct={Pct}%",
            e.CompassId, e.CalStatus, e.CompletionPct);
        
        MagCalProgressReceived?.Invoke(this, new MagCalProgressEventArgs
        {
            CompassId = e.CompassId,
            CalMask = e.CalMask,
            CalStatus = e.CalStatus,
            Attempt = e.Attempt,
            CompletionPct = e.CompletionPct,
            CompletionMask = e.CompletionMask,
            DirectionX = e.DirectionX,
            DirectionY = e.DirectionY,
            DirectionZ = e.DirectionZ
        });
    }

    private void OnMavlinkMagCalReport(object? sender, MavLinkMagCalReportData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        _logger.LogInformation("MAG_CAL_REPORT: compass={CompassId} status={Status} fitness={Fitness} offsets=({X}, {Y}, {Z})",
            e.CompassId, e.CalStatus, e.Fitness, e.OfsX, e.OfsY, e.OfsZ);
        
        MagCalReportReceived?.Invoke(this, new MagCalReportEventArgs
        {
            CompassId = e.CompassId,
            CalMask = e.CalMask,
            CalStatus = e.CalStatus,
            Autosaved = e.Autosaved,
            Fitness = e.Fitness,
            OfsX = e.OfsX,
            OfsY = e.OfsY,
            OfsZ = e.OfsZ,
            DiagX = e.DiagX,
            DiagY = e.DiagY,
            DiagZ = e.DiagZ,
            OffdiagX = e.OffdiagX,
            OffdiagY = e.OffdiagY,
            OffdiagZ = e.OffdiagZ,
            OrientationConfidence = e.OrientationConfidence,
            OldOrientation = e.OldOrientation,
            NewOrientation = e.NewOrientation,
            ScaleFactor = e.ScaleFactor
        });
    }

    private void OnMavlinkAutopilotVersion(object? sender, MavLinkAutopilotVersionData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        _logger.LogInformation("AUTOPILOT_VERSION: FW={FirmwareVersion}, Board=0x{Board:X8}, FC_ID={FcId}",
            e.FirmwareVersionString, e.BoardVersion, e.GetFcId());
        
        AutopilotVersionReceived?.Invoke(this, new AutopilotVersionDataEventArgs
        {
            SystemId = e.SystemId,
            ComponentId = e.ComponentId,
            Capabilities = e.Capabilities,
            FlightSwVersion = e.FlightSwVersion,
            MiddlewareSwVersion = e.MiddlewareSwVersion,
            OsSwVersion = e.OsSwVersion,
            BoardVersion = e.BoardVersion,
            FlightCustomVersion = e.FlightCustomVersion,
            VendorId = e.VendorId,
            ProductId = e.ProductId,
            Uid = e.Uid,
            Uid2 = e.Uid2,
            FirmwareMajor = e.FirmwareMajor,
            FirmwareMinor = e.FirmwareMinor,
            FirmwarePatch = e.FirmwarePatch,
            FirmwareType = e.FirmwareType,
            FirmwareVersionString = e.FirmwareVersionString
        });
    }

    private void OnMavlinkGlobalPositionInt(object? sender, GlobalPositionIntData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        GlobalPositionIntReceived?.Invoke(this, new GlobalPositionIntEventArgs
        {
            TimeBootMs = e.TimeBootMs,
            Latitude = e.Latitude,
            Longitude = e.Longitude,
            AltitudeMsl = e.AltitudeMsl,
            AltitudeRelative = e.AltitudeRelative,
            VelocityX = e.VelocityX,
            VelocityY = e.VelocityY,
            VelocityZ = e.VelocityZ,
            Heading = e.Heading,
            CrcValid = e.CrcValid
        });
    }

    private void OnMavlinkAttitude(object? sender, AttitudeData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        AttitudeReceived?.Invoke(this, new AttitudeEventArgs
        {
            TimeBootMs = e.TimeBootMs,
            Roll = e.Roll,
            Pitch = e.Pitch,
            Yaw = e.Yaw,
            RollSpeed = e.RollSpeed,
            PitchSpeed = e.PitchSpeed,
            YawSpeed = e.YawSpeed,
            CrcValid = e.CrcValid
        });
    }

    private void OnMavlinkVfrHud(object? sender, VfrHudData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        VfrHudReceived?.Invoke(this, new VfrHudEventArgs
        {
            Airspeed = e.Airspeed,
            GroundSpeed = e.GroundSpeed,
            Heading = e.Heading,
            Throttle = e.Throttle,
            Altitude = e.Altitude,
            ClimbRate = e.ClimbRate,
            CrcValid = e.CrcValid
        });
    }

    private void OnMavlinkGpsRawInt(object? sender, GpsRawIntData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        GpsRawIntReceived?.Invoke(this, new GpsRawIntEventArgs
        {
            TimeUsec = e.TimeUsec,
            FixType = e.FixType,
            Latitude = e.Latitude,
            Longitude = e.Longitude,
            Altitude = e.Altitude,
            Hdop = e.Hdop,
            Vdop = e.Vdop,
            GroundSpeed = e.GroundSpeed,
            CourseOverGround = e.CourseOverGround,
            SatellitesVisible = e.SatellitesVisible,
            CrcValid = e.CrcValid
        });
    }

    private void OnMavlinkSysStatus(object? sender, SysStatusData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        SysStatusReceived?.Invoke(this, new SysStatusEventArgs
        {
            SensorsPresent = e.SensorsPresent,
            SensorsEnabled = e.SensorsEnabled,
            SensorsHealth = e.SensorsHealth,
            Load = e.Load,
            BatteryVoltage = e.BatteryVoltage,
            BatteryCurrent = e.BatteryCurrent,
            BatteryRemaining = e.BatteryRemaining,
            DropRateComm = e.DropRateComm,
            ErrorsComm = e.ErrorsComm,
            CrcValid = e.CrcValid
        });
    }

    #endregion

    #region Bluetooth Event Handlers

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
    
    private void OnBluetoothHeartbeatData(object? sender, HeartbeatData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        _isArmed = e.IsArmed;
        
        HeartbeatDataReceived?.Invoke(this, new HeartbeatDataEventArgs
        {
            SystemId = e.SystemId,
            ComponentId = e.ComponentId,
            CustomMode = e.CustomMode,
            VehicleType = e.VehicleType,
            Autopilot = e.Autopilot,
            BaseMode = e.BaseMode,
            SystemStatus = e.SystemStatus,
            IsArmed = e.IsArmed
        });
    }
    
    private void OnBluetoothStatusText(object? sender, (byte Severity, string Text) e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        _logger.LogInformation("Bluetooth StatusText [{Severity}]: {Text}", e.Severity, e.Text);
        StatusTextReceived?.Invoke(this, new StatusTextEventArgs
        {
            Severity = e.Severity,
            Text = e.Text
        });
    }
    
    private void OnBluetoothRcChannels(object? sender, RcChannelsData e)
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
    
    private void OnBluetoothCommandAck(object? sender, (ushort Command, byte Result) e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        _logger.LogInformation("Bluetooth COMMAND_ACK: cmd={Command}, result={Result}", e.Command, e.Result);
        CommandAckReceived?.Invoke(this, new CommandAckEventArgs
        {
            Command = e.Command,
            Result = e.Result
        });
    }
    
    private void OnBluetoothCommandLong(object? sender, CommandLongData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        
        _logger.LogInformation("Bluetooth COMMAND_LONG received: cmd={Command}, param1={Param1}, sysid={SysId}, compid={CompId}",
            e.Command, e.Param1, e.SystemId, e.ComponentId);
        
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
    
    private void OnBluetoothRawImu(object? sender, RawImuData e)
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

    private void OnBluetoothMagCalProgress(object? sender, MavLinkMagCalProgressData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        MagCalProgressReceived?.Invoke(this, new MagCalProgressEventArgs
        {
            CompassId = e.CompassId,
            CalMask = e.CalMask,
            CalStatus = e.CalStatus,
            Attempt = e.Attempt,
            CompletionPct = e.CompletionPct,
            CompletionMask = e.CompletionMask,
            DirectionX = e.DirectionX,
            DirectionY = e.DirectionY,
            DirectionZ = e.DirectionZ
        });
    }

    private void OnBluetoothMagCalReport(object? sender, MavLinkMagCalReportData e)
    {
        _lastDataReceivedTime = DateTime.UtcNow;
        MagCalReportReceived?.Invoke(this, new MagCalReportEventArgs
        {
            CompassId = e.CompassId,
            CalMask = e.CalMask,
            CalStatus = e.CalStatus,
            Autosaved = e.Autosaved,
            Fitness = e.Fitness,
            OfsX = e.OfsX,
            OfsY = e.OfsY,
            OfsZ = e.OfsZ,
            DiagX = e.DiagX,
            DiagY = e.DiagY,
            DiagZ = e.DiagZ,
            OffdiagX = e.OffdiagX,
            OffdiagY = e.OffdiagY,
            OffdiagZ = e.OffdiagZ,
            OrientationConfidence = e.OrientationConfidence,
            OldOrientation = e.OldOrientation,
            NewOrientation = e.NewOrientation,
            ScaleFactor = e.ScaleFactor
        });
    }

    #endregion

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
                _mavlink.CommandLongReceived -= OnMavlinkCommandLong;
                _mavlink.RawImuReceived -= OnMavlinkRawImu;
                _mavlink.MagCalProgressReceived -= OnMavlinkMagCalProgress;
                _mavlink.MagCalReportReceived -= OnMavlinkMagCalReport;
                _mavlink.AutopilotVersionReceived -= OnMavlinkAutopilotVersion;
                _mavlink.GlobalPositionIntReceived -= OnMavlinkGlobalPositionInt;
                _mavlink.AttitudeReceived -= OnMavlinkAttitude;
                _mavlink.VfrHudReceived -= OnMavlinkVfrHud;
                _mavlink.GpsRawIntReceived -= OnMavlinkGpsRawInt;
                _mavlink.SysStatusReceived -= OnMavlinkSysStatus;
                try { (_mavlink as IDisposable)?.Dispose(); } catch { }
                _mavlink = null;
            }

            if (_bluetoothConnection != null)
            {
                _bluetoothConnection.HeartbeatReceived -= OnBluetoothHeartbeat;
                _bluetoothConnection.ParamValueReceived -= OnBluetoothParamValue;
                _bluetoothConnection.HeartbeatDataReceived -= OnBluetoothHeartbeatData;
                _bluetoothConnection.StatusTextReceived -= OnBluetoothStatusText;
                _bluetoothConnection.RcChannelsReceived -= OnBluetoothRcChannels;
                _bluetoothConnection.CommandAckReceived -= OnBluetoothCommandAck;
                _bluetoothConnection.CommandLongReceived -= OnBluetoothCommandLong;
                _bluetoothConnection.RawImuReceived -= OnBluetoothRawImu;
                _bluetoothConnection.MagCalProgressReceived -= OnBluetoothMagCalProgress;
                _bluetoothConnection.MagCalReportReceived -= OnBluetoothMagCalReport;
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

    #region Send Methods

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

    public async Task SendPreflightCalibrationAsync(int gyro, int mag, int groundPressure, int airspeed, int accel)
    {
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            await _bluetoothConnection.SendPreflightCalibrationAsync(gyro, mag, groundPressure, airspeed, accel);
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send MAV_CMD_PREFLIGHT_CALIBRATION - not connected");
            throw new InvalidOperationException("Not connected");
        }

        await _mavlink.SendPreflightCalibrationAsync(gyro, mag, groundPressure, airspeed, accel);
    }

    public void SendCancelPreflightCalibration()
    {
        _logger.LogInformation("Sending cancel preflight calibration command");
        
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _ = _bluetoothConnection.SendCancelPreflightCalibrationAsync();
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send cancel calibration - not connected");
            return;
        }

        _ = _mavlink.SendCancelPreflightCalibrationAsync();
    }

    public void SendAccelCalVehiclePos(int position)
    {
        _logger.LogInformation("SendAccelCalVehiclePos: position={Position} [FIRE-AND-FORGET]", position);
        
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _logger.LogInformation("Sending via Bluetooth connection (raw/fire-and-forget)");
            _bluetoothConnection.SendAccelCalVehiclePosRaw(position);
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send MAV_CMD_ACCELCAL_VEHICLE_POS - not connected (mavlink is null)");
            return;
        }

        _logger.LogInformation("Sending via MAVLink wrapper (raw/fire-and-forget - Mission Planner style)");
        _mavlink.SendAccelCalVehiclePosRaw(position);
    }

    /// <summary>
    /// Prepares for a flight controller reboot by stopping all timers and cleaning up resources.
    /// Should be called before sending the reboot command.
    /// </summary>
    public void PrepareForReboot()
    {
        _logger.LogInformation("Preparing for reboot - stopping all timers and notifying components");
        
        // Stop connection monitor timer to prevent false disconnect detection during reboot
        _connectionMonitorTimer.Stop();
        
        // Notify all components to stop their timers and cleanup
        // This allows CalibrationService, DroneInfoService, and other components 
        // to stop their internal timers before the reboot
        RebootInitiated?.Invoke(this, EventArgs.Empty);
    }

    public void SendPreflightReboot(int autopilot, int companion)
    {
        // Notify all components to stop timers and cleanup before reboot
        PrepareForReboot();
        
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
        _logger.LogInformation("Sending flash bootloader command with magic={MagicValue}", magicValue);
        
        if (!IsConnected)
        {
            _logger.LogWarning("Cannot send flash bootloader command - not connected");
            return;
        }

        if (_mavlink != null)
        {
            _ = _mavlink.SendFlashBootloaderAsync(magicValue);
        }
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

    public void SendReturnToLaunch()
    {
        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _ = _bluetoothConnection.SendReturnToLaunchAsync();
            return;
        }

        if (_mavlink == null)
        {
            _logger.LogWarning("Cannot send MAV_CMD_NAV_RETURN_TO_LAUNCH - not connected");
            return;
        }

        _ = _mavlink.SendReturnToLaunchAsync();
    }

    public void SendResetParameters()
    {
        _logger.LogInformation("Sending reset parameters command");
        
        if (!IsConnected)
        {
            _logger.LogWarning("Cannot send reset parameters - not connected");
            return;
        }

        if (_mavlink != null)
        {
            _ = _mavlink.SendResetParametersAsync();
        }
    }

    public void SendSetMessageInterval(int messageId, int intervalUs)
    {
        SendTelemetryNegotiationCommand(new TelemetryNegotiationCommand
        {
            Type = TelemetryNegotiationCommandType.SetMessageInterval,
            MessageId = messageId,
            IntervalUs = intervalUs,
            Name = $"MSG_{messageId}"
        });
    }

    public void SendRequestDataStream(int streamId, int rateHz, int startStop)
    {
        SendTelemetryNegotiationCommand(new TelemetryNegotiationCommand
        {
            Type = TelemetryNegotiationCommandType.RequestDataStream,
            StreamId = streamId,
            RateHz = rateHz,
            StartStop = startStop,
            Name = $"STREAM_{streamId}"
        });
    }

    public void SendTelemetryNegotiationCommand(TelemetryNegotiationCommand command)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Cannot send telemetry negotiation command - not connected. Type={Type}", command.Type);
            return;
        }

        switch (command.Type)
        {
            case TelemetryNegotiationCommandType.RequestDataStream:
                _logger.LogInformation("ASV command sent: REQUEST_DATA_STREAM streamId={StreamId}, rate={Rate}Hz, startStop={StartStop}, name={Name}",
                    command.StreamId, command.RateHz, command.StartStop, command.Name);

                if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
                {
                    _ = _bluetoothConnection.SendRequestDataStreamAsync(command.StreamId, command.RateHz, command.StartStop);
                    return;
                }

                if (_mavlink != null)
                {
                    _ = _mavlink.SendRequestDataStreamAsync(command.StreamId, command.RateHz, command.StartStop);
                }
                return;

            case TelemetryNegotiationCommandType.SetMessageInterval:
                _logger.LogInformation("ASV command sent: MAV_CMD_SET_MESSAGE_INTERVAL msgId={MessageId}, intervalUs={IntervalUs}, rate={Rate}Hz, name={Name}",
                    command.MessageId, command.IntervalUs, command.RateHz, command.Name);

                if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
                {
                    _ = _bluetoothConnection.SendSetMessageIntervalAsync(command.MessageId, command.IntervalUs);
                    return;
                }

                if (_mavlink != null)
                {
                    _ = _mavlink.SendSetMessageIntervalAsync(command.MessageId, command.IntervalUs);
                }
                return;

            default:
                _logger.LogWarning("Unknown telemetry negotiation command type: {Type}", command.Type);
                return;
        }
    }

    public async Task SendStartMagCalAsync(int magMask = 0, int retryOnFailure = 1, int autosave = 1, float delay = 0, int autoreboot = 0)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Cannot send start mag cal - not connected");
            return;
        }

        if (_mavlink != null)
        {
            await _mavlink.SendStartMagCalAsync(magMask, retryOnFailure, autosave, delay, autoreboot);
        }
        else if (_bluetoothConnection != null)
        {
            _logger.LogWarning("Compass calibration over Bluetooth not yet supported");
        }
    }

    public async Task SendAcceptMagCalAsync(int magMask = 0)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Cannot send accept mag cal - not connected");
            return;
        }

        if (_mavlink != null)
        {
            await _mavlink.SendAcceptMagCalAsync(magMask);
        }
        else if (_bluetoothConnection != null)
        {
            _logger.LogWarning("Compass calibration over Bluetooth not yet supported");
        }
    }

    public async Task SendCancelMagCalAsync(int magMask = 0)
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Cannot send cancel mag cal - not connected");
            return;
        }

        if (_mavlink != null)
        {
            await _mavlink.SendCancelMagCalAsync(magMask);
        }
        else if (_bluetoothConnection != null)
        {
            _logger.LogWarning("Compass calibration over Bluetooth not yet supported");
        }
    }

    public void SendRequestAutopilotVersion()
    {
        if (!IsConnected)
        {
            _logger.LogWarning("Cannot send AUTOPILOT_VERSION request - not connected");
            return;
        }

        if (_currentConnectionType == ConnectionType.Bluetooth && _bluetoothConnection != null)
        {
            _ = _bluetoothConnection.SendRequestAutopilotVersionAsync();
            return;
        }

        if (_mavlink != null)
        {
            _ = _mavlink.SendRequestAutopilotVersionAsync();
        }
    }

    #endregion

    private void SetConnected(bool connected)
    {
        if (_isConnected != connected)
        {
            _isConnected = connected;
            _logger.LogInformation("Connection state changed: {Connected}", connected);
            ConnectionStateChanged?.Invoke(this, connected);
            
            // Request AUTOPILOT_VERSION after connection is established
            // This is required to get the FC's unique identifier (UID/UID2) and firmware version
            if (connected)
            {
                _ = Task.Run(async () =>
                {
                    // Wait a bit for the connection to stabilize
                    await Task.Delay(300);
                    
                    if (_isConnected && !_isDisconnecting)
                    {
                        _logger.LogInformation("Requesting AUTOPILOT_VERSION for FC identification");
                        SendRequestAutopilotVersion();
                    }
                });
            }
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
