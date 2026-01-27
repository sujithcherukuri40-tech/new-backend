using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Production-ready firmware flashing and bootloader management service.
/// Implements Mission Planner-equivalent functionality for ArduPilot firmware.
/// Uses PX4Uploader for PX4/ChibiOS bootloader protocol (CubeOrangePlus and similar boards).
/// </summary>
public sealed class FirmwareService : IFirmwareService, IDisposable
{
    private readonly ILogger<FirmwareService> _logger;
    private readonly HttpClient _httpClient;
    private readonly IConnectionService? _connectionService;
    
    private CancellationTokenSource? _operationCts;
    private readonly object _stateLock = new();
    
    // ArduPilot firmware server URLs - Mission Planner compatible
    // Primary: JSON manifest (compressed)
    private const string FIRMWARE_MANIFEST_URL_GZ = "https://firmware.ardupilot.org/manifest.json.gz";
    private const string FIRMWARE_MANIFEST_URL = "https://firmware.ardupilot.org/manifest.json";
    
    // Fallback: XML firmware list (Mission Planner's original format)
    private static readonly string[] FIRMWARE_XML_URLS = new[]
    {
        "https://firmware.ardupilot.org/Tools/MissionPlanner/Firmware/firmware2.xml",
        "https://github.com/ArduPilot/binary/raw/master/Firmware/firmware2.xml"
    };
    
    private const string FIRMWARE_BASE_URL = "https://firmware.ardupilot.org";
    
    // Bootloader detection timeout
    private const int BOOTLOADER_DETECT_TIMEOUT_SECONDS = 30;
    
    // USB re-enumeration delay - CRITICAL: After reboot to bootloader, the FC will appear on a DIFFERENT port
    // Mission Planner uses 500ms-3s for this. We use 2500ms for safety.
    private const int USB_REENUMERATION_DELAY_MS = 2500;
    
    // Maximum number of firmware versions to log for debugging
    private const int MAX_LOGGED_FIRMWARE_VERSIONS = 3;
    
    // Manifest cache
    private FirmwareManifest? _cachedManifest;
    private DateTime _manifestCacheTime = DateTime.MinValue;
    private readonly TimeSpan _manifestCacheDuration = TimeSpan.FromMinutes(30);
    
    // Temp directory for firmware downloads
    private readonly string _firmwareCacheDir;
    private readonly string _localFirmwareDirectory;
    
    public event EventHandler<FirmwareProgress>? ProgressChanged;
    public event EventHandler<DetectedBoard>? BoardDetected;
    public event EventHandler<FirmwareFlashResult>? FlashCompleted;
    public event EventHandler<string>? LogMessage;
    
    public DetectedBoard? CurrentBoard { get; private set; }
    public bool IsOperationInProgress { get; private set; }
    public FirmwareFlashState CurrentState { get; private set; } = FirmwareFlashState.Idle;
    
    public string LocalFirmwareDirectory => _localFirmwareDirectory;

    public FirmwareService(ILogger<FirmwareService> logger, IConnectionService? connectionService = null)
    {
        _logger = logger;
        _connectionService = connectionService;
        
        // Configure HttpClient with proper headers for firmware server
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
        };
        _httpClient = new HttpClient(handler);
        _httpClient.Timeout = TimeSpan.FromMinutes(5);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "MissionPlanner/1.3.80"); // Use Mission Planner user agent for compatibility
        _httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        _httpClient.DefaultRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("deflate"));

        _firmwareCacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PavamanDroneConfigurator",
            "FirmwareCache");
        _localFirmwareDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PavamanDroneConfigurator",
            "FirmwareLocal");

        Directory.CreateDirectory(_firmwareCacheDir);
        Directory.CreateDirectory(_localFirmwareDirectory);
    }
    
    #region Board Detection
    
    public async Task<DetectedBoard?> DetectBoardAsync(CancellationToken ct = default)
    {
        Log("Scanning for connected flight controllers...");
        UpdateState(FirmwareFlashState.DetectingBoard, 0, "Scanning for boards...");
        
        try
        {
            // Get device list with USB info
            var devices = BoardDetect.GetConnectedDevices();
            var ports = SerialPort.GetPortNames();
            
            Log($"Found {ports.Length} serial ports: {string.Join(", ", ports)}");
            
            // Use parallel detection with short timeouts for speed
            var detectionTasks = new List<Task<DetectedBoard?>>();
            
            // First pass: try to find boards in bootloader mode using Px4Uploader (fast check)
            foreach (var device in devices)
            {
                if (ct.IsCancellationRequested) break;
                
                // Use short timeout for each port detection
                detectionTasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
                        
                        return TryDetectPx4BootloaderSync(device.PortName);
                    }
                    catch
                    {
                        return null;
                    }
                }, ct));
            }
            
            // Wait for first successful bootloader detection or all to complete
            while (detectionTasks.Count > 0)
            {
                var completedTask = await Task.WhenAny(detectionTasks);
                detectionTasks.Remove(completedTask);
                
                var result = await completedTask;
                if (result != null)
                {
                    CurrentBoard = result;
                    BoardDetected?.Invoke(this, result);
                    Log($"Detected board in bootloader: {result.BoardName} on {result.SerialPort}");
                    return result;
                }
            }
            
            // Second pass: Try USB VID/PID detection (fast, no serial communication)
            foreach (var device in devices)
            {
                if (ct.IsCancellationRequested) break;
                
                var boardType = BoardDetect.DetectBoardFromDevice(device);
                if (boardType != BoardDetect.Boards.none)
                {
                    var board = new DetectedBoard
                    {
                        BoardId = BoardDetect.GetPlatformName(boardType),
                        BoardName = BoardDetect.GetBoardName(boardType),
                        BoardIdNumeric = BoardDetect.GetBoardId(boardType),
                        SerialPort = device.PortName,
                        IsInBootloader = device.IsBootloader,
                        DetectedAt = DateTime.Now
                    };
                    
                    CurrentBoard = board;
                    BoardDetected?.Invoke(this, board);
                    Log($"Detected board from USB: {board.BoardName} on {device.PortName}");
                    return board;
                }
            }
            
            // Third pass: Quick MAVLink check (look for any data on port)
            foreach (var device in devices)
            {
                if (ct.IsCancellationRequested) break;
                
                try
                {
                    var mavlinkBoard = await TryQuickMavlinkCheckAsync(device.PortName, ct);
                    if (mavlinkBoard != null)
                    {
                        // Use USB detection for board type
                        var boardType = BoardDetect.DetectBoard(device.PortName, devices);
                        if (boardType != BoardDetect.Boards.none)
                        {
                            mavlinkBoard.BoardId = BoardDetect.GetPlatformName(boardType);
                            mavlinkBoard.BoardName = BoardDetect.GetBoardName(boardType);
                            mavlinkBoard.BoardIdNumeric = BoardDetect.GetBoardId(boardType);
                        }
                        
                        mavlinkBoard.SerialPort = device.PortName;
                        CurrentBoard = mavlinkBoard;
                        BoardDetected?.Invoke(this, mavlinkBoard);
                        Log($"Detected running board: {mavlinkBoard.BoardName} on {device.PortName}");
                        return mavlinkBoard;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to detect MAVLink on {Port}", device.PortName);
                }
            }
            
            Log("No compatible boards detected");
            UpdateState(FirmwareFlashState.Idle, 0, "No boards detected");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during board detection");
            UpdateState(FirmwareFlashState.Failed, 0, $"Detection failed: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// Quick MAVLink check - just look for any incoming data with very short timeout
    /// </summary>
    private async Task<DetectedBoard?> TryQuickMavlinkCheckAsync(string portName, CancellationToken ct)
    {
        try
        {
            using var port = new SerialPort(portName)
            {
                BaudRate = 115200,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                ReadTimeout = 500,  // Short timeout
                WriteTimeout = 500
            };
            
            port.Open();
            port.DiscardInBuffer();
            
            // Quick delay to collect any incoming data
            await Task.Delay(200, ct);
            
            if (port.BytesToRead > 0)
            {
                var buffer = new byte[Math.Min(port.BytesToRead, 256)];
                port.Read(buffer, 0, buffer.Length);
                
                // Check for MAVLink start bytes (0xFE for v1, 0xFD for v2)
                if (buffer.Any(b => b == 0xFE || b == 0xFD))
                {
                    port.Close();
                    return new DetectedBoard
                    {
                        BoardName = "ArduPilot Flight Controller",
                        IsInBootloader = false,
                        CurrentFirmware = "Running",
                        DetectedAt = DateTime.Now
                    };
                }
            }
            
            port.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Quick MAVLink check failed on {Port}", portName);
        }
        
        return null;
    }
    
    /// <summary>
    /// Attempts to detect a board in bootloader mode using PX4 protocol
    /// </summary>
    private async Task<DetectedBoard?> TryDetectPx4BootloaderAsync(string portName, CancellationToken ct)
    {
        // Delegate to sync version on background thread
        return await Task.Run(() => TryDetectPx4BootloaderSync(portName), ct);
    }
    
    /// <summary>
    /// Synchronous bootloader detection for use in Task.Run
    /// </summary>
    private DetectedBoard? TryDetectPx4BootloaderSync(string portName)
    {
        using var uploader = new Px4Uploader(_logger);
        
        try
        {
            uploader.Open(portName);
            uploader.Identify();
            
            // Map board type to known boards
            var boardId = uploader.BoardType;
            var platformName = BoardCompatibility.GetPlatformName(boardId);
            var knownBoard = CommonBoards.SupportedBoards
                .FirstOrDefault(b => b.BoardId == boardId);
            
            return new DetectedBoard
            {
                BoardId = platformName,
                BoardIdNumeric = boardId,
                BoardName = knownBoard?.Name ?? $"Board {boardId}",
                BootloaderVersion = $"Rev {uploader.BootloaderRevision}",
                FlashSize = uploader.FlashSize / 1024, // Convert to KB
                SerialPort = portName,
                IsInBootloader = true,
                DetectedAt = DateTime.Now
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            uploader.Close();
        }
    }
    
    private async Task<DetectedBoard?> TryMavlinkIdentificationAsync(string portName, CancellationToken ct)
    {
        try
        {
            using var port = new SerialPort(portName)
            {
                BaudRate = 115200,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                ReadTimeout = 2000,
                WriteTimeout = 2000
            };
            
            port.Open();
            port.DiscardInBuffer();
            port.DiscardOutBuffer();
            
            // Wait briefly for any incoming data
            await Task.Delay(500, ct);
            
            if (port.BytesToRead > 0)
            {
                var buffer = new byte[port.BytesToRead];
                port.Read(buffer, 0, buffer.Length);
                
                // Check for MAVLink start bytes (0xFE for v1, 0xFD for v2)
                if (buffer.Any(b => b == 0xFE || b == 0xFD))
                {
                    return new DetectedBoard
                    {
                        BoardName = "ArduPilot Flight Controller",
                        IsInBootloader = false,
                        CurrentFirmware = "Running",
                        DetectedAt = DateTime.Now
                    };
                }
            }
            
            port.Close();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MAVLink identification failed on {Port}", portName);
        }
        
        return null;
    }
    
    public IReadOnlyList<BoardInfo> GetSupportedBoards()
    {
        return CommonBoards.SupportedBoards.ToList().AsReadOnly();
    }
    
    /// <summary>
    /// Waits for a board to appear in bootloader mode.
    /// Mission Planner compatible: handles USB port re-enumeration after reboot.
    /// Uses 30-second deadline loop matching Mission Planner's UploadPX4() method.
    /// </summary>
    public async Task<DetectedBoard?> WaitForBootloaderAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        Log($"Waiting for bootloader (timeout: {timeout.TotalSeconds}s)...");
        UpdateState(FirmwareFlashState.WaitingForBootloader, 0, "Waiting for board in bootloader mode...");
        
        // Remember which ports existed before - used to detect USB re-enumeration
        var initialPorts = new HashSet<string>(SerialPort.GetPortNames());
        var checkedPorts = new HashSet<string>();
        Log($"Initial ports: {string.Join(", ", initialPorts)}");
        
        var stopwatch = Stopwatch.StartNew();
        int scanCount = 0;
        
        while (stopwatch.Elapsed < timeout && !ct.IsCancellationRequested)
        {
            scanCount++;
            
            try
            {
                // Get FRESH port list each iteration (critical for USB re-enumeration)
                var currentPorts = SerialPort.GetPortNames();
                
                // Check new ports first (most likely to be the bootloader after USB re-enumeration)
                // This is CRITICAL - the bootloader will appear on a NEW port!
                var newPorts = currentPorts.Where(p => !checkedPorts.Contains(p)).ToList();
                
                if (newPorts.Count > 0)
                {
                    Log($"New/unchecked ports detected: {string.Join(", ", newPorts)}");
                    
                    // Small delay to let port stabilize after USB re-enumeration
                    await Task.Delay(100, ct);
                    
                    // Try new ports first - these are most likely the bootloader
                    foreach (var port in newPorts)
                    {
                        if (ct.IsCancellationRequested) break;
                        checkedPorts.Add(port);
                        
                        try
                        {
                            _logger.LogDebug("Trying new port {Port} (scan {Scan})", port, scanCount);
                            
                            // Run bootloader detection on a background thread to avoid blocking UI
                            var board = await Task.Run(() => TryDetectPx4BootloaderSync(port), ct);
                            if (board != null)
                            {
                                CurrentBoard = board;
                                Log($"Bootloader detected on {board.SerialPort} (board type: {board.BoardIdNumeric})");
                                
                                // CRITICAL: Ensure we're using the NEW port for flashing!
                                Log($"Will use port {port} for flashing operations");
                                
                                // Mission Planner compatible: stabilization delay after detection
                                await Task.Delay(500, ct);
                                
                                UpdateState(FirmwareFlashState.WaitingForBootloader, 100, $"Bootloader detected: {board.BoardName}");
                                return board;
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogDebug(ex, "Failed to detect bootloader on {Port}", port);
                        }
                    }
                }
                
                // Also re-check known ports (might have just switched to bootloader mode)
                foreach (var port in currentPorts)
                {
                    if (ct.IsCancellationRequested) break;
                    if (newPorts.Contains(port)) continue; // Already checked above
                    
                    try
                    {
                        _logger.LogDebug("Re-checking port {Port} (scan {Scan})", port, scanCount);
                        
                        var board = await Task.Run(() => TryDetectPx4BootloaderSync(port), ct);
                        if (board != null)
                        {
                            CurrentBoard = board;
                            Log($"Bootloader detected on {board.SerialPort} (board type: {board.BoardIdNumeric})");
                            
                            // Mission Planner compatible: stabilization delay after detection
                            await Task.Delay(500, ct);
                            
                            UpdateState(FirmwareFlashState.WaitingForBootloader, 100, $"Bootloader detected: {board.BoardName}");
                            return board;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to detect bootloader on {Port}", port);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error during bootloader detection loop");
            }
            
            // Wait between scans - Mission Planner uses 200ms polling
            await Task.Delay(200, ct);
            
            var remaining = timeout - stopwatch.Elapsed;
            if (remaining.TotalSeconds > 0)
            {
                var progressPercent = (stopwatch.Elapsed.TotalSeconds / timeout.TotalSeconds) * 100;
                UpdateState(FirmwareFlashState.WaitingForBootloader, 
                    progressPercent,
                    $"Scanning for bootloader... ({remaining.TotalSeconds:F0}s remaining)");
            }
        }
        
        Log($"Timeout waiting for bootloader after {scanCount} scans");
        UpdateState(FirmwareFlashState.Idle, 0, "Timeout waiting for bootloader");
        return null;
    }
    
    #endregion
    
    #region Firmware Operations
    
    public IReadOnlyList<FirmwareType> GetAvailableFirmwareTypes()
    {
        return CommonBoards.AvailableVehicleTypes.ToList().AsReadOnly();
    }
    
    public async Task<IReadOnlyList<FirmwareVersion>> GetAvailableFirmwareVersionsAsync(
        string vehicleType, 
        string boardId, 
        string? releaseType = null,
        CancellationToken ct = default)
    {
        Log($"Fetching firmware versions for {vehicleType} on {boardId}... (release={releaseType ?? "OFFICIAL"})");
        
        try
        {
            var manifest = await GetFirmwareManifestAsync(ct);
            if (manifest == null)
            {
                Log("Failed to fetch firmware manifest");
                return Array.Empty<FirmwareVersion>();
            }
            
            Log($"Manifest has {manifest.Firmware.Count} entries");
            
            // Normalize vehicle type for matching
            // ArduPilot manifest uses "Copter", "Plane", "Rover", "Sub", "AntennaTracker"
            var normalizedVehicleType = NormalizeVehicleType(vehicleType);
            Log($"Normalized vehicle type: '{vehicleType}' -> '{normalizedVehicleType}'");
            
            // CRITICAL: Determine if this is a helicopter request
            // Helicopters use "Copter-heli" as normalized type, standard copters use "Copter"
            bool isHelicopter = normalizedVehicleType.Equals("Copter-heli", StringComparison.OrdinalIgnoreCase);
            
            // For helicopter requests, we need to match against "Copter" vehicle type but filter for heli platforms
            var manifestVehicleType = isHelicopter ? "Copter" : normalizedVehicleType;
            
            // Debug: Check what unique vehicle types exist in manifest
            var uniqueVehicleTypes = manifest.Firmware
                .Select(f => f.VehicleType)
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .Take(10)
                .ToList();
            Log($"Sample vehicletypes in manifest: {string.Join(", ", uniqueVehicleTypes)}");
            
            // Base filter on vehicle type - check multiple fields for better matching
            IEnumerable<FirmwareEntry> baseQuery = manifest.Firmware
                .Where(f => 
                    // Match on vehicletype field (case-insensitive)
                    (!string.IsNullOrEmpty(f.VehicleType) && f.VehicleType.Equals(manifestVehicleType, StringComparison.OrdinalIgnoreCase)) ||
                    // Also check URL path for vehicle type (e.g., /Copter/, /Plane/)
                    (!string.IsNullOrEmpty(f.Url) && f.Url.Contains($"/{manifestVehicleType}/", StringComparison.OrdinalIgnoreCase)));
            
            var vehicleMatchCount = baseQuery.Count();
            Log($"Found {vehicleMatchCount} entries matching vehicle type '{manifestVehicleType}'");

            // Apply release/format filters
            IEnumerable<FirmwareEntry> ApplyCommonFilters(IEnumerable<FirmwareEntry> query)
            {
                var filtered = query;

                // Filter by release type
                // Mission Planner convention: OFFICIAL = stable, BETA, DEV = latest/dev
                if (!string.IsNullOrWhiteSpace(releaseType))
                {
                    // Map common aliases
                    var normalizedRelease = releaseType.ToUpperInvariant() switch
                    {
                        "STABLE" => "OFFICIAL",  // Map STABLE to OFFICIAL
                        _ => releaseType.ToUpperInvariant()
                    };
                    
                    filtered = filtered.Where(f => 
                        f.ReleaseCategory.Equals(normalizedRelease, StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    // Default to OFFICIAL (stable) releases
                    filtered = filtered.Where(f =>
                        f.ReleaseCategory.Equals("OFFICIAL", StringComparison.OrdinalIgnoreCase));
                }

                // Only APJ and PX4 formats are flashable
                filtered = filtered.Where(f =>
                    !string.IsNullOrEmpty(f.Format) &&
                    (f.Format.Equals("apj", StringComparison.OrdinalIgnoreCase) || 
                     f.Format.Equals("px4", StringComparison.OrdinalIgnoreCase)));

                // CRITICAL FIX: Filter helicopter vs non-helicopter firmware
                // For standard copters (quadcopter, hexacopter, etc.), EXCLUDE heli firmware
                // For helicopters, ONLY include heli firmware
                if (isHelicopter)
                {
                    // Helicopter: only include platforms/URLs with "-heli"
                    filtered = filtered.Where(f =>
                        (!string.IsNullOrEmpty(f.Platform) && f.Platform.EndsWith("-heli", StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(f.Url) && (f.Url.Contains("-heli/", StringComparison.OrdinalIgnoreCase) || 
                                                          f.Url.Contains("arducopter-heli", StringComparison.OrdinalIgnoreCase))));
                }
                else if (manifestVehicleType.Equals("Copter", StringComparison.OrdinalIgnoreCase))
                {
                    // Standard Copter (quad, hexa, octa, etc.): EXCLUDE heli firmware
                    filtered = filtered.Where(f =>
                        // Exclude platforms ending with "-heli"
                        (string.IsNullOrEmpty(f.Platform) || !f.Platform.EndsWith("-heli", StringComparison.OrdinalIgnoreCase)) &&
                        // Exclude URLs containing heli paths
                        (string.IsNullOrEmpty(f.Url) || 
                         (!f.Url.Contains("-heli/", StringComparison.OrdinalIgnoreCase) && 
                          !f.Url.Contains("arducopter-heli", StringComparison.OrdinalIgnoreCase))));
                }

                return filtered;
            }

            // Board-specific query first (if boardId provided)
            IEnumerable<FirmwareEntry> boardScoped = baseQuery;
            if (!string.IsNullOrWhiteSpace(boardId))
            {
                // CRITICAL FIX (Issue 3): For helicopter firmware, the platform in manifest
                // includes "-heli" suffix (e.g., "CubeOrangePlus-heli")
                // When user selects helicopter with board "CubeOrangePlus", we need to match "CubeOrangePlus-heli"
                // Note: isHelicopter is already defined in outer scope
                var heliPlatform = isHelicopter ? $"{boardId}-heli" : null;
                
                // Debug: show sample platforms for this vehicle type
                var samplePlatforms = baseQuery
                    .Where(f => !string.IsNullOrEmpty(f.Format) && f.Format.Equals("apj", StringComparison.OrdinalIgnoreCase))
                    .Select(f => f.Platform)
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct()
                    .Take(15)
                    .ToList();
                Log($"Sample platforms for {manifestVehicleType}: {string.Join(", ", samplePlatforms)}");
                
                if (isHelicopter)
                {
                    Log($"Helicopter mode: looking for platform '{heliPlatform}' (NOT standard '{boardId}')");
                }
                
                // PRIORITY 1: Try exact platform match first (case-insensitive)
                // For helicopter, try both the heli platform and exact match
                var exactMatch = baseQuery.Where(f =>
                    !string.IsNullOrEmpty(f.Platform) &&
                    (f.Platform.Equals(boardId, StringComparison.OrdinalIgnoreCase) ||
                     (isHelicopter && f.Platform.Equals(heliPlatform, StringComparison.OrdinalIgnoreCase))));
                
                var exactMatchCount = exactMatch.Count();
                Log($"Found {exactMatchCount} exact matches for platform '{boardId}'" + 
                    (isHelicopter ? $" or '{heliPlatform}'" : ""));
                
                if (exactMatchCount > 0)
                {
                    // Use exact matches only
                    // For helicopter, prefer the -heli platform if available
                    if (isHelicopter)
                    {
                        var heliMatches = exactMatch.Where(f => 
                            f.Platform?.EndsWith("-heli", StringComparison.OrdinalIgnoreCase) == true);
                        if (heliMatches.Any())
                        {
                            boardScoped = heliMatches;
                            Log($"Using heli-specific platform matches");
                        }
                        else
                        {
                            boardScoped = exactMatch;
                        }
                    }
                    else
                    {
                        boardScoped = exactMatch;
                    }
                }
                else
                {
                    // PRIORITY 2: Try variant matches (e.g., "CubeOrange" matches "CubeOrange-bdshot")
                    // Only if no exact match exists
                    // For helicopter, specifically look for -heli suffix variants
                    // NOTE: The heli filtering in ApplyCommonFilters will also handle excluding heli variants
                    if (isHelicopter)
                    {
                        boardScoped = baseQuery.Where(f =>
                            !string.IsNullOrEmpty(f.Platform) &&
                            f.Platform.StartsWith(boardId, StringComparison.OrdinalIgnoreCase) &&
                            f.Platform.Contains("-heli", StringComparison.OrdinalIgnoreCase));
                    }
                    else
                    {
                        boardScoped = baseQuery.Where(f =>
                            !string.IsNullOrEmpty(f.Platform) &&
                            f.Platform.StartsWith(boardId, StringComparison.OrdinalIgnoreCase) &&
                            !f.Platform.Contains("-heli", StringComparison.OrdinalIgnoreCase)); // Exclude heli variants for standard copter
                    }
                        
                    var variantMatchCount = boardScoped.Count();
                    Log($"Found {variantMatchCount} variant matches for board '{boardId}'" +
                        (isHelicopter ? " (heli variants)" : " (non-heli variants)"));
                }
            }

            var query = ApplyCommonFilters(boardScoped);
            var filteredCount = query.Count();
            Log($"After release/format filters: {filteredCount} entries");

            // Fallback: try without board filter if no results
            if (!query.Any() && !string.IsNullOrWhiteSpace(boardId))
            {
                _logger.LogWarning("No firmware found for {Vehicle}/{Board}. Trying all boards.", vehicleType, boardId);
                query = ApplyCommonFilters(baseQuery);
                
                // Take only the first few unique platforms as suggestions
                var uniquePlatforms = query.Select(f => f.Platform).Where(p => !string.IsNullOrEmpty(p)).Distinct().Take(5).ToList();
                if (uniquePlatforms.Any())
                {
                    Log($"Available platforms: {string.Join(", ", uniquePlatforms)}");
                }
            }

            // Pre-calculate detected board ID to avoid repeated lookups in LINQ
            var detectedBoardId = !string.IsNullOrWhiteSpace(boardId) 
                ? BoardCompatibility.GetBoardId(boardId) 
                : 0;

            var versions = query
                .Select(f => new FirmwareVersion
                {
                    Version = f.MavFirmwareVersion ?? f.MavFirmwareVersionStr ?? "unknown",
                    ReleaseType = f.ReleaseCategory,
                    BoardType = f.Platform ?? "",
                    BoardId = f.BoardIdNumeric ?? 0,
                    DownloadUrl = f.Url ?? "",
                    GitHash = f.GitHash ?? "",
                    IsLatest = f.Latest,
                    Platform = f.Platform ?? "",
                    Format = f.Format ?? ""
                })
                .Where(v => !string.IsNullOrEmpty(v.DownloadUrl)) // Must have a download URL
                .Where(v => {
                    // CRITICAL VALIDATION: Prevent incompatible firmware from being selected
                    // Verify firmware board_id is compatible with detected board_id
                    if (detectedBoardId > 0 && v.BoardId > 0)
                    {
                        var isCompatible = BoardCompatibility.AreCompatible(detectedBoardId, v.BoardId);
                        if (!isCompatible)
                        {
                            _logger.LogDebug(
                                "Rejecting incompatible firmware: board_id {FwBoardId} (platform={FwPlatform}) " +
                                "is not compatible with detected board_id {DetectedBoardId} (platform={DetectedPlatform})",
                                v.BoardId, v.Platform, detectedBoardId, boardId);
                            return false;
                        }
                    }
                    return true;
                })
                // CRITICAL FIX: Additional URL-based filtering as final safety check
                // This ensures we never select heli firmware for standard copters
                .Where(v => {
                    if (isHelicopter)
                    {
                        // Helicopter: prefer URLs with heli
                        return v.DownloadUrl.Contains("-heli", StringComparison.OrdinalIgnoreCase) ||
                               v.DownloadUrl.Contains("arducopter-heli", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (manifestVehicleType.Equals("Copter", StringComparison.OrdinalIgnoreCase))
                    {
                        // Standard copter: MUST NOT have heli in URL
                        return !v.DownloadUrl.Contains("-heli/", StringComparison.OrdinalIgnoreCase) &&
                               !v.DownloadUrl.Contains("arducopter-heli", StringComparison.OrdinalIgnoreCase);
                    }
                    return true;
                })
                .OrderByDescending(v => v.IsLatest)
                .ThenByDescending(v => v.Version)
                // CRITICAL FIX: Prefer exact platform match over variants
                // This ensures CubeOrangePlus firmware is selected over CubeOrangePlus-bdshot
                .ThenBy(v => v.Platform.Equals(boardId, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(v => v.Platform.Length) // Shorter platform names first (base variants)
                .ToList();
            
            Log($"Found {versions.Count} compatible firmware versions after board validation");
            
            // Log the URLs for debugging
            if (versions.Any())
            {
                var firstFew = versions.Take(MAX_LOGGED_FIRMWARE_VERSIONS).ToList();
                foreach (var ver in firstFew)
                {
                    Log($"  - {ver.Platform} v{ver.Version} ({ver.ReleaseType}): {ver.DownloadUrl}");
                }
            }
            
            return versions.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching firmware versions");
            return Array.Empty<FirmwareVersion>();
        }
    }
    
    /// <summary>
    /// Normalizes vehicle type string to match ArduPilot manifest naming convention.
    /// </summary>
    private string NormalizeVehicleType(string vehicleType)
    {
        // Handle common variants and aliases
        var lower = vehicleType.ToLowerInvariant();
        
        // All copter variants use "Copter" firmware
        if (lower.Contains("copter") || lower == "quad" || lower == "hexa" || 
            lower == "octa" || lower == "tri" || lower == "y6" || lower == "deca" ||
            lower == "single" || lower == "coax")
        {
            return "Copter";
        }
        
        // Helicopter uses Copter-heli
        if (lower == "heli" || lower.Contains("helicopter"))
        {
            return "Copter-heli";
        }
        
        // Plane variants
        if (lower.Contains("plane") || lower.Contains("wing") || lower == "quadplane")
        {
            return "Plane";
        }
        
        // Rover/ground vehicles
        if (lower.Contains("rover") || lower.Contains("ground") || lower.Contains("boat"))
        {
            return "Rover";
        }
        
        // Submarine
        if (lower == "sub" || lower.Contains("submarine") || lower.Contains("rov"))
        {
            return "Sub";
        }
        
        // Antenna tracker
        if (lower.Contains("tracker") || lower.Contains("antenna"))
        {
            return "AntennaTracker";
        }
        
        // Return as-is for already normalized values
        return vehicleType;
    }
    
    public async Task<string?> GetLocalFirmwarePathAsync(string vehicleTypeId, CancellationToken ct = default)
    {
        try
        {
            if (!Directory.Exists(_localFirmwareDirectory))
            {
                Directory.CreateDirectory(_localFirmwareDirectory);
            }

            var patterns = new[] { "*.apj", "*.px4", "*.bin", ".hex" };
            var files = patterns.SelectMany(p => Directory.GetFiles(_localFirmwareDirectory, p)).ToList();
            var match = files.FirstOrDefault(f =>
                Path.GetFileName(f).Contains(vehicleTypeId, StringComparison.OrdinalIgnoreCase));

            // also try ArduPilot id keywords
            if (match == null)
            {
                var apIds = new[] { "copter", "plane", "rover", "sub", "tracker", "heli" };
                match = files.FirstOrDefault(f => apIds.Any(id => Path.GetFileName(f).Contains(id, StringComparison.OrdinalIgnoreCase))
                    && Path.GetFileName(f).Contains(vehicleTypeId.Split('-').First(), StringComparison.OrdinalIgnoreCase));
            }

            return match;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to locate local firmware for {Vehicle}", vehicleTypeId);
            return null;
        }
    }

    public async Task<FirmwareFlashResult> FlashFirmwareAsync(
        FirmwareType vehicleType,
        string? boardId = null,
        string? releaseType = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            IsOperationInProgress = true;
            _operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _operationCts.Token;
            
            Log($"Starting firmware flash for {vehicleType.Name}...");
            
            // Step 1: Close any existing MAVLink connection
            if (_connectionService?.IsConnected == true)
            {
                Log("Closing existing MAVLink connection...");
                await _connectionService.DisconnectAsync();
                await Task.Delay(500, token);
            }
            
            // Step 2: Detect board - ALWAYS detect, never default to fmuv2!
            Log("Detecting board...");
            var board = CurrentBoard ?? await DetectBoardAsync(token);
            
            // CRITICAL FIX: Never default to "fmuv2" - require explicit board detection
            // This prevents wrong firmware being flashed (e.g., fmuv2 firmware to CubeOrangePlus)
            if (board == null && string.IsNullOrEmpty(boardId))
            {
                Log("No board detected from USB. Waiting for board in bootloader mode...");
                UpdateState(FirmwareFlashState.WaitingForBootloader, 0, 
                    "No board detected. Please connect your flight controller or put it in bootloader mode.");
                
                // Try to get the board to enter bootloader
                var rebootSuccess = await AttemptRebootToBootloaderAsync(token);
                if (rebootSuccess)
                {
                    Log("Waiting for USB re-enumeration after reboot command...");
                    await Task.Delay(USB_REENUMERATION_DELAY_MS, token);
                }
                
                // Wait for bootloader with timeout
                board = await WaitForBootloaderAsync(TimeSpan.FromSeconds(60), token);
                
                if (board == null)
                {
                    return CreateFailResult(
                        "No board detected. Please connect a flight controller. " +
                        "If the board is connected, try holding the BOOT button while connecting.", 
                        sw.Elapsed);
                }
            }
            
            // Use bootloader-reported board ID as the authoritative source
            // This is CRITICAL for differentiating CubeOrange (140) from CubeOrangePlus (1063)
            string platformName;
            if (!string.IsNullOrEmpty(boardId))
            {
                // User explicitly specified a board ID
                platformName = boardId;
                Log($"Using user-specified platform: {platformName}");
            }
            else if (board != null && board.BoardIdNumeric > 0)
            {
                // Use the actual board ID from bootloader (authoritative source)
                platformName = BoardCompatibility.GetPlatformName(board.BoardIdNumeric);
                Log($"Using bootloader-reported board_type {board.BoardIdNumeric} -> platform: {platformName}");
            }
            else if (board != null && !string.IsNullOrEmpty(board.BoardId))
            {
                // Fallback to board's platform name from USB detection
                platformName = board.BoardId;
                Log($"Using USB-detected platform: {platformName}");
            }
            else
            {
                // This should not happen if we require board detection above
                return CreateFailResult(
                    "Could not determine board type. Please ensure the board is properly connected.", 
                    sw.Elapsed);
            }
            
            // Step 3: Get firmware
            var versions = await GetAvailableFirmwareVersionsAsync(vehicleType.ArduPilotId, platformName, releaseType, token);
            var latestVersion = versions.FirstOrDefault(v => v.IsLatest) ?? versions.FirstOrDefault();
            
            if (latestVersion == null)
            {
                return CreateFailResult($"No firmware found for {vehicleType.Name} on {platformName}", sw.Elapsed);
            }
            
            Log($"Selected firmware: {latestVersion.Version} for platform: {platformName}");
            
            // Step 4: Download firmware
            var firmwarePath = await DownloadFirmwareAsync(latestVersion.DownloadUrl, null, token);
            if (string.IsNullOrEmpty(firmwarePath))
            {
                return CreateFailResult("Failed to download firmware", sw.Elapsed);
            }
            
            // Step 5: Enter bootloader if needed
            if (board == null || !board.IsInBootloader)
            {
                Log("Rebooting to bootloader...");
                UpdateState(FirmwareFlashState.WaitingForBootloader, 0, "Rebooting to bootloader...");
                
                var existingPorts = new HashSet<string>(SerialPort.GetPortNames());
                var rebootSuccess = await AttemptRebootToBootloaderAsync(token);
                
                if (rebootSuccess)
                {
                    // CRITICAL: Wait for USB re-enumeration - port will change!
                    Log("Waiting for USB re-enumeration after reboot command...");
                    await Task.Delay(USB_REENUMERATION_DELAY_MS, token);
                }
                
                // Wait for bootloader with increased timeout (Mission Planner uses up to 60 seconds)
                // CRITICAL: The board will appear on a NEW port after USB re-enumeration!
                board = await WaitForBootloaderAsync(TimeSpan.FromSeconds(60), token);
                
                if (board == null)
                {
                    return CreateFailResult(
                        "Failed to enter bootloader. Please unplug the board and plug back in while holding the BOOT button.", 
                        sw.Elapsed);
                }
                
                // IMPORTANT: After getting board from bootloader, verify/update platformName
                // The bootloader board_type is the authoritative source
                if (board.BoardIdNumeric > 0 && string.IsNullOrEmpty(boardId))
                {
                    var bootloaderPlatform = BoardCompatibility.GetPlatformName(board.BoardIdNumeric);
                    if (!string.Equals(platformName, bootloaderPlatform, StringComparison.OrdinalIgnoreCase))
                    {
                        Log($"WARNING: Initial detection suggested '{platformName}' but bootloader reports board_type {board.BoardIdNumeric} -> '{bootloaderPlatform}'");
                        Log($"Using authoritative bootloader board_type: {bootloaderPlatform}");
                        
                        // Re-download firmware for the correct platform if needed
                        platformName = bootloaderPlatform;
                        versions = await GetAvailableFirmwareVersionsAsync(vehicleType.ArduPilotId, platformName, releaseType, token);
                        latestVersion = versions.FirstOrDefault(v => v.IsLatest) ?? versions.FirstOrDefault();
                        
                        if (latestVersion == null)
                        {
                            return CreateFailResult($"No firmware found for {vehicleType.Name} on {platformName}", sw.Elapsed);
                        }
                        
                        firmwarePath = await DownloadFirmwareAsync(latestVersion.DownloadUrl, null, token);
                        if (string.IsNullOrEmpty(firmwarePath))
                        {
                            return CreateFailResult("Failed to download firmware for correct platform", sw.Elapsed);
                        }
                    }
                }
            }
            
            // Stabilization delay (Mission Planner uses 500ms)
            Log("Stabilizing connection...");
            await Task.Delay(500, token);
            
            // Step 6: Flash
            var flashResult = await UploadPx4FirmwareAsync(board.SerialPort, firmwarePath, token);
            
            sw.Stop();
            flashResult.Duration = sw.Elapsed;
            flashResult.FirmwareVersion = latestVersion.Version;
            flashResult.BoardType = platformName;
            
            FlashCompleted?.Invoke(this, flashResult);
            return flashResult;
        }
        catch (OperationCanceledException)
        {
            return CreateFailResult("Operation cancelled", sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firmware flash failed");
            return CreateFailResult($"Flash failed: {ex.Message}", sw.Elapsed);
        }
        finally
        {
            IsOperationInProgress = false;
            _operationCts?.Dispose();
            _operationCts = null;
        }
    }
    
    /// <summary>
    /// Uploads firmware using PX4/ChibiOS bootloader protocol.
    /// Matches Mission Planner's UploadPX4() method flow.
    /// Includes proper error handling for "lost communication" and timeout scenarios.
    /// 
    /// CRITICAL FIX (Issue 2): All blocking serial I/O operations are wrapped in Task.Run()
    /// to prevent UI thread freezing. Progress events are marshaled back via IProgress<T>.
    /// </summary>
    private async Task<FirmwareFlashResult> UploadPx4FirmwareAsync(
        string portName, 
        string firmwarePath, 
        CancellationToken ct)
    {
        Log($"Opening PX4 bootloader on {portName}...");
        
        // CRITICAL FIX (Issue 2): Run all blocking serial I/O on background thread
        // to prevent UI thread freezing during firmware flash operations.
        // Progress updates are marshaled back to the caller via the ProgressEvent callback.
        return await Task.Run(() => ExecutePx4Upload(portName, firmwarePath, ct), ct);
    }
    
    /// <summary>
    /// Synchronous firmware upload execution - called from Task.Run() to avoid blocking UI thread.
    /// </summary>
    private FirmwareFlashResult ExecutePx4Upload(string portName, string firmwarePath, CancellationToken ct)
    {
        using var uploader = new Px4Uploader(_logger);
        
        try
        {
            // Parse firmware file
            var firmware = Px4Firmware.FromFile(firmwarePath);
            Log($"Firmware: board_id={firmware.BoardId}, size={firmware.ImageSize / 1024}KB");
            
            if (firmware.ExtFlashImageSize > 0)
            {
                Log($"External flash image: {firmware.ExtFlashImageSize / 1024}KB");
            }
            
            // Connect and identify - includes stabilization delay
            uploader.Open(portName);
            uploader.Identify();
            
            Log($"Bootloader: rev={uploader.BootloaderRevision}, board={uploader.BoardType}, flash={uploader.FlashSize / 1024}KB");
            
            // CRITICAL PRE-FLASH VALIDATION: Validate board compatibility with detailed logging
            if (!BoardCompatibility.AreCompatible(uploader.BoardType, firmware.BoardId))
            {
                var detectedPlatform = BoardCompatibility.GetPlatformName(uploader.BoardType);
                var firmwarePlatform = BoardCompatibility.GetPlatformName(firmware.BoardId);
                
                Log($"? BOARD ID MISMATCH DETECTED!");
                Log($"   Connected Hardware: board_id={uploader.BoardType} ({detectedPlatform})");
                Log($"   Firmware File:      board_id={firmware.BoardId} ({firmwarePlatform})");
                Log($"   This firmware is for {firmwarePlatform}, but your board is {detectedPlatform}.");
                
                // CRITICAL: Delete the incompatible cached firmware to prevent reuse
                try
                {
                    Log($"??? Deleting incompatible cached firmware: {firmwarePath}");
                    File.Delete(firmwarePath);
                    Log("? Incompatible firmware deleted. Please retry to download correct firmware.");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete incompatible cached firmware");
                }
                
                return CreateFailResult(
                    $"Firmware not suitable for this board.\n" +
                    $"Board ID: {uploader.BoardType} ({detectedPlatform})\n" +
                    $"Firmware expects: {firmware.BoardId} ({firmwarePlatform})\n\n" +
                    $"The cached firmware was for a different board. Please try flashing again - the correct firmware will be downloaded.", 
                    TimeSpan.Zero);
            }
            
            Log($"? Board ID validation passed: {uploader.BoardType} == {firmware.BoardId}");
            
            // Check if same firmware is already installed
            // Mission Planner compatible: handles IOException and TimeoutException
            try
            {
                if (uploader.IsSameFirmware(firmware.Image, uploader.FlashSize))
                {
                    Log("Same firmware already installed - skipping upload");
                    UpdateState(FirmwareFlashState.Completed, 100, "Same firmware already installed");
                    return new FirmwareFlashResult
                    {
                        Success = true,
                        Message = "Same firmware already installed - no changes made"
                    };
                }
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Lost communication during CRC check");
                return CreateFailResult("Lost communication with the board during firmware check. Check USB connection.", TimeSpan.Zero);
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "Timeout during CRC check");
                return CreateFailResult("Communication timeout during firmware check. Try reconnecting the board.", TimeSpan.Zero);
            }
            catch (Exception ex)
            {
                // Non-fatal error during CRC check, continue with flash
                _logger.LogDebug(ex, "CRC check failed, proceeding with flash");
            }
            
            // Wire up progress events
            uploader.ProgressEvent += (progress) =>
            {
                UpdateState(FirmwareFlashState.Programming, progress,
                    $"Programming... {progress:F0}%");
            };
            
            uploader.LogEvent += (msg) => Log(msg);
            
            // Erase flash
            UpdateState(FirmwareFlashState.ErasingFlash, 0, "Erasing flash memory...");
            Log("Erasing flash...");
            uploader.Erase();
            Log("Flash erased successfully");
            
            // Program external flash if present (BEFORE internal flash programming)
            // Mission Planner order: erase internal -> erase external -> program external -> verify external -> program internal -> verify internal
            if (firmware.ExtFlashImageSize > 0 && uploader.ExtFlashSize > 0)
            {
                Log("Erasing external flash...");
                uploader.EraseExternalFlash(firmware.ExtFlashImageSize);
                
                Log($"Programming external flash ({firmware.ExtFlashImageSize / 1024}KB)...");
                uploader.ProgramExternalFlash(firmware.ExtFlashImage);
                
                Log("Verifying external flash CRC...");
                if (!uploader.VerifyExternalFlashCrc(firmware.ExtFlashImage))
                {
                    return CreateFailResult("External flash verification failed", TimeSpan.Zero);
                }
            }
            
            // Program internal flash
            UpdateState(FirmwareFlashState.Programming, 0, "Programming firmware...");
            Log($"Programming firmware ({firmware.ImageSize / 1024}KB)...");
            uploader.Program(firmware.Image);
            
            // Verify CRC
            UpdateState(FirmwareFlashState.Verifying, 90, "Verifying firmware...");
            Log("Verifying firmware CRC...");
            if (!uploader.VerifyCrc(firmware.Image, uploader.FlashSize))
            {
                return CreateFailResult("Firmware verification failed - CRC mismatch", TimeSpan.Zero);
            }
            Log("Firmware verification passed");
            
            // Reboot
            UpdateState(FirmwareFlashState.Rebooting, 95, "Rebooting to firmware...");
            Log("Rebooting board...");
            uploader.Reboot();
            
            UpdateState(FirmwareFlashState.Completed, 100, "Firmware flashed successfully!");
            Log("Firmware flash completed successfully!");
            
            return new FirmwareFlashResult
            {
                Success = true,
                Message = "Firmware flashed successfully"
            };
        }
        catch (IOException ex)
        {
            // Mission Planner compatible: specific handling for "lost communication"
            _logger.LogError(ex, "Lost communication during PX4 upload");
            UpdateState(FirmwareFlashState.Failed, 0, "Lost communication with the board");
            return CreateFailResult(
                "Lost communication with the board. This often occurs during flash programming. " +
                "Check USB cable and connection, then try again.", 
                TimeSpan.Zero);
        }
        catch (TimeoutException ex)
        {
            _logger.LogError(ex, "Timeout during PX4 upload");
            UpdateState(FirmwareFlashState.Failed, 0, "Communication timeout");
            return CreateFailResult(
                "Communication timeout during flash operation. " +
                "The board may have disconnected. Try reconnecting and flashing again.", 
                TimeSpan.Zero);
        }
        catch (Exception ex) when (ex.Message.Contains("Same firmware"))
        {
            // Not really an error - same firmware already installed
            Log("Same firmware already installed");
            UpdateState(FirmwareFlashState.Completed, 100, "Same firmware already installed");
            return new FirmwareFlashResult
            {
                Success = true,
                Message = "Same firmware already installed - no changes made"
            };
        }
        catch (Exception ex) when (ex.Message.Contains("CRC verification failed"))
        {
            _logger.LogError(ex, "CRC verification failed");
            UpdateState(FirmwareFlashState.Failed, 0, "Program CRC verification failed");
            return CreateFailResult(
                "Program CRC verification failed. The firmware may not have been programmed correctly. " +
                "Try flashing again. If problem persists, check USB cable and try a different USB port.",
                TimeSpan.Zero);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PX4 upload failed");
            UpdateState(FirmwareFlashState.Failed, 0, $"Flash failed: {ex.Message}");
            return CreateFailResult($"Flash failed: {ex.Message}", TimeSpan.Zero);
        }
        finally
        {
            uploader.Close();
        }
    }
    
    public async Task<FirmwareFlashResult> FlashFirmwareFromFileAsync(
        string firmwareFilePath,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            IsOperationInProgress = true;
            _operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _operationCts.Token;
            
            Log($"Flashing custom firmware: {firmwareFilePath}");
            
            // Validate file
            var (isValid, message) = await ValidateFirmwareFileAsync(firmwareFilePath);
            if (!isValid)
            {
                return CreateFailResult($"Invalid firmware file: {message}", sw.Elapsed);
            }
            
            // Close any existing connection
            if (_connectionService?.IsConnected == true)
            {
                await _connectionService.DisconnectAsync();
            }
            
            // Detect or wait for board in bootloader
            var board = CurrentBoard;
            if (board == null || !board.IsInBootloader)
            {
                Log("Waiting for board in bootloader mode...");
                UpdateState(FirmwareFlashState.WaitingForBootloader, 0, 
                    "Please connect your board in bootloader mode (hold boot button while connecting)");
                
                board = await WaitForBootloaderAsync(TimeSpan.FromSeconds(60), token);
                if (board == null)
                {
                    return CreateFailResult("No board detected in bootloader mode", sw.Elapsed);
                }
            }
            
            // Flash the firmware
            var flashResult = await UploadPx4FirmwareAsync(board.SerialPort, firmwareFilePath, token);
            
            sw.Stop();
            flashResult.Duration = sw.Elapsed;
            
            FlashCompleted?.Invoke(this, flashResult);
            return flashResult;
        }
        catch (OperationCanceledException)
        {
            return CreateFailResult("Operation cancelled", sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Custom firmware flash failed");
            return CreateFailResult($"Flash failed: {ex.Message}", sw.Elapsed);
        }
        finally
        {
            IsOperationInProgress = false;
            _operationCts?.Dispose();
            _operationCts = null;
        }
    }
    
    public async Task<FirmwareFlashResult> FlashFirmwareVersionAsync(
        FirmwareVersion version,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            IsOperationInProgress = true;
            _operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _operationCts.Token;
            
            Log($"Flashing firmware version: {version.Version}");
            
            // Download firmware
            var firmwarePath = await DownloadFirmwareAsync(version.DownloadUrl, null, token);
            if (string.IsNullOrEmpty(firmwarePath))
            {
                return CreateFailResult("Failed to download firmware", sw.Elapsed);
            }
            
            // Wait for bootloader
            var board = CurrentBoard;
            if (board == null || !board.IsInBootloader)
            {
                Log("Waiting for board in bootloader mode...");
                board = await WaitForBootloaderAsync(TimeSpan.FromSeconds(60), token);
                if (board == null)
                {
                    return CreateFailResult("No board detected in bootloader mode", sw.Elapsed);
                }
            }
            
            // Flash using PX4 uploader
            var result = await UploadPx4FirmwareAsync(board.SerialPort, firmwarePath, token);
            result.FirmwareVersion = version.Version;
            result.BoardType = version.BoardType;
            result.Duration = sw.Elapsed;
            
            FlashCompleted?.Invoke(this, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Version flash failed");
            return CreateFailResult($"Flash failed: {ex.Message}", sw.Elapsed);
        }
        finally
        {
            IsOperationInProgress = false;
            _operationCts?.Dispose();
            _operationCts = null;
        }
    }
    
    public async Task<string?> DownloadFirmwareAsync(
        string downloadUrl,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        Log($"Downloading firmware from: {downloadUrl}");
        UpdateState(FirmwareFlashState.DownloadingFirmware, 0, "Starting download...");

        try
        {
            // CRITICAL FIX: Create unique cache filename based on URL path to prevent board_id conflicts
            // Example: Copter_stable-4.6.3_CubeOrangePlus_arducopter.apj
            var uri = new Uri(downloadUrl);
            var pathParts = uri.LocalPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var uniqueCacheName = string.Join("_", pathParts.TakeLast(4)); // e.g., Copter_stable-4.6.3_CubeOrangePlus_arducopter.apj
            
            // Sanitize filename
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                uniqueCacheName = uniqueCacheName.Replace(c, '_');
            }
            
            var localPath = Path.Combine(_firmwareCacheDir, uniqueCacheName);

            // Check if we already have this specific firmware cached
            if (File.Exists(localPath))
            {
                // CRITICAL: Validate cached firmware board_id before using it
                try
                {
                    var cachedFirmware = Px4Firmware.FromFile(localPath);
                    Log($"Cached firmware found: {localPath} (board_id={cachedFirmware.BoardId})");
                    
                    // Extract expected platform from URL to validate
                    var expectedPlatform = pathParts.Length >= 2 ? pathParts[^2] : ""; // e.g., "CubeOrangePlus"
                    var expectedBoardId = BoardCompatibility.GetBoardId(expectedPlatform);
                    
                    if (expectedBoardId > 0 && !BoardCompatibility.AreCompatible(expectedBoardId, cachedFirmware.BoardId))
                    {
                        Log($"?? Cached firmware board_id mismatch! Expected: {expectedBoardId} ({expectedPlatform}), Got: {cachedFirmware.BoardId}");
                        Log($"Deleting invalid cached firmware and re-downloading...");
                        File.Delete(localPath);
                    }
                    else
                    {
                        Log($"? Using validated cached firmware: {localPath} (board_id={cachedFirmware.BoardId})");
                        return localPath;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to validate cached firmware, re-downloading");
                    try { File.Delete(localPath); } catch { }
                }
            }

            // Also clean up any old-style generic cached files that could cause confusion
            CleanupGenericCachedFirmware();

            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;

            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalBytesRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                totalBytesRead += bytesRead;

                if (totalBytes > 0)
                {
                    var percent = (double)totalBytesRead / totalBytes * 100;
                    progress?.Report(percent);
                    UpdateState(FirmwareFlashState.DownloadingFirmware, percent,
                        $"Downloading... {totalBytesRead / 1024}KB / {totalBytes / 1024}KB");
                }
            }
            
            fileStream.Close();

            // Validate downloaded firmware
            try
            {
                var firmware = Px4Firmware.FromFile(localPath);
                Log($"? Downloaded firmware: {localPath} (board_id={firmware.BoardId}, size={firmware.ImageSize / 1024}KB)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Downloaded firmware validation failed");
                try { File.Delete(localPath); } catch { }
                return null;
            }

            Log($"Download complete: {localPath} ({totalBytesRead / 1024}KB)");
            return localPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firmware download failed");
            UpdateState(FirmwareFlashState.Failed, 0, $"Download failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Cleans up old-style generic cached firmware files (e.g., "arducopter.apj")
    /// that could cause board_id conflicts.
    /// </summary>
    private void CleanupGenericCachedFirmware()
    {
        try
        {
            var genericNames = new[] { "arducopter.apj", "arduplane.apj", "ardurover.apj", "ardusub.apj" };
            foreach (var name in genericNames)
            {
                var path = Path.Combine(_firmwareCacheDir, name);
                if (File.Exists(path))
                {
                    Log($"??? Removing generic cached firmware: {path}");
                    File.Delete(path);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup generic cached firmware");
        }
    }
    
    /// <summary>
    /// Gets the board-specific cache path from a firmware URL.
    /// 
    /// CRITICAL FIX (Issue 4): This prevents the bug where arducopter.apj for CubeOrange
    /// could be incorrectly used for CubeOrangePlus (they have different board_ids 140 vs 1063).
    /// 
    /// URL format: https://firmware.ardupilot.org/{VehicleType}/{Version}/{Platform}/{filename}
    /// Cache path: {cacheRoot}/{VehicleType}/{Version}/{Platform}/{filename}
    /// 
    /// Example:
    ///   URL: https://firmware.ardupilot.org/Copter/stable-4.6.3/CubeOrangePlus/arducopter.apj
    ///   Cache: FirmwareCache/Copter/stable-4.6.3/CubeOrangePlus/arducopter.apj
    /// </summary>
    private string GetBoardSpecificCachePath(string downloadUrl)
    {
        try
        {
            var uri = new Uri(downloadUrl);
            var pathSegments = uri.LocalPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            
            // Expected segments: [VehicleType, Version, Platform, Filename]
            // e.g., ["Copter", "stable-4.6.3", "CubeOrangePlus", "arducopter.apj"]
            if (pathSegments.Length >= 4)
            {
                // Use the last 4 segments for cache path structure
                var relevantPath = string.Join(Path.DirectorySeparatorChar.ToString(), 
                    pathSegments.Skip(pathSegments.Length - 4));
                return Path.Combine(_firmwareCacheDir, relevantPath);
            }
            else if (pathSegments.Length >= 2)
            {
                // Fallback: use last 2 segments (platform/filename)
                var relevantPath = string.Join(Path.DirectorySeparatorChar.ToString(), 
                    pathSegments.Skip(pathSegments.Length - 2));
                return Path.Combine(_firmwareCacheDir, relevantPath);
            }
            
            // Fallback to just filename (legacy behavior)
            return Path.Combine(_firmwareCacheDir, Path.GetFileName(uri.LocalPath));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse download URL for cache path, using filename only");
            return Path.Combine(_firmwareCacheDir, Path.GetFileName(new Uri(downloadUrl).LocalPath));
        }
    }
    
    #endregion
    
    #region Bootloader Operations
    
    /// <summary>
    /// Updates the bootloader on the connected board.
    /// Note: Bootloader update is a specialized operation that requires specific bootloader firmware.
    /// This sends the MAV_CMD_FLASH_BOOTLOADER command via MAVLink when connected.
    /// </summary>
    public async Task<FirmwareFlashResult> UpdateBootloaderAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        
        try
        {
            IsOperationInProgress = true;
            _operationCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var token = _operationCts.Token;
            
            Log("Starting bootloader update...");
            UpdateState(FirmwareFlashState.Programming, 0, "Updating bootloader...");
            
            // Bootloader update requires an active MAVLink connection
            if (_connectionService?.IsConnected != true)
            {
                return CreateFailResult(
                    "Bootloader update requires an active MAVLink connection to the flight controller.", 
                    sw.Elapsed);
            }
            
            // Send MAV_CMD_FLASH_BOOTLOADER command
            // Magic value 290876 confirms the operation (matches Mission Planner)
            Log("Sending bootloader update command...");
            _connectionService.SendFlashBootloaderCommand(290876);
            
            // Wait for the operation to complete
            // Bootloader update can take several seconds
            await Task.Delay(5000, token);
            
            Log("Bootloader update command sent. Board will reboot automatically.");
            UpdateState(FirmwareFlashState.Completed, 100, "Bootloader update initiated");
            
            sw.Stop();
            
            var result = new FirmwareFlashResult
            {
                Success = true,
                Message = "Bootloader update command sent successfully. Board will update and reboot.",
                Duration = sw.Elapsed
            };
            
            FlashCompleted?.Invoke(this, result);
            return result;
        }
        catch (NotSupportedException)
        {
            return CreateFailResult(
                "Bootloader update is not supported via the current connection type.", 
                sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            return CreateFailResult("Operation cancelled", sw.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bootloader update failed");
            return CreateFailResult($"Bootloader update failed: {ex.Message}", sw.Elapsed);
        }
        finally
        {
            IsOperationInProgress = false;
            _operationCts?.Dispose();
            _operationCts = null;
        }
    }
    
    /// <summary>
    /// Attempts to reboot the FC into bootloader mode - Mission Planner equivalent.
    /// This follows Mission Planner's AttemptRebootToBootloader() flow:
    /// 1. Check if any port is already in bootloader mode (parallel check like Mission Planner)
    /// 2. Try MAVLink reboot command on all ports with ArduPilot firmware
    /// 3. Wait for USB re-enumeration (CRITICAL: USB port will change!)
    /// </summary>
    public async Task<bool> AttemptRebootToBootloaderAsync(CancellationToken ct = default)
    {
        Log("Attempting to reboot flight controller to bootloader...");
        
        var allPorts = SerialPort.GetPortNames();
        Log($"Found {allPorts.Length} serial ports: {string.Join(", ", allPorts)}");
        
        // Step 1: Check if ALREADY in bootloader mode on any port (parallel check like Mission Planner)
        // Mission Planner uses Parallel.ForEach to check all ports simultaneously
        var bootloaderCheckTasks = allPorts.Select(port => Task.Run(() =>
        {
            try
            {
                _logger.LogDebug("Checking if {Port} is already in bootloader mode...", port);
                var board = TryDetectPx4BootloaderSync(port);
                return (port, board, success: board != null);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Port {Port} is not in bootloader", port);
                return (port, board: (DetectedBoard?)null, success: false);
            }
        }, ct)).ToList();
        
        try
        {
            var results = await Task.WhenAll(bootloaderCheckTasks);
            var bootloaderResult = results.FirstOrDefault(r => r.success);
            if (bootloaderResult.success && bootloaderResult.board != null)
            {
                Log($"Board already in bootloader mode on {bootloaderResult.port}");
                CurrentBoard = bootloaderResult.board;
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Parallel bootloader check failed");
        }
        
        // Step 2: Try to use connection service to reboot if connected (like MainV2.comPort)
        // Mission Planner uses the existing MAVLink connection with proper heartbeat check
        if (_connectionService?.IsConnected == true)
        {
            try
            {
                Log("Checking for heartbeat before reboot...");
                
                // Use proper MAVLink command - param1=3 for bootloader mode
                Log("Sending MAVLink reboot-to-bootloader command via connection service...");
                _connectionService.SendPreflightReboot(3, 0); // param1=3 for bootloader
                
                // MUST close connection AFTER sending reboot - USB will disconnect!
                await Task.Delay(200, ct); // Brief delay to ensure command is sent
                await _connectionService.DisconnectAsync();
                Log("MAVLink reboot command sent, connection closed");
                
                // CRITICAL: Wait for USB re-enumeration
                // The FC will appear on a DIFFERENT port after bootloader reboot!
                Log("Waiting for USB re-enumeration...");
                await Task.Delay(USB_REENUMERATION_DELAY_MS, ct);
                
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send reboot via connection service");
            }
        }
        
        // Step 3: Try MAVLink reboot command directly on each port (with proper heartbeat check)
        foreach (var port in allPorts)
        {
            if (ct.IsCancellationRequested) break;
            
            try
            {
                Log($"Trying MAVLink reboot to bootloader on {port}...");
                var success = await TrySendMavlinkRebootToBootloaderAsync(port, ct);
                if (success)
                {
                    Log($"Reboot command sent successfully on {port}");
                    
                    // CRITICAL: Wait for USB re-enumeration - port will change!
                    Log("Waiting for USB re-enumeration...");
                    await Task.Delay(USB_REENUMERATION_DELAY_MS, ct);
                    
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to send reboot on {Port}", port);
            }
        }
        
        Log("Could not send reboot command to any port");
        return false;
    }
    
    /// <summary>
    /// Tries to send MAVLink reboot-to-bootloader command on a specific port.
    /// Opens the port, checks for MAVLink heartbeat, sends reboot command.
    /// </summary>
    private async Task<bool> TrySendMavlinkRebootToBootloaderAsync(string portName, CancellationToken ct)
    {
        SerialPort? port = null;
        try
        {
            port = new SerialPort(portName)
            {
                BaudRate = 115200,
                DataBits = 8,
                Parity = Parity.None,
                StopBits = StopBits.One,
                ReadTimeout = 1000,
                WriteTimeout = 1000,
                DtrEnable = true,
                RtsEnable = true
            };
            
            port.Open();
            port.DiscardInBuffer();
            
            // Wait for any incoming data (heartbeat)
            await Task.Delay(300, ct);
            
            // Check if we see MAVLink data
            if (port.BytesToRead > 0)
            {
                var buffer = new byte[Math.Min(port.BytesToRead, 512)];
                port.Read(buffer, 0, buffer.Length);
                
                // Look for MAVLink start bytes
                bool hasMavlink = buffer.Any(b => b == 0xFE || b == 0xFD);
                
                if (hasMavlink)
                {
                    Log($"MAVLink detected on {portName}, sending reboot command...");
                    
                    // Send the reboot command
                    var rebootCmd = BuildMavlinkRebootToBootloaderCommandV2();
                    port.Write(rebootCmd, 0, rebootCmd.Length);
                    
                    // Also try v1 format for compatibility
                    await Task.Delay(50, ct);
                    var rebootCmdV1 = BuildMavlinkRebootToBootloaderCommandV1();
                    port.Write(rebootCmdV1, 0, rebootCmdV1.Length);
                    
                    // Give time for command to be processed
                    await Task.Delay(200, ct);
                    
                    // Toggle DTR/RTS as backup hardware reset
                    port.DtrEnable = false;
                    port.RtsEnable = false;
                    await Task.Delay(100, ct);
                    port.DtrEnable = true;
                    port.RtsEnable = true;
                    
                    port.Close();
                    return true;
                }
            }
            
            port.Close();
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error on port {Port}", portName);
            try { port?.Close(); } catch { }
            return false;
        }
    }
    
    public async Task<bool> RebootToBootloaderAsync(string serialPort, CancellationToken ct = default)
    {
        Log($"Attempting to reboot {serialPort} to bootloader...");
        
        // Use the comprehensive approach
        return await AttemptRebootToBootloaderAsync(ct);
    }
    
    /// <summary>
    /// Builds MAVLink v1 COMMAND_LONG message for reboot to bootloader.
    /// MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN (246) with param1=3 (stay in bootloader)
    /// </summary>
    private byte[] BuildMavlinkRebootToBootloaderCommandV1()
    {
        // MAVLink v1 COMMAND_LONG message structure:
        // Header: STX(1) + LEN(1) + SEQ(1) + SYSID(1) + COMPID(1) + MSGID(1) = 6 bytes
        // Payload: 33 bytes for COMMAND_LONG
        // Checksum: 2 bytes
        // Total: 41 bytes
        
        var payload = new byte[33];
        
        // param1 = 3.0f (reboot to bootloader) - bytes 0-3
        BitConverter.GetBytes(3.0f).CopyTo(payload, 0);
        // param2 = 0.0f - bytes 4-7
        BitConverter.GetBytes(0.0f).CopyTo(payload, 4);
        // param3 = 0.0f - bytes 8-11
        BitConverter.GetBytes(0.0f).CopyTo(payload, 8);
        // param4 = 0.0f - bytes 12-15
        BitConverter.GetBytes(0.0f).CopyTo(payload, 12);
        // param5 = 0.0f - bytes 16-19
        BitConverter.GetBytes(0.0f).CopyTo(payload, 16);
        // param6 = 0.0f - bytes 20-23
        BitConverter.GetBytes(0.0f).CopyTo(payload, 20);
        // param7 = 0.0f - bytes 24-27
        BitConverter.GetBytes(0.0f).CopyTo(payload, 24);
        // command = 246 (MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN) - bytes 28-29
        BitConverter.GetBytes((ushort)246).CopyTo(payload, 28);
        // target_system = 1 - byte 30
        payload[30] = 1;
        // target_component = 1 - byte 31
        payload[31] = 1;
        // confirmation = 0 - byte 32
        payload[32] = 0;
        
        // Build the full message
        var message = new byte[41];
        message[0] = 0xFE;  // MAVLink v1 start
        message[1] = 33;    // Payload length
        message[2] = 0;     // Sequence
        message[3] = 255;   // System ID (GCS)
        message[4] = 190;   // Component ID (GCS)
        message[5] = 76;    // Message ID (COMMAND_LONG)
        
        Array.Copy(payload, 0, message, 6, 33);
        
        // Calculate CRC (X.25)
        ushort crc = CalculateX25Crc(message, 1, 38); // length + payload
        // Add CRC_EXTRA for COMMAND_LONG (152)
        crc = CrcAccumulate(152, crc);
        
        message[39] = (byte)(crc & 0xFF);
        message[40] = (byte)((crc >> 8) & 0xFF);
        
        return message;
    }
    
    /// <summary>
    /// Builds MAVLink v2 COMMAND_LONG message for reboot to bootloader.
    /// </summary>
    private byte[] BuildMavlinkRebootToBootloaderCommandV2()
    {
        // MAVLink v2 COMMAND_LONG message structure:
        // Header: STX(1) + LEN(1) + INCOMPAT(1) + COMPAT(1) + SEQ(1) + SYSID(1) + COMPID(1) + MSGID(3) = 10 bytes
        // Payload: 33 bytes for COMMAND_LONG
        // Checksum: 2 bytes
        // Total: 45 bytes
        
        var payload = new byte[33];
        
        // param1 = 3.0f (reboot to bootloader)
        BitConverter.GetBytes(3.0f).CopyTo(payload, 0);
        // param2-7 = 0.0f
        BitConverter.GetBytes(0.0f).CopyTo(payload, 4);
        BitConverter.GetBytes(0.0f).CopyTo(payload, 8);
        BitConverter.GetBytes(0.0f).CopyTo(payload, 12);
        BitConverter.GetBytes(0.0f).CopyTo(payload, 16);
        BitConverter.GetBytes(0.0f).CopyTo(payload, 20);
        BitConverter.GetBytes(0.0f).CopyTo(payload, 24);
        // command = 246
        BitConverter.GetBytes((ushort)246).CopyTo(payload, 28);
        // target_system = 1
        payload[30] = 1;
        // target_component = 1
        payload[31] = 1;
        // confirmation = 0
        payload[32] = 0;
        
        // Build the full message
        var message = new byte[45];
        message[0] = 0xFD;  // MAVLink v2 start
        message[1] = 33;    // Payload length
        message[2] = 0;     // Incompatibility flags
        message[3] = 0;     // Compatibility flags
        message[4] = 0;     // Sequence
        message[5] = 255;   // System ID (GCS)
        message[6] = 190;   // Component ID (GCS)
        message[7] = 76;    // Message ID low byte (COMMAND_LONG)
        message[8] = 0;     // Message ID mid byte
        message[9] = 0;     // Message ID high byte
        
        Array.Copy(payload, 0, message, 10, 33);
        
        // Calculate CRC
        ushort crc = CalculateX25Crc(message, 1, 42); // from length to end of payload
        // Add CRC_EXTRA for COMMAND_LONG (152)
        crc = CrcAccumulate(152, crc);
        
        message[43] = (byte)(crc & 0xFF);
        message[44] = (byte)((crc >> 8) & 0xFF);
        
        return message;
    }
    
    /// <summary>
    /// X.25 CRC calculation used by MAVLink
    /// </summary>
    private ushort CalculateX25Crc(byte[] buffer, int offset, int length)
    {
        ushort crc = 0xFFFF;
        
        for (int i = 0; i < length; i++)
        {
            crc = CrcAccumulate(buffer[offset + i], crc);
        }
        
        return crc;
    }
    
    /// <summary>
    /// Accumulate one byte into CRC
    /// </summary>
    private ushort CrcAccumulate(byte b, ushort crc)
    {
        byte ch = (byte)(b ^ (byte)(crc & 0x00FF));
        ch = (byte)(ch ^ (ch << 4));
        return (ushort)((crc >> 8) ^ (ch << 8) ^ (ch << 3) ^ (ch >> 4));
    }
    
    private byte[] BuildMavlinkRebootToBootloaderCommand()
    {
        // Use v1 for broader compatibility
        return BuildMavlinkRebootToBootloaderCommandV1();
    }
    
    private ushort CalculateMavlinkCrc(byte[] buffer, int offset, int length, byte crcExtra)
    {
        ushort crc = CalculateX25Crc(buffer, offset, length);
        crc = CrcAccumulate(crcExtra, crc);
        return crc;
    }
    
    public async Task<bool> RebootToFirmwareAsync(CancellationToken ct = default)
    {
        if (CurrentBoard == null || string.IsNullOrEmpty(CurrentBoard.SerialPort))
        {
            Log("No board connected");
            return false;
        }
        
        try
        {
            // Use Px4Uploader to send reboot command to bootloader
            using var uploader = new Px4Uploader(_logger);
            uploader.Open(CurrentBoard.SerialPort);
            uploader.Reboot();
            uploader.Close();
            
            Log("Board rebooted to firmware");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reboot board");
            return false;
        }
    }
    
    #endregion
    
    #region Utility Methods
    
    public void CancelOperation()
    {
        Log("Cancelling operation...");
        _operationCts?.Cancel();
    }
    
    public async Task<(bool IsValid, string Message)> ValidateFirmwareFileAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return (false, "File not found");
            }
            
            var fileInfo = new FileInfo(filePath);
            var extension = fileInfo.Extension.ToLowerInvariant();
            
            if (extension != ".apj" && extension != ".px4" && extension != ".bin" && extension != ".hex")
            {
                return (false, $"Unsupported file type: {extension}. Expected .apj, .px4, .bin, or .hex");
            }
            
            if (fileInfo.Length < 1024)
            {
                return (false, "File too small to be valid firmware");
            }
            
            if (fileInfo.Length > 4 * 1024 * 1024) // 4MB max
            {
                return (false, "File too large (max 4MB)");
            }
            
            // For APJ files, validate JSON structure
            if (extension == ".apj")
            {
                var content = await File.ReadAllTextAsync(filePath);
                try
                {
                    using var doc = JsonDocument.Parse(content);
                    if (!doc.RootElement.TryGetProperty("image", out _))
                    {
                        return (false, "APJ file missing 'image' property");
                    }
                }
                catch (JsonException)
                {
                    return (false, "Invalid APJ file format");
                }
            }
            
            return (true, "Valid firmware file");
        }
        catch (Exception ex)
        {
            return (false, $"Validation error: {ex.Message}");
        }
    }
    
    public async Task<FirmwareManifest?> GetFirmwareManifestAsync(CancellationToken ct = default)
    {
        try
        {
            // Check cache first
            if (_cachedManifest != null && DateTime.Now - _manifestCacheTime < _manifestCacheDuration)
            {
                Log($"Using cached manifest ({_cachedManifest.Firmware.Count} entries)");
                return _cachedManifest;
            }
            
            Log("Fetching firmware manifest...");

            // Try JSON manifest first (preferred)
            var manifest = await FetchJsonManifestAsync(ct);
            
            // Fallback to XML firmware list if JSON fails
            if (manifest == null || manifest.Firmware.Count == 0)
            {
                Log("JSON manifest failed, trying XML firmware list...");
                manifest = await FetchXmlFirmwareListAsync(ct);
            }
            
            if (manifest != null && manifest.Firmware.Count > 0)
            {
                _cachedManifest = manifest;
                _manifestCacheTime = DateTime.Now;
                Log($"Manifest loaded: {manifest.Firmware.Count} entries");
            }
            else
            {
                Log("Failed to fetch firmware manifest from all sources");
            }
            
            return manifest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch firmware manifest");
            return null;
        }
    }

    private async Task<FirmwareManifest?> FetchJsonManifestAsync(CancellationToken ct)
    {
        var endpoints = new[] { FIRMWARE_MANIFEST_URL_GZ, FIRMWARE_MANIFEST_URL };
        const int maxAttemptsPerEndpoint = 2;

        foreach (var endpoint in endpoints)
        {
            for (int attempt = 1; attempt <= maxAttemptsPerEndpoint; attempt++)
            {
                try
                {
                    Log($"Trying {endpoint} (attempt {attempt})...");
                    
                    string json;
                    
                    if (endpoint.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                    {
                        // For .gz files, download as bytes and decompress manually
                        // This is more reliable than relying on AutomaticDecompression
                        var compressedData = await _httpClient.GetByteArrayAsync(endpoint, ct);
                        Log($"Downloaded {compressedData.Length} bytes of compressed data");
                        
                        using var compressedStream = new MemoryStream(compressedData);
                        using var decompressedStream = new MemoryStream();
                        await using (var gzipStream = new System.IO.Compression.GZipStream(
                            compressedStream, System.IO.Compression.CompressionMode.Decompress))
                        {
                            await gzipStream.CopyToAsync(decompressedStream, ct);
                        }
                        
                        decompressedStream.Position = 0;
                        using var reader = new StreamReader(decompressedStream);
                        json = await reader.ReadToEndAsync(ct);
                        
                        Log($"Decompressed to {json.Length} characters");
                    }
                    else
                    {
                        // For non-gzip, just get as string
                        json = await _httpClient.GetStringAsync(endpoint, ct);
                    }
                    
                    if (string.IsNullOrWhiteSpace(json))
                    {
                        _logger.LogWarning("Empty response from {Endpoint}", endpoint);
                        continue;
                    }
                    
                    // Debug: Log first 300 chars of JSON to verify format
                    var preview = json.Length > 300 ? json.Substring(0, 300) : json;
                    Log($"JSON preview: {preview}...");
                    
                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = false, // Use exact property name matching
                        AllowTrailingCommas = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString
                    };
                    
                    var manifest = JsonSerializer.Deserialize<FirmwareManifest>(json, options);

                    if (manifest?.Firmware != null && manifest.Firmware.Count > 0)
                    {
                        Log($"Successfully loaded JSON manifest: {manifest.Firmware.Count} firmware entries");
                        
                        // Debug: Log sample entries to verify parsing
                        var sample = manifest.Firmware.Take(3).ToList();
                        foreach (var entry in sample)
                        {
                            Log($"Sample: vehicletype={entry.VehicleType}, platform={entry.Platform}, " +
                                $"format={entry.Format}, release={entry.MavFirmwareVersionType}, " +
                                $"version={entry.MavFirmwareVersion}, latest={entry.Latest}");
                        }
                        
                        // Verify we have usable data
                        var copterCount = manifest.Firmware.Count(f => 
                            f.VehicleType?.Equals("Copter", StringComparison.OrdinalIgnoreCase) == true);
                        var officialCount = manifest.Firmware.Count(f => 
                            f.ReleaseCategory == "OFFICIAL");
                        Log($"Stats: {copterCount} Copter entries, {officialCount} OFFICIAL releases");
                        
                        return manifest;
                    }
                    else
                    {
                        _logger.LogWarning("Manifest deserialized but Firmware list is null or empty");
                    }
                }
                catch (TaskCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (JsonException jsonEx)
                {
                    _logger.LogError(jsonEx, "JSON deserialization failed for {Endpoint}: {Message}", endpoint, jsonEx.Message);
                    Log($"JSON error: {jsonEx.Message}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Manifest fetch failed from {Endpoint} (attempt {Attempt}/{Max}): {Message}", 
                        endpoint, attempt, maxAttemptsPerEndpoint, ex.Message);
                    Log($"Fetch error: {ex.Message}");
                    if (attempt < maxAttemptsPerEndpoint)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Fallback: Fetch firmware list from Mission Planner's XML format (firmware2.xml)
    /// This provides a simpler list of firmware URLs organized by vehicle type.
    /// </summary>
    private async Task<FirmwareManifest?> FetchXmlFirmwareListAsync(CancellationToken ct)
    {
        foreach (var url in FIRMWARE_XML_URLS)
        {
            try
            {
                Log($"Trying XML firmware list: {url}");
                
                var xml = await _httpClient.GetStringAsync(url, ct);
                if (string.IsNullOrWhiteSpace(xml))
                    continue;
                
                var doc = XDocument.Parse(xml);
                var firmware = doc.Descendants("Firmware").FirstOrDefault();
                if (firmware == null)
                    continue;
                
                var entries = new List<FirmwareEntry>();
                
                // Parse XML format into our FirmwareEntry structure
                foreach (var item in firmware.Elements())
                {
                    var name = item.Element("name")?.Value ?? "";
                    var desc = item.Element("desc")?.Value ?? "";
                    
                    // Extract URLs for different board types
                    var urlMappings = new Dictionary<string, string>
                    {
                        { "url", "apm1" },
                        { "url2560", "apm2" },
                        { "url2560-2", "apm2-2" },
                        { "urlpx4v2", "fmuv2" },
                        { "urlpx4v3", "fmuv3" },
                        { "urlpx4v4", "fmuv4" },
                        { "urlpx4v4pro", "fmuv4pro" },
                        { "urlfmuv5", "fmuv5" },
                        { "urlCubeOrange", "CubeOrange" },
                        { "urlCubeOrangePlus", "CubeOrangePlus" },
                        { "urlCubeYellow", "CubeYellow" },
                        { "urlPixhawk6X", "Pixhawk6X" },
                        { "urlPixhawk6C", "Pixhawk6C" },
                        { "urlMatekH743", "MatekH743" },
                        { "urlKakuteH7", "KakuteH7" }
                    };
                    
                    // Determine vehicle type from name
                    var vehicleType = DetermineVehicleTypeFromName(name);
                    
                    foreach (var mapping in urlMappings)
                    {
                        var firmwareUrl = item.Element(mapping.Key)?.Value;
                        if (!string.IsNullOrEmpty(firmwareUrl))
                        {
                            entries.Add(new FirmwareEntry
                            {
                                VehicleType = vehicleType,
                                Platform = mapping.Value,
                                Url = firmwareUrl,
                                Format = firmwareUrl.EndsWith(".apj") ? "apj" : "px4",
                                MavFirmwareVersionType = "OFFICIAL", // XML list contains stable releases
                                MavFirmwareVersion = ExtractVersionFromUrl(firmwareUrl),
                                LatestLong = 1 // Assume all from XML are latest stable
                            });
                        }
                    }
                }
                
                if (entries.Count > 0)
                {
                    Log($"Loaded {entries.Count} entries from XML firmware list");
                    return new FirmwareManifest { Firmware = entries };
                }
            }
            catch (TaskCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch XML firmware list from {Url}", url);
            }
        }
        
        return null;
    }
    
    private string DetermineVehicleTypeFromName(string name)
    {
        var lower = name.ToLowerInvariant();
        if (lower.Contains("copter") || lower.Contains("quad") || lower.Contains("hexa") || lower.Contains("octa"))
            return "Copter";
        if (lower.Contains("heli"))
            return "Copter-heli";
        if (lower.Contains("plane"))
            return "Plane";
        if (lower.Contains("rover"))
            return "Rover";
        if (lower.Contains("sub"))
            return "Sub";
        if (lower.Contains("tracker"))
            return "AntennaTracker";
        return "Copter"; // Default
    }
    
    private string ExtractVersionFromUrl(string url)
    {
        // Try to extract version from URL like ".../Copter/stable-4.5.7/..."
        try
        {
            var parts = url.Split('/');
            foreach (var part in parts)
            {
                if (part.StartsWith("stable-", StringComparison.OrdinalIgnoreCase) ||
                    part.StartsWith("beta-", StringComparison.OrdinalIgnoreCase))
                {
                    return part.Split('-').LastOrDefault() ?? "unknown";
                }
            }
        }
        catch { }
        return "stable";
    }
    
    #endregion
    
    #region Private Helpers
    
    private void UpdateState(FirmwareFlashState state, double percent, string message)
    {
        lock (_stateLock)
        {
            CurrentState = state;
        }
        
        ProgressChanged?.Invoke(this, new FirmwareProgress
        {
            State = state,
            ProgressPercent = percent,
            StatusMessage = message
        });
    }
    
    private void Log(string message)
    {
        _logger.LogInformation(message);
        LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
    }
    
    private FirmwareFlashResult CreateFailResult(string message, TimeSpan duration)
    {
        UpdateState(FirmwareFlashState.Failed, 0, message);
        return new FirmwareFlashResult
        {
            Success = false,
            Message = message,
            Duration = duration
        };
    }
    
    public void Dispose()
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _httpClient.Dispose();
    }
    
    #endregion

    /// <summary>
    /// Gets available platform variants for a board (e.g., CubeOrangePlus, CubeOrangePlus-bdshot, CubeOrangePlus-periph, etc.)
    /// Used to show a selection dialog when multiple variants exist.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAvailablePlatformVariantsAsync(
        string vehicleType,
        string baseBoardId,
        string? releaseType = null,
        CancellationToken ct = default)
    {
        Log($"Checking platform variants for {baseBoardId}...");
        
        try
        {
            var manifest = await GetFirmwareManifestAsync(ct);
            if (manifest == null) return Array.Empty<string>();
            
            var normalizedVehicleType = NormalizeVehicleType(vehicleType);
            
            // Get all platforms that match or start with the base board ID
            var matchingPlatforms = manifest.Firmware
                .Where(f => 
                    // Match vehicle type
                    (!string.IsNullOrEmpty(f.VehicleType) && f.VehicleType.Equals(normalizedVehicleType, StringComparison.OrdinalIgnoreCase)) &&
                    // Match flashable formats
                    !string.IsNullOrEmpty(f.Format) &&
                    (f.Format.Equals("apj", StringComparison.OrdinalIgnoreCase) || f.Format.Equals("px4", StringComparison.OrdinalIgnoreCase)) &&
                    // Match release type
                    (string.IsNullOrWhiteSpace(releaseType) 
                        ? f.ReleaseCategory.Equals("OFFICIAL", StringComparison.OrdinalIgnoreCase)
                        : f.ReleaseCategory.Equals(releaseType, StringComparison.OrdinalIgnoreCase)) &&
                    // Match platform (exact or starts with)
                    !string.IsNullOrEmpty(f.Platform) &&
                    (f.Platform.Equals(baseBoardId, StringComparison.OrdinalIgnoreCase) ||
                     f.Platform.StartsWith(baseBoardId + "-", StringComparison.OrdinalIgnoreCase)))
                .Select(f => f.Platform!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p.Length) // Shorter names first (base variant first)
                .ThenBy(p => p)
                .ToList();
            
            Log($"Found {matchingPlatforms.Count} platform variants for {baseBoardId}: {string.Join(", ", matchingPlatforms.Take(5))}");
            
            return matchingPlatforms.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting platform variants");
            return Array.Empty<string>();
        }
    }
}
