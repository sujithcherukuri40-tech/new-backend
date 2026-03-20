# Pavaman Drone Configurator

A professional Windows desktop application for UAV flight controller configuration built with Avalonia UI and .NET 9.

## ?? Features

- **Drone Connection** - Serial, TCP, and Bluetooth MAVLink connections
- **Parameter Management** - Read, write, and export drone parameters
- **Calibration** - Accelerometer, compass, and RC calibration
- **Firmware Management** - Flash and update flight controller firmware
- **Flight Modes** - Configure flight mode settings
- **Safety Features** - Failsafe and arming configuration
- **Log Analysis** - Flight log analysis with real-time graphs
- **Cloud Integration** - Secure authentication and cloud storage
- **User Authentication** - JWT token-based security

## ?? Project Structure

```
PavamanDroneConfigurator/
??? PavamanDroneConfigurator.UI           # Avalonia Desktop App (MVVM)
??? PavamanDroneConfigurator.API          # ASP.NET Core Backend API
??? PavamanDroneConfigurator.Core         # Domain Models & Interfaces
??? PavamanDroneConfigurator.Infrastructure # Services & Data Access
??? PavamanDroneConfigurator.Package      # MSIX Package for Microsoft Store
```

## ?? Microsoft Store Package

This application is packaged as an MSIX bundle ready for Microsoft Store submission.

### Build MSIX Package
```powershell
.\Build-MSIXForStore.ps1
```

**Documentation:**
- [MSIX Build Guide](MSIX-BUILD-GUIDE.md) - Complete build instructions
- [Quick Reference](QUICK-BUILD-REFERENCE.md) - Quick commands
- [Store Submission Checklist](STORE-SUBMISSION-CHECKLIST.md) - Pre-submission checklist
- [Build Success Report](BUILD-SUCCESS.md) - Current build status

**Package Details:**
- Version: 1.0.11.0
- Platforms: x64, x86 (bundled)
- Target: Windows 10/11 (Min: 10.0.17763.0)
- Framework: .NET 9 Windows

## ?? Requirements

- **OS:** Windows 10 (version 1809+) or Windows 11
- **Framework:** .NET 9.0 SDK
- **Database:** PostgreSQL (for API backend)
- **Development:** Visual Studio 2022 or Visual Studio Code

## ?? Quick Start

### Run Desktop App (Connects to Cloud API)

```powershell
cd PavamanDroneConfigurator.UI
dotnet run
```

### Run Local Development Setup

**Terminal 1 - Start API:**
```powershell
cd PavamanDroneConfigurator.API
# Copy .env.example to .env and configure
dotnet run
```

**Terminal 2 - Start UI:**
```powershell
cd PavamanDroneConfigurator.UI
dotnet run
```

## ?? Configuration

### API Configuration (.env)

Copy `.env.example` to `.env` and configure:

```env
DB_HOST=your-database-host
DB_PORT=5432
DB_NAME=drone_configurator
DB_USER=your_user
DB_PASSWORD=your_password
JWT_SECRET_KEY=your-secure-key-minimum-32-chars
AWS_ACCESS_KEY_ID=your-aws-key
AWS_SECRET_ACCESS_KEY=your-aws-secret
AWS_REGION=your-region
S3_BUCKET_NAME=your-bucket
```

## ?? Build from Source

### Debug Build
```powershell
dotnet build
```

### Release Build
```powershell
dotnet build -c Release
```

### Publish Standalone App
```powershell
cd PavamanDroneConfigurator.UI
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

### Build MSIX Package for Store
```powershell
.\Build-MSIXForStore.ps1
```

## ?? Security Features

- ? Rate limiting on API endpoints
- ? Account lockout after failed login attempts
- ? Strong password policy enforcement
- ? JWT token rotation and refresh
- ? CORS protection
- ? Security headers (HSTS, X-Frame-Options, CSP)
- ? HTTPS enforcement
- ? Input validation and sanitization

## ?? Testing

### Run Tests
```powershell
dotnet test
```

### Test MSIX Package Locally
```powershell
# Enable Developer Mode in Windows Settings first
Add-AppxPackage -Path "PavamanDroneConfigurator.Package\AppPackages\...\*.msixbundle"
```

## ?? Documentation

- **[MSIX Build Guide](MSIX-BUILD-GUIDE.md)** - Complete guide for building MSIX packages
- **[Quick Reference](QUICK-BUILD-REFERENCE.md)** - Quick commands and tips
- **[Store Submission Checklist](STORE-SUBMISSION-CHECKLIST.md)** - Microsoft Store submission guide
- **[Build Success Report](BUILD-SUCCESS.md)** - Latest build status and details
- **[Cleanup Summary](CLEANUP-SUMMARY.md)** - Workspace maintenance info

## ??? Technologies

- **UI Framework:** Avalonia 11.3.10
- **Backend:** ASP.NET Core 9.0
- **Database:** PostgreSQL with Entity Framework Core
- **Protocol:** MAVLink (Asv.Mavlink 3.9.0)
- **Cloud Storage:** AWS S3
- **Authentication:** JWT Tokens
- **Data Visualization:** ScottPlot, Mapsui
- **Architecture:** MVVM with CommunityToolkit.Mvvm

## ?? License

**Proprietary** - All rights reserved by Pavaman Aviation.

---

## ?? Getting Started

1. Clone the repository
2. Install .NET 9 SDK
3. Configure `.env` file for API
4. Run `dotnet restore`
5. Start the application with `dotnet run`

For Microsoft Store deployment, see [MSIX-BUILD-GUIDE.md](MSIX-BUILD-GUIDE.md).

---

**Version:** 1.0.11.0  
**Framework:** .NET 9  
**Platform:** Windows 10/11
