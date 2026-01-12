# Firmware Flashing and Bootloader Feature

## Overview
This document describes the production-ready Firmware Flashing and Bootloader Update feature implemented for the Pavaman Drone Configurator, providing Mission Planner-equivalent functionality.

## Features Implemented

### 1. Firmware Upgrade (Automatic Mode)
- **Vehicle Type Selection**: Visual grid of vehicle types (Quad, Hexa, QuadPlane, Heli, Rover) with images
- **Automatic Firmware Download**: Fetches latest stable firmware from ArduPilot servers
- **Board Detection**: Automatic detection of connected flight controllers
- **One-Click Flash**: Select vehicle type and firmware is downloaded and flashed automatically

### 2. Firmware Upgrade (Manual Mode)
- **Custom Firmware Upload**: Browse and select local firmware files (.apj, .px4, .bin, .hex)
- **File Validation**: Validates firmware file format and size before flashing
- **Bootloader Mode Detection**: Waits for board to enter bootloader mode

### 3. Bootloader Update
- **Bootloader Flashing**: Update the bootloader on supported boards
- **Board Detection Status**: Shows detected board name and port
- **Progress Tracking**: Real-time progress with cancel support

## Architecture

### Core Layer (`PavamanDroneConfigurator.Core`)

#### Models (`Core/Models/FirmwareModels.cs`)
- `FirmwareType` - Vehicle type definitions
- `FirmwareVersion` - Firmware version information
- `BoardInfo` - Supported board definitions
- `FirmwareFlashState` - State machine for flash operations
- `FirmwareProgress` - Progress tracking data
- `FirmwareFlashResult` - Operation result
- `DetectedBoard` - Detected board information
- `CommonBoards` - Static list of supported boards and vehicle types

#### Interface (`Core/Interfaces/IFirmwareService.cs`)
- Board detection methods
- Firmware download and flash operations
- Bootloader update operations
- Progress events

### Infrastructure Layer (`PavamanDroneConfigurator.Infrastructure`)

#### Services
1. **`FirmwareService.cs`** - Main firmware operations service
   - Board detection via serial port scanning
   - Firmware manifest fetching from ArduPilot servers
   - Firmware download with caching
   - Flash operations coordination

2. **`Stm32Bootloader.cs`** - Low-level bootloader communication
   - PX4/ChibiOS bootloader protocol support
   - STM32 native bootloader protocol support
   - Flash erase and program operations
   - CRC verification

3. **`FirmwareDownloader.cs`** - Firmware download service
   - ArduPilot manifest parsing
   - Version filtering and selection
   - Download progress tracking
   - Local caching

### UI Layer (`PavamanDroneConfigurator.UI`)

#### ViewModel (`ViewModels/FirmwarePageViewModel.cs`)
- Mode selection (Firmware Upgrade / Bootloader Update)
- Sub-mode selection (Automatic / Manual)
- Vehicle type selection
- Progress tracking and status updates
- Log message collection

#### View (`Views/FirmwarePage.axaml`)
- Modern stepper navigation UI
- Vehicle type grid with images
- File browser for manual mode
- Progress bars and status messages
- Console log output

#### Converters (`Converters/BoolToColorConverter.cs`)
- `BoolToColorConverter` - Converts bool to Color for stepper styling
- `ZeroToBoolConverter` - For progress bar indeterminate state

## Supported Boards
- CubeOrange
- CubeBlack/Pixhawk2.1
- Pixhawk 1
- Pixhawk 4
- Pixhawk 6X
- Matek H743
- Kakute F7
- SpeedyBee F4

## Supported Vehicle Types
- Quad (ArduCopter)
- Hexa (ArduCopter)
- QuadPlane (ArduPlane)
- Heli (ArduCopter-heli)
- Rover (ArduRover)

## Usage

### Automatic Firmware Upgrade
1. Navigate to **Firmware** page
2. Select **Automatic** mode in the left sidebar
3. Click on a vehicle type (Quad, Hexa, etc.)
4. Wait for board detection and firmware download
5. Follow prompts to enter bootloader mode if needed
6. Monitor progress until completion

### Manual Firmware Upgrade
1. Navigate to **Firmware** page
2. Select **Manual** mode in the left sidebar
3. Click **Browse** to select a firmware file
4. Click **Flash Firmware**
5. Connect board in bootloader mode when prompted
6. Monitor progress until completion

### Bootloader Update
1. Navigate to **Firmware** page
2. Select **Bootloader Update** in the left sidebar
3. Click **Update** button
4. Follow prompts to complete update

## Files Created/Modified

### New Files
- `PavamanDroneConfigurator.Core/Models/FirmwareModels.cs`
- `PavamanDroneConfigurator.Core/Interfaces/IFirmwareService.cs`
- `PavamanDroneConfigurator.Infrastructure/Services/FirmwareService.cs`
- `PavamanDroneConfigurator.Infrastructure/Services/Stm32Bootloader.cs`
- `PavamanDroneConfigurator.Infrastructure/Services/FirmwareDownloader.cs`
- `PavamanDroneConfigurator.UI/ViewModels/FirmwarePageViewModel.cs`
- `PavamanDroneConfigurator.UI/Views/FirmwarePage.axaml`
- `PavamanDroneConfigurator.UI/Views/FirmwarePage.axaml.cs`
- `PavamanDroneConfigurator.UI/Converters/BoolToColorConverter.cs`

### Modified Files
- `PavamanDroneConfigurator.UI/App.axaml` - Added converters
- `PavamanDroneConfigurator.UI/App.axaml.cs` - Registered services
- `PavamanDroneConfigurator.UI/ViewModels/MainWindowViewModel.cs` - Added FirmwarePage
- `PavamanDroneConfigurator.UI/Views/MainWindow.axaml` - Added navigation and DataTemplate

## Dependencies
- System.IO.Ports (for serial communication)
- System.Net.Http (for firmware download)
- System.Text.Json (for manifest parsing)

## Notes
- Firmware cache is stored in `%LOCALAPPDATA%/PavamanDroneConfigurator/FirmwareCache`
- Manifest is cached for 30 minutes
- Supports both MAVLink v1 and v2 for reboot commands
