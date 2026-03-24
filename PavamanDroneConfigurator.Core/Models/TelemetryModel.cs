namespace PavamanDroneConfigurator.Core.Models;

public enum TelemetryServiceState
{
    Disconnected,
    ConnectedAwaitingTelemetry,
    NegotiatingStreams,
    TelemetryActive,
    TelemetryStaleRecovering
}

public enum TelemetryStaleReason
{
    None,
    NoHeartbeat,
    NoPosition,
    NoGps,
    NoAttitude,
    NoVfr,
    NoSysStatus,
    NoRecentFrames
}

/// <summary>
/// Complete telemetry data model for drone position, attitude, and status.
/// Updated at 10Hz from MAVLink messages.
/// </summary>
public class TelemetryModel
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeMsl { get; set; }
    public double AltitudeRelative { get; set; }
    public double GroundSpeedX { get; set; }
    public double GroundSpeedY { get; set; }
    public double VerticalSpeed { get; set; }
    public double Heading { get; set; }

    public double Roll { get; set; }
    public double Pitch { get; set; }
    public double Yaw { get; set; }
    public double RollSpeed { get; set; }
    public double PitchSpeed { get; set; }
    public double YawSpeed { get; set; }

    public double Airspeed { get; set; }
    public double GroundSpeed { get; set; }
    public int Throttle { get; set; }
    public double ClimbRate { get; set; }

    public int GpsFixType { get; set; }
    public int SatelliteCount { get; set; }
    public double Hdop { get; set; }
    public double Vdop { get; set; }
    public double GpsAltitude { get; set; }

    public double BatteryVoltage { get; set; }
    public double BatteryCurrent { get; set; }
    public int BatteryRemaining { get; set; }

    public string FlightMode { get; set; } = "Unknown";
    public bool IsArmed { get; set; }
    public string VehicleType { get; set; } = "Unknown";
    public string SystemStatus { get; set; } = "Unknown";

    public DateTime LastPositionUpdate { get; set; }
    public DateTime LastAttitudeUpdate { get; set; }
    public DateTime LastGpsUpdate { get; set; }
    public DateTime LastHeartbeatUpdate { get; set; }

    /// <summary>
    /// Always returns true - we accept ALL position data from SITL/drone.
    /// The map will display whatever coordinates we receive.
    /// For SITL default location is around -35.36, 149.16 (Canberra, Australia)
    /// </summary>
    public bool HasValidPosition => true;

    public string GpsFixTypeDescription => GpsFixType switch
    {
        0 => "No GPS",
        1 => "No Fix",
        2 => "2D Fix",
        3 => "3D Fix",
        4 => "DGPS",
        5 => "RTK Float",
        6 => "RTK Fixed",
        _ => $"Type {GpsFixType}"
    };

    public string BatteryStatus => BatteryRemaining >= 0 ? $"{BatteryRemaining}%" : $"{BatteryVoltage:F1}V";

    public double GroundSpeedMagnitude => Math.Sqrt(GroundSpeedX * GroundSpeedX + GroundSpeedY * GroundSpeedY);

    public TelemetryModel Clone()
    {
        return new TelemetryModel
        {
            Latitude = Latitude,
            Longitude = Longitude,
            AltitudeMsl = AltitudeMsl,
            AltitudeRelative = AltitudeRelative,
            GroundSpeedX = GroundSpeedX,
            GroundSpeedY = GroundSpeedY,
            VerticalSpeed = VerticalSpeed,
            Heading = Heading,
            Roll = Roll,
            Pitch = Pitch,
            Yaw = Yaw,
            RollSpeed = RollSpeed,
            PitchSpeed = PitchSpeed,
            YawSpeed = YawSpeed,
            Airspeed = Airspeed,
            GroundSpeed = GroundSpeed,
            Throttle = Throttle,
            ClimbRate = ClimbRate,
            GpsFixType = GpsFixType,
            SatelliteCount = SatelliteCount,
            Hdop = Hdop,
            Vdop = Vdop,
            GpsAltitude = GpsAltitude,
            BatteryVoltage = BatteryVoltage,
            BatteryCurrent = BatteryCurrent,
            BatteryRemaining = BatteryRemaining,
            FlightMode = FlightMode,
            IsArmed = IsArmed,
            VehicleType = VehicleType,
            SystemStatus = SystemStatus,
            LastPositionUpdate = LastPositionUpdate,
            LastAttitudeUpdate = LastAttitudeUpdate,
            LastGpsUpdate = LastGpsUpdate,
            LastHeartbeatUpdate = LastHeartbeatUpdate
        };
    }
}

public class TelemetryHealthStatus
{
    public TelemetryServiceState State { get; set; }
    public bool IsConnected { get; set; }
    public bool IsReceivingTelemetry { get; set; }
    public TelemetryStaleReason StaleReason { get; set; }
    public string LastValidFrameType { get; set; } = "None";
    public DateTime LastValidFrameTime { get; set; }
    public DateTime LastHeartbeatTime { get; set; }
    public DateTime LastPositionTime { get; set; }
    public DateTime LastGpsTime { get; set; }
    public DateTime LastAttitudeTime { get; set; }
    public DateTime LastVfrTime { get; set; }
    public DateTime LastSysStatusTime { get; set; }
    public TimeSpan HeartbeatAge { get; set; }
    public TimeSpan PositionAge { get; set; }
    public TimeSpan GpsAge { get; set; }
    public TimeSpan AttitudeAge { get; set; }
    public TimeSpan VfrAge { get; set; }
    public TimeSpan SysStatusAge { get; set; }
    public int StartupAttemptCount { get; set; }
    public int RecoveryAttemptCount { get; set; }

    public string StatusText => State switch
    {
        TelemetryServiceState.ConnectedAwaitingTelemetry => "Connected, negotiating telemetry...",
        TelemetryServiceState.NegotiatingStreams => "Connected, negotiating telemetry...",
        TelemetryServiceState.TelemetryActive => "Telemetry active",
        TelemetryServiceState.TelemetryStaleRecovering => "Telemetry stale, recovering...",
        _ => "No telemetry"
    };

    public TelemetryHealthStatus Clone()
    {
        return (TelemetryHealthStatus)MemberwiseClone();
    }
}
