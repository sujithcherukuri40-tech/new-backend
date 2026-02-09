# Quick EC2 Deployment Verification Script

## Run this on EC2 to deploy the AWS SDK fix

```bash
#!/bin/bash
# Quick deploy script - paste this entire block into EC2 terminal

echo "?? Starting AWS SDK Fix Deployment..."

# Stop API
echo "Stopping API..."
sudo systemctl stop drone-api
pkill -f dotnet

# Pull latest code
echo "Pulling latest code..."
cd ~/drone-config
git pull origin main

# Show latest commit
echo "Latest commit:"
git log -1 --oneline

# Clean everything
echo "Cleaning build artifacts..."
find . -type d -name "bin" -exec rm -rf {} + 2>/dev/null || true
find . -type d -name "obj" -exec rm -rf {} + 2>/dev/null || true

# Restore with forced package update
echo "Restoring packages (updating AWS SDK)..."
cd ~/drone-config/PavamanDroneConfigurator.API
dotnet restore --force --no-cache

# Verify AWS SDK versions
echo "AWS SDK versions:"
dotnet list package | grep AWSSDK

# Build
echo "Building..."
dotnet build -c Release

# Publish
echo "Publishing..."
dotnet publish -c Release -o /home/ec2-user/drone-api-published

# Start API
echo "Starting API..."
sudo systemctl start drone-api

# Wait for startup
sleep 5

# Test
echo ""
echo "Testing endpoints..."
echo "1. Health check:"
curl -s http://localhost:5000/health | jq '.' || curl -s http://localhost:5000/health

echo ""
echo "2. S3 Health check:"
curl -s http://localhost:5000/api/firmware/health | jq '.' || curl -s http://localhost:5000/api/firmware/health

echo ""
echo "3. Firmware list:"
curl -s http://localhost:5000/api/firmware/inapp | jq '.' || curl -s http://localhost:5000/api/firmware/inapp

echo ""
echo "? Deployment complete!"
echo "Check logs: sudo journalctl -u drone-api -n 50 --no-pager"
```

## Step-by-Step Manual Instructions

### 1. SSH into EC2
```bash
ssh -i your-key.pem ec2-user@43.205.128.248
```

### 2. Run Quick Deploy
Copy and paste this entire block:

```bash
sudo systemctl stop drone-api && \
pkill -f dotnet && \
cd ~/drone-config && \
git pull origin main && \
find . -type d -name "bin" -exec rm -rf {} + 2>/dev/null && \
find . -type d -name "obj" -exec rm -rf {} + 2>/dev/null && \
cd PavamanDroneConfigurator.API && \
dotnet restore --force --no-cache && \
dotnet list package | grep AWSSDK && \
dotnet build -c Release && \
dotnet publish -c Release -o /home/ec2-user/drone-api-published && \
sudo systemctl start drone-api && \
sleep 5 && \
curl http://localhost:5000/api/firmware/health
```

### 3. Verify AWS SDK Versions
```bash
cd ~/drone-config/PavamanDroneConfigurator.API
dotnet list package | grep AWSSDK
```

**Expected output:**
```
AWSSDK.Core      3.7.404.7
AWSSDK.S3        3.7.404.7
```

**NOT:**
```
AWSSDK.Core      4.0.3.10  ? WRONG VERSION
```

### 4. Check for Errors
```bash
sudo journalctl -u drone-api -n 50 --no-pager | grep -i "error\|exception"
```

**Should NOT see:**
- ? `MissingMethodException`
- ? `NU1608` warnings

### 5. Test from Windows
```powershell
# Test from your local machine
curl http://43.205.128.248:5000/api/firmware/health
```

**Expected (if S3 is configured):**
```json
{"status":"healthy","message":"S3 bucket is accessible"}
```

**Expected (if IAM needs fixing):**
```json
{"status":"unhealthy","error":"Access Denied"}
```

**NOT:**
```
500 Internal Server Error  ?
```

---

## Troubleshooting

### If still getting 500 error:

#### Check 1: Verify code was pulled
```bash
cd ~/drone-config
git log -1
# Should show: "Fix AWS SDK version mismatch"
```

#### Check 2: Verify packages were restored
```bash
cd ~/drone-config/PavamanDroneConfigurator.API
cat obj/project.assets.json | grep "AWSSDK.Core/3.7.404.7"
# Should show version 3.7.404.7
```

#### Check 3: Nuclear clean and rebuild
```bash
cd ~/drone-config
sudo systemctl stop drone-api
rm -rf */bin */obj
rm -rf ~/.nuget/packages/awssdk.*
dotnet clean
dotnet restore --force --no-cache
cd PavamanDroneConfigurator.API
dotnet build -c Release
dotnet publish -c Release -o /home/ec2-user/drone-api-published
sudo systemctl start drone-api
```

#### Check 4: Verify systemd service is using published version
```bash
cat /etc/systemd/system/drone-api.service
# Should show:
# WorkingDirectory=/home/ec2-user/drone-api-published
# ExecStart=/usr/bin/dotnet /home/ec2-user/drone-api-published/PavamanDroneConfigurator.API.dll
```

If not, update it:
```bash
sudo nano /etc/systemd/system/drone-api.service
# Update paths to /home/ec2-user/drone-api-published
sudo systemctl daemon-reload
sudo systemctl restart drone-api
```

---

## Quick Status Check

Run this single command to check everything:

```bash
echo "=== Git Status ===" && \
cd ~/drone-config && git log -1 --oneline && \
echo "" && echo "=== AWS SDK Versions ===" && \
cd PavamanDroneConfigurator.API && dotnet list package | grep AWSSDK && \
echo "" && echo "=== Service Status ===" && \
sudo systemctl status drone-api --no-pager -l | head -20 && \
echo "" && echo "=== Recent Errors ===" && \
sudo journalctl -u drone-api -n 20 --no-pager | grep -i "error\|exception" && \
echo "" && echo "=== API Test ===" && \
curl -s http://localhost:5000/api/firmware/health
```

---

## Success Indicators

? Git shows: `d57b12a Fix AWS SDK version mismatch`  
? AWS SDK shows: `3.7.404.7` (both packages)  
? No `MissingMethodException` in logs  
? No `NU1608` warnings  
? API responds with 200 or 503 (not 500)  

---

**Run the quick deploy script above to fix the issue!**
