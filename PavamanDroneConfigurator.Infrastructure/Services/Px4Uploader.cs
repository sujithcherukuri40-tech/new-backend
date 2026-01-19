using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Models;
using System;
using System.Diagnostics;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// PX4/ChibiOS bootloader protocol implementation.
/// Matches Mission Planner's px4uploader exactly for ArduPilot firmware flashing.
/// Based on: https://github.com/PX4/PX4-Autopilot/blob/main/Tools/px_uploader.py
/// 
/// Protocol flow:
/// 1. Sync with bootloader (GET_SYNC)
/// 2. Get bootloader info (GET_DEVICE)
/// 3. Erase flash (CHIP_ERASE)
/// 4. Program flash (PROG_MULTI)
/// 5. Verify CRC (GET_CRC)
/// 6. Reboot (REBOOT)
/// </summary>
public sealed class Px4Uploader : IDisposable
{
    private readonly ILogger _logger;
    private SerialPort? _port;
    private bool _disposed;

    // Bootloader protocol revision range
    private const int BL_REV_MIN = 2;
    private const int BL_REV_MAX = 20;

    // Programming constants
    private const int PROG_MULTI_MAX = 252; // Maximum bytes per PROG_MULTI command
    private const int READ_MULTI_MAX = 255; // Maximum bytes per READ_MULTI command
    private const int DEFAULT_READ_TIMEOUT = 5000;
    private const int ERASE_TIMEOUT = 30000; // Erase can take up to 30 seconds
    private const int SYNC_ATTEMPTS = 3;

    // Detected board info
    public int BoardType { get; private set; }
    public int BoardRevision { get; private set; }
    public int BootloaderRevision { get; private set; }
    public int FlashSize { get; private set; }
    public int ExtFlashSize { get; private set; }
    public uint ChipId { get; private set; }
    public string ChipDescription { get; private set; } = string.Empty;
    public byte[] SerialNumber { get; private set; } = Array.Empty<byte>();

    public event Action<double>? ProgressEvent;
    public event Action<string>? LogEvent;

    public Px4Uploader(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Opens connection to the bootloader on specified port
    /// </summary>
    public void Open(string portName, int baudRate = 115200)
    {
        Log($"Opening port {portName} at {baudRate} baud");
        
        _port = new SerialPort(portName, baudRate)
        {
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
            ReadTimeout = 1000,   // Reduced from 5000 for faster detection
            WriteTimeout = 1000,  // Reduced for faster detection
            DtrEnable = false,
            RtsEnable = false
        };

        _port.Open();
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
        
        Log($"Port {portName} opened");
    }

    /// <summary>
    /// Closes the serial port connection
    /// </summary>
    public void Close()
    {
        if (_port?.IsOpen == true)
        {
            try
            {
                _port.Close();
            }
            catch { }
        }
    }

    /// <summary>
    /// Synchronizes with the bootloader - essential first step.
    /// Sends GET_SYNC and expects INSYNC + OK response.
    /// </summary>
    public void Sync()
    {
        _port?.DiscardInBuffer();
        
        for (int attempt = 0; attempt < SYNC_ATTEMPTS; attempt++)
        {
            try
            {
                Send(new byte[] { (byte)ProtocolCode.GET_SYNC, (byte)ProtocolCode.EOC });
                GetSync();
                return; // Success
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Sync attempt {Attempt} failed", attempt + 1);
                if (attempt == SYNC_ATTEMPTS - 1)
                    throw;
                    
                Thread.Sleep(100);
                _port?.DiscardInBuffer();
            }
        }
    }

    /// <summary>
    /// Identifies the connected board by querying bootloader.
    /// This is the main entry point after opening the port.
    /// </summary>
    public void Identify()
    {
        _port?.DiscardInBuffer();
        
        // Step 1: Sync with bootloader (multiple attempts)
        bool synced = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Sync();
                synced = true;
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Initial sync attempt {Attempt} failed", attempt + 1);
                Thread.Sleep(200);
                _port?.DiscardInBuffer();
            }
        }
        
        if (!synced)
        {
            throw new Exception("Failed to synchronize with bootloader after multiple attempts");
        }

        // Step 2: Get bootloader protocol revision
        BootloaderRevision = GetInfo(InfoType.BL_REV);
        Log($"Bootloader revision: {BootloaderRevision}");

        if (BootloaderRevision < BL_REV_MIN || BootloaderRevision > BL_REV_MAX)
        {
            throw new Exception($"Bootloader protocol revision {BootloaderRevision} not supported (expected {BL_REV_MIN}-{BL_REV_MAX})");
        }

        // Increase timeout after successful identification
        if (_port != null)
        {
            _port.WriteTimeout = 500;
        }

        // Step 3: Get board info - sync before each info request for reliability
        try
        {
            Sync();
            BoardType = GetInfo(InfoType.BOARD_ID);
        }
        catch
        {
            // Retry with fresh sync
            Thread.Sleep(50);
            Sync();
            BoardType = GetInfo(InfoType.BOARD_ID);
        }
        
        try
        {
            Sync();
            BoardRevision = GetInfo(InfoType.BOARD_REV);
        }
        catch
        {
            BoardRevision = 0; // Non-critical
        }
        
        try
        {
            Sync();
            FlashSize = GetInfo(InfoType.FLASH_SIZE);
        }
        catch
        {
            FlashSize = 2048 * 1024; // Default to 2MB
        }

        Log($"Board type: {BoardType}, revision: {BoardRevision}");
        Log($"Flash size: {FlashSize} bytes ({FlashSize / 1024} KB)");

        // Step 4: Get extended info for newer bootloaders (rev >= 5)
        if (BootloaderRevision >= 5)
        {
            try
            {
                Sync();
                ChipId = GetChipId();
                
                try
                {
                    Sync();
                    ChipDescription = GetChipDescription();
                }
                catch
                {
                    ChipDescription = string.Empty;
                }
                
                try
                {
                    Sync();
                    SerialNumber = GetSerialNumber();
                }
                catch
                {
                    SerialNumber = Array.Empty<byte>();
                }
                
                try
                {
                    Sync();
                    ExtFlashSize = GetInfo(InfoType.EXTF_SIZE);
                }
                catch
                {
                    ExtFlashSize = 0;
                }

                if (!string.IsNullOrEmpty(ChipDescription))
                {
                    Log($"Chip ID: 0x{ChipId:X8}");
                    Log($"Chip description: {ChipDescription}");
                }
                if (ExtFlashSize > 0)
                {
                    Log($"External flash size: {ExtFlashSize} bytes ({ExtFlashSize / 1024} KB)");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Extended info not available");
                // Sync again after failed extended info
                try { Sync(); } catch { }
            }
        }
    }

    /// <summary>
    /// Erases the internal flash memory.
    /// This can take up to 30 seconds!
    /// </summary>
    public void Erase()
    {
        Log("Erasing flash memory (this may take up to 30 seconds)...");
        
        // Sync before erase (required workaround for some bootloaders)
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Sync();
                break;
            }
            catch
            {
                Thread.Sleep(100);
                _port?.DiscardInBuffer();
            }
        }
        
        // Get BL_REV again as a workaround for bootloader bug
        try
        {
            GetInfo(InfoType.BL_REV);
        }
        catch
        {
            // Ignore - some bootloaders don't like this
            _port?.DiscardInBuffer();
        }
        
        // Send erase command
        Send(new byte[] { (byte)ProtocolCode.CHIP_ERASE, (byte)ProtocolCode.EOC });
        
        // Wait for erase with extended timeout
        // Erase can take a long time - poll for response
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ERASE_TIMEOUT)
        {
            if (_port != null && _port.BytesToRead > 0)
            {
                break;
            }
            Thread.Sleep(100);
        }
        
        if (_port == null || _port.BytesToRead == 0)
        {
            throw new TimeoutException("Timeout waiting for erase to complete");
        }
        
        GetSync();
        
        Log("Flash erased successfully");
    }

    /// <summary>
    /// Erases external flash memory (if present)
    /// </summary>
    public void EraseExternalFlash()
    {
        if (ExtFlashSize <= 0)
        {
            Log("No external flash to erase");
            return;
        }

        Log("Erasing external flash memory...");
        
        Send(new byte[] { (byte)ProtocolCode.EXTF_ERASE, (byte)ProtocolCode.EOC });
        WaitForBytes(1, ERASE_TIMEOUT);
        GetSync();
        
        Log("External flash erased");
    }

    /// <summary>
    /// Programs firmware to internal flash.
    /// Sends 252-byte chunks with PROG_MULTI command.
    /// </summary>
    public void Program(byte[] image)
    {
        Log($"Programming {image.Length} bytes ({image.Length / 1024} KB) to flash...");
        
        var groups = SplitIntoChunks(image, PROG_MULTI_MAX);
        int count = 0;
        int total = groups.Length;

        foreach (var chunk in groups)
        {
            ProgramMulti(chunk);
            count++;
            
            double progress = (double)count / total * 100.0;
            ProgressEvent?.Invoke(progress);
        }

        Log("Programming complete");
    }

    /// <summary>
    /// Programs firmware to external flash
    /// </summary>
    public void ProgramExternalFlash(byte[] image)
    {
        if (ExtFlashSize <= 0)
        {
            Log("No external flash present");
            return;
        }

        Log($"Programming {image.Length} bytes to external flash...");
        
        var groups = SplitIntoChunks(image, PROG_MULTI_MAX);
        int count = 0;
        int total = groups.Length;

        foreach (var chunk in groups)
        {
            ProgramMultiExternal(chunk);
            count++;
            
            double progress = (double)count / total * 100.0;
            ProgressEvent?.Invoke(progress);
        }

        Log("External flash programming complete");
    }

    /// <summary>
    /// Verifies firmware using CRC (bootloader rev 3+).
    /// This is the preferred verification method.
    /// </summary>
    public bool VerifyCrc(byte[] firmware, int maxSize)
    {
        if (BootloaderRevision < 3)
        {
            Log("CRC verification not supported on bootloader rev < 3");
            return true; // Skip verification on old bootloaders
        }

        Log("Verifying firmware CRC...");

        uint expectedCrc = CalculatePx4Crc(firmware, maxSize);
        
        Send(new byte[] { (byte)ProtocolCode.GET_CRC, (byte)ProtocolCode.EOC });
        uint reportedCrc = RecvUInt32();
        GetSync();

        Log($"Expected CRC:  0x{expectedCrc:X8}");
        Log($"Reported CRC:  0x{reportedCrc:X8}");

        if (expectedCrc != reportedCrc)
        {
            Log("CRC VERIFICATION FAILED!");
            return false;
        }

        Log("CRC verification passed");
        return true;
    }

    /// <summary>
    /// Verifies external flash CRC
    /// </summary>
    public bool VerifyExternalFlashCrc(byte[] firmware)
    {
        if (ExtFlashSize <= 0)
        {
            return true;
        }

        Log("Verifying external flash CRC...");

        uint expectedCrc = CalculatePx4Crc(firmware, firmware.Length);
        
        Send(new byte[] { (byte)ProtocolCode.EXTF_GET_CRC, (byte)ProtocolCode.EOC });
        uint reportedCrc = RecvUInt32();
        GetSync();

        Log($"Expected external CRC:  0x{expectedCrc:X8}");
        Log($"Reported external CRC:  0x{reportedCrc:X8}");

        return expectedCrc == reportedCrc;
    }

    /// <summary>
    /// Checks if firmware already matches what's on the board.
    /// Uses CRC comparison to avoid unnecessary re-flashing.
    /// </summary>
    public bool IsSameFirmware(byte[] firmware, int maxSize)
    {
        if (BootloaderRevision < 3)
        {
            return false; // Cannot check on old bootloaders
        }

        try
        {
            Sync();

            uint expectedCrc = CalculatePx4Crc(firmware, maxSize);
            
            Send(new byte[] { (byte)ProtocolCode.GET_CRC, (byte)ProtocolCode.EOC });
            uint reportedCrc = RecvUInt32();
            GetSync();

            bool same = expectedCrc == reportedCrc;
            if (same)
            {
                Log($"Firmware CRC matches (0x{expectedCrc:X8}) - already installed");
            }
            return same;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Reboots the board to run firmware.
    /// USB will disconnect and reconnect with different identity!
    /// </summary>
    public void Reboot()
    {
        Log("Rebooting board...");
        
        try
        {
            Send(new byte[] { (byte)ProtocolCode.REBOOT, (byte)ProtocolCode.EOC });
            _port?.DiscardInBuffer();
        }
        catch
        {
            // Port may close immediately on reboot - this is expected
        }
    }

    /// <summary>
    /// Sets the boot delay parameter (milliseconds to wait for bootloader sync on power-up)
    /// </summary>
    public void SetBootDelay(int delayMs)
    {
        Log($"Setting boot delay to {delayMs}ms");
        
        var cmd = new byte[3];
        cmd[0] = (byte)ProtocolCode.SET_DELAY;
        cmd[1] = (byte)(delayMs & 0xFF);
        cmd[2] = (byte)ProtocolCode.EOC;
        
        Send(cmd);
        GetSync();
    }

    /// <summary>
    /// Full upload workflow - erase, program, verify, reboot.
    /// </summary>
    public void Upload(Px4Firmware firmware)
    {
        if (_port == null || !_port.IsOpen)
        {
            throw new InvalidOperationException("Port not open");
        }

        // Validate board type
        if (BoardType != firmware.BoardId)
        {
            // Check for compatible board mappings (e.g., fmuv2 compatible with fmuv3)
            if (!IsCompatibleBoard(BoardType, firmware.BoardId))
            {
                throw new Exception($"Firmware not suitable for this board. Board ID: {BoardType}, Firmware expects: {firmware.BoardId}");
            }
            Log($"Using compatible board mapping: {BoardType} -> {firmware.BoardId}");
        }

        // Validate size
        if (FlashSize > 0 && firmware.ImageSize > FlashSize)
        {
            throw new Exception($"Firmware image too large ({firmware.ImageSize} bytes) for flash ({FlashSize} bytes)");
        }

        // Check if same firmware is already loaded
        if (firmware.ImageSize > 0 && IsSameFirmware(firmware.Image, FlashSize))
        {
            Log("Same firmware already installed. Skipping upload.");
            throw new Exception("Same firmware already installed");
        }

        // Erase and program internal flash
        if (firmware.ImageSize > 0)
        {
            Log("Erasing internal flash...");
            Erase();

            Log("Programming internal flash...");
            Program(firmware.Image);

            Log("Verifying internal flash...");
            if (!VerifyCrc(firmware.Image, FlashSize))
            {
                throw new Exception("CRC verification failed");
            }
        }

        // Handle external flash if present
        if (firmware.ExtFlashImageSize > 0 && ExtFlashSize > 0)
        {
            Log("Erasing external flash...");
            EraseExternalFlash();

            Log("Programming external flash...");
            ProgramExternalFlash(firmware.ExtFlashImage);

            Log("Verifying external flash...");
            if (!VerifyExternalFlashCrc(firmware.ExtFlashImage))
            {
                throw new Exception("External flash CRC verification failed");
            }
        }

        Log("Upload complete. Rebooting...");
        Reboot();
    }

    #region Private Protocol Methods

    /// <summary>
    /// Waits for and validates INSYNC + OK response from bootloader.
    /// </summary>
    private void GetSync()
    {
        _port?.BaseStream.Flush();
        
        byte c = Recv(1)[0];
        if (c != (byte)ProtocolCode.INSYNC)
        {
            throw new Exception($"Unexpected response 0x{c:X2} instead of INSYNC (0x{(byte)ProtocolCode.INSYNC:X2})");
        }

        c = Recv(1)[0];
        switch (c)
        {
            case (byte)ProtocolCode.OK:
                return; // Success
            case (byte)ProtocolCode.INVALID:
                throw new Exception("Bootloader reports INVALID operation");
            case (byte)ProtocolCode.FAILED:
                throw new Exception("Bootloader reports FAILED operation");
            case (byte)ProtocolCode.BAD_SILICON_REV:
                throw new Exception("Bootloader reports bad silicon revision");
            default:
                throw new Exception($"Unexpected response 0x{c:X2} instead of OK");
        }
    }

    private int GetInfo(InfoType param)
    {
        Send(new byte[] { (byte)ProtocolCode.GET_DEVICE, (byte)param, (byte)ProtocolCode.EOC });
        int info = RecvInt32();
        GetSync();
        return info;
    }

    private uint GetChipId()
    {
        Send(new byte[] { (byte)ProtocolCode.GET_CHIP, (byte)ProtocolCode.EOC });
        uint chip = RecvUInt32();
        GetSync();
        return chip;
    }

    private string GetChipDescription()
    {
        Send(new byte[] { (byte)ProtocolCode.GET_CHIP_DES, (byte)ProtocolCode.EOC });
        
        var sb = new StringBuilder();
        while (true)
        {
            byte c = Recv(1)[0];
            if (c == (byte)ProtocolCode.INSYNC)
            {
                break;
            }
            if (c != 0)
            {
                sb.Append((char)c);
            }
        }
        
        // Already received INSYNC, now get OK
        byte ok = Recv(1)[0];
        if (ok != (byte)ProtocolCode.OK)
        {
            throw new Exception($"Unexpected response 0x{ok:X2} instead of OK");
        }
        
        return sb.ToString();
    }

    private byte[] GetSerialNumber()
    {
        Send(new byte[] { (byte)ProtocolCode.GET_SN, (byte)ProtocolCode.EOC });
        
        // Serial number is 12 bytes (96 bits)
        var sn = Recv(12);
        GetSync();
        
        return sn;
    }

    private void ProgramMulti(byte[] data)
    {
        if (data.Length > PROG_MULTI_MAX)
        {
            throw new ArgumentException($"Data chunk too large ({data.Length} > {PROG_MULTI_MAX})");
        }

        var cmd = new byte[data.Length + 3];
        cmd[0] = (byte)ProtocolCode.PROG_MULTI;
        cmd[1] = (byte)data.Length;
        Array.Copy(data, 0, cmd, 2, data.Length);
        cmd[cmd.Length - 1] = (byte)ProtocolCode.EOC;

        Send(cmd);
        GetSync();
    }

    private void ProgramMultiExternal(byte[] data)
    {
        if (data.Length > PROG_MULTI_MAX)
        {
            throw new ArgumentException($"Data chunk too large ({data.Length} > {PROG_MULTI_MAX})");
        }

        var cmd = new byte[data.Length + 3];
        cmd[0] = (byte)ProtocolCode.EXTF_PROG_MULTI;
        cmd[1] = (byte)data.Length;
        Array.Copy(data, 0, cmd, 2, data.Length);
        cmd[cmd.Length - 1] = (byte)ProtocolCode.EOC;

        Send(cmd);
        GetSync();
    }

    private void Send(byte[] data)
    {
        if (_port == null || !_port.IsOpen)
        {
            throw new InvalidOperationException("Port not open");
        }
        _port.Write(data, 0, data.Length);
    }

    private byte[] Recv(int count)
    {
        if (_port == null || !_port.IsOpen)
        {
            throw new InvalidOperationException("Port not open");
        }

        var buffer = new byte[count];
        int offset = 0;
        var sw = Stopwatch.StartNew();
        
        while (offset < count)
        {
            if (sw.ElapsedMilliseconds > _port.ReadTimeout)
            {
                throw new TimeoutException($"Timeout waiting for {count} bytes (got {offset})");
            }

            if (_port.BytesToRead > 0)
            {
                int read = _port.Read(buffer, offset, count - offset);
                offset += read;
            }
            else
            {
                Thread.Sleep(1);
            }
        }

        return buffer;
    }

    private int RecvInt32()
    {
        var data = Recv(4);
        return BitConverter.ToInt32(data, 0);
    }

    private uint RecvUInt32()
    {
        var data = Recv(4);
        return BitConverter.ToUInt32(data, 0);
    }

    private void WaitForBytes(int count, int timeoutMs)
    {
        if (_port == null)
        {
            throw new InvalidOperationException("Port not open");
        }

        int originalTimeout = _port.ReadTimeout;
        _port.ReadTimeout = timeoutMs;
        
        try
        {
            var sw = Stopwatch.StartNew();
            while (_port.BytesToRead < count && sw.ElapsedMilliseconds < timeoutMs)
            {
                Thread.Sleep(50);
            }

            if (_port.BytesToRead < count)
            {
                throw new TimeoutException($"Timeout waiting for response (waited {timeoutMs}ms)");
            }
        }
        finally
        {
            _port.ReadTimeout = originalTimeout;
        }
    }

    #endregion

    #region Helper Methods

    private byte[][] SplitIntoChunks(byte[] data, int chunkSize)
    {
        int numChunks = (data.Length + chunkSize - 1) / chunkSize;
        var chunks = new byte[numChunks][];
        
        for (int i = 0; i < numChunks; i++)
        {
            int offset = i * chunkSize;
            int length = Math.Min(chunkSize, data.Length - offset);
            chunks[i] = new byte[length];
            Array.Copy(data, offset, chunks[i], 0, length);
        }
        
        return chunks;
    }

    /// <summary>
    /// Calculate CRC32 used by PX4 bootloader.
    /// Uses CRC-32/MPEG-2 polynomial (0x04C11DB7).
    /// Firmware is padded to flash size with 0xFF (unprogrammed flash state).
    /// </summary>
    private uint CalculatePx4Crc(byte[] data, int padToSize)
    {
        uint crc = 0;
        
        // Process actual data
        foreach (byte b in data)
        {
            crc = Crc32Byte(crc, b);
        }

        // Pad with 0xFF to flash size (simulates unprogrammed flash bytes)
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

    /// <summary>
    /// Checks if two board IDs are compatible.
    /// Some boards share the same ID or are compatible with each other.
    /// Uses BoardCompatibility class for comprehensive mapping.
    /// </summary>
    private bool IsCompatibleBoard(int detectedBoard, int firmwareBoard)
    {
        return BoardCompatibility.AreCompatible(detectedBoard, firmwareBoard);
    }

    private void Log(string message)
    {
        _logger.LogInformation("[PX4Uploader] {Message}", message);
        LogEvent?.Invoke(message);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
            return;
        
        _disposed = true;
        Close();
        _port?.Dispose();
    }

    #region Protocol Constants

    /// <summary>
    /// PX4 bootloader protocol codes - matches px_uploader.py exactly
    /// </summary>
    public enum ProtocolCode : byte
    {
        // Response codes
        NOP = 0x00,
        OK = 0x10,
        FAILED = 0x11,
        INSYNC = 0x12,
        INVALID = 0x13,
        BAD_SILICON_REV = 0x14,

        // Protocol commands
        EOC = 0x20,              // End of command
        GET_SYNC = 0x21,         // Sync request
        GET_DEVICE = 0x22,       // Get device info
        CHIP_ERASE = 0x23,       // Erase flash
        CHIP_VERIFY = 0x24,      // Verify flash (obsolete, use GET_CRC)
        PROG_MULTI = 0x27,       // Program multiple bytes
        READ_MULTI = 0x28,       // Read multiple bytes
        GET_CRC = 0x29,          // Get CRC (rev 3+)
        GET_OTP = 0x2A,          // Read OTP
        GET_SN = 0x2B,           // Get serial number
        GET_CHIP = 0x2C,         // Get chip ID
        SET_DELAY = 0x2D,        // Set boot delay
        GET_CHIP_DES = 0x2E,     // Get chip description
        REBOOT = 0x30,           // Reboot device

        // External flash commands (rev 5+)
        EXTF_ERASE = 0x34,       // Erase external flash
        EXTF_PROG_MULTI = 0x35,  // Program external flash
        EXTF_READ_MULTI = 0x36,  // Read external flash
        EXTF_GET_CRC = 0x37,     // Get external flash CRC
    }

    /// <summary>
    /// Device info types for GET_DEVICE command
    /// </summary>
    public enum InfoType : byte
    {
        BL_REV = 1,       // Bootloader revision
        BOARD_ID = 2,     // Board type ID
        BOARD_REV = 3,    // Board revision
        FLASH_SIZE = 4,   // Max firmware size (internal flash)
        VEC_AREA = 5,     // Vector area size
        EXTF_SIZE = 6,    // External flash size
    }

    #endregion
}
