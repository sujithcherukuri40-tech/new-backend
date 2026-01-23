using System;
using System.IO;
using System.IO.Compression;
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
    /// Declared image size from APJ file (decompressed size)
    /// </summary>
    public int DeclaredImageSize { get; private set; }

    /// <summary>
    /// External flash image bytes (for boards with external flash)
    /// </summary>
    public byte[] ExtFlashImage { get; private set; } = Array.Empty<byte>();

    /// <summary>
    /// Size of external flash image
    /// </summary>
    public int ExtFlashImageSize => ExtFlashImage.Length;

    /// <summary>
    /// Declared external flash image size from APJ file (decompressed size)
    /// </summary>
    public int DeclaredExtFlashImageSize { get; private set; }

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

        // Extract image size (declared decompressed size)
        if (root.TryGetProperty("image_size", out var imageSizeElement))
        {
            DeclaredImageSize = imageSizeElement.GetInt32();
        }

        // Extract firmware image (required)
        if (root.TryGetProperty("image", out var imageElement))
        {
            var base64Image = imageElement.GetString();
            if (string.IsNullOrEmpty(base64Image))
            {
                throw new InvalidDataException("APJ file has empty 'image' field");
            }

            // Decompress the firmware image if image_size is specified
            if (DeclaredImageSize > 0)
            {
                // Decode base64 data
                byte[] compressedData = Convert.FromBase64String(base64Image);
                
                // Calculate size with 4-byte alignment padding
                int paddedSize = DeclaredImageSize + (DeclaredImageSize % 4 == 0 ? 0 : (4 - DeclaredImageSize % 4));
                
                // Decompress using zlib
                using var compressedStream = new MemoryStream(compressedData);
                using var decompressionStream = new ZLibStream(compressedStream, CompressionMode.Decompress);
                
                // Allocate buffer with padding
                Image = new byte[paddedSize];
                
                // Read decompressed data
                int totalRead = 0;
                int bytesRead;
                while (totalRead < DeclaredImageSize && 
                       (bytesRead = decompressionStream.Read(Image, totalRead, DeclaredImageSize - totalRead)) > 0)
                {
                    totalRead += bytesRead;
                }
                
                if (totalRead != DeclaredImageSize)
                {
                    throw new InvalidDataException($"Decompressed image size mismatch: expected {DeclaredImageSize}, got {totalRead}");
                }
                
                // Padding bytes are already zeroed (default array initialization)
            }
            else
            {
                // No decompression needed, just decode base64
                Image = Convert.FromBase64String(base64Image);
            }
        }
        else
        {
            throw new InvalidDataException("APJ file missing required 'image' field");
        }

        // Extract external flash image size (declared decompressed size)
        if (root.TryGetProperty("extf_image_size", out var extfImageSizeElement))
        {
            DeclaredExtFlashImageSize = extfImageSizeElement.GetInt32();
        }

        // Extract external flash image (optional)
        if (root.TryGetProperty("extf_image", out var extfImageElement))
        {
            var base64ExtfImage = extfImageElement.GetString();
            if (!string.IsNullOrEmpty(base64ExtfImage))
            {
                // Decompress the external flash image if extf_image_size is specified
                if (DeclaredExtFlashImageSize > 0)
                {
                    // Decode base64 data
                    byte[] compressedData = Convert.FromBase64String(base64ExtfImage);
                    
                    // Calculate size with 4-byte alignment padding
                    int paddedSize = DeclaredExtFlashImageSize + (DeclaredExtFlashImageSize % 4 == 0 ? 0 : (4 - DeclaredExtFlashImageSize % 4));
                    
                    // Pre-fill with 0xFF (unprogrammed flash state)
                    ExtFlashImage = new byte[paddedSize];
                    for (int i = 0; i < ExtFlashImage.Length; i++)
                    {
                        ExtFlashImage[i] = 0xFF;
                    }
                    
                    // Decompress using zlib
                    using var compressedStream = new MemoryStream(compressedData);
                    using var decompressionStream = new ZLibStream(compressedStream, CompressionMode.Decompress);
                    
                    // Read decompressed data
                    int totalRead = 0;
                    int bytesRead;
                    while (totalRead < DeclaredExtFlashImageSize && 
                           (bytesRead = decompressionStream.Read(ExtFlashImage, totalRead, DeclaredExtFlashImageSize - totalRead)) > 0)
                    {
                        totalRead += bytesRead;
                    }
                    
                    if (totalRead != DeclaredExtFlashImageSize)
                    {
                        throw new InvalidDataException($"Decompressed external flash image size mismatch: expected {DeclaredExtFlashImageSize}, got {totalRead}");
                    }
                }
                else
                {
                    // No decompression needed, just decode base64
                    ExtFlashImage = Convert.FromBase64String(base64ExtfImage);
                }
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
    /// CRC-32 lookup table (same as MissionPlanner)
    /// Standard CRC-32 polynomial (0xEDB88320 reversed)
    /// </summary>
    private static readonly uint[] CrcTable = new uint[]
    {
        0x00000000, 0x77073096, 0xee0e612c, 0x990951ba, 0x076dc419, 0x706af48f, 0xe963a535, 0x9e6495a3,
        0x0edb8832, 0x79dcb8a4, 0xe0d5e91e, 0x97d2d988, 0x09b64c2b, 0x7eb17cbd, 0xe7b82d07, 0x90bf1d91,
        0x1db71064, 0x6ab020f2, 0xf3b97148, 0x84be41de, 0x1adad47d, 0x6ddde4eb, 0xf4d4b551, 0x83d385c7,
        0x136c9856, 0x646ba8c0, 0xfd62f97a, 0x8a65c9ec, 0x14015c4f, 0x63066cd9, 0xfa0f3d63, 0x8d080df5,
        0x3b6e20c8, 0x4c69105e, 0xd56041e4, 0xa2677172, 0x3c03e4d1, 0x4b04d447, 0xd20d85fd, 0xa50ab56b,
        0x35b5a8fa, 0x42b2986c, 0xdbbbc9d6, 0xacbcf940, 0x32d86ce3, 0x45df5c75, 0xdcd60dcf, 0xabd13d59,
        0x26d930ac, 0x51de003a, 0xc8d75180, 0xbfd06116, 0x21b4f4b5, 0x56b3c423, 0xcfba9599, 0xb8bda50f,
        0x2802b89e, 0x5f058808, 0xc60cd9b2, 0xb10be924, 0x2f6f7c87, 0x58684c11, 0xc1611dab, 0xb6662d3d,
        0x76dc4190, 0x01db7106, 0x98d220bc, 0xefd5102a, 0x71b18589, 0x06b6b51f, 0x9fbfe4a5, 0xe8b8d433,
        0x7807c9a2, 0x0f00f934, 0x9609a88e, 0xe10e9818, 0x7f6a0dbb, 0x086d3d2d, 0x91646c97, 0xe6635c01,
        0x6b6b51f4, 0x1c6c6162, 0x856530d8, 0xf262004e, 0x6c0695ed, 0x1b01a57b, 0x8208f4c1, 0xf50fc457,
        0x65b0d9c6, 0x12b7e950, 0x8bbeb8ea, 0xfcb9887c, 0x62dd1ddf, 0x15da2d49, 0x8cd37cf3, 0xfbd44c65,
        0x4db26158, 0x3ab551ce, 0xa3bc0074, 0xd4bb30e2, 0x4adfa541, 0x3dd895d7, 0xa4d1c46d, 0xd3d6f4fb,
        0x4369e96a, 0x346ed9fc, 0xad678846, 0xda60b8d0, 0x44042d73, 0x33031de5, 0xaa0a4c5f, 0xdd0d7cc9,
        0x5005713c, 0x270241aa, 0xbe0b1010, 0xc90c2086, 0x5768b525, 0x206f85b3, 0xb966d409, 0xce61e49f,
        0x5edef90e, 0x29d9c998, 0xb0d09822, 0xc7d7a8b4, 0x59b33d17, 0x2eb40d81, 0xb7bd5c3b, 0xc0ba6cad,
        0xedb88320, 0x9abfb3b6, 0x03b6e20c, 0x74b1d29a, 0xead54739, 0x9dd277af, 0x04db2615, 0x73dc1683,
        0xe3630b12, 0x94643b84, 0x0d6d6a3e, 0x7a6a5aa8, 0xe40ecf0b, 0x9309ff9d, 0x0a00ae27, 0x7d079eb1,
        0xf00f9344, 0x8708a3d2, 0x1e01f268, 0x6906c2fe, 0xf762575d, 0x806567cb, 0x196c3671, 0x6e6b06e7,
        0xfed41b76, 0x89d32be0, 0x10da7a5a, 0x67dd4acc, 0xf9b9df6f, 0x8ebeeff9, 0x17b7be43, 0x60b08ed5,
        0xd6d6a3e8, 0xa1d1937e, 0x38d8c2c4, 0x4fdff252, 0xd1bb67f1, 0xa6bc5767, 0x3fb506dd, 0x48b2364b,
        0xd80d2bda, 0xaf0a1b4c, 0x36034af6, 0x41047a60, 0xdf60efc3, 0xa867df55, 0x316e8eef, 0x4669be79,
        0xcb61b38c, 0xbc66831a, 0x256fd2a0, 0x5268e236, 0xcc0c7795, 0xbb0b4703, 0x220216b9, 0x5505262f,
        0xc5ba3bbe, 0xb2bd0b28, 0x2bb45a92, 0x5cb36a04, 0xc2d7ffa7, 0xb5d0cf31, 0x2cd99e8b, 0x5bdeae1d,
        0x9b64c2b0, 0xec63f226, 0x756aa39c, 0x026d930a, 0x9c0906a9, 0xeb0e363f, 0x72076785, 0x05005713,
        0x95bf4a82, 0xe2b87a14, 0x7bb12bae, 0x0cb61b38, 0x92d28e9b, 0xe5d5be0d, 0x7cdcefb7, 0x0bdbdf21,
        0x86d3d2d4, 0xf1d4e242, 0x68ddb3f8, 0x1fda836e, 0x81be16cd, 0xf6b9265b, 0x6fb077e1, 0x18b74777,
        0x88085ae6, 0xff0f6a70, 0x66063bca, 0x11010b5c, 0x8f659eff, 0xf862ae69, 0x616bffd3, 0x166ccf45,
        0xa00ae278, 0xd70dd2ee, 0x4e048354, 0x3903b3c2, 0xa7672661, 0xd06016f7, 0x4969474d, 0x3e6e77db,
        0xaed16a4a, 0xd9d65adc, 0x40df0b66, 0x37d83bf0, 0xa9bcae53, 0xdebb9ec5, 0x47b2cf7f, 0x30b5ffe9,
        0xbdbdf21c, 0xcabac28a, 0x53b39330, 0x24b4a3a6, 0xbad03605, 0xcdd70693, 0x54de5729, 0x23d967bf,
        0xb3667a2e, 0xc4614ab8, 0x5d681b02, 0x2a6f2b94, 0xb40bbe37, 0xc30c8ea1, 0x5a05df1b, 0x2d02ef8d
    };

    /// <summary>
    /// Calculate CRC32 using lookup table (same as MissionPlanner)
    /// Standard CRC-32 algorithm used by PX4 bootloader
    /// </summary>
    private uint CalculatePx4Crc(byte[] data, int padToSize)
    {
        uint crc = 0;

        // Process actual data
        foreach (byte b in data)
        {
            uint index = (crc ^ b) & 0xff;
            crc = CrcTable[index] ^ (crc >> 8);
        }

        // Pad with 0xFF to specified size (simulates unprogrammed flash)
        int paddingBytes = padToSize - data.Length;
        for (int i = 0; i < paddingBytes; i++)
        {
            uint index = (crc ^ 0xFF) & 0xff;
            crc = CrcTable[index] ^ (crc >> 8);
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
