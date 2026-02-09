#!/bin/bash
#####################################################################
# DEFINITIVE AWS SDK FIX DEPLOYMENT SCRIPT
# Copy and paste this ENTIRE script into EC2 terminal
#####################################################################

set -e

echo "=============================================="
echo "?? DEFINITIVE AWS SDK FIX DEPLOYMENT"
echo "=============================================="

# 1. Stop everything
echo ""
echo "[1/9] Stopping API service..."
sudo systemctl stop drone-api 2>/dev/null || true
pkill -f dotnet 2>/dev/null || true
sleep 2

# 2. Pull latest code
echo ""
echo "[2/9] Pulling latest code from GitHub..."
cd ~/drone-config
git fetch origin
git reset --hard origin/main
git log -1 --oneline
echo "? Code updated"

# 3. NUCLEAR CLEAN - Remove ALL build artifacts and NuGet cache
echo ""
echo "[3/9] NUCLEAR CLEAN - Removing all build artifacts and NuGet cache..."
cd ~/drone-config
rm -rf PavamanDroneConfigurator.API/bin
rm -rf PavamanDroneConfigurator.API/obj
rm -rf PavamanDroneConfigurator.Infrastructure/bin
rm -rf PavamanDroneConfigurator.Infrastructure/obj
rm -rf PavamanDroneConfigurator.Core/bin
rm -rf PavamanDroneConfigurator.Core/obj
rm -rf PavamanDroneConfigurator.UI/bin
rm -rf PavamanDroneConfigurator.UI/obj
rm -rf ~/.nuget/packages/awssdk.*
echo "? All artifacts cleaned"

# 4. Verify csproj files have correct versions
echo ""
echo "[4/9] Verifying package versions in csproj files..."
echo "API csproj:"
grep -i "AWSSDK" ~/drone-config/PavamanDroneConfigurator.API/PavamanDroneConfigurator.API.csproj
echo ""
echo "Infrastructure csproj:"
grep -i "AWSSDK" ~/drone-config/PavamanDroneConfigurator.Infrastructure/PavamanDroneConfigurator.Infrastructure.csproj

# 5. Restore packages
echo ""
echo "[5/9] Restoring NuGet packages..."
cd ~/drone-config/PavamanDroneConfigurator.API
dotnet restore --force --no-cache

# 6. Show resolved package versions
echo ""
echo "[6/9] Resolved AWS SDK versions:"
dotnet list package | grep -i AWSSDK || echo "No AWSSDK packages found"

# 7. Build
echo ""
echo "[7/9] Building API (Release)..."
dotnet build -c Release 2>&1 | tee /tmp/build.log
echo ""
echo "Checking for NU warnings..."
grep "warning NU" /tmp/build.log || echo "? No NuGet version warnings!"

# 8. Publish
echo ""
echo "[8/9] Publishing API..."
dotnet publish -c Release -o ~/drone-api-published --no-restore

# 9. Update systemd service and start
echo ""
echo "[9/9] Updating systemd service and starting API..."

# Create/update systemd service file
sudo tee /etc/systemd/system/drone-api.service > /dev/null << 'SYSTEMD'
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
SYSTEMD

sudo systemctl daemon-reload
sudo systemctl start drone-api
sleep 5

# Final verification
echo ""
echo "=============================================="
echo "?? TESTING ENDPOINTS"
echo "=============================================="

echo ""
echo "1. Main Health Check:"
curl -s http://localhost:5000/health || echo "? FAILED"

echo ""
echo ""
echo "2. S3 Firmware Health Check:"
RESULT=$(curl -s -w "\n%{http_code}" http://localhost:5000/api/firmware/health)
HTTP_CODE=$(echo "$RESULT" | tail -1)
BODY=$(echo "$RESULT" | head -n -1)

if [ "$HTTP_CODE" == "200" ]; then
    echo "? SUCCESS! Response: $BODY"
elif [ "$HTTP_CODE" == "503" ]; then
    echo "??  S3 not accessible (IAM/permissions issue), but AWS SDK fix worked!"
    echo "Response: $BODY"
elif [ "$HTTP_CODE" == "500" ]; then
    echo "? STILL GETTING 500 ERROR!"
    echo "Response: $BODY"
    echo ""
    echo "Checking recent logs for errors..."
    sudo journalctl -u drone-api -n 30 --no-pager | grep -i "error\|exception"
else
    echo "Unknown response: HTTP $HTTP_CODE"
    echo "Body: $BODY"
fi

echo ""
echo ""
echo "3. Firmware List Endpoint:"
curl -s http://localhost:5000/api/firmware/inapp || echo "? FAILED"

echo ""
echo ""
echo "=============================================="
echo "?? DEPLOYMENT SUMMARY"
echo "=============================================="
echo ""
echo "Service Status:"
sudo systemctl is-active drone-api && echo "? drone-api is running" || echo "? drone-api is NOT running"
echo ""
echo "Recent Logs:"
sudo journalctl -u drone-api -n 10 --no-pager
echo ""
echo "=============================================="
echo "?? DEPLOYMENT COMPLETE!"
echo "=============================================="
echo ""
echo "If S3 health returns 200 or 503 (not 500), the AWS SDK fix worked!"
echo ""
echo "Test from your Windows machine:"
echo "  curl http://43.205.128.248:5000/api/firmware/health"
echo ""
