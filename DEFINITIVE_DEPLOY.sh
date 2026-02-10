#!/bin/bash
set -e
echo DEFINITIVE API DEPLOYMENT
echo ========================

echo [1/7] Stopping API service...
sudo systemctl stop drone-api 2>/dev/null || true
pkill -f dotnet 2>/dev/null || true
sleep 2

echo [2/7] Pulling latest code...
cd ~/drone-config
git fetch origin
git reset --hard origin/main
git log -1 --oneline

echo [3/7] Cleaning build artifacts...
rm -rf PavamanDroneConfigurator.API/bin PavamanDroneConfigurator.API/obj
rm -rf PavamanDroneConfigurator.Infrastructure/bin PavamanDroneConfigurator.Infrastructure/obj
rm -rf PavamanDroneConfigurator.Core/bin PavamanDroneConfigurator.Core/obj

echo [4/7] Restoring packages...
cd ~/drone-config/PavamanDroneConfigurator.API
dotnet restore --force --no-cache

echo [5/7] Building...
dotnet build -c Release

echo [6/7] Publishing...
dotnet publish -c Release -o ~/drone-api-published --no-restore

echo [7/7] Starting service...
sudo systemctl daemon-reload
sudo systemctl start drone-api
sleep 5

echo ========================
echo TESTING ENDPOINTS
echo ========================
echo 1. Health:
curl -s http://localhost:5000/health
echo
echo 2. Firmware Health:
curl -s http://localhost:5000/api/firmware/health
echo
echo 3. Param Logs Health:
curl -s http://localhost:5000/api/param-logs/health
echo
echo 4. Param Logs List:
curl -s 'http://localhost:5000/api/param-logs?page=1'
echo

echo ========================
echo DEPLOYMENT COMPLETE
echo ========================
echo Endpoints:
echo '  GET  /api/firmware/health'
echo '  GET  /api/firmware/inapp'
echo '  POST /api/firmware/param-logs - Upload param changes'
echo '  GET  /api/param-logs - List logs'
echo '  GET  /api/param-logs/{key} - Get log content'
echo '  GET  /api/param-logs/download/{key} - Download'

