using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Infrastructure.Logging;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Universal event extractor that scans ALL message types in a log file
/// to detect any message that represents an event, state change, warning, error, or notification.
/// </summary>
public class UniversalEventExtractor
{
    private readonly ILogger<UniversalEventExtractor>? _logger;

    // Keywords that indicate an event-related message
    private static readonly string[] EventKeywords = 
    {
        "error", "err", "fail", "fault", "warning", "warn", "critical", "emergency",
        "notice", "info", "alert", "alarm", "status", "state", "change", "switch",
        "arm", "disarm", "mode", "failsafe", "safe", "unsafe", "limit", "exceeded",
        "lost", "loss", "recovery", "recover", "timeout", "glitch",
        "calibrat", "init", "start", "stop", "begin", "end", "complete", "abort",
        "crash", "impact", "land", "takeoff", "waypoint", "mission", "fence", "rally",
        "gps", "ekf", "imu", "compass", "baro", "motor", "battery", "low", "high",
        "voltage", "current", "temperature", "vibration", "clip", "overflow"
    };

    // Message types that are explicitly event-related
    private static readonly HashSet<string> EventMessageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        // ArduPilot event message types
        "MODE", "MODE2", "EV", "EVT", "ERR", "MSG", "STAT", "ARM", "DISARM",
        "FAILSAFE", "RCFAIL", "GPS", "GPSERR", "EKF", "EKFERR", "BAT", "BATT",
        "POWR", "CURR", "VIBE", "IMU", "MAG", "COMPASS", "BARO", "RCIN", "RCOU",
        "TERR", "LAND", "CRASH", "FENCE", "RALLY",
        
        // PX4 event-related topics
        "vehicle_status", "vehicle_command", "commander_state", "safety",
        "estimator_status", "sensor_combined", "battery_status", "failsafe_flags",
        "mission_result", "geofence_result", "rtl_time_estimate",
        
        // Common event indicators
        "EVENT", "WARN", "ERROR", "ALERT", "NOTICE", "INFO", "DEBUG"
    };

    // Thresholds for automatic event detection
    private const double VIBRATION_WARNING_THRESHOLD = 30.0;
    private const double VIBRATION_ERROR_THRESHOLD = 60.0;
    private const double GPS_HDOP_WARNING = 2.0;
    private const double BATTERY_LOW_VOLTAGE_PER_CELL = 3.5;
    private const double BATTERY_CRITICAL_VOLTAGE_PER_CELL = 3.3;
    
    // Message length limits
    private const int MAX_MESSAGE_LENGTH = 200;

    public UniversalEventExtractor(ILogger<UniversalEventExtractor>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extracts ALL events from a parsed log by scanning every message type.
    /// Returns normalized events in the standard output structure.
    /// </summary>
    public List<NormalizedEvent> ExtractAllEvents(
        DataFlashLogParser parser,
        ParsedLog parsedLog,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var events = new List<NormalizedEvent>();
        var messageTypes = parser.GetMessageTypes();
        var totalTypes = messageTypes.Count;
        var processedTypes = 0;

        _logger?.LogInformation("Scanning {Count} message types for events", totalTypes);

        foreach (var msgType in messageTypes)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var messages = parser.GetMessages(msgType);
            var extractedEvents = ExtractEventsFromMessageType(msgType, messages);
            events.AddRange(extractedEvents);

            processedTypes++;
            progress?.Report((int)((double)processedTypes / totalTypes * 100));
        }

        // Sort by timestamp
        events = events.OrderBy(e => e.Timestamp).ToList();

        // Assign sequential IDs
        for (int i = 0; i < events.Count; i++)
        {
            events[i].Id = i + 1;
        }

        _logger?.LogInformation("Extracted {Count} events from log", events.Count);
        return events;
    }

    /// <summary>
    /// Extracts events from a specific message type.
    /// </summary>
    private List<NormalizedEvent> ExtractEventsFromMessageType(string messageType, List<LogMessage> messages)
    {
        var events = new List<NormalizedEvent>();
        var upperType = messageType.ToUpperInvariant();

        // Handle known event message types with specific logic
        switch (upperType)
        {
            case "MODE":
            case "MODE2":
                events.AddRange(ExtractModeEvents(messages));
                break;
            case "EV":
            case "EVT":
            case "EVENT":
                events.AddRange(ExtractEvEvents(messages));
                break;
            case "ERR":
            case "ERROR":
                events.AddRange(ExtractErrorEvents(messages));
                break;
            case "MSG":
                events.AddRange(ExtractMsgEvents(messages));
                break;
            case "FAILSAFE":
            case "RCFAIL":
                events.AddRange(ExtractFailsafeEvents(messages));
                break;
            case "GPS":
                events.AddRange(ExtractGpsEvents(messages));
                break;
            case "VIBE":
                events.AddRange(ExtractVibrationEvents(messages));
                break;
            case "BAT":
            case "BATT":
            case "CURR":
            case "POWR":
                events.AddRange(ExtractBatteryEvents(messages));
                break;
            case "STAT":
                events.AddRange(ExtractStatEvents(messages));
                break;
            default:
                // For unknown message types, scan for event-like content
                if (EventMessageTypes.Contains(upperType) || 
                    EventKeywords.Any(k => upperType.Contains(k.ToUpperInvariant())))
                {
                    events.AddRange(ExtractGenericEvents(messageType, messages));
                }
                else
                {
                    // Sample scan - check first few messages for event-like content
                    var sample = messages.Take(100).ToList();
                    var sampleEvents = ExtractGenericEvents(messageType, sample);
                    if (sampleEvents.Count > 0)
                    {
                        // This message type contains events, process all messages
                        events.AddRange(ExtractGenericEvents(messageType, messages));
                    }
                }
                break;
        }

        return events;
    }

    private List<NormalizedEvent> ExtractModeEvents(List<LogMessage> messages)
    {
        var events = new List<NormalizedEvent>();
        string? lastMode = null;

        foreach (var msg in messages)
        {
            var modeName = msg.GetStringField("Mode") ?? 
                           msg.GetStringField("ModeNum")?.ToString() ?? 
                           msg.GetStringField("Name") ?? "Unknown";

            if (modeName != lastMode)
            {
                events.Add(new NormalizedEvent
                {
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Severity = "Info",
                    EventType = "ModeChange",
                    Source = "Navigator",
                    Message = $"Flight mode changed to {modeName}",
                    Details = $"Mode: {modeName}"
                });
                lastMode = modeName;
            }
        }

        return events;
    }

    private List<NormalizedEvent> ExtractEvEvents(List<LogMessage> messages)
    {
        var events = new List<NormalizedEvent>();

        // ArduPilot event ID mappings
        var eventIdMap = new Dictionary<int, (string EventType, string Severity, string Description)>
        {
            { 10, ("Armed", "Notice", "Vehicle armed and ready for flight") },
            { 11, ("Disarmed", "Notice", "Vehicle disarmed") },
            { 15, ("BatteryFailsafe", "Critical", "Battery failsafe triggered") },
            { 17, ("GPSFailsafe", "Error", "GPS failsafe triggered") },
            { 18, ("GCSFailsafe", "Error", "GCS failsafe triggered") },
            { 28, ("RadioFailsafe", "Error", "Radio failsafe triggered") },
            { 29, ("BatteryFailsafe2", "Critical", "Battery failsafe 2 triggered") },
            { 36, ("EKFFailsafe", "Critical", "EKF failsafe triggered") },
            { 37, ("EKFFailsafeCleared", "Notice", "EKF failsafe cleared") },
            { 40, ("Takeoff", "Info", "Takeoff detected") },
            { 41, ("Land", "Info", "Landing detected") },
            { 43, ("NotLandedCheck", "Warning", "Not landed check triggered") },
            { 57, ("Crash", "Emergency", "Crash detected") }
        };

        foreach (var msg in messages)
        {
            var evId = (int)(msg.GetField<double>("Id") ?? 0);

            if (eventIdMap.TryGetValue(evId, out var eventInfo))
            {
                events.Add(new NormalizedEvent
                {
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Severity = eventInfo.Severity,
                    EventType = eventInfo.EventType,
                    Source = "Autopilot",
                    Message = eventInfo.Description,
                    Details = $"Event ID: {evId}"
                });
            }
            else
            {
                // Unknown event ID - still record it
                events.Add(new NormalizedEvent
                {
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Severity = "Info",
                    EventType = "Event",
                    Source = "Autopilot",
                    Message = $"Event ID {evId}",
                    Details = $"Event ID: {evId}"
                });
            }
        }

        return events;
    }

    private List<NormalizedEvent> ExtractErrorEvents(List<LogMessage> messages)
    {
        var events = new List<NormalizedEvent>();

        // ArduPilot error subsystem mappings
        var subsystemMap = new Dictionary<int, string>
        {
            { 1, "Main" }, { 2, "Radio" }, { 3, "Compass" }, { 4, "OpticalFlow" },
            { 5, "Failsafe_Radio" }, { 6, "Failsafe_Battery" }, { 7, "Failsafe_GPS" },
            { 8, "Failsafe_GCS" }, { 9, "Failsafe_Fence" }, { 10, "Flight Mode" },
            { 11, "GPS" }, { 12, "CrashCheck" }, { 13, "Flip" }, { 14, "AutoTune" },
            { 15, "Parachute" }, { 16, "EKF_Check" }, { 17, "Failsafe_EKF" },
            { 18, "Baro" }, { 19, "CPU" }, { 20, "Failsafe_ADSB" }, { 21, "Terrain" },
            { 22, "Navigation" }, { 23, "Failsafe_Terrain" }, { 24, "EKF_Primary" },
            { 25, "Failsafe_Leak" }, { 26, "Logger" }, { 27, "Scripting" }
        };

        foreach (var msg in messages)
        {
            var subsys = (int)(msg.GetField<double>("Subsys") ?? 0);
            var errCode = (int)(msg.GetField<double>("ECode") ?? msg.GetField<double>("ErrCode") ?? 0);
            var subsysName = subsystemMap.GetValueOrDefault(subsys, $"Subsystem_{subsys}");

            events.Add(new NormalizedEvent
            {
                Timestamp = msg.Timestamp.TotalSeconds,
                Severity = "Error",
                EventType = "Error",
                Source = subsysName,
                Message = $"Error in {subsysName}: Code {errCode}",
                Details = $"Subsystem: {subsys}, Error Code: {errCode}"
            });
        }

        return events;
    }

    private List<NormalizedEvent> ExtractMsgEvents(List<LogMessage> messages)
    {
        var events = new List<NormalizedEvent>();

        foreach (var msg in messages)
        {
            var text = msg.GetStringField("Message") ?? 
                       msg.GetStringField("Text") ?? 
                       msg.GetStringField("Msg") ?? "";

            if (string.IsNullOrWhiteSpace(text))
                continue;

            // Determine severity from message content
            var (severity, eventType) = ClassifyMessage(text);

            events.Add(new NormalizedEvent
            {
                Timestamp = msg.Timestamp.TotalSeconds,
                Severity = severity,
                EventType = eventType,
                Source = "System",
                Message = text,
                Details = null
            });
        }

        return events;
    }

    private List<NormalizedEvent> ExtractFailsafeEvents(List<LogMessage> messages)
    {
        var events = new List<NormalizedEvent>();

        foreach (var msg in messages)
        {
            var fsType = msg.GetStringField("Type") ?? 
                         msg.GetStringField("Reason") ?? "Unknown";
            var action = msg.GetStringField("Action") ?? "";

            events.Add(new NormalizedEvent
            {
                Timestamp = msg.Timestamp.TotalSeconds,
                Severity = "Error",
                EventType = "Failsafe",
                Source = "Safety",
                Message = $"Failsafe triggered: {fsType}",
                Details = !string.IsNullOrEmpty(action) ? $"Action: {action}" : null
            });
        }

        return events;
    }

    private List<NormalizedEvent> ExtractGpsEvents(List<LogMessage> messages)
    {
        var events = new List<NormalizedEvent>();
        int? lastStatus = null;
        int hdopWarningCount = 0;
        const int maxHdopWarnings = 5;

        foreach (var msg in messages)
        {
            var status = (int)(msg.GetField<double>("Status") ?? 0);
            var numSats = (int)(msg.GetField<double>("NSats") ?? msg.GetField<double>("nSat") ?? 0);
            var hdop = msg.GetField<double>("HDop") ?? msg.GetField<double>("HDOP") ?? 0;

            // GPS status: 0=NoGPS, 1=NoFix, 2=2D, 3=3D, 4=DGPS, 5=RTK Float, 6=RTK Fix
            if (status < 3 && lastStatus >= 3)
            {
                events.Add(new NormalizedEvent
                {
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Severity = "Error",
                    EventType = "GPSLoss",
                    Source = "GPS",
                    Message = $"GPS fix lost (Status: {status}, Sats: {numSats})",
                    Details = $"Status: {status}, Satellites: {numSats}, HDOP: {hdop:F1}"
                });
            }
            else if (status >= 3 && lastStatus < 3 && lastStatus.HasValue)
            {
                events.Add(new NormalizedEvent
                {
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Severity = "Notice",
                    EventType = "GPSRecovery",
                    Source = "GPS",
                    Message = $"GPS fix recovered (Status: {status}, Sats: {numSats})",
                    Details = $"Status: {status}, Satellites: {numSats}, HDOP: {hdop:F1}"
                });
            }

            if (hdop > GPS_HDOP_WARNING && hdopWarningCount < maxHdopWarnings)
            {
                events.Add(new NormalizedEvent
                {
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Severity = "Warning",
                    EventType = "GPSQuality",
                    Source = "GPS",
                    Message = $"GPS HDOP high: {hdop:F1}",
                    Details = $"HDOP: {hdop:F1}, Satellites: {numSats}"
                });
                hdopWarningCount++;
            }

            lastStatus = status;
        }

        return events;
    }

    private List<NormalizedEvent> ExtractVibrationEvents(List<LogMessage> messages)
    {
        var events = new List<NormalizedEvent>();
        int vibeWarningCount = 0;
        int vibeErrorCount = 0;
        const int maxEventsPerType = 5;

        foreach (var msg in messages)
        {
            var vibeX = Math.Abs(msg.GetField<double>("VibeX") ?? 0);
            var vibeY = Math.Abs(msg.GetField<double>("VibeY") ?? 0);
            var vibeZ = Math.Abs(msg.GetField<double>("VibeZ") ?? 0);
            var clip0 = msg.GetField<double>("Clip0") ?? 0;
            var clip1 = msg.GetField<double>("Clip1") ?? 0;
            var clip2 = msg.GetField<double>("Clip2") ?? 0;

            var maxVibe = Math.Max(vibeX, Math.Max(vibeY, vibeZ));

            if (maxVibe > VIBRATION_ERROR_THRESHOLD && vibeErrorCount < maxEventsPerType)
            {
                events.Add(new NormalizedEvent
                {
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Severity = "Error",
                    EventType = "Vibration",
                    Source = "IMU",
                    Message = $"High vibration: X={vibeX:F1}, Y={vibeY:F1}, Z={vibeZ:F1}",
                    Details = $"X: {vibeX:F1}, Y: {vibeY:F1}, Z: {vibeZ:F1} m/s²"
                });
                vibeErrorCount++;
            }
            else if (maxVibe > VIBRATION_WARNING_THRESHOLD && vibeWarningCount < maxEventsPerType)
            {
                events.Add(new NormalizedEvent
                {
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Severity = "Warning",
                    EventType = "Vibration",
                    Source = "IMU",
                    Message = $"Elevated vibration: X={vibeX:F1}, Y={vibeY:F1}, Z={vibeZ:F1}",
                    Details = $"X: {vibeX:F1}, Y: {vibeY:F1}, Z: {vibeZ:F1} m/s²"
                });
                vibeWarningCount++;
            }

            var totalClips = clip0 + clip1 + clip2;
            if (totalClips > 100)
            {
                events.Add(new NormalizedEvent
                {
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Severity = "Warning",
                    EventType = "AccelClipping",
                    Source = "IMU",
                    Message = $"Accelerometer clipping: {totalClips:F0} clips",
                    Details = $"Clip0: {clip0}, Clip1: {clip1}, Clip2: {clip2}"
                });
            }
        }

        return events;
    }

    private List<NormalizedEvent> ExtractBatteryEvents(List<LogMessage> messages)
    {
        var events = new List<NormalizedEvent>();
        bool lowVoltageReported = false;
        bool criticalVoltageReported = false;

        foreach (var msg in messages)
        {
            var voltage = msg.GetField<double>("Volt") ?? msg.GetField<double>("Voltage") ?? 0;
            var current = msg.GetField<double>("Curr") ?? msg.GetField<double>("Current") ?? 0;
            var cellCount = EstimateCellCount(voltage);

            if (cellCount > 0)
            {
                var voltPerCell = voltage / cellCount;

                if (voltPerCell < BATTERY_CRITICAL_VOLTAGE_PER_CELL && !criticalVoltageReported)
                {
                    events.Add(new NormalizedEvent
                    {
                        Timestamp = msg.Timestamp.TotalSeconds,
                        Severity = "Critical",
                        EventType = "BatteryCritical",
                        Source = "Power",
                        Message = $"Battery critical: {voltage:F1}V ({voltPerCell:F2}V/cell)",
                        Details = $"Voltage: {voltage:F1}V, Current: {current:F1}A"
                    });
                    criticalVoltageReported = true;
                }
                else if (voltPerCell < BATTERY_LOW_VOLTAGE_PER_CELL && !lowVoltageReported)
                {
                    events.Add(new NormalizedEvent
                    {
                        Timestamp = msg.Timestamp.TotalSeconds,
                        Severity = "Warning",
                        EventType = "BatteryLow",
                        Source = "Power",
                        Message = $"Battery low: {voltage:F1}V ({voltPerCell:F2}V/cell)",
                        Details = $"Voltage: {voltage:F1}V, Current: {current:F1}A"
                    });
                    lowVoltageReported = true;
                }
            }
        }

        return events;
    }

    private List<NormalizedEvent> ExtractStatEvents(List<LogMessage> messages)
    {
        var events = new List<NormalizedEvent>();

        foreach (var msg in messages)
        {
            // STAT messages contain various status information
            var armed = msg.GetField<double>("Armed") ?? msg.GetField<double>("isArmed");
            
            if (armed.HasValue)
            {
                var isArmed = armed.Value > 0;
                events.Add(new NormalizedEvent
                {
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Severity = "Notice",
                    EventType = isArmed ? "Armed" : "Disarmed",
                    Source = "Autopilot",
                    Message = isArmed ? "Vehicle armed" : "Vehicle disarmed",
                    Details = null
                });
            }
        }

        return events;
    }

    private List<NormalizedEvent> ExtractGenericEvents(string messageType, List<LogMessage> messages)
    {
        var events = new List<NormalizedEvent>();

        foreach (var msg in messages)
        {
            // Check all string fields for event-like content
            foreach (var field in msg.Fields)
            {
                var valueStr = field.Value?.ToString() ?? "";
                if (string.IsNullOrWhiteSpace(valueStr))
                    continue;

                // Check if this field contains event-like content
                var hasEventContent = EventKeywords.Any(k => 
                    valueStr.Contains(k, StringComparison.OrdinalIgnoreCase));

                if (hasEventContent)
                {
                    var (severity, eventType) = ClassifyMessage(valueStr);
                    
                    events.Add(new NormalizedEvent
                    {
                        Timestamp = msg.Timestamp.TotalSeconds,
                        Severity = severity,
                        EventType = eventType,
                        Source = messageType,
                        Message = valueStr.Length > MAX_MESSAGE_LENGTH ? valueStr.Substring(0, MAX_MESSAGE_LENGTH) + "..." : valueStr,
                        Details = $"Field: {field.Key}"
                    });
                    break; // Only one event per message
                }
            }
        }

        return events;
    }

    /// <summary>
    /// Classifies a message text to determine severity and event type.
    /// </summary>
    private (string Severity, string EventType) ClassifyMessage(string text)
    {
        var lower = text.ToLowerInvariant();

        // Emergency/Critical
        if (lower.Contains("emergency") || lower.Contains("crash") || 
            lower.Contains("critical") || lower.Contains("fatal"))
            return ("Emergency", "Critical");

        // Error
        if (lower.Contains("error") || lower.Contains("fail") || 
            lower.Contains("fault") || lower.Contains("invalid"))
            return ("Error", "Error");

        // Warning
        if (lower.Contains("warning") || lower.Contains("warn") || 
            lower.Contains("low") || lower.Contains("limit") ||
            lower.Contains("exceeded") || lower.Contains("lost"))
            return ("Warning", "Warning");

        // Notice
        if (lower.Contains("arm") || lower.Contains("disarm") || 
            lower.Contains("mode") || lower.Contains("takeoff") ||
            lower.Contains("land") || lower.Contains("complete"))
            return ("Notice", "StateChange");

        // Info
        return ("Info", "Info");
    }

    private static int EstimateCellCount(double voltage)
    {
        if (voltage < 5) return 1;       // 1S
        if (voltage < 9) return 2;       // 2S
        if (voltage < 13) return 3;      // 3S
        if (voltage < 17) return 4;      // 4S
        if (voltage < 22) return 5;      // 5S
        if (voltage < 26) return 6;      // 6S
        if (voltage < 30) return 7;      // 7S
        if (voltage < 35) return 8;      // 8S
        return 0;
    }
}

/// <summary>
/// Normalized event structure for UI display.
/// This is the standard output format for all extracted events.
/// </summary>
public class NormalizedEvent
{
    /// <summary>
    /// Unique event identifier.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Timestamp in seconds from log start.
    /// </summary>
    public double Timestamp { get; set; }

    /// <summary>
    /// Human-readable timestamp display.
    /// </summary>
    public string TimestampDisplay => TimeSpan.FromSeconds(Timestamp).ToString(@"hh\:mm\:ss\.fff");

    /// <summary>
    /// Event severity: Emergency, Critical, Error, Warning, Notice, Info, Debug.
    /// </summary>
    public string Severity { get; set; } = "Info";

    /// <summary>
    /// Type/category of the event.
    /// </summary>
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Source subsystem or message type.
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable event message.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Additional details (optional).
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Converts to LogEvent for compatibility with existing UI.
    /// </summary>
    public LogEvent ToLogEvent()
    {
        var severityEnum = Severity.ToLowerInvariant() switch
        {
            "emergency" => LogEventSeverity.Emergency,
            "critical" => LogEventSeverity.Critical,
            "error" => LogEventSeverity.Error,
            "warning" => LogEventSeverity.Warning,
            "notice" => LogEventSeverity.Notice,
            "info" => LogEventSeverity.Info,
            "debug" => LogEventSeverity.Debug,
            _ => LogEventSeverity.Info
        };

        var typeEnum = EventType.ToLowerInvariant() switch
        {
            "modechange" => LogEventType.ModeChange,
            "armed" => LogEventType.Arming,
            "disarmed" => LogEventType.Disarming,
            "failsafe" => LogEventType.Failsafe,
            "batteryfailsafe" => LogEventType.BatteryFailsafe,
            "batterycritical" => LogEventType.BatteryCritical,
            "batterylow" => LogEventType.BatteryLow,
            "gpsloss" => LogEventType.GpsLoss,
            "gpsrecovery" => LogEventType.GpsRecovery,
            "gpsquality" or "gpsfailsafe" => LogEventType.GpsGlitch,
            "vibration" => LogEventType.Vibration,
            "accelclipping" => LogEventType.Clipping,
            "crash" or "critical" => LogEventType.Crash,
            "takeoff" => LogEventType.Takeoff,
            "land" or "landing" => LogEventType.Landing,
            "ekffailsafe" or "ekfwarning" => LogEventType.EkfWarning,
            "radiofailsafe" => LogEventType.RcLoss,
            _ => LogEventType.Custom
        };

        return new LogEvent
        {
            Id = Id,
            Timestamp = Timestamp,
            Severity = severityEnum,
            Type = typeEnum,
            Title = EventType,
            Description = Message,
            Source = Source,
            Details = Details
        };
    }
}
