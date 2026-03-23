using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Infrastructure.Services;
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
/// ? OPTIMIZED: Faster connection with reduced timeouts and improved retry logic
/// </summary>
public class BluetoothMavConnection : IDisposable
{
    private readonly ILogger _logger;
    private readonly IMavLinkMessageLogger? _mavLinkLogger;
    private readonly Guid _sppServiceClassId = new Guid("00001101-0000-1000-8000-00805F9B34FB"); // SPP UUID
    private BluetoothClient? _bluetoothClient;
    private Stream? _stream;
    private AsvMavlinkWrapper? _mavlinkWrapper;
    private bool _disposed;
    private bool _isConnected;
    
    // ? OPTIMIZED Connection parameters - REDUCED for faster connection
    private const int CONNECTION_RETRY_ATTEMPTS = 2; // ? Reduced from 3 to 2
    private const int CONNECTION_RETRY_DELAY_MS = 500; // ? Reduced from 1000ms to 500ms
    private const int CONNECTION_TIMEOUT_SECONDS = 15; // ? Reduced from 30 to 15 seconds
    private const int DEVICE_DISCOVERY_TIMEOUT_SECONDS = 15; // ? Reduced from 30 to 15 seconds

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
    public event EventHandler<MagCalProgressData>? MagCalProgressReceived;
    public event EventHandler<MagCalReportData>? MagCalReportReceived;

    public bool IsConnected => _isConnected;

    public BluetoothMavConnection(ILogger logger, IMavLinkMessageLogger? mavLinkLogger = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _mavLinkLogger = mavLinkLogger;
    }

    /// <summary>
    /// Connect to Bluetooth device using SPP/RFCOMM
    /// Modern async implementation for .NET 9 with retry logic
    /// </summary>
    public async Task<bool> ConnectAsync(string deviceAddress)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BluetoothMavConnection));

        if (string.IsNullOrWhiteSpace(deviceAddress))
        {
            _logger.LogError("Invalid Bluetooth device address");
            throw new ArgumentException("Device address cannot be empty", nameof(deviceAddress));
        }

        // Close any previous connection
        await CloseAsync();

        Exception? lastException = null;

        // Retry logic for robust connection
        for (int attempt = 1; attempt <= CONNECTION_RETRY_ATTEMPTS; attempt++)
        {
            BluetoothClient? tempClient = null;
            
            try
            {
                _logger.LogInformation("Connecting to Bluetooth device: {Address} (attempt {Attempt}/{Total})", 
                    deviceAddress, attempt, CONNECTION_RETRY_ATTEMPTS);

                // Parse Bluetooth address
                BluetoothAddress address;
                try
                {
                    address = BluetoothAddress.Parse(deviceAddress);
                }
                catch (FormatException ex)
                {
                    _logger.LogError(ex, "Invalid Bluetooth address format: {Address}", deviceAddress);
                    throw new ArgumentException($"Invalid Bluetooth address format: {deviceAddress}", nameof(deviceAddress), ex);
                }

                // Create RFCOMM socket (SPP)
                tempClient = new BluetoothClient();
                _bluetoothClient = tempClient;

                // Blocking connect to SPP service with timeout
                _logger.LogDebug("Establishing RFCOMM connection to SPP service...");
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(CONNECTION_TIMEOUT_SECONDS));
                
                var connectTask = Task.Run(() => 
                {
                    try
                    {
                        tempClient.Connect(address, _sppServiceClassId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Bluetooth connection attempt failed");
                        throw;
                    }
                }, cts.Token);

                try
                {
                    await connectTask;
                }
                catch (OperationCanceledException)
                {
                    throw new TimeoutException($"Bluetooth connection timed out after {CONNECTION_TIMEOUT_SECONDS} seconds");
                }

                if (!tempClient.Connected)
                {
                    throw new IOException("Bluetooth RFCOMM connection failed - client not connected");
                }

                // Get the network stream (combined input/output)
                _stream = tempClient.GetStream();

                if (_stream == null)
                {
                    throw new IOException("Failed to get Bluetooth stream");
                }

                if (!_stream.CanRead || !_stream.CanWrite)
                {
                    throw new IOException("Bluetooth stream is not readable/writable");
                }

                _logger.LogInformation("Bluetooth SPP connection established");

                // Initialize MAVLink connection using ASV.Mavlink wrapper with logger
                _mavlinkWrapper = new AsvMavlinkWrapper(_logger, _mavLinkLogger);
                _mavlinkWrapper.HeartbeatReceived += OnMavlinkHeartbeat;
                _mavlinkWrapper.ParamValueReceived += OnMavlinkParamValue;
                _mavlinkWrapper.HeartbeatDataReceived += OnMavlinkHeartbeatData;
                _mavlinkWrapper.StatusTextReceived += OnMavlinkStatusText;
                _mavlinkWrapper.RcChannelsReceived += OnMavlinkRcChannels;
                _mavlinkWrapper.CommandAckReceived += OnMavlinkCommandAck;
                _mavlinkWrapper.CommandLongReceived += OnMavlinkCommandLong;
                _mavlinkWrapper.RawImuReceived += OnMavlinkRawImu;
                _mavlinkWrapper.MagCalProgressReceived += OnMavlinkMagCalProgress;
                _mavlinkWrapper.MagCalReportReceived += OnMavlinkMagCalReport;
                _mavlinkWrapper.Initialize(_stream, _stream);

                _isConnected = true;
                ConnectionStateChanged?.Invoke(this, true);

                _logger.LogInformation("Bluetooth MAVLink connection ready - waiting for heartbeat");
                return true;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Bluetooth connection attempt {Attempt} failed: {Message}", 
                    attempt, ex.Message);
                
                // Clean up on failure - dispose temp client, not the field
                try
                {
                    _stream?.Dispose();
                    _stream = null;
                    
                    if (tempClient != null)
                    {
                        try { tempClient.Close(); } catch { }
                        try { tempClient.Dispose(); } catch { }
                    }
                    
                    _bluetoothClient = null;
                }
                catch { }

                if (attempt < CONNECTION_RETRY_ATTEMPTS)
                {
                    _logger.LogInformation("Retrying Bluetooth connection in {Delay}ms...", CONNECTION_RETRY_DELAY_MS);
                    await Task.Delay(CONNECTION_RETRY_DELAY_MS);
                }
            }
        }

        // All attempts failed
        _logger.LogError(lastException, "Failed to connect to Bluetooth device after {Attempts} attempts", CONNECTION_RETRY_ATTEMPTS);
        throw new IOException($"Failed to establish Bluetooth connection after {CONNECTION_RETRY_ATTEMPTS} attempts", lastException);
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
                _mavlinkWrapper.MagCalProgressReceived -= OnMavlinkMagCalProgress;
                _mavlinkWrapper.MagCalReportReceived -= OnMavlinkMagCalReport;
                // Note: AsvMavlinkWrapper.Dispose() may not exist yet
                try { _mavlinkWrapper.Dispose(); } catch { }
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
    /// Send MAV_CMD_SET_MESSAGE_INTERVAL over Bluetooth.
    /// </summary>
    public async Task SendSetMessageIntervalAsync(int messageId, int intervalUs, CancellationToken ct = default)
    {
        if (!_isConnected || _mavlinkWrapper == null)
            throw new InvalidOperationException("Bluetooth connection is not active");

        await _mavlinkWrapper.SendSetMessageIntervalAsync(messageId, intervalUs, ct);
    }

    /// <summary>
    /// Send REQUEST_DATA_STREAM over Bluetooth.
    /// </summary>
    public async Task SendRequestDataStreamAsync(int streamId, int rateHz, int startStop, CancellationToken ct = default)
    {
        if (!_isConnected || _mavlinkWrapper == null)
            throw new InvalidOperationException("Bluetooth connection is not active");

        await _mavlinkWrapper.SendRequestDataStreamAsync(streamId, rateHz, startStop, ct);
    }

    /// <summary>
    /// Request AUTOPILOT_VERSION over Bluetooth.
    /// </summary>
    public async Task SendRequestAutopilotVersionAsync(CancellationToken ct = default)
    {
        if (!_isConnected || _mavlinkWrapper == null)
            throw new InvalidOperationException("Bluetooth connection is not active");

        await _mavlinkWrapper.SendRequestAutopilotVersionAsync(ct);
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
    /// Cancel any ongoing preflight calibration by sending MAV_CMD_PREFLIGHT_CALIBRATION with all zeros.
    /// </summary>
    public async Task SendCancelPreflightCalibrationAsync(CancellationToken ct = default)
    {
        if (!_isConnected || _mavlinkWrapper == null)
            throw new InvalidOperationException("Bluetooth connection is not active");

        await _mavlinkWrapper.SendCancelPreflightCalibrationAsync(ct);
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
    /// Discover available Bluetooth devices with improved error handling and smart prioritization.
    /// ? OPTIMIZED: Faster discovery (8s instead of 15s), paired devices shown first.
    /// </summary>
    public async Task<IEnumerable<CoreBluetoothDeviceInfo>> DiscoverDevicesAsync()
    {
        var devices = new List<CoreBluetoothDeviceInfo>();
        
        try
        {
            _logger.LogInformation("Discovering Bluetooth devices (optimized fast scan ~8 seconds)...");
            
            var client = new BluetoothClient();
            
            // ? OPTIMIZATION: Reduced timeout from 15s to 8s for faster UX
            var discoverTask = Task.Run(() => client.DiscoverDevices().ToList());
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(8)); // ? Reduced from 15s
            
            var completedTask = await Task.WhenAny(discoverTask, timeoutTask);
            
            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Bluetooth device discovery timed out after 8 seconds (fast scan)");
                return devices;
            }
            
            var discovered = await discoverTask;
            
            // ? OPTIMIZATION: Separate paired and unpaired devices for prioritization
            var pairedDevices = new List<CoreBluetoothDeviceInfo>();
            var unpairedDevices = new List<CoreBluetoothDeviceInfo>();
            
            foreach (var device in discovered)
            {
                try
                {
                    var deviceName = device.DeviceName;
                    var isPaired = device.Authenticated;
                    var isConnected = device.Connected;
                    
                    // ? OPTIMIZATION: Smart device name handling
                    if (string.IsNullOrWhiteSpace(deviceName))
                    {
                        // Better naming for unknown devices
                        deviceName = isPaired 
                            ? $"Paired Device ({device.DeviceAddress})" 
                            : $"Unknown Device ({device.DeviceAddress})";
                        _logger.LogDebug("Found device with no name: {Address}, paired={Paired}", 
                            device.DeviceAddress, isPaired);
                    }
                    
                    var deviceInfo = new CoreBluetoothDeviceInfo
                    {
                        DeviceAddress = device.DeviceAddress.ToString(),
                        DeviceName = deviceName,
                        IsConnected = isConnected,
                        IsPaired = isPaired
                    };
                    
                    // ? OPTIMIZATION: Prioritize paired devices - they're more likely to be the drone
                    if (isPaired)
                    {
                        pairedDevices.Add(deviceInfo);
                        _logger.LogDebug("? Paired device: {Name} ({Address})", 
                            deviceName, device.DeviceAddress);
                    }
                    else
                    {
                        unpairedDevices.Add(deviceInfo);
                        _logger.LogDebug("  Unpaired device: {Name} ({Address})", 
                            deviceName, device.DeviceAddress);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing Bluetooth device: {Address}", device.DeviceAddress);
                }
            }
            
            // ? OPTIMIZATION: Show paired devices FIRST in the UI (better UX)
            // Users want to see their drone (which is paired) at the top of the list
            devices.AddRange(pairedDevices.OrderBy(d => d.DeviceName));
            devices.AddRange(unpairedDevices.OrderBy(d => d.DeviceName));
            
            _logger.LogInformation("Found {Total} Bluetooth device(s): {Paired} paired, {Unpaired} unpaired", 
                devices.Count, pairedDevices.Count, unpairedDevices.Count);
            
            if (devices.Count == 0)
            {
                _logger.LogWarning("No Bluetooth devices found. Troubleshooting tips:");
                _logger.LogWarning("  1. Enable Bluetooth on this computer");
                _logger.LogWarning("  2. Power on the drone's Bluetooth module");
                _logger.LogWarning("  3. Pair the device first using Windows Bluetooth settings");
                _logger.LogWarning("  4. Make sure the drone is within range (< 10 meters)");
            }
            else if (pairedDevices.Count > 0)
            {
                _logger.LogInformation("? {Count} paired device(s) ready to connect", pairedDevices.Count);
            }
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

    private void OnMavlinkMagCalProgress(object? sender, MagCalProgressData e)
    {
        MagCalProgressReceived?.Invoke(this, e);
    }

    private void OnMavlinkMagCalReport(object? sender, MagCalReportData e)
    {
        MagCalReportReceived?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        CloseAsync().GetAwaiter().GetResult();
        _disposed = true;
    }
}
