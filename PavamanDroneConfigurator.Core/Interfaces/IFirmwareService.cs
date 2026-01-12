using PavamanDroneConfigurator.Core.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Service for firmware flashing and bootloader operations.
/// Provides Mission Planner-equivalent functionality for ArduPilot firmware management.
/// </summary>
public interface IFirmwareService
{
    #region Events
    
    /// <summary>
    /// Raised when firmware flash progress changes
    /// </summary>
    event EventHandler<FirmwareProgress>? ProgressChanged;
    
    /// <summary>
    /// Raised when a board is detected in bootloader mode
    /// </summary>
    event EventHandler<DetectedBoard>? BoardDetected;
    
    /// <summary>
    /// Raised when firmware flashing completes (success or failure)
    /// </summary>
    event EventHandler<FirmwareFlashResult>? FlashCompleted;
    
    /// <summary>
    /// Raised when log messages are generated during operations
    /// </summary>
    event EventHandler<string>? LogMessage;
    
    #endregion

    #region Properties
    
    /// <summary>
    /// Gets the currently detected board (if any)
    /// </summary>
    DetectedBoard? CurrentBoard { get; }
    
    /// <summary>
    /// Gets whether a firmware operation is currently in progress
    /// </summary>
    bool IsOperationInProgress { get; }
    
    /// <summary>
    /// Gets the current operation state
    /// </summary>
    FirmwareFlashState CurrentState { get; }
    
    /// <summary>
    /// Local firmware directory where bundled/manual firmware files can be placed.
    /// </summary>
    string LocalFirmwareDirectory { get; }
    
    #endregion

    #region Board Detection
    
    /// <summary>
    /// Scans for connected flight controllers and detects board type
    /// </summary>
    Task<DetectedBoard?> DetectBoardAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Gets list of supported board types
    /// </summary>
    IReadOnlyList<BoardInfo> GetSupportedBoards();
    
    /// <summary>
    /// Waits for a board to appear in bootloader mode
    /// </summary>
    Task<DetectedBoard?> WaitForBootloaderAsync(TimeSpan timeout, CancellationToken ct = default);
    
    #endregion

    #region Firmware Operations
    
    /// <summary>
    /// Gets available firmware types/vehicle categories
    /// </summary>
    IReadOnlyList<FirmwareType> GetAvailableFirmwareTypes();
    
    /// <summary>
    /// Fetches available firmware versions from ArduPilot firmware server
    /// </summary>
    Task<IReadOnlyList<FirmwareVersion>> GetAvailableFirmwareVersionsAsync(
        string vehicleType,
        string boardId,
        string? releaseType = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Downloads firmware from the ArduPilot firmware server
    /// </summary>
    Task<string?> DownloadFirmwareAsync(
        string downloadUrl, 
        IProgress<double>? progress = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Gets a local firmware file path for a given vehicle type id (if present)
    /// </summary>
    Task<string?> GetLocalFirmwarePathAsync(string vehicleTypeId, CancellationToken ct = default);
    
    /// <summary>
    /// Flashes firmware to the connected board (automatic mode)
    /// </summary>
    Task<FirmwareFlashResult> FlashFirmwareAsync(
        FirmwareType vehicleType,
        string? boardId = null,
        string? releaseType = null,
        CancellationToken ct = default);
    
    /// <summary>
    /// Flashes a local firmware file to the connected board
    /// </summary>
    Task<FirmwareFlashResult> FlashFirmwareFromFileAsync(
        string firmwareFilePath,
        CancellationToken ct = default);
    
    /// <summary>
    /// Flashes firmware from a specific version
    /// </summary>
    Task<FirmwareFlashResult> FlashFirmwareVersionAsync(
        FirmwareVersion version,
        CancellationToken ct = default);
    
    #endregion

    #region Bootloader Operations
    
    /// <summary>
    /// Updates the bootloader on the connected board
    /// </summary>
    Task<FirmwareFlashResult> UpdateBootloaderAsync(CancellationToken ct = default);
    
    /// <summary>
    /// Reboots the board into bootloader mode
    /// </summary>
    Task<bool> RebootToBootloaderAsync(string serialPort, CancellationToken ct = default);
    
    /// <summary>
    /// Reboots the board from bootloader back to normal mode
    /// </summary>
    Task<bool> RebootToFirmwareAsync(CancellationToken ct = default);
    
    #endregion

    #region Utility Methods
    
    /// <summary>
    /// Cancels any ongoing firmware operation
    /// </summary>
    void CancelOperation();
    
    /// <summary>
    /// Validates a firmware file
    /// </summary>
    Task<(bool IsValid, string Message)> ValidateFirmwareFileAsync(string filePath);
    
    /// <summary>
    /// Gets the firmware manifest from ArduPilot servers
    /// </summary>
    Task<FirmwareManifest?> GetFirmwareManifestAsync(CancellationToken ct = default);
    
    #endregion
}
