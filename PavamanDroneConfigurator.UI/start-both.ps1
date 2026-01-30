#!/usr/bin/env pwsh
# ============================================
# Drone Configurator - Unified Startup Script
# ============================================
# This script starts both API and UI together
# Run from UI directory: .\start-both.ps1

param(
    [switch]$SkipBuild,
    [switch]$Production
)

$ErrorActionPreference = "Stop"

Write-Host @"
??????????????????????????????????????????????????????????????
?   Pavaman Drone Configurator - Startup Script             ?
??????????????????????????????????????????????????????????????
"@ -ForegroundColor Cyan

# Get solution root (go up one level from UI directory)
$solutionRoot = Split-Path -Parent $PSScriptRoot
$apiPath = Join-Path $solutionRoot "PavamanDroneConfigurator.API"
$uiPath = Join-Path $solutionRoot "PavamanDroneConfigurator.UI"

# Configuration
$configuration = if ($Production) { "Release" } else { "Debug" }

Write-Host "`n?? Solution Root: $solutionRoot" -ForegroundColor Gray
Write-Host "?? API Path: $apiPath" -ForegroundColor Gray
Write-Host "?? UI Path: $uiPath" -ForegroundColor Gray
Write-Host "??  Configuration: $configuration`n" -ForegroundColor Gray

# Verify directories exist
if (-not (Test-Path $apiPath)) {
    Write-Host "? API directory not found: $apiPath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $uiPath)) {
    Write-Host "? UI directory not found: $uiPath" -ForegroundColor Red
    exit 1
}

# Check for .env file in API
$envFile = Join-Path $apiPath ".env"
if (-not (Test-Path $envFile)) {
    Write-Host "??  Warning: .env file not found in API directory" -ForegroundColor Yellow
    Write-Host "   Expected: $envFile" -ForegroundColor Yellow
    $copyEnv = Read-Host "`nCopy from .env.example? (y/n)"
    if ($copyEnv -eq 'y') {
        $envExample = Join-Path $apiPath ".env.example"
        if (Test-Path $envExample) {
            Copy-Item $envExample $envFile
            Write-Host "? Created .env from .env.example" -ForegroundColor Green
            Write-Host "??  IMPORTANT: Edit .env and add your actual credentials!" -ForegroundColor Yellow
            Start-Sleep -Seconds 2
        }
    }
}

# Build projects if needed
if (-not $SkipBuild) {
    Write-Host "`n?? Building solution..." -ForegroundColor Yellow
    Push-Location $solutionRoot
    try {
        dotnet build --configuration $configuration
        if ($LASTEXITCODE -ne 0) {
            Write-Host "? Build failed" -ForegroundColor Red
            exit 1
        }
        Write-Host "? Build successful`n" -ForegroundColor Green
    }
    finally {
        Pop-Location
    }
}

# Apply database migrations
Write-Host "???  Applying database migrations..." -ForegroundColor Yellow
Push-Location $apiPath
try {
    dotnet ef database update --no-build
    if ($LASTEXITCODE -eq 0) {
        Write-Host "? Database migrations applied`n" -ForegroundColor Green
    } else {
        Write-Host "??  Database migration warning (continuing anyway)`n" -ForegroundColor Yellow
    }
}
catch {
    Write-Host "??  Could not apply migrations: $_" -ForegroundColor Yellow
}
finally {
    Pop-Location
}

# Start API in background job
Write-Host "?? Starting API Server..." -ForegroundColor Cyan
$apiJob = Start-Job -ScriptBlock {
    param($path, $config)
    Set-Location $path
    dotnet run --configuration $config --no-build
} -ArgumentList $apiPath, $configuration

Write-Host "   Job ID: $($apiJob.Id)" -ForegroundColor Gray

# Wait for API to be ready
Write-Host "`n? Waiting for API to start..." -ForegroundColor Yellow
$maxWaitSeconds = 30
$waited = 0
$apiReady = $false

while ($waited -lt $maxWaitSeconds) {
    Start-Sleep -Seconds 1
    $waited++
    
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5000/health" -TimeoutSec 2 -ErrorAction SilentlyContinue
        if ($response.StatusCode -eq 200) {
            $apiReady = $true
            break
        }
    }
    catch {
        # API not ready yet
        Write-Host "." -NoNewline -ForegroundColor Gray
    }
}

Write-Host ""

if ($apiReady) {
    Write-Host "? API is running on http://localhost:5000" -ForegroundColor Green
} else {
    Write-Host "??  API may still be starting... check logs if UI connection fails" -ForegroundColor Yellow
}

# Show API logs (last few lines)
Write-Host "`n?? API Output:" -ForegroundColor Gray
$apiOutput = Receive-Job -Job $apiJob
if ($apiOutput) {
    $apiOutput | Select-Object -Last 5 | ForEach-Object { Write-Host "   $_" -ForegroundColor DarkGray }
}

# Start UI (in foreground)
Write-Host "`n???  Starting UI Application..." -ForegroundColor Cyan
Write-Host "??????????????????????????????????????????????????????????`n" -ForegroundColor Cyan

Push-Location $uiPath
try {
    # UI runs in foreground - user can interact with it
    dotnet run --configuration $configuration --no-build
}
finally {
    Pop-Location
    
    # Cleanup: Stop API when UI closes
    Write-Host "`n`n?? Stopping API server..." -ForegroundColor Red
    Stop-Job -Job $apiJob -ErrorAction SilentlyContinue
    Remove-Job -Job $apiJob -Force -ErrorAction SilentlyContinue
    
    Write-Host "? Shutdown complete" -ForegroundColor Green
}
