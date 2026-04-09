namespace PavamanDroneConfigurator.UI.Controls;

/// <summary>
/// GPS track point for map display with extended data
/// </summary>
public class GpsTrackPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Altitude { get; set; }
    public double Timestamp { get; set; }
    public double Speed { get; set; }
    public double Heading { get; set; }
    public int NumSatellites { get; set; }
    public double HorizontalAccuracy { get; set; }
    public double VerticalAccuracy { get; set; }
    public double GroundSpeed { get; set; }
    public double VerticalSpeed { get; set; }
}

/// <summary>
/// Waypoint point for map display with label
/// </summary>
public class WaypointPoint
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string Label { get; set; } = string.Empty;
}
