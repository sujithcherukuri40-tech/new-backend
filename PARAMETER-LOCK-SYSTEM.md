# Parameter Lock System - Implementation Guide

## Overview

This system allows administrators to lock specific flight controller parameters for users/devices, preventing unauthorized modifications.

---

## Architecture

### Data Flow

```
Admin UI ? API Controller ? ParamLockService ? S3 + RDS
User UI ? API Check ? ParamLockService ? Return Locked Params ? UI Validation
```

### Storage

- **RDS (PostgreSQL)**: Metadata (user_id, device_id, s3_key, param_count, timestamps)
- **S3**: JSON files with actual locked parameter lists

---

## Database Schema

### Table: `parameter_locks`

```sql
CREATE TABLE parameter_locks (
    id SERIAL PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    device_id VARCHAR(100),  -- NULL for user-wide locks
    s3_key VARCHAR(500) NOT NULL,
    param_count INT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    created_by UUID NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
    updated_at TIMESTAMPTZ,
    is_active BOOLEAN DEFAULT TRUE
);

CREATE INDEX idx_param_locks_user_device ON parameter_locks(user_id, device_id);
CREATE INDEX idx_param_locks_active ON parameter_locks(is_active);
```

---

## S3 Structure

### Bucket: `kft-main-bucket` (or configured bucket)

### Path Format:
```
locked-firmware-params/{userId}/{deviceId}/{timestamp}.json
```

### Example:
```
locked-firmware-params/123e4567-e89b-12d3-a456-426614174000/drone_abc123/20240615_143022.json
```

### JSON Format:
```json
{
  "lockedParams": [
    "PARAM_1",
    "PARAM_2",
    "PARAM_3"
  ],
  "userId": "123e4567-e89b-12d3-a456-426614174000",
  "deviceId": "drone_abc123",
  "createdAt": "2024-06-15T14:30:22Z",
  "createdBy": "admin-user-id"
}
```

---

## API Endpoints

### 1. Create Parameter Lock

**Endpoint**: `POST /admin/parameter-locks`

**Authorization**: Admin only

**Request**:
```json
{
  "userId": "123e4567-e89b-12d3-a456-426614174000",
  "deviceId": "drone_abc123",  // Optional
  "params": ["PARAM_1", "PARAM_2", "PARAM_3"]
}
```

**Response**:
```json
{
  "success": true,
  "message": "Parameter lock created successfully",
  "s3Key": "locked-firmware-params/...",
  "paramCount": 3
}
```

---

### 2. Update Parameter Lock

**Endpoint**: `PUT /admin/parameter-locks`

**Request**:
```json
{
  "lockId": 1,
  "params": ["PARAM_1", "PARAM_4", "PARAM_5"]
}
```

---

### 3. Delete Parameter Lock

**Endpoint**: `DELETE /admin/parameter-locks/{lockId}`

**Response**: 200 OK

---

### 4. Get All Locks (Admin)

**Endpoint**: `GET /admin/parameter-locks`

**Response**:
```json
[
  {
    "id": 1,
    "userId": "...",
    "userName": "John Doe",
    "userEmail": "john@example.com",
    "deviceId": "drone_123",
    "paramCount": 3,
    "lockedParams": ["PARAM_1", "PARAM_2"],  // First 5 for preview
    "createdAt": "2024-06-15T14:30:22Z",
    "createdBy": "...",
    "createdByName": "Admin User",
    "isActive": true
  }
]
```

---

### 5. Get User Locks

**Endpoint**: `GET /admin/parameter-locks/user/{userId}`

---

### 6. Check Locked Parameters

**Endpoint**: `POST /admin/parameter-locks/check`

**Authorization**: User can check own locks, Admin can check any

**Request**:
```json
{
  "userId": "123e4567-e89b-12d3-a456-426614174000",
  "deviceId": "drone_abc123"
}
```

**Response**:
```json
{
  "userId": "...",
  "deviceId": "drone_abc123",
  "lockedParams": ["PARAM_1", "PARAM_2", "PARAM_3"],
  "count": 3
}
```

---

## Enforcement Strategy

### Backend (API)

If you create parameter update endpoints in the API:

```csharp
// In parameter update controller/service
var isLocked = await _paramLockService.IsParamLockedAsync(userId, deviceId, paramName);
if (isLocked)
{
    return BadRequest(new ErrorResponse 
    { 
        Message = "This parameter is locked by administrator",
        Code = "PARAM_LOCKED" 
    });
}
```

### Frontend (UI)

**Option 1: On Connection**
```csharp
// When user connects to drone
var lockedParams = await FetchLockedParamsFromAPI(userId, deviceId);
_paramLockValidator.UpdateLockedParameters(userId, deviceId, lockedParams);
```

**Option 2: Before Parameter Change**
```csharp
// In ParameterService.SetParameterAsync()
if (_paramLockValidator.IsParameterLocked(parameterName))
{
    throw new InvalidOperationException(
        $"Parameter {parameterName} is locked by administrator");
}
```

**Option 3: UI Indication**
```csharp
// In parameter grid - show lock icon
public bool IsLocked => _paramLockValidator.IsParameterLocked(ParameterName);
```

---

## Integration Steps

### Step 1: Run Migration

```bash
cd PavamanDroneConfigurator.API
dotnet ef migrations add AddParameterLocks
dotnet ef database update
```

### Step 2: Deploy to Production

1. Ensure `kft-main-bucket` exists in S3
2. Verify IAM role has S3 permissions:
   - `s3:PutObject`
   - `s3:GetObject`
   - `s3:DeleteObject`

### Step 3: Admin UI Integration

Create admin page for parameter locking:

1. Fetch all users
2. Select user
3. Optionally select device ID
4. Multi-select parameters to lock
5. Call `POST /admin/parameter-locks`

### Step 4: User UI Integration

On drone connection:

1. Call `POST /admin/parameter-locks/check` with current user/device
2. Cache locked parameters
3. Block modifications in UI
4. Show lock icon on locked parameters

---

## Testing

### 1. Create a Lock

```bash
curl -X POST https://api.example.com/admin/parameter-locks \
  -H "Authorization: Bearer {admin-token}" \
  -H "Content-Type: application/json" \
  -d '{
    "userId": "user-guid",
    "deviceId": "drone_123",
    "params": ["PARAM_1", "PARAM_2"]
  }'
```

### 2. Verify in S3

Check S3 bucket for JSON file at:
`locked-firmware-params/{userId}/{deviceId}/{timestamp}.json`

### 3. Test Enforcement

Try to modify locked parameter - should be blocked.

---

## Security Considerations

1. **Admin Only**: Only admins can create/update/delete locks
2. **User Verification**: Users can only check their own locks (unless admin)
3. **Audit Trail**: All locks record `created_by` and timestamps
4. **Soft Delete**: Locks are deactivated, not deleted (audit trail)
5. **S3 Encryption**: Server-side encryption (AES256) enabled by default

---

## Performance

- **DB Lookups**: Indexed on `(user_id, device_id)` and `is_active`
- **S3 Caching**: UI caches locked params for 5 minutes
- **Lazy Loading**: Only fetch param list when needed
- **Batch Operations**: Admin overview only loads first 5 params per lock

---

## Rollback Plan

If issues arise:

1. **Disable Enforcement**: Comment out lock check in parameter update code
2. **Deactivate All Locks**:
   ```sql
   UPDATE parameter_locks SET is_active = false;
   ```
3. **Remove Migration**:
   ```bash
   dotnet ef migrations remove
   ```

---

## Future Enhancements

1. **Time-based Locks**: Lock parameters for specific time periods
2. **Reason Field**: Require admin to provide reason for lock
3. **Notification**: Email user when parameters are locked
4. **Lock Templates**: Pre-defined sets of parameters to lock
5. **Granular Permissions**: Allow certain users to override locks

---

## Support

For issues or questions, contact the development team.
