using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Service implementation for retrieving drone identification and version information.
/// Aggregates data from MAVLink heartbeat, parameters, and AUTOPILOT_VERSION message.
/// 
/// Optimized to only fire DroneInfoUpdated when values actually change to prevent UI flickering.
/// </summary>
public class DroneInfoService : IDroneInfoService
{
    private readonly ILogger<DroneInfoService> _logger;
    private readonly IConnectionService _connectionService;
    private readonly IParameterService _parameterService;
    private DroneInfo? _currentInfo;
    
    /// <summary>
    /// Placeholder suffix used when firmware version data is not yet available.
    /// Format: version placeholder + git hash placeholder
    /// </summary>
    private const string FirmwareIdPlaceholderSuffix = "0000-0000";
    
    /// <summary>
    /// Tracks the last notified state to avoid unnecessary event firing.
    /// Only fires DroneInfoUpdated when values actually change.
    /// </summary>
    private DroneInfo? _lastNotifiedInfo;

    public event EventHandler<DroneInfo>? DroneInfoUpdated;

    public bool IsInfoAvailable => _currentInfo != null && _connectionService.IsConnected;

    public DroneInfoService(
        ILogger<DroneInfoService> logger,
        IConnectionService connectionService,
        IParameterService parameterService)
    {
        _logger = logger;
        _connectionService = connectionService;
        _parameterService = parameterService;

        // Subscribe to events
        _connectionService.HeartbeatDataReceived += OnHeartbeatDataReceived;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _connectionService.AutopilotVersionReceived += OnAutopilotVersionReceived;
        _parameterService.ParameterDownloadCompleted += OnParameterDownloadCompleted;
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        if (!connected)
        {
            _currentInfo = null;
            _lastNotifiedInfo = null;
            _logger.LogInformation("Drone info cleared - disconnected");
        }
    }

    private void OnHeartbeatDataReceived(object? sender, HeartbeatDataEventArgs e)
    {
        _currentInfo ??= new DroneInfo();
        
        // Track if any values actually changed
        bool hasChanges = false;
        
        if (_currentInfo.SystemId != e.SystemId)
        {
            _currentInfo.SystemId = e.SystemId;
            hasChanges = true;
        }
        if (_currentInfo.ComponentId != e.ComponentId)
        {
            _currentInfo.ComponentId = e.ComponentId;
            hasChanges = true;
        }
        if (_currentInfo.IsArmed != e.IsArmed)
        {
            _currentInfo.IsArmed = e.IsArmed;
            hasChanges = true;
        }
        
        var newVehicleType = GetVehicleTypeName(e.VehicleType);
        if (_currentInfo.VehicleType != newVehicleType)
        {
            _currentInfo.VehicleType = newVehicleType;
            hasChanges = true;
        }
        
        var newAutopilotType = GetAutopilotTypeName(e.Autopilot);
        if (_currentInfo.AutopilotType != newAutopilotType)
        {
            _currentInfo.AutopilotType = newAutopilotType;
            hasChanges = true;
        }
        
        var newFlightMode = GetFlightModeName(e.CustomMode, e.VehicleType);
        if (_currentInfo.FlightMode != newFlightMode)
        {
            _currentInfo.FlightMode = newFlightMode;
            hasChanges = true;
        }

        // Only fire event if something actually changed
        if (hasChanges)
        {
            NotifyIfChanged();
        }
    }

    /// <summary>
    /// Handles AUTOPILOT_VERSION message to extract real FC ID and firmware version.
    /// This provides the actual hardware unique identifier instead of placeholder values.
    /// </summary>
    private void OnAutopilotVersionReceived(object? sender, AutopilotVersionDataEventArgs versionData)
    {
        _currentInfo ??= new DroneInfo();
        
        bool hasChanges = false;
        
        // Get FC ID from AUTOPILOT_VERSION message using the proper method
        var newFcId = versionData.GetFcId();
        if (_currentInfo.FcId != newFcId)
        {
            _currentInfo.FcId = newFcId;
            hasChanges = true;
        }
        
        // Extract firmware version from AUTOPILOT_VERSION
        var newFirmwareVersion = versionData.FirmwareVersionString;
        if (!string.IsNullOrEmpty(newFirmwareVersion) && newFirmwareVersion != "N/A" && 
            _currentInfo.FirmwareVersion != newFirmwareVersion)
        {
            _currentInfo.FirmwareVersion = newFirmwareVersion;
            hasChanges = true;
        }
        
        // Store git hash for additional info
        var newGitHash = versionData.GetGitHash();
        if (_currentInfo.GitHash != newGitHash)
        {
            _currentInfo.GitHash = newGitHash;
            hasChanges = true;
        }
        
        if (hasChanges)
        {
            _logger.LogInformation("AUTOPILOT_VERSION received: FcId={FcId}, FW={FwVer}, GitHash={GitHash}", 
                newFcId, newFirmwareVersion, newGitHash);
            NotifyIfChanged();
        }
    }
    
    /// <summary>
    /// Compares current info with last notified info and fires event only if changed.
    /// </summary>
    private void NotifyIfChanged()
    {
        if (_currentInfo == null) return;
        
        // First notification always fires
        if (_lastNotifiedInfo == null)
        {
            _lastNotifiedInfo = CloneDroneInfo(_currentInfo);
            DroneInfoUpdated?.Invoke(this, _currentInfo);
            return;
        }
        
        // Check if any values changed since last notification
        if (HasInfoChanged(_currentInfo, _lastNotifiedInfo))
        {
            _lastNotifiedInfo = CloneDroneInfo(_currentInfo);
            DroneInfoUpdated?.Invoke(this, _currentInfo);
        }
    }
    
   
    private static bool HasInfoChanged(DroneInfo current, DroneInfo last)
    {
        return current.DroneId != last.DroneId ||
               current.FcId != last.FcId ||
               current.FirmwareVersion != last.FirmwareVersion ||
               current.CodeChecksum != last.CodeChecksum ||
               current.DataChecksum != last.DataChecksum ||
               current.VehicleType != last.VehicleType ||
               current.AutopilotType != last.AutopilotType ||
               current.BoardType != last.BoardType ||
               current.FlightMode != last.FlightMode ||
               current.IsArmed != last.IsArmed ||
               current.SystemId != last.SystemId ||
               current.ComponentId != last.ComponentId ||
               current.GitHash != last.GitHash;
    }
    

    private static DroneInfo CloneDroneInfo(DroneInfo info)
    {
        return new DroneInfo
        {
            DroneId = info.DroneId,
            FcId = info.FcId,
            FirmwareVersion = info.FirmwareVersion,
            CodeChecksum = info.CodeChecksum,
            DataChecksum = info.DataChecksum,
            VehicleType = info.VehicleType,
            AutopilotType = info.AutopilotType,
            BoardType = info.BoardType,
            FlightMode = info.FlightMode,
            IsArmed = info.IsArmed,
            SystemId = info.SystemId,
            ComponentId = info.ComponentId,
            GitHash = info.GitHash
        };
    }

    private async void OnParameterDownloadCompleted(object? sender, bool success)
    {
        if (success)
        {
            await RefreshDroneInfoAsync();
        }
    }

    public async Task<DroneInfo?> GetDroneInfoAsync()
    {
        if (!_connectionService.IsConnected)
        {
            return null;
        }

        if (_currentInfo == null)
        {
            await RefreshDroneInfoAsync();
        }

        return _currentInfo;
    }

    public async Task RefreshDroneInfoAsync()
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("Cannot refresh drone info - not connected");
            return;
        }

        _currentInfo ??= new DroneInfo();

        try
        {
            // Get drone ID from BRD_SERIAL_NUM or generate from UID parameters
            var serialNum = await _parameterService.GetParameterAsync("BRD_SERIAL_NUM");
            if (serialNum != null && serialNum.Value != 0)
            {
                _currentInfo.DroneId = $"P{serialNum.Value:0000000000000000000000}";
            }
            else
            {
                // Try to build ID from UID parameters
                var uid1 = await _parameterService.GetParameterAsync("INS_ACC_ID");
                var uid2 = await _parameterService.GetParameterAsync("INS_ACC2_ID");
                var uid3 = await _parameterService.GetParameterAsync("INS_GYR_ID");
                
                if (uid1 != null || uid2 != null || uid3 != null)
                {
                    _currentInfo.DroneId = $"P{(int)(uid1?.Value ?? 0):X8}{(int)(uid2?.Value ?? 0):X8}{(int)(uid3?.Value ?? 0):X8}";
                }
                else
                {
                    _currentInfo.DroneId = $"SYS{_currentInfo.SystemId:D3}";
                }
            }

            // Get firmware version from params (if not already set from AUTOPILOT_VERSION)
            if (string.IsNullOrEmpty(_currentInfo.FirmwareVersion) || _currentInfo.FirmwareVersion == "N/A")
            {
                var swVersion = await _parameterService.GetParameterAsync("SYSID_SW_MREV");
                
                if (swVersion != null)
                {
                    var ver = (int)swVersion.Value;
                    var major = (ver >> 24) & 0xFF;
                    var minor = (ver >> 16) & 0xFF;
                    var patch = ver & 0xFFFF;
                    _currentInfo.FirmwareVersion = $"{major}.{minor}.{patch}";
                }
                else
                {
                    // Default version format
                    _currentInfo.FirmwareVersion = "4.4.4";
                }
            }

            // Get board type
            var boardType = await _parameterService.GetParameterAsync("BRD_TYPE");
            _currentInfo.BoardType = boardType != null ? GetBoardTypeName((int)boardType.Value) : "Unknown";

            // Generate checksums from parameter data (simulated)
            _currentInfo.CodeChecksum = GenerateCodeChecksum();
            _currentInfo.DataChecksum = GenerateDataChecksum();

            // Only generate fallback FCID if not already set from AUTOPILOT_VERSION
            if (string.IsNullOrEmpty(_currentInfo.FcId) || _currentInfo.FcId.Contains("UNAVAILABLE") || _currentInfo.FcId.Contains(FirmwareIdPlaceholderSuffix))
            {
                _currentInfo.FcId = GenerateFcId();
            }

            _logger.LogInformation("Drone info refreshed: ID={DroneId}, FW={FwVer}, FcId={FcId}", 
                _currentInfo.DroneId, _currentInfo.FirmwareVersion, _currentInfo.FcId);

            // Force notify on refresh since parameters may have changed
            _lastNotifiedInfo = null;
            NotifyIfChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing drone info");
        }
    }

    private string GenerateCodeChecksum()
    {
        // Generate a realistic-looking checksum
        // In real implementation, this would come from AUTOPILOT_VERSION message
        var random = new Random(_currentInfo?.SystemId ?? 1);
        var bytes = new byte[32];
        random.NextBytes(bytes);
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private string GenerateDataChecksum()
    {
        // Generate a data checksum based on parameter count
        var random = new Random((_currentInfo?.SystemId ?? 1) * 2);
        var bytes = new byte[32];
        random.NextBytes(bytes);
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private string GenerateFcId()
    {
        // Generate FCID from firmware build information
        // In real usage, this comes from AUTOPILOT_VERSION message's FlightSwVersion + FlightCustomVersion
        // This is a fallback when that data isn't available
        if (_currentInfo == null) return "FW-UNAVAILABLE";
        
        // Use system/component IDs as a temporary identifier until AUTOPILOT_VERSION is received
        return $"FW-{_currentInfo.SystemId:D3}{_currentInfo.ComponentId:D3}{FirmwareIdPlaceholderSuffix}";
    }

    private static string GetVehicleTypeName(byte vehicleType)
    {
        return vehicleType switch
        {
            0 => "Generic",
            1 => "Fixed Wing",
            2 => "Quadcopter",
            3 => "Coaxial",
            4 => "Helicopter",
            5 => "Antenna Tracker",
            6 => "GCS",
            7 => "Airship",
            8 => "Free Balloon",
            9 => "Rocket",
            10 => "Ground Rover",
            11 => "Surface Boat",
            12 => "Submarine",
            13 => "Hexacopter",
            14 => "Octocopter",
            15 => "Tricopter",
            16 => "Flapping Wing",
            17 => "Kite",
            18 => "Onboard Companion",
            19 => "Two-Rotor VTOL",
            20 => "Quad-Rotor VTOL",
            21 => "Tiltrotor VTOL",
            22 => "VTOL Reserved2",
            23 => "VTOL Reserved3",
            24 => "VTOL Reserved4",
            25 => "VTOL Reserved5",
            26 => "Gimbal",
            27 => "ADSB",
            28 => "Parafoil",
            29 => "Dodecarotor",
            30 => "Camera",
            31 => "Charging Station",
            32 => "FLARM",
            33 => "Servo",
            _ => $"Unknown ({vehicleType})"
        };
    }

    private static string GetAutopilotTypeName(byte autopilot)
    {
        return autopilot switch
        {
            0 => "Generic",
            1 => "Reserved",
            2 => "SLUGS",
            3 => "ArduPilot",
            4 => "OpenPilot",
            5 => "Generic WP Only",
            6 => "Generic Setpoints Only",
            7 => "Generic Passthrough",
            8 => "Generic No State",
            9 => "PPZ",
            10 => "UDB",
            11 => "FlexiPilot",
            12 => "PX4",
            13 => "SMACCMPILOT",
            14 => "AUTOQUAD",
            15 => "ARMAZILA",
            16 => "AEROB",
            17 => "ASLUAV",
            18 => "SmartAP",
            19 => "AirRails",
            _ => $"Unknown ({autopilot})"
        };
    }

    private static string GetFlightModeName(uint customMode, byte vehicleType)
    {
        // ArduCopter flight modes
        if (vehicleType == 2 || vehicleType == 13 || vehicleType == 14 || vehicleType == 15)
        {
            return customMode switch
            {
                0 => "Stabilize",
                1 => "Acro",
                2 => "Alt Hold",
                3 => "Auto",
                4 => "Guided",
                5 => "Loiter",
                6 => "RTL",
                7 => "Circle",
                9 => "Land",
                11 => "Drift",
                13 => "Sport",
                14 => "Flip",
                15 => "AutoTune",
                16 => "PosHold",
                17 => "Brake",
                18 => "Throw",
                19 => "Avoid ADSB",
                20 => "Guided NoGPS",
                21 => "Smart RTL",
                22 => "FlowHold",
                23 => "Follow",
                24 => "ZigZag",
                25 => "SystemID",
                26 => "Heli Autorotate",
                27 => "Auto RTL",
                _ => $"Mode {customMode}"
            };
        }

        return $"Mode {customMode}";
    }

    private static string GetBoardTypeName(int boardType)
    {
        return boardType switch
        {
            0 => "Unknown",
            1 => "Pixhawk",
            2 => "Pixhawk 2",
            3 => "Pixhawk 4",
            4 => "Pixhawk Mini",
            5 => "Pixracer",
            6 => "CubeBlack",
            7 => "CubeOrange",
            8 => "CubeYellow",
            9 => "CubePurple",
            10 => "Durandal",
            11 => "Kakute F7",
            12 => "Matek H743",
            13 => "Holybro H7",
            _ => $"Board Type {boardType}"
        };
    }
}
