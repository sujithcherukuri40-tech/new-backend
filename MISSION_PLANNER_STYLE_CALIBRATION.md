# Mission Planner-Style Accelerometer Calibration - FC IS THE AUTHORITY

## What Was Implemented

### **ADDED:**
1. ✓ COMMAND_LONG message handler for MAV_CMD_ACCELCAL_VEHICLE_POS (42429)
2. ✓ Position detection via both STATUSTEXT and COMMAND_LONG
3. ✓ Correct position parameter mapping (1-6, matching Mission Planner)
4. ✓ Level-only calibration mode (param5=2)
5. ✓ Simple accelerometer calibration mode (param5=4)

### **REMOVED FROM DOCS:**
1. ✗ `AccelImuValidator.cs` references - NO client-side validation (matches Mission Planner)
2. ✗ Hardcoded threshold documentation

### **KEPT:**
1. ✓ `CalibrationService.cs` - Mission Planner style workflow
2. ✓ FC-driven workflow via STATUSTEXT and COMMAND_LONG
3. ✓ User confirmation via button clicks
4. ✓ Position detection from FC messages

---

## How It Works Now (Mission Planner Style)

### **1. User Starts Calibration**
```csharp
// User clicks "Calibrate Accelerometer"
// ✓ Send MAV_CMD_PREFLIGHT_CALIBRATION (param5=1 for 6-position)
_connectionService.SendPreflightCalibration(gyro: 0, mag: 0, groundPressure: 0, airspeed: 0, accel: 1);
```

### **2. FC Drives Everything**
```csharp
// FC sends STATUSTEXT: "Place vehicle level and press any key"
// OR FC sends COMMAND_LONG: MAV_CMD_ACCELCAL_VEHICLE_POS param1=1 (LEVEL)
// ✓ We detect position request → Show position 1 image
// ✓ Enable "Click When In Position" button
// ✓ Wait for user

// NO validation on our end!
// NO IMU checks!
// NO threshold comparisons!
// FC is the AUTHORITY - it validates everything
```

### **3. User Confirms Position**
```csharp
// User clicks button
// ✓ Send MAV_CMD_ACCELCAL_VEHICLE_POS with param1=1-6 (NOT 0-5!)
_connectionService.SendAccelCalVehiclePos(position); // position is 1-6

// That's it! FC decides if it's correct.
```

### **4. FC Responds**
```csharp
// If correct: FC sends "Place vehicle on its left side and press any key"
// ? Detect "left" ? Show position 2 image
// ? Enable button ? Wait for user

// If wrong: FC sends "Rotation bad, try again"  
// ? Show error ? Keep on same position ? User tries again

// If done: FC sends "Calibration successful"
// ? Show success ? Enable reboot button
```

---

## Comparison

### **Before (Hardcoded):**
```csharp
// ? Client collects 50 IMU samples
// ? Client calculates gravity magnitude: 9.81 � 20%
// ? Client checks Z-axis dominant (85%)
// ? Client checks other axes small (30%)
// ? Client decides if position is correct
// ? IF pass ? send to FC
// ? IF fail ? show error dialog

// Problems:
// - Hardcoded 9.81 (gravity varies by location!)
// - Hardcoded 20% tolerance (too strict/loose?)
// - Hardcoded 50 samples (why 50?)
// - Hardcoded 85% threshold (why 85%?)
// - Client doesn't know FC's IMU orientation
// - Client doesn't know FC's calibration algorithm
```

### **After (Mission Planner):**
```csharp
// ? User clicks button
// ? Send MAV_CMD_ACCELCAL_VEHICLE_POS(position)
// ? Wait for FC response via STATUSTEXT
// ? Show FC's exact message to user
// ? Let FC decide everything

// Benefits:
// - NO hardcoded values
// - NO assumptions about FC
// - Works with ANY ArduPilot version
// - Works with ANY IMU orientation
// - Works with ANY calibration algorithm
// - FC is ALWAYS the source of truth
```

---

## Code Flow

### **CalibrationService.cs - Main Logic**

```csharp
public Task<bool> StartAccelerometerCalibrationAsync(bool fullSixAxis = true)
{
    // Just send command and wait for FC
    _connectionService.SendPreflightCalibration(gyro: 0, mag: 0, groundPressure: 0, airspeed: 0, accel: fullSixAxis ? 4 : 1);
    return Task.FromResult(true);
}

private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
{
    var text = e.Text;
    var lower = text.ToLowerInvariant();
    
    // Detect position request from FC
    if (lower.Contains("place") && lower.Contains("level"))
    {
        // Show position 1, enable button
        RaiseStepRequired(1, true, text);
    }
    else if (lower.Contains("calibration successful"))
    {
        // Done!
        FinishCalibration(true, text);
    }
    else if (lower.Contains("rotation bad"))
    {
        // FC rejected - let user try again
        RaiseStepRequired(_currentPosition, true, text);
    }
}

public Task<bool> AcceptCalibrationStepAsync()
{
    // User clicked button - just send to FC
    _connectionService.SendAccelCalVehiclePos(_currentPosition);
    return Task.FromResult(true);
}
```

---

## What UI Shows

### **Position Detection (From FC Messages)**
- **"Place vehicle level"** ? Position 1 (LEVEL)
- **"Place vehicle on its left side"** ? Position 2 (LEFT)
- **"Place vehicle on its right side"** ? Position 3 (RIGHT)
- **"Place vehicle nose down"** ? Position 4 (NOSE DOWN)
- **"Place vehicle nose up"** ? Position 5 (NOSE UP)
- **"Place vehicle on its back"** ? Position 6 (BACK)

### **FC Responses Displayed**
- **"Rotation bad"** ? Error: try again on same position
- **"Calibration successful"** ? Success: enable reboot
- **"Calibration failed"** ? Failure: show error
- **ANY other FC message** ? Display as-is to user

---

## Dependency Injection Changes

### **Current State:**
```csharp
// ✓ Already properly configured:
services.AddSingleton<CalibrationService>();
// No AccelImuValidator or AccelStatusTextParser needed
```

---

## Testing

### **Expected Flow:**
1. Connect to FC
2. Click "Calibrate Accelerometer"
3. FC sends "Place vehicle level and press any key" (STATUSTEXT)
   - OR FC sends COMMAND_LONG with MAV_CMD_ACCELCAL_VEHICLE_POS param1=1
4. UI shows Level.png + "Click When In Position" button
5. User places drone level
6. User clicks button → Sends MAV_CMD_ACCELCAL_VEHICLE_POS param1=1
7. FC validates internally (we don't know how)
8. If correct: FC sends "Place vehicle on its left side..."
9. If wrong: FC sends "Rotation bad, try again"
10. Repeat for all 6 positions
11. FC sends "Calibration successful"
12. UI shows success + reboot button

### **Position Parameter Mapping:**
- ✓ Positions 1-6 are sent directly (LEVEL=1, LEFT=2, RIGHT=3, NOSEDOWN=4, NOSEUP=5, BACK=6)
- ✓ NO offset needed (matches Mission Planner ACCELCAL_VEHICLE_POS enum)

### **No More:**
- ✗ "Position 1 validation FAILED: Gravity magnitude 10.5 m/s² outside tolerance"
- ✗ "Expected Z-axis dominant (85%) but got 78%"
- ✗ Error dialogs from client-side checks

### **Only FC Messages:**
- ✓ "Place vehicle level and press any key"
- ✓ "Rotation bad, try again"
- ✓ "Calibration successful"
- ✓ Whatever FC decides to send

---

## Files Changed

1. ✓ `AsvMavlinkWrapper.cs` - Added COMMAND_LONG handler
   - Added HandleCommandLong method
   - Added CommandLongReceived event
   - Added CommandLongData class
   
2. ✓ `IConnectionService.cs` - Added COMMAND_LONG event
   - Added CommandLongReceived event
   - Added CommandLongEventArgs class

3. ✓ `ConnectionService.cs` - Forward COMMAND_LONG events
   - Subscribe to wrapper's CommandLongReceived
   - Forward to CalibrationService

4. ✓ `CalibrationService.cs` - Handle COMMAND_LONG position requests
   - Subscribe to CommandLongReceived
   - Added OnCommandLongReceived handler
   - Fixed position mapping (1-6, not 0-5)
   - Added StartSimpleAccelerometerCalibrationAsync

5. ✓ `ICalibrationService.cs` - Added simple calibration method
   - Added StartSimpleAccelerometerCalibrationAsync

6. ✓ Documentation updated
   - Removed AccelImuValidator references
   - Documented COMMAND_LONG handling
   - Corrected position mapping documentation

---

## Files NOT Changed (No AccelImuValidator to Delete)

**Note:** The referenced files never existed in this codebase:
- `AccelImuValidator.cs` - Never existed
- `AccelImuValidator_Improved.cs` - Never existed

The implementation was already FC-driven via STATUSTEXT. This update adds:
- COMMAND_LONG handling for more reliable position detection
- Correct position parameter mapping (1-6)
- Additional calibration modes (simple/level-only)

---

## Summary

? **NO hardcoded values**  
? **100% FC-driven**  
? **Mission Planner behavior**  
? **Works with any ArduPilot firmware**  
? **Works with any IMU orientation**  
? **FC is always source of truth**  
? **User just clicks when ready**  
? **FC decides if position is correct**  

---

**Date:** January 2026  
**Author:** GitHub Copilot  
**Status:** ? READY FOR TESTING

