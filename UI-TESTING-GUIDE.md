# ?? COMPLETE UI TESTING GUIDE - KFT DRONE CONFIGURATOR

**API URL:** `http://13.235.13.233:5000`  
**Email Service:** `noreply@kftgcs.com`  
**Testing Date:** 2026-04-22

---

## ? **PRE-TESTING CHECKLIST**

Before starting UI tests, verify:

- [ ] **API is running on EC2**
  ```bash
  ssh -i your-key.pem ubuntu@13.235.13.233
  sudo systemctl status kft-api
  curl http://localhost:5000/health
  ```

- [ ] **Port 5000 is open** (AWS Security Group)
  - EC2 ? Security Groups ? Inbound rules ? Port 5000 from 0.0.0.0/0

- [ ] **IAM role attached** (for S3/SES)
  - EC2 ? Instance ? Actions ? Security ? Modify IAM role
  - Role should have: `AmazonS3FullAccess` + `AmazonSESFullSendingAccess`

- [ ] **S3 bucket exists**
  ```bash
  aws s3 ls s3://drone-config-param-logs/
  ```

- [ ] **SES email verified**
  - AWS Console ? SES ? Verified identities
  - `noreply@kftgcs.com` should be verified

- [ ] **Desktop app built**
  ```powershell
  dotnet build PavamanDroneConfigurator.UI\PavamanDroneConfigurator.UI.csproj -c Release
  ```

---

## ?? **TESTING FLOW**

### **Test Sequence:**
1. ? Account Creation (Registration)
2. ? Email OTP Verification
3. ? Login
4. ? Forgot Password
5. ? Firmware Download/Flash
6. ? Admin Dashboard
7. ? User Approvals
8. ? Firmware Management
9. ? Parameter Logs
10. ? Parameter Locking
11. ? Logout

---

## ?? **TEST 1: ACCOUNT CREATION & EMAIL VERIFICATION**

### **Objective:** Create new user account with email OTP verification

### **Steps:**

1. **Launch Desktop App**
   ```powershell
   cd PavamanDroneConfigurator.UI\bin\Release\net9.0
   .\PavamanDroneConfigurator.UI.exe
   ```

2. **Click "Register" or "Create Account"**

3. **Fill Registration Form:**
   - **Full Name:** `Test User`
   - **Email:** `your-actual-email@gmail.com` (use real email to receive OTP)
   - **Password:** `TestPass123!@#`
   - **Confirm Password:** `TestPass123!@#`

4. **Click "Register"**

### **Expected Results:**

? **Success Message:**
```
Registration successful!
Please check your email for verification code.
```

? **Email Received:**
- **From:** `noreply@kftgcs.com`
- **Subject:** Account Verification - KFT Drone Configurator
- **Body:** Contains 6-digit OTP code (e.g., `123456`)

? **OTP Input Dialog Appears:**
- Shows input field for 6-digit code
- Has "Verify" button
- Has "Resend Code" option

5. **Enter OTP from Email**
   - Type the 6-digit code
   - Click "Verify"

### **Expected After OTP Verification:**

? **Account Status:**
- Account created
- `isApproved = false` (pending admin approval)
- Cannot login yet (shows "Account pending approval")

---

## ?? **TEST 2: LOGIN (ADMIN)**

### **Objective:** Login with default admin account

### **Steps:**

1. **On Login Screen, Enter:**
   - **Email:** `admin@kft.local`
   - **Password:** `KftAdmin@2026!`

2. **Click "Login"**

### **Expected Results:**

? **Successful Login:**
- Main window opens
- User info displayed: `Admin User (admin@kft.local)`
- All admin tabs visible:
  - Safety Settings
  - Camera
  - Serial Config
  - PID Tuning
  - Spraying Config
  - Drone Details
  - Parameters
  - **Admin Dashboard** ?
  - Log Analyzer
  - **Firmware Management** ?
  - **Parameter Logs** ?
  - **Parameter Locks** ?
  - Reset Parameters
  - Live Map
  - Telemetry

? **Connection Status:**
- Shows "Disconnected" (until MAVLink connection)

---

## ?? **TEST 3: FORGOT PASSWORD**

### **Objective:** Reset password via email

### **Steps:**

1. **Logout** (if logged in)

2. **On Login Screen, Click "Forgot Password?"**

3. **Enter Email:**
   - **Email:** `your-email@gmail.com` (registered email)
   - Click "Send Reset Code"

### **Expected Results:**

? **Success Message:**
```
Password reset code sent to your email.
Please check your inbox.
```

? **Email Received:**
- **From:** `noreply@kftgcs.com`
- **Subject:** Password Reset - KFT Drone Configurator
- **Body:** Contains 6-digit reset code

4. **Enter Reset Code and New Password:**
   - **Code:** (from email)
   - **New Password:** `NewPass123!@#`
   - **Confirm Password:** `NewPass123!@#`
   - Click "Reset Password"

### **Expected After Reset:**

? **Password Changed:**
- Success message shown
- Redirected to login
- Can login with new password

---

## ?? **TEST 4: FIRMWARE DOWNLOAD & FLASH**

### **Objective:** Download firmware from S3 and flash to drone

### **Steps:**

1. **Login as Admin**

2. **Navigate to "Firmware Management" Tab**

3. **View Available Firmwares:**

### **Expected Results:**

? **Firmware List Displayed:**
```
Available Firmwares:
- ArduCopter 4.3.7 (Copter) - 1.2 MB
- ArduPlane 4.3.7 (Plane) - 1.1 MB
- ArduRover 4.3.7 (Rover) - 1.0 MB
```

? **For Each Firmware:**
- Display Name shown
- Vehicle Type badge (Copter/Plane/Rover)
- File size shown
- Download button enabled

4. **Click "Download" on ArduCopter 4.3.7**

### **Expected During Download:**

? **Download Progress:**
- Progress bar appears
- Shows percentage (0% ? 100%)
- Shows speed (e.g., "500 KB/s")
- Shows downloaded/total size (e.g., "600 KB / 1.2 MB")

? **After Download:**
- File saved to local cache
- "Flash Firmware" button appears
- Download button changes to "Re-download"

5. **Connect Drone** (if available)
   - Connect via USB/Serial
   - Wait for "Connected" status

6. **Click "Flash Firmware"**

### **Expected During Flashing:**

? **Flash Progress:**
- Shows flashing status
- Progress updates (0% ? 100%)
- Shows current step:
  - "Preparing bootloader..."
  - "Erasing flash..."
  - "Writing firmware..."
  - "Verifying..."
  - "Rebooting..."

? **After Flash:**
- Success message: "Firmware flashed successfully!"
- Drone reboots
- Reconnects automatically

---

## ?? **TEST 5: ADMIN DASHBOARD - USER APPROVALS**

### **Objective:** Approve new user registrations

### **Steps:**

1. **Login as Admin**

2. **Navigate to "Admin Dashboard" Tab**

3. **View Pending Users:**

### **Expected Results:**

? **User List Displayed:**
```
Pending Approvals:
????????????????????????????????????????????????????????????????
? Email               ? Name        ? Role     ? Status        ?
????????????????????????????????????????????????????????????????
? test@example.com    ? Test User   ? User     ? Pending       ?
????????????????????????????????????????????????????????????????
```

? **For Each User:**
- Shows email, name, role
- Shows registration date
- Has "Approve" button
- Has "Reject" button
- Has "Change Role" dropdown

4. **Click "Approve" on Test User**

### **Expected After Approval:**

? **User Status Updated:**
- User status changes to "Approved"
- User can now login
- Success notification shown

5. **Change User Role:**
   - Select user
   - Change role dropdown: `User` ? `Admin`
   - Click "Update Role"

### **Expected:**
- Role updated to "Admin"
- User now has admin privileges

6. **Test Login with Approved User:**
   - Logout
   - Login with: `test@example.com` / `TestPass123!@#`
   - Should succeed

---

## ?? **TEST 6: FIRMWARE MANAGEMENT (ADMIN)**

### **Objective:** Upload and manage firmware files

### **Steps:**

1. **Login as Admin**

2. **Navigate to "Firmware Management" Tab**

3. **Click "Upload Firmware"**

4. **Fill Upload Form:**
   - **File:** Select `.apj`, `.px4`, or `.bin` file
   - **Vehicle Type:** Select `Copter`
   - **Version:** `4.4.0`
   - **Description:** `Test firmware upload`

5. **Click "Upload"**

### **Expected Results:**

? **Upload Progress:**
- Progress bar shown
- Shows upload percentage
- Shows upload speed

? **After Upload:**
- File appears in firmware list
- Shows correct metadata
- Available for download

6. **Delete Firmware:**
   - Select a firmware
   - Click "Delete"
   - Confirm deletion

### **Expected:**
- Firmware removed from list
- Deleted from S3 bucket
- Success message shown

---

## ?? **TEST 7: PARAMETER LOGS**

### **Objective:** View and search parameter logs

### **Steps:**

1. **Login as Admin**

2. **Navigate to "Parameter Logs" Tab**

### **Expected Results:**

? **Logs List Displayed:**
```
Parameter Logs:
???????????????????????????????????????????????????????????
? User        ? Drone ID    ? Date         ? Size         ?
???????????????????????????????????????????????????????????
? admin       ? drone-001   ? 2026-04-22   ? 45 KB        ?
? test-user   ? drone-002   ? 2026-04-21   ? 38 KB        ?
???????????????????????????????????????????????????????????
```

3. **Filter Logs:**
   - **Search by User:** Enter `admin`
   - **Search by Drone:** Enter `drone-001`
   - **Date Range:** Select last 7 days

### **Expected:**
- List filters in real-time
- Shows matching logs only
- Pagination works (if > 50 logs)

4. **View Log Details:**
   - Click on a log entry
   - Click "Download" or "View"

### **Expected:**
- CSV file downloads
- Shows parameter changes:
  ```
  Parameter,Old Value,New Value,Timestamp
  SYSID_THISMAV,1,2,2026-04-22 10:30:15
  FENCE_ENABLE,0,1,2026-04-22 10:30:20
  ```

---

## ?? **TEST 8: PARAMETER LOCKING**

### **Objective:** Lock parameters for specific users/drones

### **Steps:**

1. **Login as Admin**

2. **Navigate to "Parameter Locks" Tab**

3. **Click "Create New Lock"**

4. **Fill Lock Form:**
   - **User:** Select `test@example.com`
   - **Drone ID:** `drone-001`
   - **Parameters to Lock:**
     - `SYSID_THISMAV` = `1` (Locked)
     - `FENCE_ENABLE` = `1` (Locked)
     - `FENCE_RADIUS` = `500` (Locked)

5. **Click "Save Lock"**

### **Expected Results:**

? **Lock Created:**
- Lock appears in locks list
- Shows user, drone ID, locked params count
- Success message shown

6. **Test Lock (as Test User):**
   - Logout
   - Login as `test@example.com`
   - Connect to `drone-001`
   - Try to change `SYSID_THISMAV`

### **Expected:**
- Parameter shows as "Locked ??"
- Cannot modify value
- Shows lock message: "This parameter is locked by admin"
- Can view but not edit

7. **Update Lock:**
   - Back to admin account
   - Edit existing lock
   - Add new parameter: `RTL_ALT` = `100`
   - Save

### **Expected:**
- Lock updated
- New parameter locked for user
- Changes apply immediately

8. **Delete Lock:**
   - Select lock
   - Click "Delete"
   - Confirm

### **Expected:**
- Lock removed
- User can now modify parameters
- Success message

---

## ?? **TEST 9: LOGOUT**

### **Objective:** Logout and clear session

### **Steps:**

1. **Click User Menu** (top-right)

2. **Click "Logout"**

### **Expected Results:**

? **Logout Successful:**
- Session cleared
- Tokens revoked
- Redirected to login screen
- Cannot access protected pages
- "Disconnected" status shown

? **Verify Session Cleared:**
- Close app
- Reopen app
- Should show login screen (not auto-login)

---

## ?? **VERIFICATION CHECKLIST**

After completing all tests, verify:

### **Authentication Flow:**
- [ ] ? Registration with email OTP works
- [ ] ? Email from `noreply@kftgcs.com` received
- [ ] ? OTP verification works
- [ ] ? Login works
- [ ] ? Forgot password with email works
- [ ] ? Password reset works
- [ ] ? Logout works

### **Admin Features:**
- [ ] ? Admin dashboard accessible
- [ ] ? User approval works
- [ ] ? Role change works
- [ ] ? User deletion works

### **Firmware Management:**
- [ ] ? Firmware list loads from S3
- [ ] ? Firmware download works
- [ ] ? Download progress shown
- [ ] ? Firmware upload works (admin)
- [ ] ? Firmware deletion works (admin)
- [ ] ? Firmware flashing works (if drone connected)

### **Parameter Logs:**
- [ ] ? Logs list loads
- [ ] ? Search/filter works
- [ ] ? Log download works
- [ ] ? CSV format correct

### **Parameter Locking:**
- [ ] ? Lock creation works
- [ ] ? Locked params cannot be modified
- [ ] ? Lock update works
- [ ] ? Lock deletion works
- [ ] ? Lock applies to correct user/drone

---

## ?? **COMMON ISSUES & FIXES**

### Issue: Email not received

**Check:**
1. SES email verified in AWS Console
2. Email in SES sandbox (approve recipient emails)
3. Check spam folder
4. Check API logs: `sudo journalctl -u kft-api -n 50 | grep SES`

**Fix:**
```bash
# On EC2, test SES
aws ses send-email --from noreply@kftgcs.com --to your-email@gmail.com --subject "Test" --text "Test message" --region ap-south-1
```

---

### Issue: Firmware list empty

**Check:**
1. IAM role attached to EC2
2. S3 bucket `drone-config-param-logs` exists
3. Firmware files in `firmware/` folder

**Fix:**
```bash
# Check bucket
aws s3 ls s3://drone-config-param-logs/firmware/

# Upload test firmware
aws s3 cp ArduCopter.apj s3://drone-config-param-logs/firmware/
```

---

### Issue: "Cannot connect to API"

**Check:**
1. API running: `curl http://13.235.13.233:5000/health`
2. Port 5000 open in Security Group
3. API URL in app: `http://13.235.13.233:5000`

**Fix:**
```bash
# Restart API
sudo systemctl restart kft-api
```

---

### Issue: OTP verification fails

**Check:**
1. OTP not expired (10 minutes validity)
2. Correct code entered
3. Case-sensitive check disabled

**Fix:**
- Request new code ("Resend OTP")
- Check API logs for errors

---

## ?? **TEST RESULTS TEMPLATE**

Use this to document your test results:

```
TEST EXECUTION DATE: _______________
TESTER: _______________

? = Pass | ? = Fail | ?? = Partial

???????????????????????????????????????????????????????????????
? Test Case                      ? Result ? Notes             ?
???????????????????????????????????????????????????????????????
? 1. Account Creation            ?        ?                   ?
? 2. Email OTP Verification      ?        ?                   ?
? 3. Login (Admin)               ?        ?                   ?
? 4. Forgot Password             ?        ?                   ?
? 5. Firmware Download           ?        ?                   ?
? 6. Firmware Flash              ?        ?                   ?
? 7. Admin Dashboard             ?        ?                   ?
? 8. User Approvals              ?        ?                   ?
? 9. Firmware Management         ?        ?                   ?
? 10. Parameter Logs             ?        ?                   ?
? 11. Parameter Locking          ?        ?                   ?
? 12. Logout                     ?        ?                   ?
???????????????????????????????????????????????????????????????

OVERALL STATUS: _______________
CRITICAL ISSUES: _______________
MINOR ISSUES: _______________
RECOMMENDATIONS: _______________
```

---

## ?? **FINAL CHECKLIST BEFORE PRODUCTION**

- [ ] All tests pass
- [ ] No critical bugs found
- [ ] Email delivery working
- [ ] S3 firmware access working
- [ ] Parameter locks working
- [ ] Admin approvals working
- [ ] Performance acceptable
- [ ] UI responsive
- [ ] Error messages clear
- [ ] Help text available

---

**Ready for Testing!** ??

Start with Test 1 (Account Creation) and work through sequentially. Document any issues found.
