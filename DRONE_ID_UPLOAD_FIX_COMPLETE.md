# Drone ID Upload Fix - COMPLETE ?

## Problem Summary

Parameter logs were uploading with **FC ID** (FW-0000000000-0000) instead of the actual **Drone ID** (P002A020H002A020C002A010H).

### Root Cause

Bug in `FirmwareController.cs` line 264:

```csharp
// WRONG ?
await _s3Service.AppendParameterChangesAsync(
    request.UserId,
    request.UserName,
    request.FcId ?? "unknown",  // ?? Using FC ID instead of Drone ID
    changes,
    cancellationToken);
```

### Data Flow

```
Desktop App                    API Controller              S3 Service
??????????????????            ???????????????????         ????????????????
? Parameters     ?            ? Firmware        ?         ? AWS S3       ?
? PageViewModel  ?            ? Controller      ?         ? Service      ?
??????????????????            ???????????????????         ????????????????
         ?                             ?                         ?
         ? POST /api/firmware/param-logs                        ?
         ?????????????????????????????>?                         ?
         ? Body: {                     ?                         ?
         ?   UserId: "ec837aab-..."    ?                         ?
         ?   UserName: "Admin User"    ?                         ?
         ?   DroneId: "P002A020H..."   ? ? Correct Drone ID    ?
         ?   FcId: "FW-0000000000..."  ? ? Correct FC ID       ?
         ?   Changes: [...]            ?                         ?
         ? }                            ?                         ?
         ?                             ?                         ?
         ?                             ? AppendParameterChangesAsync
         ?                             ?????????????????????????>?
         ?                             ? (userId, userName,      ?
         ?                             ?  droneId, changes)      ?
         ?                             ?                         ?
         ?                             ? ? BUG WAS HERE!        ?
         ?                             ? Passed FcId instead     ?
         ?                             ? of DroneId              ?
         ?                             ?                         ?
         ?                             ?                    Creates folder:
         ?                             ?               user_xxx/drone_FcId ?
         ?                             ?                         ?
         ?                             ?               SHOULD BE:
         ?                             ?           user_xxx/drone_DroneId ?
```

## The Fix

### File: `FirmwareController.cs` (Line 261-268)

```csharp
// BEFORE (WRONG) ?
await _s3Service.AppendParameterChangesAsync(
    request.UserId,
    request.UserName,
    request.FcId ?? "unknown",  // ? FC ID used for folder name
    changes,
    cancellationToken);

// AFTER (CORRECT) ?
// Upload to S3 using DroneId (not FcId) for folder organization
await _s3Service.AppendParameterChangesAsync(
    request.UserId,
    request.UserName,
    request.DroneId ?? "unknown",  // ? Drone ID for folder structure
    changes,
    cancellationToken);
```

## S3 Folder Structure

### Before (Wrong) ?
```
param-logs/
??? user_ec837aab-..._Admin User/
?   ??? drone_FW-0000000000-0000/      ? FC ID (not unique per drone!)
?       ??? params_20260211_152240.csv
?       ??? params_20260211_124120.csv
```

### After (Correct) ?
```
param-logs/
??? user_ec837aab-..._Admin User/
?   ??? drone_P002A020H002A020C002A010H/  ? Actual Drone ID (unique!)
?       ??? params_20260211_152240.csv
?       ??? params_20260211_124120.csv
```

## Why This Matters

### Drone ID (P002A020H002A020C002A020H)
- **Source:** `BRD_SERIAL_NUM` parameter or IMU IDs
- **Purpose:** Identifies specific drone hardware
- **Unique:** Yes - each drone has different ID
- **Format:** `P` + hex string from hardware

### FC ID (FW-0000000000-0000)
- **Source:** AUTOPILOT_VERSION message board serial
- **Purpose:** Flight controller hardware identifier
- **Issue:** Can be same across drones if not properly flashed
- **Format:** `FW-` + board serial or firmware version

### Database Impact

```sql
-- ParamLogs table
CREATE TABLE ParamLogs (
    UserId VARCHAR(100),
    DroneId VARCHAR(100),  -- Now contains correct P002A020H... ID ?
    FcId VARCHAR(100),     -- FC-xxx or FW-xxx (for reference)
    FileName VARCHAR(255),
    S3Key VARCHAR(500),
    Timestamp DATETIME
);
```

## Testing Checklist

- [x] Build successful
- [ ] Upload parameters from drone
- [ ] Check S3 folder uses Drone ID
- [ ] Verify Admin ? Parameter Logs shows correct Drone ID
- [ ] Filter by Drone ID works correctly
- [ ] Download CSV file contains correct metadata

## Expected Results After Fix

1. **Parameter Logs Table:**
   ```
   User          Drone ID                        Date          Time
   Admin User    P002A020H002A020C002A010H       2026-02-11    15:22:40
   Admin User    P002A020H002A020C002A010H       2026-02-11    12:41:20
   ```

2. **S3 Console:**
   ```
   param-logs/
     user_ec837aab-c83c-49d2-8ce4-9a3823559aeb_Admin User/
       drone_P002A020H002A020C002A010H/
         params_20260211_152240.csv
         params_20260211_124120.csv
   ```

3. **CSV File Metadata:**
   ```csv
   # timestamp=2026-02-11T15:22:40Z
   # user_id=ec837aab-c83c-49d2-8ce4-9a3823559aeb
   # user_name=Admin User
   # drone_id=P002A020H002A020C002A010H  ? Correct!
   # board_id=FW-0000000000-0000
   Parameter Name,Old Value,New Value
   ACRO_BAL_ROLL,1.0,0.8
   ```

## Deployment Steps

1. **Stop API service**
   ```bash
   sudo systemctl stop droneconfig-api
   ```

2. **Deploy updated API**
   ```bash
   dotnet publish -c Release
   sudo cp -r bin/Release/net9.0/publish/* /var/www/droneconfig-api/
   ```

3. **Start API service**
   ```bash
   sudo systemctl start droneconfig-api
   ```

4. **Update desktop app** (no changes needed - already sending correct data)

## Verification

After deployment, upload a parameter change and verify:

1. **Check S3 Console:**
   - Folder name should be `drone_P002A020H...` not `drone_FW-000...`

2. **Check Admin Panel:**
   - Drone ID column shows `P002A020H...` format

3. **Check CSV file:**
   - Metadata comment has correct `drone_id=P002A020H...`

## Related Documentation

- `DRONE_ID_EXTRACTION_EXPLAINED.md` - How Drone ID is extracted
- `DRONE_ID_UPLOAD_FIX.md` - Original fix attempt (this replaces it)
- `PARAM_LOGS_FIXES.md` - Parameter logs UI fixes

---

**Status:** ? Fixed and Ready to Deploy
**Build:** ? Success
**Testing:** ? Awaiting verification

**Next:** Deploy to production and verify parameter uploads use correct Drone ID
