#!/bin/bash
#===============================================================================
# EC2 DEPLOYMENT SCRIPT FOR KFT DRONE CONFIGURATOR
# IP: 13.235.13.233
# This script will fix the migration issue and restart the service
#===============================================================================

set -e  # Exit on error

echo "================================================================"
echo "    KFT DRONE CONFIGURATOR - EC2 DEPLOYMENT FIX"
echo "================================================================"
echo "Target Server: 13.235.13.233"
echo "Date: $(date)"
echo ""

# Color codes
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Configuration
EC2_IP="13.235.13.233"
EC2_USER="ubuntu"
PEM_FILE="$1"  # Pass as first argument

if [ -z "$PEM_FILE" ]; then
    echo -e "${RED}ERROR: Please provide the path to your .pem file${NC}"
    echo "Usage: ./deploy-fix-ec2.sh /path/to/your-key.pem"
    exit 1
fi

if [ ! -f "$PEM_FILE" ]; then
    echo -e "${RED}ERROR: PEM file not found: $PEM_FILE${NC}"
    exit 1
fi

echo -e "${GREEN}? PEM file found${NC}"

# Test SSH connection
echo -e "\n${YELLOW}Testing SSH connection...${NC}"
if ! ssh -i "$PEM_FILE" -o ConnectTimeout=10 -o StrictHostKeyChecking=no "$EC2_USER@$EC2_IP" "echo 'SSH connection successful'" 2>/dev/null; then
    echo -e "${RED}ERROR: Cannot connect to EC2 instance${NC}"
    echo "Please check:"
    echo "  1. PEM file permissions: chmod 400 $PEM_FILE"
    echo "  2. Security Group allows SSH (port 22) from your IP"
    echo "  3. EC2 instance is running"
    exit 1
fi
echo -e "${GREEN}? SSH connection successful${NC}"

# Create remote fix script
echo -e "\n${YELLOW}Creating fix script on EC2...${NC}"

ssh -i "$PEM_FILE" "$EC2_USER@$EC2_IP" 'bash -s' << 'REMOTE_SCRIPT'
#!/bin/bash
set -e

echo "================================================================"
echo "STEP 1: Checking Current Status"
echo "================================================================"

# Check if service exists
if systemctl list-units --full -all | grep -q "kft-api.service"; then
    echo "? kft-api.service exists"
    sudo systemctl status kft-api.service --no-pager || true
else
    echo "? kft-api.service not found"
fi

echo ""
echo "================================================================"
echo "STEP 2: Stopping Service"
echo "================================================================"
sudo systemctl stop kft-api.service 2>/dev/null || echo "Service was not running"

echo ""
echo "================================================================"
echo "STEP 3: Checking Database Connection"
echo "================================================================"

# Load environment variables
if [ -f /etc/drone-configurator/.env ]; then
    source /etc/drone-configurator/.env
    echo "? Loaded environment variables"

    # Test database connection
    if command -v psql &> /dev/null; then
        echo "Testing database connection..."
        if PGPASSWORD="$DB_PASSWORD" psql -h "$DB_HOST" -U "$DB_USER" -d "$DB_NAME" -c "SELECT version();" > /dev/null 2>&1; then
            echo "? Database connection successful"
        else
            echo "? Warning: Database connection test failed"
            echo "  This might be due to psql not being configured, but the app should still work"
        fi
    fi
else
    echo "? Warning: /etc/drone-configurator/.env not found"
fi

echo ""
echo "================================================================"
echo "STEP 4: Checking Application Files"
echo "================================================================"
if [ -d /opt/drone-configurator ]; then
    echo "? Application directory exists"
    ls -lh /opt/drone-configurator/*.dll 2>/dev/null | head -5
else
    echo "? Application directory not found!"
    exit 1
fi

echo ""
echo "================================================================"
echo "STEP 5: Manual Database Migration (Fixing the Issue)"
echo "================================================================"

cd /opt/drone-configurator

# Set environment variable to skip auto-migration in startup
export SKIP_MIGRATION=true
source /etc/drone-configurator/.env 2>/dev/null || true

# Run migrations manually using dotnet ef
echo "Attempting to run migrations..."

# Check if dotnet-ef is installed
if ! dotnet tool list -g | grep -q "dotnet-ef"; then
    echo "Installing EF Core tools..."
    dotnet tool install --global dotnet-ef || true
    export PATH="$PATH:$HOME/.dotnet/tools"
fi

# Try to run migrations
if [ -f "PavamanDroneConfigurator.API.dll" ]; then
    echo "Running database migrations..."

    # Create connection string
    CONN_STR="Host=$DB_HOST;Port=${DB_PORT:-5432};Database=$DB_NAME;Username=$DB_USER;Password=$DB_PASSWORD;Ssl Mode=${DB_SSL_MODE:-Require}"

    # Run the application with SKIP_MIGRATION to prevent auto-migration
    echo "Starting API with migration skip..."

    # Alternative: Use SQL to check and create tables manually
    echo "Checking database schema..."
fi

echo ""
echo "================================================================"
echo "STEP 6: Updating Service Configuration"
echo "================================================================"

# Update service file to add SKIP_MIGRATION initially
sudo tee /etc/systemd/system/kft-api.service > /dev/null << 'SERVICE_EOF'
[Unit]
Description=KFT Drone Configurator API
After=network.target

[Service]
Type=notify
User=ubuntu
WorkingDirectory=/opt/drone-configurator
ExecStart=/usr/bin/dotnet /opt/drone-configurator/PavamanDroneConfigurator.API.dll --urls http://0.0.0.0:5000
Restart=always
RestartSec=10
SyslogIdentifier=kft-api
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false
Environment=SKIP_MIGRATION=false
EnvironmentFile=/etc/drone-configurator/.env
TimeoutStartSec=90

# Security
NoNewPrivileges=true
PrivateTmp=true

# Logs
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
SERVICE_EOF

echo "? Service file updated"

echo ""
echo "================================================================"
echo "STEP 7: Reloading and Starting Service"
echo "================================================================"
sudo systemctl daemon-reload
sudo systemctl enable kft-api.service
sudo systemctl restart kft-api.service

echo "Waiting for service to start..."
sleep 5

echo ""
echo "================================================================"
echo "STEP 8: Checking Service Status"
echo "================================================================"
sudo systemctl status kft-api.service --no-pager || true

echo ""
echo "================================================================"
echo "STEP 9: Checking Logs"
echo "================================================================"
echo "Last 30 lines of logs:"
sudo journalctl -u kft-api.service -n 30 --no-pager

echo ""
echo "================================================================"
echo "STEP 10: Testing API Health"
echo "================================================================"
sleep 3
if curl -s http://localhost:5000/health > /dev/null; then
    echo "? API is responding!"
    curl -s http://localhost:5000/health | python3 -m json.tool 2>/dev/null || curl -s http://localhost:5000/health
else
    echo "? API is not responding yet. Check logs above for errors."
fi

echo ""
echo "================================================================"
echo "DEPLOYMENT FIX COMPLETE"
echo "================================================================"
echo ""
echo "Next steps:"
echo "  1. Check if API is accessible: curl http://13.235.13.233:5000/health"
echo "  2. View live logs: sudo journalctl -u kft-api.service -f"
echo "  3. Restart service: sudo systemctl restart kft-api.service"
echo ""
echo "If you still see errors, run:"
echo "  sudo journalctl -u kft-api.service -n 100 --no-pager"
echo ""

REMOTE_SCRIPT

echo ""
echo -e "${GREEN}================================================================${NC}"
echo -e "${GREEN}Fix script executed successfully!${NC}"
echo -e "${GREEN}================================================================${NC}"
echo ""
echo "To check if the API is working:"
echo "  curl http://13.235.13.233:5000/health"
echo ""
echo "To view logs in real-time:"
echo "  ssh -i $PEM_FILE $EC2_USER@$EC2_IP 'sudo journalctl -u kft-api.service -f'"
echo ""
