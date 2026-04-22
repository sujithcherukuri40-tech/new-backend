# API Endpoints Status - Admin Tabs

## ? EXISTING API ENDPOINTS

### 1. **FirmwareController** (`/api/firmware`)

#### Public Endpoints (No Auth Required):
- ? `GET /api/firmware/inapp` - List all firmwares with presigned URLs
- ? `GET /api/firmware/list` - Alias for inapp endpoint
- ? `GET /api/firmware/download/{key}` - Generate presigned download URL
- ? `GET /api/firmware/health` - S3 health check

#### Admin Endpoints (Admin Role Required):
- ? `POST /api/firmware/admin/upload` - Upload firmware to S3
- ? `DELETE /api/firmware/admin/{key}` - Delete firmware from S3
- ? `GET /api/firmware/storage-stats` - Get storage statistics

**Location:** `PavamanDroneConfigurator.API/Controllers/FirmwareController.cs`

---

### 2. **ParamLogsController** (`/api/param-logs`)

#### Public Endpoints:
- ? `GET /api/param-logs/health` - Health check

#### Authenticated Endpoints:
- ? `GET /api/param-logs` - List all param logs (Admin only)
  - Query params: search, userId, droneId, fromDate, toDate, page, pageSize
- ? `GET /api/param-logs/storage-stats` - Storage stats (Admin only)

**Location:** `PavamanDroneConfigurator.API/Controllers/ParamLogsController.cs`

---

### 3. **AdminController** (`/admin`)

#### User Management:
- ? `GET /admin/users` - Get all users
- ? `POST /admin/users/{userId}/approve` - Approve/disapprove user
- ? `POST /admin/users/{userId}/role` - Change user role
- ? `DELETE /admin/users/{userId}` - Delete user

**Location:** `PavamanDroneConfigurator.API/Controllers/AdminController.cs`

---

### 4. **ParameterLocksController** (`/api/parameter-locks`)

#### Parameter Locking:
- ? `GET /api/parameter-locks/user/{userId}` - Get locked params for user
- ? `POST /api/parameter-locks/user/{userId}` - Save locked params
- ? `DELETE /api/parameter-locks/user/{userId}` - Delete locked params
- ? `GET /api/parameter-locks/users` - List all users with locked params (Admin)

**Location:** `PavamanDroneConfigurator.API/Controllers/ParameterLocksController.cs`

---

## ? MISSING API ENDPOINTS

Based on the admin tabs requirements, we need to add:

### 1. **User-Specific Firmware Endpoints** ??

Current issue: Firmwares are global, not user-specific.

**Need to add:**
- `POST /api/firmware/admin/upload-for-user` - Upload firmware for specific user
- `GET /api/firmware/user/{userId}` - Get firmwares assigned to a user
- `GET /api/firmware/my-firmwares` - Get firmwares for current logged-in user
- `POST /api/firmware/admin/assign/{firmwareId}/to-user/{userId}` - Assign existing firmware to user
- `DELETE /api/firmware/admin/unassign/{firmwareId}/from-user/{userId}` - Unassign firmware

**Why needed:** 
> "Admin uploads the firmware to the cloud this is a user specific in this tab we will show all the users and we will upload the firmware file to that specific user only that file can be flashed"

---

### 2. **Enhanced Param Logs Endpoints** ??

Current: Basic listing exists, but missing some features.

**Need to add:**
- `GET /api/param-logs/user/{userId}` - Get logs for specific user
- `GET /api/param-logs/{logId}/download` - Download specific param log file
- `GET /api/param-logs/{logId}/content` - View param log content (JSON)
- `GET /api/param-logs/summary` - Get summary statistics
- `POST /api/param-logs/upload` - Upload param log (for users)

**Why needed:**
> "In this tab it will show the logs of every user if any parameter changed in full param list or via calibration anything"

---

### 3. **Parameter Lock Validation Endpoint** ??

Current: Basic CRUD exists, but missing validation.

**Need to add:**
- `POST /api/parameter-locks/validate` - Validate parameter changes against locks
- `GET /api/parameter-locks/check/{userId}/{paramName}` - Check if param is locked
- `POST /api/parameter-locks/sync` - Force sync locked params

**Why needed:**
> "If there is any change in the params we will show the popup to sync the fixed params and we will automatically change them"

---

## ?? PRIORITY ORDER

### Priority 1: User-Specific Firmware (CRITICAL)
This is blocking the firmware management tab from working as designed.

**Steps:**
1. Add user-firmware mapping to database
2. Create new endpoints for user-specific firmware
3. Update FirmwareController
4. Test with UI

### Priority 2: Enhanced Param Logs (MEDIUM)
Current endpoints work, but need improvements for better UX.

**Steps:**
1. Add individual log download
2. Add content viewer
3. Add upload for users
4. Test filtering

### Priority 3: Parameter Lock Validation (LOW)
Basic functionality exists, this is for enhanced features.

**Steps:**
1. Add validation endpoint
2. Add check endpoint
3. Add sync endpoint
4. Test with UI

---

## ?? CURRENT API STATUS

| Feature | Endpoint Status | Database | UI | Notes |
|---------|----------------|----------|-----|-------|
| User Management | ? Complete | ? | ? | Working |
| Firmware Upload | ? Exists | ? No user mapping | ?? | Needs user-specific |
| Param Logs | ? Basic | ? | ? | Needs enhancements |
| Param Locking | ? Complete | ? | ? | Working |

---

## ?? WHAT TO DO NEXT

Let's implement the missing endpoints one by one:

### Task 1: User-Specific Firmware Mapping
1. Create database migration for user-firmware relationship
2. Add UserFirmware entity
3. Update FirmwareController with new endpoints
4. Test with Postman
5. Update UI to use new endpoints

Would you like me to start with **Task 1: User-Specific Firmware Mapping**?

This will involve:
- Creating a new database table/entity for user-firmware assignments
- Adding new controller endpoints
- Updating the firmware upload logic to support user assignment

**Shall I proceed with creating the database entity and endpoints for user-specific firmwares?**
