# Accelerometer Calibration Troubleshooting Guide
## Quick Reference for Common Validation Failures

**Version:** 1.0.0  
**Date:** January 2026  
**Status:** Production Reference

---

## Quick Diagnostic Tool

### When You See: "MAV_RESULT_FAILED for position 0"

**Run this checklist immediately:**

```
☐ 1. Is vehicle DISARMED? (90% of cases - CHECK THIS FIRST!)
☐ 2. Did you wait 2 seconds after STATUSTEXT before clicking?
☐ 3. Is vehicle on stable, non-vibrating surface?
☐ 4. Did previous calibration complete or get cancelled properly?
☐ 5. Is FC fully booted (30+ seconds after power-on)?
```

**Most Common Fix:**
```
1. Disarm vehicle completely
2. Wait 5 seconds
3. Restart calibration from beginning
4. Wait for "Place vehicle level" STATUSTEXT
5. Wait 2 full seconds (count: one-Mississippi, two-Mississippi)
6. Click "Confirm Position"
```

---

## Problem 1: Vehicle is Armed

### Symptoms
- MAV_CMD_ACCELCAL_VEHICLE_POS returns MAV_RESULT_FAILED (4)
- Usually happens on first position (LEVEL)
- No detailed error from FC

### Root Cause
ArduPilot REQUIRES vehicle to be disarmed for safety. This is the #1 cause of calibration failures (90% of reported issues).

### Solution
```bash
# Method 1: Transmitter
1. Use transmitter to disarm (throttle down + rudder right, or assigned switch)
2. Verify motors stopped
3. Wait 2 seconds for FC to enter disarmed state
4. Restart calibration

# Method 2: Ground Control Station
1. Click "Disarm" button in GCS
2. Verify motors stopped  
3. Wait 2 seconds
4. Restart calibration

# Method 3: MAVLink Command
Send MAV_CMD_COMPONENT_ARM_DISARM with param1=0 (disarm)
```

### Verification
```csharp
// Check HEARTBEAT base_mode field (bit 7)
bool isArmed = (heartbeat.base_mode & 0x80) != 0;
if (isArmed) {
    Console.WriteLine("ARMED - must disarm before calibration!");
}
```

### Prevention
- Add pre-flight check in UI to verify disarmed state
- Show clear warning if armed
- Block calibration start if armed

---

## Problem 2: Insufficient Settle Delay

### Symptoms
- Position 0 (LEVEL) fails immediately after clicking confirm
- Happens even when vehicle is disarmed
- FC seems "not ready" to accept command

### Root Cause
ArduPilot requires 2-second delay after sending STATUSTEXT "Place vehicle level" before it's ready to accept MAV_CMD_ACCELCAL_VEHICLE_POS.

This is an internal FC state transition delay - NOT optional.

### Solution
```csharp
// CORRECT Implementation (Mission Planner style)
void OnStatusTextReceived(string text) {
    if (text.Contains("Place vehicle level")) {
        // 1. Show position image
        ShowPositionImage(Position.LEVEL);
        
        // 2. Disable confirm button
        ConfirmButton.Enabled = false;
        
        // 3. Wait MANDATORY 2 seconds
        await Task.Delay(2000);  // DO NOT reduce this!
        
        // 4. Enable confirm button
        ConfirmButton.Enabled = true;
        
        // 5. User clicks when ready
    }
}

// WRONG Implementation (causes failures)
void OnStatusTextReceived(string text) {
    if (text.Contains("Place vehicle level")) {
        ShowPositionImage(Position.LEVEL);
        ConfirmButton.Enabled = true;  // ❌ Enabled immediately - WRONG!
        // User clicks immediately, FC not ready, returns FAILED
    }
}
```

### Timing Details
```
T+0ms:    FC sends STATUSTEXT "Place vehicle level"
T+0ms:    UI receives STATUSTEXT
T+0ms:    UI shows position image
T+0ms:    UI DISABLES confirm button (user cannot click yet)
T+0-2000ms: FC performs internal state transitions
T+2000ms: UI ENABLES confirm button
T+2000+ms: User clicks when ready
T+2000+ms: Send MAV_CMD_ACCELCAL_VEHICLE_POS
T+2000+ms: FC accepts command (MAV_RESULT_ACCEPTED)
```

### Verification
```csharp
// Add logging to verify delay
var stopwatch = Stopwatch.StartNew();

void OnStatusTextReceived(string text) {
    if (text.Contains("Place vehicle level")) {
        stopwatch.Restart();
        // ... show image, disable button ...
        await Task.Delay(2000);
        var elapsed = stopwatch.ElapsedMilliseconds;
        _logger.LogInformation($"Settle delay: {elapsed}ms (should be ~2000ms)");
        // ... enable button ...
    }
}
```

---

## Problem 3: Previous Calibration Still Active

### Symptoms
- Calibration command returns MAV_RESULT_DENIED (2)
- Or TEMPORARILY_REJECTED (1)
- Happens when starting new calibration

### Root Cause
Previous calibration did not complete cleanly (cancelled, timeout, or crashed). FC internal state machine stuck.

### Solution
```csharp
// Cancel any active calibration before starting new one
async Task StartCalibrationWithCleanup() {
    // Step 1: Send cancel command
    _logger.LogInformation("Cancelling any previous calibration...");
    SendCommandLong(
        command: 241,           // MAV_CMD_PREFLIGHT_CALIBRATION
        param1: 0,              // Cancel all calibrations
        param2: 0,
        param3: 0,
        param4: 0,
        param5: 0,              // 0 = Cancel
        param6: 0,
        param7: 0);
    
    // Step 2: Wait for FC to process cancellation
    await Task.Delay(1000);
    
    // Step 3: Start fresh calibration
    _logger.LogInformation("Starting fresh calibration...");
    SendCommandLong(
        command: 241,
        param5: 1);             // 1 = Full 6-position calibration
}
```

### Verification
```bash
# Check FC console logs
MAVLink Inspector > STATUSTEXT messages
Look for:
  - "Calibration cancelled" (good - FC acknowledged cancellation)
  - "Calibration already running" (bad - need to reboot FC)
```

---

## Problem 4: Vibration or Movement

### Symptoms
- Position accepted initially (MAV_RESULT_ACCEPTED)
- Then later FC sends "Rotation bad" or "Position rejected"
- Happens during sampling phase (2-4 seconds after acceptance)

### Root Cause
Vehicle moved or vibrated during FC's sampling window. FC detects inconsistent IMU data and rejects position.

### Solution
```
Physical Setup:
1. Use solid, heavy table (not folding table)
2. Indoors (no wind)
3. Turn off all fans, air conditioning near vehicle
4. Remove vehicle from powered platform (vibration from electronics)
5. Do NOT touch vehicle for 4 seconds after clicking confirm

User Instruction:
"After clicking 'Confirm Position', keep vehicle COMPLETELY STILL for 4 seconds.
 Do not touch, move, or breathe on vehicle.
 FC is taking IMU measurements and any movement will cause failure."
```

### Advanced: Vibration Monitoring
```csharp
// Monitor VIBRATION message (ID 241) during calibration
void OnVibrationReceived(VibrationMessage vib) {
    const float MAX_VIBRATION = 30.0f;  // m/s/s
    
    if (vib.vibration_x > MAX_VIBRATION ||
        vib.vibration_y > MAX_VIBRATION ||
        vib.vibration_z > MAX_VIBRATION) {
        
        _logger.LogWarning(
            $"Excessive vibration: X={vib.vibration_x:F1}, " +
            $"Y={vib.vibration_y:F1}, Z={vib.vibration_z:F1} m/s/s");
        
        ShowWarning(
            "VIBRATION TOO HIGH!\n\n" +
            "Place vehicle on more stable surface.\n" +
            "Turn off nearby fans or vibration sources.\n" +
            $"Current: {Math.Max(vib.vibration_x, Math.Max(vib.vibration_y, vib.vibration_z)):F1} m/s/s\n" +
            "Maximum: 30 m/s/s");
    }
    
    // Check for sensor clipping (saturation)
    if (vib.clipping_0 > 0 || vib.clipping_1 > 0 || vib.clipping_2 > 0) {
        _logger.LogError("IMU SENSOR CLIPPING DETECTED - hardware issue!");
        ShowError(
            "IMU SENSOR CLIPPING!\n\n" +
            "This indicates IMU sensors are saturating (hitting limits).\n" +
            "Possible causes:\n" +
            "1. Extreme vibration levels\n" +
            "2. Defective IMU sensor\n" +
            "3. Hardware damage\n\n" +
            "Action: Inspect vehicle for hardware issues.");
    }
}
```

---

## Problem 5: System State Not Ready

### Symptoms
- Calibration denied immediately
- Happens right after FC boot or reboot
- FC logs show "System not ready"

### Root Cause
FC still initializing sensors and systems. Takes 30+ seconds after power-on.

### Solution
```csharp
// Add startup delay recommendation
void OnConnected() {
    var timeSinceBoot = GetTimeSinceBoot();  // From SYSTEM_TIME message
    
    const int MIN_BOOT_TIME_MS = 30000;  // 30 seconds
    
    if (timeSinceBoot < MIN_BOOT_TIME_MS) {
        var remaining = (MIN_BOOT_TIME_MS - timeSinceBoot) / 1000;
        
        ShowInfo(
            $"Flight controller is still initializing.\n\n" +
            $"Wait {remaining} more seconds before starting calibration.\n\n" +
            "Why? FC needs time to:\n" +
            "- Initialize IMU sensors\n" +
            "- Stabilize temperature\n" +
            "- Complete self-tests\n" +
            "- Load parameters");
    }
}

// Check HEARTBEAT system_status
void OnHeartbeatReceived(HeartbeatMessage hb) {
    if (hb.system_status == MAV_STATE.BOOT || 
        hb.system_status == MAV_STATE.UNINIT) {
        
        _logger.LogWarning("FC still booting, calibration not available");
        DisableCalibrationButtons();
    }
    else if (hb.system_status == MAV_STATE.CRITICAL ||
             hb.system_status == MAV_STATE.EMERGENCY) {
        
        _logger.LogError("FC in CRITICAL/EMERGENCY state!");
        DisableCalibrationButtons();
        ShowError(
            "Flight controller reports CRITICAL or EMERGENCY state!\n\n" +
            "Calibration blocked for safety.\n" +
            "Check FC console logs for error details.");
    }
    else {
        EnableCalibrationButtons();
    }
}
```

---

## Problem 6: Firmware Version Incompatibility

### Symptoms
- MAV_CMD_ACCELCAL_VEHICLE_POS returns UNSUPPORTED (3)
- Calibration command not recognized

### Root Cause
Very old ArduPilot firmware (pre-3.6) doesn't support position-based calibration.

### Solution
```csharp
// Check firmware version from AUTOPILOT_VERSION message
void OnAutopilotVersionReceived(AutopilotVersionMessage ver) {
    // ArduPilot version encoding: MAJOR.MINOR.PATCH
    // Example: 4.0.0 = 0x04000000
    
    uint major = (ver.flight_sw_version >> 24) & 0xFF;
    uint minor = (ver.flight_sw_version >> 16) & 0xFF;
    
    if (major < 3 || (major == 3 && minor < 6)) {
        _logger.LogWarning(
            $"Old firmware detected: {major}.{minor}.x\n" +
            "Position-based calibration requires ArduPilot 3.6+");
        
        ShowWarning(
            "FIRMWARE TOO OLD\n\n" +
            $"Your FC is running ArduPilot {major}.{minor}.x\n" +
            "Position-based calibration requires 3.6 or later.\n\n" +
            "Options:\n" +
            "1. Update firmware to latest stable version (RECOMMENDED)\n" +
            "2. Use simple calibration (less accurate)\n" +
            "3. Use legacy calibration method via Mission Planner");
    }
}
```

---

## Diagnostic Logging

### Enable Detailed Logging

```csharp
public class CalibrationDiagnostics {
    private List<string> _log = new();
    
    public void LogEvent(string category, string message) {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var entry = $"[{timestamp}] [{category}] {message}";
        _log.Add(entry);
        Console.WriteLine(entry);
    }
    
    public void SaveLog(string filename) {
        File.WriteAllLines(filename, _log);
    }
}

// Usage during calibration
diagnostics.LogEvent("PRE-FLIGHT", "Checking arming status...");
diagnostics.LogEvent("PRE-FLIGHT", $"Armed: {isArmed}");
diagnostics.LogEvent("COMMAND", "Sending MAV_CMD_PREFLIGHT_CALIBRATION");
diagnostics.LogEvent("ACK", $"Result: {result}");
diagnostics.LogEvent("STATUSTEXT", $"FC: {text}");
diagnostics.LogEvent("USER", "User clicked confirm position");
diagnostics.LogEvent("TIMING", $"Settle delay: {elapsedMs}ms");
diagnostics.LogEvent("COMMAND", $"Sending MAV_CMD_ACCELCAL_VEHICLE_POS({position})");

// Save log for analysis
diagnostics.SaveLog("calibration_debug.log");
```

### Example Debug Log

```
[09:45:01.234] [PRE-FLIGHT] Starting pre-flight validation
[09:45:01.235] [PRE-FLIGHT] Connection: OK
[09:45:01.236] [PRE-FLIGHT] Armed: FALSE ✓
[09:45:01.237] [PRE-FLIGHT] System state: STANDBY ✓
[09:45:01.250] [COMMAND] Sending MAV_CMD_PREFLIGHT_CALIBRATION(param5=1)
[09:45:01.267] [ACK] Command 241 result: ACCEPTED (0) ✓
[09:45:02.156] [STATUSTEXT] FC: "Place vehicle level and press any key"
[09:45:02.157] [UI] Showing LEVEL position image
[09:45:02.158] [TIMING] Starting 2000ms settle delay...
[09:45:04.159] [TIMING] Settle delay complete (2001ms)
[09:45:04.160] [UI] Enabling confirm button
[09:45:07.892] [USER] User clicked confirm position
[09:45:07.893] [COMMAND] Sending MAV_CMD_ACCELCAL_VEHICLE_POS(0)
[09:45:07.910] [ACK] Command 42429 result: ACCEPTED (0) ✓
[09:45:07.911] [STATUSTEXT] FC: "Position accepted - sampling IMU..."
[09:45:11.234] [STATUSTEXT] FC: "Position 2 of 6 detected"
[09:45:14.567] [STATUSTEXT] FC: "Position 3 of 6 detected"
[09:45:17.890] [STATUSTEXT] FC: "Position 4 of 6 detected"
[09:45:21.123] [STATUSTEXT] FC: "Position 5 of 6 detected"
[09:45:24.456] [STATUSTEXT] FC: "Position 6 of 6 detected"
[09:45:27.789] [STATUSTEXT] FC: "Calibration successful"
[09:45:27.790] [COMPLETION] Calibration completed successfully ✓
```

---

## Emergency Recovery

### When All Else Fails

```bash
# Last Resort Recovery Procedure

1. Power cycle FC
   - Disconnect power completely
   - Wait 10 seconds
   - Reconnect power
   - Wait 30 seconds for boot

2. Clear calibration state
   - Connect with Mission Planner or MAVProxy
   - Send: param set INS_ACCOFFS_X 0
   - Send: param set INS_ACCOFFS_Y 0
   - Send: param set INS_ACCOFFS_Z 0
   - Send: param write
   - Reboot FC

3. Factory reset parameters (EXTREME - only if necessary)
   - Backup current parameters first!
   - Send MAV_CMD_PREFLIGHT_STORAGE with param1=2
   - Reconfigure vehicle from scratch
   - Retry calibration

4. Update firmware
   - Backup parameters
   - Flash latest stable ArduPilot
   - Restore parameters (carefully)
   - Retry calibration

5. Check hardware
   - Inspect IMU physically
   - Check for loose connections
   - Test with different FC if available
```

---

## Summary Checklist

### Before Every Calibration Attempt:

```
✓ Vehicle DISARMED (verify motors stopped)
✓ FC fully booted (30+ seconds after power-on)
✓ Stable surface (heavy table, indoors)
✓ No vibration sources nearby
✓ Connection stable (no packet loss)
✓ Previous calibration cancelled/completed
✓ ArduPilot 3.6+ firmware
```

### During Calibration:

```
✓ Wait for FC STATUSTEXT before clicking
✓ Wait 2 seconds after STATUSTEXT (count: one-Mississippi, two-Mississippi)
✓ Hold vehicle completely still after clicking (4 seconds)
✓ Do not cancel unless absolutely necessary
```

### If Failure Occurs:

```
1. Check arming status (90% of issues)
2. Verify settle delay implemented (2000ms)
3. Check FC console logs
4. Save diagnostic log
5. Try recovery procedure
6. Contact support with logs if unresolved
```

---

**Document Version:** 1.0.0  
**Last Updated:** January 2026  
**Status:** ✅ Production Reference

**For additional support:**
- Mission Planner Documentation: https://ardupilot.org/planner/
- ArduPilot Forums: https://discuss.ardupilot.org/
- PDRL Configuration Guide: See PDRL_CONFIGURATION_GUIDE.md
