using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Enums;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Detects the type of drone log file by examining file extension and content.
/// Supports ArduPilot DataFlash, MAVLink telemetry, PX4 ULog, CSV exports, and DJI formats.
/// </summary>
public class LogFileTypeDetector
{
    private readonly ILogger<LogFileTypeDetector>? _logger;

    // DataFlash binary log magic bytes
    private const byte DATAFLASH_HEAD1 = 0xA3;
    private const byte DATAFLASH_HEAD2 = 0x95;

    // ULog magic bytes "ULog"
    private static readonly byte[] ULOG_MAGIC = { 0x55, 0x4C, 0x6F, 0x67, 0x01, 0x12, 0x35 };

    // MAVLink packet start bytes (version 1.0 and 2.0)
    private const byte MAVLINK_V1_STX = 0xFE;
    private const byte MAVLINK_V2_STX = 0xFD;

    public LogFileTypeDetector(ILogger<LogFileTypeDetector>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Detects the log file type from the given file path.
    /// Examines both file extension and content for accurate detection.
    /// </summary>
    /// <param name="filePath">Path to the log file.</param>
    /// <returns>Detection result with format and confidence.</returns>
    public LogFileTypeDetectionResult DetectFileType(string filePath)
    {
        var result = new LogFileTypeDetectionResult
        {
            Extension = Path.GetExtension(filePath).ToLowerInvariant()
        };

        if (!File.Exists(filePath))
        {
            result.Notes = "File not found";
            return result;
        }

        try
        {
            // First, check by extension
            var extensionFormat = DetectByExtension(result.Extension);
            
            // Then verify by content
            var contentFormat = DetectByContent(filePath);

            // Combine results
            if (contentFormat.Format != DroneLogFormat.Unknown)
            {
                result.Format = contentFormat.Format;
                result.Confidence = contentFormat.Confidence;
                result.Notes = contentFormat.Notes;
            }
            else if (extensionFormat.Format != DroneLogFormat.Unknown)
            {
                result.Format = extensionFormat.Format;
                result.Confidence = extensionFormat.Confidence;
                result.Notes = "Detected by extension only - content verification failed";
            }

            _logger?.LogInformation("Detected log format: {Format} (confidence: {Confidence}%) for {File}",
                result.Format, result.Confidence, Path.GetFileName(filePath));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error detecting file type for {File}", filePath);
            result.Notes = $"Detection error: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Detects log format based on file extension.
    /// </summary>
    private LogFileTypeDetectionResult DetectByExtension(string extension)
    {
        var result = new LogFileTypeDetectionResult { Extension = extension };

        switch (extension)
        {
            case ".bin":
                result.Format = DroneLogFormat.ArduPilotDataFlashBinary;
                result.Confidence = 70;
                result.Notes = "Binary DataFlash log (extension-based)";
                break;
            case ".log":
                result.Format = DroneLogFormat.ArduPilotDataFlashText;
                result.Confidence = 60;
                result.Notes = "Text DataFlash log (extension-based)";
                break;
            case ".tlog":
                result.Format = DroneLogFormat.MavLinkTelemetry;
                result.Confidence = 80;
                result.Notes = "MAVLink telemetry log (extension-based)";
                break;
            case ".ulg":
                result.Format = DroneLogFormat.PX4ULog;
                result.Confidence = 90;
                result.Notes = "PX4 ULog (extension-based)";
                break;
            case ".csv":
                result.Format = DroneLogFormat.CsvExport;
                result.Confidence = 50;
                result.Notes = "CSV export (extension-based)";
                break;
            case ".txt":
                // Could be DJI or text log
                result.Format = DroneLogFormat.DjiTxt;
                result.Confidence = 30;
                result.Notes = "May be DJI TXT or text log (extension-based)";
                break;
            default:
                result.Format = DroneLogFormat.Unknown;
                result.Confidence = 0;
                result.Notes = "Unknown extension";
                break;
        }

        return result;
    }

    /// <summary>
    /// Detects log format based on file content (magic bytes and structure).
    /// </summary>
    private LogFileTypeDetectionResult DetectByContent(string filePath)
    {
        var result = new LogFileTypeDetectionResult();

        try
        {
            using var stream = File.OpenRead(filePath);
            var header = new byte[Math.Min(1024, stream.Length)];
            var bytesRead = stream.Read(header, 0, header.Length);

            if (bytesRead < 3)
            {
                result.Notes = "File too small";
                return result;
            }

            // Check for ULog format
            if (IsULogFormat(header))
            {
                result.Format = DroneLogFormat.PX4ULog;
                result.Confidence = 100;
                result.Notes = "PX4 ULog verified by magic bytes";
                return result;
            }

            // Check for DataFlash binary format
            if (IsDataFlashBinary(header))
            {
                result.Format = DroneLogFormat.ArduPilotDataFlashBinary;
                result.Confidence = 100;
                result.Notes = "ArduPilot DataFlash binary verified by header";
                return result;
            }

            // Check for MAVLink telemetry format
            if (IsMavLinkTelemetry(header))
            {
                result.Format = DroneLogFormat.MavLinkTelemetry;
                result.Confidence = 95;
                result.Notes = "MAVLink telemetry verified by packet headers";
                return result;
            }

            // Check for text-based formats
            stream.Position = 0;
            using var reader = new StreamReader(stream);
            var firstLines = new List<string>();
            for (int i = 0; i < 10 && !reader.EndOfStream; i++)
            {
                var line = reader.ReadLine();
                if (!string.IsNullOrEmpty(line))
                    firstLines.Add(line);
            }

            // Check for DataFlash text format
            if (IsDataFlashText(firstLines))
            {
                result.Format = DroneLogFormat.ArduPilotDataFlashText;
                result.Confidence = 90;
                result.Notes = "ArduPilot DataFlash text verified by FMT messages";
                return result;
            }

            // Check for DJI format
            if (IsDjiFormat(firstLines))
            {
                result.Format = DroneLogFormat.DjiCsv;
                result.Confidence = 85;
                result.Notes = "DJI log format detected";
                return result;
            }

            // Check for generic CSV with drone data
            if (IsDroneCSV(firstLines))
            {
                result.Format = DroneLogFormat.CsvExport;
                result.Confidence = 75;
                result.Notes = "CSV export with drone data fields";
                return result;
            }
        }
        catch (Exception ex)
        {
            result.Notes = $"Content detection error: {ex.Message}";
        }

        return result;
    }

    private bool IsULogFormat(byte[] header)
    {
        if (header.Length < ULOG_MAGIC.Length)
            return false;

        for (int i = 0; i < ULOG_MAGIC.Length; i++)
        {
            if (header[i] != ULOG_MAGIC[i])
                return false;
        }
        return true;
    }

    private bool IsDataFlashBinary(byte[] header)
    {
        // Look for DataFlash header bytes in first 1KB
        for (int i = 0; i < header.Length - 1; i++)
        {
            if (header[i] == DATAFLASH_HEAD1 && header[i + 1] == DATAFLASH_HEAD2)
                return true;
        }
        return false;
    }

    private bool IsMavLinkTelemetry(byte[] header)
    {
        // Count MAVLink packet starts
        int mavlinkPackets = 0;
        for (int i = 0; i < header.Length; i++)
        {
            if (header[i] == MAVLINK_V1_STX || header[i] == MAVLINK_V2_STX)
                mavlinkPackets++;
        }
        // If we find multiple MAVLink packet starts, it's likely a tlog
        return mavlinkPackets >= 3;
    }

    private bool IsDataFlashText(List<string> lines)
    {
        // DataFlash text logs typically start with FMT messages
        foreach (var line in lines)
        {
            if (line.StartsWith("FMT,") || line.StartsWith("FMT "))
                return true;
            // Also check for common ArduPilot message types
            if (line.StartsWith("GPS,") || line.StartsWith("ATT,") || 
                line.StartsWith("IMU,") || line.StartsWith("RCIN,"))
                return true;
        }
        return false;
    }

    private bool IsDjiFormat(List<string> lines)
    {
        // DJI logs often have specific headers or field names
        foreach (var line in lines)
        {
            var lower = line.ToLowerInvariant();
            if (lower.Contains("dji") || lower.Contains("phantom") || 
                lower.Contains("mavic") || lower.Contains("inspire") ||
                lower.Contains("flightctrlinfo") || lower.Contains("osd_data"))
                return true;
        }
        return false;
    }

    private bool IsDroneCSV(List<string> lines)
    {
        if (lines.Count == 0)
            return false;

        // Check if first line looks like a CSV header with drone-related fields
        var firstLine = lines[0].ToLowerInvariant();
        var droneFields = new[] { "timestamp", "time", "lat", "lon", "alt", "roll", "pitch", "yaw", 
                                   "gps", "battery", "voltage", "mode", "throttle", "airspeed" };
        
        int matchCount = 0;
        foreach (var field in droneFields)
        {
            if (firstLine.Contains(field))
                matchCount++;
        }

        return matchCount >= 2;
    }

    /// <summary>
    /// Gets a human-readable description of the detected format.
    /// </summary>
    public static string GetFormatDescription(DroneLogFormat format)
    {
        return format switch
        {
            DroneLogFormat.ArduPilotDataFlashBinary => "ArduPilot DataFlash Binary Log (.bin)",
            DroneLogFormat.ArduPilotDataFlashText => "ArduPilot DataFlash Text Log (.log)",
            DroneLogFormat.MavLinkTelemetry => "MAVLink Telemetry Log (.tlog)",
            DroneLogFormat.PX4ULog => "PX4 ULog (.ulg)",
            DroneLogFormat.CsvExport => "CSV Export Log",
            DroneLogFormat.DjiTxt => "DJI TXT Log",
            DroneLogFormat.DjiCsv => "DJI CSV Log",
            _ => "Unknown Format"
        };
    }
}
