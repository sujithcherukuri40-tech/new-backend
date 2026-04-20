# EC2 DEPLOYMENT - MANUAL STEPS

## STEP 1: On Your Local Machine (PowerShell)

### 1.1 Fix SSH key permissions (run once)
```powershell
icacls "kft-config.pem" /inheritance:r
icacls "kft-config.pem" /grant:r "$env:USERNAME`:R"
icacls "kft-config.pem" /remove "BUILTIN\Users"
icacls "kft-config.pem" /remove "NT AUTHORITY\Authenticated Users"
```

### 1.2 Publish and upload
```powershell
cd C:\Pavaman\kft-comfig
dotnet publish PavamanDroneConfigurator.API -c Release -o .\publish
scp -i kft-config.pem -r .\publish\* ec2-user@13.233.82.9:/opt/drone-configurator/
```

---

## STEP 2: SSH Into EC2

```bash
ssh -i kft-config.pem ec2-user@13.233.82.9
```

---

## STEP 3: Run These Commands on EC2 (Copy-Paste Each Block)

### 3.1 Create directories
```bash
sudo mkdir -p /opt/drone-configurator /etc/drone-configurator
sudo chown -R ec2-user:ec2-user /opt/drone-configurator /etc/drone-configurator
```

### 3.2 Create environment file
```bash
sudo tee /etc/drone-configurator/.env > /dev/null << 'EOF'
ConnectionStrings__PostgresDb=Host=kft-db.cz4q02aqgdg8.ap-south-1.rds.amazonaws.com;Database=postgres;Username=kftadmin;Password=Kftgcs2026;SSL Mode=Require;Trust Server Certificate=true
JWT_SECRET_KEY=t4X4sWxYDZ/T/KRUaczdr2kkZnbCbU2e/6AQ9iqLQ6cnol9d6Co3fmHtTe+nFT8C
JWT_ISSUER=DroneConfigurator
JWT_AUDIENCE=DroneConfiguratorClient
AWS_REGION=ap-south-1
AWS_S3_BUCKET_NAME=drone-config-param-logs
SES__SenderEmail=noreply@example.com
ADMIN_EMAIL=admin@kft.local
ADMIN_PASSWORD=KftAdmin@2026!
ALLOWED_ORIGINS=http://localhost:5000,https://localhost:5001
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5000
EOF

sudo cp /etc/drone-configurator/.env /opt/drone-configurator/.env
```

### 3.3 Create systemd service
```bash
sudo tee /etc/systemd/system/drone-configurator.service > /dev/null << 'EOF'
[Unit]
Description=Pavaman Drone Configurator API
After=network.target

[Service]
Type=simple
User=ec2-user
WorkingDirectory=/opt/drone-configurator
EnvironmentFile=/etc/drone-configurator/.env
ExecStart=/usr/bin/dotnet /opt/drone-configurator/PavamanDroneConfigurator.API.dll
Restart=always
RestartSec=10
TimeoutStartSec=300
SyslogIdentifier=drone-configurator

[Install]
WantedBy=multi-user.target
EOF
```

### 3.4 Stop old service and start new one
```bash
sudo systemctl stop kft-api 2>/dev/null
sudo systemctl disable kft-api 2>/dev/null
sudo systemctl daemon-reload
sudo systemctl enable drone-configurator
sudo systemctl start drone-configurator
```

### 3.5 Check if it's running
```bash
sleep 5
sudo systemctl status drone-configurator
```

### 3.6 Test the API
```bash
curl http://localhost:5000/health
```

**Expected output:** `{"status":"healthy","timestamp":"..."}`

---

## STEP 4: View Logs (If Something Is Wrong)

```bash
sudo journalctl -u drone-configurator -n 100 --no-pager
```

---

## STEP 5: Test From Outside

From your local machine:
```bash
curl http://13.233.82.9:5000/health
```

If it doesn't work, check Security Group allows port 5000 inbound.

---

## QUICK COMMANDS REFERENCE

| Action | Command |
|--------|---------|
| Start | `sudo systemctl start drone-configurator` |
| Stop | `sudo systemctl stop drone-configurator` |
| Restart | `sudo systemctl restart drone-configurator` |
| Status | `sudo systemctl status drone-configurator` |
| Logs | `sudo journalctl -u drone-configurator -f` |
| Test | `curl http://localhost:5000/health` |

---

## ADMIN LOGIN CREDENTIALS

- **Email:** `admin@kft.local`
- **Password:** `KftAdmin@2026!`

---

## IF APP CRASHES

Check logs:
```bash
sudo journalctl -u drone-configurator -n 50 --no-pager | grep -i error
```

Common issues:
1. **Database connection** - Check RDS security group
2. **Port 5000 in use** - `sudo lsof -i :5000`
3. **Missing .env** - Check `/opt/drone-configurator/.env` exists
