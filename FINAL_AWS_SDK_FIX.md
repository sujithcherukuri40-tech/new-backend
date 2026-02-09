# ?? FINAL AWS SDK FIX - EC2 Deployment Command

## ? What Was Fixed

**Root Cause:** `AWSSDK.SecretsManager` version 4.0.4.5 was pulling in incompatible `AWSSDK.Core` 4.0.3.10

**Solution Applied:**
1. ? Downgraded `AWSSDK.SecretsManager` from 4.0.4.5 ? 3.7.400.35
2. ? Explicitly set `AWSSDK.Core` to 3.7.500 (compatible with S3 3.7.404.7)
3. ? All AWS SDK packages now use compatible 3.7.x versions

---

## ?? Quick Deploy to EC2

### Step 1: Save Current Work (If Needed)

If you have any uncommitted changes on your local machine:

```powershell
# Check status
git status

# If there are changes, commit them
git add .
git commit -m "AWS SDK fix - use compatible 3.7.x versions"
git push origin main
```

### Step 2: SSH into EC2

```bash
ssh -i your-key.pem ec2-user@43.205.128.248
```

### Step 3: Run This Single Command (Copy & Paste)

```bash
sudo systemctl stop drone-api && \
pkill -f dotnet && \
cd ~/drone-config && \
git pull origin main && \
git log -1 --oneline && \
find . -type d -name "bin" -o -name "obj" | xargs rm -rf 2>/dev/null && \
cd ~/drone-config/PavamanDroneConfigurator.API && \
rm -rf ~/.nuget/packages/awssdk.* && \
dotnet restore --force --no-cache && \
echo "" && echo "=== AWS SDK VERSIONS ===" && \
dotnet list package | grep AWSSDK && \
echo "" && \
dotnet build -c Release 2>&1 | grep -i "warning NU" && \
dotnet publish -c Release -o ~/drone-api-published && \
sudo systemctl start drone-api && \
sleep 5 && \
echo "" && echo "=== TESTING ENDPOINTS ===" && \
curl -s http://localhost:5000/api/firmware/health
```

### Step 4: Verify Success

**Expected Output:**

```
=== AWS SDK VERSIONS ===
   > AWSSDK.Core             3.7.500      3.7.500
   > AWSSDK.S3               3.7.404.7    3.7.404.7
   > AWSSDK.SecretsManager   3.7.400.35   3.7.400.35

(No "warning NU" should appear here)

=== TESTING ENDPOINTS ===
{"status":"healthy","message":"S3 bucket is accessible"}
```

**OR** (if IAM role still needs configuration):

```
{"status":"unhealthy","error":"Access Denied"}
```

**NOT:**

```
500 Internal Server Error  ?
MissingMethodException     ?
warning NU1608             ?
```

---

## ?? Final Verification

### From EC2:

```bash
# Check service status
sudo systemctl status drone-api

# Check recent logs (should see NO MissingMethodException)
sudo journalctl -u drone-api -n 50 --no-pager | grep -i "error\|exception"

# Test all endpoints
curl http://localhost:5000/health
curl http://localhost:5000/api/firmware/health
curl http://localhost:5000/api/firmware/inapp
```

### From Windows:

```powershell
# Run status checker
powershell -ExecutionPolicy Bypass -File Check-EC2Status.ps1

# Or manual tests
curl http://43.205.128.248:5000/health
curl http://43.205.128.248:5000/api/firmware/health
curl http://43.205.128.248:5000/api/firmware/inapp
```

---

## ? Success Indicators

After deployment, you should have:

- ? No `MissingMethodException` in logs
- ? No `NU1608` package warnings
- ? All AWS SDK packages show version 3.7.x
- ? `/api/firmware/health` returns 200 or 503 (not 500)
- ? `/api/firmware/inapp` returns 200 with `[]` or firmware list
- ? UI can connect and load firmwares

---

## ?? Package Version Summary

| Package | Old Version | New Version | Status |
|---------|-------------|-------------|--------|
| AWSSDK.Core | 4.0.3.10 ? | 3.7.500 ? | Fixed |
| AWSSDK.S3 | 3.7.404.7 | 3.7.404.7 | Same |
| AWSSDK.SecretsManager | 4.0.4.5 ? | 3.7.400.35 ? | Fixed |

All packages now use compatible 3.7.x versions!

---

## ?? If Still Getting Errors

### Error: Git pull fails

```bash
cd ~/drone-config
git status
git stash
git pull origin main
git log -1
```

### Error: Still seeing NU1608 warnings

```bash
# Nuclear clean - remove all cached packages
rm -rf ~/.nuget/packages/awssdk.*
cd ~/drone-config
find . -name "bin" -o -name "obj" | xargs rm -rf
dotnet restore --force --no-cache
```

### Error: Service won't start

```bash
# Check what's wrong
sudo journalctl -u drone-api -n 100 --no-pager

# Try manual start
cd ~/drone-api-published
dotnet PavamanDroneConfigurator.API.dll
```

---

## ?? What This Fix Does

1. ? **Eliminates AWS SDK version conflicts** - All packages use compatible 3.7.x
2. ? **Removes MissingMethodException** - Methods now exist in the correct SDK version
3. ? **Enables S3 integration** - AwsS3Service can initialize properly
4. ? **Fixes firmware endpoints** - `/api/firmware/*` routes work correctly

---

## ?? Deployment Checklist

Before deployment:
- [ ] Code pushed to GitHub (commit: "AWS SDK fix")
- [ ] SSH access to EC2 available

During deployment:
- [ ] API service stopped
- [ ] Latest code pulled
- [ ] NuGet cache cleared
- [ ] Packages restored (all 3.7.x versions)
- [ ] Build successful (no NU warnings)
- [ ] API published
- [ ] Service started

After deployment:
- [ ] Health endpoint returns 200 OK
- [ ] S3 health endpoint returns 200 or 503 (not 500)
- [ ] No MissingMethodException in logs
- [ ] UI can connect to API
- [ ] Firmware page loads without errors

---

**Run the deployment command now to fix the AWS SDK issue!** ??
