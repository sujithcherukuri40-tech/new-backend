using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Service for detecting flight events in log data.
/// Analyzes log for arming, failsafes, EKF issues, GPS problems, vibration warnings, etc.
/// </summary>
public interface ILogEventDetector
{
    /// <summary>
    /// Detects all events in the loaded log.
    /// </summary>
    /// <param name="progress">Progress callback (0-100)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<List<LogEvent>> DetectEventsAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets events within a time range.
    /// </summary>
    Task<List<LogEvent>> GetEventsInRangeAsync(
        double startTime,
        double endTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets events by severity.
    /// </summary>
    Task<List<LogEvent>> GetEventsBySeverityAsync(
        LogEventSeverity minSeverity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets events by type.
    /// </summary>
    Task<List<LogEvent>> GetEventsByTypeAsync(
        LogEventType eventType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a summary of detected events.
    /// </summary>
    EventSummary GetEventSummary();
}

/// <summary>
/// Represents a detected flight event with comprehensive data.
/// </summary>
public class LogEvent
{
    public int Id { get; set; }
    
    // Timestamp information
    public double Timestamp { get; set; }
    public string TimestampDisplay => TimeSpan.FromSeconds(Timestamp).ToString(@"hh\:mm\:ss\.fff");
    
    // Event classification
    public LogEventType Type { get; set; }
    public LogEventSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Details { get; set; }
    
    // Source/Subsystem information
    public string Source { get; set; } = string.Empty;  // e.g., "GPS", "IMU", "EKF", "RC", "Battery"
    public string Subsystem { get; set; } = string.Empty; // More specific subsystem info
    
    // MAVLink Component/System ID
    public int? ComponentId { get; set; }  // e.g., 1 = Autopilot, 100 = Camera
    public int? SystemId { get; set; }
    public string ComponentDisplay => ComponentId.HasValue ? GetComponentName(ComponentId.Value) : "";
    
    // Location at time of event (if GPS available)
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Altitude { get; set; }
    public bool HasLocation => Latitude.HasValue && Longitude.HasValue;
    public string LocationDisplay => HasLocation 
        ? $"{Latitude:F6}, {Longitude:F6}" + (Altitude.HasValue ? $" @ {Altitude:F1}m" : "")
        : "N/A";
    
    // Raw MAVLink event information (for expandable view)
    public int? EventId { get; set; }
    public int? EventSubId { get; set; }
    public long? LogFileOffset { get; set; }
    public string? RawMessage { get; set; }
    
    // Additional data dictionary
    public Dictionary<string, object> Data { get; set; } = new();
    
    // Display helpers
    public string SeverityIcon => Severity switch
    {
        LogEventSeverity.Emergency => "??",
        LogEventSeverity.Critical => "??",
        LogEventSeverity.Error => "?",
        LogEventSeverity.Warning => "??",
        LogEventSeverity.Notice => "??",
        LogEventSeverity.Info => "??",
        LogEventSeverity.Debug => "??",
        _ => "?"
    };
    
    public string SeverityDisplay => Severity switch
    {
        LogEventSeverity.Emergency => "Emergency",
        LogEventSeverity.Critical => "Critical",
        LogEventSeverity.Error => "Error",
        LogEventSeverity.Warning => "Warning",
        LogEventSeverity.Notice => "Notice",
        LogEventSeverity.Info => "Info",
        LogEventSeverity.Debug => "Debug",
        _ => "Unknown"
    };
    
    public string TypeDisplay => Type switch
    {
        LogEventType.ModeChange => "Mode Changed",
        LogEventType.Arming => "Armed",
        LogEventType.Disarming => "Disarmed",
        LogEventType.Failsafe => "Failsafe",
        LogEventType.EkfWarning => "EKF Warning",
        LogEventType.EkfError => "EKF Error",
        LogEventType.GpsLoss => "GPS Lost",
        LogEventType.GpsGlitch => "GPS Glitch",
        LogEventType.GpsRecovery => "GPS Recovered",
        LogEventType.Vibration => "Vibration",
        LogEventType.Clipping => "Accel Clipping",
        LogEventType.BatteryLow => "Battery Low",
        LogEventType.BatteryCritical => "Battery Critical",
        LogEventType.BatteryFailsafe => "Battery Failsafe",
        LogEventType.MotorImbalance => "Motor Imbalance",
        LogEventType.CompassVariance => "Compass Variance",
        LogEventType.BarometerError => "Barometer Error",
        LogEventType.RcLoss => "RC Signal Lost",
        LogEventType.RcRecovery => "RC Recovered",
        LogEventType.Crash => "Crash Detected",
        LogEventType.Takeoff => "Takeoff",
        LogEventType.Landing => "Landing",
        LogEventType.Waypoint => "Waypoint Reached",
        LogEventType.RallyPoint => "Rally Point",
        LogEventType.Fence => "Geofence",
        LogEventType.MissionStart => "Mission Started",
        LogEventType.MissionComplete => "Mission Complete",
        LogEventType.MotorFailure => "Motor Failure",
        LogEventType.Custom => Title,
        _ => Title
    };
    
    private static string GetComponentName(int componentId)
    {
        return componentId switch
        {
            1 => "Autopilot",
            25 => "GPS",
            100 => "Camera",
            154 => "Gimbal",
            190 => "Companion",
            _ => $"Component {componentId}"
        };
    }
}

/// <summary>
/// Event severity levels (MAVLink compatible).
/// </summary>
public enum LogEventSeverity
{
    Debug = 0,      // Debug-level logs
    Info = 1,       // Informational system messages
    Notice = 2,     // Useful but not critical
    Warning = 3,    // Something needs attention
    Error = 4,      // Something failed; safety risk
    Critical = 5,   // Requires immediate action
    Emergency = 6   // System in emergency mode
}

/// <summary>
/// Event type categories.
/// </summary>
public enum LogEventType
{
    ModeChange,
    Arming,
    Disarming,
    Failsafe,
    EkfWarning,
    EkfError,
    GpsLoss,
    GpsGlitch,
    GpsRecovery,
    Vibration,
    Clipping,
    BatteryLow,
    BatteryCritical,
    BatteryFailsafe,
    MotorImbalance,
    MotorFailure,
    CompassVariance,
    BarometerError,
    RcLoss,
    RcRecovery,
    Crash,
    Takeoff,
    Landing,
    Waypoint,
    RallyPoint,
    Fence,
    MissionStart,
    MissionComplete,
    Custom
}

/// <summary>
/// Summary of detected events.
/// </summary>
public class EventSummary
{
    public int TotalEvents { get; set; }
    public int DebugCount { get; set; }
    public int InfoCount { get; set; }
    public int NoticeCount { get; set; }
    public int WarningCount { get; set; }
    public int ErrorCount { get; set; }
    public int CriticalCount { get; set; }
    public int EmergencyCount { get; set; }
    public Dictionary<LogEventType, int> EventsByType { get; set; } = new();
    public Dictionary<string, int> EventsBySource { get; set; } = new();
    public double FlightDurationSeconds { get; set; }
    public bool HasCriticalEvents => CriticalCount > 0 || EmergencyCount > 0;
    public bool HasErrors => ErrorCount > 0;
    
    /// <summary>
    /// Total count of all events.
    /// </summary>
    public int TotalCount => TotalEvents;
}
