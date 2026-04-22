# Quick Start Guide - Fix API and Test Admin Tabs

## ?? Objective
Fix the EC2 API service (returning 503 error) and test the new admin dashboard tabs.

---

## ? IMMEDIATE FIX - EC2 Server

### Option 1: Quick Fix (Recommended)

**Step 1:** Upload the fix script to EC2
```powershell
# From your local machine
scp quick-fix-api.sh ubuntu@13.235.13.233:~/
```

**Step 2:** SSH into EC2 and run it
```bash
ssh ubuntu@13.235.13.233
chmod +x quick-fix-api.sh
./quick-fix-api.sh
```

### Option 2: Manual Fix

**Step 1:** SSH into EC2
```bash
ssh ubuntu@13.235.13.233
```

**Step 2:** Restart the service
```bash
# Stop the service
sudo systemctl stop kft-api

# Kill any orphaned processes
sudo pkill -f "PavamanDroneConfigurator.API"

# Check if port 5000 is free
sudo lsof -i :5000

# If something is using it, kill it
sudo lsof -ti :5000 | xargs sudo kill -9

# Start the service
sudo systemctl start kft-api

# Check status
sudo systemctl status kft-api

# Follow logs
sudo journalctl -u kft-api -f
```

**Step 3:** Test from EC2
```bash
# Test the API locally
curl http://localhost:5000/api/firmware/inapp

# Should return JSON array or empty array []
```

**Step 4:** Test from your machine
```powershell
# From Windows PowerShell
curl http://13.235.13.233:5000/api/firmware/inapp

# Should return JSON, not 503 error
```

---

## ? VERIFICATION - Test the Admin Tabs

### 1. Build and Run the Application

```powershell
cd C:\Pavaman\kft-comfig

# Build the solution
dotnet build

# Run the UI
cd PavamanDroneConfigurator.UI
dotnet run
```

### 2. Login as Admin

- Use your admin credentials
- Navigate to Admin Dashboard

### 3. Test Each Tab

#### Tab 1: Dashboard (User Management) ?
**What to test:**
- [ ] View user list
- [ ] See user statistics (Total, Pending, Approved, Admins, Users)
- [ ] Search for users
- [ ] Filter by status (All/Approved/Pending)
- [ ] Filter by role (All/Admin/User)
- [ ] Approve a pending user
- [ ] Change user role
- [ ] Delete a user

**Quick Actions:**
- [ ] Click "Manage Firmwares" ? Should switch to Tab 2
- [ ] Click "View Param Logs" ? Should switch to Tab 3

#### Tab 2: Firmware Management ?
**What to test:**
- [ ] View all uploaded firmwares
- [ ] See firmware stats (total files, storage used)
- [ ] Upload a new firmware:
  1. Fill in firmware name
  2. Fill in version
  3. Fill in description
  4. Select vehicle type (Copter/Plane/etc)
  5. Browse and select firmware file
  6. Click Upload
- [ ] Verify firmware appears in list
- [ ] Edit firmware metadata
- [ ] Delete a firmware

**Note:** This should work now that API is fixed!

#### Tab 3: Parameter Logs ?
**What to test:**
- [ ] View parameter change logs
- [ ] See total count of logs
- [ ] Filter by user (select from dropdown)
- [ ] Filter by drone ID
- [ ] Filter by date range
- [ ] Search for specific parameter names
- [ ] View parameter details (old value ? new value)
- [ ] Download logs

**Note:** This should populate with data from S3!

#### Tab 4: Parameter Locking ?
**What to test:**
- [ ] Select a user from dropdown
- [ ] View available parameters
- [ ] Lock specific parameters:
  1. Check parameters to lock
  2. Click "Save Locked Parameters"
- [ ] Verify locked params are saved to cloud
- [ ] View currently locked parameters
- [ ] Unlock parameters
- [ ] Test sync functionality

---

## ?? TROUBLESHOOTING

### Issue: "Navigation Failed - No target page"
**Status:** ? FIXED
**Solution:** Tab navigation is now working. Quick action buttons now switch tabs instead of trying to navigate to separate pages.

### Issue: "Using local firmware directory (S3 unavailable)"
**Status:** ? IN PROGRESS
**Solution:** Restart EC2 API service (see steps above)

### Issue: Firmware upload fails
**Cause:** API service not running
**Solution:**
1. Restart EC2 API service
2. Check AWS credentials in `/var/www/kft-api/.env`
3. Verify S3 bucket permissions

### Issue: Parameter logs not loading
**Cause:** API service not responding
**Solution:**
1. Restart EC2 API service
2. Check database connection
3. Verify S3 bucket has param-logs folder

---

## ?? EXPECTED RESULTS

### After API Fix:

**Firmware Page:**
```
? Firmwares loaded from S3
? Shows available firmware versions
? Can download and install firmwares
```

**Admin Dashboard Tab 2:**
```
? Can upload firmwares
? Firmwares appear in list
? Shows storage statistics
```

**Admin Dashboard Tab 3:**
```
? Parameter logs load from S3
? Can filter and search
? Shows change history
```

**Admin Dashboard Tab 4:**
```
? Can lock parameters per user
? Locked params saved to S3
? Auto-sync on parameter changes
```

---

## ?? DEMO SCENARIO

### Complete Workflow Test:

1. **Admin logs in**
   - Go to Admin Dashboard

2. **Approve a user** (Tab 1)
   - Navigate to Tab 1 (Dashboard)
   - Find a pending user
   - Click "Approve"
   - Verify status changes to "Approved"

3. **Upload firmware for that user** (Tab 2)
   - Click "Manage Firmwares" or go to Tab 2
   - Select the user
   - Upload a firmware file
   - Verify it appears in the list

4. **Lock some parameters for that user** (Tab 4)
   - Go to Tab 4 (Parameter Locking)
   - Select the user
   - Check parameters like:
     - ARMING_CHECK
     - FENCE_ENABLE
     - RTL_ALT
   - Click "Save Locked Parameters"
   - Verify they're saved

5. **User logs in and tries to change locked param**
   - User opens Parameters page
   - Tries to change a locked parameter
   - Should see popup: "This parameter is locked by admin"
   - Parameter automatically syncs back to admin value

6. **View the parameter change logs** (Tab 3)
   - Admin goes to Tab 3
   - Filters by that user
   - Sees all parameter changes
   - Sees the locked parameter sync event

---

## ?? CHECKLIST

- [ ] EC2 API service restarted
- [ ] API responding (curl test passes)
- [ ] Desktop app running
- [ ] Logged in as admin
- [ ] Tab 1: User management working
- [ ] Tab 2: Firmware upload working
- [ ] Tab 3: Parameter logs loading
- [ ] Tab 4: Parameter locking working
- [ ] Tab switching via quick actions working
- [ ] No "Navigation Failed" errors

---

## ?? COMMON ERRORS AND FIXES

### Error: "503 Service Unavailable"
```bash
# On EC2
sudo systemctl restart kft-api
```

### Error: "Connection refused"
```bash
# Check if service is running
sudo systemctl status kft-api

# Check logs
sudo journalctl -u kft-api -n 50
```

### Error: "AWS credentials not configured"
```bash
# Check .env file
cat /var/www/kft-api/.env

# Ensure these are set:
# AWS_ACCESS_KEY_ID=your-key
# AWS_SECRET_ACCESS_KEY=your-secret
# AWS_REGION=ap-south-1
# S3_BUCKET_NAME=your-bucket
```

### Error: Database connection failed
```bash
# Check PostgreSQL service
sudo systemctl status postgresql

# Restart if needed
sudo systemctl restart postgresql
```

---

## ?? NEXT STEPS

1. **Fix the API** (Priority 1)
   - Run `quick-fix-api.sh` on EC2
   - Verify with curl test

2. **Test all tabs** (Priority 2)
   - Go through each tab
   - Test all features
   - Document any issues

3. **User testing** (Priority 3)
   - Have a regular user test
   - Try to modify locked parameters
   - Verify logs are created

---

**Last Updated:** $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
