# ?? QUICK FIX: Invalid Salt Version Error

## The Problem
When you try to login, you see: **"Invalid salt version"**

**Why?** The admin user doesn't exist in the database yet.

---

## ?? EASIEST Solution: Use Quick Login

**This works RIGHT NOW - no database setup needed!**

```powershell
cd C:\Pavaman\config\PavamanDroneConfigurator.UI
dotnet run
```

Click **"?? Quick Login (Dev)"** ?

**Done!** Main window appears instantly.

---

## ?? Production Solution: Create Admin User

### Option A: Run the PowerShell Script (Automatic)

```powershell
cd C:\Pavaman\config
.\create-admin-user.ps1
```

This will:
- ? Connect to your RDS database
- ? Create the admin user
- ? Verify it was created

### Option B: SSH into EC2 (If API is Running There)

```bash
# Connect to EC2
ssh -i your-key.pem ec2-user@43.205.128.248

# Restart API to trigger auto-creation
sudo systemctl restart drone-configurator-api

# Watch for success message
sudo journalctl -u drone-configurator-api -f | grep "admin user"
```

Look for: `? Default admin user created: admin@droneconfig.local`

### Option C: Manual SQL (Advanced)

```bash
# Connect to database
psql -h drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com \
     -U new_app_user \
     -d drone_configurator

# Paste this SQL
INSERT INTO users (id, full_name, email, password_hash, is_approved, role, created_at)
VALUES (
    gen_random_uuid(),
    'Admin User',
    'admin@droneconfig.local',
    '$2a$11$vK3XqYQJ5jE7Y5rZ0wZ2HeO5xN7dZzYP7hK3L9mW8nC4qR6tS8vPe',
    true,
    'Admin',
    CURRENT_TIMESTAMP
);
```

---

## ? After Creating User

Test the login:

```powershell
cd C:\Pavaman\config\PavamanDroneConfigurator.UI
dotnet run
```

1. Email: `admin@droneconfig.local`
2. Password: `Admin@123`
3. Click **"Sign In"**
4. ? Should work now!

---

## ?? Summary

**Problem**: "Invalid salt version" error  
**Cause**: No admin user in database  
**Quick Fix**: Use **Quick Login** button  
**Permanent Fix**: Create user via script, SSH, or SQL  

**Recommended**: Use Quick Login now, create user later when you need production auth.

---

**See FIX_SALT_VERSION_ERROR.md for detailed instructions**
