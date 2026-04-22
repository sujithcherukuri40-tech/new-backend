#!/bin/bash

# EC2 API Service Diagnostic and Fix Script
# Run this on your EC2 instance to diagnose and fix API issues

echo "========================================="
echo "KFT API Service Diagnostic Script"
echo "========================================="
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check 1: Service Status
echo -e "${YELLOW}[1/7] Checking service status...${NC}"
if systemctl is-active --quiet kft-api; then
    echo -e "${GREEN}? Service is running${NC}"
else
    echo -e "${RED}? Service is NOT running${NC}"
    echo "Attempting to start service..."
    sudo systemctl start kft-api
    sleep 3
    if systemctl is-active --quiet kft-api; then
        echo -e "${GREEN}? Service started successfully${NC}"
    else
        echo -e "${RED}? Failed to start service${NC}"
        echo "Checking service logs..."
        sudo journalctl -u kft-api -n 20 --no-pager
    fi
fi
echo ""

# Check 2: Port 5000 Listening
echo -e "${YELLOW}[2/7] Checking if port 5000 is open...${NC}"
if sudo netstat -tlnp 2>/dev/null | grep -q ":5000"; then
    echo -e "${GREEN}? Port 5000 is listening${NC}"
    sudo netstat -tlnp | grep ":5000"
elif sudo ss -tlnp 2>/dev/null | grep -q ":5000"; then
    echo -e "${GREEN}? Port 5000 is listening${NC}"
    sudo ss -tlnp | grep ":5000"
else
    echo -e "${RED}? Port 5000 is NOT listening${NC}"
fi
echo ""

# Check 3: Process Check
echo -e "${YELLOW}[3/7] Checking for .NET process...${NC}"
if pgrep -f "PavamanDroneConfigurator.API" > /dev/null; then
    echo -e "${GREEN}? API process is running${NC}"
    pgrep -fa "PavamanDroneConfigurator.API"
else
    echo -e "${RED}? API process is NOT running${NC}"
fi
echo ""

# Check 4: Local API Test
echo -e "${YELLOW}[4/7] Testing API locally...${NC}"
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/firmware/inapp 2>/dev/null)
if [ "$HTTP_CODE" = "200" ]; then
    echo -e "${GREEN}? API responds with HTTP 200${NC}"
elif [ "$HTTP_CODE" = "401" ]; then
    echo -e "${YELLOW}? API responds with HTTP 401 (auth required - this is OK)${NC}"
elif [ "$HTTP_CODE" = "503" ]; then
    echo -e "${RED}? API responds with HTTP 503 (Service Unavailable)${NC}"
else
    echo -e "${RED}? API responds with HTTP $HTTP_CODE${NC}"
fi
echo ""

# Check 5: Environment Variables
echo -e "${YELLOW}[5/7] Checking .env file...${NC}"
if [ -f "/var/www/kft-api/.env" ]; then
    echo -e "${GREEN}? .env file exists${NC}"
    echo "Checking required variables..."

    if grep -q "AWS_ACCESS_KEY_ID" /var/www/kft-api/.env; then
        echo -e "${GREEN}  ? AWS_ACCESS_KEY_ID configured${NC}"
    else
        echo -e "${RED}  ? AWS_ACCESS_KEY_ID missing${NC}"
    fi

    if grep -q "AWS_SECRET_ACCESS_KEY" /var/www/kft-api/.env; then
        echo -e "${GREEN}  ? AWS_SECRET_ACCESS_KEY configured${NC}"
    else
        echo -e "${RED}  ? AWS_SECRET_ACCESS_KEY missing${NC}"
    fi

    if grep -q "S3_BUCKET_NAME" /var/www/kft-api/.env; then
        echo -e "${GREEN}  ? S3_BUCKET_NAME configured${NC}"
    else
        echo -e "${RED}  ? S3_BUCKET_NAME missing${NC}"
    fi
else
    echo -e "${RED}? .env file NOT found at /var/www/kft-api/.env${NC}"
fi
echo ""

# Check 6: Recent Logs
echo -e "${YELLOW}[6/7] Recent service logs (last 20 lines)...${NC}"
sudo journalctl -u kft-api -n 20 --no-pager
echo ""

# Check 7: Disk Space
echo -e "${YELLOW}[7/7] Checking disk space...${NC}"
df -h / | tail -n 1 | awk '{print "  Used: "$3" / "$2" ("$5")"}'
echo ""

# Summary and Recommendations
echo "========================================="
echo "SUMMARY"
echo "========================================="

SERVICE_RUNNING=$(systemctl is-active kft-api)
PORT_LISTENING=$(sudo netstat -tlnp 2>/dev/null | grep -q ":5000" && echo "yes" || echo "no")
API_RESPONDING=$([ "$HTTP_CODE" = "200" ] || [ "$HTTP_CODE" = "401" ] && echo "yes" || echo "no")

if [ "$SERVICE_RUNNING" = "active" ] && [ "$PORT_LISTENING" = "yes" ] && [ "$API_RESPONDING" = "yes" ]; then
    echo -e "${GREEN}? Everything looks good!${NC}"
else
    echo -e "${RED}Issues detected. Recommended actions:${NC}"

    if [ "$SERVICE_RUNNING" != "active" ]; then
        echo "  1. Start the service: sudo systemctl start kft-api"
    fi

    if [ "$PORT_LISTENING" = "no" ]; then
        echo "  2. Check if another process is using port 5000"
        echo "     sudo lsof -i :5000"
    fi

    if [ "$API_RESPONDING" != "yes" ]; then
        echo "  3. Check application logs: sudo journalctl -u kft-api -f"
        echo "  4. Restart the service: sudo systemctl restart kft-api"
    fi

    echo ""
    echo "Quick fix: Run the following commands:"
    echo "  sudo systemctl restart kft-api"
    echo "  sleep 5"
    echo "  sudo systemctl status kft-api"
fi

echo ""
echo "========================================="
echo "To follow logs in real-time:"
echo "  sudo journalctl -u kft-api -f"
echo "========================================="
