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
    
    // Mission Planner compatible timeouts and delays
    private const int PROGRAM_TIMEOUT = 2000;  // Timeout for program operations (was 1000ms)
    private const int STABILIZATION_DELAY = 500; // USB stabilization delay after connect
    private const int PROGRAM_CHUNK_DELAY = 5;   // Small delay between chunks to avoid USB overruns
    private const int MAX_PROGRAM_RETRIES = 3;   // Retry failed program chunks
    private const int CRC_VERIFICATION_TIMEOUT = 30000; // CRC calculation can take up to 30 seconds
    private const double CRC_PROGRESS_DURATION_MS = 10000.0; // Duration for progress bar during CRC verification
    
    // INVALID response detection constants
    private const int MIN_BYTES_FOR_INVALID_CHECK = 4; // Minimum bytes before checking for INVALID
    private const int INVALID_HEADER_SIZE = 2; // INSYNC + INVALID bytes
    
    // Consecutive failure thresholds before giving up
    // These are tuned based on Mission Planner's behavior for the ~60% failure zone
    private const int MAX_CONSECUTIVE_TIMEOUTS = 5;  // Timeout failures before abort
    private const int MAX_CONSECUTIVE_IO_ERRORS = 3; // IO errors (USB disconnect) before abort

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
    /// Implements Mission Planner's connection sequence with stabilization delay.
    /// </summary>
    public void Open(string portName, int baudRate = 115200)
    {
        Log($"Opening port {portName} at {baudRate} baud");
        
        _port = new SerialPort(portName, baudRate)
        {
            DataBits = 8,
            Parity = Parity.None,
            StopBits = StopBits.One,
            ReadTimeout = PROGRAM_TIMEOUT,   // Increased from 1000 for program reliability
            WriteTimeout = PROGRAM_TIMEOUT,  // Increased from 1000 for program reliability
            DtrEnable = false,
            RtsEnable = false
        };

        _port.Open();
        _port.DiscardInBuffer();
        _port.DiscardOutBuffer();
        
        // Mission Planner compatible: stabilization delay after opening port
        // This prevents "System.TimeoutException: The write timed out" errors
        Thread.Sleep(STABILIZATION_DELAY);
        
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
    /// Erases external flash memory (if present).
    /// Mission Planner compatible: sends image size as part of erase command.
    /// </summary>
    public void EraseExternalFlash(int imageSize = 0)
    {
        if (ExtFlashSize <= 0)
        {
            Log("No external flash to erase");
            return;
        }

        Log("Erasing external flash memory...");
        
        // Sync before external flash erase
        try
        {
            Sync();
        }
        catch
        {
            _port?.DiscardInBuffer();
        }
        
        // Get BL_REV as workaround for bootloader bug (same as internal erase)
        try
        {
            GetInfo(InfoType.BL_REV);
        }
        catch
        {
            _port?.DiscardInBuffer();
        }
        
        // Mission Planner sends the image size as part of the erase command
        byte[] sizeBytes = BitConverter.GetBytes(imageSize);
        Send(new byte[] { 
            (byte)ProtocolCode.EXTF_ERASE,
            sizeBytes[0], sizeBytes[1], sizeBytes[2], sizeBytes[3],
            (byte)ProtocolCode.EOC 
        });
        
        // External flash erase can take a while - poll for response with progress reporting
        var sw = Stopwatch.StartNew();
        int lastProgress = 0;
        
        while (sw.ElapsedMilliseconds < ERASE_TIMEOUT)
        {
            if (_port != null && _port.BytesToRead > 0)
            {
                // Check if we got a progress byte or the final sync
                byte[] buffer = new byte[1];
                _port.Read(buffer, 0, 1);
                
                if (buffer[0] == (byte)ProtocolCode.INSYNC)
                {
                    // Got INSYNC, now get OK
                    byte ok = Recv(1)[0];
                    if (ok == (byte)ProtocolCode.OK)
                    {
                        ProgressEvent?.Invoke(100);
                        Log("External flash erased successfully");
                        return;
                    }
                    else if (ok == (byte)ProtocolCode.INVALID)
                    {
                        throw new Exception("Bootloader reports INVALID operation during external flash erase");
                    }
                    else if (ok == (byte)ProtocolCode.FAILED)
                    {
                        throw new Exception("Bootloader reports FAILED during external flash erase");
                    }
                }
                else if (buffer[0] < 100)
                {
                    // Progress percentage
                    if (buffer[0] != lastProgress)
                    {
                        lastProgress = buffer[0];
                        ProgressEvent?.Invoke(lastProgress);
                    }
                }
            }
            Thread.Sleep(100);
        }
        
        throw new TimeoutException("Timeout waiting for external flash erase to complete");
    }

    /// <summary>
    /// Programs firmware to internal flash.
    /// Sends 252-byte chunks with PROG_MULTI command.
    /// Implements Mission Planner compatible retry logic for the ~60% failure zone.
    /// </summary>
    public void Program(byte[] image)
    {
        Log($"Programming {image.Length} bytes ({image.Length / 1024} KB) to flash...");
        
        // Sync before programming to ensure clean state
        try
        {
            Sync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pre-program sync failed, continuing anyway");
            _port?.DiscardInBuffer();
        }
        
        var groups = SplitIntoChunks(image, PROG_MULTI_MAX);
        int count = 0;
        int total = groups.Length;
        int consecutiveFailures = 0;

        foreach (var chunk in groups)
        {
            bool chunkProgrammed = false;
            
            // Retry logic for each chunk - addresses the ~60% failure zone
            for (int retry = 0; retry < MAX_PROGRAM_RETRIES && !chunkProgrammed; retry++)
            {
                try
                {
                    if (retry > 0)
                    {
                        _logger.LogDebug("Retrying chunk {Count} (attempt {Retry})", count + 1, retry + 1);
                        Thread.Sleep(100); // Brief delay before retry
                        _port?.DiscardInBuffer();
                        
                        // Re-sync after failure
                        try { Sync(); } catch { _port?.DiscardInBuffer(); }
                    }
                    
                    ProgramMulti(chunk);
                    chunkProgrammed = true;
                    consecutiveFailures = 0;
                    
                    // Small delay between chunks to prevent USB overruns
                    if (PROGRAM_CHUNK_DELAY > 0 && count < total - 1)
                    {
                        Thread.Sleep(PROGRAM_CHUNK_DELAY);
                    }
                }
                catch (TimeoutException ex)
                {
                    consecutiveFailures++;
                    _logger.LogWarning(ex, "Timeout programming chunk {Count}/{Total} (attempt {Retry})", 
                        count + 1, total, retry + 1);
                    
                    if (consecutiveFailures > MAX_CONSECUTIVE_TIMEOUTS)
                    {
                        throw new Exception($"Lost communication with the board after {consecutiveFailures} consecutive failures at {(count * 100.0 / total):F0}%");
                    }
                }
                catch (IOException ex)
                {
                    consecutiveFailures++;
                    _logger.LogWarning(ex, "IO error programming chunk {Count}/{Total} (attempt {Retry})", 
                        count + 1, total, retry + 1);
                    
                    // IOException often means USB disconnect - this is the ~60% failure zone
                    if (consecutiveFailures > MAX_CONSECUTIVE_IO_ERRORS)
                    {
                        throw new IOException($"Lost communication with the board at {(count * 100.0 / total):F0}%. Check USB cable and connection.", ex);
                    }
                }
            }
            
            if (!chunkProgrammed)
            {
                throw new Exception($"Failed to program chunk {count + 1}/{total} after {MAX_PROGRAM_RETRIES} retries");
            }
            
            count++;
            
            double progress = (double)count / total * 100.0;
            ProgressEvent?.Invoke(progress);
        }

        Log("Programming complete");
    }

    /// <summary>
    /// Programs firmware to external flash.
    /// Implements same retry logic as internal flash programming.
    /// </summary>
    public void ProgramExternalFlash(byte[] image)
    {
        if (ExtFlashSize <= 0)
        {
            Log("No external flash present");
            return;
        }

        Log($"Programming {image.Length} bytes to external flash...");
        
        // Sync before programming
        try
        {
            Sync();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Pre-program sync failed, continuing anyway");
            _port?.DiscardInBuffer();
        }
        
        var groups = SplitIntoChunks(image, PROG_MULTI_MAX);
        int count = 0;
        int total = groups.Length;
        int consecutiveFailures = 0;

        foreach (var chunk in groups)
        {
            bool chunkProgrammed = false;
            
            for (int retry = 0; retry < MAX_PROGRAM_RETRIES && !chunkProgrammed; retry++)
            {
                try
                {
                    if (retry > 0)
                    {
                        _logger.LogDebug("Retrying external flash chunk {Count} (attempt {Retry})", count + 1, retry + 1);
                        Thread.Sleep(100);
                        _port?.DiscardInBuffer();
                        try { Sync(); } catch { _port?.DiscardInBuffer(); }
                    }
                    
                    ProgramMultiExternal(chunk);
                    chunkProgrammed = true;
                    consecutiveFailures = 0;
                    
                    if (PROGRAM_CHUNK_DELAY > 0 && count < total - 1)
                    {
                        Thread.Sleep(PROGRAM_CHUNK_DELAY);
                    }
                }
                catch (TimeoutException ex)
                {
                    consecutiveFailures++;
                    _logger.LogWarning(ex, "Timeout programming external flash chunk {Count}/{Total}", count + 1, total);
                    
                    if (consecutiveFailures > MAX_CONSECUTIVE_TIMEOUTS)
                    {
                        throw new Exception($"Lost communication during external flash programming at {(count * 100.0 / total):F0}%");
                    }
                }
                catch (IOException ex)
                {
                    consecutiveFailures++;
                    _logger.LogWarning(ex, "IO error programming external flash chunk {Count}/{Total}", count + 1, total);
                    
                    if (consecutiveFailures > MAX_CONSECUTIVE_IO_ERRORS)
                    {
                        throw new IOException($"Lost communication during external flash programming at {(count * 100.0 / total):F0}%", ex);
                    }
                }
            }
            
            if (!chunkProgrammed)
            {
                throw new Exception($"Failed to program external flash chunk {count + 1}/{total} after {MAX_PROGRAM_RETRIES} retries");
            }
            
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
    /// Verifies external flash CRC.
    /// Mission Planner compatible: sends image size as part of the CRC command.
    /// </summary>
    public bool VerifyExternalFlashCrc(byte[] firmware)
    {
        if (ExtFlashSize <= 0)
        {
            return true;
        }

        Log("Verifying external flash CRC...");

        uint expectedCrc = CalculatePx4Crc(firmware, firmware.Length);
        
        // Mission Planner sends the image size as part of the CRC command
        byte[] sizeBytes = BitConverter.GetBytes(firmware.Length);
        Send(new byte[] { 
            (byte)ProtocolCode.EXTF_GET_CRC,
            sizeBytes[0], sizeBytes[1], sizeBytes[2], sizeBytes[3],
            (byte)ProtocolCode.EOC 
        });
        
        // CRC calculation can be slow, give it extra time with progress reporting
        var sw = Stopwatch.StartNew();
        
        while (sw.ElapsedMilliseconds < CRC_VERIFICATION_TIMEOUT)
        {
            if (_port != null && _port.BytesToRead >= 4)
            {
                ProgressEvent?.Invoke(100);
                break;
            }
            
            // Report progress during CRC calculation
            double progress = (sw.ElapsedMilliseconds / CRC_PROGRESS_DURATION_MS) * 100.0;
            if (progress < 100)
            {
                ProgressEvent?.Invoke(progress);
            }
            
            Thread.Sleep(50);
        }
        
        if (_port == null || _port.BytesToRead < 4)
        {
            throw new TimeoutException("Timeout waiting for external flash CRC");
        }
        
        uint reportedCrc = RecvUInt32();
        GetSync();

        Log($"Expected external CRC:  0x{expectedCrc:X8}");
        Log($"Reported external CRC:  0x{reportedCrc:X8}");

        return expectedCrc == reportedCrc;
    }

    /// <summary>
    /// Checks if firmware already matches what's on the board.
    /// Uses CRC comparison to avoid unnecessary re-flashing.
    /// Mission Planner compatible: handles IOException as "lost communication" case.
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
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Lost communication during CRC check");
            throw; // Re-throw IOException so caller can handle "lost communication" case
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(ex, "Timeout during CRC check");
            throw; // Re-throw TimeoutException so caller can handle timeout case
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CRC check failed, assuming different firmware");
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
    /// Mission Planner compatible implementation with proper error handling
    /// for "lost communication" and "same firmware" scenarios.
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
        // Mission Planner compatible: handles IOException and TimeoutException
        if (firmware.ImageSize > 0)
        {
            try
            {
                if (IsSameFirmware(firmware.Image, FlashSize))
                {
                    Log("Same firmware already installed. Skipping upload.");
                    // Don't reboot here - let the caller handle it
                    // This prevents losing the exception if reboot causes disconnect
                    throw new Exception("Same firmware already installed");
                }
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Lost communication during CRC check");
                throw new IOException("Lost communication with the board during firmware check.", ex);
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "Timeout during CRC check");
                throw new TimeoutException("Communication timeout during firmware check.", ex);
            }
            catch (Exception ex) when (ex.Message.Contains("Same firmware"))
            {
                throw; // Re-throw "same firmware" exception
            }
            catch (Exception ex)
            {
                // Other exceptions during CRC check are non-fatal, continue with upload
                _logger.LogDebug(ex, "CRC check failed, proceeding with upload");
            }
        }

        // Mission Planner compatible upload order:
        // 1. Erase internal flash FIRST - this invalidates the firmware to prevent
        //    a case where an unplug during external flashing causes bootloader mode
        //    on reboot because there's no valid internal flash, even if external was valid.
        // 2. Erase external flash (if present)
        // 3. Program external flash (if present)
        // 4. Verify external flash (if present)
        // 5. Program internal flash
        // 6. Verify internal flash
        // 7. Reboot
        
        // Step 1: Erase internal flash first
        if (firmware.ImageSize > 0)
        {
            Log("Erasing internal flash...");
            Erase();
        }
        
        // Step 2-4: Handle external flash if present (BEFORE internal programming)
        if (firmware.ExtFlashImageSize > 0 && ExtFlashSize > 0)
        {
            Log("Erasing external flash...");
            EraseExternalFlash(firmware.ExtFlashImageSize);

            Log("Programming external flash...");
            ProgramExternalFlash(firmware.ExtFlashImage);

            Log("Verifying external flash...");
            if (!VerifyExternalFlashCrc(firmware.ExtFlashImage))
            {
                throw new Exception("External flash CRC verification failed");
            }
        }
        
        // Step 5-6: Program and verify internal flash
        if (firmware.ImageSize > 0)
        {
            Log("Programming internal flash...");
            Program(firmware.Image);

            Log("Verifying internal flash...");
            if (!VerifyCrc(firmware.Image, FlashSize))
            {
                throw new Exception("CRC verification failed - Program CRC mismatch");
            }
        }

        Log("Upload complete. Rebooting...");
        Reboot();
    }

    #region Private Protocol Methods

    /// <summary>
    /// Waits for and validates INSYNC + OK response from bootloader.
    /// Mission Planner compatible: flush, wait for bytes, then read.
    /// </summary>
    private void GetSync()
    {
        _port?.BaseStream.Flush();
        
        // Wait for response to arrive (Mission Planner compatible)
        var deadline = DateTime.Now.AddMilliseconds(_port?.ReadTimeout ?? 2000);
        while (_port != null && _port.BytesToRead == 0)
        {
            if (DateTime.Now > deadline)
            {
                throw new TimeoutException("Timeout waiting for response from bootloader");
            }
            Thread.Yield();
        }
        
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

    /// <summary>
    /// Programs a single chunk using PROG_MULTI command.
    /// Mission Planner compatible: flushes buffer after send to ensure data is transmitted.
    /// </summary>
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
        
        // Flush to ensure data is actually transmitted before waiting for response
        _port?.BaseStream.Flush();
        
        GetSync();
    }

    /// <summary>
    /// Programs a single chunk to external flash.
    /// </summary>
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
        
        // Flush to ensure data is actually transmitted before waiting for response
        _port?.BaseStream.Flush();
        
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

    /// <summary>
    /// Receives specified number of bytes from the bootloader.
    /// Uses busy-wait loop with small delays for responsiveness.
    /// Mission Planner compatible timeout handling and early INVALID detection.
    /// </summary>
    private byte[] Recv(int count)
    {
        if (_port == null || !_port.IsOpen)
        {
            throw new InvalidOperationException("Port not open");
        }

        var buffer = new byte[count];
        int offset = 0;
        var sw = Stopwatch.StartNew();
        var timeout = _port.ReadTimeout;
        
        while (offset < count)
        {
            if (sw.ElapsedMilliseconds > timeout)
            {
                throw new TimeoutException($"Timeout waiting for {count} bytes (got {offset} after {sw.ElapsedMilliseconds}ms)");
            }

            try
            {
                if (_port.BytesToRead > 0)
                {
                    int toRead = Math.Min(_port.BytesToRead, count - offset);
                    int read = _port.Read(buffer, offset, toRead);
                    offset += read;
                    
                    // Mission Planner compatible: early INVALID detection
                    // If we get INSYNC + INVALID at the start of a multi-byte read, fail fast
                    if (count >= MIN_BYTES_FOR_INVALID_CHECK && offset >= INVALID_HEADER_SIZE && 
                        buffer[0] == (byte)ProtocolCode.INSYNC && 
                        buffer[1] == (byte)ProtocolCode.INVALID)
                    {
                        throw new Exception("Bootloader reports INVALID operation (Bad Request)");
                    }
                }
                else
                {
                    // Yield to allow USB data to arrive
                    Thread.Sleep(1);
                }
            }
            catch (IOException ex)
            {
                // USB disconnect during read
                throw new IOException($"Lost communication with board during receive (got {offset}/{count} bytes)", ex);
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
