# Admin Tabs Fix and API Issues Resolution

## Overview
This document summarizes the fixes applied to the Admin Dashboard tabs and identifies API connectivity issues.

---

## ? FIXES COMPLETED

### 1. Admin Dashboard - Four Tabs Implementation

Successfully implemented the four admin tabs as requested:

#### **Tab 1: Dashboard (User Management)** ?
- User management CRM
- Approve/reject user requests
- Role assignment (Admin/User)
- User statistics and analytics
- Search and filter capabilities

#### **Tab 2: Firmware Management** ?
- Upload firmware files to S3 for specific users
- User-specific firmware assignment
- Vehicle type selection (Copter, Plane, Rover, etc.)
- Firmware versioning and descriptions
- View all uploaded firmwares

#### **Tab 3: Parameter Logs** ?
- View all parameter change logs from users
- Track parameter modifications from:
  - Full parameter list changes
  - Calibration parameter changes
  - Any other parameter updates
- Filter by user, drone, date range
- View detailed parameter change history

#### **Tab 4: Parameter Locking** ?
- User-specific parameter locking
- Admin selects which parameters to lock per user
- Locked parameters stored in cloud (JSON format)
- Automatic sync when users try to modify locked params
- Popup notification when locked parameters are changed
- Auto-restore to admin-defined values

### 2. Navigation Fixes

**Problem:** Quick action buttons were trying to navigate to separate pages, causing "Navigation Failed" errors.

**Solution:**
- Added `SelectedTabIndex` property to `AdminDashboardViewModel`
- Updated navigation commands to switch tabs instead of navigating to different pages:
  - `NavigateToFirmwareManagement()` ? Sets `SelectedTabIndex = 1`
  - `NavigateToParamLogs()` ? Sets `SelectedTabIndex = 2`
- Bound `TabControl.SelectedIndex` to `SelectedTabIndex` property

**Files Modified:**
- `PavamanDroneConfigurator.UI/ViewModels/Admin/AdminDashboardViewModel.cs`
- `PavamanDroneConfigurator.UI/Views/Admin/AdminDashboardView.axaml`

---

## ? ISSUES IDENTIFIED

### API Server Not Responding (503 Error)

**Problem:**
```
curl http://13.235.13.233:5000/api/firmware/inapp
? 503 Server Unavailable
```

This causes:
- Firmware page showing "Using local firmware directory (S3 unavailable)"
- Admin firmware management unable to connect to S3
- Parameter logs unable to fetch from API

**Root Cause:**
The backend API service on EC2 is not running or not responding on port 5000.

---

## ?? REQUIRED SERVER FIXES

### Check 1: Verify API Service Status

SSH into your EC2 instance and run:

```bash
# Check if the service is running
sudo systemctl status kft-api

# If not running, start it
sudo systemctl start kft-api

# Enable auto-start on boot
sudo systemctl enable kft-api
```

### Check 2: Verify Port 5000 is Open

```bash
# Check if the API is listening on port 5000
sudo netstat -tlnp | grep :5000

# Or using ss
sudo ss -tlnp | grep :5000
```

### Check 3: Check API Logs

```bash
# View recent logs
sudo journalctl -u kft-api -n 100 --no-pager

# Follow logs in real-time
sudo journalctl -u kft-api -f
```

### Check 4: Test API Locally on EC2

```bash
# Test from within the EC2 instance
curl http://localhost:5000/api/firmware/inapp

# Test health endpoint (if available)
curl http://localhost:5000/health
```

### Check 5: Verify .env Configuration

```bash
cd /var/www/kft-api
cat .env

# Check required environment variables:
# - AWS_ACCESS_KEY_ID
# - AWS_SECRET_ACCESS_KEY
# - AWS_REGION
# - S3_BUCKET_NAME
# - Database connection strings
```

### Check 6: Restart the Service

```bash
# Restart the API service
sudo systemctl restart kft-api

# Wait a few seconds, then check status
sleep 5
sudo systemctl status kft-api
```

---

## ?? VERIFICATION STEPS

After fixing the server, verify from your local machine:

### Test 1: Firmware API Endpoint
```powershell
curl http://13.235.13.233:5000/api/firmware/inapp
```
**Expected:** JSON array of firmware metadata

### Test 2: Parameter Logs Endpoint
```powershell
curl "http://13.235.13.233:5000/api/paramlogs?page=1&pageSize=10"
```
**Expected:** JSON array of parameter log entries

### Test 3: Admin Endpoints (requires auth token)
```powershell
# You'll need a valid JWT token from login
$token = "your-jwt-token-here"
curl -H "Authorization: Bearer $token" http://13.235.13.233:5000/api/admin/users
```

---

## ?? TESTING THE ADMIN TABS

Once the API is fixed:

1. **Login as Admin**
   - Use admin credentials
   - Navigate to Admin Dashboard

2. **Test Tab 1 - Dashboard**
   - View user statistics
   - Approve/reject pending users
   - Change user roles
   - Search and filter users

3. **Test Tab 2 - Firmware Management**
   - Select a user
   - Upload a firmware file
   - Verify it appears in the firmwares list
   - Download a firmware to test

4. **Test Tab 3 - Parameter Logs**
   - View all parameter changes
   - Filter by user
   - Filter by date range
   - View parameter details

5. **Test Tab 4 - Parameter Locking**
   - Select a user
   - View available parameters
   - Lock specific parameters
   - Save locked parameters
   - Verify they sync to cloud (JSON stored in S3)

---

## ?? CODE CHANGES SUMMARY

### AdminDashboardViewModel.cs
```csharp
// Added properties
public FirmwareManagementViewModel FirmwareManagementVm { get; }
public ParamLogsViewModel ParamLogsVm { get; }
private int _selectedTabIndex = 0;

// Updated constructor
public AdminDashboardViewModel(
    IAdminService adminService,
    ILogger<AdminDashboardViewModel> logger,
    ITokenStorage tokenStorage,
    ParameterLockManagementViewModel paramLockVm,
    FirmwareManagementViewModel firmwareManagementVm,
    ParamLogsViewModel paramLogsVm,
    FirmwareApiService? firmwareApiService = null)

// Updated navigation commands
[RelayCommand]
private void NavigateToFirmwareManagement()
{
    SelectedTabIndex = 1; // Switch to Firmware Management tab
}

[RelayCommand]
private void NavigateToParamLogs()
{
    SelectedTabIndex = 2; // Switch to Parameter Logs tab
}

// Added initialization
public async Task InitializeAsync()
{
    await FirmwareManagementVm.InitializeAsync();
    await ParamLogsVm.InitializeAsync();
}
```

### AdminDashboardView.axaml
```xml
<TabControl TabStripPlacement="Top" SelectedIndex="{Binding SelectedTabIndex}">

    <!-- Tab 1: Dashboard -->
    <TabItem Header="??  Dashboard">
        <!-- User Management UI -->
    </TabItem>

    <!-- Tab 2: Firmware Management -->
    <TabItem Header="??  Firmware Management">
        <adminViews:FirmwareManagementPage DataContext="{Binding FirmwareManagementVm}"/>
    </TabItem>

    <!-- Tab 3: Parameter Logs -->
    <TabItem Header="??  Parameter Logs">
        <adminViews:ParamLogsPage DataContext="{Binding ParamLogsVm}"/>
    </TabItem>

    <!-- Tab 4: Parameter Locking -->
    <TabItem Header="??  Parameter Locking">
        <adminViews:ParameterLockManagementPage DataContext="{Binding ParamLockVm}"/>
    </TabItem>

</TabControl>
```

---

## ?? NEXT STEPS

1. **Fix EC2 API Service**
   - SSH into EC2: `ssh ubuntu@13.235.13.233`
   - Check service status: `sudo systemctl status kft-api`
   - Review logs: `sudo journalctl -u kft-api -n 100`
   - Restart if needed: `sudo systemctl restart kft-api`

2. **Verify AWS Configuration**
   - Check `.env` file has correct AWS credentials
   - Verify S3 bucket exists and is accessible
   - Test S3 permissions

3. **Test the UI**
   - Run the desktop application
   - Login as admin
   - Test all four tabs
   - Upload firmware
   - Lock parameters
   - View logs

---

## ?? SUPPORT

If issues persist:

1. Check EC2 security group - port 5000 must be open
2. Check nginx/reverse proxy configuration (if using one)
3. Verify database connectivity
4. Check application logs for detailed errors

---

**Status:** ? UI Fixes Complete | ? API Server Needs Restart

**Last Updated:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
