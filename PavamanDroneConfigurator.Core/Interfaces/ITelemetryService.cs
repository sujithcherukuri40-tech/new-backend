using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Core.Interfaces;

public interface ITelemetryService
{
    TelemetryModel CurrentTelemetry { get; }
    TelemetryServiceState CurrentState { get; }
    TelemetryHealthStatus CurrentHealth { get; }
    bool IsReceivingTelemetry { get; }
    bool HasValidPosition { get; }

    event EventHandler<TelemetryModel>? TelemetryUpdated;
    event EventHandler<PositionChangedEventArgs>? PositionChanged;
    event EventHandler<AttitudeChangedEventArgs>? AttitudeChanged;
    event EventHandler<BatteryStatusEventArgs>? BatteryStatusChanged;
    event EventHandler<GpsStatusEventArgs>? GpsStatusChanged;
    event EventHandler<bool>? TelemetryAvailabilityChanged;
    event EventHandler<TelemetryHealthStatus>? TelemetryHealthChanged;

    void Start();
    void Stop();
    void Clear();
    void RequestStreams();
    IReadOnlyList<(double Latitude, double Longitude, double Altitude, DateTime Timestamp)> GetFlightPath();
    void ClearFlightPath();
    string GetTelemetryDebugDump();
}

public class PositionChangedEventArgs : EventArgs
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double AltitudeRelative { get; set; }
    public double Heading { get; set; }
    public double GroundSpeed { get; set; }
    public DateTime Timestamp { get; set; }
}

public class AttitudeChangedEventArgs : EventArgs
{
    public double Roll { get; set; }
    public double Pitch { get; set; }
    public double Yaw { get; set; }
    public DateTime Timestamp { get; set; }
}

public class BatteryStatusEventArgs : EventArgs
{
    public double Voltage { get; set; }
    public double Current { get; set; }
    public int RemainingPercent { get; set; }
}

public class GpsStatusEventArgs : EventArgs
{
    public int FixType { get; set; }
    public int SatelliteCount { get; set; }
    public double Hdop { get; set; }
    public bool HasValidFix { get; set; }
}
