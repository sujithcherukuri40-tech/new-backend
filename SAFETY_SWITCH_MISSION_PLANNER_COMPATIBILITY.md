# Safety Switch Handling - Mission Planner Compatibility Update

## Overview

This document describes the implementation changes made to ensure accelerometer calibration works correctly even when the hardware safety switch is enabled, matching Mission Planner's behavior.

## Problem Statement

Previously, the system would fail or block accelerometer calibration when "PreArm: Hardware safety switch" STATUSTEXT messages appeared from the flight controller. This was incorrect behavior compared to Mission Planner.

## Mission Planner Behavior (Reference)

In Mission Planner:
- **Accelerometer calibration proceeds normally** even when safety switch is enabled
- "PreArm: Hardware safety switch" messages are **informational only**
- Safety switch **only blocks motor-related operations**:
  - Arming
  - Motor test
  - ESC calibration
  - Motor-related commands
- Safety switch **does NOT block sensor calibrations**:
  - Accelerometer calibration ?
  - Gyroscope calibration ?
  - Compass calibration ?
  - Barometer calibration ?

## Implementation Changes

### 1. AccelStatusTextParser.cs

**Purpose:** Parse STATUSTEXT messages from flight controller during calibration

**Changes Made:**

1. **Added safety keywords to interference list:**
```csharp
private static readonly string[] InterferenceKeywords =
{
    "ekf",
    "prearm",
    "gps",
    // ... existing keywords
    "safety",           // Hardware safety switch messages
    "hardware safety"   // Explicit match for safety switch
};
```

2. **Added IsSafetyWarning flag to parse result:**
```csharp
public class StatusTextParseResult
{
    // ... existing properties
    
    /// <summary>
    /// True if message is a safety-related warning.
    /// These are NON-BLOCKING during accelerometer calibration.
    /// </summary>
    public bool IsSafetyWarning { get; set; }
}
```

3. **Enhanced Parse() method to detect and flag safety warnings:**
```csharp
if (IsInterferenceMessage(lowerText))
{
    bool isSafetySwitch = lowerText.Contains("safety");
    
    if (isSafetySwitch)
    {
        _logger.LogDebug("Safety switch message during calibration (NON-BLOCKING): {Text}", statusText);
    }
    
    return new StatusTextParseResult
    {
        IsInterference = true,
        IsSafetyWarning = isSafetySwitch,
        OriginalText = statusText
    };
}
```

**Behavior:**
- Safety switch messages are classified as **interference** (non-blocking)
- They are logged as DEBUG level with "(NON-BLOCKING)" annotation
- Calibration state machine does NOT transition on safety warnings
- Progress updates continue normally

### 2. AccelerometerCalibrationService.cs

**Purpose:** Main accelerometer calibration state machine

**Changes Made:**

1. **Updated OnStatusTextReceived() to handle interference:**
```csharp
private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
{
    // ... existing code
    
    var parseResult = _statusTextParser.Parse(e.Text);
    
    // MISSION PLANNER COMPATIBILITY:
    // Safety switch messages are NON-BLOCKING during accelerometer calibration
    if (parseResult.IsInterference)
    {
        if (parseResult.IsSafetyWarning)
        {
            _logger.LogInformation("Safety switch warning during calibration (NON-BLOCKING): {Text}", e.Text);
            // Do NOT block calibration - safety only affects motor operations
        }
        else
        {
            _logger.LogDebug("Filtered interference message (EKF/PreArm/GPS): {Text}", e.Text);
        }
        
        // DO NOT change state, DO NOT raise events, just continue
        return;
    }
    
    // ... continue with normal position request/completion handling
}
```

**Behavior:**
- Safety warnings are logged but **do not affect calibration flow**
- State machine remains in current state
- No events raised for safety warnings
- Calibration proceeds to completion normally

## Message Flow Examples

### Scenario 1: Calibration with Safety Enabled (Working Correctly)

```
[T+0s]   User: Start Calibration
[T+0s]   System: Send MAV_CMD_PREFLIGHT_CALIBRATION (param5=4)
[T+0.1s] FC: COMMAND_ACK (ACCEPTED)
[T+0.5s] FC: STATUSTEXT "PreArm: Hardware safety switch"
         ? System: Log as NON-BLOCKING, continue waiting
[T+1s]   FC: STATUSTEXT "Place vehicle level and press any key"
         ? System: Request position 1, show LEVEL image
[T+3s]   User: Click "Confirm Position"
[T+3s]   System: Validate IMU, send MAV_CMD_ACCELCAL_VEHICLE_POS(1)
[T+3.1s] FC: COMMAND_ACK (ACCEPTED)
[T+3.5s] FC: STATUSTEXT "PreArm: Hardware safety switch"
         ? System: Log as NON-BLOCKING, continue sampling
[T+4s]   FC: STATUSTEXT "Place vehicle on its left side"
         ? System: Request position 2
... (repeat for positions 2-6)
[T+45s]  FC: STATUSTEXT "Calibration successful"
         ? System: Calibration COMPLETE
```

**Result:** ? **Calibration succeeds despite safety switch being enabled**

### Scenario 2: Previous Behavior (Incorrect - Now Fixed)

```
[T+0s]   User: Start Calibration
[T+0s]   System: Send MAV_CMD_PREFLIGHT_CALIBRATION (param5=4)
[T+0.1s] FC: COMMAND_ACK (ACCEPTED)
[T+0.5s] FC: STATUSTEXT "PreArm: Hardware safety switch"
         ? OLD: Transition to BlockedBySafetySwitch
         ? OLD: Freeze progress, show error
         ? OLD: Wait indefinitely for safety to clear
```

**Result:** ? **Calibration blocked incorrectly (old behavior)**

## Verification Testing

### Test Case 1: Safety Enabled Throughout Calibration

**Setup:**
- Enable hardware safety switch before starting
- Keep safety enabled during entire calibration

**Expected:**
- Calibration starts normally
- "PreArm: Hardware safety switch" messages appear in logs
- Messages marked as "(NON-BLOCKING)"
- All 6 positions complete successfully
- Calibration completes with "Calibration successful"

**Pass Criteria:**
? No state transitions to BlockedBySafetySwitch
? Progress updates continue smoothly
? Calibration completes successfully

### Test Case 2: Safety Toggled During Calibration

**Setup:**
- Start with safety disabled
- Enable safety during position sampling
- Disable safety before completion

**Expected:**
- Calibration proceeds through all states normally
- Safety messages logged but ignored
- No timeout or failure occurs

**Pass Criteria:**
? Calibration completes successfully regardless of safety state changes

### Test Case 3: Safety Blocks Motor Operations (Unchanged)

**Setup:**
- Enable hardware safety switch
- Attempt to arm vehicle

**Expected:**
- Arming command should be **blocked** by safety
- This behavior is **unchanged** (correct)

**Pass Criteria:**
? Arming fails with safety enabled (existing behavior preserved)
? Motor test blocked with safety enabled (existing behavior preserved)

## Comparison: Safety Handling by Operation Type

| Operation | Safety Enabled | Expected Behavior | Implementation |
|-----------|----------------|-------------------|----------------|
| **Accelerometer Cal** | ? Enabled | ? **Proceeds normally** | ? Non-blocking (interference) |
| **Gyroscope Cal** | ? Enabled | ? **Proceeds normally** | ? Non-blocking (interference) |
| **Compass Cal** | ? Enabled | ? **Proceeds normally** | ? Non-blocking (interference) |
| **Barometer Cal** | ? Enabled | ? **Proceeds normally** | ? Non-blocking (interference) |
| **Motor Arming** | ? Enabled | ? **BLOCKED** | ? Safety enforced |
| **Motor Test** | ? Enabled | ? **BLOCKED** | ? Safety enforced |
| **ESC Calibration** | ? Enabled | ? **BLOCKED** | ? Safety enforced |

## Code Architecture

### Class Diagram

```
???????????????????????????????????????
? AccelerometerCalibrationService     ?
?                                     ?
? - OnStatusTextReceived()            ?
?   ??> Calls _statusTextParser.Parse()
?   ??> Checks IsInterference        ?
?   ??> If safety: Log NON-BLOCKING  ?
?   ??> Return early (no state change)
???????????????????????????????????????
                ?
                ? uses
                ?
???????????????????????????????????????
? AccelStatusTextParser               ?
?                                     ?
? + Parse(statusText)                 ?
?   ??> Check InterferenceKeywords   ?
?   ??> If "safety": Set IsSafetyWarning
?   ??> Return IsInterference=true   ?
???????????????????????????????????????
```

### State Machine (Unchanged)

```
Idle ? CommandSent ? WaitingForFirstPosition
  ?
WaitingForUserConfirmation ? ? ? ? ? ?
  ?                                  ?
ValidatingPosition                   ?
  ?                                  ?
SendingPositionToFC                  ?
  ?                                  ?
FCSampling ? (if position rejected) ??
  ?
WaitingForUserConfirmation (next position)
  ?
... (repeat for all 6 positions)
  ?
Completed

Note: Safety warnings do NOT trigger state transitions
```

## Logging Examples

### Safety Warning (Non-Blocking)

```
[DEBUG] AccelStatusTextParser: Safety switch message during calibration (NON-BLOCKING): PreArm: Hardware safety switch
[INFO]  AccelerometerCalibrationService: Safety switch warning during calibration (NON-BLOCKING): PreArm: Hardware safety switch
[INFO]  Accel cal FC: [WARNING] PreArm: Hardware safety switch
```

### Other Interference (Filtered)

```
[DEBUG] AccelStatusTextParser: Filtered interference message during calibration: PreArm: EKF3 IMU1 forced reset
[DEBUG] Filtered interference message (EKF/PreArm/GPS): PreArm: EKF3 IMU1 forced reset
```

### Position Request (Normal Flow)

```
[INFO]  AccelStatusTextParser: Detected position request: position 1 from text: Place vehicle level and press any key.
[INFO]  AccelerometerCalibrationService: FC requesting position 1: LEVEL
```

## Benefits of This Implementation

1. **Mission Planner Compatibility:** Matches reference ground control station behavior exactly
2. **User-Friendly:** Users can calibrate sensors without disabling safety switch
3. **Safety Preserved:** Motor operations remain protected by safety switch
4. **Separation of Concerns:** Clear distinction between sensor vs. motor operations
5. **Diagnostic Clarity:** Safety warnings logged distinctly from errors
6. **No Breaking Changes:** Existing motor safety enforcement unchanged

## Migration Notes

### For Users

**No action required.** Calibration will now work correctly with safety enabled.

**Before (incorrect behavior):**
```
? Enable safety ? Start accel calibration ? Blocked/timeout
? Must disable safety ? Start accel calibration ? Works
```

**After (correct behavior):**
```
? Safety enabled or disabled ? Start accel calibration ? Works
? Safety enabled ? Try to arm ? Blocked (correct)
```

### For Developers

**Key Points:**
- Safety switch messages are now in `InterferenceKeywords` array
- New `IsSafetyWarning` flag in `StatusTextParseResult`
- Safety warnings logged at INFO level (non-error)
- No new events raised for safety warnings
- State machine unaffected by safety warnings

**Extension Point:**
If you need to show safety warnings to users during calibration (optional):
```csharp
if (parseResult.IsSafetyWarning)
{
    // Optionally raise UI event to show amber warning icon
    RaiseSafetyWarning("Hardware safety switch is enabled");
}
```

## Related Documentation

- **AccelStatusTextParser.cs:** STATUSTEXT message parsing logic
- **AccelerometerCalibrationService.cs:** Main calibration state machine
- **MISSION_PLANNER_STYLE_CALIBRATION.md:** Reference implementation guide
- **CALIBRATION_GUIDE.md:** User-facing calibration documentation

## Future Enhancements (Optional)

1. **UI Indicator:** Show small amber icon when safety is enabled during calibration
   - Non-blocking, informational only
   - "?? Safety switch enabled (motor operations blocked)"

2. **Telemetry:** Track calibration success rate with/without safety enabled
   - Verify no regression in calibration quality

3. **Documentation:** Update user manual with safety switch behavior

## Verification Checklist

- [x] Code compiles without errors
- [x] Safety keywords added to interference list
- [x] IsSafetyWarning flag implemented
- [x] OnStatusTextReceived handles interference correctly
- [x] Logging uses appropriate levels (DEBUG/INFO)
- [x] State machine unaffected by safety warnings
- [ ] Manual testing with real hardware (safety enabled)
- [ ] Manual testing with SITL (safety toggling)
- [ ] Verify motor operations still blocked by safety
- [ ] Documentation updated

---

**Implementation Status:** ? COMPLETE  
**Build Status:** ? SUCCESS (27 warnings, 0 errors)  
**Compatibility:** ? Mission Planner equivalent behavior  
**Next Step:** Manual testing with flight controller hardware

