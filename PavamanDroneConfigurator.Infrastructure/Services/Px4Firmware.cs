using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Firmware file parser for ArduPilot/PX4 firmware files.
/// Supports APJ (ArduPilot JSON), PX4, BIN, and HEX formats.
/// Matches Mission Planner's firmware parsing exactly.
/// </summary>
public sealed class Px4Firmware
{
    /// <summary>
    /// Board ID from firmware metadata
    /// </summary>
    public int BoardId { get; private set; }

    /// <summary>
    /// Board revision from firmware metadata
    /// </summary>
    public int BoardRevision { get; private set; }

    /// <summary>
    /// Firmware version string
    /// </summary>
    public string Version { get; private set; } = string.Empty;

    /// <summary>
    /// Git hash/commit from firmware metadata
    /// </summary>
    public string GitHash { get; private set; } = string.Empty;

    /// <summary>
    /// Vehicle type (e.g., "Copter", "Plane")
    /// </summary>
    public string VehicleType { get; private set; } = string.Empty;

    /// <summary>
    /// Internal flash image bytes
    /// </summary>
    public byte[] Image { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// Size of internal flash image
    /// </summary>
    public int ImageSize => Image.Length;

    /// <summary>
    /// External flash image bytes (for boards with external flash)
    /// </summary>
    public byte[] ExtFlashImage { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// Size of external flash image
    /// </summary>
    public int ExtFlashImageSize => ExtFlashImage.Length;

    /// <summary>
    /// Summary string from APJ metadata
    /// </summary>
    public string Summary { get; private set; } = string.Empty;

    /// <summary>
    /// Build type (e.g., "copter", "plane")
    /// </summary>
    public string BuildType { get; private set; } = string.Empty;

    /// <summary>
    /// Loads firmware from file path
    /// </summary>
    public static Px4Firmware FromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Firmware file not found", filePath);
        }

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var firmware = new Px4Firmware();

        switch (extension)
        {
            case ".apj":
                firmware.LoadApjFile(filePath);
                break;
            case ".px4":
                firmware.LoadPx4File(filePath);
                break;
            case ".bin":
                firmware.LoadBinFile(filePath);
                break;
            case ".hex":
                firmware.LoadHexFile(filePath);
                break;
            default:
                throw new NotSupportedException($"Unsupported firmware format: {extension}");
        }

        return firmware;
    }

    /// <summary>
    /// Loads firmware from byte array with explicit format
    /// </summary>
    public static Px4Firmware FromBytes(byte[] data, string format)
    {
        var firmware = new Px4Firmware();

        switch (format.ToLowerInvariant())
        {
            case "apj":
                firmware.ParseApjContent(Encoding.UTF8.GetString(data));
                break;
            case "px4":
                firmware.ParsePx4Content(data);
                break;
            case "bin":
                firmware.Image = data;
                break;
            default:
                throw new NotSupportedException($"Unsupported firmware format: {format}");
        }

        return firmware;
    }

    /// <summary>
    /// Calculates CRC32 for firmware verification
    /// Uses same algorithm as PX4 bootloader
    /// </summary>
    public uint CalculateCrc(int padToSize)
    {
        return CalculatePx4Crc(Image, padToSize);
    }

    /// <summary>
    /// Calculates external flash CRC
    /// </summary>
    public uint CalculateExtFlashCrc()
    {
        return CalculatePx4Crc(ExtFlashImage, ExtFlashImage.Length);
    }

    #region File Format Parsers

    private void LoadApjFile(string filePath)
    {
        var json = File.ReadAllText(filePath);
        ParseApjContent(json);
    }

    private void ParseApjContent(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Extract board ID (required)
        if (root.TryGetProperty("board_id", out var boardIdElement))
        {
            BoardId = boardIdElement.GetInt32();
        }
        else
        {
            throw new InvalidDataException("APJ file missing required 'board_id' field");
        }

        // Extract board revision
        if (root.TryGetProperty("board_revision", out var boardRevElement))
        {
            BoardRevision = boardRevElement.GetInt32();
        }

        // Extract firmware image (required)
        if (root.TryGetProperty("image", out var imageElement))
        {
            var base64Image = imageElement.GetString();
            if (string.IsNullOrEmpty(base64Image))
            {
                throw new InvalidDataException("APJ file has empty 'image' field");
            }
            Image = Convert.FromBase64String(base64Image);
        }
        else
        {
            throw new InvalidDataException("APJ file missing required 'image' field");
        }

        // Extract external flash image (optional)
        if (root.TryGetProperty("extf_image", out var extfImageElement))
        {
            var base64ExtfImage = extfImageElement.GetString();
            if (!string.IsNullOrEmpty(base64ExtfImage))
            {
                ExtFlashImage = Convert.FromBase64String(base64ExtfImage);
            }
        }

        // Extract optional metadata
        if (root.TryGetProperty("git_identity", out var gitElement))
        {
            GitHash = gitElement.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("summary", out var summaryElement))
        {
            Summary = summaryElement.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("vehicletype", out var vehicleTypeElement))
        {
            VehicleType = vehicleTypeElement.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("build_type", out var buildTypeElement))
        {
            BuildType = buildTypeElement.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("version", out var versionElement))
        {
            Version = versionElement.GetString() ?? string.Empty;
        }

        // Try alternative version field names
        if (string.IsNullOrEmpty(Version))
        {
            if (root.TryGetProperty("mav-firmware-version", out var mavVersionElement))
            {
                Version = mavVersionElement.GetString() ?? string.Empty;
            }
        }
    }

    private void LoadPx4File(string filePath)
    {
        var data = File.ReadAllBytes(filePath);
        ParsePx4Content(data);
    }

    private void ParsePx4Content(byte[] data)
    {
        // PX4 file format is same as APJ - JSON with base64 image
        // Some files may have binary header, try to find JSON start
        int jsonStart = FindJsonStart(data);
        
        if (jsonStart >= 0)
        {
            var json = Encoding.UTF8.GetString(data, jsonStart, data.Length - jsonStart);
            ParseApjContent(json);
        }
        else
        {
            // Treat as raw binary
            Image = data;
        }
    }

    private void LoadBinFile(string filePath)
    {
        Image = File.ReadAllBytes(filePath);
        
        // Try to infer board ID from filename
        var fileName = Path.GetFileNameWithoutExtension(filePath);
        InferBoardIdFromFilename(fileName);
    }

    private void LoadHexFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        Image = ParseIntelHex(lines);
    }

    /// <summary>
    /// Parse Intel HEX format file
    /// </summary>
    private byte[] ParseIntelHex(string[] lines)
    {
        using var ms = new MemoryStream();
        uint baseAddress = 0;
        uint minAddress = uint.MaxValue;
        uint maxAddress = 0;

        // First pass: determine address range
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith(':'))
                continue;

            var hex = line.Substring(1);
            if (hex.Length < 10)
                continue;

            int byteCount = Convert.ToInt32(hex.Substring(0, 2), 16);
            int address = Convert.ToInt32(hex.Substring(2, 4), 16);
            int recordType = Convert.ToInt32(hex.Substring(6, 2), 16);

            switch (recordType)
            {
                case 0x00: // Data record
                    uint fullAddress = baseAddress + (uint)address;
                    minAddress = Math.Min(minAddress, fullAddress);
                    maxAddress = Math.Max(maxAddress, fullAddress + (uint)byteCount);
                    break;
                case 0x02: // Extended segment address
                    baseAddress = (uint)(Convert.ToInt32(hex.Substring(8, 4), 16) << 4);
                    break;
                case 0x04: // Extended linear address
                    baseAddress = (uint)(Convert.ToInt32(hex.Substring(8, 4), 16) << 16);
                    break;
            }
        }

        if (minAddress == uint.MaxValue)
        {
            return Array.Empty<byte>();
        }

        // Allocate buffer
        var buffer = new byte[maxAddress - minAddress];
        Array.Fill(buffer, (byte)0xFF); // Fill with 0xFF (unprogrammed flash)

        // Second pass: populate data
        baseAddress = 0;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith(':'))
                continue;

            var hex = line.Substring(1);
            if (hex.Length < 10)
                continue;

            int byteCount = Convert.ToInt32(hex.Substring(0, 2), 16);
            int address = Convert.ToInt32(hex.Substring(2, 4), 16);
            int recordType = Convert.ToInt32(hex.Substring(6, 2), 16);

            switch (recordType)
            {
                case 0x00: // Data record
                    uint fullAddress = baseAddress + (uint)address - minAddress;
                    for (int i = 0; i < byteCount; i++)
                    {
                        buffer[fullAddress + i] = Convert.ToByte(hex.Substring(8 + i * 2, 2), 16);
                    }
                    break;
                case 0x02: // Extended segment address
                    baseAddress = (uint)(Convert.ToInt32(hex.Substring(8, 4), 16) << 4);
                    break;
                case 0x04: // Extended linear address
                    baseAddress = (uint)(Convert.ToInt32(hex.Substring(8, 4), 16) << 16);
                    break;
                case 0x01: // End of file
                    break;
            }
        }

        return buffer;
    }

    private int FindJsonStart(byte[] data)
    {
        // Look for JSON object start '{'
        for (int i = 0; i < Math.Min(100, data.Length); i++)
        {
            if (data[i] == '{')
            {
                // Verify it looks like JSON
                if (i + 10 < data.Length)
                {
                    var sample = Encoding.ASCII.GetString(data, i, 10);
                    if (sample.Contains("\""))
                    {
                        return i;
                    }
                }
            }
        }
        return -1;
    }

    private void InferBoardIdFromFilename(string filename)
    {
        // Try to determine board ID from filename patterns
        filename = filename.ToLowerInvariant();

        if (filename.Contains("fmuv5") || filename.Contains("pixhawk4"))
        {
            BoardId = 50;
        }
        else if (filename.Contains("fmuv4") || filename.Contains("pixracer"))
        {
            BoardId = 11;
        }
        else if (filename.Contains("fmuv3") || filename.Contains("cube"))
        {
            BoardId = 9;
        }
        else if (filename.Contains("fmuv2") || filename.Contains("pixhawk"))
        {
            BoardId = 9;
        }
        else if (filename.Contains("cubeorange"))
        {
            BoardId = 140;
        }
        else if (filename.Contains("cubeblack"))
        {
            BoardId = 9;
        }
        else if (filename.Contains("matek") && filename.Contains("h743"))
        {
            BoardId = 1013;
        }
        // Add more patterns as needed
    }

    #endregion

    #region CRC Calculation

    /// <summary>
    /// Calculate CRC32 as used by PX4 bootloader (CRC-32/MPEG-2)
    /// </summary>
    private uint CalculatePx4Crc(byte[] data, int padToSize)
    {
        uint crc = 0;

        // Process actual data
        foreach (byte b in data)
        {
            crc = Crc32Byte(crc, b);
        }

        // Pad with 0xFF to specified size (simulates unprogrammed flash)
        int paddingBytes = padToSize - data.Length;
        for (int i = 0; i < paddingBytes; i++)
        {
            crc = Crc32Byte(crc, 0xFF);
        }

        return crc;
    }

    private uint Crc32Byte(uint crc, byte data)
    {
        crc ^= (uint)data << 24;
        for (int i = 0; i < 8; i++)
        {
            if ((crc & 0x80000000) != 0)
            {
                crc = (crc << 1) ^ 0x04C11DB7; // CRC-32/MPEG-2 polynomial
            }
            else
            {
                crc <<= 1;
            }
        }
        return crc;
    }

    #endregion

    /// <summary>
    /// Returns a summary of the firmware
    /// </summary>
    public override string ToString()
    {
        return $"Firmware: BoardId={BoardId}, Size={ImageSize} bytes, Version={Version}, Type={VehicleType}";
    }
}
