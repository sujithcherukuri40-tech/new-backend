using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Alias to avoid ambiguity with InTheHand.Net.Sockets.BluetoothDeviceInfo
using CoreBluetoothDeviceInfo = PavamanDroneConfigurator.Core.Models.BluetoothDeviceInfo;

namespace PavamanDroneConfigurator.Infrastructure.MAVLink;

/// <summary>
/// Bluetooth MAVLink Connection using SPP (Serial Port Profile)
/// Modernized for .NET 9 using InTheHand.Net.Bluetooth
/// Production-ready cross-platform Bluetooth RFCOMM implementation
/// </summary>
public class BluetoothMavConnection : IDisposable
{
    private readonly ILogger _logger;
    private readonly Guid _sppServiceClassId = new Guid("00001101-0000-1000-8000-00805F9B34FB"); // SPP UUID
    private BluetoothClient? _bluetoothClient;
    private Stream? _stream;
    private AsvMavlinkWrapper? _mavlinkWrapper;
    private bool _disposed;
    private bool _isConnected;

    // Events matching existing connection architecture
    public event EventHandler<(byte SystemId, byte ComponentId)>? HeartbeatReceived;
    public event EventHandler<(string Name, float Value, ushort Index, ushort Count)>? ParamValueReceived;
    public event EventHandler<bool>? ConnectionStateChanged;
    
    // Additional events needed for calibration (forwarded from AsvMavlinkWrapper)
    public event EventHandler<HeartbeatData>? HeartbeatDataReceived;
    public event EventHandler<(byte Severity, string Text)>? StatusTextReceived;
    public event EventHandler<RcChannelsData>? RcChannelsReceived;
    public event EventHandler<(ushort Command, byte Result)>? CommandAckReceived;
    public event EventHandler<CommandLongData>? CommandLongReceived;
    public event EventHandler<RawImuData>? RawImuReceived;

    public bool IsConnected => _isConnected;

    public BluetoothMavConnection(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Connect to Bluetooth device using SPP/RFCOMM
    /// Modern async implementation for .NET 9
    /// Throws on failure - no retries
    /// </summary>
    public async Task<bool> ConnectAsync(string deviceAddress)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BluetoothMavConnection));

        try
        {
            // Close any previous connection
            await CloseAsync();

            _logger.LogInformation("Connecting to Bluetooth device: {Address}", deviceAddress);

            // Parse Bluetooth address
            var address = BluetoothAddress.Parse(deviceAddress);

            // Create RFCOMM socket (SPP)
            _bluetoothClient = new BluetoothClient();

            // Blocking connect to SPP service
            _logger.LogDebug("Establishing RFCOMM connection to SPP service...");
            await Task.Run(() => _bluetoothClient.Connect(address, _sppServiceClassId));

            if (!_bluetoothClient.Connected)
            {
                throw new IOException("Bluetooth RFCOMM connection failed");
            }

            // Get the network stream (combined input/output)
            _stream = _bluetoothClient.GetStream();

            if (_stream == null)
            {
                throw new IOException("Failed to get Bluetooth stream");
            }

            _logger.LogInformation("Bluetooth SPP connection established");

            // Initialize MAVLink connection using ASV.Mavlink wrapper
            _mavlinkWrapper = new AsvMavlinkWrapper(_logger);
            _mavlinkWrapper.HeartbeatReceived += OnMavlinkHeartbeat;
            _mavlinkWrapper.ParamValueReceived += OnMavlinkParamValue;
            _mavlinkWrapper.HeartbeatDataReceived += OnMavlinkHeartbeatData;
            _mavlinkWrapper.StatusTextReceived += OnMavlinkStatusText;
            _mavlinkWrapper.RcChannelsReceived += OnMavlinkRcChannels;
            _mavlinkWrapper.CommandAckReceived += OnMavlinkCommandAck;
            _mavlinkWrapper.CommandLongReceived += OnMavlinkCommandLong;
            _mavlinkWrapper.RawImuReceived += OnMavlinkRawImu;
            _mavlinkWrapper.Initialize(_stream, _stream);

            _isConnected = true;
            ConnectionStateChanged?.Invoke(this, true);

            _logger.LogInformation("Bluetooth MAVLink connection ready");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bluetooth connection failed");
            
            // Clean up on failure
            await CloseAsync();
            throw;
        }
    }

    /// <summary>
    /// Connect to Bluetooth device by name
    /// Discovers devices and connects to first match
    /// </summary>
    public async Task<bool> ConnectByNameAsync(string deviceName)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BluetoothMavConnection));

        try
        {
            _logger.LogInformation("Discovering Bluetooth devices...");

            var client = new BluetoothClient();
            var devices = await Task.Run(() => client.DiscoverDevices().ToList());

            foreach (var device in devices)
            {
                if (device.DeviceName.Equals(deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Found device: {Name} ({Address})", device.DeviceName, device.DeviceAddress);
                    return await ConnectAsync(device.DeviceAddress.ToString());
                }
            }

            throw new IOException($"Bluetooth device not found: {deviceName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect by device name");
            throw;
        }
    }

    /// <summary>
    /// Close Bluetooth connection
    /// Suppresses exceptions during teardown
    /// </summary>
    public async Task CloseAsync()
    {
        if (!_isConnected)
            return;

        try
        {
            _logger.LogInformation("Closing Bluetooth connection");

            // Unsubscribe from events
            if (_mavlinkWrapper != null)
            {
                _mavlinkWrapper.HeartbeatReceived -= OnMavlinkHeartbeat;
                _mavlinkWrapper.ParamValueReceived -= OnMavlinkParamValue;
                _mavlinkWrapper.HeartbeatDataReceived -= OnMavlinkHeartbeatData;
                _mavlinkWrapper.StatusTextReceived -= OnMavlinkStatusText;
                _mavlinkWrapper.RcChannelsReceived -= OnMavlinkRcChannels;
                _mavlinkWrapper.CommandAckReceived -= OnMavlinkCommandAck;
                _mavlinkWrapper.CommandLongReceived -= OnMavlinkCommandLong;
                _mavlinkWrapper.RawImuReceived -= OnMavlinkRawImu;
                _mavlinkWrapper.Dispose();
                _mavlinkWrapper = null;
            }

            // Close stream
            if (_stream != null)
            {
                await _stream.DisposeAsync();
                _stream = null;
            }

            // Close Bluetooth client
            _bluetoothClient?.Close();
            _bluetoothClient?.Dispose();
            _bluetoothClient = null;

            _isConnected = false;
            ConnectionStateChanged?.Invoke(this, false);

            _logger.LogInformation("Bluetooth connection closed");
        }
        catch (Exception ex)
        {
            // Suppress exceptions during teardown
            _logger.LogDebug(ex, "Exception suppressed during Bluetooth close");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Send PARAM_REQUEST_LIST to drone
    /// Throws if connection is not active
    /// </summary>
    public async Task SendParamRequestListAsync(CancellationToken ct = default)
    {
        if (!_isConnected || _mavlinkWrapper == null)
            throw new InvalidOperationException("Bluetooth connection is not active");

        await _mavlinkWrapper.SendParamRequestListAsync(ct);
    }

    /// <summary>
    /// Send PARAM_REQUEST_READ to drone
    /// Throws if connection is not active
    /// </summary>
    public async Task SendParamRequestReadAsync(ushort paramIndex, CancellationToken ct = default)
    {
        if (!_isConnected || _mavlinkWrapper == null)
            throw new InvalidOperationException("Bluetooth connection is not active");

        await _mavlinkWrapper.SendParamRequestReadAsync(paramIndex, ct);
    }

    /// <summary>
    /// Send PARAM_SET to drone
    /// Throws if connection is not active
    /// </summary>
    public async Task SendParamSetAsync(string paramName, float paramValue, CancellationToken ct = default)
    {
        if (!_isConnected || _mavlinkWrapper == null)
            throw new InvalidOperationException("Bluetooth connection is not active");

        await _mavlinkWrapper.SendParamSetAsync(paramName, paramValue, ct);
    }

    /// <summary>
    /// Send DO_MOTOR_TEST command to drone
    /// Throws if connection is not active
    /// </summary>
    public async Task SendMotorTestAsync(int motorInstance, int throttleType, float throttleValue, 
        float timeout, int motorCount = 0, int testOrder = 0, CancellationToken ct = default)
    {
        if (!_isConnected || _mavlinkWrapper == null)
            throw new InvalidOperationException("Bluetooth connection is not active");

        await _mavlinkWrapper.SendMotorTestAsync(motorInstance, throttleType, throttleValue, 
            timeout, motorCount, testOrder, ct);
    }

    /// <summary>
    /// Send MAV_CMD_PREFLIGHT_CALIBRATION command to drone
    /// </summary>
    public async Task SendPreflightCalibrationAsync(int gyro, int mag, int groundPressure, 
        int airspeed, int accel, CancellationToken ct = default)
    {
        if (!_isConnected || _mavlinkWrapper == null)
            throw new InvalidOperationException("Bluetooth connection is not active");

        await _mavlinkWrapper.SendPreflightCalibrationAsync(gyro, mag, groundPressure, airspeed, accel, ct);
    }

    /// <summary>
    /// Send MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN command to drone
    /// </summary>
    public async Task SendPreflightRebootAsync(int autopilot, int companion, CancellationToken ct = default)
    {
        if (!_isConnected || _mavlinkWrapper == null)
            throw new InvalidOperationException("Bluetooth connection is not active");

        await _mavlinkWrapper.SendPreflightRebootAsync(autopilot, companion, ct);
    }

    /// <summary>
    /// Send MAV_CMD_COMPONENT_ARM_DISARM command to drone
    /// </summary>
    public async Task SendArmDisarmAsync(bool arm, bool force = false, CancellationToken ct = default)
    {
        if (!_isConnected || _mavlinkWrapper == null)
            throw new InvalidOperationException("Bluetooth connection is not active");

        await _mavlinkWrapper.SendArmDisarmAsync(arm, force, ct);
    }

    /// <summary>
    /// Send MAV_CMD_ACCELCAL_VEHICLE_POS command to tell FC the vehicle is in position
    /// </summary>
    public async Task SendAccelCalVehiclePosAsync(int position, CancellationToken ct = default)
    {
        if (!_isConnected || _mavlinkWrapper == null)
            throw new InvalidOperationException("Bluetooth connection is not active");

        await _mavlinkWrapper.SendAccelCalVehiclePosAsync(position, ct);
    }

    /// <summary>
    /// Send MAV_CMD_ACCELCAL_VEHICLE_POS - Mission Planner style (fire and forget)
    /// This is the EXACT behavior of Mission Planner's sendPacket() method.
    /// Does NOT wait for COMMAND_ACK - FC responds via STATUSTEXT/COMMAND_LONG
    /// </summary>
    public void SendAccelCalVehiclePosRaw(int position)
    {
        if (!_isConnected || _mavlinkWrapper == null)
        {
            _logger.LogWarning("Bluetooth connection is not active - cannot send ACCELCAL_VEHICLE_POS");
            return;
        }

        _mavlinkWrapper.SendAccelCalVehiclePosRaw(position);
    }

    /// <summary>
    /// Discover available Bluetooth devices
    /// </summary>
    public async Task<IEnumerable<CoreBluetoothDeviceInfo>> DiscoverDevicesAsync()
    {
        var devices = new List<CoreBluetoothDeviceInfo>();
        
        try
        {
            _logger.LogInformation("Discovering Bluetooth devices...");
            
            var client = new BluetoothClient();
            var discovered = await Task.Run(() => client.DiscoverDevices().ToList());
            
            foreach (var device in discovered)
            {
                devices.Add(new CoreBluetoothDeviceInfo
                {
                    DeviceAddress = device.DeviceAddress.ToString(),
                    DeviceName = device.DeviceName ?? "Unknown Device",
                    IsConnected = device.Connected,
                    IsPaired = device.Authenticated
                });
            }
            
            _logger.LogInformation("Found {Count} Bluetooth devices", devices.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering Bluetooth devices");
        }
        
        return devices;
    }

    private void OnMavlinkHeartbeat(object? sender, (byte SystemId, byte ComponentId) e)
    {
        HeartbeatReceived?.Invoke(this, e);
    }

    private void OnMavlinkParamValue(object? sender, (string Name, float Value, ushort Index, ushort Count) e)
    {
        ParamValueReceived?.Invoke(this, e);
    }
    
    private void OnMavlinkHeartbeatData(object? sender, HeartbeatData e)
    {
        HeartbeatDataReceived?.Invoke(this, e);
    }
    
    private void OnMavlinkStatusText(object? sender, (byte Severity, string Text) e)
    {
        StatusTextReceived?.Invoke(this, e);
    }
    
    private void OnMavlinkRcChannels(object? sender, RcChannelsData e)
    {
        RcChannelsReceived?.Invoke(this, e);
    }
    
    private void OnMavlinkCommandAck(object? sender, (ushort Command, byte Result) e)
    {
        CommandAckReceived?.Invoke(this, e);
    }
    
    private void OnMavlinkCommandLong(object? sender, CommandLongData e)
    {
        CommandLongReceived?.Invoke(this, e);
    }
    
    private void OnMavlinkRawImu(object? sender, RawImuData e)
    {
        RawImuReceived?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        CloseAsync().GetAwaiter().GetResult();
        _disposed = true;
    }
}
