#!/bin/bash
set -e
echo DEFINITIVE API DEPLOYMENT

echo [1/9] Stopping API service...
sudo systemctl stop drone-api 2>/dev/null || true
pkill -f dotnet 2>/dev/null || true
sleep 2

echo [2/9] Pulling latest code...
cd ~/drone-config
git fetch origin
git reset --hard origin/main

echo [3/9] Cleaning build artifacts...
rm -rf PavamanDroneConfigurator.API/bin PavamanDroneConfigurator.API/obj
rm -rf PavamanDroneConfigurator.Infrastructure/bin PavamanDroneConfigurator.Infrastructure/obj
rm -rf PavamanDroneConfigurator.Core/bin PavamanDroneConfigurator.Core/obj

echo [4/9] Restoring packages...
cd ~/drone-config/PavamanDroneConfigurator.API
dotnet restore --force --no-cache

echo [5/9] Building...
dotnet build -c Release

echo [6/9] Publishing...
dotnet publish -c Release -o ~/drone-api-published --no-restore

echo [7/9] Starting service...
sudo systemctl daemon-reload
sudo systemctl start drone-api
sleep 5

echo TESTING ENDPOINTS
curl -s http://localhost:5000/health
echo
curl -s http://localhost:5000/api/firmware/health
echo
curl -s http://localhost:5000/api/param-logs/health
echo

echo DEPLOYMENT COMPLETE
echo API Endpoints:
echo '  GET  /api/firmware/health'
echo '  GET  /api/firmware/inapp'
echo '  POST /api/firmware/param-logs'
echo '  GET  /api/param-logs/health'
echo '  GET  /api/param-logs'
echo '  GET  /api/param-logs/{key}'
