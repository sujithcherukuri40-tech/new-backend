# Drone Info "Pending..." Fix

## Problem
Drone Details page showing:
- **"Pending..."** for FC ID and Firmware Version

## Solution Applied

### 1. Fixed Fallback Logic
- Now generates proper FC ID from system info if AUTOPILOT_VERSION doesn't arrive
- Uses SYSID_SW_MREV parameter for firmware version
- Won't stay stuck on "Pending..." forever

### 2. Added Detailed Logging
Check the logs to see what's happening:

```
=== AUTOPILOT_VERSION received ===
Extracted FC ID: FC-A1B2C3D4E5F6
Updating FC ID: Pending... -> FC-A1B2C3D4E5F6
Extracted firmware version: 4.3.6
Updating firmware version: Pending... -> 4.3.6
? AUTOPILOT_VERSION processed successfully
```

## How to Test

### Option 1: Hot Reload (Fastest)
1. Stop debugging
2. Press F5 to restart app
3. Connect to drone
4. Wait 2-3 seconds
5. Check Drone Details page

### Option 2: Check Logs
Open Output window and look for:
- `Requesting AUTOPILOT_VERSION from FC`
- `AUTOPILOT_VERSION received`
- `Extracted FC ID: ...`
- `Firmware version from SYSID_SW_MREV: ...`

## Expected Results

### If AUTOPILOT_VERSION Works:
```
FC ID: FC-A1B2C3D4E5F6G7H8  (real hardware ID)
Firmware: 4.3.6  (real version)
```

### If AUTOPILOT_VERSION Doesn't Arrive (Fallback):
```
FC ID: FW-001001-placeholder  (generated from system ID)
Firmware: 4.3.6  (from SYSID_SW_MREV parameter)
```

## Troubleshooting

### Still showing "Pending..."?

**Check logs for:**
1. `Requesting AUTOPILOT_VERSION from FC` - Message was sent
2. `AUTOPILOT_VERSION received` - Message was received
3. `Extracted FC ID: FW-UNAVAILABLE` - FC didn't provide UID/UID2

**If no AUTOPILOT_VERSION received:**
- Drone may not support it (older firmware)
- Bluetooth connections don't support it yet
- Connection unstable

**Solution:** System will use fallback after RefreshAsync():
- FC ID: Generated from system/component ID
- Firmware: From SYSID_SW_MREV parameter

### Bluetooth Connections
Currently AUTOPILOT_VERSION not supported over Bluetooth. Will show fallback values:
```
FC ID: FW-001001-0000  (from system ID)
Firmware: 4.3.6  (from parameters)
```

## Deploy to EC2

```sh
git add .
git commit -m "Fix: Use fallback FC ID instead of Pending..."
git push origin main

# On EC2:
cd ~/drone-config && git pull
# No need to rebuild API - changes are desktop app only
```

## Summary

? No more "Pending..." - shows real data or useful fallback
? Better logging to debug AUTOPILOT_VERSION issues
? Fallback FC ID generated from system info
? Firmware version from SYSID_SW_MREV parameter
