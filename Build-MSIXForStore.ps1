#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Builds MSIX package bundle for Microsoft Store submission
.DESCRIPTION
    This script builds the Pavaman Drone Configurator MSIX package for both x64 and x86 platforms
    and creates a bundle ready for Microsoft Store submission.
.EXAMPLE
    .\Build-MSIXForStore.ps1
    .\Build-MSIXForStore.ps1 -Configuration Debug
#>

param(
    [Parameter()]
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Building MSIX Package for Microsoft Store" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host ""

$SolutionPath = "PavamanDroneConfigurator.sln"
$PackageProject = "PavamanDroneConfigurator.Package\PavamanDroneConfigurator.Package.wapproj"

# Check if solution exists
if (-not (Test-Path $SolutionPath)) {
    Write-Host "Error: Solution file not found at $SolutionPath" -ForegroundColor Red
    exit 1
}

Write-Host "Configuration: $Configuration" -ForegroundColor Yellow
Write-Host "Target Platforms: x64, x86" -ForegroundColor Yellow
Write-Host ""

# Clean previous builds
Write-Host "Cleaning previous builds..." -ForegroundColor Green
& msbuild $SolutionPath /t:Clean /p:Configuration=$Configuration /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "Warning: Clean operation reported issues" -ForegroundColor Yellow
}
Write-Host ""

# Restore NuGet packages for the solution
Write-Host "Restoring NuGet packages..." -ForegroundColor Green
& msbuild $SolutionPath /t:Restore /p:Configuration=$Configuration /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: NuGet restore failed" -ForegroundColor Red
    exit 1
}
Write-Host ""

# Build for x64
Write-Host "Building for x64 platform..." -ForegroundColor Green
& msbuild $PackageProject `
    /p:Configuration=$Configuration `
    /p:Platform=x64 `
    /p:UapAppxPackageBuildMode=StoreUpload `
    /p:AppxBundle=Always `
    /p:AppxBundlePlatforms="x86|x64" `
    /maxcpucount `
    /verbosity:minimal

if ($LASTEXITCODE -ne 0) {
    Write-Host "Error: Build failed for x64" -ForegroundColor Red
    exit 1
}
Write-Host ""

Write-Host "=================================================" -ForegroundColor Green
Write-Host "  Build Completed Successfully!" -ForegroundColor Green
Write-Host "=================================================" -ForegroundColor Green
Write-Host ""

# Find the generated package
$AppPackagesPath = "PavamanDroneConfigurator.Package\AppPackages"
if (Test-Path $AppPackagesPath) {
    Write-Host "Package location:" -ForegroundColor Cyan
    Write-Host "  $AppPackagesPath" -ForegroundColor White
    Write-Host ""
    
    # Find the .msixupload file
    $UploadFile = Get-ChildItem -Path $AppPackagesPath -Filter "*.msixupload" -Recurse | Select-Object -First 1
    if ($UploadFile) {
        Write-Host "Store Upload Package:" -ForegroundColor Cyan
        Write-Host "  $($UploadFile.FullName)" -ForegroundColor White
        Write-Host "  Size: $([math]::Round($UploadFile.Length / 1MB, 2)) MB" -ForegroundColor White
    }
    
    # Find the .msixbundle file
    $BundleFile = Get-ChildItem -Path $AppPackagesPath -Filter "*.msixbundle" -Recurse | Select-Object -First 1
    if ($BundleFile) {
        Write-Host ""
        Write-Host "MSIX Bundle:" -ForegroundColor Cyan
        Write-Host "  $($BundleFile.FullName)" -ForegroundColor White
        Write-Host "  Size: $([math]::Round($BundleFile.Length / 1MB, 2)) MB" -ForegroundColor White
    }
}

Write-Host ""
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "  Next Steps:" -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "1. Go to Microsoft Partner Center" -ForegroundColor Yellow
Write-Host "   https://partner.microsoft.com/dashboard" -ForegroundColor White
Write-Host ""
Write-Host "2. Upload the .msixupload file" -ForegroundColor Yellow
Write-Host ""
Write-Host "3. Complete the Store listing information" -ForegroundColor Yellow
Write-Host "   - Description" -ForegroundColor White
Write-Host "   - Screenshots" -ForegroundColor White
Write-Host "   - Privacy policy" -ForegroundColor White
Write-Host "   - Category" -ForegroundColor White
Write-Host ""
Write-Host "4. Submit for certification" -ForegroundColor Yellow
Write-Host ""
