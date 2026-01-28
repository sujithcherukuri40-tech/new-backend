using System;
using System.Collections.Generic;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Board compatibility mapping for firmware flashing.
/// Matches Mission Planner's board ID compatibility logic.
/// </summary>
public static class BoardCompatibility
{
    /// <summary>
    /// Known board IDs from ArduPilot - matches Mission Planner's BoardDetect.boards enum
    /// These are the actual board_id values reported by the bootloader.
    /// Reference: https://github.com/ArduPilot/ardupilot/tree/master/libraries/AP_HAL_ChibiOS/hwdef
    /// </summary>
    public static class BoardIds
    {
        // Legacy Arduino
        public const int APM1_2560 = 0;
        public const int APM2 = 3;

        // PX4/Pixhawk family
        public const int PX4_FMUv1 = 5;
        public const int PX4_FMUv2 = 9;        // Pixhawk 1, Cube Black (bootloader reports 9)
        public const int PX4_FMUv3_LOGICAL = 9; // FMUv3 also reports 9, uses different firmware
        public const int PX4_FMUv4 = 11;       // Pixracer
        public const int PX4_FMUv4_PRO = 13;   // Pixhawk 3 Pro
        public const int AUAV_X2 = 33;         // Also compatible with fmuv2 (board_id 9)
        public const int PX4_FMUv5 = 50;       // Pixhawk 4
        public const int PX4_FMUv5X = 51;      // Pixhawk 5X
        public const int PX4_FMUv6 = 52;       // Pixhawk 6
        public const int PX4_FMUv6X = 53;      // Pixhawk 6X
        public const int PX4_FMUv6C = 55;      // Pixhawk 6C

        // Cube variants - CORRECT ArduPilot board IDs
        public const int CubeBlack = 9;        // Same as FMUv2 (bootloader reports 9)
        public const int CubeYellow = 120;
        public const int CubeOrange = 140;     // Original CubeOrange board_id in hwdef
        public const int CubeOrangePlus = 1063; // CubeOrange+ has board_id 1063
        public const int CubePurple = 142;

        // Holybro
        public const int Durandal = 36;
        public const int Pix32v5 = 78;

        // mRo
        public const int mRo_X21 = 71;
        public const int mRo_Control_Zero_OEM_H7 = 139;

        // Intel
        public const int Intel_Aero = 98;

        // Omnibus
        public const int OmnibusF4SD = 42;

        // Matek
        public const int MatekF405 = 1002;
        public const int MatekF405_Wing = 1003;
        public const int MatekF405_STD = 1004;
        public const int MatekF765 = 1008;
        public const int MatekH743 = 1013;
        public const int MatekH743_Mini = 1066;

        // Kakute
        public const int KakuteF4 = 1010;
        public const int KakuteF4_Mini = 1011;
        public const int KakuteF7 = 1012;      // FIXED: Was incorrectly 1025
        public const int KakuteF7_Mini = 1041;
        public const int KakuteH7 = 1044;
        public const int KakuteH7_Mini = 1062;

        // SpeedyBee
        public const int SpeedyBeeF4 = 1015;
        public const int SpeedyBeeF4V3 = 1077;
        public const int SpeedyBeeF405V3 = 1093;
        public const int SpeedyBeeF405V4 = 1110;

        // FlywooF4
        public const int FlywooF405S_AIO = 1080;
        public const int FlywooF745 = 1061;
        public const int FlywooF745_AIO = 1064;

        // JHEMCU
        public const int JHEMCU_GF16F405 = 1082;
        public const int JHEMCU_GF30H7_AIO = 1112;
        public const int JHEMCU_H743HD = 1113;

        // Other F4/F7/H7 boards
        public const int BeastH7 = 1040;
        public const int BeastF7 = 1021;
        public const int iFlight_BeastF7_V2 = 1089;

        // SKY boards
        public const int SKYH743 = 190;

        // More boards can be added as needed
    }

    /// <summary>
    /// Compatibility groups - boards that can use the same firmware
    /// NOTE: CubeOrange and CubeOrangePlus are NOT compatible - they have different MCUs!
    /// CubeOrange uses STM32H753 (single-core), CubeOrangePlus uses STM32H757 (dual-core)
    /// 
    /// Mission Planner board ID compatibility mappings from Uploader.cs:918-928
    /// </summary>
    private static readonly Dictionary<int, int[]> CompatibilityGroups = new()
    {
        // FMUv2/v3 compatibility group (Pixhawk 1, Cube Black, etc.)
        // Both report board_id=9 in bootloader
        // Special case from Mission Planner: board_type 33 is compatible with fw.board_id 9
        {
            BoardIds.PX4_FMUv2, new[]
            {
                BoardIds.PX4_FMUv2,
                BoardIds.CubeBlack,
                BoardIds.AUAV_X2 // AUAV-X2 (33) can use fmuv2 (9) firmware
            }
        },

        // AUAV-X2 can use fmuv2 firmware - explicit mapping
        {
            BoardIds.AUAV_X2, new[]
            {
                BoardIds.AUAV_X2,
                BoardIds.PX4_FMUv2
            }
        },
        
        // FMUv5 group - Pixhawk 4 and variants
        {
            BoardIds.PX4_FMUv5, new[]
            {
                BoardIds.PX4_FMUv5
            }
        },

        // Matek H743 family
        {
            BoardIds.MatekH743, new[]
            {
                BoardIds.MatekH743,
                BoardIds.MatekH743_Mini
            }
        },

        // Kakute H7 family
        {
            BoardIds.KakuteH7, new[]
            {
                BoardIds.KakuteH7,
                BoardIds.KakuteH7_Mini
            }
        },

        // Kakute F7 family
        {
            BoardIds.KakuteF7, new[]
            {
                BoardIds.KakuteF7,
                BoardIds.KakuteF7_Mini
            }
        },
        
        // CubeOrange - standalone, NOT compatible with CubeOrangePlus
        {
            BoardIds.CubeOrange, new[]
            {
                BoardIds.CubeOrange
            }
        },
        
        // CubeOrangePlus - standalone, NOT compatible with CubeOrange
        {
            BoardIds.CubeOrangePlus, new[]
            {
                BoardIds.CubeOrangePlus
            }
        },

        // SKYH743 - standalone board
        {
            BoardIds.SKYH743, new[]
            {
                BoardIds.SKYH743
            }
        }
    };

    /// <summary>
    /// Checks if two board IDs are compatible for firmware flashing.
    /// Matches Mission Planner's board compatibility logic from Uploader.cs:918-928.
    /// </summary>
    /// <param name="detectedBoardId">The board ID detected from bootloader</param>
    /// <param name="firmwareBoardId">The board ID specified in the firmware file</param>
    /// <returns>True if the firmware can be flashed to the detected board</returns>
    public static bool AreCompatible(int detectedBoardId, int firmwareBoardId)
    {
        // Exact match is always compatible
        if (detectedBoardId == firmwareBoardId)
        {
            return true;
        }

        // Check if firmware board ID is 0 (universal firmware)
        if (firmwareBoardId == 0)
        {
            return true;
        }

        // Check compatibility groups - forward lookup
        if (CompatibilityGroups.TryGetValue(detectedBoardId, out var compatibleBoards))
        {
            if (Array.IndexOf(compatibleBoards, firmwareBoardId) >= 0)
            {
                return true;
            }
        }
        
        // Check reverse mapping - firmware board might have a compatibility group
        if (CompatibilityGroups.TryGetValue(firmwareBoardId, out var reverseCompatible))
        {
            if (Array.IndexOf(reverseCompatible, detectedBoardId) >= 0)
            {
                return true;
            }
        }

        // Check if both boards are in the same compatibility group
        foreach (var group in CompatibilityGroups)
        {
            var groupBoards = group.Value;

            if (Array.IndexOf(groupBoards, detectedBoardId) >= 0 &&
                Array.IndexOf(groupBoards, firmwareBoardId) >= 0)
            {
                return true;
            }
        }

        // Special cases for common compatibility patterns
        // Mission Planner exception: board_type 33 (AUAV-X2) is compatible with fw.board_id 9 (fmuv2)
        if (detectedBoardId == BoardIds.AUAV_X2 && firmwareBoardId == BoardIds.PX4_FMUv2)
        {
            return true;
        }
        
        // Reverse: firmware expects board 33 but we have board 9
        if (detectedBoardId == BoardIds.PX4_FMUv2 && firmwareBoardId == BoardIds.AUAV_X2)
        {
            return true;
        }

        // Board ID 9 is compatible with fmuv2 and fmuv3 firmware
        if (detectedBoardId == 9 && firmwareBoardId == 9)
        {
            return true;
        }

        // NOTE: CubeOrange (140) and CubeOrangePlus (1063) are NOT compatible!
        // They have different MCUs and require different firmware.

        return false;
    }

    /// <summary>
    /// Gets the firmware platform name for a board ID.
    /// Used to select the correct firmware from the manifest.
    /// This MUST return the exact platform name as it appears in the ArduPilot firmware manifest.
    /// </summary>
    public static string GetPlatformName(int boardId)
    {
        return boardId switch
        {
            BoardIds.PX4_FMUv2 or BoardIds.CubeBlack => "fmuv2",
            BoardIds.PX4_FMUv4 => "fmuv4",
            BoardIds.PX4_FMUv4_PRO => "fmuv4-pro",
            BoardIds.PX4_FMUv5 => "fmuv5",
            BoardIds.PX4_FMUv5X => "Pixhawk5X",
            BoardIds.PX4_FMUv6 => "Pixhawk6",
            BoardIds.PX4_FMUv6X => "Pixhawk6X",
            BoardIds.PX4_FMUv6C => "Pixhawk6C",
            BoardIds.CubeYellow => "CubeYellow",
            BoardIds.CubeOrange => "CubeOrange",
            BoardIds.CubeOrangePlus => "CubeOrangePlus",  // CRITICAL: Must match exactly (board ID 1063)
            BoardIds.CubePurple => "CubePurple",
            BoardIds.Durandal => "Durandal",
            BoardIds.Pix32v5 => "Pix32v5",
            BoardIds.AUAV_X2 => "fmuv2",  // AUAV-X2 uses fmuv2 firmware
            BoardIds.MatekF405 => "MatekF405",
            BoardIds.MatekF765 => "MatekF765-Wing",
            BoardIds.MatekH743 => "MatekH743",
            BoardIds.MatekH743_Mini => "MatekH743-mini",
            BoardIds.KakuteF4 => "KakuteF4",
            BoardIds.KakuteF7 => "KakuteF7",
            BoardIds.KakuteH7 => "KakuteH7",
            BoardIds.KakuteH7_Mini => "KakuteH7Mini",
            BoardIds.SpeedyBeeF4 => "speedybeef4",
            BoardIds.FlywooF745 => "FlywooF745",
            BoardIds.SKYH743 => "SKYH743",
            _ => $"board_{boardId}"
        };
    }

    /// <summary>
    /// Gets the board ID for a given platform name.
    /// </summary>
    public static int GetBoardId(string platformName)
    {
        var lower = platformName.ToLowerInvariant();

        return lower switch
        {
            "fmuv2" or "pixhawk1" => BoardIds.PX4_FMUv2,
            "fmuv3" => BoardIds.PX4_FMUv2, // fmuv3 also uses board_id 9
            "fmuv4" or "pixracer" => BoardIds.PX4_FMUv4,
            "fmuv4-pro" => BoardIds.PX4_FMUv4_PRO,
            "fmuv5" or "pixhawk4" => BoardIds.PX4_FMUv5,
            "pixhawk5x" or "fmuv5x" => BoardIds.PX4_FMUv5X,
            "pixhawk6" or "fmuv6" => BoardIds.PX4_FMUv6,
            "pixhawk6x" or "fmuv6x" => BoardIds.PX4_FMUv6X,
            "pixhawk6c" or "fmuv6c" => BoardIds.PX4_FMUv6C,
            "cubeyellow" => BoardIds.CubeYellow,
            "cubeorange" => BoardIds.CubeOrange,
            "cubeorangeplus" or "cubeorange+" or "cubeorange-plus" => BoardIds.CubeOrangePlus,
            "cubepurple" => BoardIds.CubePurple,
            "durandal" => BoardIds.Durandal,
            "pix32v5" => BoardIds.Pix32v5,
            "matekf405" => BoardIds.MatekF405,
            "matekf765" or "matekf765-wing" => BoardIds.MatekF765,
            "matekh743" => BoardIds.MatekH743,
            "matekh743-mini" => BoardIds.MatekH743_Mini,
            "kakutef4" => BoardIds.KakuteF4,
            "kakutef7" => BoardIds.KakuteF7,
            "kakuteh7" => BoardIds.KakuteH7,
            "kakuteh7mini" => BoardIds.KakuteH7_Mini,
            "speedybeef4" => BoardIds.SpeedyBeeF4,
            "flywooh745" or "flywoof745" => BoardIds.FlywooF745,
            "skyh743" => BoardIds.SKYH743,
            _ => 0
        };
    }

    /// <summary>
    /// Gets the exact platform name to use for firmware download based on detected board ID.
    /// This ensures we download the correct firmware variant.
    /// </summary>
    public static string GetExactPlatformName(int boardId)
    {
        // Return the exact platform name that matches the ArduPilot firmware manifest
        return boardId switch
        {
            1063 => "CubeOrangePlus",  // CubeOrange+ requires CubeOrangePlus firmware
            140 => "CubeOrange",        // Original CubeOrange
            120 => "CubeYellow",
            9 => "fmuv2",               // Pixhawk 1, Cube Black
            50 => "fmuv5",              // Pixhawk 4
            _ => GetPlatformName(boardId)
        };
    }

    /// <summary>
    /// Gets compatible platform names for firmware search.
    /// Returns the primary platform ONLY - no fallbacks that could cause mismatches.
    /// </summary>
    public static string[] GetCompatiblePlatforms(int boardId)
    {
        // IMPORTANT: CubeOrangePlus (1063) must ONLY return CubeOrangePlus
        // It is NOT compatible with CubeOrange firmware!
        return boardId switch
        {
            BoardIds.CubeOrangePlus or 1063 => new[] { "CubeOrangePlus" },
            BoardIds.CubeOrange or 140 => new[] { "CubeOrange" },
            BoardIds.PX4_FMUv2 or BoardIds.CubeBlack or 9 => new[] { "fmuv3", "fmuv2" },
            BoardIds.MatekH743_Mini => new[] { "MatekH743-mini", "MatekH743" },
            BoardIds.KakuteH7_Mini => new[] { "KakuteH7Mini", "KakuteH7" },
            _ => new[] { GetPlatformName(boardId) }
        };
    }
}
