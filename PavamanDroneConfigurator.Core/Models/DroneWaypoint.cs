namespace PavamanDroneConfigurator.Core.Models;

/// <summary>
/// Represents a waypoint in a drone flight path extracted from log files.
/// Contains GPS coordinates, altitude, timestamp, and waypoint type information.
/// </summary>
public class DroneWaypoint
{
    /// <summary>
    /// Sequence number of the waypoint in the mission/flight path
    /// </summary>
    public int Sequence { get; set; }
    
    /// <summary>
    /// Latitude in decimal degrees (WGS84)
    /// </summary>
    public double Latitude { get; set; }
    
    /// <summary>
    /// Longitude in decimal degrees (WGS84)
    /// </summary>
    public double Longitude { get; set; }
    
    /// <summary>
    /// Altitude in meters (relative or absolute depending on frame)
    /// </summary>
    public double AltitudeMeters { get; set; }
    
    /// <summary>
    /// Timestamp of when the waypoint was reached/recorded
    /// </summary>
    public TimeSpan Timestamp { get; set; }
    
    /// <summary>
    /// Type of waypoint (Takeoff, Navigate, Loiter, Land, Home, RTL, etc.)
    /// </summary>
    public WaypointType Type { get; set; }
    
    /// <summary>
    /// MAVLink command ID (MAV_CMD) that defines this waypoint
    /// </summary>
    public int CommandId { get; set; }
    
    /// <summary>
    /// Altitude frame reference (relative, absolute, terrain, etc.)
    /// </summary>
    public AltitudeFrame Frame { get; set; } = AltitudeFrame.RelativeHome;
    
    /// <summary>
    /// Hold time in seconds for loiter waypoints
    /// </summary>
    public double HoldTimeSeconds { get; set; }
    
    /// <summary>
    /// Accept radius in meters (waypoint is considered reached within this distance)
    /// </summary>
    public double AcceptRadiusMeters { get; set; }
    
    /// <summary>
    /// Pass-by radius for fly-through waypoints
    /// </summary>
    public double PassByRadiusMeters { get; set; }
    
    /// <summary>
    /// Desired yaw angle at waypoint (degrees, 0 = North)
    /// </summary>
    public double? YawDegrees { get; set; }
    
    /// <summary>
    /// Whether this waypoint is the home/launch position
    /// </summary>
    public bool IsHome { get; set; }
    
    /// <summary>
    /// Whether this waypoint is the current target
    /// </summary>
    public bool IsCurrent { get; set; }
    
    /// <summary>
    /// Whether the waypoint has valid GPS coordinates
    /// </summary>
    public bool HasValidCoordinates => 
        Math.Abs(Latitude) > 0.001 && 
        Math.Abs(Longitude) > 0.001 &&
        Math.Abs(Latitude) <= 90 && 
        Math.Abs(Longitude) <= 180;
    
    /// <summary>
    /// Display label for the waypoint
    /// </summary>
    public string Label => Type switch
    {
        WaypointType.Home => "Home",
        WaypointType.Takeoff => $"Takeoff ({AltitudeMeters:F0}m)",
        WaypointType.Land => "Land",
        WaypointType.RTL => "RTL",
        WaypointType.Loiter => $"Loiter ({HoldTimeSeconds:F0}s)",
        WaypointType.LoiterUnlimited => "Loiter ?",
        WaypointType.Navigate => $"WP{Sequence}",
        WaypointType.Spline => $"Spline{Sequence}",
        _ => $"WP{Sequence}"
    };
    
    /// <summary>
    /// Full display string with coordinates
    /// </summary>
    public string DisplayString => 
        $"{Label} ({Latitude:F6}°, {Longitude:F6}°) Alt: {AltitudeMeters:F1}m";
    
    /// <summary>
    /// Timestamp display string
    /// </summary>
    public string TimestampDisplay => 
        Timestamp.TotalSeconds > 0 ? Timestamp.ToString(@"hh\:mm\:ss\.fff") : "N/A";
}

/// <summary>
/// Types of waypoints in a drone mission
/// </summary>
public enum WaypointType
{
    /// <summary>Home/Launch position</summary>
    Home,
    
    /// <summary>Takeoff command</summary>
    Takeoff,
    
    /// <summary>Standard navigation waypoint</summary>
    Navigate,
    
    /// <summary>Spline navigation waypoint (curved path)</summary>
    Spline,
    
    /// <summary>Loiter at position for specified time</summary>
    Loiter,
    
    /// <summary>Loiter indefinitely at position</summary>
    LoiterUnlimited,
    
    /// <summary>Loiter and orbit around a point</summary>
    LoiterTurns,
    
    /// <summary>Land at current or specified position</summary>
    Land,
    
    /// <summary>Return to launch position</summary>
    RTL,
    
    /// <summary>Guided mode target</summary>
    Guided,
    
    /// <summary>Region of Interest (camera target)</summary>
    ROI,
    
    /// <summary>Delay/hold at current position</summary>
    Delay,
    
    /// <summary>Condition waypoint</summary>
    Condition,
    
    /// <summary>Do command (servo, relay, etc.)</summary>
    DoCommand,
    
    /// <summary>Unknown or unsupported command</summary>
    Unknown
}

/// <summary>
/// Altitude frame reference for waypoints
/// </summary>
public enum AltitudeFrame
{
    /// <summary>Altitude relative to home position</summary>
    RelativeHome = 0,
    
    /// <summary>Absolute altitude (MSL - Mean Sea Level)</summary>
    Absolute = 1,
    
    /// <summary>Altitude relative to terrain</summary>
    Terrain = 2,
    
    /// <summary>Altitude relative to ground (AGL)</summary>
    GroundLevel = 3
}

/// <summary>
/// MAVLink command IDs for common waypoint types
/// </summary>
public static class MavCmd
{
    public const int NAV_WAYPOINT = 16;
    public const int NAV_LOITER_UNLIM = 17;
    public const int NAV_LOITER_TURNS = 18;
    public const int NAV_LOITER_TIME = 19;
    public const int NAV_RETURN_TO_LAUNCH = 20;
    public const int NAV_LAND = 21;
    public const int NAV_TAKEOFF = 22;
    public const int NAV_LAND_LOCAL = 23;
    public const int NAV_TAKEOFF_LOCAL = 24;
    public const int NAV_FOLLOW = 25;
    public const int NAV_CONTINUE_AND_CHANGE_ALT = 30;
    public const int NAV_LOITER_TO_ALT = 31;
    public const int DO_FOLLOW = 32;
    public const int DO_FOLLOW_REPOSITION = 33;
    public const int NAV_ROI = 80;
    public const int NAV_PATHPLANNING = 81;
    public const int NAV_SPLINE_WAYPOINT = 82;
    public const int NAV_GUIDED_ENABLE = 92;
    public const int NAV_DELAY = 93;
    public const int NAV_PAYLOAD_PLACE = 94;
    public const int NAV_LAST = 95;
    public const int CONDITION_DELAY = 112;
    public const int CONDITION_CHANGE_ALT = 113;
    public const int CONDITION_DISTANCE = 114;
    public const int CONDITION_YAW = 115;
    public const int DO_SET_MODE = 176;
    public const int DO_JUMP = 177;
    public const int DO_CHANGE_SPEED = 178;
    public const int DO_SET_HOME = 179;
    public const int DO_SET_SERVO = 183;
    public const int DO_SET_RELAY = 181;
    public const int DO_REPEAT_SERVO = 184;
    public const int DO_REPEAT_RELAY = 182;
    public const int DO_DIGICAM_CONTROL = 203;
    public const int DO_MOUNT_CONTROL = 205;
    public const int DO_GRIPPER = 211;
    public const int DO_AUTOTUNE_ENABLE = 212;
    
    /// <summary>
    /// Convert MAVLink command ID to WaypointType
    /// </summary>
    public static WaypointType ToWaypointType(int cmdId) => cmdId switch
    {
        NAV_WAYPOINT => WaypointType.Navigate,
        NAV_TAKEOFF => WaypointType.Takeoff,
        NAV_LAND => WaypointType.Land,
        NAV_RETURN_TO_LAUNCH => WaypointType.RTL,
        NAV_LOITER_UNLIM => WaypointType.LoiterUnlimited,
        NAV_LOITER_TURNS => WaypointType.LoiterTurns,
        NAV_LOITER_TIME => WaypointType.Loiter,
        NAV_LOITER_TO_ALT => WaypointType.Loiter,
        NAV_SPLINE_WAYPOINT => WaypointType.Spline,
        NAV_ROI => WaypointType.ROI,
        NAV_DELAY => WaypointType.Delay,
        NAV_GUIDED_ENABLE => WaypointType.Guided,
        CONDITION_DELAY or CONDITION_CHANGE_ALT or CONDITION_DISTANCE or CONDITION_YAW => WaypointType.Condition,
        >= 176 and <= 220 => WaypointType.DoCommand,
        _ => WaypointType.Unknown
    };
    
    /// <summary>
    /// Get human-readable name for a command ID
    /// </summary>
    public static string GetCommandName(int cmdId) => cmdId switch
    {
        NAV_WAYPOINT => "Waypoint",
        NAV_TAKEOFF => "Takeoff",
        NAV_LAND => "Land",
        NAV_RETURN_TO_LAUNCH => "Return to Launch",
        NAV_LOITER_UNLIM => "Loiter Unlimited",
        NAV_LOITER_TURNS => "Loiter Turns",
        NAV_LOITER_TIME => "Loiter Time",
        NAV_SPLINE_WAYPOINT => "Spline Waypoint",
        NAV_ROI => "Region of Interest",
        NAV_DELAY => "Delay",
        DO_SET_HOME => "Set Home",
        DO_CHANGE_SPEED => "Change Speed",
        DO_SET_SERVO => "Set Servo",
        DO_DIGICAM_CONTROL => "Camera Control",
        _ => $"CMD_{cmdId}"
    };
}
