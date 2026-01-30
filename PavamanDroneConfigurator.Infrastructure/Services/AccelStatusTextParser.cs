using Microsoft.Extensions.Logging;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Parses STATUSTEXT messages from Flight Controller during accelerometer calibration.
/// 
/// CRITICAL: This parser detects position requests, completion, and failure messages.
/// FC controls the calibration workflow entirely via STATUSTEXT.
/// 
/// MISSION PLANNER COMPATIBILITY (REFERENCE BEHAVIOR):
/// - ALL "PreArm:" messages are NON-BLOCKING during accelerometer calibration
/// - PreArm messages include:
///   - "PreArm: RC not found"
///   - "PreArm: Hardware safety switch"
///   - "PreArm: Compass not calibrated"
///   - "PreArm: EKF / GPS / AHRS" warnings
/// - These are informational ONLY during IMU calibration
/// - Safety/PreArm only blocks motor-related operations (arming, motor test, ESC calibration)
/// - Accelerometer calibration proceeds normally even when:
///   - RC is not connected
///   - Safety switch is enabled
///   - Compass is not calibrated
///   - GPS is not available
/// 
/// TASK 1: Filters out EKF/PreArm/GPS/RC warnings that interfere with calibration flow.
/// These messages are normal during accel calibration and must NOT affect state progression.
/// </summary>
public class AccelStatusTextParser
{
    private readonly ILogger<AccelStatusTextParser> _logger;
    
    // TASK 1: Interference patterns to IGNORE during accelerometer calibration
    // These messages are normal side-effects and must NOT affect calibration flow
    // 
    // CRITICAL - MISSION PLANNER BEHAVIOR:
    // ALL PreArm messages are treated as INTERFERENCE (non-blocking) during IMU calibration
    // This includes:
    //   - "PreArm: RC not found"
    //   - "PreArm: Hardware safety switch"
    //   - "PreArm: Compass not calibrated"
    //   - "PreArm: EKF / GPS / AHRS" warnings
    //   - Any other PreArm warning
    // 
    // These ONLY block motor operations (arming, motor test, ESC cal)
    // They do NOT block sensor calibrations (accelerometer, gyro, compass, baro)
    private static readonly string[] InterferenceKeywords =
    {
        "prearm",           // CRITICAL: Catches ALL PreArm messages (RC, safety, compass, etc.)
        "ekf",
        "ekf3",
        "ekf2",
        "ahrs",
        "yaw",
        "gps",
        "waiting for gps",
        "no gps",
        "bad ahrs",
        "compass",
        "mag",
        "magnetometer",
        "gyro",
        "velocity",
        "position",
        "home",
        "fence",
        "safety",           // Hardware safety switch messages
        "hardware safety",  // Explicit match for safety switch
        "rc not",           // RC not found / not connected
        "radio",            // Radio/RC failsafe messages
        "failsafe"          // Failsafe warnings during calibration
    };
    
    // Keywords for position detection (case-insensitive)
    private const string PLACE = "place";
    private const string LEVEL = "level";
    private const string LEFT = "left";
    private const string RIGHT = "right";
    private const string NOSE_DOWN = "nose down";
    private const string NOSE_UP = "nose up";
    private const string BACK = "back";
    private const string UPSIDE = "upside";
    
    // Keywords for completion detection
    private static readonly string[] CompletionKeywords =
    {
        "calibration successful",
        "calibration complete",
        "calibration done",
        "accel calibration successful",
        "accelerometer calibration successful",
        "accel cal complete",
        "accel offsets"
    };
    
    // Keywords for failure detection (calibration-specific only)
    // CRITICAL: Generic "failed" or "denied" are NOT here - only calibration-specific failures
    private static readonly string[] FailureKeywords =
    {
        "accel calibration failed",
        "accel cal failed",
        "calibration cancelled",
        "calibration timeout",
        "rotation bad"
    };
    
    // Keywords for sampling detection
    private static readonly string[] SamplingKeywords =
    {
        "sampling",
        "reading",
        "detected",
        "hold still"
    };
    
    public AccelStatusTextParser(ILogger<AccelStatusTextParser> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Parse STATUSTEXT message to detect position requests, completion, or failure.
    /// TASK 1: Filters out ALL PreArm/EKF/GPS/RC/Safety warnings during calibration.
    /// 
    /// MISSION PLANNER BEHAVIOR (REFERENCE):
    /// - ALL "PreArm:" messages ? Filtered as interference (non-blocking)
    /// - "RC not found" ? Filtered as interference (non-blocking)
    /// - "Hardware safety switch" ? Filtered as interference (non-blocking)
    /// - "Compass not calibrated" ? Filtered as interference (non-blocking)
    /// - PreArm warnings do NOT prevent accelerometer calibration
    /// - They ONLY block motor operations (arming, motor test, ESC cal)
    /// </summary>
    public StatusTextParseResult Parse(string statusText)
    {
        if (string.IsNullOrWhiteSpace(statusText))
            return new StatusTextParseResult();
        
        var lowerText = statusText.ToLowerInvariant();
        
        // TASK 1: FILTER OUT ALL PreArm/EKF/GPS/RC/Safety interference FIRST
        // This must happen BEFORE checking for completion/failure to avoid false positives
        if (IsInterferenceMessage(lowerText))
        {
            // Determine the type of interference for better logging
            bool isPreArmMessage = lowerText.Contains("prearm");
            bool isSafetyMessage = lowerText.Contains("safety");
            bool isRcMessage = lowerText.Contains("rc not") || lowerText.Contains("radio");
            bool isCompassMessage = lowerText.Contains("compass");
            
            if (isPreArmMessage)
            {
                _logger.LogDebug("PreArm message during calibration (NON-BLOCKING - Mission Planner compatible): {Text}", statusText);
            }
            else if (isSafetyMessage)
            {
                _logger.LogDebug("Safety switch message during calibration (NON-BLOCKING): {Text}", statusText);
            }
            else if (isRcMessage)
            {
                _logger.LogDebug("RC/Radio message during calibration (NON-BLOCKING): {Text}", statusText);
            }
            else if (isCompassMessage)
            {
                _logger.LogDebug("Compass message during calibration (NON-BLOCKING): {Text}", statusText);
            }
            else
            {
                _logger.LogDebug("Filtered interference message during calibration: {Text}", statusText);
            }
            
            return new StatusTextParseResult
            {
                IsInterference = true,
                IsPreArmWarning = isPreArmMessage,
                IsSafetyWarning = isSafetyMessage,
                IsRcWarning = isRcMessage,
                IsCompassWarning = isCompassMessage,
                OriginalText = statusText
            };
        }
        
        // Check for completion FIRST (highest priority)
        if (IsCompletionMessage(lowerText))
        {
            _logger.LogInformation("Detected completion message: {Text}", statusText);
            return new StatusTextParseResult
            {
                IsSuccess = true,
                OriginalText = statusText
            };
        }
        
        // Check for failure (calibration-specific only)
        if (IsFailureMessage(lowerText))
        {
            _logger.LogWarning("Detected failure message: {Text}", statusText);
            return new StatusTextParseResult
            {
                IsFailure = true,
                OriginalText = statusText
            };
        }
        
        // Check for position request
        var requestedPosition = DetectPositionRequest(lowerText);
        if (requestedPosition.HasValue)
        {
            _logger.LogInformation("Detected position request: position {Position} from text: {Text}", 
                requestedPosition.Value, statusText);
            
            return new StatusTextParseResult
            {
                IsPositionRequest = true,
                RequestedPosition = requestedPosition.Value,
                OriginalText = statusText
            };
        }
        
        // Check for sampling message
        if (IsSamplingMessage(lowerText))
        {
            _logger.LogDebug("Detected sampling message: {Text}", statusText);
            return new StatusTextParseResult
            {
                IsSampling = true,
                OriginalText = statusText
            };
        }
        
        // Unknown/informational message
        return new StatusTextParseResult
        {
            OriginalText = statusText
        };
    }
    
    /// <summary>
    /// TASK 1: Check if message is PreArm/EKF/GPS/RC/Safety interference.
    /// These messages are normal during accelerometer calibration and should be ignored.
    /// 
    /// CRITICAL - MISSION PLANNER COMPATIBILITY:
    /// ALL "PreArm:" messages are treated as INTERFERENCE (non-blocking) during IMU calibration.
    /// This includes:
    ///   - "PreArm: RC not found"
    ///   - "PreArm: Hardware safety switch"
    ///   - "PreArm: Compass not calibrated"
    ///   - "PreArm: EKF / GPS / AHRS" warnings
    /// 
    /// These ONLY block motor operations, NOT sensor calibrations.
    /// </summary>
    private bool IsInterferenceMessage(string lowerText)
    {
        // Check against interference patterns
        foreach (var keyword in InterferenceKeywords)
        {
            if (lowerText.Contains(keyword))
            {
                return true;
            }
        }
        
        return false;
    }
    
    private bool IsCompletionMessage(string lowerText)
    {
        return CompletionKeywords.Any(keyword => lowerText.Contains(keyword));
    }
    
    private bool IsFailureMessage(string lowerText)
    {
        // TASK 1: Only check calibration-specific failure keywords
        // Generic "failed" or "error" are NOT treated as calibration failures
        return FailureKeywords.Any(keyword => lowerText.Contains(keyword));
    }
    
    private bool IsSamplingMessage(string lowerText)
    {
        return SamplingKeywords.Any(keyword => lowerText.Contains(keyword));
    }
    
    /// <summary>
    /// Detect which position FC is requesting (1-6).
    /// Returns null if not a position request.
    /// </summary>
    private int? DetectPositionRequest(string lowerText)
    {
        // Must contain "place" to be a position request
        if (!lowerText.Contains(PLACE))
            return null;
        
        // Check positions in order of specificity (most specific first)
        
        // Position 4: NOSE DOWN (check before general "nose")
        if (lowerText.Contains(NOSE_DOWN) || 
            (lowerText.Contains("nose") && lowerText.Contains("down")))
        {
            return 4;
        }
        
        // Position 5: NOSE UP
        if (lowerText.Contains(NOSE_UP) || 
            (lowerText.Contains("nose") && lowerText.Contains("up")))
        {
            return 5;
        }
        
        // Position 2: LEFT (check it's not "left side" vs "right side")
        if (lowerText.Contains(LEFT) && !lowerText.Contains(RIGHT))
        {
            return 2;
        }
        
        // Position 3: RIGHT
        if (lowerText.Contains(RIGHT) && !lowerText.Contains(LEFT))
        {
            return 3;
        }
        
        // Position 6: BACK / UPSIDE DOWN
        if (lowerText.Contains(BACK) || lowerText.Contains(UPSIDE))
        {
            return 6;
        }
        
        // Position 1: LEVEL (check last, as it's most common word)
        if (lowerText.Contains(LEVEL))
        {
            return 1;
        }
        
        // Contains "place" but no recognized position keyword
        _logger.LogWarning("STATUSTEXT contains 'place' but no recognized position: {Text}", lowerText);
        return null;
    }
}

/// <summary>
/// Result of parsing a STATUSTEXT message.
/// </summary>
public class StatusTextParseResult
{
    /// <summary>FC is requesting a position</summary>
    public bool IsPositionRequest { get; set; }
    
    /// <summary>Position requested (1-6), if IsPositionRequest is true</summary>
    public int? RequestedPosition { get; set; }
    
    /// <summary>FC reported calibration success</summary>
    public bool IsSuccess { get; set; }
    
    /// <summary>FC reported calibration failure</summary>
    public bool IsFailure { get; set; }
    
    /// <summary>FC is sampling position</summary>
    public bool IsSampling { get; set; }
    
    /// <summary>TASK 1: Message is EKF/PreArm/GPS/RC/Safety interference (should be ignored)</summary>
    public bool IsInterference { get; set; }
    
    /// <summary>
    /// MISSION PLANNER COMPATIBILITY:
    /// True if message is a PreArm warning (e.g., "PreArm: RC not found", "PreArm: Hardware safety switch").
    /// ALL PreArm messages are NON-BLOCKING during accelerometer calibration.
    /// PreArm only blocks motor operations (arming, motor test, ESC calibration).
    /// </summary>
    public bool IsPreArmWarning { get; set; }
    
    /// <summary>
    /// MISSION PLANNER COMPATIBILITY:
    /// True if message is a safety-related warning (e.g., "PreArm: Hardware safety switch").
    /// These are NON-BLOCKING during accelerometer calibration.
    /// Safety only blocks motor operations (arming, motor test, ESC calibration).
    /// </summary>
    public bool IsSafetyWarning { get; set; }
    
    /// <summary>
    /// True if message is an RC/Radio warning (e.g., "RC not found", "Radio failsafe").
    /// These are NON-BLOCKING during accelerometer calibration.
    /// </summary>
    public bool IsRcWarning { get; set; }
    
    /// <summary>
    /// True if message is a compass warning (e.g., "Compass not calibrated").
    /// These are NON-BLOCKING during accelerometer calibration.
    /// </summary>
    public bool IsCompassWarning { get; set; }
    
    /// <summary>Original STATUSTEXT message</summary>
    public string OriginalText { get; set; } = "";
}
