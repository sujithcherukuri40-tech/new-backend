using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Infrastructure.MAVLink;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Production-grade accelerometer position validator using IMU data. 
/// 
/// CRITICAL SAFETY: Prevents bad calibration data from reaching flight controller.
/// Incorrect accelerometer calibration can cause CRASHES and LOSS OF CONTROL.
/// 
/// ArduPilot Body-Fixed Coordinate System (NED - North-East-Down):
/// - X-axis: Points forward (nose direction)
/// - Y-axis: Points right (starboard wing)
/// - Z-axis: Points down (towards ground when level)
/// 
/// Gravity vector when level: (0, 0, +9.81) m/s² (pointing down)
/// 
/// Validation Strategy:
/// 1. Check gravity magnitude is approximately 9.81 m/s² (±15% tolerance for sensor noise)
/// 2. Check dominant axis has at least 87% of gravity magnitude (stricter than 85%)
/// 3. Check other axes are below 25% of gravity magnitude (stricter than 30%)
/// 4. Verify correct sign for dominant axis
/// 5. Multi-sample validation for stability (optional, recommended)
/// </summary>
public sealed class AccelImuValidatorImproved
{
    private readonly ILogger<AccelImuValidatorImproved> _logger;

    // Physical constants (SI units)
    private const double GRAVITY = 9.80665; // Standard gravity in m/s² (more precise)

    // Validation thresholds (calibrated for production use)
    private const double GRAVITY_TOLERANCE_PERCENT = 15.0; // ±15% (tighter than 20% for better accuracy)
    private const double DOMINANT_AXIS_THRESHOLD = 0.87;   // 87% minimum (stricter than 85%)
    private const double OTHER_AXIS_MAX_THRESHOLD = 0.25;  // 25% maximum (stricter than 30%)

    // Statistical validation (for multi-sample mode)
    private const int MIN_SAMPLES_FOR_STABILITY = 3;
    private const double MAX_SAMPLE_VARIANCE_PERCENT = 5.0; // Max 5% variance between samples

    public AccelImuValidatorImproved(ILogger<AccelImuValidatorImproved> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Validate position using single IMU sample (compatibility with existing interface).
    /// </summary>
    public AccelValidationResult ValidatePosition(int position, RawImuData imuData)
    {
        if (position < 1 || position > 6)
        {
            return new AccelValidationResult
            {
                IsValid = false,
                ErrorMessage = $"Invalid position number: {position}. Must be 1-6."
            };
        }

        if (imuData == null)
        {
            return new AccelValidationResult
            {
                IsValid = false,
                ErrorMessage = "IMU data is null"
            };
        }

        // Get acceleration in m/s²
        var accel = imuData.GetAcceleration();
        var magnitude = CalculateMagnitude(accel.X, accel.Y, accel.Z);

        _logger.LogDebug(
            "Validating position {Position}: raw=({XRaw}, {YRaw}, {ZRaw}), scaled=({X:F3}, {Y:F3}, {Z:F3}) m/s², mag={Mag:F3}",
            position, imuData.XAcc, imuData.YAcc, imuData.ZAcc, accel.X, accel.Y, accel.Z, magnitude);

        // Step 1: Validate gravity magnitude
        var magnitudeResult = ValidateGravityMagnitude(position, magnitude);
        if (!magnitudeResult.IsValid)
        {
            magnitudeResult.MeasuredX = accel.X;
            magnitudeResult.MeasuredY = accel.Y;
            magnitudeResult.MeasuredZ = accel.Z;
            magnitudeResult.MeasuredMagnitude = magnitude;
            return magnitudeResult;
        }

        // Step 2: Validate axis alignment
        var alignmentResult = ValidateAxisAlignment(position, accel.X, accel.Y, accel.Z, magnitude);
        if (!alignmentResult.IsValid)
        {
            alignmentResult.MeasuredX = accel.X;
            alignmentResult.MeasuredY = accel.Y;
            alignmentResult.MeasuredZ = accel.Z;
            alignmentResult.MeasuredMagnitude = magnitude;
            return alignmentResult;
        }

        // Validation PASSED
        var successMessage = $"✓ Position {position} ({GetPositionName(position)}) verified correctly.\n" +
                            $"Magnitude: {magnitude:F3} m/s²\n" +
                            $"Orientation: {GetExpectedAxisShort(position)}";

        _logger.LogInformation(
            "Position {Position} validation PASSED: mag={Mag:F3} m/s², accel=({X:F3}, {Y:F3}, {Z:F3})",
            position, magnitude, accel.X, accel.Y, accel.Z);

        return new AccelValidationResult
        {
            IsValid = true,
            ErrorMessage = successMessage,
            MeasuredX = accel.X,
            MeasuredY = accel.Y,
            MeasuredZ = accel.Z,
            MeasuredMagnitude = magnitude
        };
    }

    /// <summary>
    /// Validate position using multiple IMU samples for improved stability (RECOMMENDED).
    /// Takes average of samples and checks variance is acceptable.
    /// </summary>
    public AccelValidationResult ValidatePositionMultiSample(int position, IReadOnlyList<RawImuData> imuSamples)
    {
        if (imuSamples == null || imuSamples.Count == 0)
        {
            return new AccelValidationResult
            {
                IsValid = false,
                ErrorMessage = "No IMU samples provided"
            };
        }

        if (imuSamples.Count < MIN_SAMPLES_FOR_STABILITY)
        {
            _logger.LogWarning(
                "Only {Count} samples provided, minimum {MinCount} recommended for stability check",
                imuSamples.Count,
                MIN_SAMPLES_FOR_STABILITY
            );
        }

        // Calculate average acceleration from all samples
        double sumX = 0, sumY = 0, sumZ = 0;
        var magnitudes = new List<double>();

        foreach (var sample in imuSamples)
        {
            if (sample == null) continue;

            var accel = sample.GetAcceleration();
            sumX += accel.X;
            sumY += accel.Y;
            sumZ += accel.Z;
            magnitudes.Add(CalculateMagnitude(accel.X, accel.Y, accel.Z));
        }

        int validSamples = imuSamples.Count(s => s != null);
        if (validSamples == 0)
        {
            return new AccelValidationResult
            {
                IsValid = false,
                ErrorMessage = "All IMU samples are null"
            };
        }

        var avgX = sumX / validSamples;
        var avgY = sumY / validSamples;
        var avgZ = sumZ / validSamples;
        var avgMagnitude = magnitudes.Average();

        // Check variance/stability of samples
        if (validSamples >= MIN_SAMPLES_FOR_STABILITY)
        {
            var variance = CalculateVariance(magnitudes, avgMagnitude);
            var variancePercent = (variance / avgMagnitude) * 100;

            if (variancePercent > MAX_SAMPLE_VARIANCE_PERCENT)
            {
                var message =
                    $"Position {position} ({GetPositionName(position)}) samples are UNSTABLE:\n" +
                    $"  Magnitude variance: {variancePercent:F2}% (max allowed: {MAX_SAMPLE_VARIANCE_PERCENT:F1}%)\n" +
                    $"  This indicates vibration, movement, or sensor noise.\n\n" +
                    $"→ Ensure vehicle is completely stationary.\n" +
                    $"→ Check IMU mounting is secure (no vibrations).\n" +
                    $"→ Wait a few seconds after positioning before confirming.";

                _logger.LogWarning(
                    "Position {Position} sample variance too high: {Variance:F2}% (max {MaxVariance:F1}%)",
                    position,
                    variancePercent,
                    MAX_SAMPLE_VARIANCE_PERCENT
                );

                return new AccelValidationResult
                {
                    IsValid = false,
                    ErrorMessage = message,
                    MeasuredMagnitude = avgMagnitude,
                    MeasuredX = avgX,
                    MeasuredY = avgY,
                    MeasuredZ = avgZ
                };
            }

            _logger.LogDebug(
                "Position {Position} sample stability check PASSED: variance={Variance:F2}%",
                position,
                variancePercent
            );
        }

        // Validate using average values
        return ValidatePosition(position, CreateSyntheticImuData(avgX, avgY, avgZ));
    }

    #region Private Validation Methods

    /// <summary>
    /// Validate that measured gravity magnitude is within acceptable range.
    /// </summary>
    private AccelValidationResult ValidateGravityMagnitude(int position, double magnitude)
    {
        var toleranceLow = GRAVITY * (1 - GRAVITY_TOLERANCE_PERCENT / 100);
        var toleranceHigh = GRAVITY * (1 + GRAVITY_TOLERANCE_PERCENT / 100);

        if (magnitude < toleranceLow || magnitude > toleranceHigh)
        {
            var percentOff = Math.Abs((magnitude - GRAVITY) / GRAVITY * 100);

            var message =
                $"❌ Position {position} ({GetPositionName(position)}) REJECTED:\n\n" +
                $"Gravity magnitude is {percentOff:F1}% off from expected value.\n" +
                $"  Measured: {magnitude:F3} m/s²\n" +
                $"  Expected: {GRAVITY:F3} m/s² (±{GRAVITY_TOLERANCE_PERCENT:F1}%)\n" +
                $"  Valid range: {toleranceLow:F3} - {toleranceHigh:F3} m/s²\n\n" +
                $"Possible causes:\n" +
                $"  • IMU sensor malfunction or damage\n" +
                $"  • Excessive vibration during measurement\n" +
                $"  • Vehicle moved during calibration\n" +
                $"  • Incorrect raw-to-scaled conversion factor\n\n" +
                $"→ Check IMU mounting is secure and not vibrating.\n" +
                $"→ Ensure vehicle is completely stationary.\n" +
                $"→ If problem persists, IMU may be faulty.";

            return new AccelValidationResult
            {
                IsValid = false,
                ErrorMessage = message
            };
        }

        return new AccelValidationResult { IsValid = true };
    }

    /// <summary>
    /// Validate that gravity vector is correctly aligned with expected axis for this position.
    /// Uses strict thresholds to ensure accurate calibration.
    /// </summary>
    private AccelValidationResult ValidateAxisAlignment(
        int position,
        double x,
        double y,
        double z,
        double magnitude)
    {
        var absX = Math.Abs(x);
        var absY = Math.Abs(y);
        var absZ = Math.Abs(z);

        var dominantThreshold = magnitude * DOMINANT_AXIS_THRESHOLD; // 87% minimum
        var otherThreshold = magnitude * OTHER_AXIS_MAX_THRESHOLD;   // 25% maximum

        // ArduPilot NED coordinate system - Expected gravity vectors:
        // 1. LEVEL:      (0, 0, +g)  → Z ≈ +9.81 (down)
        // 2. LEFT:       (0, -g, 0)  → Y ≈ -9.81 (left side down)
        // 3. RIGHT:      (0, +g, 0)  → Y ≈ +9.81 (right side down)
        // 4. NOSE DOWN:  (+g, 0, 0)  → X ≈ +9.81 (nose down)
        // 5. NOSE UP:    (-g, 0, 0)  → X ≈ -9.81 (tail down)
        // 6. BACK:       (0, 0, -g)  → Z ≈ -9.81 (upside down)

        var (isDominantCorrect, dominantAxis, dominantValue, expectedSign) = position switch
        {
            1 => (absZ >= dominantThreshold && z > 0, "Z", absZ, "+"),  // LEVEL
            2 => (absY >= dominantThreshold && y < 0, "Y", absY, "-"),  // LEFT
            3 => (absY >= dominantThreshold && y > 0, "Y", absY, "+"),  // RIGHT
            4 => (absX >= dominantThreshold && x > 0, "X", absX, "+"),  // NOSE DOWN
            5 => (absX >= dominantThreshold && x < 0, "X", absX, "-"),  // NOSE UP
            6 => (absZ >= dominantThreshold && z < 0, "Z", absZ, "-"),  // BACK
            _ => (false, "?", 0.0, "?")
        };

        // Check dominant axis is strong enough
        if (!isDominantCorrect)
        {
            var actualDominantAxis = GetDominantAxis(absX, absY, absZ);
            var actualSign = GetActualSignForAxis(dominantAxis, x, y, z);
            var expectedAxisFull = GetExpectedAxisShort(position);

            var percentOfGravity = dominantValue / magnitude * 100;
            var requiredPercent = DOMINANT_AXIS_THRESHOLD * 100;

            var message =
                $"❌ Position {position} ({GetPositionName(position)}) INCORRECT:\n\n" +
                $"Expected: {expectedAxisFull}\n" +
                $"Measured: X={x:F3}, Y={y:F3}, Z={z:F3} m/s²\n" +
                $"Dominant: {actualDominantAxis} ({actualSign:F3} m/s²)\n\n";

            // Diagnose specific problem
            if (dominantValue < dominantThreshold)
            {
                message +=
                    $"Problem: {dominantAxis}-axis too weak\n" +
                    $"  Measured: {dominantValue:F3} m/s² ({percentOfGravity:F1}% of gravity)\n" +
                    $"  Required: ≥{dominantThreshold:F3} m/s² (≥{requiredPercent:F1}% of gravity)\n\n";
            }

            var (hasWrongSign, signValue) = position switch
            {
                1 => (z <= 0, z),
                2 => (y >= 0, y),
                3 => (y <= 0, y),
                4 => (x <= 0, x),
                5 => (x >= 0, x),
                6 => (z >= 0, z),
                _ => (false, 0.0)
            };

            if (hasWrongSign)
            {
                message +=
                    $"Problem: {dominantAxis}-axis has wrong sign\n" +
                    $"  Measured: {signValue:F3} m/s² (expected {expectedSign}{GRAVITY:F3} m/s²)\n\n";
            }

            message += GetCorrectionAdvice(position);

            return new AccelValidationResult
            {
                IsValid = false,
                ErrorMessage = message
            };
        }

        // Check that other axes are not too large (indicates tilt/misalignment)
        var (otherAxesOk, maxOtherAxis, maxOtherValue) = position switch
        {
            1 or 6 => (absX <= otherThreshold && absY <= otherThreshold,
                       absX > absY ? "X" : "Y",
                       Math.Max(absX, absY)),
            2 or 3 => (absX <= otherThreshold && absZ <= otherThreshold,
                       absX > absZ ? "X" : "Z",
                       Math.Max(absX, absZ)),
            4 or 5 => (absY <= otherThreshold && absZ <= otherThreshold,
                       absY > absZ ? "Y" : "Z",
                       Math.Max(absY, absZ)),
            _ => (false, "?", 0.0)
        };

        if (!otherAxesOk)
        {
            var percentOff = maxOtherValue / magnitude * 100;
            var maxAllowed = OTHER_AXIS_MAX_THRESHOLD * 100;

            var message =
                $"❌ Position {position} ({GetPositionName(position)}) INCORRECT:\n\n" +
                $"Dominant axis is correct, but vehicle is tilted.\n" +
                $"  Measured: X={x:F3}, Y={y:F3}, Z={z:F3} m/s²\n\n" +
                $"Problem: {maxOtherAxis}-axis shows {maxOtherValue:F3} m/s² ({percentOff:F1}% of gravity)\n" +
                $"  Maximum allowed: {otherThreshold:F3} m/s² ({maxAllowed:F1}% of gravity)\n\n" +
                $"→ Vehicle must be positioned more precisely without tilt.\n" +
                $"→ Use a level surface and ensure vehicle is stable.\n\n" +
                $"{GetCorrectionAdvice(position)}";

            return new AccelValidationResult
            {
                IsValid = false,
                ErrorMessage = message
            };
        }

        return new AccelValidationResult { IsValid = true };
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Calculate magnitude of 3D vector.
    /// </summary>
    private static double CalculateMagnitude(double x, double y, double z)
    {
        return Math.Sqrt(x * x + y * y + z * z);
    }

    /// <summary>
    /// Calculate variance of sample set.
    /// </summary>
    private static double CalculateVariance(IEnumerable<double> values, double mean)
    {
        var variance = values.Select(v => Math.Pow(v - mean, 2)).Average();
        return Math.Sqrt(variance); // Return standard deviation
    }

    /// <summary>
    /// Create synthetic IMU data from scaled acceleration values (for multi-sample average).
    /// </summary>
    private static RawImuData CreateSyntheticImuData(double x, double y, double z)
    {
        // Convert back to raw milli-g (assuming SCALED_IMU with standard conversion)
        const double MS2_TO_MILLI_G = 1000.0 / 9.80665;
        return new RawImuData
        {
            XAcc = (short)(x * MS2_TO_MILLI_G),
            YAcc = (short)(y * MS2_TO_MILLI_G),
            ZAcc = (short)(z * MS2_TO_MILLI_G),
            IsScaled = true // Mark as scaled IMU data
        };
    }

    /// <summary>
    /// Get human-readable position name.
    /// </summary>
    private static string GetPositionName(int position)
    {
        return position switch
        {
            1 => "LEVEL",
            2 => "LEFT SIDE DOWN",
            3 => "RIGHT SIDE DOWN",
            4 => "NOSE DOWN",
            5 => "NOSE UP (TAIL DOWN)",
            6 => "BACK / UPSIDE DOWN",
            _ => "UNKNOWN"
        };
    }

    /// <summary>
    /// Get expected axis description (short form).
    /// </summary>
    private static string GetExpectedAxisShort(int position)
    {
        return position switch
        {
            1 => "+Z axis (down) ≈ +9.81 m/s²",
            2 => "-Y axis (left) ≈ -9.81 m/s²",
            3 => "+Y axis (right) ≈ +9.81 m/s²",
            4 => "+X axis (forward) ≈ +9.81 m/s²",
            5 => "-X axis (backward) ≈ -9.81 m/s²",
            6 => "-Z axis (up) ≈ -9.81 m/s²",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Get dominant axis from absolute values.
    /// </summary>
    private static string GetDominantAxis(double absX, double absY, double absZ)
    {
        if (absX >= absY && absX >= absZ) return "X";
        if (absY >= absX && absY >= absZ) return "Y";
        return "Z";
    }

    /// <summary>
    /// Get actual sign value for specified axis.
    /// </summary>
    private static double GetActualSignForAxis(string axis, double x, double y, double z)
    {
        return axis switch
        {
            "X" => x,
            "Y" => y,
            "Z" => z,
            _ => 0.0
        };
    }

    /// <summary>
    /// Get position-specific correction advice.
    /// </summary>
    private static string GetCorrectionAdvice(int position)
    {
        return position switch
        {
            1 =>
                "Correction for LEVEL position:\n" +
                "  → Place vehicle completely flat on a level surface.\n" +
                "  → All four corners/legs must touch surface evenly.\n" +
                "  → Use a bubble level or smartphone level app to verify.\n" +
                "  → Ensure no rocking or wobbling.",

            2 =>
                "Correction for LEFT SIDE DOWN position:\n" +
                "  → Place vehicle on its left side (right side up).\n" +
                "  → Left side must be flush against a vertical surface.\n" +
                "  → Nose should point forward (not tilted up/down).\n" +
                "  → Use box/stand to support if needed.",

            3 =>
                "Correction for RIGHT SIDE DOWN position:\n" +
                "  → Place vehicle on its right side (left side up).\n" +
                "  → Right side must be flush against a vertical surface.\n" +
                "  → Nose should point forward (not tilted up/down).\n" +
                "  → Use box/stand to support if needed.",

            4 =>
                "Correction for NOSE DOWN position:\n" +
                "  → Tilt vehicle forward exactly 90° (nose straight down).\n" +
                "  → Tail should point straight up (perpendicular to ground).\n" +
                "  → Use stable box/stand to hold position securely.\n" +
                "  → Avoid any left/right tilt.",

            5 =>
                "Correction for NOSE UP position:\n" +
                "  → Tilt vehicle backward exactly 90° (nose straight up).\n" +
                "  → Tail should point straight down (perpendicular to ground).\n" +
                "  → Use stable box/stand to hold position securely.\n" +
                "  → Avoid any left/right tilt.",

            6 =>
                "Correction for BACK/UPSIDE DOWN position:\n" +
                "  → Flip vehicle completely upside down (180°).\n" +
                "  → Bottom facing up, top touching flat surface.\n" +
                "  → Must be perfectly flat (no forward/back or left/right tilt).\n" +
                "  → Use foam padding to protect camera/props.",

            _ => ""
        };
    }

    #endregion
}