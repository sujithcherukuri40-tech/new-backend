using System.Collections.ObjectModel;

namespace PavamanDroneConfigurator.Core.Models;

/// <summary>
/// MAV_CMD command types for mission items
/// </summary>
public enum MissionCommandType : ushort
{
    NavWaypoint = 16,
    NavLoiterUnlim = 17,
    NavLoiterTurns = 18,
    NavLoiterTime = 19,
    NavReturnToLaunch = 20,
    NavLand = 21,
    NavTakeoff = 22,
    NavDelay = 93,
    DoChangeSpeed = 178,
    DoSetRelay = 181,
    DoSetServo = 183,
    DoSetRoi = 201,
    DoDigicamControl = 203
}

/// <summary>
/// MAV_FRAME coordinate frame types
/// </summary>
public enum MissionFrame : byte
{
    Global = 0,
    LocalNed = 1,
    Mission = 2,
    GlobalRelativeAlt = 3,
    LocalEnu = 4,
    GlobalInt = 5,
    GlobalRelativeAltInt = 6,
    LocalOffsetNed = 7,
    BodyNed = 8,
    BodyOffsetNed = 9,
    GlobalTerrainAlt = 10,
    GlobalTerrainAltInt = 11
}

/// <summary>
/// Single mission item (waypoint or command)
/// </summary>
public class MissionItem
{
    public int Index { get; set; }
    public MissionCommandType Command { get; set; } = MissionCommandType.NavWaypoint;
    public MissionFrame Frame { get; set; } = MissionFrame.GlobalRelativeAltInt;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public float Altitude { get; set; }
    public float Param1 { get; set; } // Hold time / turns / speed
    public float Param2 { get; set; } // Acceptance radius
    public float Param3 { get; set; } // Pass radius / relay number
    public float Param4 { get; set; } // Yaw angle
    public bool IsCurrent { get; set; }
    public bool Autocontinue { get; set; } = true;
    
    /// <summary>
    /// Whether spray should be ON at this waypoint
    /// </summary>
    public bool SprayEnabled { get; set; }
    
    /// <summary>
    /// Whether to trigger camera at this waypoint
    /// </summary>
    public bool CameraTrigger { get; set; }
    
    public string DisplayName => Command switch
    {
        MissionCommandType.NavWaypoint => $"WP {Index}",
        MissionCommandType.NavTakeoff => "TAKEOFF",
        MissionCommandType.NavLand => "LAND",
        MissionCommandType.NavReturnToLaunch => "RTL",
        MissionCommandType.NavLoiterTime => $"LOITER {Param1}s",
        MissionCommandType.NavLoiterTurns => $"ORBIT {Param1}x",
        MissionCommandType.NavLoiterUnlim => "LOITER",
        MissionCommandType.DoSetRelay => Param2 > 0 ? "SPRAY ON" : "SPRAY OFF",
        MissionCommandType.DoChangeSpeed => $"SPEED {Param2}m/s",
        _ => Command.ToString()
    };
    
    public string Icon => Command switch
    {
        MissionCommandType.NavWaypoint => "??",
        MissionCommandType.NavTakeoff => "??",
        MissionCommandType.NavLand => "??",
        MissionCommandType.NavReturnToLaunch => "??",
        MissionCommandType.NavLoiterTime => "?",
        MissionCommandType.NavLoiterTurns => "??",
        MissionCommandType.NavLoiterUnlim => "?",
        MissionCommandType.DoSetRelay => "??",
        MissionCommandType.DoChangeSpeed => "?",
        MissionCommandType.DoDigicamControl => "??",
        _ => "?"
    };
}

/// <summary>
/// Complete mission with metadata
/// </summary>
public class Mission
{
    public string Name { get; set; } = "Untitled Mission";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public ObservableCollection<MissionItem> Items { get; set; } = new();
    
    /// <summary>
    /// Estimated total distance in kilometers
    /// </summary>
    public double TotalDistanceKm { get; set; }
    
    /// <summary>
    /// Estimated flight time
    /// </summary>
    public TimeSpan EstimatedFlightTime { get; set; }
    
    /// <summary>
    /// Home position (if set)
    /// </summary>
    public GeoPoint? HomePosition { get; set; }
    
    /// <summary>
    /// Whether mission is valid for upload
    /// </summary>
    public bool IsValid => Items.Count > 0;
}

/// <summary>
/// Geographic point with altitude
/// </summary>
public class GeoPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Altitude { get; set; }
    
    public GeoPoint() { }
    
    public GeoPoint(double lat, double lon, double alt = 0)
    {
        Latitude = lat;
        Longitude = lon;
        Altitude = alt;
    }
    
    /// <summary>
    /// Calculate distance to another point in meters using Haversine formula
    /// </summary>
    public double DistanceTo(GeoPoint other)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = ToRadians(other.Latitude - Latitude);
        var dLon = ToRadians(other.Longitude - Longitude);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(Latitude)) * Math.Cos(ToRadians(other.Latitude)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }
    
    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;
}

/// <summary>
/// Survey grid configuration parameters
/// </summary>
public class SurveyConfig
{
    public List<GeoPoint> BoundaryPolygon { get; set; } = new();
    public double LaneSpacingMeters { get; set; } = 3.5;
    public double AltitudeMeters { get; set; } = 3.0;
    public double AngleDegrees { get; set; } = 0;
    public double TurnaroundMeters { get; set; } = 5.0;
    public double FlightSpeedMs { get; set; } = 4.0;
    public double SprayWidthMeters { get; set; } = 4.0;
    public bool AutoOptimizeAngle { get; set; } = true;
    public bool InsertSprayCommands { get; set; } = true;
}

/// <summary>
/// Result of survey grid generation
/// </summary>
public class SurveyResult
{
    public List<GeoPoint> Waypoints { get; set; } = new();
    public int TotalLanes { get; set; }
    public double TotalDistanceKm { get; set; }
    public TimeSpan EstimatedFlightTime { get; set; }
    public double CoverageAreaHa { get; set; }
    public double EstimatedLiquidLiters { get; set; }
    public double OptimizedAngleDegrees { get; set; }
}
