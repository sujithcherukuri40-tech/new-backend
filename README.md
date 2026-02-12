# Pavaman Drone Configurator

A professional Windows desktop application for drone configuration built with Avalonia UI and .NET 9.

## Features

- **Drone Connection** - Serial, TCP, and Bluetooth MAVLink connections
- **Parameter Management** - Read, write, and export drone parameters
- **Calibration** - Accelerometer, compass, and RC calibration
- **Firmware** - Flash and update drone firmware
- **Flight Modes** - Configure flight mode settings
- **Safety** - Configure failsafe and arming settings
- **Log Analysis** - Analyze flight logs with graphs
- **User Authentication** - Secure login with JWT tokens

## Architecture

```
PavamanDroneConfigurator/
??? PavamanDroneConfigurator.UI           # Avalonia Desktop App (MVVM)
??? PavamanDroneConfigurator.API          # ASP.NET Core Backend API
??? PavamanDroneConfigurator.Core         # Domain Models & Interfaces
??? PavamanDroneConfigurator.Infrastructure # Services & Data Access
```

## Requirements

- Windows 10/11 (64-bit)
- .NET 9.0 SDK
- PostgreSQL (for API backend)

## Quick Start

### Run Desktop App Only (Connects to Cloud API)

```powershell
cd PavamanDroneConfigurator.UI
dotnet run
```

### Run Local API + Desktop App

**Terminal 1 - Start API:**
```powershell
cd PavamanDroneConfigurator.API
# Create .env from .env.example first
dotnet run
```

**Terminal 2 - Start UI:**
```powershell
cd PavamanDroneConfigurator.UI
dotnet run
```

## Configuration

### API Configuration (.env)

Copy `.env.example` to `.env` and configure:

```env
DB_HOST=your-database-host
DB_PORT=5432
DB_NAME=drone_configurator
DB_USER=your_user
DB_PASSWORD=your_password
JWT_SECRET_KEY=your-secure-key-minimum-32-chars
```

## Build Release

```powershell
cd PavamanDroneConfigurator.UI
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Security Features

- Rate limiting on API endpoints
- Account lockout after failed login attempts
- Strong password policy enforcement
- JWT token rotation
- CORS protection
- Security headers (HSTS, X-Frame-Options)

## License

Proprietary - All rights reserved.
