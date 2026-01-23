using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Management;
using System.Text.RegularExpressions;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Board detection service for ArduPilot flight controllers.
/// Matches Mission Planner's BoardDetect.cs logic for USB VID/PID detection.
/// </summary>
public static class BoardDetect
{
    /// <summary>
    /// Known board types
    /// </summary>
    public enum Boards
    {
        none = 0,
        b1280,           // APM1 (old)
        b2560,           // APM1
        b2560v2,         // APM2+
        px4,             // PX4
        px4v2,           // Pixhawk 1
        px4v3,           // Pixhawk 2 / Cube with 2MB flash
        px4v4,           // Pixracer
        px4v4pro,        // Pixhawk 3 Pro
        vrbrainv40,
        vrbrainv45,
        vrbrainv50,
        vrbrainv51,
        vrbrainv52,
        vrbrainv54,
        vrcorev10,
        vrubrainv51,
        vrubrainv52,
        bebop2,
        disco,
        solo,
        revomini,
        mindpxv2,
        fmuv5,           // Pixhawk 4 / PixHack V5
        fmuv5x,          // Pixhawk 5X
        fmuv6,           // Pixhawk 6
        fmuv6x,          // Pixhawk 6X
        fmuv6c,          // Pixhawk 6C
        cubeorange,      // Cube Orange
        cubeorangeplus,  // Cube Orange+
        cubeyellow,      // Cube Yellow
        cubepurple,      // Cube Purple
        durandal,        // Durandal
        matekf405,
        matekf765,
        matekh743,       // Matek H743
        matekh743_mini,  // Matek H743 Mini
        kakutef4,
        kakutef7,
        kakuteh7,
        kakuteh7_mini,
        speedybeef4,
        speedybeef4v3,
        omnibusf4,
        f4by,
        skyviper,
        navigator,
        bluerobotics,
        cheetahf7,
        f35lightning,
        flywoof405,
        flywoof7,
        flywooF745,
        radiomasterbandit,
        airbotf4,
        airbotf7,
        jhef405,
        jhef7,
        holybro_pix32v5,
        sp_racing_h7,
        gps_rtk_base,
        gps_rtk_rover
    }

    /// <summary>
    /// USB VID/PID combinations for board detection
    /// </summary>
    public static class UsbIds
    {
        // PX4/Pixhawk bootloader VID/PID
        public const int PX4_BOOTLOADER_VID = 0x26AC;
        public const int PX4_BOOTLOADER_PID = 0x0010;

        // STM32 bootloader (DFU mode)
        public const int STM32_DFU_VID = 0x0483;
        public const int STM32_DFU_PID = 0xDF11;

        // STMicro Virtual COM Port
        public const int STM32_VCP_VID = 0x0483;
        public const int STM32_VCP_PID = 0x5740;

        // FTDI
        public const int FTDI_VID = 0x0403;

        // ProfiCNC Cube
        public const int PROFICNC_VID = 0x2DAE;
        
        // ProfiCNC Cube product IDs
        public const int PROFICNC_CUBE_ORANGE_PID = 0x1058;     // CubeOrange
        public const int PROFICNC_CUBE_ORANGEPLUS_PID = 0x1059; // CubeOrange+
        public const int PROFICNC_CUBE_YELLOW_PID = 0x1016;     // CubeYellow
        public const int PROFICNC_CUBE_PURPLE_PID = 0x1017;     // CubePurple

        // ArduPilot bootloader (new style)
        public const int ARDUPILOT_VID = 0x1209;
        public const int ARDUPILOT_BOOTLOADER_PID = 0x5740;
        public const int ARDUPILOT_MAVLINK_PID = 0x5741;

        // Silicon Labs
        public const int SILABS_VID = 0x10C4;

        // RadioMaster
        public const int RADIOMASTER_VID = 0x0483;

        // Holybro
        public const int HOLYBRO_VID = 0x0483;
    
        // Holybro specific PIDs
        public const int HOLYBRO_PIX32V5_PID = 0x0033;
        public const int HOLYBRO_DURANDAL_PID = 0x0037;
        public const int HOLYBRO_PIXHAWK6X_PID = 0x0054;
        public const int HOLYBRO_PIXHAWK6C_PID = 0x0055;
    }

    /// <summary>
    /// Device info from USB enumeration
    /// </summary>
    public class DeviceInfo
    {
        public string PortName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string HardwareId { get; set; } = string.Empty;
        public int VendorId { get; set; }
        public int ProductId { get; set; }
        public string Manufacturer { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public string Board { get; set; } = string.Empty;
        public bool IsBootloader { get; set; }
    }

    /// <summary>
    /// Detects board type from the specified port
    /// </summary>
    public static Boards DetectBoard(string port, IList<DeviceInfo>? deviceList = null)
    {
        // Get device list if not provided
        deviceList ??= GetConnectedDevices();

        // Find the device for this port
        var device = deviceList.FirstOrDefault(d => 
            d.PortName.Equals(port, StringComparison.OrdinalIgnoreCase));

        if (device == null)
        {
            return Boards.none;
        }

        return DetectBoardFromDevice(device);
    }

    /// <summary>
    /// Detects board type from device info
    /// </summary>
    public static Boards DetectBoardFromDevice(DeviceInfo device)
    {
        var hwid = device.HardwareId.ToUpperInvariant();
        var board = device.Board.ToLowerInvariant();

        // Check for new-style ArduPilot bootloader
        if (hwid.Contains($"VID_{UsbIds.ARDUPILOT_VID:X4}") ||
            hwid.Contains($"VID_1209"))
        {
            device.IsBootloader = hwid.Contains($"PID_{UsbIds.ARDUPILOT_BOOTLOADER_PID:X4}") ||
                                  hwid.Contains("PID_5740");
            return DetectBoardFromBootloaderString(board, device);
        }

        // Check for STM32 VCP (most common for ArduPilot)
        if (hwid.Contains($"VID_{UsbIds.STM32_VCP_VID:X4}") ||
            hwid.Contains("VID_0483"))
        {
            if (hwid.Contains($"PID_{UsbIds.STM32_VCP_PID:X4}") ||
                hwid.Contains("PID_5740"))
            {
                device.IsBootloader = true;
                return DetectBoardFromBootloaderString(board, device);
            }
        }

        // Check for ProfiCNC (Cube)
        if (hwid.Contains($"VID_{UsbIds.PROFICNC_VID:X4}") ||
            Regex.IsMatch(hwid, "VID_2DAE"))
        {
            device.IsBootloader = hwid.Contains("-BL") || board.Contains("-bl");
            return DetectCubeBoard(board, device);
        }

        // Check for PX4 bootloader
        if (hwid.Contains($"VID_{UsbIds.PX4_BOOTLOADER_VID:X4}") ||
            hwid.Contains("VID_26AC"))
        {
            device.IsBootloader = true;
            return DetectPx4Board(board, device);
        }

        // Check for STM32 DFU
        if (hwid.Contains($"VID_{UsbIds.STM32_DFU_VID:X4}") &&
            hwid.Contains($"PID_{UsbIds.STM32_DFU_PID:X4}"))
        {
            device.IsBootloader = true;
            return Boards.px4v2; // Generic STM32 DFU
        }

        return Boards.none;
    }

    /// <summary>
    /// Detects board from bootloader string
    /// </summary>
    private static Boards DetectBoardFromBootloaderString(string board, DeviceInfo device)
    {
        board = board.ToLowerInvariant();

        // FMU versions
        if (board.Contains("fmuv6x") || board.Contains("fmu-v6x"))
            return Boards.fmuv6x;
        if (board.Contains("fmuv6c") || board.Contains("fmu-v6c"))
            return Boards.fmuv6c;
        if (board.Contains("fmuv6") || board.Contains("fmu-v6"))
            return Boards.fmuv6;
        if (board.Contains("fmuv5x") || board.Contains("fmu-v5x"))
            return Boards.fmuv5x;
        if (board.Contains("fmuv5") || board.Contains("fmu-v5"))
            return Boards.fmuv5;
        if (board.Contains("fmuv4pro"))
            return Boards.px4v4pro;
        if (board.Contains("fmuv4") || board.Contains("fmu-v4"))
            return Boards.px4v4;
        if (board.Contains("fmuv3") || board.Contains("fmu-v3"))
            return Boards.px4v3;
        if (board.Contains("fmuv2") || board.Contains("fmu-v2"))
            return Boards.px4v2;

        // Cube variants - check CubeOrange+ before CubeOrange
        if (board.Contains("cubeorange+") || board.Contains("cubeorangeplus"))
            return Boards.cubeorangeplus;
        if (board.Contains("cubeorange"))
            return Boards.cubeorange;
        if (board.Contains("cubeyellow"))
            return Boards.cubeyellow;
        if (board.Contains("cubepurple"))
            return Boards.cubepurple;
        if (board.Contains("cubeblack") || board.Contains("cube"))
            return Boards.px4v3;

        // Matek - check H743 Mini before H743
        if (board.Contains("matekh743-mini") || board.Contains("matekh743mini"))
            return Boards.matekh743_mini;
        if (board.Contains("matekh743") || board.Contains("matek-h743"))
            return Boards.matekh743;
        if (board.Contains("matekf765"))
            return Boards.matekf765;
        if (board.Contains("matekf405"))
            return Boards.matekf405;

        // Kakute - check Mini variants first
        if (board.Contains("kakuteh7mini") || board.Contains("kakuteh7-mini"))
            return Boards.kakuteh7_mini;
        if (board.Contains("kakuteh7"))
            return Boards.kakuteh7;
        if (board.Contains("kakutef7"))
            return Boards.kakutef7;
        if (board.Contains("kakutef4"))
            return Boards.kakutef4;

        // Holybro
        if (board.Contains("durandal"))
            return Boards.durandal;
        if (board.Contains("pix32v5"))
            return Boards.holybro_pix32v5;

        // Other boards
        if (board.Contains("speedybeef4v3"))
            return Boards.speedybeef4v3;
        if (board.Contains("speedybeef4"))
            return Boards.speedybeef4;
        if (board.Contains("omnibusf4"))
            return Boards.omnibusf4;
        if (board.Contains("f4by"))
            return Boards.f4by;
        if (board.Contains("skyviper"))
            return Boards.skyviper;
        if (board.Contains("navigator") || board.Contains("bluerobotics"))
            return Boards.navigator;

        // Default to px4v2 for STM32 devices
        if (device.VendorId == UsbIds.STM32_VCP_VID)
            return Boards.px4v2;

        return Boards.none;
    }

    /// <summary>
    /// Detects Cube board variant.
    /// 
    /// IMPORTANT: CubeOrange (board_id=140, STM32H753) and CubeOrangePlus (board_id=1063, STM32H757) 
    /// are NOT compatible - they have different MCUs!
    /// 
    /// Note: USB PIDs can be unreliable for differentiating CubeOrange vs CubeOrangePlus as they
    /// may share VID_2DAE&PID_1058. The authoritative source is the bootloader-reported board_type
    /// which should be queried via Px4Uploader.Identify() after connecting.
    /// 
    /// This method provides initial detection from USB info; the bootloader board_type should be
    /// used for final firmware selection validation.
    /// </summary>
    private static Boards DetectCubeBoard(string board, DeviceInfo device)
    {
        board = board.ToLowerInvariant();
        
        // Log USB detection info for debugging
        System.Diagnostics.Debug.WriteLine(
            $"[BoardDetect] USB Detection: VID={device.VendorId:X4}, PID={device.ProductId:X4}, Board string='{device.Board}'");

        // IMPORTANT: Check string matching FIRST for CubeOrange+ vs CubeOrange
        // USB PIDs can be unreliable - CubeOrange and CubeOrangePlus may both use PID 0x1058
        // The board string from USB descriptor is more reliable for differentiation
        
        // Check for CubeOrange+ explicitly first (must check before checking for "orange")
        if (board.Contains("orange+") || board.Contains("orangeplus") || board.Contains("cubeplus"))
        {
            System.Diagnostics.Debug.WriteLine("[BoardDetect] Detected CubeOrangePlus from board string");
            return Boards.cubeorangeplus;
        }
        
        // Now check by product ID (but be aware CubeOrange/CubeOrangePlus may share PIDs)
        if (device.ProductId == UsbIds.PROFICNC_CUBE_ORANGEPLUS_PID)  // 0x1059
        {
            System.Diagnostics.Debug.WriteLine("[BoardDetect] Detected CubeOrangePlus from PID 0x1059");
            return Boards.cubeorangeplus;
        }
        if (device.ProductId == UsbIds.PROFICNC_CUBE_YELLOW_PID)
            return Boards.cubeyellow;
        if (device.ProductId == UsbIds.PROFICNC_CUBE_PURPLE_PID)
            return Boards.cubepurple;
            
        // For PID 0x1058, default to CubeOrange but log warning
        // The bootloader board_type should be used for final confirmation
        if (device.ProductId == UsbIds.PROFICNC_CUBE_ORANGE_PID)      // 0x1058
        {
            // Could be either CubeOrange OR CubeOrangePlus - both may use this PID!
            // Default to cubeorange but the bootloader board_type will be authoritative
            System.Diagnostics.Debug.WriteLine(
                "[BoardDetect] PID 0x1058 detected - could be CubeOrange or CubeOrangePlus. " +
                "Bootloader board_type will be used for final identification.");
            return Boards.cubeorange;
        }

        // Fallback string matching for other variants
        if (board.Contains("orange"))
            return Boards.cubeorange;
        if (board.Contains("yellow"))
            return Boards.cubeyellow;
        if (board.Contains("purple"))
            return Boards.cubepurple;
        if (board.Contains("black") || board.Contains("2.1"))
            return Boards.px4v3;

        return Boards.px4v3; // Default Cube
    }

    /// <summary>
    /// Detects PX4 board variant
    /// </summary>
    private static Boards DetectPx4Board(string board, DeviceInfo device)
    {
        board = board.ToLowerInvariant();

        if (board.Contains("v5"))
            return Boards.fmuv5;
        if (board.Contains("v4"))
            return Boards.px4v4;
        if (board.Contains("v3"))
            return Boards.px4v3;

        return Boards.px4v2; // Default PX4
    }

    /// <summary>
    /// Gets list of connected USB devices that could be flight controllers
    /// </summary>
    public static List<DeviceInfo> GetConnectedDevices()
    {
        var devices = new List<DeviceInfo>();

        try
        {
            // Query Win32_PnPEntity for USB serial devices
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE Caption LIKE '%(COM%' OR Caption LIKE '%Arduino%' OR Caption LIKE '%STM%' OR Caption LIKE '%Pixhawk%'");

            foreach (var obj in searcher.Get())
            {
                try
                {
                    var device = new DeviceInfo();
                    
                    // Get port name from caption
                    var caption = obj["Caption"]?.ToString() ?? string.Empty;
                    var match = Regex.Match(caption, @"\((COM\d+)\)");
                    if (match.Success)
                    {
                        device.PortName = match.Groups[1].Value;
                    }
                    else
                    {
                        continue; // Skip if no COM port
                    }

                    device.Description = caption;
                    device.HardwareId = obj["DeviceID"]?.ToString() ?? obj["PNPDeviceID"]?.ToString() ?? string.Empty;
                    
                    // Parse VID/PID from hardware ID
                    var vidMatch = Regex.Match(device.HardwareId, @"VID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
                    if (vidMatch.Success)
                    {
                        device.VendorId = Convert.ToInt32(vidMatch.Groups[1].Value, 16);
                    }

                    var pidMatch = Regex.Match(device.HardwareId, @"PID_([0-9A-Fa-f]{4})", RegexOptions.IgnoreCase);
                    if (pidMatch.Success)
                    {
                        device.ProductId = Convert.ToInt32(pidMatch.Groups[1].Value, 16);
                    }

                    device.Manufacturer = obj["Manufacturer"]?.ToString() ?? string.Empty;

                    // Try to extract board name from description
                    device.Board = ExtractBoardName(caption, device.HardwareId);

                    // Determine if this is bootloader mode
                    device.IsBootloader = caption.Contains("bootloader", StringComparison.OrdinalIgnoreCase) ||
                                         caption.Contains("-BL", StringComparison.OrdinalIgnoreCase) ||
                                         device.HardwareId.Contains("-BL", StringComparison.OrdinalIgnoreCase);

                    devices.Add(device);
                }
                catch
                {
                    // Skip devices that fail to parse
                }
            }
        }
        catch (Exception)
        {
            // WMI not available, try fallback
            foreach (var portName in SerialPort.GetPortNames())
            {
                devices.Add(new DeviceInfo
                {
                    PortName = portName,
                    Description = portName
                });
            }
        }

        return devices;
    }

    /// <summary>
    /// Extracts board name from device caption/description
    /// </summary>
    private static string ExtractBoardName(string caption, string hardwareId)
    {
        caption = caption.ToLowerInvariant();
        hardwareId = hardwareId.ToLowerInvariant();

        // Known board name patterns
        var patterns = new[]
        {
            @"(fmu[-_]?v\d+[a-z]*)",
            @"(cubeorange\+?)",
            @"(cubeyellow)",
            @"(cubeblack)",
            @"(pixhawk\d*)",
            @"(pixracer)",
            @"(matek[-_]?[a-z0-9]+)",
            @"(kakute[-_]?[a-z0-9]+)",
            @"(durandal)",
            @"(speedybee[a-z0-9]*)",
            @"(omnibus[a-z0-9]*)",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(caption, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            match = Regex.Match(hardwareId, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// Gets the bootloader port name for a device
    /// When a device reboots into bootloader mode, it may appear on a different port
    /// </summary>
    public static string? WaitForBootloaderPort(string originalPort, int timeoutMs = 30000)
    {
        var startDevices = GetConnectedDevices();
        var startPorts = startDevices.Select(d => d.PortName).ToHashSet();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            System.Threading.Thread.Sleep(500);
            
            var currentDevices = GetConnectedDevices();
            
            // Look for new port that appeared (bootloader mode)
            foreach (var device in currentDevices)
            {
                // Check if this is a new bootloader port
                if (device.IsBootloader || 
                    device.Description.Contains("bootloader", StringComparison.OrdinalIgnoreCase) ||
                    device.VendorId == UsbIds.STM32_VCP_VID ||
                    device.VendorId == UsbIds.ARDUPILOT_VID ||
                    device.VendorId == UsbIds.PROFICNC_VID)
                {
                    // If original port reappeared or new bootloader port found
                    if (device.PortName.Equals(originalPort, StringComparison.OrdinalIgnoreCase) ||
                        !startPorts.Contains(device.PortName))
                    {
                        return device.PortName;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the board ID for a given board type
    /// These MUST match the actual board_id values reported by the PX4 bootloader.
    /// Reference: ArduPilot hwdef files and bootloader source
    /// </summary>
    public static int GetBoardId(Boards board)
    {
        return board switch
        {
            Boards.px4 => 5,
            Boards.px4v2 => 9,
            Boards.px4v3 => 9,      // Same as px4v2, uses fmuv3 firmware
            Boards.px4v4 => 11,
            Boards.px4v4pro => 13,
            Boards.fmuv5 => 50,
            Boards.fmuv5x => 51,
            Boards.fmuv6 => 52,
            Boards.fmuv6x => 53,
            Boards.fmuv6c => 55,
            Boards.cubeorange => 140,       // CubeOrange uses board_id 140
            Boards.cubeorangeplus => 1063,  // FIXED: CubeOrange+ uses board_id 1063, NOT 141!
            Boards.cubeyellow => 120,
            Boards.cubepurple => 142,
            Boards.durandal => 36,
            Boards.matekh743 => 1013,
            Boards.matekh743_mini => 1066,
            Boards.matekf765 => 1008,
            Boards.matekf405 => 1002,
            Boards.kakuteh7 => 1044,
            Boards.kakuteh7_mini => 1062,
            Boards.kakutef7 => 1012,  // FIXED: Was 1025, which conflicts with nothing standard
            Boards.kakutef4 => 1010,
            Boards.holybro_pix32v5 => 78,
            Boards.speedybeef4 => 1015,
            Boards.speedybeef4v3 => 1077,
            Boards.omnibusf4 => 1022,
            _ => 0
        };
    }

    /// <summary>
    /// Gets the board name for a given board type
    /// </summary>
    public static string GetBoardName(Boards board)
    {
        return board switch
        {
            Boards.px4v2 => "Pixhawk 1 (FMUv2)",
            Boards.px4v3 => "Pixhawk 2 / Cube (FMUv3)",
            Boards.px4v4 => "Pixracer (FMUv4)",
            Boards.px4v4pro => "Pixhawk 3 Pro (FMUv4 Pro)",
            Boards.fmuv5 => "Pixhawk 4 (FMUv5)",
            Boards.fmuv5x => "Pixhawk 5X (FMUv5X)",
            Boards.fmuv6 => "Pixhawk 6 (FMUv6)",
            Boards.fmuv6x => "Pixhawk 6X (FMUv6X)",
            Boards.fmuv6c => "Pixhawk 6C (FMUv6C)",
            Boards.cubeorange => "CubeOrange",
            Boards.cubeorangeplus => "CubeOrange+",
            Boards.cubeyellow => "CubeYellow",
            Boards.cubepurple => "CubePurple",
            Boards.durandal => "Holybro Durandal",
            Boards.matekh743 => "Matek H743",
            Boards.matekh743_mini => "Matek H743 Mini",
            Boards.matekf765 => "Matek F765",
            Boards.matekf405 => "Matek F405",
            Boards.kakuteh7 => "Holybro Kakute H7",
            Boards.kakuteh7_mini => "Holybro Kakute H7 Mini",
            Boards.kakutef7 => "Holybro Kakute F7",
            Boards.kakutef4 => "Holybro Kakute F4",
            Boards.holybro_pix32v5 => "Holybro Pix32 v5",
            Boards.speedybeef4 => "SpeedyBee F4",
            Boards.speedybeef4v3 => "SpeedyBee F4 V3",
            Boards.omnibusf4 => "Omnibus F4",
            _ => board.ToString()
        };
    }

    /// <summary>
    /// Gets the platform/target name for firmware download
    /// </summary>
    public static string GetPlatformName(Boards board)
    {
        return board switch
        {
            Boards.px4v2 => "fmuv2",
            Boards.px4v3 => "fmuv3",
            Boards.px4v4 => "fmuv4",
            Boards.px4v4pro => "fmuv4-pro",
            Boards.fmuv5 => "fmuv5",
            Boards.fmuv5x => "Pixhawk5X",
            Boards.fmuv6 => "Pixhawk6",
            Boards.fmuv6x => "Pixhawk6X",
            Boards.fmuv6c => "Pixhawk6C",
            Boards.cubeorange => "CubeOrange",
            Boards.cubeorangeplus => "CubeOrangePlus",
            Boards.cubeyellow => "CubeYellow",
            Boards.cubepurple => "CubePurple",
            Boards.durandal => "Durandal",
            Boards.matekh743 => "MatekH743",
            Boards.matekh743_mini => "MatekH743-mini",
            Boards.matekf765 => "MatekF765-Wing",
            Boards.matekf405 => "MatekF405",
            Boards.kakuteh7 => "KakuteH7",
            Boards.kakuteh7_mini => "KakuteH7Mini",
            Boards.kakutef7 => "KakuteF7",
            Boards.kakutef4 => "KakuteF4",
            Boards.holybro_pix32v5 => "Pix32v5",
            Boards.speedybeef4 => "speedybeef4",
            Boards.speedybeef4v3 => "speedybeef4v3",
            Boards.omnibusf4 => "omnibusf4",
            _ => board.ToString()
        };
    }
}
