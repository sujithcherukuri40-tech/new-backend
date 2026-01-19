using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Infrastructure.Logging;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Detects flight events from log data.
/// Analyzes logs for arming, failsafes, EKF issues, GPS problems, vibration warnings, etc.
/// Uses a universal event extractor to scan ALL message types for any event-like content.
/// </summary>
public class LogEventDetector : ILogEventDetector
{
    private readonly ILogger<LogEventDetector> _logger;
    private readonly UniversalEventExtractor? _universalExtractor;
    private DataFlashLogParser? _parser;
    private ParsedLog? _parsedLog;
    private List<LogEvent>? _cachedEvents;
    
    // GPS data cache for location lookups
    private List<(double Timestamp, double Lat, double Lng, double Alt)>? _gpsCache;

    // Thresholds for event detection
    private const double VIBRATION_WARNING_THRESHOLD = 30.0;  // m/s²
    private const double VIBRATION_ERROR_THRESHOLD = 60.0;    // m/s²
    private const double CLIPPING_THRESHOLD = 100;            // clip count
    private const double BATTERY_LOW_VOLTAGE = 3.5;           // V per cell
    private const double BATTERY_CRITICAL_VOLTAGE = 3.3;      // V per cell
    private const double GPS_HDOP_WARNING = 2.0;
    private const double GPS_HDOP_ERROR = 4.0;
    private const int GPS_MIN_SATS_WARNING = 6;
    private const double EKF_VARIANCE_WARNING = 0.5;
    private const double EKF_VARIANCE_ERROR = 0.8;

    /// <summary>
    /// Whether to use the universal event extractor for comprehensive event detection.
    /// </summary>
    public bool UseUniversalExtractor { get; set; } = true;

    public LogEventDetector(ILogger<LogEventDetector> logger)
    {
        _logger = logger;
        _universalExtractor = new UniversalEventExtractor(null);
    }

    /// <summary>
    /// Sets the parsed log data for event detection.
    /// </summary>
    public void SetLogData(DataFlashLogParser parser, ParsedLog parsedLog)
    {
        _parser = parser;
        _parsedLog = parsedLog;
        _cachedEvents = null;
        _gpsCache = null;
    }
    
    /// <summary>
    /// Builds GPS cache for location lookups.
    /// </summary>
    private void BuildGpsCache()
    {
        if (_gpsCache != null || _parser == null) return;
        
        _gpsCache = new List<(double, double, double, double)>();
        
        var gpsMessages = _parser.GetMessages("GPS");
        foreach (var msg in gpsMessages)
        {
            var lat = msg.GetField<double>("Lat") ?? 0;
            var lng = msg.GetField<double>("Lng") ?? 0;
            var alt = msg.GetField<double>("Alt") ?? 0;
            
            // Skip invalid coordinates
            if (Math.Abs(lat) < 0.001 && Math.Abs(lng) < 0.001) continue;
            
            _gpsCache.Add((msg.Timestamp.TotalSeconds, lat, lng, alt));
        }
    }
    
    /// <summary>
    /// Gets GPS location at a specific timestamp.
    /// </summary>
    private (double? Lat, double? Lng, double? Alt) GetLocationAtTime(double timestamp)
    {
        BuildGpsCache();
        
        if (_gpsCache == null || _gpsCache.Count == 0)
            return (null, null, null);
        
        // Find closest GPS point
        var closest = _gpsCache.MinBy(g => Math.Abs(g.Timestamp - timestamp));
        
        // Only return if within 5 seconds
        if (Math.Abs(closest.Timestamp - timestamp) <= 5)
            return (closest.Lat, closest.Lng, closest.Alt);
        
        return (null, null, null);
    }

    public async Task<List<LogEvent>> DetectEventsAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (_cachedEvents != null)
        {
            return _cachedEvents;
        }

        if (_parser == null || _parsedLog == null)
        {
            return new List<LogEvent>();
        }

        return await Task.Run(() =>
        {
            var events = new List<LogEvent>();
            var eventId = 1;

            try
            {
                progress?.Report(0);
                
                // Build GPS cache first
                BuildGpsCache();
                progress?.Report(5);

                // Use universal extractor for comprehensive event detection
                if (UseUniversalExtractor && _universalExtractor != null)
                {
                    _logger.LogInformation("Using universal event extractor for comprehensive detection");
                    var universalProgress = new Progress<int>(p => progress?.Report(5 + (int)(p * 0.85)));
                    var normalizedEvents = _universalExtractor.ExtractAllEvents(_parser, _parsedLog, universalProgress, cancellationToken);
                    
                    // Convert normalized events to LogEvent
                    foreach (var ne in normalizedEvents)
                    {
                        var logEvent = ne.ToLogEvent();
                        logEvent.Id = eventId++;
                        events.Add(logEvent);
                    }
                    
                    _logger.LogInformation("Universal extractor found {Count} events", events.Count);
                }
                else
                {
                    // Fallback to original detection methods
                    _logger.LogInformation("Using legacy event detection methods");

                    // Detect mode changes
                    var modeEvents = DetectModeChanges(ref eventId);
                    events.AddRange(modeEvents);
                    progress?.Report(15);

                    if (cancellationToken.IsCancellationRequested) return events;

                    // Detect arming/disarming
                    var armingEvents = DetectArmingEvents(ref eventId);
                    events.AddRange(armingEvents);
                    progress?.Report(25);

                    if (cancellationToken.IsCancellationRequested) return events;

                    // Detect failsafes
                    var failsafeEvents = DetectFailsafeEvents(ref eventId);
                    events.AddRange(failsafeEvents);
                    progress?.Report(35);

                    if (cancellationToken.IsCancellationRequested) return events;

                    // Detect EKF issues
                    var ekfEvents = DetectEkfEvents(ref eventId);
                    events.AddRange(ekfEvents);
                    progress?.Report(45);

                    if (cancellationToken.IsCancellationRequested) return events;

                    // Detect GPS issues
                    var gpsEvents = DetectGpsEvents(ref eventId);
                    events.AddRange(gpsEvents);
                    progress?.Report(55);

                    if (cancellationToken.IsCancellationRequested) return events;

                    // Detect vibration issues
                    var vibeEvents = DetectVibrationEvents(ref eventId);
                    events.AddRange(vibeEvents);
                    progress?.Report(65);

                    if (cancellationToken.IsCancellationRequested) return events;

                    // Detect battery issues
                    var batteryEvents = DetectBatteryEvents(ref eventId);
                    events.AddRange(batteryEvents);
                    progress?.Report(75);

                    if (cancellationToken.IsCancellationRequested) return events;

                    // Detect RC issues
                    var rcEvents = DetectRcEvents(ref eventId);
                    events.AddRange(rcEvents);
                    progress?.Report(85);

                    if (cancellationToken.IsCancellationRequested) return events;

                    // Detect crash/impact
                    var crashEvents = DetectCrashEvents(ref eventId);
                    events.AddRange(crashEvents);
                }

                progress?.Report(95);

                // Sort by timestamp
                events = events.OrderBy(e => e.Timestamp).ToList();
                
                // Renumber and add location data
                for (int i = 0; i < events.Count; i++)
                {
                    events[i].Id = i + 1;
                    
                    // Add location if not already set
                    if (!events[i].HasLocation)
                    {
                        var loc = GetLocationAtTime(events[i].Timestamp);
                        events[i].Latitude = loc.Lat;
                        events[i].Longitude = loc.Lng;
                        events[i].Altitude = loc.Alt;
                    }
                }

                _cachedEvents = events;
                progress?.Report(100);

                _logger.LogInformation("Detected {Count} events in log", events.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting events");
            }

            return events;
        }, cancellationToken);
    }

    private List<LogEvent> DetectModeChanges(ref int eventId)
    {
        var events = new List<LogEvent>();

        var modeMessages = _parser!.GetMessages("MODE");
        if (modeMessages.Count == 0)
        {
            // Try MSG for mode changes
            var msgMessages = _parser.GetMessages("MSG");
            foreach (var msg in msgMessages)
            {
                var text = msg.GetStringField("Message") ?? "";
                if (text.Contains("Mode", StringComparison.OrdinalIgnoreCase))
                {
                    events.Add(new LogEvent
                    {
                        Id = eventId++,
                        Timestamp = msg.Timestamp.TotalSeconds,
                        Type = LogEventType.ModeChange,
                        Severity = LogEventSeverity.Info,
                        Title = "Mode Change",
                        Description = text,
                        Source = "Navigator",
                        Subsystem = "Flight Mode",
                        ComponentId = 1,
                        RawMessage = text
                    });
                }
            }
            return events;
        }

        string? lastMode = null;
        foreach (var msg in modeMessages)
        {
            var modeName = msg.GetStringField("Mode") ?? msg.GetStringField("ModeNum")?.ToString() ?? "Unknown";
            
            if (modeName != lastMode)
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.ModeChange,
                    Severity = LogEventSeverity.Info,
                    Title = "Mode Change",
                    Description = $"Flight mode changed to {modeName}",
                    Source = "Navigator",
                    Subsystem = "Flight Mode",
                    ComponentId = 1,
                    Data = { ["Mode"] = modeName },
                    RawMessage = $"MODE: {modeName}"
                });
                lastMode = modeName;
            }
        }

        return events;
    }

    private List<LogEvent> DetectArmingEvents(ref int eventId)
    {
        var events = new List<LogEvent>();

        // Check EV (Event) messages for arming
        var evMessages = _parser!.GetMessages("EV");
        foreach (var msg in evMessages)
        {
            var evId = msg.GetField<double>("Id") ?? 0;
            
            // ArduPilot event IDs: 10 = Armed, 11 = Disarmed
            if ((int)evId == 10)
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.Arming,
                    Severity = LogEventSeverity.Notice,
                    Title = "Armed",
                    Description = "Vehicle armed and ready for flight",
                    Source = "Autopilot",
                    Subsystem = "Arming",
                    ComponentId = 1,
                    EventId = 10,
                    RawMessage = "EV: Armed (ID=10)"
                });
            }
            else if ((int)evId == 11)
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.Disarming,
                    Severity = LogEventSeverity.Notice,
                    Title = "Disarmed",
                    Description = "Vehicle disarmed",
                    Source = "Autopilot",
                    Subsystem = "Arming",
                    ComponentId = 1,
                    EventId = 11,
                    RawMessage = "EV: Disarmed (ID=11)"
                });
            }
        }

        // Also check MSG for arming text
        var msgMessages = _parser.GetMessages("MSG");
        foreach (var msg in msgMessages)
        {
            var text = msg.GetStringField("Message") ?? "";
            if (text.Contains("Arming", StringComparison.OrdinalIgnoreCase) && 
                !text.Contains("Disarm", StringComparison.OrdinalIgnoreCase))
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.Arming,
                    Severity = LogEventSeverity.Notice,
                    Title = "Armed",
                    Description = text,
                    Source = "Autopilot",
                    Subsystem = "Arming",
                    ComponentId = 1,
                    RawMessage = text
                });
            }
            else if (text.Contains("Disarm", StringComparison.OrdinalIgnoreCase))
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.Disarming,
                    Severity = LogEventSeverity.Notice,
                    Title = "Disarmed",
                    Description = text,
                    Source = "Autopilot",
                    Subsystem = "Arming",
                    ComponentId = 1,
                    RawMessage = text
                });
            }
        }

        return events;
    }

    private List<LogEvent> DetectFailsafeEvents(ref int eventId)
    {
        var events = new List<LogEvent>();

        // Check EV messages for failsafe events
        var evMessages = _parser!.GetMessages("EV");
        foreach (var msg in evMessages)
        {
            var evId = (int)(msg.GetField<double>("Id") ?? 0);
            
            // ArduPilot failsafe event IDs
            var failsafeEvents = new Dictionary<int, (LogEventType Type, string Title, string Source, LogEventSeverity Severity)>
            {
                [15] = (LogEventType.BatteryFailsafe, "Battery Failsafe", "Power", LogEventSeverity.Critical),
                [17] = (LogEventType.Failsafe, "GPS Failsafe", "GPS", LogEventSeverity.Error),
                [28] = (LogEventType.RcLoss, "Radio Failsafe", "RC Input", LogEventSeverity.Error),
                [29] = (LogEventType.BatteryFailsafe, "Battery Failsafe", "Power", LogEventSeverity.Critical),
                [36] = (LogEventType.Failsafe, "EKF Failsafe", "EKF/AHRS", LogEventSeverity.Critical),
                [37] = (LogEventType.Failsafe, "EKF Failsafe Cleared", "EKF/AHRS", LogEventSeverity.Notice)
            };

            if (failsafeEvents.TryGetValue(evId, out var fsInfo))
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = fsInfo.Type,
                    Severity = fsInfo.Severity,
                    Title = fsInfo.Title,
                    Description = $"Failsafe triggered (Event ID: {evId})",
                    Source = fsInfo.Source,
                    Subsystem = "Failsafe",
                    ComponentId = 1,
                    EventId = evId,
                    RawMessage = $"EV: {fsInfo.Title} (ID={evId})"
                });
            }
        }

        // Check MSG for failsafe text
        var msgMessages = _parser.GetMessages("MSG");
        foreach (var msg in msgMessages)
        {
            var text = msg.GetStringField("Message") ?? "";
            if (text.Contains("failsafe", StringComparison.OrdinalIgnoreCase))
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.Failsafe,
                    Severity = LogEventSeverity.Error,
                    Title = "Failsafe",
                    Description = text,
                    Source = "Autopilot",
                    Subsystem = "Failsafe",
                    ComponentId = 1,
                    RawMessage = text
                });
            }
        }

        return events;
    }

    private List<LogEvent> DetectEkfEvents(ref int eventId)
    {
        var events = new List<LogEvent>();
        
        // Limit events to prevent too many similar events
        int ekfWarningCount = 0;
        int ekfErrorCount = 0;
        const int maxEventsPerType = 10;

        // Check NKF messages for EKF status
        var nkfMessages = _parser!.GetMessages("NKF4");
        if (nkfMessages.Count == 0)
            nkfMessages = _parser.GetMessages("XKF4");

        foreach (var msg in nkfMessages)
        {
            var sqErr = msg.GetField<double>("SV") ?? 0;
            
            if (sqErr > EKF_VARIANCE_ERROR && ekfErrorCount < maxEventsPerType)
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.EkfError,
                    Severity = LogEventSeverity.Error,
                    Title = "EKF Error",
                    Description = $"EKF variance too high: {sqErr:F2}",
                    Source = "EKF/AHRS",
                    Subsystem = "State Estimation",
                    ComponentId = 1,
                    Data = { ["Variance"] = sqErr },
                    RawMessage = $"NKF4: SV={sqErr:F2}"
                });
                ekfErrorCount++;
            }
            else if (sqErr > EKF_VARIANCE_WARNING && ekfWarningCount < maxEventsPerType)
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.EkfWarning,
                    Severity = LogEventSeverity.Warning,
                    Title = "EKF Warning",
                    Description = $"EKF variance elevated: {sqErr:F2}",
                    Source = "EKF/AHRS",
                    Subsystem = "State Estimation",
                    ComponentId = 1,
                    Data = { ["Variance"] = sqErr },
                    RawMessage = $"NKF4: SV={sqErr:F2}"
                });
                ekfWarningCount++;
            }
        }

        return events;
    }

    private List<LogEvent> DetectGpsEvents(ref int eventId)
    {
        var events = new List<LogEvent>();

        var gpsMessages = _parser!.GetMessages("GPS");
        int? lastStatus = null;
        int hdopWarningCount = 0;
        const int maxHdopWarnings = 5;

        foreach (var msg in gpsMessages)
        {
            var status = (int)(msg.GetField<double>("Status") ?? 0);
            var numSats = (int)(msg.GetField<double>("NSats") ?? msg.GetField<double>("nSat") ?? 0);
            var hdop = msg.GetField<double>("HDop") ?? msg.GetField<double>("HDOP") ?? 0;
            var lat = msg.GetField<double>("Lat") ?? 0;
            var lng = msg.GetField<double>("Lng") ?? 0;
            var alt = msg.GetField<double>("Alt") ?? 0;

            // GPS status: 0=NoGPS, 1=NoFix, 2=2D, 3=3D, 4=DGPS, 5=RTK Float, 6=RTK Fix
            if (status < 3 && lastStatus >= 3)
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.GpsLoss,
                    Severity = LogEventSeverity.Error,
                    Title = "GPS Fix Lost",
                    Description = $"GPS fix lost (Status: {status}, Sats: {numSats})",
                    Source = "GPS",
                    Subsystem = "Position",
                    ComponentId = 25,
                    Latitude = lat,
                    Longitude = lng,
                    Altitude = alt,
                    Data = { ["Status"] = status, ["Sats"] = numSats },
                    RawMessage = $"GPS: Status={status}, NSats={numSats}, HDOP={hdop:F1}"
                });
            }
            else if (status >= 3 && lastStatus < 3 && lastStatus.HasValue)
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.GpsRecovery,
                    Severity = LogEventSeverity.Notice,
                    Title = "GPS Fix Recovered",
                    Description = $"GPS fix recovered (Status: {status}, Sats: {numSats})",
                    Source = "GPS",
                    Subsystem = "Position",
                    ComponentId = 25,
                    Latitude = lat,
                    Longitude = lng,
                    Altitude = alt,
                    Data = { ["Status"] = status, ["Sats"] = numSats },
                    RawMessage = $"GPS: Status={status}, NSats={numSats}, HDOP={hdop:F1}"
                });
            }

            if (hdop > GPS_HDOP_ERROR && hdopWarningCount < maxHdopWarnings)
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.GpsGlitch,
                    Severity = LogEventSeverity.Warning,
                    Title = "GPS HDOP High",
                    Description = $"GPS horizontal dilution of precision is poor: {hdop:F1}",
                    Source = "GPS",
                    Subsystem = "Position",
                    ComponentId = 25,
                    Latitude = lat,
                    Longitude = lng,
                    Altitude = alt,
                    Data = { ["HDOP"] = hdop, ["Sats"] = numSats },
                    RawMessage = $"GPS: HDOP={hdop:F1}, NSats={numSats}"
                });
                hdopWarningCount++;
            }

            lastStatus = status;
        }

        return events;
    }

    private List<LogEvent> DetectVibrationEvents(ref int eventId)
    {
        var events = new List<LogEvent>();
        int vibeWarningCount = 0;
        int vibeErrorCount = 0;
        int clipCount = 0;
        const int maxEventsPerType = 5;

        var vibeMessages = _parser!.GetMessages("VIBE");
        
        foreach (var msg in vibeMessages)
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
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.Vibration,
                    Severity = LogEventSeverity.Error,
                    Title = "High Vibration",
                    Description = $"Excessive vibration detected: X={vibeX:F1}, Y={vibeY:F1}, Z={vibeZ:F1} m/s�",
                    Source = "IMU",
                    Subsystem = "Vibration",
                    ComponentId = 1,
                    Data = { ["VibeX"] = vibeX, ["VibeY"] = vibeY, ["VibeZ"] = vibeZ },
                    RawMessage = $"VIBE: X={vibeX:F1}, Y={vibeY:F1}, Z={vibeZ:F1}"
                });
                vibeErrorCount++;
            }
            else if (maxVibe > VIBRATION_WARNING_THRESHOLD && vibeWarningCount < maxEventsPerType)
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.Vibration,
                    Severity = LogEventSeverity.Warning,
                    Title = "Elevated Vibration",
                    Description = $"Vibration elevated: X={vibeX:F1}, Y={vibeY:F1}, Z={vibeZ:F1} m/s�",
                    Source = "IMU",
                    Subsystem = "Vibration",
                    ComponentId = 1,
                    Data = { ["VibeX"] = vibeX, ["VibeY"] = vibeY, ["VibeZ"] = vibeZ },
                    RawMessage = $"VIBE: X={vibeX:F1}, Y={vibeY:F1}, Z={vibeZ:F1}"
                });
                vibeWarningCount++;
            }

            var totalClips = clip0 + clip1 + clip2;
            if (totalClips > CLIPPING_THRESHOLD && clipCount < maxEventsPerType)
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.Clipping,
                    Severity = LogEventSeverity.Warning,
                    Title = "Accelerometer Clipping",
                    Description = $"Accelerometer clipping detected: {totalClips:F0} clips",
                    Source = "IMU",
                    Subsystem = "Accelerometer",
                    ComponentId = 1,
                    Data = { ["Clips"] = totalClips },
                    RawMessage = $"VIBE: Clips={totalClips:F0}"
                });
                clipCount++;
            }
        }

        return events;
    }

    private List<LogEvent> DetectBatteryEvents(ref int eventId)
    {
        var events = new List<LogEvent>();

        var batMessages = _parser!.GetMessages("BAT");
        if (batMessages.Count == 0)
            batMessages = _parser.GetMessages("CURR");

        bool lowVoltageReported = false;
        bool criticalVoltageReported = false;

        foreach (var msg in batMessages)
        {
            var voltage = msg.GetField<double>("Volt") ?? 0;
            var current = msg.GetField<double>("Curr") ?? 0;
            var cellCount = EstimateCellCount(voltage);
            
            if (cellCount > 0)
            {
                var voltPerCell = voltage / cellCount;

                if (voltPerCell < BATTERY_CRITICAL_VOLTAGE && !criticalVoltageReported)
                {
                    events.Add(new LogEvent
                    {
                        Id = eventId++,
                        Timestamp = msg.Timestamp.TotalSeconds,
                        Type = LogEventType.BatteryCritical,
                        Severity = LogEventSeverity.Critical,
                        Title = "Battery Critical",
                        Description = $"Battery voltage critical: {voltage:F1}V ({voltPerCell:F2}V/cell)",
                        Source = "Power",
                        Subsystem = "Battery",
                        ComponentId = 1,
                        Data = { ["Voltage"] = voltage, ["VoltPerCell"] = voltPerCell, ["Current"] = current },
                        RawMessage = $"BAT: Volt={voltage:F1}V, Curr={current:F1}A"
                    });
                    criticalVoltageReported = true;
                }
                else if (voltPerCell < BATTERY_LOW_VOLTAGE && !lowVoltageReported)
                {
                    events.Add(new LogEvent
                    {
                        Id = eventId++,
                        Timestamp = msg.Timestamp.TotalSeconds,
                        Type = LogEventType.BatteryLow,
                        Severity = LogEventSeverity.Warning,
                        Title = "Battery Low",
                        Description = $"Battery voltage low: {voltage:F1}V ({voltPerCell:F2}V/cell)",
                        Source = "Power",
                        Subsystem = "Battery",
                        ComponentId = 1,
                        Data = { ["Voltage"] = voltage, ["VoltPerCell"] = voltPerCell, ["Current"] = current },
                        RawMessage = $"BAT: Volt={voltage:F1}V, Curr={current:F1}A"
                    });
                    lowVoltageReported = true;
                }
            }
        }

        return events;
    }

    private List<LogEvent> DetectRcEvents(ref int eventId)
    {
        var events = new List<LogEvent>();

        var rcMessages = _parser!.GetMessages("RCIN");
        ushort? lastRssi = null;
        int rssiWarningCount = 0;
        const int maxRssiWarnings = 5;

        foreach (var msg in rcMessages)
        {
            var rssi = (ushort)(msg.GetField<double>("RSSI") ?? 255);
            
            if (rssi < 50 && (lastRssi == null || lastRssi >= 50) && rssiWarningCount < maxRssiWarnings)
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.RcLoss,
                    Severity = LogEventSeverity.Warning,
                    Title = "RC Signal Weak",
                    Description = $"RC signal strength low: RSSI={rssi}",
                    Source = "RC Input",
                    Subsystem = "Receiver",
                    ComponentId = 1,
                    Data = { ["RSSI"] = rssi },
                    RawMessage = $"RCIN: RSSI={rssi}"
                });
                rssiWarningCount++;
            }
            else if (rssi >= 50 && lastRssi < 50)
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.RcRecovery,
                    Severity = LogEventSeverity.Notice,
                    Title = "RC Signal Recovered",
                    Description = $"RC signal recovered: RSSI={rssi}",
                    Source = "RC Input",
                    Subsystem = "Receiver",
                    ComponentId = 1,
                    Data = { ["RSSI"] = rssi },
                    RawMessage = $"RCIN: RSSI={rssi}"
                });
            }

            lastRssi = rssi;
        }

        return events;
    }

    private List<LogEvent> DetectCrashEvents(ref int eventId)
    {
        var events = new List<LogEvent>();

        // Check for crash detection in MSG
        var msgMessages = _parser!.GetMessages("MSG");
        foreach (var msg in msgMessages)
        {
            var text = msg.GetStringField("Message") ?? "";
            if (text.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("impact", StringComparison.OrdinalIgnoreCase))
            {
                events.Add(new LogEvent
                {
                    Id = eventId++,
                    Timestamp = msg.Timestamp.TotalSeconds,
                    Type = LogEventType.Crash,
                    Severity = LogEventSeverity.Emergency,
                    Title = "Crash Detected",
                    Description = text,
                    Source = "Autopilot",
                    Subsystem = "Safety",
                    ComponentId = 1,
                    RawMessage = text
                });
            }
        }

        return events;
    }

    private static int EstimateCellCount(double voltage)
    {
        // Estimate cell count based on voltage
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

    public Task<List<LogEvent>> GetEventsInRangeAsync(
        double startTime,
        double endTime,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            (_cachedEvents ?? new List<LogEvent>())
            .Where(e => e.Timestamp >= startTime && e.Timestamp <= endTime)
            .ToList()
        );
    }

    public Task<List<LogEvent>> GetEventsBySeverityAsync(
        LogEventSeverity minSeverity,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            (_cachedEvents ?? new List<LogEvent>())
            .Where(e => e.Severity >= minSeverity)
            .ToList()
        );
    }

    public Task<List<LogEvent>> GetEventsByTypeAsync(
        LogEventType eventType,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(
            (_cachedEvents ?? new List<LogEvent>())
            .Where(e => e.Type == eventType)
            .ToList()
        );
    }

    public EventSummary GetEventSummary()
    {
        var events = _cachedEvents ?? new List<LogEvent>();
        
        return new EventSummary
        {
            TotalEvents = events.Count,
            DebugCount = events.Count(e => e.Severity == LogEventSeverity.Debug),
            InfoCount = events.Count(e => e.Severity == LogEventSeverity.Info),
            NoticeCount = events.Count(e => e.Severity == LogEventSeverity.Notice),
            WarningCount = events.Count(e => e.Severity == LogEventSeverity.Warning),
            ErrorCount = events.Count(e => e.Severity == LogEventSeverity.Error),
            CriticalCount = events.Count(e => e.Severity == LogEventSeverity.Critical),
            EmergencyCount = events.Count(e => e.Severity == LogEventSeverity.Emergency),
            EventsByType = events.GroupBy(e => e.Type)
                .ToDictionary(g => g.Key, g => g.Count()),
            EventsBySource = events.GroupBy(e => e.Source)
                .Where(g => !string.IsNullOrEmpty(g.Key))
                .ToDictionary(g => g.Key, g => g.Count()),
            FlightDurationSeconds = _parsedLog?.Duration.TotalSeconds ?? 0
        };
    }
}
