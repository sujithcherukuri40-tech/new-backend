using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Service for managing real-time drone telemetry.
/// Subscribes to MAVLink events, processes telemetry data,
/// and provides throttled updates (10Hz) to ViewModels.
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Current telemetry data
    /// </summary>
    TelemetryModel CurrentTelemetry { get; }
    
    /// <summary>
    /// Whether telemetry is currently being received
    /// </summary>
    bool IsReceivingTelemetry { get; }
    
    /// <summary>
    /// Whether we have a valid GPS position
    /// </summary>
    bool HasValidPosition { get; }
    
    /// <summary>
    /// Event raised when telemetry is updated (throttled to 10Hz)
    /// </summary>
    event EventHandler<TelemetryModel>? TelemetryUpdated;
    
    /// <summary>
    /// Event raised when position changes (throttled to 10Hz)
    /// </summary>
    event EventHandler<PositionChangedEventArgs>? PositionChanged;
    
    /// <summary>
    /// Event raised when attitude changes (throttled to 10Hz)
    /// </summary>
    event EventHandler<AttitudeChangedEventArgs>? AttitudeChanged;
    
    /// <summary>
    /// Event raised when battery status changes
    /// </summary>
    event EventHandler<BatteryStatusEventArgs>? BatteryStatusChanged;
    
    /// <summary>
    /// Event raised when GPS status changes
    /// </summary>
    event EventHandler<GpsStatusEventArgs>? GpsStatusChanged;
    
    /// <summary>
    /// Event raised when connection status or telemetry availability changes
    /// </summary>
    event EventHandler<bool>? TelemetryAvailabilityChanged;
    
    /// <summary>
    /// Start receiving telemetry data
    /// </summary>
    void Start();
    
    /// <summary>
    /// Stop receiving telemetry data
    /// </summary>
    void Stop();
    
    /// <summary>
    /// Clear all telemetry data (on disconnect)
    /// </summary>
    void Clear();
    
    /// <summary>
    /// Get flight path history (list of positions for trail)
    /// </summary>
    IReadOnlyList<(double Latitude, double Longitude, double Altitude, DateTime Timestamp)> GetFlightPath();
    
    /// <summary>
    /// Clear flight path history
    /// </summary>
    void ClearFlightPath();
}

/// <summary>
/// Event args for position changes
/// </summary>
public class PositionChangedEventArgs : EventArgs
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeRelative { get; set; }
    public double Heading { get; set; }
    public double GroundSpeed { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Event args for attitude changes
/// </summary>
public class AttitudeChangedEventArgs : EventArgs
{
    public double Roll { get; set; }
    public double Pitch { get; set; }
    public double Yaw { get; set; }
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// Event args for battery status
/// </summary>
public class BatteryStatusEventArgs : EventArgs
{
    public double Voltage { get; set; }
    public double Current { get; set; }
    public int RemainingPercent { get; set; }
}

/// <summary>
/// Event args for GPS status
/// </summary>
public class GpsStatusEventArgs : EventArgs
{
    public int FixType { get; set; }
    public int SatelliteCount { get; set; }
    public double Hdop { get; set; }
    public bool HasValidFix { get; set; }
}
