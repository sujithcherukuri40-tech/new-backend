# ?? DEPLOYMENT - 3 SIMPLE STEPS

**Problem:** API crashing on EC2, desktop app showing "Navigation Failed"  
**Solution:** Deploy the fix in 3 steps

---

## ? STEP 1: DEPLOY THE FIX

Open PowerShell in the solution directory and run:

```powershell
cd C:\Pavaman\kft-comfig
.\deploy-to-ec2.ps1 -PemFile "C:\path\to\your-ec2-key.pem"
```

**Replace** `C:\path\to\your-ec2-key.pem` with your actual PEM file location.

**What it does:**
- Builds your API
- Uploads to EC2
- Restarts the service
- Shows you the results

**Takes:** ~2-3 minutes

---

## ? STEP 2: VERIFY IT WORKS

Test the API from your computer:

```powershell
curl http://13.235.13.233:5000/health
```

**Expected response:**
```json
{"status":"healthy","timestamp":"2026-04-21T..."}
```

If you see this ? the API is working!

---

## ? STEP 3: TEST YOUR DESKTOP APP

1. Open your KFT Drone Configurator desktop app
2. Make sure API URL is set to: `http://13.235.13.233:5000`
3. Login with:
   - Email: `admin@kft.local`
   - Password: `KftAdmin@2026!`
4. Check that all features/tabs are visible
5. No "Navigation Failed" error should appear

If everything loads ? **YOU'RE DONE!**

---

## ? IF SOMETHING GOES WRONG

### Issue: PowerShell script fails

**Check your PEM file path:**
```powershell
Test-Path "C:\path\to\your-ec2-key.pem"
```
Should return `True`

**Make sure you're in the right directory:**
```powershell
Get-Location
# Should show: C:\Pavaman\kft-comfig
```

### Issue: API health check fails

**View the logs:**
```bash
ssh -i your-key.pem ubuntu@13.235.13.233 "sudo journalctl -u kft-api.service -n 50"
```

**Common causes:**
1. Database connection issue ? Check `/etc/drone-configurator/.env` on EC2
2. Service not started ? Run: `sudo systemctl start kft-api`
3. Port 5000 blocked ? Check AWS Security Group

### Issue: Desktop app can't connect

**Check Security Group:**
- AWS Console ? EC2 ? Security Groups
- Your instance's security group should allow:
  - Port 5000 from `0.0.0.0/0` (or your IP range)

**Test from browser:**
Open: `http://13.235.13.233:5000/health`

If browser can't connect ? Security group issue  
If browser works but app doesn't ? Check app API URL configuration

---

## ?? NEED MORE HELP?

See these detailed guides:
- **`FIX-COMPLETE-SUMMARY.md`** - Full explanation of what was fixed
- **`EC2-FIX-GUIDE.md`** - Detailed troubleshooting
- **`QUICK-REFERENCE.md`** - All useful commands

---

## ?? STILL STUCK?

Run these diagnostic commands and share the output:

```bash
# 1. Check service status
ssh -i your-key.pem ubuntu@13.235.13.233 "sudo systemctl status kft-api"

# 2. Get last 30 log lines
ssh -i your-key.pem ubuntu@13.235.13.233 "sudo journalctl -u kft-api.service -n 30"

# 3. Test database connection
ssh -i your-key.pem ubuntu@13.235.13.233 "source /etc/drone-configurator/.env && echo \$DB_HOST"
```

---

**That's it!** Just 3 steps to get your application working. ??
