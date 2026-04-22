#!/bin/bash

# Quick Fix Script for KFT API Service
# This script attempts to fix common issues with the API service

echo "========================================"
echo "KFT API Quick Fix Script"
echo "========================================"
echo ""

# Stop the service
echo "[1/5] Stopping kft-api service..."
sudo systemctl stop kft-api
sleep 2

# Kill any orphaned processes
echo "[2/5] Checking for orphaned processes..."
if pgrep -f "PavamanDroneConfigurator.API" > /dev/null; then
    echo "  Killing orphaned processes..."
    sudo pkill -f "PavamanDroneConfigurator.API"
    sleep 2
fi

# Check if port 5000 is still in use
echo "[3/5] Checking if port 5000 is free..."
if sudo lsof -i :5000 > /dev/null 2>&1; then
    echo "  Port 5000 is still in use, killing process..."
    sudo lsof -ti :5000 | xargs sudo kill -9
    sleep 2
fi

# Start the service
echo "[4/5] Starting kft-api service..."
sudo systemctl start kft-api
sleep 5

# Verify service is running
echo "[5/5] Verifying service status..."
if systemctl is-active --quiet kft-api; then
    echo "? Service is running!"

    # Test the API
    echo ""
    echo "Testing API endpoint..."
    HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/firmware/inapp)

    if [ "$HTTP_CODE" = "200" ] || [ "$HTTP_CODE" = "401" ]; then
        echo "? API is responding! (HTTP $HTTP_CODE)"
    else
        echo "? API returned HTTP $HTTP_CODE"
        echo ""
        echo "Showing recent logs:"
        sudo journalctl -u kft-api -n 20 --no-pager
    fi
else
    echo "? Service failed to start"
    echo ""
    echo "Showing recent logs:"
    sudo journalctl -u kft-api -n 30 --no-pager
fi

echo ""
echo "========================================"
echo "Done!"
echo "========================================"
