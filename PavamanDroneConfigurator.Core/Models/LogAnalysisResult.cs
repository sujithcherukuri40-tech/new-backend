using PavamanDroneConfigurator.Core.Enums;

namespace PavamanDroneConfigurator.Core.Models;

/// <summary>
/// Represents the result of analyzing a flight log.
/// </summary>
public class LogAnalysisResult
{
    /// <summary>
    /// Path to the analyzed log file.
    /// </summary>
    public string LogFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Name of the analyzed log file.
    /// </summary>
    public string LogFileName { get; set; } = string.Empty;

    /// <summary>
    /// Time when analysis was performed.
    /// </summary>
    public DateTime AnalysisTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Whether the analysis completed successfully.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Error message if analysis failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Flight summary information.
    /// </summary>
    public FlightSummary? Summary { get; set; }

    /// <summary>
    /// List of issues/events detected during analysis.
    /// </summary>
    public List<LogAnalysisIssue> Issues { get; set; } = new();

    /// <summary>
    /// Overall health score (0-100).
    /// </summary>
    public int HealthScore { get; set; }

    /// <summary>
    /// Health score display with color indicator.
    /// </summary>
    public string HealthScoreDisplay => HealthScore >= 80 ? "Good" : HealthScore >= 50 ? "Fair" : "Poor";

    /// <summary>
    /// Count of critical issues.
    /// </summary>
    public int CriticalCount => Issues.Count(i => i.Severity == LogMessageSeverity.Critical);

    /// <summary>
    /// Count of error issues.
    /// </summary>
    public int ErrorCount => Issues.Count(i => i.Severity == LogMessageSeverity.Error);

    /// <summary>
    /// Count of warning issues.
    /// </summary>
    public int WarningCount => Issues.Count(i => i.Severity == LogMessageSeverity.Warning);

    /// <summary>
    /// Count of info issues.
    /// </summary>
    public int InfoCount => Issues.Count(i => i.Severity == LogMessageSeverity.Info);
}

/// <summary>
/// Summary information extracted from a flight log.
/// </summary>
public class FlightSummary
{
    /// <summary>
    /// Total flight duration.
    /// </summary>
    public TimeSpan FlightDuration { get; set; }

    /// <summary>
    /// Display-friendly flight duration.
    /// </summary>
    public string FlightDurationDisplay => FlightDuration.TotalMinutes < 1 
        ? $"{FlightDuration.Seconds}s" 
        : $"{(int)FlightDuration.TotalMinutes}m {FlightDuration.Seconds}s";

    /// <summary>
    /// Maximum altitude reached (meters).
    /// </summary>
    public float MaxAltitude { get; set; }

    /// <summary>
    /// Maximum ground speed (m/s).
    /// </summary>
    public float MaxSpeed { get; set; }

    /// <summary>
    /// Maximum distance from home (meters).
    /// </summary>
    public float MaxDistance { get; set; }

    /// <summary>
    /// Total distance traveled (meters).
    /// </summary>
    public float TotalDistance { get; set; }

    /// <summary>
    /// Average battery voltage.
    /// </summary>
    public float AverageVoltage { get; set; }

    /// <summary>
    /// Minimum battery voltage.
    /// </summary>
    public float MinVoltage { get; set; }

    /// <summary>
    /// Battery capacity used (mAh).
    /// </summary>
    public float BatteryUsed { get; set; }

    /// <summary>
    /// Number of GPS satellites.
    /// </summary>
    public int GpsSatellites { get; set; }

    /// <summary>
    /// GPS fix type.
    /// </summary>
    public string GpsFixType { get; set; } = "Unknown";

    /// <summary>
    /// Vehicle type detected.
    /// </summary>
    public string VehicleType { get; set; } = "Unknown";

    /// <summary>
    /// Firmware version detected.
    /// </summary>
    public string FirmwareVersion { get; set; } = "Unknown";

    /// <summary>
    /// Flight modes used during the flight.
    /// </summary>
    public List<string> FlightModesUsed { get; set; } = new();

    /// <summary>
    /// Whether any failsafe was triggered.
    /// </summary>
    public bool FailsafeTriggered { get; set; }

    /// <summary>
    /// Whether the vehicle crashed.
    /// </summary>
    public bool CrashDetected { get; set; }

    /// <summary>
    /// Average vibration levels (X, Y, Z).
    /// </summary>
    public (float X, float Y, float Z) AverageVibration { get; set; }

    /// <summary>
    /// Maximum vibration levels (X, Y, Z).
    /// </summary>
    public (float X, float Y, float Z) MaxVibration { get; set; }
}

/// <summary>
/// Represents an issue or event detected during log analysis.
/// </summary>
public class LogAnalysisIssue
{
    /// <summary>
    /// Severity of the issue.
    /// </summary>
    public LogMessageSeverity Severity { get; set; }

    /// <summary>
    /// Category of the issue.
    /// </summary>
    public LogAnalysisCategory Category { get; set; }

    /// <summary>
    /// Short title of the issue.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of the issue.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when the issue occurred (relative to log start).
    /// </summary>
    public TimeSpan Timestamp { get; set; }

    /// <summary>
    /// Timestamp display.
    /// </summary>
    public string TimestampDisplay => $"{(int)Timestamp.TotalMinutes}:{Timestamp.Seconds:D2}";

    /// <summary>
    /// Suggested action or fix.
    /// </summary>
    public string? Suggestion { get; set; }

    /// <summary>
    /// Whether a suggestion is available.
    /// </summary>
    public bool HasSuggestion => !string.IsNullOrEmpty(Suggestion);

    /// <summary>
    /// Severity icon for display.
    /// </summary>
    public string SeverityIcon => Severity switch
    {
        LogMessageSeverity.Critical => "??",
        LogMessageSeverity.Error => "??",
        LogMessageSeverity.Warning => "?",
        LogMessageSeverity.Info => "??",
        _ => "•"
    };

    /// <summary>
    /// Category icon for display.
    /// </summary>
    public string CategoryIcon => Category switch
    {
        LogAnalysisCategory.MotorPerformance => "??",
        LogAnalysisCategory.VibrationLevels => "??",
        LogAnalysisCategory.GpsSignal => "??",
        LogAnalysisCategory.CompassHealth => "??",
        LogAnalysisCategory.BatteryHealth => "??",
        LogAnalysisCategory.EkfStatus => "??",
        LogAnalysisCategory.RcSignal => "??",
        LogAnalysisCategory.Failsafe => "??",
        LogAnalysisCategory.Crash => "??",
        LogAnalysisCategory.FlightMode => "??",
        LogAnalysisCategory.Altitude => "??",
        LogAnalysisCategory.Attitude => "??",
        _ => "??"
    };
}

/// <summary>
/// Progress information for log download operations.
/// </summary>
public class LogDownloadProgress
{
    /// <summary>
    /// Current file being downloaded.
    /// </summary>
    public string CurrentFile { get; set; } = string.Empty;

    /// <summary>
    /// Bytes downloaded so far.
    /// </summary>
    public long BytesDownloaded { get; set; }

    /// <summary>
    /// Total bytes to download.
    /// </summary>
    public long TotalBytes { get; set; }

    /// <summary>
    /// Progress percentage (0-100).
    /// </summary>
    public int ProgressPercent => TotalBytes > 0 ? (int)(BytesDownloaded * 100 / TotalBytes) : 0;

    /// <summary>
    /// Download speed in bytes per second.
    /// </summary>
    public long BytesPerSecond { get; set; }

    /// <summary>
    /// Estimated time remaining.
    /// </summary>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    /// <summary>
    /// Display-friendly progress.
    /// </summary>
    public string ProgressDisplay
    {
        get
        {
            var downloaded = FormatBytes(BytesDownloaded);
            var total = FormatBytes(TotalBytes);
            var speed = FormatBytes(BytesPerSecond) + "/s";
            return $"{downloaded} / {total} ({speed})";
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
