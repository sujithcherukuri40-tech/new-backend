# Pavaman Drone Configurator

A Windows-only Avalonia-based drone configurator application with Clean Architecture layout.

## Project Structure

- **PavamanDroneConfigurator.Core** - Domain layer with interfaces and models (no dependencies)
- **PavamanDroneConfigurator.Infrastructure** - Implementation layer with service implementations
- **PavamanDroneConfigurator.UI** - Avalonia UI layer with MVVM pattern using CommunityToolkit.Mvvm

## Requirements

- .NET 9.0 SDK
- Windows OS (Windows 10 or later)

## Building the Application

From the repository root, run:

```bash
dotnet build PavamanDroneConfigurator.sln
```

## Running the Application

```bash
dotnet run --project PavamanDroneConfigurator.UI/PavamanDroneConfigurator.UI.csproj
```

Or build and run the executable:

```bash
dotnet build -c Release
cd PavamanDroneConfigurator.UI/bin/Release/net9.0-windows
./PavamanDroneConfigurator.UI.exe
```

## Features

### Core Layer
- **Enums**: ConnectionType, CalibrationType, CalibrationState, FailsafeAction, FlightMode
- **Models**: ConnectionSettings, DroneParameter, SafetySettings, CalibrationStateModel
- **Interfaces**: IConnectionService, IParameterService, ICalibrationService, ISafetyService, IPersistenceService

### Infrastructure Layer
- ConnectionService - Handles Serial and TCP connections
- ParameterService - Manages drone parameters
- CalibrationService - Handles sensor calibration (Accelerometer, Compass, Gyroscope)
- SafetyService - Manages safety settings (battery failsafe, geofencing, RTL)
- PersistenceService - JSON-based profile save/load

### UI Layer
- **Connection Page** - Configure and establish drone connections (Serial/TCP)
- **Parameters Page** - Read and write drone parameters
- **Calibration Page** - Perform sensor calibrations
- **Safety Page** - Configure safety settings
- **Profile Page** - Save and load configuration profiles

## Accelerometer Calibration

The application implements Mission Planner-compatible accelerometer calibration with comprehensive validation and error handling.

### Documentation
- **[PDRL Configuration Guide](PDRL_CONFIGURATION_GUIDE.md)** - Complete reference for accelerometer calibration following PDRL standards
- **[Troubleshooting Guide](ACCELEROMETER_CALIBRATION_TROUBLESHOOTING.md)** - Quick reference for diagnosing and fixing common calibration failures
- **[Implementation Summary](IMPLEMENTATION_SUMMARY.md)** - Technical overview of calibration validation implementation

### Quick Start
1. Connect to flight controller via Serial or TCP
2. Navigate to Calibration page
3. Click "Calibrate Accelerometer"
4. **IMPORTANT:** Vehicle MUST be disarmed (90% of calibration failures are due to armed status)
5. Follow on-screen instructions to position vehicle
6. Wait for FC to complete calibration

### Common Issues
- **Position validation fails:** Check vehicle is disarmed (most common cause)
- **Calibration times out:** Ensure 2-second settle delay after each position request
- **Position rejected:** Vehicle may have moved during sampling - keep completely still

See [Troubleshooting Guide](ACCELEROMETER_CALIBRATION_TROUBLESHOOTING.md) for detailed solutions.

## Architecture

The application follows Clean Architecture principles:

1. **Core** layer contains pure contracts (interfaces, models, enums) with no external dependencies
2. **Infrastructure** layer implements Core interfaces using external libraries
3. **UI** layer depends only on Core interfaces, with dependency injection providing Infrastructure implementations

## Dependency Injection

Services are configured in `App.axaml.cs` using Microsoft.Extensions.DependencyInjection. All ViewModels receive their dependencies through constructor injection.

## Technology Stack

- **UI Framework**: Avalonia 11.3.10
- **MVVM**: CommunityToolkit.Mvvm 8.2.1
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection 9.0.0
- **Logging**: Microsoft.Extensions.Logging 9.0.0
- **Serialization**: Newtonsoft.Json 13.0.4
- **Target Framework**: .NET 9.0 (Windows-only)

## Development Notes

- The application is configured for Windows-only deployment
- Supports multiple Windows architectures: x64, x86, ARM64
- All async operations use proper async/await patterns
- Services include error handling and logging

## Next Steps for Production

- Integrate real MAVLink protocol communication (Asv.Mavlink library)
- Add real-time parameter validation
- Implement advanced calibration procedures
- Add mission planning capabilities
- Enhance UI with charts and graphs for telemetry visualization
- Add firmware update functionality
- Implement log file download and analysis

## License

[Add your license here]
