# Accelerometer Calibration Guide
**Pavaman Drone Configurator - Professional-Grade Implementation**

## Overview

This document provides comprehensive information about the accelerometer calibration feature in the Pavaman Drone Configurator, which implements MAVLink commands similar to Mission Planner (ArduPilot/MissionPlanner).

## Implementation Architecture

### Core Components

1. **AccelerometerCalibrationService** - Main calibration service
   - Handles 6-position accelerometer calibration workflow
   - Implements fire-and-forget pattern for position commands
   - Manages state machine with strict FC-driven transitions
   - Validates IMU data before sending positions to FC

2. **AsvMavlinkWrapper** - MAVLink protocol implementation
   - Implements MAVLink v1/v2 frame parsing
   - Handles CRC validation
   - Provides command sending methods:
     - `SendPreflightCalibrationAsync()` - MAV_CMD_PREFLIGHT_CALIBRATION (241)
     - `SendAccelCalVehiclePosAsync()` - MAV_CMD_ACCELCAL_VEHICLE_POS (42429)

3. **AccelImuValidator** - IMU data validation
   - Validates gravity magnitude (~9.81 m/s² ±20%)
   - Checks gravity vector direction (≥85% on correct axis)
   - Rejects incorrect orientations with diagnostic messages

4. **AccelStatusTextParser** - STATUSTEXT message parsing
   - Detects position requests from FC
   - Identifies completion/failure messages
   - Parses sampling progress updates

### MAVLink Commands

#### MAV_CMD_PREFLIGHT_CALIBRATION (241)

Used to start various sensor calibrations:

**Parameters:**
- param1: Gyroscope calibration (0=skip, 1=calibrate)
- param2: Magnetometer calibration (0=skip, 1=calibrate)
- param3: Ground pressure/barometer (0=skip, 1=calibrate)
- param4: Radio/airspeed calibration (0=skip, 1=calibrate)
- param5: Accelerometer calibration:
  - 0 = skip
  - 1 = simple calibration
  - 2 = level-only (AHRS trims)
  - 4 = full 6-axis calibration
- param6: Compass motor compensation (0=skip, 1=calibrate)
- param7: Reserved

**Example (6-axis accel calibration):**
```
MAV_CMD_PREFLIGHT_CALIBRATION
  param1: 0 (skip gyro)
  param2: 0 (skip mag)
  param3: 0 (skip baro)
  param4: 0 (skip airspeed)
  param5: 4 (6-axis accel)
  param6: 0 (skip compmot)
  param7: 0 (reserved)
```

#### MAV_CMD_ACCELCAL_VEHICLE_POS (42429)

Used to confirm vehicle position during accelerometer calibration:

**Parameters:**
- param1: Position number (1-6)
  - 1 = LEVEL (flat on ground)
  - 2 = LEFT (on left side)
  - 3 = RIGHT (on right side)
  - 4 = NOSE_DOWN (nose pointing down 90°)
  - 5 = NOSE_UP (nose pointing up 90°)
  - 6 = BACK (upside down)
- param2-7: Reserved (0)

## Calibration Workflow

### Mission Planner-Compatible Behavior

The implementation follows Mission Planner's strict FC-driven workflow:

1. **Start Calibration**
   - User clicks "Calibrate Accelerometer"
   - Service sends `MAV_CMD_PREFLIGHT_CALIBRATION` with param5=4
   - State transitions to `CommandSent`

2. **FC Acknowledgment**
   - FC responds with `COMMAND_ACK` (ACCEPTED/IN_PROGRESS)
   - State transitions to `WaitingForFirstPosition`

3. **Position Request**
   - FC sends STATUSTEXT: "Place vehicle level and press any key"
   - Parser detects position request
   - State transitions to `WaitingForUserConfirmation`
   - UI enables confirm button

4. **User Confirms Position**
   - User positions vehicle and clicks confirm
   - Service validates IMU data (50 samples @ 50Hz = 1 second)
   - If validation passes:
     - Send `MAV_CMD_ACCELCAL_VEHICLE_POS` with position number
     - State transitions to `SendingPositionToFC`
   - If validation fails:
     - Show error message to user
     - State transitions to `PositionRejected`
     - Return to `WaitingForUserConfirmation`

5. **FC Sampling**
   - FC acknowledges position with `COMMAND_ACK`
   - FC samples accelerometer data
   - State transitions to `FCSampling`
   - FC may send progress updates via STATUSTEXT

6. **Next Position or Completion**
   - For positions 1-5: FC sends next position request (repeat from step 3)
   - After position 6: FC sends "Calibration successful" STATUSTEXT
   - State transitions to `Completed`

7. **Failure Handling**
   - FC may send failure STATUSTEXT at any point
   - State transitions to `Failed`
   - User must restart calibration

### State Machine

```
Idle
  ↓ (User clicks Start)
CommandSent
  ↓ (COMMAND_ACK received)
WaitingForFirstPosition
  ↓ (STATUSTEXT position request)
WaitingForUserConfirmation
  ↓ (User clicks Confirm)
ValidatingPosition
  ↓ (IMU validation passed)
SendingPositionToFC
  ↓ (COMMAND_ACK received)
FCSampling
  ↓ (STATUSTEXT next position OR success)
[Repeat OR Completed]

Error states:
- PositionRejected (IMU validation failed)
- Failed (FC reported failure)
- Cancelled (User cancelled)
- Rejected (FC rejected command)
```

## Safety-Critical Rules

### CRITICAL SAFETY REQUIREMENTS

1. **FC is the Single Source of Truth**
   - All state changes driven by STATUSTEXT messages from FC
   - UI NEVER decides calibration success
   - NO auto-completion, NO timeouts to finish

2. **User MUST Confirm Every Position**
   - User explicitly places vehicle in position
   - User explicitly clicks confirm button
   - NO automatic progression

3. **IMU Validation MUST Pass Before Sending to FC**
   - Validate gravity magnitude
   - Validate axis alignment
   - Reject incorrect orientations
   - Prevents bad calibration data reaching FC

4. **Finish ONLY When FC Sends Success**
   - Wait for "Calibration successful" STATUSTEXT
   - Ignore timeouts
   - Trust FC completion signal

5. **Flight Safety Takes Precedence**
   - Incorrect accelerometer calibration can cause CRASHES
   - Better to abort than to accept bad data
   - Validator rejects questionable orientations

## Position Validation

### ArduPilot Body-Fixed Coordinate System

**NED (North-East-Down) Convention:**
- **X-axis**: Points forward (nose direction)
- **Y-axis**: Points right (starboard wing)
- **Z-axis**: Points down (towards ground when level)

**Gravity Vector When Level:** (0, 0, +9.81) m/s² (pointing down)

### Validation Criteria

For each position, the validator checks:

1. **Gravity Magnitude**
   - Expected: 9.81 m/s²
   - Tolerance: ±20% (7.85 - 11.77 m/s²)

2. **Dominant Axis**
   - At least 85% of gravity magnitude on correct axis
   - Example for LEVEL: Z-axis ≥ 8.34 m/s²

3. **Other Axes**
   - Below 30% of gravity magnitude
   - Example for LEVEL: |X|, |Y| ≤ 2.94 m/s²

4. **Correct Sign**
   - Gravity points in expected direction
   - Example for LEVEL: Z is positive (down)

### Expected Accelerations by Position

| Position | Name | X (m/s²) | Y (m/s²) | Z (m/s²) |
|----------|------|----------|----------|----------|
| 1 | LEVEL | ~0 | ~0 | +9.81 |
| 2 | LEFT | ~0 | -9.81 | ~0 |
| 3 | RIGHT | ~0 | +9.81 | ~0 |
| 4 | NOSE_DOWN | -9.81 | ~0 | ~0 |
| 5 | NOSE_UP | +9.81 | ~0 | ~0 |
| 6 | BACK | ~0 | ~0 | -9.81 |

## Usage Instructions

### Prerequisites

1. Vehicle must be **disarmed**
2. Vehicle must be in a **safe location**
3. Ensure IMU sensors are **properly mounted**
4. Connection to FC must be **active and stable**

### Step-by-Step Calibration

1. **Start Calibration**
   - Navigate to Sensors Calibration page
   - Click "Calibrate Accelerometer" button
   - Wait for FC to acknowledge

2. **Position 1 - LEVEL**
   - Place vehicle on flat, level surface
   - Ensure vehicle is completely still
   - Wait for confirm button to enable (~2 seconds settle time)
   - Click "Confirm Position"
   - Wait for validation and FC acknowledgment

3. **Position 2 - LEFT**
   - Place vehicle on its left side
   - Ensure vehicle is stable and still
   - Click "Confirm Position"

4. **Position 3 - RIGHT**
   - Place vehicle on its right side
   - Ensure vehicle is stable and still
   - Click "Confirm Position"

5. **Position 4 - NOSE DOWN**
   - Tilt vehicle nose down 90° (vertical)
   - Support vehicle to prevent tipping
   - Click "Confirm Position"

6. **Position 5 - NOSE UP**
   - Tilt vehicle nose up 90° (vertical)
   - Support vehicle to prevent tipping
   - Click "Confirm Position"

7. **Position 6 - BACK**
   - Flip vehicle upside down
   - Ensure vehicle is stable
   - Click "Confirm Position"

8. **Completion**
   - Wait for FC to process all positions
   - FC sends "Calibration successful"
   - Calibration parameters saved to FC
   - Reboot FC when prompted (recommended)

### Troubleshooting

**"Position rejected - gravity magnitude out of range"**
- Vehicle may be moving or vibrating
- IMU sensor may be malfunctioning
- Ensure vehicle is completely still
- Try position again

**"Position rejected - incorrect orientation"**
- Vehicle not in correct position
- Adjust vehicle orientation
- Use level/angle gauge for precision
- Try position again

**"FC rejected calibration command"**
- Vehicle may be armed - disarm first
- FC may be busy with other operations
- Check FC is not in flight mode
- Restart calibration

**"Calibration timeout"**
- FC did not complete within expected time
- Check FC firmware version compatibility
- Check MAVLink connection stability
- Restart calibration

## Code Examples

### Starting Calibration

```csharp
// In ViewModel or Service
var calibService = serviceProvider.GetRequiredService<AccelerometerCalibrationService>();

// Subscribe to events
calibService.StateChanged += OnStateChanged;
calibService.PositionRequested += OnPositionRequested;
calibService.PositionValidated += OnPositionValidated;
calibService.CalibrationCompleted += OnCalibrationCompleted;

// Start 6-axis calibration
bool started = calibService.StartCalibration();

if (!started)
{
    // Handle error (not connected, already calibrating, etc.)
}
```

### Confirming Position

```csharp
// When user clicks confirm button
bool confirmed = await calibService.ConfirmPositionAsync();

if (!confirmed)
{
    // Handle rejection (validation failed, wrong state, etc.)
}
```

### Event Handling

```csharp
private void OnPositionRequested(object? sender, AccelPositionRequestedEventArgs e)
{
    // Update UI to show requested position
    CurrentPosition = e.Position;
    PositionName = e.PositionName;
    ShowPositionImage(e.Position);
}

private void OnPositionValidated(object? sender, AccelPositionValidationEventArgs e)
{
    if (e.IsValid)
    {
        // Position accepted
        StatusMessage = $"Position {e.PositionName} validated successfully";
    }
    else
    {
        // Position rejected
        StatusMessage = e.Message;
        ShowError(e.Message);
    }
}

private void OnCalibrationCompleted(object? sender, AccelCalibrationCompletedEventArgs e)
{
    if (e.Result == AccelCalibrationResult.Success)
    {
        StatusMessage = "Calibration completed successfully!";
        ShowSuccess();
    }
    else
    {
        StatusMessage = $"Calibration {e.Result}: {e.Message}";
        ShowError(e.Message);
    }
}
```

## Advanced Features

### Simple Calibration

For large vehicles where 6-axis is impractical:

```csharp
calibService.StartSimpleCalibration();
// Sends MAV_CMD_PREFLIGHT_CALIBRATION with param5=1
```

### Level-Only Calibration

Sets AHRS trims only:

```csharp
calibService.StartLevelCalibration();
// Sends MAV_CMD_PREFLIGHT_CALIBRATION with param5=2
```

### Diagnostics

Get detailed calibration diagnostics:

```csharp
var diagnostics = calibService.GetDiagnostics();

// Access diagnostic information
var state = diagnostics.State;
var position = diagnostics.CurrentPosition;
var duration = diagnostics.Duration;
var statusTextLog = diagnostics.StatusTextLog; // All STATUSTEXT messages
var attempts = diagnostics.PositionAttempts; // Per-position attempt history
```

## References

- ArduPilot MAVLink Command Reference: https://ardupilot.org/dev/docs/mavlink-commands.html
- Mission Planner Source Code: https://github.com/ArduPilot/MissionPlanner
- MAVLink Protocol Specification: https://mavlink.io/en/
- ArduPilot Accelerometer Calibration: https://ardupilot.org/copter/docs/common-accelerometer-calibration.html

## License

This implementation is part of the Pavaman Drone Configurator project.

---

**Last Updated:** January 2026  
**Version:** 1.0.0  
**Status:** Production Ready
