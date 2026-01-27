using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PavamanDroneConfigurator.Core.Models;

/// <summary>
/// Represents a firmware type/vehicle category - Mission Planner compatible
/// </summary>
public class FirmwareType
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public VehicleClass VehicleClass { get; set; }
    
    /// <summary>
    /// ArduPilot firmware identifier (e.g., "Copter", "Plane", "Rover")
    /// </summary>
    public string ArduPilotId { get; set; } = string.Empty;
    
    /// <summary>
    /// Current/Latest firmware version string (e.g., "V4.6.3 OFFICIAL")
    /// </summary>
    public string LatestVersion { get; set; } = string.Empty;
    
    /// <summary>
    /// Release type: OFFICIAL, BETA, DEV
    /// </summary>
    public string ReleaseType { get; set; } = "OFFICIAL";
    
    /// <summary>
    /// MAV_TYPE value for this vehicle
    /// </summary>
    public int MavType { get; set; }
    
    /// <summary>
    /// Display order in the grid
    /// </summary>
    public int DisplayOrder { get; set; }
}

/// <summary>
/// Vehicle classification matching ArduPilot types
/// </summary>
public enum VehicleClass
{
    Copter,
    Plane,
    Rover,
    Sub,
    AntennaTracker,
    Blimp
}

/// <summary>
/// Represents a specific firmware version available for download
/// </summary>
public class FirmwareVersion
{
    public string Version { get; set; } = string.Empty;
    public string ReleaseType { get; set; } = "stable"; // stable, beta, dev
    public string BoardType { get; set; } = string.Empty;
    public int BoardId { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime ReleaseDate { get; set; }
    public string GitHash { get; set; } = string.Empty;
    public string ChangeLog { get; set; } = string.Empty;
    public bool IsLatest { get; set; }
    public string Platform { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    
    /// <summary>
    /// Display string like "V4.6.3 OFFICIAL"
    /// </summary>
    public string DisplayVersion => $"V{Version} {ReleaseType.ToUpperInvariant()}";
}

/// <summary>
/// Supported flight controller board types
/// </summary>
public class BoardInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string BootloaderProtocol { get; set; } = "stm32";
    public int FlashSize { get; set; }
    public bool SupportsBootloaderUpdate { get; set; }
    public int VendorId { get; set; }
    public int ProductId { get; set; }
    public int BootloaderProductId { get; set; }
    
    /// <summary>
    /// Board ID used in ArduPilot firmware manifest
    /// </summary>
    public int BoardId { get; set; }
}

/// <summary>
/// Firmware flashing operation mode
/// </summary>
public enum FirmwareFlashMode
{
    Automatic,
    Manual,
    CustomFile,
    Beta,
    Dev
}

/// <summary>
/// Firmware flashing operation state
/// </summary>
public enum FirmwareFlashState
{
    Idle,
    DetectingBoard,
    WaitingForBootloader,
    DownloadingFirmware,
    ErasingFlash,
    Programming,
    Verifying,
    Rebooting,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Bootloader update state
/// </summary>
public enum BootloaderUpdateState
{
    Idle,
    CheckingCurrentVersion,
    DownloadingBootloader,
    EnteringBootloader,
    ErasingBootloader,
    ProgrammingBootloader,
    Verifying,
    Rebooting,
    Completed,
    Failed
}

/// <summary>
/// Progress information for firmware operations
/// </summary>
public class FirmwareProgress
{
    public FirmwareFlashState State { get; set; }
    public double ProgressPercent { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public string DetailMessage { get; set; } = string.Empty;
    public long BytesTransferred { get; set; }
    public long TotalBytes { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public TimeSpan? EstimatedTimeRemaining { get; set; }
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Result of a firmware flash operation
/// </summary>
public class FirmwareFlashResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorDetails { get; set; }
    public TimeSpan Duration { get; set; }
    public string FirmwareVersion { get; set; } = string.Empty;
    public string BoardType { get; set; } = string.Empty;
}

/// <summary>
/// Detected board information from bootloader
/// </summary>
public class DetectedBoard
{
    public string BoardId { get; set; } = string.Empty;
    public string BoardName { get; set; } = string.Empty;
    public string BootloaderVersion { get; set; } = string.Empty;
    public int FlashSize { get; set; }
    public string SerialPort { get; set; } = string.Empty;
    public bool IsInBootloader { get; set; }
    public string CurrentFirmware { get; set; } = string.Empty;
    public DateTime? DetectedAt { get; set; }
    public int BoardIdNumeric { get; set; }
}

/// <summary>
/// Firmware manifest from ArduPilot firmware server
/// The manifest.json has structure: { "format-version": "1.0.0", "firmware": [...] }
/// </summary>
public class FirmwareManifest
{
    [JsonPropertyName("format-version")]
    public string FormatVersion { get; set; } = string.Empty;
    
    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;
    
    [JsonPropertyName("firmware")]
    public List<FirmwareEntry> Firmware { get; set; } = new();
}

/// <summary>
/// Individual firmware entry in the manifest.
/// Uses JsonPropertyName to match ArduPilot's JSON field names exactly.
/// Based on Mission Planner's APFirmware.FirmwareInfo class.
/// </summary>
public class FirmwareEntry
{
    [JsonPropertyName("mav-type")]
    public string MavType { get; set; } = string.Empty;
    
    /// <summary>
    /// Board ID - ArduPilot manifest uses a number, not string
    /// </summary>
    [JsonPropertyName("board_id")]
    public long BoardIdLong { get; set; }
    
    [JsonPropertyName("platform")]
    public string Platform { get; set; } = string.Empty;
    
    [JsonPropertyName("vehicletype")]
    public string VehicleType { get; set; } = string.Empty;
    
    [JsonPropertyName("format")]
    public string Format { get; set; } = string.Empty;
    
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;
    
    [JsonPropertyName("git-sha")]
    public string GitHash { get; set; } = string.Empty;
    
    /// <summary>
    /// The "latest" field in manifest - is a long/int (0 or 1)
    /// </summary>
    [JsonPropertyName("latest")]
    public long LatestLong { get; set; }
    
    /// <summary>
    /// Computed property to convert to bool
    /// </summary>
    [JsonIgnore]
    public bool Latest => LatestLong == 1;
    
    /// <summary>
    /// Firmware version string like "4.5.7" or "4.6.0-beta1"
    /// </summary>
    [JsonPropertyName("mav-firmware-version")]
    public string MavFirmwareVersion { get; set; } = string.Empty;
    
    /// <summary>
    /// Release type - OFFICIAL, BETA, DEV, etc.
    /// Mission Planner calls stable releases "OFFICIAL"
    /// </summary>
    [JsonPropertyName("mav-firmware-version-type")]
    public string MavFirmwareVersionType { get; set; } = string.Empty;
    
    [JsonPropertyName("mav-firmware-version-str")]
    public string MavFirmwareVersionStr { get; set; } = string.Empty;
    
    [JsonPropertyName("frame-type")]
    public string FrameType { get; set; } = string.Empty;
    
    [JsonPropertyName("image-size")]
    public long ImageSize { get; set; }
    
    [JsonPropertyName("brand_name")]
    public string BrandName { get; set; } = string.Empty;
    
    [JsonPropertyName("manufacturer")]
    public string Manufacturer { get; set; } = string.Empty;
    
    /// <summary>
    /// Board ID string for compatibility with existing code
    /// </summary>
    [JsonIgnore]
    public string BoardId => BoardIdLong.ToString();
    
    /// <summary>
    /// Numeric board ID for board validation
    /// </summary>
    [JsonIgnore]
    public int? BoardIdNumeric => (int)BoardIdLong;
    
    /// <summary>
    /// Returns the release type category normalized for filtering.
    /// Mission Planner uses: OFFICIAL (stable), BETA, DEV (latest)
    /// </summary>
    [JsonIgnore]
    public string ReleaseCategory
    {
        get
        {
            if (string.IsNullOrEmpty(MavFirmwareVersionType))
                return "UNKNOWN";
                
            var upper = MavFirmwareVersionType.ToUpperInvariant();
            
            // Mission Planner convention: OFFICIAL = stable releases
            if (upper.StartsWith("OFFICIAL") || upper.StartsWith("STABLE"))
                return "OFFICIAL";
            if (upper.StartsWith("BETA") || upper.StartsWith("RC"))
                return "BETA";
            if (upper.StartsWith("DEV") || upper.StartsWith("LATEST"))
                return "DEV";
                
            return upper;
        }
    }
}

/// <summary>
/// STM32 bootloader command identifiers
/// </summary>
public static class Stm32BootloaderCommands
{
    public const byte GET = 0x00;
    public const byte GET_VERSION = 0x01;
    public const byte GET_ID = 0x02;
    public const byte READ_MEMORY = 0x11;
    public const byte GO = 0x21;
    public const byte WRITE_MEMORY = 0x31;
    public const byte ERASE = 0x43;
    public const byte EXTENDED_ERASE = 0x44;
    public const byte WRITE_PROTECT = 0x63;
    public const byte WRITE_UNPROTECT = 0x73;
    public const byte READOUT_PROTECT = 0x82;
    public const byte READOUT_UNPROTECT = 0x92;
    
    public const byte ACK = 0x79;
    public const byte NACK = 0x1F;
    public const byte SYNC = 0x7F;
}

/// <summary>
/// Mission Planner compatible vehicle type definitions
/// Matches firmware.cs vehicle options exactly
/// </summary>
public static class CommonBoards
{
    public static readonly BoardInfo[] SupportedBoards = new[]
    {
        new BoardInfo
        {
            Id = "fmuv2",
            Name = "Pixhawk1",
            Manufacturer = "3DR/mRo",
            Description = "Original Pixhawk",
            BootloaderProtocol = "px4",
            FlashSize = 2048,
            SupportsBootloaderUpdate = true,
            BoardId = 9
        },
        new BoardInfo
        {
            Id = "fmuv3",
            Name = "Pixhawk2/Cube",
            Manufacturer = "Hex/ProfiCNC",
            Description = "Pixhawk 2.1 Cube",
            BootloaderProtocol = "px4",
            FlashSize = 2048,
            SupportsBootloaderUpdate = true,
            BoardId = 9
        },
        new BoardInfo
        {
            Id = "fmuv4",
            Name = "Pixracer",
            Manufacturer = "mRo",
            Description = "Pixracer",
            BootloaderProtocol = "px4",
            FlashSize = 2048,
            SupportsBootloaderUpdate = true,
            BoardId = 11
        },
        new BoardInfo
        {
            Id = "fmuv5",
            Name = "Pixhawk4",
            Manufacturer = "Holybro",
            Description = "Pixhawk 4",
            BootloaderProtocol = "px4",
            FlashSize = 2048,
            SupportsBootloaderUpdate = true,
            BoardId = 50
        },
        new BoardInfo
        {
            Id = "CubeOrange",
            Name = "CubeOrange",
            Manufacturer = "Hex/ProfiCNC",
            Description = "Cube Orange H7",
            BootloaderProtocol = "px4",
            FlashSize = 2048,
            SupportsBootloaderUpdate = true,
            BoardId = 140,
            VendorId = 0x2DAE,
            ProductId = 0x1058
        },
        new BoardInfo
        {
            Id = "CubeOrangePlus",
            Name = "CubeOrange+",
            Manufacturer = "Hex/ProfiCNC",
            Description = "Cube Orange+ H7 (enhanced sensors)",
            BootloaderProtocol = "px4",
            FlashSize = 2048,
            SupportsBootloaderUpdate = true,
            BoardId = 1063,
            VendorId = 0x2DAE,
            ProductId = 0x1059
        },
        new BoardInfo
        {
            Id = "CubeYellow",
            Name = "CubeYellow",
            Manufacturer = "Hex/ProfiCNC",
            Description = "Cube Yellow F7",
            BootloaderProtocol = "px4",
            FlashSize = 2048,
            SupportsBootloaderUpdate = true,
            BoardId = 120,
            VendorId = 0x2DAE,
            ProductId = 0x1016
        },
        new BoardInfo
        {
            Id = "Pixhawk6X",
            Name = "Pixhawk6X",
            Manufacturer = "Holybro",
            Description = "Pixhawk 6X",
            BootloaderProtocol = "px4",
            FlashSize = 2048,
            SupportsBootloaderUpdate = true,
            BoardId = 53
        },
        new BoardInfo
        {
            Id = "Pixhawk6C",
            Name = "Pixhawk6C",
            Manufacturer = "Holybro",
            Description = "Pixhawk 6C",
            BootloaderProtocol = "px4",
            FlashSize = 2048,
            SupportsBootloaderUpdate = true,
            BoardId = 55
        },
        new BoardInfo
        {
            Id = "MatekH743",
            Name = "MatekH743",
            Manufacturer = "Matek",
            Description = "Matek H743",
            BootloaderProtocol = "px4",
            FlashSize = 2048,
            SupportsBootloaderUpdate = true,
            BoardId = 1013
        },
        new BoardInfo
        {
            Id = "KakuteH7",
            Name = "KakuteH7",
            Manufacturer = "Holybro",
            Description = "Holybro Kakute H7",
            BootloaderProtocol = "px4",
            FlashSize = 2048,
            SupportsBootloaderUpdate = true,
            BoardId = 1044
        },
        new BoardInfo
        {
            Id = "SpeedyBeeF4",
            Name = "SpeedyBeeF4",
            Manufacturer = "SpeedyBee",
            Description = "SpeedyBee F4",
            BootloaderProtocol = "px4",
            FlashSize = 1024,
            SupportsBootloaderUpdate = true,
            BoardId = 1015
        }
    };
    
    /// <summary>
    /// All vehicle types matching Mission Planner's firmware.cs display
    /// Note: All copter frame types (Quad, Hexa, Octa, etc.) use the same "Copter" firmware.
    /// Frame type is configured via FRAME_CLASS parameter after flashing.
    /// </summary>
    public static readonly FirmwareType[] AvailableVehicleTypes = new[]
    {
        // Row 1
        new FirmwareType
        {
            Id = "rover",
            Name = "Rover",
            Description = "Ground vehicle",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Rover.png",
            VehicleClass = VehicleClass.Rover,
            ArduPilotId = "Rover",
            MavType = 10,
            DisplayOrder = 1
        },
        new FirmwareType
        {
            Id = "plane",
            Name = "Plane",
            Description = "Fixed-wing aircraft",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/QuadPlane.png",
            VehicleClass = VehicleClass.Plane,
            ArduPilotId = "Plane",
            MavType = 1,
            DisplayOrder = 2
        },
        new FirmwareType
        {
            Id = "copter",
            Name = "Copter",
            Description = "Quadcopter (X frame)",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png",
            VehicleClass = VehicleClass.Copter,
            ArduPilotId = "Copter",
            MavType = 2,
            DisplayOrder = 3
        },
        new FirmwareType
        {
            Id = "hexa",
            Name = "Copter",
            Description = "Hexacopter",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png",
            VehicleClass = VehicleClass.Copter,
            ArduPilotId = "Copter",
            MavType = 13,
            DisplayOrder = 4
        },
        new FirmwareType
        {
            Id = "octa",
            Name = "Copter",
            Description = "Octocopter",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png", // Fallback to Hexa image
            VehicleClass = VehicleClass.Copter,
            ArduPilotId = "Copter",
            MavType = 14,
            DisplayOrder = 5
        },
        new FirmwareType
        {
            Id = "y6",
            Name = "Copter",
            Description = "Y6 configuration",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png", // Fallback to Hexa image
            VehicleClass = VehicleClass.Copter,
            ArduPilotId = "Copter",
            MavType = 2,
            DisplayOrder = 6
        },
        new FirmwareType
        {
            Id = "sub",
            Name = "Sub",
            Description = "Underwater vehicle (ROV)",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png", // Fallback to Quad image
            VehicleClass = VehicleClass.Sub,
            ArduPilotId = "Sub",
            MavType = 12,
            DisplayOrder = 7
        },
        // Row 2
        new FirmwareType
        {
            Id = "antennatracker",
            Name = "AntennaTracker",
            Description = "Antenna Tracker",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Rover.png", // Fallback to Rover image
            VehicleClass = VehicleClass.AntennaTracker,
            ArduPilotId = "AntennaTracker",
            MavType = 5,
            DisplayOrder = 8
        },
        new FirmwareType
        {
            Id = "heli",
            Name = "Heli",
            Description = "Traditional Helicopter",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Heli.png",
            VehicleClass = VehicleClass.Copter,
            ArduPilotId = "Copter-heli",
            MavType = 4,
            DisplayOrder = 9
        },
        new FirmwareType
        {
            Id = "tri",
            Name = "Copter",
            Description = "Tricopter",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png", // Fallback to Quad image
            VehicleClass = VehicleClass.Copter,
            ArduPilotId = "Copter",
            MavType = 15,
            DisplayOrder = 10
        },
        new FirmwareType
        {
            Id = "octaquad",
            Name = "Copter",
            Description = "OctaQuad (X8)",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png", // Fallback to Hexa image
            VehicleClass = VehicleClass.Copter,
            ArduPilotId = "Copter",
            MavType = 14,
            DisplayOrder = 11
        },
        new FirmwareType
        {
            Id = "single",
            Name = "Copter",
            Description = "SingleCopter",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png", // Fallback to Quad image
            VehicleClass = VehicleClass.Copter,
            ArduPilotId = "Copter",
            MavType = 2,
            DisplayOrder = 12
        },
        new FirmwareType
        {
            Id = "coax",
            Name = "Copter",
            Description = "CoaxCopter",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Quad.png", // Fallback to Quad image
            VehicleClass = VehicleClass.Copter,
            ArduPilotId = "Copter",
            MavType = 2,
            DisplayOrder = 13
        },
        new FirmwareType
        {
            Id = "deca",
            Name = "Copter",
            Description = "DecaCopter (10 motors)",
            ImagePath = "avares://PavamanDroneConfigurator.UI/Assets/Images/Vehicle/Hexa.png", // Fallback to Hexa image
            VehicleClass = VehicleClass.Copter,
            ArduPilotId = "Copter",
            MavType = 2,
            DisplayOrder = 14
        }
    };
}
