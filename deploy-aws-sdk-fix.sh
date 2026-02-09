#!/bin/bash
# EC2 Deployment Script for AWS SDK Fix
# Run this on EC2 instance: bash deploy-aws-sdk-fix.sh

set -e  # Exit on any error

echo "?? Starting AWS SDK Fix Deployment..."

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Configuration
API_DIR="/home/ec2-user/drone-config/PavamanDroneConfigurator.API"
REPO_DIR="/home/ec2-user/drone-config"
SERVICE_NAME="drone-api"

echo -e "${YELLOW}?? Working directory: ${REPO_DIR}${NC}"

# Step 1: Stop the API service
echo -e "${YELLOW}?? Stopping ${SERVICE_NAME} service...${NC}"
sudo systemctl stop ${SERVICE_NAME} || echo "Service not running"
sleep 2

# Kill any remaining dotnet processes
pkill -f "dotnet.*PavamanDroneConfigurator" || echo "No dotnet processes to kill"

# Step 2: Backup current deployment
BACKUP_DIR="${REPO_DIR}_backup_$(date +%Y%m%d_%H%M%S)"
echo -e "${YELLOW}?? Creating backup at ${BACKUP_DIR}...${NC}"
cp -r ${REPO_DIR} ${BACKUP_DIR}
echo -e "${GREEN}? Backup created${NC}"

# Step 3: Pull latest code from Git
echo -e "${YELLOW}?? Pulling latest code from Git...${NC}"
cd ${REPO_DIR}
git fetch origin
git pull origin main
echo -e "${GREEN}? Code updated${NC}"

# Step 4: Clean previous builds
echo -e "${YELLOW}?? Cleaning previous builds...${NC}"
cd ${REPO_DIR}
find . -type d -name "bin" -exec rm -rf {} + 2>/dev/null || true
find . -type d -name "obj" -exec rm -rf {} + 2>/dev/null || true
echo -e "${GREEN}? Build directories cleaned${NC}"

# Step 5: Restore NuGet packages (CRITICAL - this updates AWS SDK)
echo -e "${YELLOW}?? Restoring NuGet packages (this will update AWS SDK)...${NC}"
cd ${API_DIR}
dotnet restore --force
echo -e "${GREEN}? Packages restored${NC}"

# Step 6: Verify AWS SDK versions
echo -e "${YELLOW}?? Verifying AWS SDK versions...${NC}"
dotnet list package | grep -i "AWSSDK"
echo ""

# Step 7: Build the API
echo -e "${YELLOW}?? Building API (Release configuration)...${NC}"
dotnet build --configuration Release --no-restore
echo -e "${GREEN}? Build completed${NC}"

# Step 8: Apply database migrations
echo -e "${YELLOW}???  Applying database migrations...${NC}"
dotnet ef database update || echo "??  No migrations to apply or migration failed (continuing...)"

# Step 9: Publish the API
echo -e "${YELLOW}?? Publishing API...${NC}"
dotnet publish -c Release -o /home/ec2-user/drone-api-published
echo -e "${GREEN}? Published to /home/ec2-user/drone-api-published${NC}"

# Step 10: Update systemd service to use published version
echo -e "${YELLOW}??  Updating systemd service configuration...${NC}"
sudo tee /etc/systemd/system/${SERVICE_NAME}.service > /dev/null <<EOF
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
EOF

sudo systemctl daemon-reload
echo -e "${GREEN}? Service configuration updated${NC}"

# Step 11: Start the API service
echo -e "${YELLOW}?? Starting ${SERVICE_NAME} service...${NC}"
sudo systemctl start ${SERVICE_NAME}
sleep 5

# Step 12: Check service status
echo -e "${YELLOW}?? Checking service status...${NC}"
sudo systemctl status ${SERVICE_NAME} --no-pager -l

# Step 13: Verify API is responding
echo -e "${YELLOW}?? Testing API endpoints...${NC}"
sleep 2

# Test health endpoint
echo -e "${YELLOW}Testing /health endpoint...${NC}"
curl -s http://localhost:5000/health | jq '.' || echo "? Health endpoint failed"

# Test S3 health endpoint
echo -e "${YELLOW}Testing /api/firmware/health endpoint...${NC}"
curl -s http://localhost:5000/api/firmware/health | jq '.' || echo "? S3 health endpoint failed"

# Test firmware list endpoint
echo -e "${YELLOW}Testing /api/firmware/inapp endpoint...${NC}"
curl -s http://localhost:5000/api/firmware/inapp | jq '.' || echo "? Firmware list endpoint failed"

# Step 14: Show recent logs
echo -e "${YELLOW}?? Recent logs (last 30 lines):${NC}"
sudo journalctl -u ${SERVICE_NAME} -n 30 --no-pager

# Step 15: Summary
echo ""
echo -e "${GREEN}???????????????????????????????????????????????????????${NC}"
echo -e "${GREEN}? Deployment Complete!${NC}"
echo -e "${GREEN}???????????????????????????????????????????????????????${NC}"
echo ""
echo -e "${YELLOW}?? Service Status:${NC}"
sudo systemctl is-active ${SERVICE_NAME} && echo -e "${GREEN}? Service is running${NC}" || echo -e "${RED}? Service is not running${NC}"
echo ""
echo -e "${YELLOW}?? Test URLs:${NC}"
echo "  Health:    http://localhost:5000/health"
echo "  S3 Health: http://localhost:5000/api/firmware/health"
echo "  Firmwares: http://localhost:5000/api/firmware/inapp"
echo ""
echo -e "${YELLOW}?? Useful Commands:${NC}"
echo "  View logs:       sudo journalctl -u ${SERVICE_NAME} -f"
echo "  Restart service: sudo systemctl restart ${SERVICE_NAME}"
echo "  Stop service:    sudo systemctl stop ${SERVICE_NAME}"
echo "  Service status:  sudo systemctl status ${SERVICE_NAME}"
echo ""
echo -e "${YELLOW}?? Backup Location: ${BACKUP_DIR}${NC}"
echo ""
echo -e "${GREEN}?? If all tests passed, the AWS SDK issue is fixed!${NC}"
echo ""
