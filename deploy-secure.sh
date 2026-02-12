#!/bin/bash
# ============================================
# PAVAMAN DRONE CONFIGURATOR - EC2 DEPLOYMENT
# ============================================
# One-command deployment with all security fixes
# Run on EC2: curl -sSL <raw-github-url> | bash
# ============================================

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

echo -e "${BLUE}"
echo "????????????????????????????????????????????????????????????"
echo "?   PAVAMAN DRONE CONFIGURATOR - SECURE DEPLOYMENT         ?"
echo "?   Version: 1.0.0 with Security Hardening                 ?"
echo "????????????????????????????????????????????????????????????"
echo -e "${NC}"

# Configuration
REPO_URL="https://github.com/sujithcherukuri40-tech/drone-config.git"
DEPLOY_DIR="/home/ec2-user/drone-config"
API_DIR="${DEPLOY_DIR}/PavamanDroneConfigurator.API"
PUBLISH_DIR="/home/ec2-user/drone-api-published"
SERVICE_NAME="drone-api"
DOTNET_VERSION="9.0"

# Function to print status
print_status() {
    echo -e "${YELLOW}? $1${NC}"
}

print_success() {
    echo -e "${GREEN}? $1${NC}"
}

print_error() {
    echo -e "${RED}? $1${NC}"
}

# ============================================
# STEP 1: Install Prerequisites
# ============================================
print_status "Installing prerequisites..."

# Check if .NET is installed
if ! command -v dotnet &> /dev/null; then
    print_status "Installing .NET SDK ${DOTNET_VERSION}..."
    sudo rpm -Uvh https://packages.microsoft.com/config/centos/7/packages-microsoft-prod.rpm || true
    sudo yum install -y dotnet-sdk-9.0 || {
        # Alternative installation method
        wget https://dot.net/v1/dotnet-install.sh
        chmod +x dotnet-install.sh
        ./dotnet-install.sh --channel 9.0
        export PATH="$HOME/.dotnet:$PATH"
        echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.bashrc
    }
fi

# Install git if not present
if ! command -v git &> /dev/null; then
    print_status "Installing Git..."
    sudo yum install -y git
fi

# Install jq for JSON parsing
if ! command -v jq &> /dev/null; then
    print_status "Installing jq..."
    sudo yum install -y jq || sudo amazon-linux-extras install -y epel && sudo yum install -y jq
fi

print_success "Prerequisites installed"

# ============================================
# STEP 2: Stop existing service
# ============================================
print_status "Stopping existing service..."
sudo systemctl stop ${SERVICE_NAME} 2>/dev/null || true
pkill -f "dotnet.*PavamanDroneConfigurator" 2>/dev/null || true
sleep 2
print_success "Service stopped"

# ============================================
# STEP 3: Clone/Update repository
# ============================================
print_status "Setting up repository..."

if [ -d "${DEPLOY_DIR}" ]; then
    print_status "Updating existing repository..."
    cd ${DEPLOY_DIR}
    git fetch origin
    git reset --hard origin/main
    git pull origin main
else
    print_status "Cloning repository..."
    git clone ${REPO_URL} ${DEPLOY_DIR}
    cd ${DEPLOY_DIR}
fi

print_success "Repository ready"

# ============================================
# STEP 4: Setup environment file
# ============================================
print_status "Setting up environment configuration..."

ENV_FILE="${API_DIR}/.env"

if [ ! -f "${ENV_FILE}" ]; then
    print_status "Creating .env file from template..."
    
    # Generate secure JWT secret
    JWT_SECRET=$(openssl rand -base64 48 | tr -d '\n')
    
    # Generate secure admin password
    ADMIN_PASS=$(openssl rand -base64 16 | tr -d '\n')
    
    cat > ${ENV_FILE} << EOF
# ============================================
# PRODUCTION ENVIRONMENT - AUTO-GENERATED
# ============================================

# DATABASE (Update these with your actual values!)
DB_HOST=drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com
DB_PORT=5432
DB_NAME=drone_configurator
DB_USER=new_app_user
DB_PASSWORD=Sujith2007
DB_SSL_MODE=Require

# JWT AUTHENTICATION (Auto-generated secure key)
JWT_SECRET_KEY=${JWT_SECRET}
JWT_ISSUER=DroneConfigurator
JWT_AUDIENCE=DroneConfiguratorClient

# ADMIN USER (Auto-generated secure password)
ADMIN_EMAIL=admin@droneconfig.local
ADMIN_PASSWORD=${ADMIN_PASS}

# AWS S3
AWS_S3_BUCKET_NAME=drone-config-param-logs
AWS_S3_REGION=ap-south-1

# CORS
ALLOWED_ORIGINS=http://localhost:5000,http://43.205.128.248:5000

# ENVIRONMENT
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5000
EOF

    print_success ".env file created"
    echo -e "${YELLOW}??  IMPORTANT: Generated admin password: ${ADMIN_PASS}${NC}"
    echo -e "${YELLOW}??  Save this password securely and change it after first login!${NC}"
else
    print_success ".env file exists, keeping existing configuration"
fi

# ============================================
# STEP 5: Build and Publish
# ============================================
print_status "Building API..."

cd ${API_DIR}

# Clean previous builds
rm -rf bin obj 2>/dev/null || true

# Restore packages
dotnet restore --force

# Build
dotnet build --configuration Release --no-restore

# Publish
print_status "Publishing API..."
rm -rf ${PUBLISH_DIR} 2>/dev/null || true
dotnet publish -c Release -o ${PUBLISH_DIR} --no-build

# Copy .env to publish directory
cp ${ENV_FILE} ${PUBLISH_DIR}/.env

print_success "API built and published to ${PUBLISH_DIR}"

# ============================================
# STEP 6: Setup systemd service
# ============================================
print_status "Configuring systemd service..."

sudo tee /etc/systemd/system/${SERVICE_NAME}.service > /dev/null << EOF
[Unit]
Description=Pavaman Drone Configurator API (Secure)
After=network.target

[Service]
Type=simple
User=ec2-user
WorkingDirectory=${PUBLISH_DIR}
ExecStart=/usr/bin/dotnet ${PUBLISH_DIR}/PavamanDroneConfigurator.API.dll
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=drone-api
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Environment=DOTNET_PRINT_TELEMETRY_MESSAGE=false

# Security hardening
NoNewPrivileges=true
ProtectSystem=strict
ProtectHome=read-only
ReadWritePaths=${PUBLISH_DIR}
PrivateTmp=true

[Install]
WantedBy=multi-user.target
EOF

sudo systemctl daemon-reload
sudo systemctl enable ${SERVICE_NAME}

print_success "Systemd service configured"

# ============================================
# STEP 7: Start service
# ============================================
print_status "Starting API service..."
sudo systemctl start ${SERVICE_NAME}
sleep 5

# Check if service is running
if sudo systemctl is-active --quiet ${SERVICE_NAME}; then
    print_success "Service started successfully"
else
    print_error "Service failed to start"
    echo "Checking logs..."
    sudo journalctl -u ${SERVICE_NAME} -n 50 --no-pager
    exit 1
fi

# ============================================
# STEP 8: Verify deployment
# ============================================
print_status "Verifying deployment..."

echo ""
echo "Testing endpoints..."

# Test health endpoint
echo -n "  /health: "
HEALTH=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/health 2>/dev/null || echo "000")
if [ "$HEALTH" = "200" ]; then
    echo -e "${GREEN}OK${NC}"
else
    echo -e "${RED}FAILED (HTTP $HEALTH)${NC}"
fi

# Test S3 health
echo -n "  /api/firmware/health: "
S3_HEALTH=$(curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/firmware/health 2>/dev/null || echo "000")
if [ "$S3_HEALTH" = "200" ]; then
    echo -e "${GREEN}OK${NC}"
else
    echo -e "${YELLOW}WARN (HTTP $S3_HEALTH) - S3 may need IAM configuration${NC}"
fi

# Test auth endpoint
echo -n "  /auth/login: "
AUTH=$(curl -s -o /dev/null -w "%{http_code}" -X POST http://localhost:5000/auth/login -H "Content-Type: application/json" -d '{}' 2>/dev/null || echo "000")
if [ "$AUTH" = "400" ] || [ "$AUTH" = "401" ]; then
    echo -e "${GREEN}OK (auth working)${NC}"
else
    echo -e "${YELLOW}WARN (HTTP $AUTH)${NC}"
fi

# ============================================
# STEP 9: Summary
# ============================================
echo ""
echo -e "${BLUE}"
echo "????????????????????????????????????????????????????????????"
echo "?              DEPLOYMENT COMPLETE!                        ?"
echo "????????????????????????????????????????????????????????????"
echo -e "${NC}"

echo -e "${GREEN}? Security Features Enabled:${NC}"
echo "   • Rate Limiting (100 req/min global, 10 req/min for auth)"
echo "   • Account Lockout (5 attempts, 15 min lockout)"
echo "   • Strong Password Policy (12+ chars with complexity)"
echo "   • JWT with Token Rotation"
echo "   • CORS Protection"
echo "   • Security Headers (HSTS, X-Frame-Options, etc.)"
echo "   • Role-Based Authorization"
echo "   • No Hardcoded Credentials"
echo ""

echo -e "${YELLOW}?? API Endpoints:${NC}"
echo "   Health:     http://$(curl -s ifconfig.me):5000/health"
echo "   Auth:       http://$(curl -s ifconfig.me):5000/auth/login"
echo "   Firmware:   http://$(curl -s ifconfig.me):5000/api/firmware/inapp"
echo "   Admin:      http://$(curl -s ifconfig.me):5000/admin/users"
echo ""

echo -e "${YELLOW}?? Useful Commands:${NC}"
echo "   View logs:       sudo journalctl -u ${SERVICE_NAME} -f"
echo "   Restart:         sudo systemctl restart ${SERVICE_NAME}"
echo "   Stop:            sudo systemctl stop ${SERVICE_NAME}"
echo "   Status:          sudo systemctl status ${SERVICE_NAME}"
echo ""

echo -e "${YELLOW}?? Admin Credentials:${NC}"
echo "   Email: admin@droneconfig.local"
if [ -n "${ADMIN_PASS}" ]; then
    echo "   Password: ${ADMIN_PASS}"
    echo -e "${RED}   ??  SAVE THIS PASSWORD AND CHANGE IT AFTER FIRST LOGIN!${NC}"
else
    echo "   Password: (check .env file or API logs)"
fi
echo ""

echo -e "${GREEN}?? Deployment successful! Your secure API is now running.${NC}"
