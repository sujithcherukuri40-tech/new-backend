# ?? EC2 DEPLOYMENT FIX GUIDE
**Server IP:** `13.235.13.233`  
**Issue:** API crashing due to database migration errors  
**Status:** ?? NEEDS IMMEDIATE FIX

---

## ?? Problem Analysis

From your error logs, the issue is:
```
Unhandled exception. System.InvalidOperationException
at Microsoft.EntityFrameworkCore.Diagnostics...
at Npgsql.EntityFrameworkCore.PostgreSQL...
```

**Root Causes:**
1. ? Database migration is failing during API startup
2. ? The exception is not being handled gracefully, causing the entire API to crash
3. ? New features can't load because the API never fully starts

---

## ? SOLUTION - Quick Fix Steps

### Step 1: Update the API Code (DONE)
The `Program.cs` has been fixed to:
- ? Handle database connection failures gracefully
- ? Allow API to start even if migrations fail (for diagnostics)
- ? Add better error logging
- ? Support `SKIP_MIGRATION` environment variable

### Step 2: Rebuild and Deploy to EC2

#### Option A: Using the Deploy Script (Recommended)

1. **Make the script executable:**
   ```bash
   chmod +x deploy-fix-ec2.sh
   ```

2. **Run the deployment:**
   ```bash
   ./deploy-fix-ec2.sh /path/to/your-ec2-key.pem
   ```

#### Option B: Manual Steps

1. **Build the project locally:**
   ```bash
   cd C:\Pavaman\kft-comfig\PavamanDroneConfigurator.API
   dotnet publish -c Release -o ../publish/api
   ```

2. **Copy to EC2:**
   ```bash
   scp -i your-key.pem -r ../publish/api/* ubuntu@13.235.13.233:/opt/drone-configurator/
   ```

3. **SSH into EC2 and restart:**
   ```bash
   ssh -i your-key.pem ubuntu@13.235.13.233
   sudo systemctl restart kft-api
   sudo journalctl -u kft-api.service -f
   ```

---

## ?? Troubleshooting on EC2

### Check Service Status
```bash
ssh -i your-key.pem ubuntu@13.235.13.233
sudo systemctl status kft-api.service
```

### View Real-time Logs
```bash
sudo journalctl -u kft-api.service -f
```

### View Last 50 Log Lines
```bash
sudo journalctl -u kft-api.service -n 50 --no-pager
```

### Test Database Connection Manually
```bash
source /etc/drone-configurator/.env
PGPASSWORD=$DB_PASSWORD psql -h $DB_HOST -U $DB_USER -d $DB_NAME -c "SELECT version();"
```

### Test API Health
```bash
curl http://localhost:5000/health
curl http://13.235.13.233:5000/health
```

---

## ?? Security Checklist

Make sure these are configured on AWS:

### 1. Security Group Rules
- ? Port 22 (SSH) - Your IP only
- ? Port 5000 (HTTP) - `0.0.0.0/0` or your app's IP range
- ? Port 443 (HTTPS) - If using reverse proxy

### 2. IAM Role (for S3 and SES)
Attach an IAM role to the EC2 instance with these permissions:
```json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": [
        "s3:GetObject",
        "s3:PutObject",
        "s3:ListBucket"
      ],
      "Resource": [
        "arn:aws:s3:::drone-config-param-logs",
        "arn:aws:s3:::drone-config-param-logs/*"
      ]
    },
    {
      "Effect": "Allow",
      "Action": [
        "ses:SendEmail",
        "ses:SendRawEmail"
      ],
      "Resource": "*"
    }
  ]
}
```

### 3. RDS Security Group
- ? Port 5432 (PostgreSQL) - Allow from EC2 security group

---

## ?? Verification Steps

After deployment, verify everything works:

### 1. API Health
```bash
curl http://13.235.13.233:5000/health
# Expected: {"status":"healthy","timestamp":"..."}
```

### 2. Database Tables
```bash
ssh -i your-key.pem ubuntu@13.235.13.233
source /etc/drone-configurator/.env
PGPASSWORD=$DB_PASSWORD psql -h $DB_HOST -U $DB_USER -d $DB_NAME -c "\dt"
# Should show: Users, RefreshTokens, ParameterLocks, __EFMigrationsHistory
```

### 3. API Endpoints
```bash
# Test login
curl -X POST http://13.235.13.233:5000/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@kft.local","password":"KftAdmin@2026!"}'
```

### 4. Service Auto-Restart
```bash
sudo systemctl restart kft-api
sleep 5
curl http://localhost:5000/health
```

---

## ?? Expected Log Output (Success)

After the fix, you should see:
```
[OK] Loaded .env file
[OK] Using DB_HOST/DB_NAME/DB_USER/DB_PASSWORD
[OK] Using JWT_SECRET_KEY from environment
Testing database connection...
[OK] Database connection successful
Applying database migrations...
[OK] Database migrations applied
[OK] Database seeding completed
================================================================
[OK] Drone Configurator API Starting
     Database: kft-db.cz4q02aqgdg8.ap-south-1.rds.amazonaws.com
     JWT Issuer: DroneConfigurator
     Environment: Production
================================================================
Now listening on: http://0.0.0.0:5000
Application started. Press Ctrl+C to shut down.
```

---

## ?? If Still Not Working

### 1. Check Database Connection
The most common issue is database connectivity:
```bash
source /etc/drone-configurator/.env
echo "Host: $DB_HOST"
echo "Database: $DB_NAME"
echo "User: $DB_USER"

# Test connection
PGPASSWORD=$DB_PASSWORD psql -h $DB_HOST -U $DB_USER -d $DB_NAME -c "SELECT NOW();"
```

### 2. Check Environment Variables
```bash
cat /etc/drone-configurator/.env
# Make sure all variables are set correctly
```

### 3. Check File Permissions
```bash
ls -la /opt/drone-configurator/
# Files should be owned by ubuntu:ubuntu
```

### 4. Check Disk Space
```bash
df -h
# Make sure there's enough space
```

### 5. Restart from Scratch
```bash
sudo systemctl stop kft-api
sudo systemctl daemon-reload
sudo systemctl start kft-api
sudo journalctl -u kft-api.service -f
```

---

## ?? Support Commands Reference

```bash
# Service management
sudo systemctl start kft-api      # Start service
sudo systemctl stop kft-api       # Stop service
sudo systemctl restart kft-api    # Restart service
sudo systemctl status kft-api     # Check status

# Logs
sudo journalctl -u kft-api.service -f                    # Follow logs
sudo journalctl -u kft-api.service -n 100 --no-pager   # Last 100 lines
sudo journalctl -u kft-api.service --since "10 min ago" # Last 10 minutes

# Configuration
cat /etc/systemd/system/kft-api.service  # View service file
cat /etc/drone-configurator/.env         # View environment variables
sudo systemctl daemon-reload              # Reload after config change

# Application
cd /opt/drone-configurator
ls -lh                                    # List files
dotnet PavamanDroneConfigurator.API.dll --version  # Check version (if supported)
```

---

## ?? Success Indicators

You'll know it's working when:
- ? `systemctl status kft-api` shows "active (running)"
- ? `curl http://13.235.13.233:5000/health` returns `{"status":"healthy"}`
- ? Desktop app can connect to the API
- ? No "Navigation Failed" errors in the UI
- ? New tabs and features are visible

---

**Last Updated:** 2026-04-21  
**Next Steps:** Run the deploy script and verify the checklist above.
