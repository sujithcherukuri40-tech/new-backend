# ?? QUICK DEPLOYMENT REFERENCE
**EC2 IP:** `13.235.13.233`  
**User:** `ubuntu`  
**Service:** `kft-api.service`

---

## ?? DEPLOY FROM WINDOWS

### PowerShell (Recommended)
```powershell
# Make sure you're in the solution root directory
cd C:\Pavaman\kft-comfig

# Run deployment script
.\deploy-to-ec2.ps1 -PemFile "C:\path\to\your-key.pem"
```

### Manual Steps
```powershell
# 1. Build
dotnet publish PavamanDroneConfigurator.API\PavamanDroneConfigurator.API.csproj -c Release -o publish\api

# 2. Upload (using SCP)
scp -i "C:\path\to\your-key.pem" -r publish\api\* ubuntu@13.235.13.233:/opt/drone-configurator/

# 3. Restart service via SSH
ssh -i "C:\path\to\your-key.pem" ubuntu@13.235.13.233 "sudo systemctl restart kft-api"
```

---

## ?? TROUBLESHOOTING COMMANDS

### View Live Logs
```bash
ssh -i your-key.pem ubuntu@13.235.13.233
sudo journalctl -u kft-api.service -f
```

### Check Service Status
```bash
ssh -i your-key.pem ubuntu@13.235.13.233
sudo systemctl status kft-api.service
```

### View Last 100 Log Lines
```bash
ssh -i your-key.pem ubuntu@13.235.13.233
sudo journalctl -u kft-api.service -n 100 --no-pager
```

### Restart Service
```bash
ssh -i your-key.pem ubuntu@13.235.13.233
sudo systemctl restart kft-api
```

### Test API Health
```bash
# From EC2
ssh -i your-key.pem ubuntu@13.235.13.233 "curl http://localhost:5000/health"

# From your computer (PowerShell)
curl http://13.235.13.233:5000/health

# From your computer (CMD)
curl http://13.235.13.233:5000/health
```

---

## ??? COMMON FIXES

### Fix 1: Database Connection Failed
```bash
ssh -i your-key.pem ubuntu@13.235.13.233

# Check environment variables
cat /etc/drone-configurator/.env

# Test database connection
source /etc/drone-configurator/.env
PGPASSWORD=$DB_PASSWORD psql -h $DB_HOST -U $DB_USER -d $DB_NAME -c "SELECT NOW();"
```

### Fix 2: Service Won't Start
```bash
ssh -i your-key.pem ubuntu@13.235.13.233

# Check for port conflicts
sudo lsof -i :5000

# Check file permissions
ls -la /opt/drone-configurator/

# Check service configuration
cat /etc/systemd/system/kft-api.service

# Reload and restart
sudo systemctl daemon-reload
sudo systemctl restart kft-api
```

### Fix 3: "Navigation Failed" in Desktop App
1. Make sure API is running:
   ```bash
   curl http://13.235.13.233:5000/health
   ```

2. Check Security Group allows port 5000 from your IP

3. Update desktop app API URL to: `http://13.235.13.233:5000`

### Fix 4: Can't See New Features
1. Make sure you deployed the latest code:
   ```powershell
   .\deploy-to-ec2.ps1 -PemFile "C:\path\to\your-key.pem"
   ```

2. Clear desktop app cache/data and restart

3. Verify API version:
   ```bash
   curl http://13.235.13.233:5000/health
   ```

---

## ?? AWS CONFIGURATION CHECKLIST

### EC2 Security Group
Open these ports:
- ? Port 22 (SSH) - Your IP
- ? Port 5000 (HTTP) - `0.0.0.0/0` or your app IPs
- ? Port 443 (HTTPS) - If using SSL

### RDS Security Group
- ? Port 5432 (PostgreSQL) - EC2 Security Group

### IAM Role
Attach to EC2 instance with permissions for:
- ? S3 bucket: `drone-config-param-logs`
- ? SES (SendEmail, SendRawEmail)

---

## ?? HEALTH CHECK ENDPOINTS

```bash
# Health check
GET http://13.235.13.233:5000/health
# Expected: {"status":"healthy","timestamp":"2026-04-21T08:00:00Z"}

# Swagger UI (if Development mode)
http://13.235.13.233:5000/swagger

# Test authentication
POST http://13.235.13.233:5000/api/auth/login
Content-Type: application/json

{
  "email": "admin@kft.local",
  "password": "KftAdmin@2026!"
}
```

---

## ?? ROLLBACK PROCEDURE

If something goes wrong:

```bash
ssh -i your-key.pem ubuntu@13.235.13.233

# Stop current service
sudo systemctl stop kft-api

# Find backup
ls -lt /opt/ | grep drone-configurator.backup

# Restore from backup
sudo rm -rf /opt/drone-configurator
sudo mv /opt/drone-configurator.backup.YYYYMMDD_HHMMSS /opt/drone-configurator

# Restart
sudo systemctl start kft-api
```

---

## ?? SERVICE MANAGEMENT

```bash
# Start service
sudo systemctl start kft-api

# Stop service
sudo systemctl stop kft-api

# Restart service
sudo systemctl restart kft-api

# Enable auto-start on boot
sudo systemctl enable kft-api

# Disable auto-start
sudo systemctl disable kft-api

# Check if enabled
sudo systemctl is-enabled kft-api

# Check if active
sudo systemctl is-active kft-api
```

---

## ?? DEBUG MODE

To enable verbose logging:

```bash
ssh -i your-key.pem ubuntu@13.235.13.233

# Edit service file
sudo nano /etc/systemd/system/kft-api.service

# Add this line under [Service]:
Environment=ASPNETCORE_ENVIRONMENT=Development
Environment=Logging__LogLevel__Default=Debug

# Save and restart
sudo systemctl daemon-reload
sudo systemctl restart kft-api
sudo journalctl -u kft-api.service -f
```

---

## ?? EMERGENCY CONTACTS

**If you need immediate help:**

1. **Check logs first:**
   ```bash
   sudo journalctl -u kft-api.service -n 200 --no-pager > /tmp/api-logs.txt
   cat /tmp/api-logs.txt
   ```

2. **Verify environment:**
   ```bash
   cat /etc/drone-configurator/.env | grep -v PASSWORD
   ```

3. **Check disk space:**
   ```bash
   df -h
   ```

4. **Check memory:**
   ```bash
   free -h
   ```

5. **Check CPU:**
   ```bash
   top -bn1 | head -20
   ```

---

## ? SUCCESS INDICATORS

Everything is working when:
- ? `systemctl status kft-api` shows "active (running)"
- ? `curl http://13.235.13.233:5000/health` returns `{"status":"healthy"}`
- ? Logs show: `[OK] Drone Configurator API Starting`
- ? Desktop app connects successfully
- ? No "Navigation Failed" errors
- ? All features/tabs are visible

---

**Last Updated:** 2026-04-21  
**Version:** 1.0
