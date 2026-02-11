# Parameter Logs Fixes

## Changes Made

### 1. **Terminology Fixed**
- **"Build ID" ? "Board ID"** everywhere in UI
- Clarified the distinction:
  - **User ID**: UUID (ec837aab-c83...)
  - **Username**: Actual name (John Doe) or email
  - **Board ID**: Flight controller board ID (FW-0000000000-0000)
  - **Build ID**: Internal drone build identifier (not shown in param logs)

### 2. **Username Now Visible in List**

**Before:**
```
S3 Key: params-logs/user_{userId}/drone_{boardId}/params_timestamp.csv
Result: Username not visible until clicking on log
```

**After:**
```
S3 Key: params-logs/user_{userId}_{userName}/board_{boardId}/params_timestamp.csv
Result: Username visible immediately in the list
```

**Example:**
```
params-logs/user_ec837aab-c83c-49d2-8ce4-9a38235_John_Doe/board_FW-0000000000-0000/params_20260211_113545.csv
```

### 3. **CSV Metadata Enhanced**

CSV files now include header comments:
```csv
# user_name=John Doe
# user_id=ec837aab-c83c-49d2-8ce4-9a38235
# board_id=FW-0000000000-0000
# timestamp=2026-02-11 11:35:45
param_name,old_value,new_value,changed_at
ACRO_THR_MID,0,1,2026-02-11 11:35:45
ACRO_BAL_ROLL,1,2,2026-02-11 11:35:45
```

### 4. **UI Labels Updated**

**Parameter Logs Page:**
- Filter dropdown: "Build ID" ? "Board ID"
- Table header: "User ID" ? "User" (shows name when available)
- Table header: "Build ID" ? "Board ID"
- Details panel: "Build ID:" ? "Board ID:"

### 5. **Code Updates**

**ParametersPageViewModel.cs:**
```csharp
// Clear terminology
var boardId = droneInfo?.FcId ?? "unknown";  // FcId is the Board ID
var buildId = droneInfo?.DroneId ?? "unknown";  // DroneId is the Build ID
```

**AwsS3Service.cs:**
```csharp
// New S3 key format includes username
var safeUserName = System.Text.RegularExpressions.Regex.Replace(userName, @"[^a-zA-Z0-9_\-\.]", "_");
var s3Key = $"{ParamsLogsPrefix}user_{userId}_{safeUserName}/board_{fcId}/params_{timestamp}.csv";
```

**ParseParamLogKey:**
- Handles both old and new formats
- Extracts username from path
- Converts underscores back to spaces
- Backward compatible with old logs

## Testing

### Test new parameter uploads:
1. Connect to drone
2. Modify parameters (Parameters tab)
3. Click "Update"
4. Go to Admin > Parameter Logs
5. Check that:
   - Username shows in the list (not just UUID)
   - "Board ID" column shows flight controller ID
   - Clicking on log shows username and all changes

### Test download:
1. Click on a log entry
2. Click "Download CSV"
3. Should download correctly (no NoSuchKey error)

## Migration Notes

**Old logs** (created before this fix):
- Format: `params-logs/user_{userId}/drone_{boardId}/params_timestamp.csv`
- Will still be listed and work
- Username will show after clicking (from CSV metadata)

**New logs** (created after this fix):
- Format: `params-logs/user_{userId}_{userName}/board_{boardId}/params_timestamp.csv`
- Username visible immediately in list
- Both S3 path AND CSV metadata have username

## Deployment

```sh
git add .
git commit -m "Fix param logs: add username to list, rename Build ID to Board ID"
git push origin main

# On EC2:
cd ~/drone-config && git pull
cd PavamanDroneConfigurator.API
dotnet publish -c Release -o ~/drone-api-published
sudo systemctl restart drone-api
```

## Summary

? Username now shows in param logs list  
? "Board ID" terminology used consistently  
? CSV metadata includes all relevant info  
? Backward compatible with old logs  
? Download works correctly (no URL encoding issues)
