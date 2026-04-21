# ============================================================================
# EC2 DEPLOYMENT SCRIPT FOR WINDOWS
# KFT Drone Configurator - Fix Deployment
# ============================================================================
# Usage: .\deploy-to-ec2.ps1 -PemFile "C:\path\to\your-key.pem"
# ============================================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$PemFile,

    [Parameter(Mandatory=$false)]
    [string]$EC2IP = "13.235.13.233",

    [Parameter(Mandatory=$false)]
    [string]$EC2User = "ubuntu"
)

$ErrorActionPreference = "Stop"

# Color functions
function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) {
        Write-Output $args
    }
    $host.UI.RawUI.ForegroundColor = $fc
}

function Write-Success { Write-ColorOutput Green $args }
function Write-Warning { Write-ColorOutput Yellow $args }
function Write-Error { Write-ColorOutput Red $args }

Write-Host ""
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "    KFT DRONE CONFIGURATOR - EC2 DEPLOYMENT" -ForegroundColor Cyan
Write-Host "================================================================" -ForegroundColor Cyan
Write-Host "Target Server: $EC2IP"
Write-Host "Date: $(Get-Date)"
Write-Host ""

# Check if PEM file exists
if (-not (Test-Path $PemFile)) {
    Write-Error "ERROR: PEM file not found: $PemFile"
    exit 1
}
Write-Success "? PEM file found"

# Check if we have the API project
$apiProjectPath = "PavamanDroneConfigurator.API\PavamanDroneConfigurator.API.csproj"
if (-not (Test-Path $apiProjectPath)) {
    Write-Error "ERROR: API project not found at: $apiProjectPath"
    Write-Host "Make sure you're running this script from the solution root directory"
    exit 1
}
Write-Success "? API project found"

# Create publish directory
$publishPath = "publish\api"
if (Test-Path $publishPath) {
    Write-Warning "Cleaning existing publish directory..."
    Remove-Item -Recurse -Force $publishPath
}
New-Item -ItemType Directory -Force -Path $publishPath | Out-Null

Write-Host ""
Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "STEP 1: Building API (Release)" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Yellow

try {
    dotnet publish $apiProjectPath -c Release -o $publishPath --self-contained false
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed"
    }
    Write-Success "? Build completed successfully"
}
catch {
    Write-Error "ERROR: Build failed - $($_.Exception.Message)"
    exit 1
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "STEP 2: Preparing Deployment Package" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Yellow

$publishFiles = Get-ChildItem -Path $publishPath
Write-Host "Published files: $($publishFiles.Count)"
Write-Success "? Deployment package ready"

Write-Host ""
Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "STEP 3: Checking SSH Connection" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Yellow

# Test SSH connection using built-in SSH (Windows 10+)
try {
    $testCmd = "ssh -i `"$PemFile`" -o ConnectTimeout=10 -o StrictHostKeyChecking=no $EC2User@$EC2IP `"echo 'SSH connection successful'`""
    $result = Invoke-Expression $testCmd 2>&1

    if ($result -match "SSH connection successful") {
        Write-Success "? SSH connection successful"
    }
    else {
        throw "Connection test failed"
    }
}
catch {
    Write-Error "ERROR: Cannot connect to EC2 instance"
    Write-Host ""
    Write-Host "Please check:" -ForegroundColor Yellow
    Write-Host "  1. Security Group allows SSH (port 22) from your IP"
    Write-Host "  2. EC2 instance is running"
    Write-Host "  3. PEM file has correct permissions"
    Write-Host "  4. OpenSSH Client is installed (Windows 10+ feature)"
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "STEP 4: Stopping Remote Service" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Yellow

try {
    ssh -i "$PemFile" "$EC2User@$EC2IP" "sudo systemctl stop kft-api.service 2>/dev/null || echo 'Service was not running'"
    Write-Success "? Service stopped"
}
catch {
    Write-Warning "? Could not stop service (might not exist yet)"
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "STEP 5: Uploading Files to EC2" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Yellow

try {
    # Create backup of existing deployment
    ssh -i "$PemFile" "$EC2User@$EC2IP" @"
        if [ -d /opt/drone-configurator ]; then
            echo 'Creating backup...'
            sudo cp -r /opt/drone-configurator /opt/drone-configurator.backup.`$(date +%Y%m%d_%H%M%S)
            sudo rm -rf /opt/drone-configurator/*
        else
            echo 'Creating directory...'
            sudo mkdir -p /opt/drone-configurator
        fi
        sudo chown -R ubuntu:ubuntu /opt/drone-configurator
"@

    Write-Host "Uploading files (this may take a moment)..."
    scp -i "$PemFile" -r "$publishPath\*" "${EC2User}@${EC2IP}:/opt/drone-configurator/"

    Write-Success "? Files uploaded successfully"
}
catch {
    Write-Error "ERROR: File upload failed - $($_.Exception.Message)"
    exit 1
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "STEP 6: Setting Up Service" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Yellow

try {
    ssh -i "$PemFile" "$EC2User@$EC2IP" @"
        # Create systemd service file
        sudo tee /etc/systemd/system/kft-api.service > /dev/null << 'SERVICEEOF'
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
SERVICEEOF

        # Reload systemd
        sudo systemctl daemon-reload
        sudo systemctl enable kft-api.service

        echo 'Service configured successfully'
"@

    Write-Success "? Service configured"
}
catch {
    Write-Error "ERROR: Service setup failed - $($_.Exception.Message)"
    exit 1
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "STEP 7: Starting Service" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Yellow

try {
    ssh -i "$PemFile" "$EC2User@$EC2IP" @"
        sudo systemctl start kft-api.service
        sleep 3
        sudo systemctl status kft-api.service --no-pager || true
"@

    Write-Success "? Service started"
}
catch {
    Write-Warning "? Service may have started with errors - check logs below"
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "STEP 8: Checking Logs" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Yellow

ssh -i "$PemFile" "$EC2User@$EC2IP" "sudo journalctl -u kft-api.service -n 30 --no-pager"

Write-Host ""
Write-Host "================================================================" -ForegroundColor Yellow
Write-Host "STEP 9: Testing API" -ForegroundColor Yellow
Write-Host "================================================================" -ForegroundColor Yellow

Start-Sleep -Seconds 3

try {
    $healthCheck = ssh -i "$PemFile" "$EC2User@$EC2IP" "curl -s http://localhost:5000/health"

    if ($healthCheck -match "healthy") {
        Write-Success "? API is responding!"
        Write-Host $healthCheck
    }
    else {
        Write-Warning "? API is not responding yet. Check logs above for errors."
    }
}
catch {
    Write-Warning "? Could not test API health"
}

Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "DEPLOYMENT COMPLETE" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "API URL: http://$EC2IP:5000" -ForegroundColor Cyan
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Test API: curl http://$EC2IP:5000/health"
Write-Host "  2. View logs: ssh -i `"$PemFile`" $EC2User@$EC2IP 'sudo journalctl -u kft-api.service -f'"
Write-Host "  3. Check status: ssh -i `"$PemFile`" $EC2User@$EC2IP 'sudo systemctl status kft-api'"
Write-Host ""
Write-Host "To test from your desktop app, update the API URL to: http://$EC2IP:5000" -ForegroundColor Cyan
Write-Host ""
