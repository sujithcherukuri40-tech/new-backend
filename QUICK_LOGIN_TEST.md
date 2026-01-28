# ? QUICK LOGIN TEST GUIDE

## ?? Testing Direct Login Feature

### Prerequisites
- ? API running on `http://localhost:5000`
- ? Database accessible
- ? Default admin user created (auto-created on first API start)
- ? UI built in **DEBUG** configuration

---

## ?? Test Procedure

### Test 1: Verify Direct Login Button Visibility

**Steps:**
1. Start the application in DEBUG mode:
   ```powershell
   cd PavamanDroneConfigurator.UI
   dotnet run --configuration Debug
   ```

2. **Expected:**
   - Login screen appears
   - **Green button** with text "?? Quick Login (Dev)" is visible below the "Sign In" button

**? PASS CRITERIA:**
- Direct login button is visible
- Button is green colored
- Button has rocket emoji

---

### Test 2: Direct Login to Main Window

**Steps:**
1. On login screen, click **"?? Quick Login (Dev)"**

**Expected Behavior:**
1. Button becomes disabled briefly
2. Loading state appears (button might show progress)
3. **Login screen closes**
4. **Main window opens** showing the drone configurator interface
5. User is logged in as admin

**? PASS CRITERIA:**
- Seamless transition from login ? main window
- No errors in console
- Main window shows "?? User Management" in sidebar (admin feature)

---

### Test 3: Verify Admin Access

**After Quick Login:**

1. Look at the left sidebar
2. Scroll to bottom
3. **Expected:** "ADMIN" section with "?? User Management" button

**Click "?? User Management":**

**Expected:**
- Admin panel loads
- Shows user list with at least one user (admin@droneconfig.local)
- User has "Approved" status
- Role shows "Admin"

**? PASS CRITERIA:**
- Admin panel accessible
- Admin user visible in list
- All buttons (Approve, Change Role) work

---

### Test 4: Release Build Verification

**Important: Direct login should NOT be visible in Release builds**

**Steps:**
1. Build in Release mode:
   ```powershell
   dotnet build --configuration Release
   ```

2. Run the Release build:
   ```powershell
   dotnet run --configuration Release
   ```

3. Check login screen

**? PASS CRITERIA:**
- Direct login button is **HIDDEN**
- Only manual login available
- No green rocket button visible

---

## ?? Detailed Flow Verification

### What Happens During Quick Login

```
User Clicks "?? Quick Login (Dev)"
    ?
LoginViewModel.DirectLoginCommand executes
    ?
Calls AuthSessionViewModel.LoginAsync("admin@droneconfig.local", "Admin@123")
    ?
AuthApiService sends POST to /auth/login
    ?
Backend validates credentials
    ?
Backend returns JWT tokens + user info (role: Admin)
    ?
Tokens stored in DPAPI-encrypted file
    ?
AuthState updated to Authenticated
    ?
LoginViewModel raises LoginSucceeded event
    ?
AuthShellViewModel catches event
    ?
AuthShellViewModel raises AuthenticationCompleted
    ?
App.axaml.cs catches AuthenticationCompleted
    ?
Closes AuthShell window
    ?
Creates and shows MainWindow
    ?
MainWindowViewModel checks IsAdmin from AuthSession
    ?
Admin panel initialized (if user is admin)
    ?
User sees main drone configurator interface
```

---

## ?? Troubleshooting

### Issue: Direct login button not visible

**Cause:** Running in Release mode

**Fix:**
```powershell
# Ensure DEBUG mode
dotnet run --configuration Debug
```

---

### Issue: "Unable to connect to server"

**Cause:** API not running

**Fix:**
```powershell
# Terminal 1: Start API
cd PavamanDroneConfigurator.API
dotnet run

# Terminal 2: Start UI (after API is running)
cd PavamanDroneConfigurator.UI
dotnet run
```

---

### Issue: "Invalid credentials"

**Cause:** Admin user not created in database

**Fix:**
1. Stop API
2. Delete database (if testing locally)
3. Restart API (will auto-create admin user)
4. Check API console for message: "? Default admin user created"

---

### Issue: Main window doesn't open after click

**Cause:** Auth flow issue

**Debug Steps:**
1. Check API console for authentication logs
2. Check UI console for errors
3. Verify AuthShellViewModel.AuthenticationCompleted event is raised
4. Verify App.axaml.cs ShowMainWindow is called

**Quick Fix:**
```powershell
# Clean and rebuild
dotnet clean
dotnet build
dotnet run
```

---

### Issue: Admin panel not showing after login

**Cause:** User role not set to Admin

**Verify in database:**
```sql
SELECT email, role, is_approved 
FROM users 
WHERE email = 'admin@droneconfig.local';
```

**Expected:**
- role: `Admin` (case-sensitive)
- is_approved: `true`

**Fix:**
```sql
UPDATE users 
SET role = 'Admin', is_approved = true 
WHERE email = 'admin@droneconfig.local';
```

---

## ?? Performance Benchmarks

| Action | Expected Time |
|--------|---------------|
| Button click ? API call | < 100ms |
| API authentication | < 500ms |
| Token storage | < 50ms |
| Window transition | < 200ms |
| **Total (click ? main window)** | **< 1 second** |

---

## ? Final Verification Checklist

- [ ] Direct login button visible in DEBUG mode
- [ ] Direct login button hidden in Release mode
- [ ] Click navigates to main window (no intermediate screens)
- [ ] Main window shows admin panel option
- [ ] Admin panel loads and functions correctly
- [ ] No console errors during login
- [ ] No UI freezing or lag
- [ ] Session persists (restart app ? still logged in)
- [ ] Manual login still works normally
- [ ] Logout works (if implemented)

---

## ?? Success Criteria

? **Direct Login Working** if:
1. One click logs in as admin
2. Main window appears immediately
3. Admin features accessible
4. No errors in console
5. Transition is smooth (< 1 second)

---

## ?? Test Results Template

```
Date: _____________
Tester: _____________
Build: DEBUG / RELEASE

[ ] Test 1: Button Visibility - PASS / FAIL
    Notes: ___________________________________

[ ] Test 2: Quick Login Flow - PASS / FAIL
    Notes: ___________________________________

[ ] Test 3: Admin Access - PASS / FAIL
    Notes: ___________________________________

[ ] Test 4: Release Build - PASS / FAIL
    Notes: ___________________________________

Overall: PASS / FAIL
Issues: ___________________________________
```

---

## ?? Quick Test Commands

```powershell
# Full test (from solution root)

# 1. Start API
Start-Process powershell -ArgumentList "cd PavamanDroneConfigurator.API; dotnet run"

# 2. Wait 5 seconds for API startup
Start-Sleep -Seconds 5

# 3. Start UI in DEBUG
cd PavamanDroneConfigurator.UI
dotnet run --configuration Debug

# 4. Click green "?? Quick Login (Dev)" button
# 5. Verify main window appears
```

---

**Last Updated:** January 2025  
**Version:** 1.0.0  
**Status:** ? Ready for Testing
