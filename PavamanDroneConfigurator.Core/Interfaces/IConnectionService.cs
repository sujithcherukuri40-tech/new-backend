using PavamanDroneConfigurator.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Core.Interfaces;

public interface IConnectionService
{
    Task<bool> ConnectAsync(ConnectionSettings settings);
    Task DisconnectAsync();
    bool IsConnected { get; }
    bool IsArmed { get; } // Added: Track armed status from HEARTBEAT
    event EventHandler<bool>? ConnectionStateChanged;
    
    // Serial port methods
    IEnumerable<SerialPortInfo> GetAvailableSerialPorts();
    event EventHandler<IEnumerable<SerialPortInfo>>? AvailableSerialPortsChanged;
    
    // Bluetooth methods
    Task<IEnumerable<BluetoothDeviceInfo>> GetAvailableBluetoothDevicesAsync();
    
    Stream? GetTransportStream();
    
    // MAVLink message events
    event EventHandler<MavlinkParamValueEventArgs>? ParamValueReceived;
    event EventHandler? HeartbeatReceived;
    event EventHandler<HeartbeatDataEventArgs>? HeartbeatDataReceived;
    event EventHandler<StatusTextEventArgs>? StatusTextReceived;
    event EventHandler<RcChannelsEventArgs>? RcChannelsReceived;
    event EventHandler<CommandAckEventArgs>? CommandAckReceived;
    event EventHandler<CommandLongEventArgs>? CommandLongReceived;
    event EventHandler<RawImuEventArgs>? RawImuReceived;
    event EventHandler<MagCalProgressEventArgs>? MagCalProgressReceived;
    event EventHandler<MagCalReportEventArgs>? MagCalReportReceived;
    
    /// <summary>
    /// Event raised when AUTOPILOT_VERSION message is received.
    /// Contains firmware version, capabilities, and unique hardware identifiers (UID/UID2).
    /// </summary>
    event EventHandler<AutopilotVersionDataEventArgs>? AutopilotVersionReceived;
    
    // MAVLink send methods for ParameterService to call
    void SendParamRequestList();
    void SendParamRequestRead(ushort paramIndex);
    void SendParamSet(ParameterWriteRequest request);
    
    // Motor test command (DO_MOTOR_TEST MAV_CMD = 209)
    void SendMotorTest(int motorInstance, int throttleType, float throttleValue, float timeout, int motorCount = 0, int testOrder = 0);
    
    // Calibration command (MAV_CMD_PREFLIGHT_CALIBRATION = 241)
    void SendPreflightCalibration(int gyro, int mag, int groundPressure, int airspeed, int accel);
    
    /// <summary>
    /// Cancel any ongoing preflight calibration by sending MAV_CMD_PREFLIGHT_CALIBRATION with all zeros.
    /// This is the standard way to abort calibration in ArduPilot.
    /// </summary>
    void SendCancelPreflightCalibration();
    
    // Accelerometer calibration position acknowledgment (MAV_CMD_ACCELCAL_VEHICLE_POS = 42429)
    // This tells the FC that the vehicle is in position and ready for sampling
    void SendAccelCalVehiclePos(int position);
    
    // Reboot command (MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN = 246)
    void SendPreflightReboot(int autopilot, int companion);
    
    // Flash bootloader command (MAV_CMD_FLASH_BOOTLOADER = 42650)
    // Magic value 290876 confirms the operation
    void SendFlashBootloaderCommand(int magicValue);
    
    // Arm/Disarm command (MAV_CMD_COMPONENT_ARM_DISARM = 400)
    void SendArmDisarm(bool arm, bool force = false);
    
    // Reset all parameters to default (MAV_CMD_PREFLIGHT_STORAGE = 245, param1 = 2)
    void SendResetParameters();
    
    // Set message interval for specific MAVLink message (MAV_CMD_SET_MESSAGE_INTERVAL = 511)
    // Used to force IMU messages at 50Hz during accelerometer calibration
    void SendSetMessageInterval(int messageId, int intervalUs);
    
    // Request data stream (legacy fallback for older firmware)
    // streamId: 1=RAW_SENSORS (includes IMU), rateHz: desired rate, startStop: 1=start, 0=stop
    void SendRequestDataStream(int streamId, int rateHz, int startStop);
    
    // Compass calibration commands (MAV_CMD_DO_START/ACCEPT/CANCEL_MAG_CAL = 42424/42425/42426)
    Task SendStartMagCalAsync(int magMask = 0, int retryOnFailure = 1, int autosave = 1, float delay = 0, int autoreboot = 0);
    Task SendAcceptMagCalAsync(int magMask = 0);
    Task SendCancelMagCalAsync(int magMask = 0);
    
    /// <summary>
    /// Request AUTOPILOT_VERSION message from the flight controller.
    /// This must be called to get the FC's unique identifier (UID/UID2) and firmware version.
    /// </summary>
    void SendRequestAutopilotVersion();
}

// Event args for PARAM_VALUE messages
public class MavlinkParamValueEventArgs : EventArgs
{
    public DroneParameter Parameter { get; }
    public ushort ParamIndex { get; }
    public ushort ParamCount { get; }

    public MavlinkParamValueEventArgs(DroneParameter parameter, ushort paramIndex, ushort paramCount)
    {
        Parameter = parameter;
        ParamIndex = paramIndex;
        ParamCount = paramCount;
    }
}

// Event args for HEARTBEAT data
public class HeartbeatDataEventArgs : EventArgs
{
    public byte SystemId { get; set; }
    public byte ComponentId { get; set; }
    public uint CustomMode { get; set; }
    public byte VehicleType { get; set; }
    public byte Autopilot { get; set; }
    public byte BaseMode { get; set; }
    public bool IsArmed { get; set; }
}

// Event args for STATUSTEXT messages
public class StatusTextEventArgs : EventArgs
{
    public byte Severity { get; set; }
    public string Text { get; set; } = string.Empty;
}

// Event args for RC_CHANNELS messages
public class RcChannelsEventArgs : EventArgs
{
    public ushort Channel1 { get; set; }
    public ushort Channel2 { get; set; }
    public ushort Channel3 { get; set; }
    public ushort Channel4 { get; set; }
    public ushort Channel5 { get; set; }
    public ushort Channel6 { get; set; }
    public ushort Channel7 { get; set; }
    public ushort Channel8 { get; set; }
    public byte ChannelCount { get; set; }
    public byte Rssi { get; set; }
    
    public ushort GetChannel(int number) => number switch
    {
        1 => Channel1,
        2 => Channel2,
        3 => Channel3,
        4 => Channel4,
        5 => Channel5,
        6 => Channel6,
        7 => Channel7,
        8 => Channel8,
        _ => 0
    };
}

// Event args for COMMAND_ACK messages
public class CommandAckEventArgs : EventArgs
{
    public ushort Command { get; set; }
    public byte Result { get; set; }
    public bool IsSuccess => Result == 0; // MAV_RESULT_ACCEPTED
}

// Event args for RAW_IMU messages
public class RawImuEventArgs : EventArgs
{
    public double AccelX { get; set; } // m/s²
    public double AccelY { get; set; } // m/s²
    public double AccelZ { get; set; } // m/s²
    public double GyroX { get; set; }  // rad/s
    public double GyroY { get; set; }  // rad/s
    public double GyroZ { get; set; }  // rad/s
    public ulong TimeUsec { get; set; }
    public double Temperature { get; set; } // °C
}

// Event args for COMMAND_LONG messages
public class CommandLongEventArgs : EventArgs
{
    public byte SystemId { get; set; }
    public byte ComponentId { get; set; }
    public ushort Command { get; set; }
    public float Param1 { get; set; }
    public float Param2 { get; set; }
    public float Param3 { get; set; }
    public float Param4 { get; set; }
    public float Param5 { get; set; }
    public float Param6 { get; set; }
    public float Param7 { get; set; }
    public byte TargetSystem { get; set; }
    public byte TargetComponent { get; set; }
    public byte Confirmation { get; set; }
}

// Event args for MAG_CAL_PROGRESS messages
public class MagCalProgressEventArgs : EventArgs
{
    public byte CompassId { get; set; }
    public byte CalMask { get; set; }
    public byte CalStatus { get; set; }
    public byte Attempt { get; set; }
    public byte CompletionPct { get; set; }
    public byte[] CompletionMask { get; set; } = new byte[10];
    public float DirectionX { get; set; }
    public float DirectionY { get; set; }
    public float DirectionZ { get; set; }
}

// Event args for MAG_CAL_REPORT messages
public class MagCalReportEventArgs : EventArgs
{
    public byte CompassId { get; set; }
    public byte CalMask { get; set; }
    public byte CalStatus { get; set; }
    public byte Autosaved { get; set; }
    public float Fitness { get; set; }
    public float OfsX { get; set; }
    public float OfsY { get; set; }
    public float OfsZ { get; set; }
    public float DiagX { get; set; }
    public float DiagY { get; set; }
    public float DiagZ { get; set; }
    public float OffdiagX { get; set; }
    public float OffdiagY { get; set; }
    public float OffdiagZ { get; set; }
    public byte OrientationConfidence { get; set; }
    public byte OldOrientation { get; set; }
    public byte NewOrientation { get; set; }
    public float ScaleFactor { get; set; }
    
    public string GetOffsetString() => $"X: {OfsX:F1}, Y: {OfsY:F1}, Z: {OfsZ:F1}";
    public float GetMaxAbsOffset() => Math.Max(Math.Max(Math.Abs(OfsX), Math.Abs(OfsY)), Math.Abs(OfsZ));
}

/// <summary>
/// Event args for AUTOPILOT_VERSION messages.
/// Contains firmware version, capabilities, and unique hardware identifiers.
/// </summary>
public class AutopilotVersionDataEventArgs : EventArgs
{
    public byte SystemId { get; set; }
    public byte ComponentId { get; set; }
    public ulong Capabilities { get; set; }
    public uint FlightSwVersion { get; set; }
    public uint MiddlewareSwVersion { get; set; }
    public uint OsSwVersion { get; set; }
    public uint BoardVersion { get; set; }
    public byte[]? FlightCustomVersion { get; set; }
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    
    /// <summary>
    /// 8-byte unique hardware identifier (UID)
    /// </summary>
    public byte[]? Uid { get; set; }
    
    /// <summary>
    /// 18-byte unique hardware identifier (UID2) - more reliable for FC identification
    /// </summary>
    public byte[]? Uid2 { get; set; }
    
    public byte FirmwareMajor { get; set; }
    public byte FirmwareMinor { get; set; }
    public byte FirmwarePatch { get; set; }
    public byte FirmwareType { get; set; }
    public string FirmwareVersionString { get; set; } = "N/A";
    
    /// <summary>
    /// Gets the Flight Controller ID using the best available identifier.
    /// Priority: UID2 > UID > FlightSwVersion + GitHash
    /// </summary>
    public string GetFcId()
    {
        // Priority 1: Use UID2 if available (18-byte hardware unique ID - most reliable)
        if (Uid2 != null && Uid2.Length > 0)
        {
            bool allZeros = true;
            foreach (byte b in Uid2)
            {
                if (b != 0)
                {
                    allZeros = false;
                    break;
                }
            }
            
            if (!allZeros)
            {
                // Use first 10 bytes of UID2 for a readable ID
                var hex = BitConverter.ToString(Uid2, 0, Math.Min(10, Uid2.Length))
                    .Replace("-", "").ToUpperInvariant();
                return $"FC-{hex}";
            }
        }
        
        // Priority 2: Use UID if available (8-byte hardware ID)
        if (Uid != null && Uid.Length > 0)
        {
            bool allZeros = true;
            foreach (byte b in Uid)
            {
                if (b != 0)
                {
                    allZeros = false;
                    break;
                }
            }
            
            if (!allZeros)
            {
                var hex = BitConverter.ToString(Uid).Replace("-", "").ToUpperInvariant();
                return $"FC-{hex}";
            }
        }
        
        // Priority 3: Use firmware version + git hash
        if (FlightSwVersion > 0)
        {
            var gitPrefix = FlightCustomVersion != null && FlightCustomVersion.Length >= 4
                ? BitConverter.ToString(FlightCustomVersion, 0, 4).Replace("-", "").ToUpperInvariant()
                : "0000";
            return $"FW-{FlightSwVersion:X8}-{gitPrefix}";
        }
        
        return "FW-UNAVAILABLE";
    }
    
    /// <summary>
    /// Gets the git hash from FlightCustomVersion
    /// </summary>
    public string GetGitHash()
    {
        if (FlightCustomVersion == null || FlightCustomVersion.Length == 0)
            return "N/A";

        bool allZeros = true;
        foreach (byte b in FlightCustomVersion)
        {
            if (b != 0)
            {
                allZeros = false;
                break;
            }
        }

        if (allZeros)
            return "N/A";

        var hex = BitConverter.ToString(FlightCustomVersion).Replace("-", "").ToLowerInvariant();
        return hex;
    }
}
