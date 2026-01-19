namespace PavamanDroneConfigurator.Core.Enums;

/// <summary>
/// Detailed drone log format types for the universal log analyzer.
/// This provides more granular format detection beyond the basic LogFileType enum.
/// </summary>
public enum DroneLogFormat
{
    /// <summary>
    /// Unknown or unsupported format.
    /// </summary>
    Unknown,
    
    /// <summary>
    /// ArduPilot DataFlash binary log (.bin).
    /// </summary>
    ArduPilotDataFlashBinary,
    
    /// <summary>
    /// ArduPilot DataFlash text log (.log).
    /// </summary>
    ArduPilotDataFlashText,
    
    /// <summary>
    /// MAVLink telemetry log (.tlog).
    /// </summary>
    MavLinkTelemetry,
    
    /// <summary>
    /// PX4 ULog binary format (.ulg).
    /// </summary>
    PX4ULog,
    
    /// <summary>
    /// Generic CSV exported log.
    /// </summary>
    CsvExport,
    
    /// <summary>
    /// DJI TXT log format.
    /// </summary>
    DjiTxt,
    
    /// <summary>
    /// DJI CSV log format.
    /// </summary>
    DjiCsv
}

/// <summary>
/// Result of log file type detection.
/// </summary>
public class LogFileTypeDetectionResult
{
    /// <summary>
    /// The detected log format.
    /// </summary>
    public DroneLogFormat Format { get; set; } = DroneLogFormat.Unknown;
    
    /// <summary>
    /// Confidence level of the detection (0-100).
    /// </summary>
    public int Confidence { get; set; }
    
    /// <summary>
    /// The file extension.
    /// </summary>
    public string Extension { get; set; } = string.Empty;
    
    /// <summary>
    /// Additional detection notes.
    /// </summary>
    public string Notes { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether the format is supported for parsing.
    /// </summary>
    public bool IsSupported => Format != DroneLogFormat.Unknown;
}
