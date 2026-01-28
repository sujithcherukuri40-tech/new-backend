# ? Build & Run Verification Script

## Pre-Flight Checklist

### 1. Verify All Files Exist

```powershell
# Backend files
Test-Path "PavamanDroneConfigurator.API\Controllers\AdminController.cs"
Test-Path "PavamanDroneConfigurator.API\Services\AdminService.cs"
Test-Path "PavamanDroneConfigurator.API\Data\DatabaseSeeder.cs"
Test-Path "PavamanDroneConfigurator.API\DTOs\AdminDTOs.cs"

# Frontend files
Test-Path "PavamanDroneConfigurator.UI\Views\Admin\AdminPanelView.axaml"
Test-Path "PavamanDroneConfigurator.UI\ViewModels\Admin\AdminPanelViewModel.cs"
Test-Path "PavamanDroneConfigurator.Infrastructure\Services\Auth\AdminApiService.cs"
Test-Path "PavamanDroneConfigurator.Core\Interfaces\IAdminService.cs"
```

### 2. Verify .env File

```powershell
# Check if .env exists in API directory
Test-Path "PavamanDroneConfigurator.API\.env"
```

If missing, create it:

```bash
# PavamanDroneConfigurator.API\.env

# Database
DB_HOST=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com
DB_PORT=5432
DB_NAME=drone_configurator
DB_USER=new_app_user
DB_PASSWORD=Sujith2007
DB_SSL_MODE=Require

# JWT (CHANGE THIS IN PRODUCTION!)
JWT_SECRET_KEY=kZx9mP2qR7tY4wV8nB3cF6hJ1lN5oS0uA9dG2kM5pQ8rT7vW4xE1yH6jL3nP0sU
JWT_ISSUER=DroneConfigurator
JWT_AUDIENCE=DroneConfiguratorClient

# AWS (Optional)
AWS_REGION=ap-south-1
```

---

## Build Steps

### Option 1: Using Start Script (Recommended)

```powershell
cd PavamanDroneConfigurator.UI
.\start-both.ps1
```

### Option 2: Manual Build

```powershell
# Clean solution
dotnet clean

# Restore packages
dotnet restore

# Build API
cd PavamanDroneConfigurator.API
dotnet build --configuration Debug

# Build UI
cd ..\PavamanDroneConfigurator.UI
dotnet build --configuration Debug
```

---

## Run Steps

### 1. Start API (Terminal 1)

```powershell
cd PavamanDroneConfigurator.API
dotnet run
```

**Expected Output:**
```
? Loaded environment variables from .env file
?? Using database connection from individual environment variables
?? Using JWT secret from environment variable
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5000
info: Microsoft.Hosting.Lifetime[0]
      Application started.
```

**Look for:**
- ? "Database migrations applied successfully"
- ? "Default admin user created" OR "Admin user already exists"

### 2. Start UI (Terminal 2)

```powershell
cd PavamanDroneConfigurator.UI
dotnet run
```

**Expected Output:**
```
?? Auth API URL: http://localhost:5000
[Avalonia UI window appears]
```

---

## Verification Tests

### Test 1: Login with Direct Login (DEBUG only)

1. Launch UI
2. See login screen
3. **Verify**: Green "?? Quick Login (Dev)" button is visible
4. Click it
5. **Expected**: Auto-logged in, navigates to main window

### Test 2: Manual Login

1. On login screen, enter:
   - Email: `admin@droneconfig.local`
   - Password: `Admin@123`
2. Click **"Sign In"**
3. **Expected**: Navigates to main window

### Test 3: Admin Panel Access

1. After login, look at left sidebar
2. Scroll to bottom
3. **Verify**: "ADMIN" section with "?? User Management" visible
4. Click "?? User Management"
5. **Expected**: Admin panel loads showing user list

### Test 4: User Registration & Approval

1. **As Admin**: Note current user count
2. **Logout** (if there's a logout button) or restart app
3. On login screen, click **"Create one"**
4. Fill registration form:
   - Full Name: Test User
   - Email: test@example.com
   - Password: Test@123
   - Confirm Password: Test@123
5. Click **"Create Account"**
6. **Expected**: "Pending Approval" message
7. Try to login with test@example.com ? **Expected**: "Pending approval" error
8. Login as admin again
9. Go to Admin Panel
10. **Verify**: Test User appears with "Pending" status
11. Click **"Approve"** button
12. **Expected**: Status changes to "Approved"
13. Logout, login as test@example.com ? **Expected**: Success!

### Test 5: Role Change

1. Login as admin
2. Go to Admin Panel
3. Find Test User
4. Click **"Change Role"**
5. **Expected**: Role toggles from "User" to "Admin"
6. Logout, login as test@example.com
7. **Verify**: Admin panel is now visible for Test User

---

## Common Build Errors & Fixes

### Error: "Package not found"

```powershell
dotnet restore
```

### Error: "Database connection failed"

Check `.env` file and database credentials:
```powershell
# Test database connection
psql -h drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com -U new_app_user -d drone_configurator
```

### Error: "Port 5000 already in use"

```powershell
# Find and kill process using port 5000
netstat -ano | findstr :5000
taskkill /PID <PID> /F
```

### Error: "Unable to connect to server"

1. Verify API is running on port 5000
2. Check `appsettings.json` in UI project:
   ```json
   {
     "Auth": {
       "ApiUrl": "http://localhost:5000"
     }
   }
   ```

### Error: "Admin panel not showing"

1. Verify you're logged in as admin
2. Check database:
   ```sql
   SELECT email, role, is_approved FROM users WHERE email = 'admin@droneconfig.local';
   ```
3. Ensure role is `Admin` (case-sensitive)

---

## Build Output Validation

### Successful API Build

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

### Successful UI Build

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Runtime Validation

### API Health Check

```powershell
curl http://localhost:5000/health
```

**Expected:**
```json
{"status":"healthy","timestamp":"2025-01-XX..."}
```

### API Swagger (Development)

Open browser: `http://localhost:5000/swagger`

**Verify endpoints:**
- ? POST /auth/login
- ? POST /auth/register
- ? GET /admin/users (with lock icon = requires auth)

---

## Performance Benchmarks

| Metric | Expected | Acceptable |
|--------|----------|------------|
| **API Startup** | < 5s | < 10s |
| **UI Startup** | < 3s | < 5s |
| **Login Time** | < 1s | < 2s |
| **Admin Panel Load** | < 2s | < 3s |
| **User List Refresh** | < 1s | < 2s |

---

## Final Checklist

- [ ] API builds without errors
- [ ] UI builds without errors
- [ ] API starts and creates admin user
- [ ] UI connects to API successfully
- [ ] Direct login button visible (DEBUG)
- [ ] Manual login works
- [ ] Admin panel loads
- [ ] User list displays
- [ ] Approve user works
- [ ] Change role works
- [ ] New user registration works
- [ ] Pending approval flow works
- [ ] No console errors
- [ ] No UI freezing

---

## Success Criteria

? **All tests pass**  
? **No compilation errors**  
? **No runtime errors**  
? **Admin can manage users**  
? **UI is responsive**  
? **Authentication secure**  

---

## Next Steps After Verification

1. ? Change default admin password
2. ? Test with real database
3. ? Test all user flows
4. ? Review security settings
5. ? Prepare for production deployment

---

**Last Updated:** January 2025  
**Version:** 1.0.0  
**Status:** ? Ready for Testing
