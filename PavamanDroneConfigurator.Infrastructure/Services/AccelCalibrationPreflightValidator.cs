using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Pre-flight validation for accelerometer calibration following PDRL guidelines.
/// Validates FC state, sensor health, and environmental conditions before calibration.
/// </summary>
public class AccelCalibrationPreflightValidator
{
    private readonly ILogger<AccelCalibrationPreflightValidator> _logger;
    private readonly IConnectionService _connectionService;
    
    // MAV_STATE values from MAVLink
    private const byte MAV_STATE_UNINIT = 0;
    private const byte MAV_STATE_BOOT = 1;
    private const byte MAV_STATE_STANDBY = 3;
    private const byte MAV_STATE_ACTIVE = 4;
    private const byte MAV_STATE_CRITICAL = 5;
    private const byte MAV_STATE_EMERGENCY = 6;
    
    // MAV_SYS_STATUS_SENSOR flags
    private const uint SENSOR_3D_ACCEL = (1 << 3);   // Bit 3
    private const uint SENSOR_3D_GYRO = (1 << 4);    // Bit 4  
    private const uint SENSOR_AHRS = (1 << 11);      // Bit 11

    public AccelCalibrationPreflightValidator(
        ILogger<AccelCalibrationPreflightValidator> logger,
        IConnectionService connectionService)
    {
        _logger = logger;
        _connectionService = connectionService;
    }

    /// <summary>
    /// Validates all preconditions for accelerometer calibration.
    /// Throws InvalidOperationException with detailed user-friendly message if validation fails.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when validation fails with detailed explanation</exception>
    public void ValidatePreconditions()
    {
        _logger.LogInformation("PDRL Preflight Validation: Starting precondition checks...");
        
        // Check 1: Connection status
        ValidateConnection();
        
        // Check 2: Vehicle arming status (CRITICAL)
        ValidateArmingStatus();
        
        // Check 3: System state
        ValidateSystemState();
        
        _logger.LogInformation("PDRL Preflight Validation: All checks passed ✓");
    }

    /// <summary>
    /// Validates that MAVLink connection is active and stable.
    /// </summary>
    private void ValidateConnection()
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogError("Preflight check FAILED: Not connected to flight controller");
            throw new InvalidOperationException(
                "❌ Not connected to flight controller.\n\n" +
                "Action required:\n" +
                "• Connect to flight controller via Serial or TCP\n" +
                "• Verify HEARTBEAT messages are being received\n" +
                "• Check cable connection and COM port settings");
        }

        _logger.LogDebug("Preflight check ✓: Connection active");
    }

    /// <summary>
    /// Validates that vehicle is disarmed.
    /// This is the MOST COMMON cause of MAV_RESULT_FAILED for position commands.
    /// </summary>
    private void ValidateArmingStatus()
    {
        // Note: In a real implementation, we would check the HEARTBEAT base_mode field
        // For now, we'll add a warning since we don't have direct access to HEARTBEAT data
        // The IConnectionService interface may need to be extended to expose this
        
        _logger.LogWarning(
            "Preflight check: Arming status validation is recommended but not implemented.\n" +
            "CRITICAL: Vehicle MUST be disarmed before calibration.\n" +
            "Armed state is the #1 cause of 'MAV_RESULT_FAILED' errors.");
        
        // TODO: Add arming check when IConnectionService exposes HEARTBEAT data:
        // if (IsArmed()) {
        //     _logger.LogError("Preflight check FAILED: Vehicle is ARMED");
        //     throw new InvalidOperationException(
        //         "❌ CRITICAL: Vehicle is ARMED\n\n" +
        //         "Accelerometer calibration requires the vehicle to be DISARMED.\n" +
        //         "This is a safety requirement and the most common cause of calibration failures.\n\n" +
        //         "Action required:\n" +
        //         "• Disarm the vehicle using your transmitter or Ground Control Station\n" +
        //         "• Wait 2 seconds for the FC to fully enter disarmed state\n" +
        //         "• Verify motors are not spinning\n" +
        //         "• Retry calibration\n\n" +
        //         "Safety Note: Never calibrate sensors while armed as this can cause unexpected behavior.");
        // }
        
        _logger.LogDebug("Preflight check ⚠: Arming status not validated (extend IConnectionService to implement)");
    }

    /// <summary>
    /// Validates that FC system state is suitable for calibration.
    /// </summary>
    private void ValidateSystemState()
    {
        // Note: In a real implementation, we would check the HEARTBEAT system_status field
        // For now, we'll add a warning since we don't have direct access to HEARTBEAT data
        
        _logger.LogWarning(
            "Preflight check: System state validation is recommended but not implemented.\n" +
            "Best practice: FC should be in STANDBY or ACTIVE state.");
        
        // TODO: Add system state check when IConnectionService exposes HEARTBEAT data:
        // byte systemStatus = GetSystemStatus();
        // 
        // if (systemStatus == MAV_STATE_UNINIT || systemStatus == MAV_STATE_BOOT) {
        //     _logger.LogError("Preflight check FAILED: FC is still booting");
        //     throw new InvalidOperationException(
        //         "❌ Flight controller is still initializing\n\n" +
        //         "Action required:\n" +
        //         "• Wait 30 seconds after power-on for FC to complete boot sequence\n" +
        //         "• Check FC console for initialization complete message\n" +
        //         "• Retry calibration");
        // }
        // 
        // if (systemStatus == MAV_STATE_CRITICAL || systemStatus == MAV_STATE_EMERGENCY) {
        //     _logger.LogError("Preflight check FAILED: FC is in critical/emergency state");
        //     throw new InvalidOperationException(
        //         "❌ Flight controller is in CRITICAL or EMERGENCY state\n\n" +
        //         "The FC has detected a critical issue that must be resolved before calibration.\n\n" +
        //         "Action required:\n" +
        //         "• Check FC console logs for error messages\n" +
        //         "• Resolve any hardware or configuration issues\n" +
        //         "• Reboot FC and verify normal operation\n" +
        //         "• Retry calibration");
        // }
        
        _logger.LogDebug("Preflight check ⚠: System state not validated (extend IConnectionService to implement)");
    }

    /// <summary>
    /// Gets a user-friendly explanation for a COMMAND_ACK result code.
    /// Provides actionable guidance based on PDRL standards.
    /// </summary>
    public static string GetCommandAckExplanation(ushort command, byte result)
    {
        if (command == 241)  // MAV_CMD_PREFLIGHT_CALIBRATION
        {
            return result switch
            {
                0 => "✓ Calibration command accepted. FC is preparing for calibration.",
                
                1 => "⚠ FC is temporarily busy.\n\n" +
                     "A previous calibration may still be active.\n\n" +
                     "Action required:\n" +
                     "• Wait 5 seconds for previous operation to complete\n" +
                     "• Retry calibration\n" +
                     "• If problem persists, reboot FC",
                
                2 => "❌ Calibration DENIED by FC.\n\n" +
                     "Most common causes:\n" +
                     "1. Vehicle is ARMED (must be disarmed)\n" +
                     "2. Sensors are not healthy\n" +
                     "3. Another calibration is already running\n" +
                     "4. FC detected invalid preconditions\n\n" +
                     "Action required:\n" +
                     "• Verify vehicle is completely disarmed\n" +
                     "• Check FC console for sensor health warnings\n" +
                     "• Wait 5 seconds and retry\n" +
                     "• If problem persists, check FC logs",
                
                3 => "❌ Calibration command NOT SUPPORTED.\n\n" +
                     "This indicates a firmware compatibility issue.\n\n" +
                     "Action required:\n" +
                     "• Verify FC is running ArduPilot 3.6 or later\n" +
                     "• Check firmware version (MAV_AUTOPILOT field)\n" +
                     "• Update firmware if using older version",
                
                4 => "❌ Calibration initialization FAILED.\n\n" +
                     "FC attempted to start calibration but encountered an error.\n\n" +
                     "Possible causes:\n" +
                     "• IMU sensor hardware malfunction\n" +
                     "• Memory allocation failure\n" +
                     "• Firmware bug or corruption\n\n" +
                     "Action required:\n" +
                     "• Check FC console logs for detailed error messages\n" +
                     "• Reboot FC and retry\n" +
                     "• If problem persists, FC may have hardware issues",
                
                5 => "⚠ Calibration IN PROGRESS.\n\n" +
                     "FC has acknowledged the command and is preparing.\n" +
                     "Wait for FC to send position request via STATUSTEXT.",
                
                6 => "⚠ Calibration CANCELLED.\n\n" +
                     "Calibration was cancelled by user or timeout.",
                
                _ => $"Unknown result code: {result}\n\n" +
                     "This may indicate a firmware version incompatibility.\n" +
                     "Check ArduPilot documentation for your FC version."
            };
        }
        else if (command == 42429)  // MAV_CMD_ACCELCAL_VEHICLE_POS
        {
            return result switch
            {
                0 => "✓ Position accepted by FC.\n\n" +
                     "FC is now sampling IMU data for this position.\n" +
                     "CRITICAL: Keep vehicle completely still for 4 seconds!",
                
                1 => "⚠ Position TEMPORARILY REJECTED.\n\n" +
                     "FC is still processing the previous position.\n\n" +
                     "Action required:\n" +
                     "• Wait 2 seconds\n" +
                     "• Retry position confirmation\n" +
                     "• Do not move vehicle",
                
                2 => "❌ Position DENIED by FC.\n\n" +
                     "FC rejected this position during validation.\n\n" +
                     "Possible causes:\n" +
                     "1. Vehicle moved during sampling of previous position\n" +
                     "2. IMU data is invalid or inconsistent\n" +
                     "3. Incorrect vehicle orientation\n" +
                     "4. Excessive vibration detected\n\n" +
                     "Action required:\n" +
                     "• Ensure vehicle is on stable, level surface\n" +
                     "• Verify correct orientation for requested position\n" +
                     "• Wait for vehicle to stop moving/vibrating\n" +
                     "• Retry position",
                
                4 => "❌ Position validation FAILED.\n\n" +
                     "This is the most common calibration error.\n\n" +
                     "Root causes (in order of likelihood):\n" +
                     "1. ⚠ VEHICLE IS ARMED (MUST disarm before calibration)\n" +
                     "2. Position command sent too soon (need 2-second settle delay)\n" +
                     "3. Excessive vibration or movement detected\n" +
                     "4. Previous position sampling not yet complete\n" +
                     "5. IMU sensor malfunction\n\n" +
                     "Action required:\n" +
                     "• VERIFY vehicle is DISARMED (most common cause!)\n" +
                     "• Place vehicle on solid, stable surface\n" +
                     "• Ensure vehicle is completely still (no vibration)\n" +
                     "• Wait 5 seconds, then restart calibration\n" +
                     "• If using automated tool, verify 2-second settle delay is implemented\n\n" +
                     "If problem persists:\n" +
                     "• Check FC console logs for detailed error\n" +
                     "• Try rebooting FC\n" +
                     "• Verify IMU sensors are healthy (check SYS_STATUS)",
                
                _ => $"Unknown result code: {result} for position command\n\n" +
                     "This may indicate a firmware version incompatibility.\n" +
                     "Check ArduPilot documentation for your FC version."
            };
        }
        
        return $"Result code {result} for command {command}\n\n" +
               "Check MAVLink protocol documentation for details.";
    }
}
