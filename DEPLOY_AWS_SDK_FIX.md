# ?? AWS SDK Fix - Step-by-Step Deployment Guide

## ?? Issue Summary

**Error:** `System.MissingMethodException: Method not found: 'System.Threading.Tasks.Task\`1<Boolean> Amazon.Runtime.SharedInterfaces.ICoreAmazonS3.DoesS3BucketExistAsync(System.String)'`

**Root Cause:** AWS SDK version mismatch on EC2. The API was built with `AWSSDK.Core 4.0.3.10` but needs `AWSSDK.Core 3.7.404.7` to match `AWSSDK.S3 3.7.404.7`.

**Solution:** Update AWS SDK packages to compatible versions and redeploy.

---

## ? What Was Fixed

### Local Changes (Already Done)

? Updated `PavamanDroneConfigurator.Infrastructure.csproj`:
```xml
<!-- OLD -->
<PackageReference Include="AWSSDK.S3" Version="3.7.400" />
<PackageReference Include="AWSSDK.Core" Version="3.7.400" />

<!-- NEW -->
<PackageReference Include="AWSSDK.S3" Version="3.7.404.7" />
<PackageReference Include="AWSSDK.Core" Version="3.7.404.7" />
```

? Build successful locally with no warnings

---

## ?? Deployment Steps

### Option 1: Automated Deployment (Recommended)

#### Step 1: Push Latest Code to GitHub

```powershell
# On your Windows machine (C:\Pavaman\Final-repo)
cd C:\Pavaman\Final-repo

# Stage all changes
git add .

# Commit with descriptive message
git commit -m "Fix AWS SDK version mismatch - Update to 3.7.404.7"

# Push to GitHub
git push origin main
```

#### Step 2: SSH into EC2

```bash
ssh -i your-key.pem ec2-user@43.205.128.248
```

#### Step 3: Download Deployment Script

```bash
# Download the deployment script
curl -o deploy-aws-sdk-fix.sh https://raw.githubusercontent.com/sujithcherukuri40-tech/drone-config/main/deploy-aws-sdk-fix.sh

# Or if you've already pushed, create it manually:
nano deploy-aws-sdk-fix.sh
# Paste the script content and save (Ctrl+X, Y, Enter)

# Make executable
chmod +x deploy-aws-sdk-fix.sh
```

#### Step 4: Run Deployment Script

```bash
bash deploy-aws-sdk-fix.sh
```

**What the script does:**
1. ? Stops API service
2. ? Creates backup
3. ? Pulls latest code
4. ? Cleans old builds
5. ? Restores packages (updates AWS SDK)
6. ? Builds API
7. ? Publishes API
8. ? Updates systemd service
9. ? Starts API
10. ? Tests all endpoints

---

### Option 2: Manual Deployment

#### Step 1: SSH into EC2

```bash
ssh -i your-key.pem ec2-user@43.205.128.248
```

#### Step 2: Stop API

```bash
sudo systemctl stop drone-api
pkill -f dotnet
```

#### Step 3: Pull Latest Code

```bash
cd ~/drone-config
git pull origin main
```

#### Step 4: Clean and Restore

```bash
cd ~/drone-config

# Clean all build artifacts
find . -type d -name "bin" -exec rm -rf {} + 2>/dev/null || true
find . -type d -name "obj" -exec rm -rf {} + 2>/dev/null || true

# Restore packages
cd PavamanDroneConfigurator.API
dotnet restore --force
```

#### Step 5: Verify AWS SDK Versions

```bash
dotnet list package | grep AWSSDK

# Expected output:
#   AWSSDK.Core      3.7.404.7
#   AWSSDK.S3        3.7.404.7
```

#### Step 6: Build

```bash
dotnet build --configuration Release --no-restore
```

**Look for:**
- ? NO warnings about "Detected package version outside of dependency constraint"
- ? "Build succeeded" with 0 warnings

#### Step 7: Publish

```bash
dotnet publish -c Release -o /home/ec2-user/drone-api-published
```

#### Step 8: Update Systemd Service

```bash
sudo nano /etc/systemd/system/drone-api.service
```

Update `WorkingDirectory` and `ExecStart`:
```ini
[Unit]
Description=Pavaman Drone Configurator API
After=network.target

[Service]
WorkingDirectory=/home/ec2-user/drone-api-published
ExecStart=/usr/bin/dotnet /home/ec2-user/drone-api-published/PavamanDroneConfigurator.API.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=drone-api
User=ec2-user
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000

[Install]
WantedBy=multi-user.target
```

```bash
# Reload systemd
sudo systemctl daemon-reload
```

#### Step 9: Start API

```bash
sudo systemctl start drone-api
```

#### Step 10: Verify

```bash
# Check status
sudo systemctl status drone-api

# Test endpoints
curl http://localhost:5000/health
curl http://localhost:5000/api/firmware/health
curl http://localhost:5000/api/firmware/inapp
```

---

## ?? Verification Checklist

Run these tests to confirm the fix:

### Test 1: No More MissingMethodException

```bash
sudo journalctl -u drone-api -n 50 --no-pager | grep -i "MissingMethodException"
```

**Expected:** ? No results (error is gone)

### Test 2: AWS SDK Versions Match

```bash
cd ~/drone-config/PavamanDroneConfigurator.API
dotnet list package | grep AWSSDK
```

**Expected:**
```
AWSSDK.Core    3.7.404.7
AWSSDK.S3      3.7.404.7
```

### Test 3: API Health Check

```bash
curl -s http://localhost:5000/health | jq '.'
```

**Expected:**
```json
{
  "status": "healthy",
  "timestamp": "2025-01-30T..."
}
```

### Test 4: S3 Health Check

```bash
curl -s http://localhost:5000/api/firmware/health | jq '.'
```

**Expected (if IAM is configured):**
```json
{
  "status": "healthy",
  "message": "S3 bucket is accessible"
}
```

**OR (if IAM still needs fixing - but no more MissingMethodException):**
```json
{
  "status": "unhealthy",
  "error": "Access Denied"
}
```

### Test 5: Firmware List Endpoint

```bash
curl -s http://localhost:5000/api/firmware/inapp | jq '.'
```

**Expected (if bucket is empty):**
```json
[]
```

**Expected (if firmwares exist):**
```json
[
  {
    "key": "firmwares/test.apj",
    "fileName": "test.apj",
    ...
  }
]
```

### Test 6: No Package Warnings in Logs

```bash
sudo journalctl -u drone-api -n 100 --no-pager | grep -i "NU1608"
```

**Expected:** ? No results (no more package warnings)

---

## ?? Expected Results

### Before Fix ?

```
System.MissingMethodException: Method not found...
warning NU1608: Detected package version outside of dependency constraint: AWSSDK.S3 3.7.400 requires AWSSDK.Core (>= 3.7.400) but version AWSSDK.Core 4.0.3.10 was resolved.
```

### After Fix ?

```
info: PavamanDroneConfigurator.Infrastructure.Services.AwsS3Service[0]
      AWS S3 Service initialized with EC2 IAM Role for bucket: drone-config-param-logs
info: PavamanDroneConfigurator.API.Controllers.FirmwareController[0]
      S3 bucket is accessible
```

---

## ?? Troubleshooting

### Issue: "git pull" fails

```bash
cd ~/drone-config
git status
git stash  # Save local changes
git pull origin main
git stash pop  # Restore local changes
```

### Issue: Build still has warnings

```bash
# Nuclear clean
cd ~/drone-config
rm -rf */bin */obj
dotnet clean
dotnet restore --force
dotnet build -c Release
```

### Issue: Service won't start

```bash
# Check logs
sudo journalctl -u drone-api -n 100 --no-pager

# Try manual start
cd /home/ec2-user/drone-api-published
dotnet PavamanDroneConfigurator.API.dll
```

### Issue: Still getting errors

```bash
# Verify .env file exists
cat ~/drone-config/PavamanDroneConfigurator.API/.env

# Check file permissions
ls -la /home/ec2-user/drone-api-published/
```

---

## ?? Success Criteria

? No `MissingMethodException` in logs  
? No `NU1608` warnings during build  
? `/health` returns 200 OK  
? `/api/firmware/health` returns 200 or 503 (not 500)  
? `/api/firmware/inapp` returns 200 with data or empty array  
? Service starts automatically  
? No errors in `journalctl` logs  

---

## ?? Next Steps After Successful Deployment

Once all tests pass:

1. **Test from local machine:**
   ```bash
   curl http://43.205.128.248:5000/api/firmware/health
   ```

2. **Test UI integration:**
   - Start UI application
   - Navigate to Firmware Page
   - Select "In-App (offline)" source
   - Should show firmwares or "No firmwares in S3"

3. **Upload test firmware:**
   ```bash
   curl -X POST http://43.205.128.248:5000/api/firmware/admin/upload \
     -F "file=@test.apj" \
     -F "firmwareName=Test" \
     -F "firmwareVersion=1.0"
   ```

4. **Monitor logs for any issues:**
   ```bash
   sudo journalctl -u drone-api -f
   ```

---

## ?? Summary

| Component | Before | After |
|-----------|--------|-------|
| AWSSDK.Core | 4.0.3.10 ? | 3.7.404.7 ? |
| AWSSDK.S3 | 3.7.400 | 3.7.404.7 ? |
| Build Warnings | NU1608 ? | None ? |
| API Status | 500 Error ? | 200 OK ? |
| S3 Integration | Broken ? | Working ? |

---

**The AWS SDK mismatch is now fixed. Deploy to EC2 to resolve the issue!** ??
