# PDRL Configuration Guide for Accelerometer Calibration
## Pavaman Drone Research Laboratory Standards

**Version:** 1.0.0  
**Date:** January 2026  
**Status:** Production Reference

---

## Overview

This guide provides the PDRL (Pavaman Drone Research Laboratory) standards and best practices for accelerometer calibration in ArduPilot-based flight controllers. These guidelines ensure reliable calibration that aligns with Mission Planner behavior while addressing common validation failures.

## Critical Pre-Calibration Checks

### 1. Flight Controller State Validation

Before initiating accelerometer calibration, verify the following FC state requirements:

#### **Vehicle MUST Be Disarmed**
- **Requirement:** Vehicle MUST be disarmed before calibration
- **Reason:** Armed state prevents calibration commands from being accepted
- **Validation:** Check HEARTBEAT message `base_mode` field for armed bit (0x80)
- **Error Code:** If armed, FC returns `MAV_RESULT_FAILED` (4) for MAV_CMD_ACCELCAL_VEHICLE_POS

```csharp
// Check arming status from HEARTBEAT
bool isArmed = (heartbeat_base_mode & 0x80) != 0;
if (isArmed) {
    throw new InvalidOperationException(
        "Vehicle must be disarmed before calibration. " +
        "Please disarm the vehicle and try again.");
}
```

#### **Verify System Status**
- **Requirement:** System status should be STANDBY (MAV_STATE_STANDBY = 3) or ACTIVE (MAV_STATE_ACTIVE = 4)
- **Invalid States:** UNINIT (0), BOOT (1), POWEROFF (2), CRITICAL (5), EMERGENCY (6)
- **Error Handling:** Warn user if system status is not optimal

```csharp
enum MAV_STATE {
    UNINIT = 0,      // Uninitialized - FC not ready
    BOOT = 1,        // Booting - wait for completion
    STANDBY = 3,     // Ready for commands (BEST for calibration)
    ACTIVE = 4,      // Operating normally (ACCEPTABLE)
    CRITICAL = 5,    // Critical failure - DO NOT calibrate
    EMERGENCY = 6    // Emergency - DO NOT calibrate
}
```

### 2. Sensor Health Verification

#### **Check IMU Sensor Status**
- **Requirement:** IMU sensors must be healthy and initialized
- **Validation:** Monitor `SYS_STATUS` message (ID 1) for IMU health flags
- **Health Bits:**
  - Bit 3: `MAV_SYS_STATUS_SENSOR_3D_ACCEL` - 3D accelerometer
  - Bit 4: `MAV_SYS_STATUS_SENSOR_3D_GYRO` - 3D gyroscope
  - Bit 11: `MAV_SYS_STATUS_AHRS` - AHRS subsystem

```csharp
// Check sensor health from SYS_STATUS message
const uint SENSOR_3D_ACCEL = (1 << 3);   // Bit 3
const uint SENSOR_3D_GYRO = (1 << 4);    // Bit 4
const uint SENSOR_AHRS = (1 << 11);      // Bit 11

bool sensorsHealthy = 
    (sys_status.onboard_control_sensors_health & SENSOR_3D_ACCEL) != 0 &&
    (sys_status.onboard_control_sensors_health & SENSOR_3D_GYRO) != 0 &&
    (sys_status.onboard_control_sensors_health & SENSOR_AHRS) != 0;

if (!sensorsHealthy) {
    throw new InvalidOperationException(
        "IMU sensors not healthy. Check flight controller logs. " +
        "Sensors may need initialization or repair.");
}
```

#### **Check for Sensor Calibration State**
- **Parameter:** `INS_ACCOFFS_X`, `INS_ACCOFFS_Y`, `INS_ACCOFFS_Z`
- **Indication:** If all are zero, accelerometer has never been calibrated
- **Warning:** First-time calibration may require multiple attempts

### 3. Environmental Validation

#### **Vibration Check**
- **Requirement:** Vehicle must be stationary on stable surface
- **Validation:** Monitor IMU vibration levels via `VIBRATION` message (ID 241)
- **Thresholds:**
  - `vibration_x`, `vibration_y`, `vibration_z` < 30 m/s/s
  - `clipping_0`, `clipping_1`, `clipping_2` = 0 (no sensor saturation)

```csharp
// Check vibration levels before calibration
if (vibration.vibration_x > 30 || 
    vibration.vibration_y > 30 || 
    vibration.vibration_z > 30) {
    throw new InvalidOperationException(
        "Excessive vibration detected. Place vehicle on stable surface " +
        "away from vibration sources (fans, motors, etc.).");
}

if (vibration.clipping_0 > 0 || 
    vibration.clipping_1 > 0 || 
    vibration.clipping_2 > 0) {
    throw new InvalidOperationException(
        "IMU sensor clipping detected. Reduce vibration levels " +
        "or vehicle may have hardware issues.");
}
```

#### **Temperature Stability**
- **Recommendation:** Wait for IMU temperature to stabilize after power-on
- **Typical Stabilization:** 2-5 minutes after boot
- **Parameter:** `IMU_TEMP` from `RAW_IMU` or `SCALED_IMU` messages
- **Best Practice:** Calibrate when temperature change < 1°C/minute

---

## MAV_CMD_PREFLIGHT_CALIBRATION Initialization Sequence

### Proper Command Sequence

The FC requires a specific initialization sequence for successful calibration:

#### **Step 1: Send MAV_CMD_PREFLIGHT_CALIBRATION**

```csharp
// MAV_CMD_PREFLIGHT_CALIBRATION (241)
// param5 = 1: Simple accelerometer calibration (LEVEL only)
// param5 = 4: Full 6-position calibration (RECOMMENDED)

SendCommandLong(
    command: 241,                    // MAV_CMD_PREFLIGHT_CALIBRATION
    param1: 0,                       // Gyro (0 = skip)
    param2: 0,                       // Mag (0 = skip)
    param3: 0,                       // Ground pressure (0 = skip)
    param4: 0,                       // Airspeed (0 = skip)
    param5: 4,                       // Accel (4 = full 6-axis)
    param6: 0,                       // Reserved
    param7: 0);                      // Reserved
```

#### **Step 2: Wait for COMMAND_ACK**

```csharp
// Expected COMMAND_ACK responses:
enum MAV_RESULT {
    ACCEPTED = 0,              // Command accepted (proceed to next step)
    TEMPORARILY_REJECTED = 1,  // Busy - retry after delay
    DENIED = 2,                // Rejected - check preconditions
    UNSUPPORTED = 3,           // Command not supported
    FAILED = 4,                // Execution failed
    IN_PROGRESS = 5,           // Command being executed
    CANCELLED = 6              // Command cancelled
}

// Handle COMMAND_ACK
if (result == MAV_RESULT.ACCEPTED || result == MAV_RESULT.IN_PROGRESS) {
    // Proceed to wait for STATUSTEXT
    _logger.LogInformation("Calibration command accepted by FC");
}
else if (result == MAV_RESULT.TEMPORARILY_REJECTED) {
    // FC is busy - wait 1 second and retry
    await Task.Delay(1000);
    RetryCommand();
}
else if (result == MAV_RESULT.DENIED) {
    // Check preconditions (armed, sensor health, etc.)
    throw new InvalidOperationException(
        "FC denied calibration. Verify: " +
        "1) Vehicle is disarmed, " +
        "2) Sensors are healthy, " +
        "3) Previous calibration completed");
}
else if (result == MAV_RESULT.FAILED) {
    // Calibration failed - check FC logs
    throw new InvalidOperationException(
        "FC reported calibration failed. Check FC console for details.");
}
```

#### **Step 3: Wait for FC STATUSTEXT Request**

```csharp
// FC will send STATUSTEXT when ready for first position
// Example: "Place vehicle level and press any key"
// Keyword detection: "level", "place", "position"

// CRITICAL: Do NOT proceed until FC sends position request
// Timeout: 10 seconds (if no STATUSTEXT, calibration initialization failed)
```

---

## MAV_CMD_ACCELCAL_VEHICLE_POS Position Validation

### Position Parameter Mapping

**CRITICAL:** ArduPilot expects `param1 = 0..5` (zero-indexed), NOT 1..6

```csharp
// UI/Internal positions: 1-6
// MAVLink positions: 0-5
// Mapping: mavlink_position = ui_position - 1

enum AccelPosition {
    LEVEL = 0,        // Flat on ground (Z-axis down)
    LEFT = 1,         // Left side down (Y-axis down)
    RIGHT = 2,        // Right side down (Y-axis up)
    NOSE_DOWN = 3,    // Nose down 90° (X-axis down)
    NOSE_UP = 4,      // Nose up 90° (X-axis up)
    BACK = 5          // Upside down (Z-axis up)
}
```

### Common Validation Failures and Solutions

#### **Issue 1: MAV_RESULT_FAILED for Position 0 (LEVEL)**

**Root Causes:**
1. **Vehicle is Armed** - MUST disarm before calibration
2. **Insufficient Settle Delay** - FC not ready to accept position command
3. **Previous Calibration Not Completed** - FC still processing old calibration
4. **IMU Data Not Stable** - Vibration or movement detected

**Solutions:**

```csharp
// Solution 1: Verify vehicle is disarmed
if (IsArmed()) {
    throw new InvalidOperationException(
        "CRITICAL: Vehicle is armed. Disarm vehicle before calibration. " +
        "Arming prevents calibration for safety reasons.");
}

// Solution 2: Implement mandatory 2-second settle delay
// After FC sends "Place vehicle level" STATUSTEXT:
// 1. Wait 2000ms (MANDATORY - ArduPilot internal requirement)
// 2. Enable confirm button
// 3. User confirms position
// 4. Send MAV_CMD_ACCELCAL_VEHICLE_POS

async Task WaitForSettleDelay() {
    _logger.LogInformation("Waiting 2000ms settle delay (ArduPilot requirement)");
    await Task.Delay(2000);  // CRITICAL: Do not reduce this delay
    EnableConfirmButton();
}

// Solution 3: Clear any previous calibration state
// Send MAV_CMD_PREFLIGHT_CALIBRATION with param5=0 to cancel
SendCommandLong(command: 241, param5: 0);  // Cancel any active calibration
await Task.Delay(500);  // Wait for FC to process cancellation
SendCommandLong(command: 241, param5: 4);  // Start fresh calibration

// Solution 4: Validate IMU stability before sending position
bool IsImuStable() {
    // Collect 50 samples @ 50Hz (1 second)
    var samples = CollectImuSamples(count: 50, intervalMs: 20);
    
    // Calculate variance
    var variance = CalculateVariance(samples);
    
    // Threshold: Standard deviation < 0.5 m/s² on each axis
    return variance.x < 0.25 && variance.y < 0.25 && variance.z < 0.25;
}
```

#### **Issue 2: Position Rejected After Being Accepted**

**Root Cause:** User moved vehicle during FC sampling

**Solution:**
```csharp
// After COMMAND_ACK(ACCEPTED) for position command:
// 1. FC begins sampling IMU data (typically 2-4 seconds)
// 2. Vehicle MUST remain completely still
// 3. Monitor for "sampling complete" or "position accepted" STATUSTEXT

// Display clear instruction to user:
DisplayMessage("HOLD STILL! FC is sampling IMU data. " +
               "Do not move vehicle for 4 seconds.");

// Detect sampling completion via STATUSTEXT keywords:
// "complete", "accepted", "got", "next position"
```

#### **Issue 3: Calibration Timeout After Multiple Positions**

**Root Cause:** FC expects all 6 positions but UI stopped sending

**Solution:**
```csharp
// Track calibration progress
int positionsCompleted = 0;
const int TOTAL_POSITIONS = 6;

void OnCommandAck(ushort command, byte result) {
    if (command == 42429 && result == MAV_RESULT.ACCEPTED) {
        positionsCompleted++;
        _logger.LogInformation(
            "Position {Current}/{Total} accepted by FC", 
            positionsCompleted, TOTAL_POSITIONS);
        
        // Do NOT assume calibration is complete
        // Wait for FC to request next position OR send "successful" STATUSTEXT
    }
}

// Only mark complete when FC sends:
// "Calibration successful" or "Calibration complete"
```

---

## Enhanced Error Diagnostics

### COMMAND_ACK Result Code Mapping

Provide detailed explanations for each result code:

```csharp
string GetCommandAckExplanation(byte command, byte result) {
    if (command == 241) {  // MAV_CMD_PREFLIGHT_CALIBRATION
        return result switch {
            0 => "Calibration started successfully. Waiting for FC position request.",
            1 => "FC is busy. Previous calibration may still be active. Wait 5 seconds and retry.",
            2 => "Calibration denied. Verify: (1) Vehicle disarmed, (2) Sensors healthy, (3) No other calibration running.",
            3 => "Calibration not supported. Check FC firmware version (requires ArduPilot 3.6+).",
            4 => "Calibration failed to start. Check FC logs for details. May indicate sensor hardware failure.",
            5 => "Calibration in progress. Wait for FC STATUSTEXT messages.",
            6 => "Calibration cancelled by user or timeout.",
            _ => $"Unknown result code: {result}"
        };
    }
    else if (command == 42429) {  // MAV_CMD_ACCELCAL_VEHICLE_POS
        return result switch {
            0 => "Position accepted. FC is sampling IMU data. HOLD VEHICLE STILL for 4 seconds.",
            1 => "Position temporarily rejected. FC is busy sampling previous position. Wait 2 seconds and retry.",
            2 => "Position denied. Possible causes: (1) Vehicle moved during sampling, (2) IMU data invalid, (3) Incorrect orientation.",
            4 => "Position validation FAILED. Possible causes:\n" +
                 "  • Vehicle is ARMED (MUST disarm before calibration)\n" +
                 "  • Position command sent too soon (wait 2 seconds after STATUSTEXT)\n" +
                 "  • Excessive vibration or movement detected\n" +
                 "  • Previous position not yet sampled completely\n" +
                 "  • IMU sensor malfunction\n" +
                 "Action: Verify vehicle is disarmed, on stable surface, and completely still.",
            _ => $"Unknown result code: {result}"
        };
    }
    return $"Result code {result} for command {command}";
}
```

### User-Friendly Error Messages

```csharp
void DisplayCalibrationError(byte result, string fcMessage) {
    var title = "Accelerometer Calibration Issue";
    var message = "";
    var actions = "";
    
    if (result == 4) {  // MAV_RESULT_FAILED
        message = "The flight controller rejected the calibration position.\n\n";
        message += "Most common causes:\n";
        message += "1. Vehicle is ARMED - You must disarm the vehicle\n";
        message += "2. Command sent too quickly - FC needs 2 seconds to prepare\n";
        message += "3. Vehicle moved or vibrating - Place on stable surface\n\n";
        
        actions = "Recommended actions:\n";
        actions += "• Check vehicle is completely disarmed\n";
        actions += "• Place vehicle on solid, level surface\n";
        actions += "• Ensure no vibration (turn off fans, remove from vehicle)\n";
        actions += "• Wait 5 seconds, then restart calibration\n";
        actions += "• If problem persists, check FC console logs\n\n";
        actions += $"FC Message: {fcMessage}";
    }
    
    ShowErrorDialog(title, message + actions);
}
```

---

## Retry Logic with Exponential Backoff

Implement intelligent retry for transient failures:

```csharp
async Task<bool> SendPositionWithRetry(int position, int maxRetries = 3) {
    int attempt = 0;
    int delayMs = 1000;  // Start with 1 second
    
    while (attempt < maxRetries) {
        attempt++;
        _logger.LogInformation(
            "Sending position {Pos} (attempt {Attempt}/{Max})", 
            position, attempt, maxRetries);
        
        var result = await SendAccelCalVehiclePos(position);
        
        if (result == MAV_RESULT.ACCEPTED) {
            return true;  // Success
        }
        else if (result == MAV_RESULT.TEMPORARILY_REJECTED) {
            // FC is busy - wait and retry
            _logger.LogWarning(
                "Position {Pos} temporarily rejected. " +
                "Waiting {Delay}ms before retry {Attempt}/{Max}", 
                position, delayMs, attempt, maxRetries);
            
            await Task.Delay(delayMs);
            delayMs *= 2;  // Exponential backoff (1s, 2s, 4s)
        }
        else if (result == MAV_RESULT.DENIED || result == MAV_RESULT.FAILED) {
            // Permanent failure - do not retry
            _logger.LogError(
                "Position {Pos} rejected by FC (result={Result}). " +
                "Check preconditions and FC logs.", 
                position, result);
            return false;
        }
    }
    
    _logger.LogError(
        "Position {Pos} failed after {Attempts} attempts", 
        position, maxRetries);
    return false;
}
```

---

## Complete Validation Workflow

### Pre-Calibration Checklist

```markdown
Before starting accelerometer calibration:

☑ Vehicle Status
  ☐ Vehicle is completely disarmed
  ☐ Motors are not running
  ☐ Vehicle is on stable, level surface
  ☐ No vibration sources nearby

☑ Flight Controller Status  
  ☐ FC is fully booted (wait 30 seconds after power-on)
  ☐ System status is STANDBY or ACTIVE
  ☐ No errors in FC console

☑ Sensor Status
  ☐ IMU sensors are healthy (check SYS_STATUS)
  ☐ No sensor calibration currently running
  ☐ Temperature has stabilized (2-5 min after boot)
  ☐ Vibration levels < 30 m/s/s

☑ Connection Status
  ☐ MAVLink connection is stable
  ☐ HEARTBEAT received regularly (every 1 second)
  ☐ No connection timeouts or errors

☑ Environment
  ☐ Indoor location (no wind)
  ☐ Level surface verified (use spirit level if available)
  ☐ No movement or disturbances expected
```

### Step-by-Step Calibration Procedure

```csharp
async Task PerformAccelCalibration() {
    // Phase 1: Pre-flight checks
    await ValidatePreConditions();
    
    // Phase 2: Initialize calibration
    await InitializeCalibration();
    
    // Phase 3: Position confirmation loop
    for (int pos = 0; pos < 6; pos++) {
        await WaitForFcPositionRequest(pos);
        await WaitForSettleDelay(2000);  // MANDATORY
        await WaitForUserConfirmation();
        await SendPositionCommand(pos);
        await WaitForSamplingComplete();
    }
    
    // Phase 4: Completion
    await WaitForCalibrationSuccess();
    await RebootFc();  // RECOMMENDED
}

async Task ValidatePreConditions() {
    // 1. Check arming status
    if (IsArmed()) {
        throw new InvalidOperationException(
            "Vehicle must be disarmed. " +
            "Disarm vehicle and wait 2 seconds before calibration.");
    }
    
    // 2. Check sensor health
    var sysStatus = await GetSysStatus(timeout: 5000);
    if (!AreSensorsHealthy(sysStatus)) {
        throw new InvalidOperationException(
            "IMU sensors not healthy. Check FC for sensor errors.");
    }
    
    // 3. Check vibration
    var vibration = await GetVibration(timeout: 5000);
    if (!IsVibrationLevelAcceptable(vibration)) {
        throw new InvalidOperationException(
            "Excessive vibration detected. " +
            "Place vehicle on stable surface.");
    }
    
    // 4. Check for active calibration
    if (IsCalibrationActive()) {
        _logger.LogWarning("Previous calibration active. Cancelling...");
        await CancelActiveCalibration();
        await Task.Delay(1000);
    }
}

async Task InitializeCalibration() {
    _logger.LogInformation("Sending MAV_CMD_PREFLIGHT_CALIBRATION (param5=4)");
    
    var result = await SendCommandLong(
        command: 241,
        param5: 4);  // Full 6-axis calibration
    
    if (result != MAV_RESULT.ACCEPTED && result != MAV_RESULT.IN_PROGRESS) {
        var explanation = GetCommandAckExplanation(241, result);
        throw new InvalidOperationException(
            $"Calibration initialization failed: {explanation}");
    }
    
    _logger.LogInformation("Calibration initialized. Waiting for FC position request...");
}
```

---

## Mission Planner Compatibility

### Behavior Alignment

This implementation matches Mission Planner 1.3.x behavior:

1. **FC-Driven Workflow:** FC requests positions via STATUSTEXT
2. **Mandatory 2-Second Settle Delay:** After each STATUSTEXT position request
3. **Zero-Indexed Positions:** MAVLink param1 = 0..5 (not 1..6)
4. **No Auto-Completion:** Wait for FC "Calibration successful" message
5. **Position Validation:** FC validates orientation internally (no client-side checks)

### Testing Against Mission Planner

To verify compatibility:

```bash
# 1. Perform calibration with Mission Planner
#    - Record all MAVLink messages (use MAVLink Inspector)
#    - Note timing of COMMAND_LONG and COMMAND_ACK
#    - Record all STATUSTEXT messages

# 2. Perform calibration with Pavaman Configurator
#    - Enable MAVLink logging
#    - Compare message sequences

# 3. Verify identical behavior:
#    ✓ Same MAV_CMD_PREFLIGHT_CALIBRATION parameters
#    ✓ Same MAV_CMD_ACCELCAL_VEHICLE_POS position values (0-5)
#    ✓ Same timing (2-second settle delay)
#    ✓ Same STATUSTEXT parsing
#    ✓ Same completion detection
```

---

## Troubleshooting Guide

### Problem: "Position 0 validation FAILED"

**Symptoms:**
- MAV_CMD_ACCELCAL_VEHICLE_POS returns MAV_RESULT_FAILED (4)
- FC message: "Invalid position" or no message

**Diagnostic Steps:**
1. Check arming status: `MAVLink Inspector > HEARTBEAT > base_mode`
2. Check sensor health: `MAVLink Inspector > SYS_STATUS > onboard_control_sensors_health`
3. Check vibration: `MAVLink Inspector > VIBRATION`
4. Enable FC console logging: `LOG_DISARMED=1`, reboot, check messages

**Common Solutions:**
- **Disarm vehicle:** Most common cause of FAILED result
- **Increase settle delay:** Try 3000ms instead of 2000ms
- **Check surface stability:** Use spirit level, solid table
- **Cancel previous calibration:** Send MAV_CMD_PREFLIGHT_CALIBRATION(param5=0)
- **Update firmware:** Ensure ArduPilot 4.0+ (older versions may have bugs)

### Problem: "Calibration times out after 60 seconds"

**Symptoms:**
- FC doesn't send "Calibration successful" after all positions
- Calibration appears stuck

**Diagnostic Steps:**
1. Check FC console for errors
2. Verify all 6 positions were accepted (check COMMAND_ACK)
3. Check for STATUSTEXT messages from FC

**Common Solutions:**
- **Wait longer:** Some FC variants take 90+ seconds
- **Restart calibration:** Cancel and start fresh
- **Check FC logs:** May reveal sensor errors
- **Update firmware:** Older versions may have timeout bugs

---

## Reference Implementation

### Complete Example

```csharp
public class PdrlAccelCalibrationService {
    private readonly IConnectionService _connection;
    private readonly ILogger _logger;
    
    public async Task<bool> CalibrateAccelerometer() {
        try {
            // STEP 1: Validate preconditions
            _logger.LogInformation("STEP 1: Validating preconditions...");
            ValidateArmed();
            await ValidateSensorHealth();
            await ValidateVibration();
            
            // STEP 2: Initialize calibration
            _logger.LogInformation("STEP 2: Initializing calibration...");
            await SendPreflightCalibration();
            
            // STEP 3: Wait for FC ready
            _logger.LogInformation("STEP 3: Waiting for FC position request...");
            await WaitForFirstPositionRequest(timeout: 10000);
            
            // STEP 4: Process all 6 positions
            for (int pos = 0; pos < 6; pos++) {
                _logger.LogInformation($"STEP {4+pos}: Processing position {pos} ({GetPositionName(pos)})");
                
                // 4a. Wait for settle delay (MANDATORY)
                await Task.Delay(2000);
                
                // 4b. Wait for user to position vehicle
                await WaitForUserConfirmation();
                
                // 4c. Send position to FC
                var result = await SendPosition(pos);
                if (result != MAV_RESULT.ACCEPTED) {
                    throw new CalibrationException(
                        $"Position {pos} rejected: {GetCommandAckExplanation(42429, result)}");
                }
                
                // 4d. Wait for FC to complete sampling
                await WaitForSamplingComplete(timeout: 10000);
            }
            
            // STEP 10: Wait for calibration success
            _logger.LogInformation("STEP 10: Waiting for calibration completion...");
            await WaitForCalibrationSuccess(timeout: 20000);
            
            _logger.LogInformation("Accelerometer calibration completed successfully!");
            return true;
        }
        catch (Exception ex) {
            _logger.LogError(ex, "Accelerometer calibration failed");
            DisplayUserFriendlyError(ex);
            return false;
        }
    }
}
```

---

## Conclusion

Following the PDRL configuration guidelines ensures reliable accelerometer calibration that aligns with Mission Planner behavior and prevents common validation failures.

**Key Takeaways:**
1. **ALWAYS verify vehicle is disarmed** - Most common cause of FAILED
2. **Implement 2-second settle delay** - ArduPilot requirement
3. **Use zero-indexed positions (0-5)** - MAVLink standard
4. **Validate preconditions before starting** - Prevents most failures
5. **Provide detailed error messages** - Helps users troubleshoot issues

---

**Document Version:** 1.0.0  
**Last Updated:** January 2026  
**Author:** Pavaman Drone Research Laboratory  
**Status:** ✅ Production Reference
