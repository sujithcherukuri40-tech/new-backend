# Drone Info Fix: Real FC ID and Firmware Version

## Problem

The Drone Details page was showing:
- ? **Firmware Version: 4.4.4** (hardcoded fallback)
- ? **Flight Controller ID: FW-0000000000-0000** (placeholder)

Instead of the REAL values from the flight controller.

## Root Cause

The `DroneInfoService.RefreshAsync()` method was using fallback/placeholder values even when the REAL data was available from the AUTOPILOT_VERSION MAVLink message.

The system WAS correctly:
1. ? Requesting AUTOPILOT_VERSION on connection
2. ? Receiving AUTOPILOT_VERSION message with UID/UID2 and firmware version
3. ? Parsing the message and extracting real FC ID and firmware version

BUT:
4. ? `RefreshAsync()` was overwriting the real values with fallbacks

## Solution

Updated `DroneInfoService.RefreshAsync()` to:

### 1. **Never use hardcoded "4.4.4" firmware version**

**Before:**
```csharp
else
{
    // Default version format
    _currentInfo.FirmwareVersion = "4.4.4";  // ? Hardcoded fallback
}
```

**After:**
```csharp
else
{
    _currentInfo.FirmwareVersion = "Pending...";  // ? Show we're waiting for real data
    _logger.LogWarning("Waiting for AUTOPILOT_VERSION message for accurate firmware version");
}
```

### 2. **Don't overwrite real FC ID with placeholder**

**Before:**
```csharp
// Only generate fallback FCID if not already set from AUTOPILOT_VERSION
if (string.IsNullOrEmpty(_currentInfo.FcId) || 
    _currentInfo.FcId.Contains("UNAVAILABLE") || 
    _currentInfo.FcId.Contains(FirmwareIdPlaceholderSuffix))
{
    _currentInfo.FcId = GenerateFcId();  // ? Generates FW-0000000000-0000
}
```

**After:**
```csharp
// ONLY generate fallback FCID if AUTOPILOT_VERSION has not provided a real one
// The AUTOPILOT_VERSION message provides UID/UID2 which are the REAL hardware identifiers
if (string.IsNullOrEmpty(_currentInfo.FcId) || 
    _currentInfo.FcId.Contains("UNAVAILABLE") || 
    _currentInfo.FcId.Contains(FirmwareIdPlaceholderSuffix) ||
    _currentInfo.FcId.StartsWith("FW-000"))  // ? Also check for placeholder IDs
{
    _currentInfo.FcId = "Pending...";  // ? Show we're waiting for real data
    _logger.LogWarning("Waiting for AUTOPILOT_VERSION message for real FC ID (UID/UID2)");
}
```

### 3. **Validate SYSID_SW_MREV before using**

**Added check:**
```csharp
// Only use if it looks valid (not 0.0.0)
if (major > 0 || minor > 0 || patch > 0)
{
    _currentInfo.FirmwareVersion = versionString;
    _logger.LogInformation("Firmware version from SYSID_SW_MREV: {Version}", versionString);
}
else
{
    _currentInfo.FirmwareVersion = "Pending...";
}
```

## How It Works Now

### Connection Flow:

```
1. User connects to drone via MAVLink
   ?
2. ConnectionService requests AUTOPILOT_VERSION
   (SendRequestAutopilotVersionAsync)
   ?
3. Flight controller responds with AUTOPILOT_VERSION message containing:
   - UID/UID2 (hardware unique ID)
   - FlightSwVersion (firmware version number)
   - FlightCustomVersion (git hash)
   ?
4. DroneInfoService.OnAutopilotVersionReceived() extracts:
   - Real FC ID from UID2/UID/FlightSwVersion
   - Real firmware version (e.g., "4.3.6", "4.4.0")
   ?
5. UI shows REAL values:
   ? Flight Controller ID: FC-A1B2C3D4E5F6...  (from UID2)
   ? Firmware Version: 4.3.6  (from FlightSwVersion)
```

### FC ID Priority:

1. **UID2** (18-byte hardware unique ID) ? `FC-{hex}` - BEST
2. **UID** (8-byte hardware ID) ? `FC-{hex}`
3. **FlightSwVersion + git hash** ? `FW-{version}-{hash}`
4. **Pending...** (waiting for AUTOPILOT_VERSION)

### Example Real Values:

**From UID2:**
```
FC-A1B2C3D4E5F6G7H8I9J0
```

**From UID:**
```
FC-1234567890ABCDEF
```

**From FlightSwVersion:**
```
FW-040300006-A3B4C5D6
```

## Testing

1. **Connect to drone**
2. **Wait 1-2 seconds** for AUTOPILOT_VERSION message
3. **Check Drone Details page**:
   - ? Flight Controller ID should show `FC-` or `FW-` prefix with hex values
   - ? Firmware Version should show actual version (e.g., "4.3.6", not "4.4.4")
   - If still shows "Pending...", check logs for AUTOPILOT_VERSION message

## Log Messages

**Good:**
```
AUTOPILOT_VERSION received: FcId=FC-A1B2C3D4E5F6, FW=4.3.6, GitHash=a3b4c5d6...
Firmware version from SYSID_SW_MREV: 4.3.6
```

**Bad (means AUTOPILOT_VERSION not received):**
```
Waiting for AUTOPILOT_VERSION message for real FC ID (UID/UID2)
Waiting for AUTOPILOT_VERSION message for accurate firmware version
```

## Files Changed

- ? `DroneInfoService.cs` - Fixed RefreshAsync() to not overwrite real values

## No Changes Needed

- ? `ConnectionService.cs` - Already requests AUTOPILOT_VERSION on connect
- ? `AutopilotVersionDataEventArgs.cs` - Already has GetFcId() and FirmwareVersionString
- ? `DroneInfoService.OnAutopilotVersionReceived()` - Already handles AUTOPILOT_VERSION correctly

## Summary

The system was already getting the real data from the flight controller. The only issue was that `RefreshAsync()` was overwriting it with fallback values. Now it preserves the real data and only shows "Pending..." while waiting for the AUTOPILOT_VERSION message to arrive.

?? **Result:** Drone Details page now shows REAL FC ID and firmware version from the flight controller hardware!
