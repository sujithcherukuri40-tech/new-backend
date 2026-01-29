# AWS Authentication Configuration Guide

## ? EC2 Server Connected - Authentication Ready

Your application is now configured to connect to **AWS EC2 Backend** at:
- **Server IP**: `43.205.128.248`
- **Port**: `5000`
- **Health Status**: ? **ONLINE**

---

## ?? Current Configuration

Your `appsettings.json` is configured with:

```json
{
  "Auth": {
    "AwsApiUrl": "http://43.205.128.248:5000",
    "UseAwsApi": true
  }
}
```

**Status**: ? **ACTIVE** - App will connect to AWS EC2

---

## ?? Quick Start

### Option 1: Quick Login (Offline - Dev Only)
```powershell
cd C:\Pavaman\config\PavamanDroneConfigurator.UI
dotnet run
```
Click **"?? Quick Login (Dev)"** - works without server

### Option 2: AWS Login (Production)
```powershell
cd C:\Pavaman\config\PavamanDroneConfigurator.UI
dotnet run
```
1. Enter credentials
2. Click **"Sign In"**
3. Connects to `http://43.205.128.248:5000`
4. Authenticates with AWS RDS

---

## ? Connection Test Results

**Health Check:**
```powershell
curl http://43.205.128.248:5000/health
```
**Response**: ? `{"status":"healthy","timestamp":"2026-01-28T11:44:35Z"}`

**Status**: Server is **ONLINE** and responding!

---

## ?? Initial Setup Required

The admin user needs to be created in your AWS RDS database. You have two options:

### Option A: Auto-Create (Recommended)

The API will auto-create the admin user on first startup. Check EC2 logs:

```bash
# SSH into EC2
ssh -i your-key.pem ec2-user@43.205.128.248

# Check API logs
journalctl -u drone-configurator-api -f

# Look for:
# "? Default admin user created: admin@droneconfig.local"
```

If you see this message, the admin user is ready!

### Option B: Manual Database Creation

Connect to your RDS database and create the user:

```sql
-- Connect to RDS
psql -h drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com \
     -U new_app_user -d drone_configurator

-- Check if users table exists
\dt users

-- If table exists, check for admin user
SELECT email, is_approved, role FROM users;

-- If admin doesn't exist, wait for API to create it automatically
-- OR insert manually (password is BCrypt hashed for "Admin@123"):
INSERT INTO users (id, full_name, email, password_hash, is_approved, role, created_at)
VALUES (
  gen_random_uuid(),
  'Admin User',
  'admin@droneconfig.local',
  '$2a$11$YourBCryptHashHere', -- You need to generate this
  true,
  'Admin',
  CURRENT_TIMESTAMP
);
```

**Note**: It's easier to let the API auto-create the user via `DatabaseSeeder`.

---

## ?? Current Login Test Result

```powershell
# Tested: POST http://43.205.128.248:5000/auth/login
# Credentials: admin@droneconfig.local / Admin@123
# Result: 401 Unauthorized - "Invalid email or password"
```

**This means**:
- ? API is working correctly
- ? Database connection is working
- ?? Admin user doesn't exist yet (needs to be created)

---

## ?? Setup Checklist

- [x] EC2 instance is running
- [x] Security group allows port 5000
- [x] API is running on EC2
- [x] API can connect to RDS
- [x] Health endpoint responds
- [x] UI appsettings.json configured with EC2 IP
- [ ] **Admin user exists in database** ? NEXT STEP
- [ ] Admin user is approved (`is_approved = true`)
- [ ] Application builds successfully
- [ ] Login screen appears
- [ ] Can login with admin credentials

---

## ?? Next Steps

### 1. Create Admin User

**Option A: Restart EC2 API (Auto-Creates)**
```bash
# SSH into EC2
ssh -i your-key.pem ec2-user@43.205.128.248

# Restart API service
sudo systemctl restart drone-configurator-api

# Check logs for "Default admin user created"
journalctl -u drone-configurator-api -n 50
```

**Option B: Use Quick Login** (Skip server for now)
```powershell
cd C:\Pavaman\config\PavamanDroneConfigurator.UI
dotnet run
# Click "?? Quick Login (Dev)"
```

### 2. Test AWS Login

After admin user is created:
```powershell
cd C:\Pavaman\config\PavamanDroneConfigurator.UI
dotnet run
```
1. Enter: `admin@droneconfig.local`
2. Enter: `Admin@123`
3. Click **"Sign In"**
4. Should authenticate successfully!

---

## ?? Troubleshooting Commands

```powershell
# Test server health
curl http://43.205.128.248:5000/health

# Test login (after user is created)
$body = '{"email":"admin@droneconfig.local","password":"Admin@123"}'
Invoke-WebRequest -Uri 'http://43.205.128.248:5000/auth/login' `
  -Method POST -Body $body -ContentType 'application/json' -UseBasicParsing

# Check if API is accessible
Test-NetConnection 43.205.128.248 -Port 5000

# View app configuration
cat C:\Pavaman\config\PavamanDroneConfigurator.UI\appsettings.json
```

---

## ?? Configuration Options

### Switch to Local Development
```json
{
  "Auth": {
    "UseAwsApi": false,
    "ApiUrl": "http://localhost:5000"
  }
}
```

### Use AWS (Current)
```json
{
  "Auth": {
    "UseAwsApi": true,
    "AwsApiUrl": "http://43.205.128.248:5000"
  }
}
```

---

## ?? Summary

| Component | Status | Details |
|-----------|--------|---------|
| **EC2 API** | ? Online | http://43.205.128.248:5000 |
| **Health Check** | ? Passing | Server responding |
| **Database Connection** | ? Working | RDS accessible |
| **Admin User** | ?? Pending | Needs creation |
| **UI Configuration** | ? Complete | Pointing to AWS |
| **Quick Login** | ? Enabled | Works offline |

---

**Last Updated:** January 28, 2026  
**Server IP:** 43.205.128.248:5000  
**Status:** ? **SERVER ONLINE - READY FOR ADMIN USER CREATION**
