# ?? AWS SDK Version Mismatch - Fix Guide

## ?? Root Cause

```
System.MissingMethodException: Method not found: 
'System.Threading.Tasks.Task`1<Boolean> Amazon.Runtime.SharedInterfaces.ICoreAmazonS3.DoesS3BucketExistAsync(System.String)'
```

**The Problem:** The AWS SDK version deployed on EC2 is incompatible with the code. The method `DoesS3BucketExistAsync` doesn't exist in the deployed SDK version.

**Why This Happens:** EC2 has an older build of the API that was compiled against a different AWS SDK version.

---

## ? **Solution: Redeploy Latest Code to EC2**

### Step 1: SSH into EC2

```bash
ssh -i your-key.pem ec2-user@43.205.128.248
```

### Step 2: Stop Running API

```bash
# Stop systemd service
sudo systemctl stop drone-api

# Or kill manually
pkill -f dotnet
ps aux | grep dotnet  # Verify it's stopped
```

### Step 3: Pull Latest Code from Git

```bash
cd ~/drone-config

# Pull latest changes
git fetch origin
git pull origin main

# Verify you have the latest code
git log -1
```

### Step 4: Clean and Rebuild

```bash
cd ~/drone-config/PavamanDroneConfigurator.API

# Clean previous build
dotnet clean

# Restore NuGet packages (IMPORTANT - updates AWS SDK)
dotnet restore

# Build with latest packages
dotnet build --configuration Release
```

### Step 5: Verify AWS SDK Versions

```bash
# Check installed packages
dotnet list package | grep AWS

# Expected output:
# AWSSDK.Core      3.7.400 (or later)
# AWSSDK.S3        3.7.400 (or later)
```

### Step 6: Run Database Migrations

```bash
# Apply any pending migrations
dotnet ef database update --project /home/ec2-user/drone-config/PavamanDroneConfigurator.API
```

### Step 7: Start API

```bash
# Start manually for testing
dotnet run --configuration Release --urls "http://0.0.0.0:5000"

# Watch for startup logs
# Should see: "AWS S3 Service initialized with EC2 IAM Role for bucket: drone-config-param-logs"
```

### Step 8: Test in Another Terminal

```bash
# Open new SSH session
ssh -i your-key.pem ec2-user@43.205.128.248

# Test endpoints
curl http://localhost:5000/health
curl http://localhost:5000/api/firmware/health
curl http://localhost:5000/api/firmware/inapp
```

### Step 9: If Tests Pass, Run as Service

```bash
# Stop manual run (Ctrl+C)

# Start as systemd service
sudo systemctl start drone-api

# Enable auto-start on reboot
sudo systemctl enable drone-api

# Check status
sudo systemctl status drone-api

# View logs
sudo journalctl -u drone-api -n 50 --no-pager
```

---

## ?? Quick Rebuild Script

Save this as `redeploy-api.sh` on EC2:

```bash
#!/bin/bash
set -e

echo "?? Stopping API..."
sudo systemctl stop drone-api || true
pkill -f dotnet || true

echo "?? Pulling latest code..."
cd ~/drone-config
git pull origin main

echo "?? Cleaning previous build..."
cd PavamanDroneConfigurator.API
dotnet clean

echo "?? Restoring packages..."
dotnet restore

echo "?? Building..."
dotnet build --configuration Release

echo "??? Updating database..."
dotnet ef database update || echo "No migrations to apply"

echo "? Starting API..."
sudo systemctl start drone-api

echo "? Waiting for API to start..."
sleep 5

echo "?? Testing endpoints..."
curl -s http://localhost:5000/health | jq '.'
echo ""
curl -s http://localhost:5000/api/firmware/health | jq '.'
echo ""

echo "? Deployment complete!"
echo "?? View logs: sudo journalctl -u drone-api -n 50 --no-pager"
```

Run it:
```bash
chmod +x redeploy-api.sh
./redeploy-api.sh
```

---

## ?? Verification Steps

### 1. Verify AWS SDK Version

```bash
cd ~/drone-config/PavamanDroneConfigurator.Infrastructure
dotnet list package | grep AWSSDK

# Should show:
# AWSSDK.Core    3.7.400
# AWSSDK.S3      3.7.400
```

### 2. Verify No Missing Methods

```bash
# Start API and watch logs
sudo journalctl -u drone-api -f

# In another terminal, test
curl http://localhost:5000/api/firmware/health

# Should NOT see "MissingMethodException"
# Should see: {"status":"healthy","message":"S3 bucket is accessible"}
```

### 3. Test All Endpoints

```bash
# Health check
curl http://localhost:5000/health

# S3 health
curl http://localhost:5000/api/firmware/health

# List firmwares
curl http://localhost:5000/api/firmware/inapp

# All should return 200 OK (or proper error if bucket empty)
```

---

## ?? Common Issues After Redeployment

### Issue: "No such file or directory"

```bash
# Verify git repo location
ls -la ~/drone-config
cd ~/drone-config
git status
```

### Issue: "Permission denied"

```bash
# Fix ownership
sudo chown -R ec2-user:ec2-user ~/drone-config
chmod +x redeploy-api.sh
```

### Issue: "Database connection failed"

```bash
# Check .env file exists
cat ~/drone-config/PavamanDroneConfigurator.API/.env

# Verify database credentials
psql -h drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com \
     -U new_app_user -d drone_configurator
```

### Issue: "Still getting MissingMethodException"

```bash
# Nuclear option: delete obj and bin folders
cd ~/drone-config/PavamanDroneConfigurator.API
rm -rf obj bin
cd ../PavamanDroneConfigurator.Infrastructure
rm -rf obj bin
cd ../PavamanDroneConfigurator.Core
rm -rf obj bin

# Rebuild everything
cd ~/drone-config
dotnet clean
dotnet restore
dotnet build --configuration Release
```

---

## ?? Expected Results After Fix

| Endpoint | Before | After |
|----------|--------|-------|
| `GET /health` | ? 200 OK | ? 200 OK |
| `GET /api/firmware/health` | ? 500 Error | ? 200 OK |
| `GET /api/firmware/inapp` | ? 500 Error | ? 200 OK |

**After Fix, /api/firmware/health should return:**
```json
{
  "status": "healthy",
  "message": "S3 bucket is accessible"
}
```

**OR (if IAM role still needs fixing):**
```json
{
  "status": "unhealthy",
  "message": "S3 bucket is not accessible",
  "error": "Access Denied"
}
```

At least you'll get a **proper error message** instead of `MissingMethodException`!

---

##  Alternative: Deploy Pre-Built Binary

If git pull doesn't work, you can build locally and copy:

### On Your Windows Machine:

```powershell
# Build release version
cd C:\Pavaman\Final-repo\PavamanDroneConfigurator.API
dotnet publish -c Release -o publish

# Copy to EC2
scp -i your-key.pem -r publish/* ec2-user@43.205.128.248:~/drone-config-publish/
```

### On EC2:

```bash
# Stop current API
sudo systemctl stop drone-api

# Backup current version
cp -r ~/drone-config ~/drone-config-backup-$(date +%Y%m%d)

# Replace with new build
rm -rf ~/drone-config/PavamanDroneConfigurator.API/bin
rm -rf ~/drone-config/PavamanDroneConfigurator.API/obj
cp -r ~/drone-config-publish/* ~/drone-config/PavamanDroneConfigurator.API/

# Start API
sudo systemctl start drone-api
```

---

## ? Success Criteria

After redeployment, you should see:

1. ? API starts without `MissingMethodException`
2. ? `/api/firmware/health` returns 200 or 503 (not 500)
3. ? `/api/firmware/inapp` returns 200 with `[]` or firmware list
4. ? Logs show "AWS S3 Service initialized"
5. ? No more `System.MissingMethodException` in logs

---

## ?? Next Steps After Fix

Once the API is running correctly:

1. **Fix IAM role** (if still getting Access Denied)
2. **Create S3 bucket** (if NoSuchBucket)
3. **Upload test firmwares**
4. **Test UI integration**

---

**The issue is NOT your code - it's the deployed build using old AWS SDK!**

**Solution: Redeploy with latest code and packages.**
