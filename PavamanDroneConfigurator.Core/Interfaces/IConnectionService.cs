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
    bool IsArmed { get; }
    event EventHandler<bool>? ConnectionStateChanged;
    
    IEnumerable<SerialPortInfo> GetAvailableSerialPorts();
    event EventHandler<IEnumerable<SerialPortInfo>>? AvailableSerialPortsChanged;
    
    Task<IEnumerable<BluetoothDeviceInfo>> GetAvailableBluetoothDevicesAsync();
    
    Stream? GetTransportStream();
    
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
    event EventHandler<AutopilotVersionDataEventArgs>? AutopilotVersionReceived;
    event EventHandler<GlobalPositionIntEventArgs>? GlobalPositionIntReceived;
    event EventHandler<AttitudeEventArgs>? AttitudeReceived;
    event EventHandler<VfrHudEventArgs>? VfrHudReceived;
    event EventHandler<GpsRawIntEventArgs>? GpsRawIntReceived;
    event EventHandler<SysStatusEventArgs>? SysStatusReceived;
    event EventHandler? RebootInitiated;
    
    void SendParamRequestList();
    void SendParamRequestRead(ushort paramIndex);
    void SendParamSet(ParameterWriteRequest request);
    void SendMotorTest(int motorInstance, int throttleType, float throttleValue, float timeout, int motorCount = 0, int testOrder = 0);
    void SendPreflightCalibration(int gyro, int mag, int groundPressure, int airspeed, int accel);
    Task SendPreflightCalibrationAsync(int gyro, int mag, int groundPressure, int airspeed, int accel);
    void SendCancelPreflightCalibration();
    void SendAccelCalVehiclePos(int position);
    void SendPreflightReboot(int autopilot, int companion);
    void PrepareForReboot();
    void SendFlashBootloaderCommand(int magicValue);
    void SendArmDisarm(bool arm, bool force = false);
    void SendReturnToLaunch();
    void SendResetParameters();
    void SendSetMessageInterval(int messageId, int intervalUs);
    void SendRequestDataStream(int streamId, int rateHz, int startStop);
    void SendTelemetryNegotiationCommand(TelemetryNegotiationCommand command);
    Task SendStartMagCalAsync(int magMask = 0, int retryOnFailure = 1, int autosave = 1, float delay = 0, int autoreboot = 0);
    Task SendAcceptMagCalAsync(int magMask = 0);
    Task SendCancelMagCalAsync(int magMask = 0);
    void SendRequestAutopilotVersion();
}

public enum TelemetryNegotiationCommandType
{
    RequestDataStream,
    SetMessageInterval
}

public class TelemetryNegotiationCommand
{
    public TelemetryNegotiationCommandType Type { get; init; }
    public int StreamId { get; init; }
    public int RateHz { get; init; }
    public int StartStop { get; init; } = 1;
    public int MessageId { get; init; }
    public int IntervalUs { get; init; }
    public string Name { get; init; } = string.Empty;

    public static TelemetryNegotiationCommand ForDataStream(int streamId, int rateHz, int startStop, string name)
    {
        return new TelemetryNegotiationCommand
        {
            Type = TelemetryNegotiationCommandType.RequestDataStream,
            StreamId = streamId,
            RateHz = rateHz,
            StartStop = startStop,
            Name = name
        };
    }

    public static TelemetryNegotiationCommand ForMessageInterval(int messageId, int rateHz, string name)
    {
        return new TelemetryNegotiationCommand
        {
            Type = TelemetryNegotiationCommandType.SetMessageInterval,
            MessageId = messageId,
            RateHz = rateHz,
            IntervalUs = 1_000_000 / Math.Max(rateHz, 1),
            Name = name
        };
    }
}

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

public class HeartbeatDataEventArgs : EventArgs
{
    public byte SystemId { get; set; }
    public byte ComponentId { get; set; }
    public uint CustomMode { get; set; }
    public byte VehicleType { get; set; }
    public byte Autopilot { get; set; }
    public byte BaseMode { get; set; }
    public byte SystemStatus { get; set; }
    public bool IsArmed { get; set; }
    public bool CrcValid { get; set; } = true;
}

public class StatusTextEventArgs : EventArgs
{
    public byte Severity { get; set; }
    public string Text { get; set; } = string.Empty;
}

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
        1 => Channel1, 2 => Channel2, 3 => Channel3, 4 => Channel4,
        5 => Channel5, 6 => Channel6, 7 => Channel7, 8 => Channel8,
        _ => 0
    };
}

public class CommandAckEventArgs : EventArgs
{
    public ushort Command { get; set; }
    public byte Result { get; set; }
    public bool IsSuccess => Result == 0;
}

public class RawImuEventArgs : EventArgs
{
    public double AccelX { get; set; }
    public double AccelY { get; set; }
    public double AccelZ { get; set; }
    public double GyroX { get; set; }
    public double GyroY { get; set; }
    public double GyroZ { get; set; }
    public ulong TimeUsec { get; set; }
    public double Temperature { get; set; }
}

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
    public byte[]? Uid { get; set; }
    public byte[]? Uid2 { get; set; }
    public byte FirmwareMajor { get; set; }
    public byte FirmwareMinor { get; set; }
    public byte FirmwarePatch { get; set; }
    public byte FirmwareType { get; set; }
    public string FirmwareVersionString { get; set; } = "N/A";
    
    public string GetFcId()
    {
        if (Uid2 != null && Uid2.Length > 0)
        {
            bool allZeros = true;
            foreach (byte b in Uid2) { if (b != 0) { allZeros = false; break; } }
            if (!allZeros)
            {
                var hex = BitConverter.ToString(Uid2, 0, Math.Min(10, Uid2.Length)).Replace("-", "").ToUpperInvariant();
                return $"FC-{hex}";
            }
        }
        if (Uid != null && Uid.Length > 0)
        {
            bool allZeros = true;
            foreach (byte b in Uid) { if (b != 0) { allZeros = false; break; } }
            if (!allZeros)
            {
                var hex = BitConverter.ToString(Uid).Replace("-", "").ToUpperInvariant();
                return $"FC-{hex}";
            }
        }
        if (FlightSwVersion > 0)
        {
            var gitPrefix = FlightCustomVersion != null && FlightCustomVersion.Length >= 4
                ? BitConverter.ToString(FlightCustomVersion, 0, 4).Replace("-", "").ToUpperInvariant()
                : "0000";
            return $"FW-{FlightSwVersion:X8}-{gitPrefix}";
        }
        return "FW-UNAVAILABLE";
    }
    
    public string GetGitHash()
    {
        if (FlightCustomVersion == null || FlightCustomVersion.Length == 0) return "N/A";
        bool allZeros = true;
        foreach (byte b in FlightCustomVersion) { if (b != 0) { allZeros = false; break; } }
        if (allZeros) return "N/A";
        return BitConverter.ToString(FlightCustomVersion).Replace("-", "").ToLowerInvariant();
    }
}

public class GlobalPositionIntEventArgs : EventArgs
{
    public uint TimeBootMs { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeMsl { get; set; }
    public double AltitudeRelative { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public double VelocityZ { get; set; }
    public double Heading { get; set; }
    public bool CrcValid { get; set; } = true;
}

public class AttitudeEventArgs : EventArgs
{
    public uint TimeBootMs { get; set; }
    public double Roll { get; set; }
    public double Pitch { get; set; }
    public double Yaw { get; set; }
    public double RollSpeed { get; set; }
    public double PitchSpeed { get; set; }
    public double YawSpeed { get; set; }
    public bool CrcValid { get; set; } = true;
}

public class VfrHudEventArgs : EventArgs
{
    public double Airspeed { get; set; }
    public double GroundSpeed { get; set; }
    public double Heading { get; set; }
    public int Throttle { get; set; }
    public double Altitude { get; set; }
    public double ClimbRate { get; set; }
    public bool CrcValid { get; set; } = true;
}

public class GpsRawIntEventArgs : EventArgs
{
    public ulong TimeUsec { get; set; }
    public byte FixType { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Altitude { get; set; }
    public double Hdop { get; set; }
    public double Vdop { get; set; }
    public double GroundSpeed { get; set; }
    public double CourseOverGround { get; set; }
    public byte SatellitesVisible { get; set; }
    public bool CrcValid { get; set; } = true;
}

public class SysStatusEventArgs : EventArgs
{
    public uint SensorsPresent { get; set; }
    public uint SensorsEnabled { get; set; }
    public uint SensorsHealth { get; set; }
    public ushort Load { get; set; }
    public double BatteryVoltage { get; set; }
    public double BatteryCurrent { get; set; }
    public sbyte BatteryRemaining { get; set; }
    public ushort DropRateComm { get; set; }
    public ushort ErrorsComm { get; set; }
    public bool CrcValid { get; set; } = true;
}
