namespace PavamanDroneConfigurator.Core.Models;

/// <summary>
/// Complete telemetry data model for drone position, attitude, and status.
/// Updated at 10Hz from MAVLink messages.
/// </summary>
public class TelemetryModel
{
    #region Position (from GLOBAL_POSITION_INT)
    
    /// <summary>Latitude in degrees</summary>
    public double Latitude { get; set; }
    
    /// <summary>Longitude in degrees</summary>
    public double Longitude { get; set; }
    
    /// <summary>Altitude above mean sea level in meters</summary>
    public double AltitudeMsl { get; set; }
    
    /// <summary>Altitude above ground/home in meters</summary>
    public double AltitudeRelative { get; set; }
    
    /// <summary>Ground speed in m/s (from GLOBAL_POSITION_INT)</summary>
    public double GroundSpeedX { get; set; }
    
    /// <summary>Ground speed in m/s (from GLOBAL_POSITION_INT)</summary>
    public double GroundSpeedY { get; set; }
    
    /// <summary>Vertical speed in m/s (positive = down)</summary>
    public double VerticalSpeed { get; set; }
    
    /// <summary>Heading in degrees (0-360, from GLOBAL_POSITION_INT hdg)</summary>
    public double Heading { get; set; }
    
    #endregion

    #region Attitude (from ATTITUDE)
    
    /// <summary>Roll angle in degrees</summary>
    public double Roll { get; set; }
    
    /// <summary>Pitch angle in degrees</summary>
    public double Pitch { get; set; }
    
    /// <summary>Yaw angle in degrees (0-360)</summary>
    public double Yaw { get; set; }
    
    /// <summary>Roll rate in degrees/sec</summary>
    public double RollSpeed { get; set; }
    
    /// <summary>Pitch rate in degrees/sec</summary>
    public double PitchSpeed { get; set; }
    
    /// <summary>Yaw rate in degrees/sec</summary>
    public double YawSpeed { get; set; }
    
    #endregion

    #region VFR HUD (from VFR_HUD)
    
    /// <summary>Airspeed in m/s</summary>
    public double Airspeed { get; set; }
    
    /// <summary>Ground speed in m/s</summary>
    public double GroundSpeed { get; set; }
    
    /// <summary>Throttle percentage (0-100)</summary>
    public int Throttle { get; set; }
    
    /// <summary>Climb rate in m/s</summary>
    public double ClimbRate { get; set; }
    
    #endregion

    #region GPS Status (from GPS_RAW_INT)
    
    /// <summary>GPS fix type (0=no fix, 2=2D, 3=3D, 4=DGPS, 5=RTK float, 6=RTK fixed)</summary>
    public int GpsFixType { get; set; }
    
    /// <summary>Number of satellites visible</summary>
    public int SatelliteCount { get; set; }
    
    /// <summary>Horizontal dilution of precision (lower is better)</summary>
    public double Hdop { get; set; }
    
    /// <summary>Vertical dilution of precision</summary>
    public double Vdop { get; set; }
    
    /// <summary>GPS altitude in meters</summary>
    public double GpsAltitude { get; set; }
    
    #endregion

    #region Battery Status (from SYS_STATUS)
    
    /// <summary>Battery voltage in Volts</summary>
    public double BatteryVoltage { get; set; }
    
    /// <summary>Battery current in Amps</summary>
    public double BatteryCurrent { get; set; }
    
    /// <summary>Battery remaining percentage (0-100, -1 if unknown)</summary>
    public int BatteryRemaining { get; set; }
    
    #endregion

    #region System Status (from HEARTBEAT)
    
    /// <summary>Current flight mode name</summary>
    public string FlightMode { get; set; } = "Unknown";
    
    /// <summary>Whether the vehicle is armed</summary>
    public bool IsArmed { get; set; }
    
    /// <summary>Vehicle type (copter, plane, rover, etc.)</summary>
    public string VehicleType { get; set; } = "Unknown";
    
    /// <summary>System status (standby, active, critical, etc.)</summary>
    public string SystemStatus { get; set; } = "Unknown";
    
    #endregion

    #region Timestamps
    
    /// <summary>Last position update time</summary>
    public DateTime LastPositionUpdate { get; set; }
    
    /// <summary>Last attitude update time</summary>
    public DateTime LastAttitudeUpdate { get; set; }
    
    /// <summary>Last GPS update time</summary>
    public DateTime LastGpsUpdate { get; set; }
    
    /// <summary>Last heartbeat time</summary>
    public DateTime LastHeartbeatUpdate { get; set; }
    
    #endregion

    #region Computed Properties
    
    /// <summary>Whether we have a valid GPS position</summary>
    public bool HasValidPosition => GpsFixType >= 2 && 
                                    Math.Abs(Latitude) > 0.0001 && 
                                    Math.Abs(Longitude) > 0.0001;
    
    /// <summary>GPS fix type description</summary>
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
    
    /// <summary>Battery status description</summary>
    public string BatteryStatus => BatteryRemaining >= 0 
        ? $"{BatteryRemaining}%" 
        : $"{BatteryVoltage:F1}V";
    
    /// <summary>Overall ground speed magnitude in m/s</summary>
    public double GroundSpeedMagnitude => Math.Sqrt(GroundSpeedX * GroundSpeedX + GroundSpeedY * GroundSpeedY);
    
    #endregion

    /// <summary>
    /// Creates a deep copy of this telemetry model
    /// </summary>
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
