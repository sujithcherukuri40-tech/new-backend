# Parameter Logs: Drone ID Upload Fix

## Changes Made

### ? Now Uploads **Drone ID** (Not Board ID) to Cloud

**What Changed:**
- **Before**: Uploaded Board ID (FC-xxx or FW-xxx) as the drone identifier
- **After**: Uploads real Drone ID (P003B04H22003B0522003B0H22) from drone sensors

### Code Changes

#### 1. **ParametersPageViewModel.cs**
```csharp
// Get Drone ID and Board ID from drone info service
var droneInfo = await _droneInfoService.GetDroneInfoAsync();
var droneId = droneInfo?.DroneId ?? "unknown";  // Drone ID (P003B04H22...)
var boardId = droneInfo?.FcId ?? "unknown";  // Board ID (FC-xxx or FW-xxx)

// Upload: send droneId (actual drone identifier)
await _firmwareApiService.UploadParameterLogAsync(userId, userName, droneId, boardId, changes);
```

#### 2. **AwsS3Service.cs**

**S3 Key Structure:**
```
OLD: params-logs/user_{userId}_{userName}/board_{boardId}/params_{timestamp}.csv
NEW: params-logs/user_{userId}_{userName}/drone_{droneId}/params_{timestamp}.csv
```

**Example:**
```
params-logs/user_ec837aab_John_Doe/drone_P003B04H22003B0522003B0H22/params_20260211_113545.csv
```

**CSV Metadata:**
```csv
# user_name=John Doe
# user_id=ec837aab-c83c-49d2-8ce4-9a38235
# drone_id=P003B04H22003B0522003B0H22  ? Real Drone ID
# timestamp=2026-02-11 11:35:45
param_name,old_value,new_value,changed_at
ACRO_THR_MID,0,1,2026-02-11 11:35:45
```

#### 3. **UI Labels Updated**
- Filter: "Board ID" ? "Drone ID"
- Table header: "Board ID" ? "Drone ID"
- Details panel: "Board ID:" ? "Drone ID:"

### What Gets Uploaded

| Field | Value | Description |
|-------|-------|-------------|
| **User ID** | `ec837aab-c83c-49d2-8ce4...` | User's UUID |
| **Username** | `John Doe` | User's full name or email |
| **Drone ID** | `P003B04H22003B0522003B0H22` | **Real drone identifier** from sensors |
| **Board ID** | `FC-A1B2C3D4E5F6` or `FW-xxx` | Flight controller board (not used in path) |

### Drone ID Source

The Drone ID is built from:
```csharp
// From DroneInfoService.RefreshAsync()
var uid1 = await _parameterService.GetParameterAsync("INS_ACC_ID");
var uid2 = await _parameterService.GetParameterAsync("INS_ACC2_ID");
var uid3 = await _parameterService.GetParameterAsync("INS_GYR_ID");

_currentInfo.DroneId = $"P{(int)(uid1?.Value ?? 0):X8}{(int)(uid2?.Value ?? 0):X8}{(int)(uid3?.Value ?? 0):X8}";
```

**Example:**
- INS_ACC_ID = `0x003B04H2`
- INS_ACC2_ID = `0x2003B052`
- INS_GYR_ID = `0x2003B0H2`
- **Result**: `P003B04H22003B0522003B0H22`

### Backward Compatibility

The parser handles both formats:
- ? **New**: `params-logs/.../drone_{droneId}/...`
- ? **Old**: `params-logs/.../board_{boardId}/...`
- ? **Legacy**: `params-logs/user_{userId}/drone_{droneId}/...`

### Benefits

1. **Unique Drone Identification**: Each physical drone has a unique ID
2. **Track Specific Aircraft**: Know which exact drone had parameter changes
3. **Better Analytics**: Can track which drones need attention
4. **Clearer Logs**: "Drone ID" is more intuitive than "Board ID"

### Deploy

```sh
git add .
git commit -m "Upload Drone ID instead of Board ID to param logs"
git push origin main

# On EC2:
cd ~/drone-config && git pull
cd PavamanDroneConfigurator.API
dotnet publish -c Release -o ~/drone-api-published
sudo systemctl restart drone-api
```

### Verification

After deploying and uploading parameters:

1. **Check S3 bucket structure:**
```
params-logs/
  ??? user_ec837aab_John_Doe/
      ??? drone_P003B04H22003B0522003B0H22/  ? Drone ID!
          ??? params_20260211_113545.csv
```

2. **Check CSV content:**
```csv
# user_name=John Doe
# drone_id=P003B04H22003B0522003B0H22  ? Real Drone ID
```

3. **Check UI:** Param Logs page shows "Drone ID" column with `P003B04H22...`

## Summary

? **Drone ID (P003B04H22...)** uploaded to cloud  
? Real aircraft identification from sensors  
? S3 path uses `drone_` prefix  
? CSV metadata shows correct drone_id  
? UI labels updated to "Drone ID"  
? Backward compatible with old logs
