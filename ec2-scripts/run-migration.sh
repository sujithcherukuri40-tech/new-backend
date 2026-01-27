#!/bin/bash
################################################################################
# EC2 Database Migration Script
# Purpose: Run EF Core migrations from EC2 to create PostgreSQL schema
# Author: Auto-generated for Drone Configurator Project
# Date: January 27, 2026
################################################################################

set -e  # Exit on error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

echo -e "${GREEN}================================${NC}"
echo -e "${GREEN}Drone Configurator - DB Migration${NC}"
echo -e "${GREEN}================================${NC}"
echo ""

# Step 1: Check if .NET SDK is installed
echo -e "${YELLOW}[1/7]${NC} Checking .NET SDK..."
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}? .NET SDK not found${NC}"
    echo "Installing .NET 9 SDK..."
    sudo dnf install -y dotnet-sdk-9.0
    echo -e "${GREEN}? .NET SDK installed${NC}"
else
    echo -e "${GREEN}? .NET SDK found: $(dotnet --version)${NC}"
fi
echo ""

# Step 2: Check if EF Core tools are installed
echo -e "${YELLOW}[2/7]${NC} Checking EF Core tools..."
if ! command -v dotnet-ef &> /dev/null; then
    echo -e "${RED}? EF Core tools not found${NC}"
    echo "Installing EF Core tools..."
    dotnet tool install --global dotnet-ef --version 9.0.0
    export PATH="$PATH:$HOME/.dotnet/tools"
    echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc
    echo -e "${GREEN}? EF Core tools installed${NC}"
else
    echo -e "${GREEN}? EF Core tools found: $(dotnet-ef --version)${NC}"
fi
echo ""

# Step 3: Check if PostgreSQL client is installed (for verification)
echo -e "${YELLOW}[3/7]${NC} Checking PostgreSQL client..."
if ! command -v psql &> /dev/null; then
    echo -e "${YELLOW}! PostgreSQL client not found (optional)${NC}"
    echo "Installing PostgreSQL client for verification..."
    sudo dnf install -y postgresql15 2>/dev/null || echo "Skipping psql install"
fi
echo ""

# Step 4: Navigate to project directory
echo -e "${YELLOW}[4/7]${NC} Checking project directory..."
PROJECT_DIR="$HOME/drone-config"

if [ ! -d "$PROJECT_DIR" ]; then
    echo -e "${RED}? Project directory not found: $PROJECT_DIR${NC}"
    echo "Cloning repository..."
    cd ~
    git clone https://github.com/sujithcherukuri40-tech/drone-config.git
    cd drone-config
    echo -e "${GREEN}? Repository cloned${NC}"
else
    echo -e "${GREEN}? Project directory found${NC}"
    cd "$PROJECT_DIR"
    
    # Pull latest changes
    echo "Pulling latest changes..."
    git pull origin main || echo "Warning: Could not pull latest changes"
fi
echo ""

# Step 5: Restore NuGet packages
echo -e "${YELLOW}[5/7]${NC} Restoring NuGet packages..."
dotnet restore
echo -e "${GREEN}? Packages restored${NC}"
echo ""

# Step 6: Apply database migration
echo -e "${YELLOW}[6/7]${NC} Applying database migration..."
echo "This will create the following tables:"
echo "  - drones"
echo "  - parameter_history"
echo "  - calibration_records"
echo "  - __EFMigrationsHistory"
echo ""

dotnet ef database update \
  --project PavamanDroneConfigurator.Infrastructure \
  --startup-project PavamanDroneConfigurator.UI \
  --context DroneDbContext

if [ $? -eq 0 ]; then
    echo -e "${GREEN}? Migration applied successfully!${NC}"
else
    echo -e "${RED}? Migration failed!${NC}"
    exit 1
fi
echo ""

# Step 7: Verify migration
echo -e "${YELLOW}[7/7]${NC} Verifying database schema..."

# Check using EF Core tools
echo "Checking applied migrations..."
dotnet ef migrations list \
  --project PavamanDroneConfigurator.Infrastructure \
  --startup-project PavamanDroneConfigurator.UI \
  --context DroneDbContext | grep Applied

# If psql is available, verify tables
if command -v psql &> /dev/null; then
    echo ""
    echo "Listing database tables..."
    PGPASSWORD=Sujith2007 psql \
      -h drone-configurator-db.cxa0c8wu0du4.ap-south-1.rds.amazonaws.com \
      -p 5432 \
      -U new_app_user \
      -d drone_configurator \
      -c "\dt" 2>/dev/null || echo "Could not verify tables with psql"
fi

echo ""
echo -e "${GREEN}================================${NC}"
echo -e "${GREEN}? Migration Complete!${NC}"
echo -e "${GREEN}================================${NC}"
echo ""
echo "Next steps:"
echo "  1. Verify tables exist in database"
echo "  2. Test application connectivity"
echo "  3. Move password to AWS Secrets Manager"
echo ""
