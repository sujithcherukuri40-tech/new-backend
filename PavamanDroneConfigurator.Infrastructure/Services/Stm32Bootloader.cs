using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Models;
using System;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// STM32 bootloader protocol implementation for programming ArduPilot flight controllers.
/// Supports both STM32 native bootloader and PX4/ChibiOS bootloader protocols.
/// </summary>
public sealed class Stm32Bootloader
{
    private readonly ILogger _logger;
    
    // Protocol constants
    private const byte STM32_SYNC = 0x7F;
    private const byte STM32_ACK = 0x79;
    private const byte STM32_NACK = 0x1F;
    
    // STM32 bootloader commands
    private const byte CMD_GET = 0x00;
    private const byte CMD_GET_VERSION = 0x01;
    private const byte CMD_GET_ID = 0x02;
    private const byte CMD_READ_MEMORY = 0x11;
    private const byte CMD_GO = 0x21;
    private const byte CMD_WRITE_MEMORY = 0x31;
    private const byte CMD_ERASE = 0x43;
    private const byte CMD_EXTENDED_ERASE = 0x44;
    
    // PX4/ChibiOS bootloader protocol
    private const byte PX4_PROTO_GET_SYNC = 0x21;
    private const byte PX4_PROTO_GET_DEVICE = 0x22;
    private const byte PX4_PROTO_CHIP_ERASE = 0x23;
    private const byte PX4_PROTO_PROG_MULTI = 0x27;
    private const byte PX4_PROTO_GET_CRC = 0x29;
    private const byte PX4_PROTO_BOOT = 0x30;
    private const byte PX4_PROTO_EOC = 0x20;
    private const byte PX4_PROTO_INSYNC = 0x12;
    private const byte PX4_PROTO_OK = 0x10;
    private const byte PX4_PROTO_INVALID = 0x13;
    private const byte PX4_PROTO_BAD_SILICON_REV = 0x14;
    
    // Flash programming settings - Mission Planner compatible
    private const int PROG_BLOCK_SIZE = 252; // PROG_MULTI_MAX from PX4 protocol
    private const int MAX_RETRIES = 3;
    private const int STABILIZATION_DELAY = 500; // USB stabilization delay
    private const int PROGRAM_TIMEOUT = 2000; // Timeout for program operations
    
    // Consecutive failure thresholds - consistent with Px4Uploader
    private const int MAX_CONSECUTIVE_TIMEOUTS = 5;  // Timeout failures before abort
    private const int MAX_CONSECUTIVE_IO_ERRORS = 3; // IO errors (USB disconnect) before abort
    
    private bool _usePx4Protocol = false;
    
    public Stm32Bootloader(ILogger logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Attempts to synchronize with the bootloader.
    /// Mission Planner compatible: multiple sync attempts with delays.
    /// </summary>
    public async Task<bool> TrySyncAsync(SerialPort port, CancellationToken ct = default)
    {
        _logger.LogDebug("Attempting bootloader sync on {Port}", port.PortName);
        
        // Clear buffers
        port.DiscardInBuffer();
        port.DiscardOutBuffer();
        
        // Try PX4/ChibiOS protocol first (most common for ArduPilot)
        if (await TryPx4SyncAsync(port, ct))
        {
            _usePx4Protocol = true;
            _logger.LogDebug("PX4 bootloader sync successful");
            return true;
        }
        
        // Try STM32 native bootloader
        if (await TryStm32SyncAsync(port, ct))
        {
            _usePx4Protocol = false;
            _logger.LogDebug("STM32 bootloader sync successful");
            return true;
        }
        
        _logger.LogWarning("Failed to sync with bootloader");
        return false;
    }
    
    private async Task<bool> TryPx4SyncAsync(SerialPort port, CancellationToken ct)
    {
        for (int attempt = 0; attempt < MAX_RETRIES; attempt++)
        {
            try
            {
                port.DiscardInBuffer();
                
                // Send sync sequence
                port.Write(new byte[] { PX4_PROTO_GET_SYNC, PX4_PROTO_EOC }, 0, 2);
                
                await Task.Delay(50, ct);
                
                // Read response
                if (port.BytesToRead >= 2)
                {
                    var response = new byte[2];
                    port.Read(response, 0, 2);
                    
                    if (response[0] == PX4_PROTO_INSYNC && response[1] == PX4_PROTO_OK)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "PX4 sync attempt {Attempt} failed", attempt + 1);
            }
            
            await Task.Delay(100, ct);
        }
        
        return false;
    }
    
    private async Task<bool> TryStm32SyncAsync(SerialPort port, CancellationToken ct)
    {
        for (int attempt = 0; attempt < MAX_RETRIES; attempt++)
        {
            try
            {
                port.DiscardInBuffer();
                
                // Send sync byte
                port.Write(new byte[] { STM32_SYNC }, 0, 1);
                
                await Task.Delay(50, ct);
                
                if (port.BytesToRead > 0)
                {
                    var response = (byte)port.ReadByte();
                    if (response == STM32_ACK)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "STM32 sync attempt {Attempt} failed", attempt + 1);
            }
            
            await Task.Delay(100, ct);
        }
        
        return false;
    }
    
    /// <summary>
    /// Gets the bootloader version string
    /// </summary>
    public async Task<string?> GetVersionAsync(SerialPort port, CancellationToken ct = default)
    {
        try
        {
            if (_usePx4Protocol)
            {
                return await GetPx4VersionAsync(port, ct);
            }
            else
            {
                return await GetStm32VersionAsync(port, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get bootloader version");
            return null;
        }
    }
    
    private async Task<string?> GetPx4VersionAsync(SerialPort port, CancellationToken ct)
    {
        // PX4 bootloader doesn't have a dedicated version command
        // but we can identify it from the device info
        return "PX4/ChibiOS Bootloader";
    }
    
    private async Task<string?> GetStm32VersionAsync(SerialPort port, CancellationToken ct)
    {
        try
        {
            // Send GET_VERSION command
            if (!await SendCommandAsync(port, CMD_GET_VERSION, ct))
            {
                return null;
            }
            
            // Read version byte
            if (port.BytesToRead >= 1)
            {
                var version = (byte)port.ReadByte();
                
                // Read ACK
                if (port.BytesToRead > 0)
                {
                    port.ReadByte();
                }
                
                return $"STM32 v{(version >> 4) & 0x0F}.{version & 0x0F}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read STM32 version");
        }
        
        return null;
    }
    
    /// <summary>
    /// Gets board information from the bootloader
    /// </summary>
    public async Task<BoardInfo?> GetBoardInfoAsync(SerialPort port, CancellationToken ct = default)
    {
        try
        {
            if (_usePx4Protocol)
            {
                return await GetPx4BoardInfoAsync(port, ct);
            }
            else
            {
                return await GetStm32BoardInfoAsync(port, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get board info");
            return null;
        }
    }
    
    private async Task<BoardInfo?> GetPx4BoardInfoAsync(SerialPort port, CancellationToken ct)
    {
        try
        {
            // Send GET_DEVICE command
            port.Write(new byte[] { PX4_PROTO_GET_DEVICE, PX4_PROTO_EOC }, 0, 2);
            
            await Task.Delay(100, ct);
            
            if (port.BytesToRead >= 6)
            {
                var response = new byte[port.BytesToRead];
                port.Read(response, 0, response.Length);
                
                // Parse device info
                // Response format: [board_id:2][board_rev:1][flash_size:3][INSYNC][OK]
                if (response.Length >= 8 && 
                    response[response.Length - 2] == PX4_PROTO_INSYNC &&
                    response[response.Length - 1] == PX4_PROTO_OK)
                {
                    int boardId = response[0] | (response[1] << 8);
                    int boardRev = response[2];
                    int flashSize = response[3] | (response[4] << 8) | (response[5] << 16);
                    
                    // Map board ID to known boards
                    var knownBoard = CommonBoards.SupportedBoards
                        .FirstOrDefault(b => b.VendorId == boardId || b.ProductId == boardId);
                    
                    return knownBoard ?? new BoardInfo
                    {
                        Id = $"Board_{boardId:X4}",
                        Name = $"Unknown Board (ID: 0x{boardId:X4})",
                        FlashSize = flashSize / 1024,
                        BootloaderProtocol = "px4"
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get PX4 board info");
        }
        
        return new BoardInfo
        {
            Id = "Unknown",
            Name = "PX4 Compatible Board",
            BootloaderProtocol = "px4"
        };
    }
    
    private async Task<BoardInfo?> GetStm32BoardInfoAsync(SerialPort port, CancellationToken ct)
    {
        try
        {
            // Send GET_ID command
            if (!await SendCommandAsync(port, CMD_GET_ID, ct))
            {
                return null;
            }
            
            await Task.Delay(50, ct);
            
            if (port.BytesToRead >= 3)
            {
                var numBytes = (byte)port.ReadByte();
                var pid = new byte[numBytes + 1];
                port.Read(pid, 0, pid.Length);
                
                // Read ACK
                if (port.BytesToRead > 0)
                {
                    port.ReadByte();
                }
                
                int productId = (pid[0] << 8) | pid[1];
                
                // Map to known STM32 chips
                string chipName = productId switch
                {
                    0x0413 => "STM32F405/407",
                    0x0419 => "STM32F427/429",
                    0x0423 => "STM32F401",
                    0x0431 => "STM32F411",
                    0x0451 => "STM32F7xx",
                    0x0450 => "STM32H743/745/750/753",
                    0x0480 => "STM32H7A3/H7B3/H7B0",
                    _ => $"STM32 (0x{productId:X4})"
                };
                
                return new BoardInfo
                {
                    Id = $"STM32_{productId:X4}",
                    Name = chipName,
                    BootloaderProtocol = "stm32"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get STM32 board info");
        }
        
        return null;
    }
    
    /// <summary>
    /// Erases the flash memory
    /// </summary>
    public async Task<bool> EraseFlashAsync(SerialPort port, CancellationToken ct = default)
    {
        _logger.LogDebug("Erasing flash memory...");
        
        try
        {
            if (_usePx4Protocol)
            {
                return await ErasePx4FlashAsync(port, ct);
            }
            else
            {
                return await EraseStm32FlashAsync(port, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flash erase failed");
            return false;
        }
    }
    
    private async Task<bool> ErasePx4FlashAsync(SerialPort port, CancellationToken ct)
    {
        // PX4 chip erase command
        port.Write(new byte[] { PX4_PROTO_CHIP_ERASE, PX4_PROTO_EOC }, 0, 2);
        
        // Erase can take a while - wait up to 30 seconds
        var timeout = DateTime.Now.AddSeconds(30);
        
        while (DateTime.Now < timeout && !ct.IsCancellationRequested)
        {
            if (port.BytesToRead >= 2)
            {
                var response = new byte[2];
                port.Read(response, 0, 2);
                
                if (response[0] == PX4_PROTO_INSYNC && response[1] == PX4_PROTO_OK)
                {
                    _logger.LogDebug("Flash erase complete");
                    return true;
                }
                else
                {
                    _logger.LogWarning("Unexpected erase response: 0x{0:X2} 0x{1:X2}", response[0], response[1]);
                    return false;
                }
            }
            
            await Task.Delay(100, ct);
        }
        
        _logger.LogWarning("Flash erase timeout");
        return false;
    }
    
    private async Task<bool> EraseStm32FlashAsync(SerialPort port, CancellationToken ct)
    {
        // Try extended erase first (newer bootloaders)
        if (await SendCommandAsync(port, CMD_EXTENDED_ERASE, ct))
        {
            // Mass erase: send 0xFFFF
            var eraseCmd = new byte[] { 0xFF, 0xFF, 0x00 };
            port.Write(eraseCmd, 0, eraseCmd.Length);
            
            // Wait for erase (up to 30 seconds)
            var timeout = DateTime.Now.AddSeconds(30);
            while (DateTime.Now < timeout && !ct.IsCancellationRequested)
            {
                if (port.BytesToRead > 0)
                {
                    var response = (byte)port.ReadByte();
                    if (response == STM32_ACK)
                    {
                        return true;
                    }
                    else if (response == STM32_NACK)
                    {
                        return false;
                    }
                }
                await Task.Delay(100, ct);
            }
        }
        
        // Fall back to standard erase
        if (await SendCommandAsync(port, CMD_ERASE, ct))
        {
            // Mass erase: send 0xFF
            var eraseCmd = new byte[] { 0xFF, 0x00 };
            port.Write(eraseCmd, 0, eraseCmd.Length);
            
            var timeout = DateTime.Now.AddSeconds(30);
            while (DateTime.Now < timeout && !ct.IsCancellationRequested)
            {
                if (port.BytesToRead > 0)
                {
                    var response = (byte)port.ReadByte();
                    if (response == STM32_ACK)
                    {
                        return true;
                    }
                }
                await Task.Delay(100, ct);
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Programs firmware data to flash
    /// </summary>
    public async Task<bool> ProgramFlashAsync(
        SerialPort port, 
        byte[] data, 
        Action<double, long, long>? progressCallback = null,
        CancellationToken ct = default)
    {
        _logger.LogDebug("Programming {Size} bytes to flash...", data.Length);
        
        try
        {
            if (_usePx4Protocol)
            {
                return await ProgramPx4FlashAsync(port, data, progressCallback, ct);
            }
            else
            {
                return await ProgramStm32FlashAsync(port, data, progressCallback, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flash programming failed");
            return false;
        }
    }
    
    /// <summary>
    /// Programs firmware using PX4 protocol with retry logic.
    /// Mission Planner compatible: handles timeout and retry for each chunk.
    /// </summary>
    private async Task<bool> ProgramPx4FlashAsync(
        SerialPort port, 
        byte[] data, 
        Action<double, long, long>? progressCallback,
        CancellationToken ct)
    {
        int offset = 0;
        int totalBytes = data.Length;
        int consecutiveFailures = 0;
        
        // Set extended timeout for programming
        var originalTimeout = port.ReadTimeout;
        port.ReadTimeout = PROGRAM_TIMEOUT;
        
        try
        {
            while (offset < totalBytes && !ct.IsCancellationRequested)
            {
                int chunkSize = Math.Min(PROG_BLOCK_SIZE, totalBytes - offset);
                bool chunkProgrammed = false;
                
                // Retry logic for each chunk - addresses the ~60% failure zone
                for (int chunkRetry = 0; chunkRetry < MAX_RETRIES && !chunkProgrammed; chunkRetry++)
                {
                    try
                    {
                        if (chunkRetry > 0)
                        {
                            _logger.LogDebug("Retrying chunk at offset {Offset} (attempt {Retry})", offset, chunkRetry + 1);
                            await Task.Delay(100, ct);
                            port.DiscardInBuffer();
                        }
                        
                        // Build PROG_MULTI command
                        var cmd = new byte[chunkSize + 3];
                        cmd[0] = PX4_PROTO_PROG_MULTI;
                        cmd[1] = (byte)chunkSize;
                        Array.Copy(data, offset, cmd, 2, chunkSize);
                        cmd[chunkSize + 2] = PX4_PROTO_EOC;
                        
                        // Send command
                        port.Write(cmd, 0, cmd.Length);
                        port.BaseStream.Flush();
                        
                        // Wait for ACK with timeout polling
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        bool acked = false;
                        
                        while (sw.ElapsedMilliseconds < PROGRAM_TIMEOUT && !acked)
                        {
                            if (port.BytesToRead >= 2)
                            {
                                var response = new byte[2];
                                port.Read(response, 0, 2);
                                
                                if (response[0] == PX4_PROTO_INSYNC && response[1] == PX4_PROTO_OK)
                                {
                                    acked = true;
                                    chunkProgrammed = true;
                                    consecutiveFailures = 0;
                                }
                                else if (response[1] == PX4_PROTO_INVALID)
                                {
                                    _logger.LogWarning("Invalid response during programming at offset {Offset}", offset);
                                    // Don't return false immediately - retry
                                    break;
                                }
                            }
                            else
                            {
                                await Task.Delay(5, ct);
                            }
                        }
                        
                        if (!acked && !chunkProgrammed)
                        {
                            consecutiveFailures++;
                            _logger.LogWarning("No ACK at offset {Offset} after {Elapsed}ms", offset, sw.ElapsedMilliseconds);
                        }
                    }
                    catch (IOException ex)
                    {
                        consecutiveFailures++;
                        _logger.LogWarning(ex, "IO error programming chunk at offset {Offset}", offset);
                        
                        if (consecutiveFailures > MAX_CONSECUTIVE_IO_ERRORS)
                        {
                            throw new IOException($"Lost communication during programming at {(offset * 100.0 / totalBytes):F0}%", ex);
                        }
                    }
                    catch (TimeoutException ex)
                    {
                        consecutiveFailures++;
                        _logger.LogWarning(ex, "Timeout programming chunk at offset {Offset}", offset);
                        
                        if (consecutiveFailures > MAX_CONSECUTIVE_TIMEOUTS)
                        {
                            throw new TimeoutException($"Repeated timeouts during programming at {(offset * 100.0 / totalBytes):F0}%", ex);
                        }
                    }
                }
                
                if (!chunkProgrammed)
                {
                    _logger.LogError("Failed to program chunk at offset {Offset} after {Retries} retries", offset, MAX_RETRIES);
                    return false;
                }
                
                offset += chunkSize;
                
                double percent = (double)offset / totalBytes * 100;
                progressCallback?.Invoke(percent, offset, totalBytes);
            }
            
            _logger.LogDebug("Flash programming complete");
            return true;
        }
        finally
        {
            port.ReadTimeout = originalTimeout;
        }
    }
    
    private async Task<bool> ProgramStm32FlashAsync(
        SerialPort port, 
        byte[] data, 
        Action<double, long, long>? progressCallback,
        CancellationToken ct)
    {
        // STM32 flash start address (typically 0x08000000 for most STM32)
        uint address = 0x08000000;
        int offset = 0;
        int totalBytes = data.Length;
        
        while (offset < totalBytes && !ct.IsCancellationRequested)
        {
            int chunkSize = Math.Min(PROG_BLOCK_SIZE, totalBytes - offset);
            
            // Ensure chunk size is multiple of 4 (word aligned)
            if (chunkSize % 4 != 0 && offset + chunkSize < totalBytes)
            {
                chunkSize = (chunkSize / 4) * 4;
            }
            
            // Send WRITE_MEMORY command
            if (!await SendCommandAsync(port, CMD_WRITE_MEMORY, ct))
            {
                _logger.LogWarning("WRITE_MEMORY command rejected at offset {Offset}", offset);
                return false;
            }
            
            // Send address with checksum
            var addrBytes = new byte[5];
            addrBytes[0] = (byte)((address >> 24) & 0xFF);
            addrBytes[1] = (byte)((address >> 16) & 0xFF);
            addrBytes[2] = (byte)((address >> 8) & 0xFF);
            addrBytes[3] = (byte)(address & 0xFF);
            addrBytes[4] = (byte)(addrBytes[0] ^ addrBytes[1] ^ addrBytes[2] ^ addrBytes[3]);
            
            port.Write(addrBytes, 0, 5);
            
            await Task.Delay(10, ct);
            
            if (!await WaitForAckAsync(port, ct))
            {
                _logger.LogWarning("Address rejected at offset {Offset}", offset);
                return false;
            }
            
            // Send data with length and checksum
            var dataCmd = new byte[chunkSize + 2];
            dataCmd[0] = (byte)(chunkSize - 1); // N-1 bytes
            Array.Copy(data, offset, dataCmd, 1, chunkSize);
            
            // Calculate checksum
            byte checksum = dataCmd[0];
            for (int i = 0; i < chunkSize; i++)
            {
                checksum ^= dataCmd[i + 1];
            }
            dataCmd[chunkSize + 1] = checksum;
            
            port.Write(dataCmd, 0, dataCmd.Length);
            
            if (!await WaitForAckAsync(port, ct, timeout: 1000))
            {
                _logger.LogWarning("Write failed at offset {Offset}", offset);
                return false;
            }
            
            address += (uint)chunkSize;
            offset += chunkSize;
            
            double percent = (double)offset / totalBytes * 100;
            progressCallback?.Invoke(percent, offset, totalBytes);
        }
        
        return true;
    }
    
    /// <summary>
    /// Reboots the board to run the firmware
    /// </summary>
    public async Task RebootAsync(SerialPort port, CancellationToken ct = default)
    {
        _logger.LogDebug("Rebooting board...");
        
        try
        {
            if (_usePx4Protocol)
            {
                // Send BOOT command
                port.Write(new byte[] { PX4_PROTO_BOOT, PX4_PROTO_EOC }, 0, 2);
            }
            else
            {
                // Send GO command to reset address
                if (await SendCommandAsync(port, CMD_GO, ct))
                {
                    // Jump to flash start
                    var addr = new byte[5];
                    addr[0] = 0x08;
                    addr[1] = 0x00;
                    addr[2] = 0x00;
                    addr[3] = 0x00;
                    addr[4] = 0x08; // checksum
                    
                    port.Write(addr, 0, 5);
                }
            }
            
            await Task.Delay(100, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Reboot command failed (may be expected if port closed)");
        }
    }
    
    #region Private Helpers
    
    private async Task<bool> SendCommandAsync(SerialPort port, byte command, CancellationToken ct)
    {
        var cmdBytes = new byte[] { command, (byte)~command };
        port.Write(cmdBytes, 0, 2);
        
        return await WaitForAckAsync(port, ct);
    }
    
    private async Task<bool> WaitForAckAsync(SerialPort port, CancellationToken ct, int timeout = 500)
    {
        var deadline = DateTime.Now.AddMilliseconds(timeout);
        
        while (DateTime.Now < deadline && !ct.IsCancellationRequested)
        {
            if (port.BytesToRead > 0)
            {
                var response = (byte)port.ReadByte();
                if (response == STM32_ACK)
                {
                    return true;
                }
                else if (response == STM32_NACK)
                {
                    return false;
                }
            }
            
            await Task.Delay(10, ct);
        }
        
        return false;
    }
    
    #endregion
}
