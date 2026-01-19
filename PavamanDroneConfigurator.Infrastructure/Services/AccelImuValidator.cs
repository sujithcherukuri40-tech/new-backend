using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Infrastructure.MAVLink;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Validates accelerometer orientation using IMU data.
/// 
/// CRITICAL SAFETY: This validator prevents bad calibration data from being sent to FC.
/// Incorrect accelerometer calibration can cause CRASHES.
/// 
/// ArduPilot Body-Fixed Coordinate System (NED - North-East-Down):
/// - X-axis: Points forward (nose direction)
/// - Y-axis: Points right (starboard wing)
/// - Z-axis: Points down (towards ground when level)
/// 
/// Gravity vector when level: (0, 0, +9.81) m/s² (pointing down)
/// 
/// Validation logic:
/// - Checks gravity vector magnitude (~9.81 m/s² ±20%)
/// - Checks gravity vector direction matches expected axis (?85% of magnitude)
/// - Checks other axes are small (?30% of magnitude)
/// - Rejects incorrect orientations with detailed diagnostic messages
/// </summary>
public class AccelImuValidator
{
    private readonly ILogger<AccelImuValidator> _logger;
    
    // Physical constants
    private const double GRAVITY = 9.81; // m/s²
    private const double GRAVITY_TOLERANCE_PERCENT = 20.0; // ±20% tolerance for sensor noise/calibration
    private const double DOMINANT_AXIS_THRESHOLD = 0.85; // 85% of gravity must be on correct axis
    private const double OTHER_AXIS_THRESHOLD = 0.30; // Other axes must be below 30% of gravity
    
    public AccelImuValidator(ILogger<AccelImuValidator> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Validate position using IMU accelerometer data.
    /// Returns validation result with pass/fail and error message.
    /// </summary>
    public AccelValidationResult ValidatePosition(int position, RawImuData imuData)
    {
        if (position < 1 || position > 6)
        {
            return new AccelValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Invalid position number: {position}"
            };
        }
        
        // Convert raw IMU to m/s²
        var accel = imuData.GetAcceleration();
        
        _logger.LogDebug("Validating position {Position}: raw=({XRaw}, {YRaw}, {ZRaw}), scaled=({X:F2}, {Y:F2}, {Z:F2}) m/s²",
            position, imuData.XAcc, imuData.YAcc, imuData.ZAcc, accel.X, accel.Y, accel.Z);
        
        // Calculate gravity magnitude
        var magnitude = Math.Sqrt(accel.X * accel.X + accel.Y * accel.Y + accel.Z * accel.Z);
        
        // Check magnitude is approximately 1G (±20% tolerance)
        var expectedGravity = GRAVITY;
        var toleranceLow = expectedGravity * (1 - GRAVITY_TOLERANCE_PERCENT / 100);
        var toleranceHigh = expectedGravity * (1 + GRAVITY_TOLERANCE_PERCENT / 100);
        
        if (magnitude < toleranceLow || magnitude > toleranceHigh)
        {
            var message = $"Position {position} ({GetPositionName(position)}) REJECTED:\n\n" +
                         $"Gravity magnitude {magnitude:F2} m/s² is outside expected range.\n" +
                         $"Expected: {toleranceLow:F2} - {toleranceHigh:F2} m/s² (9.81 ±{GRAVITY_TOLERANCE_PERCENT:F0}%)\n\n" +
                         $"This may indicate:\n" +
                         $"• IMU sensor malfunction\n" +
                         $"• Excessive vibration\n" +
                         $"• Vehicle not stationary\n\n" +
                         $"? Ensure IMU is securely mounted and vehicle is completely still.";
            
            _logger.LogWarning("Position {Position} magnitude check FAILED: {Mag:F2} m/s² (expected {Expected:F2} ±{Tol:F0}%)",
                position, magnitude, GRAVITY, GRAVITY_TOLERANCE_PERCENT);
            
            return new AccelValidationResult
            {
                IsValid = false,
                ErrorMessage = message,
                MeasuredMagnitude = magnitude
            };
        }
        
        // Check axis alignment for this position
        var alignmentResult = CheckAxisAlignment(position, accel.X, accel.Y, accel.Z, magnitude);
        
        if (!alignmentResult.IsValid)
        {
            _logger.LogWarning("Position {Position} alignment check FAILED: {Message}",
                position, alignmentResult.ErrorMessage);
            
            return new AccelValidationResult
            {
                IsValid = false,
                ErrorMessage = alignmentResult.ErrorMessage,
                MeasuredMagnitude = magnitude,
                MeasuredX = accel.X,
                MeasuredY = accel.Y,
                MeasuredZ = accel.Z
            };
        }
        
        // Validation PASSED
        var successMessage = $"? Position {position} ({GetPositionName(position)}) verified correctly.\n" +
                            $"Magnitude: {magnitude:F2} m/s²\n" +
                            $"Orientation: {GetExpectedAxis(position)}";
        
        _logger.LogInformation("Position {Position} validation PASSED: mag={Mag:F2} m/s², " +
                              "accel=({X:F2}, {Y:F2}, {Z:F2})",
                              position, magnitude, accel.X, accel.Y, accel.Z);
        
        return new AccelValidationResult
        {
            IsValid = true,
            ErrorMessage = successMessage,
            MeasuredMagnitude = magnitude,
            MeasuredX = accel.X,
            MeasuredY = accel.Y,
            MeasuredZ = accel.Z
        };
    }
    
    /// <summary>
    /// Check that gravity vector is aligned with expected axis for this position.
    /// Uses strict validation: dominant axis ?85%, other axes ?30%.
    /// </summary>
    private AccelValidationResult CheckAxisAlignment(int position, double x, double y, double z, double magnitude)
    {
        var absX = Math.Abs(x);
        var absY = Math.Abs(y);
        var absZ = Math.Abs(z);
        
        var dominantThreshold = magnitude * DOMINANT_AXIS_THRESHOLD; // 85% of measured gravity
        var otherThreshold = magnitude * OTHER_AXIS_THRESHOLD;       // 30% of measured gravity
        
        // ArduPilot body-fixed NED coordinate system:
        // Expected orientations:
        // 1. LEVEL:      Z ? +9.81 (gravity points down through bottom)
        // 2. LEFT:       Y ? -9.81 (gravity points down through left side)
        // 3. RIGHT:      Y ? +9.81 (gravity points down through right side)
        // 4. NOSE DOWN:  X ? +9.81 (gravity points down through nose)
        // 5. NOSE UP:    X ? -9.81 (gravity points down through tail)
        // 6. BACK:       Z ? -9.81 (gravity points up through top)
        
        bool isDominantCorrect = position switch
        {
            1 => absZ >= dominantThreshold && z > 0,  // LEVEL: +Z dominant
            2 => absY >= dominantThreshold && y < 0,  // LEFT: -Y dominant
            3 => absY >= dominantThreshold && y > 0,  // RIGHT: +Y dominant
            4 => absX >= dominantThreshold && x > 0,  // NOSE DOWN: +X dominant
            5 => absX >= dominantThreshold && x < 0,  // NOSE UP: -X dominant
            6 => absZ >= dominantThreshold && z < 0,  // BACK: -Z dominant
            _ => false
        };
        
        if (!isDominantCorrect)
        {
            var expectedAxis = GetExpectedAxis(position);
            var expectedSign = GetExpectedSign(position);
            var actualDominant = GetDominantAxisName(absX, absY, absZ);
            var actualValue = GetDominantAxisValue(position, x, y, z);
            
            var message = $"Position {position} ({GetPositionName(position)}) INCORRECT:\n\n" +
                         $"Expected: Gravity on {expectedAxis}\n" +
                         $"          ({expectedSign})\n\n" +
                         $"Measured: X={x:F2}, Y={y:F2}, Z={z:F2} m/s²\n" +
                         $"Dominant: {actualDominant} = {actualValue:F2} m/s²\n\n" +
                         $"Problem: ";
            
            // Diagnose the specific problem
            var (dominantAxis, dominantValue, requiredValue) = position switch
            {
                1 or 6 => ("Z", absZ, z),
                2 or 3 => ("Y", absY, y),
                4 or 5 => ("X", absX, x),
                _ => ("?", 0.0, 0.0)
            };
            
            if (dominantValue < dominantThreshold)
            {
                message += $"{dominantAxis}-axis magnitude too small.\n";
                message += $"  Measured: {dominantValue:F2} m/s² ({dominantValue/magnitude*100:F0}%)\n";
                message += $"  Required: ?{dominantThreshold:F2} m/s² (?{DOMINANT_AXIS_THRESHOLD*100:F0}%)\n\n";
            }
            
            var expectedPositive = position switch { 1 => true, 3 => true, 4 => true, _ => false };
            var wrongSign = position switch
            {
                1 => z <= 0,
                2 => y >= 0,
                3 => y <= 0,
                4 => x <= 0,
                5 => x >= 0,
                6 => z >= 0,
                _ => false
            };
            
            if (wrongSign)
            {
                message += $"{dominantAxis}-axis has wrong sign.\n";
                message += $"  Measured: {requiredValue:F2} m/s²\n";
                message += $"  Expected: {expectedSign}\n\n";
            }
            
            message += GetCorrectionAdvice(position);
            
            return new AccelValidationResult
            {
                IsValid = false,
                ErrorMessage = message
            };
        }
        
        // Check that other axes are not too large (indicates tilt or wrong orientation)
        var otherAxesOk = position switch
        {
            1 or 6 => absX <= otherThreshold && absY <= otherThreshold,  // Z dominant
            2 or 3 => absX <= otherThreshold && absZ <= otherThreshold,  // Y dominant
            4 or 5 => absY <= otherThreshold && absZ <= otherThreshold,  // X dominant
            _ => false
        };
        
        if (!otherAxesOk)
        {
            var message = $"Position {position} ({GetPositionName(position)}) INCORRECT:\n\n" +
                         $"Dominant axis is correct, but vehicle is TILTED.\n\n" +
                         $"Measured: X={x:F2}, Y={y:F2}, Z={z:F2} m/s²\n" +
                         $"Required: Non-dominant axes ?{otherThreshold:F2} m/s² (?{OTHER_AXIS_THRESHOLD*100:F0}%)\n\n" +
                         $"The vehicle is not positioned precisely enough.\n" +
                         $"All non-dominant axes must be small.\n\n" +
                         GetCorrectionAdvice(position);
            
            _logger.LogWarning("Position {Position} has excessive tilt: X={X:F2}, Y={Y:F2}, Z={Z:F2}, threshold={Thresh:F2}",
                position, absX, absY, absZ, otherThreshold);
            
            return new AccelValidationResult
            {
                IsValid = false,
                ErrorMessage = message
            };
        }
        
        return new AccelValidationResult
        {
            IsValid = true
        };
    }
    
    private static string GetPositionName(int position)
    {
        return position switch
        {
            1 => "LEVEL",
            2 => "LEFT SIDE",
            3 => "RIGHT SIDE",
            4 => "NOSE DOWN",
            5 => "NOSE UP",
            6 => "BACK / UPSIDE DOWN",
            _ => "UNKNOWN"
        };
    }
    
    private static string GetExpectedAxis(int position)
    {
        return position switch
        {
            1 => "+Z axis (down)",
            2 => "-Y axis (left)",
            3 => "+Y axis (right)",
            4 => "+X axis (forward)",
            5 => "-X axis (backward)",
            6 => "-Z axis (up)",
            _ => "unknown"
        };
    }
    
    private static string GetExpectedSign(int position)
    {
        return position switch
        {
            1 => "Z ? +9.81 m/s²",
            2 => "Y ? -9.81 m/s²",
            3 => "Y ? +9.81 m/s²",
            4 => "X ? +9.81 m/s²",
            5 => "X ? -9.81 m/s²",
            6 => "Z ? -9.81 m/s²",
            _ => "unknown"
        };
    }
    
    private static string GetDominantAxisName(double absX, double absY, double absZ)
    {
        if (absX > absY && absX > absZ) return "X-axis";
        if (absY > absX && absY > absZ) return "Y-axis";
        return "Z-axis";
    }
    
    private static double GetDominantAxisValue(int position, double x, double y, double z)
    {
        return position switch
        {
            1 or 6 => z,
            2 or 3 => y,
            4 or 5 => x,
            _ => 0.0
        };
    }
    
    private static string GetCorrectionAdvice(int position)
    {
        return position switch
        {
            1 => "? For LEVEL: Place vehicle flat on level surface.\n" +
                 "  • All four corners/legs must touch surface evenly\n" +
                 "  • Use bubble level or smartphone level app if available\n" +
                 "  • Vehicle must be completely still",
            
            2 => "? For LEFT SIDE: Place vehicle on its left side.\n" +
                 "  • Right side should point straight up\n" +
                 "  • Left side touching surface\n" +
                 "  • Nose should point forward (not tilted)\n" +
                 "  • Use foam or blocks to prevent rolling",
            
            3 => "? For RIGHT SIDE: Place vehicle on its right side.\n" +
                 "  • Left side should point straight up\n" +
                 "  • Right side touching surface\n" +
                 "  • Nose should point forward (not tilted)\n" +
                 "  • Use foam or blocks to prevent rolling",
            
            4 => "? For NOSE DOWN: Tilt vehicle forward 90 degrees.\n" +
                 "  • Nose pointing straight down\n" +
                 "  • Tail pointing straight up\n" +
                 "  • Use box/stand to hold position without tilt\n" +
                 "  • Vehicle must not lean left or right",
            
            5 => "? For NOSE UP: Tilt vehicle backward 90 degrees.\n" +
                 "  • Nose pointing straight up\n" +
                 "  • Tail pointing straight down\n" +
                 "  • Use box/stand to hold position without tilt\n" +
                 "  • Vehicle must not lean left or right",
            
            6 => "? For BACK (UPSIDE DOWN): Flip vehicle completely.\n" +
                 "  • Bottom facing up\n" +
                 "  • Top touching surface\n" +
                 "  • Must be flat (not tilted forward/back or left/right)\n" +
                 "  • Use foam padding to protect camera/props",
            
            _ => ""
        };
    }
}

/// <summary>
/// Result of IMU-based position validation.
/// </summary>
public class AccelValidationResult
{
    /// <summary>Validation passed</summary>
    public bool IsValid { get; set; }
    
    /// <summary>Error or success message</summary>
    public string ErrorMessage { get; set; } = "";
    
    /// <summary>Measured gravity magnitude (m/s²)</summary>
    public double MeasuredMagnitude { get; set; }
    
    /// <summary>Measured X acceleration (m/s²)</summary>
    public double MeasuredX { get; set; }
    
    /// <summary>Measured Y acceleration (m/s²)</summary>
    public double MeasuredY { get; set; }
    
    /// <summary>Measured Z acceleration (m/s²)</summary>
    public double MeasuredZ { get; set; }
}
