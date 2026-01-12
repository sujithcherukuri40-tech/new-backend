using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Mission Planner compatible firmware downloader.
/// Parses ArduPilot manifest exactly as Mission Planner does.
/// </summary>
public sealed class FirmwareDownloader : IDisposable
{
    private readonly ILogger<FirmwareDownloader> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    
    // ArduPilot firmware server URLs - same as Mission Planner
    private const string MANIFEST_URL = "https://firmware.ardupilot.org/manifest.json.gz";
    private const string MANIFEST_URL_FALLBACK = "https://firmware.ardupilot.org/manifest.json";
    
    private MissionPlannerManifest? _cachedManifest;
    private DateTime _manifestCacheTime = DateTime.MinValue;
    private readonly TimeSpan _manifestCacheExpiry = TimeSpan.FromMinutes(30);
    
    // Cached firmware versions by vehicle type
    private Dictionary<string, string> _latestVersions = new();
    
    public event EventHandler<DownloadProgressEventArgs>? DownloadProgress;
    
    public FirmwareDownloader(ILogger<FirmwareDownloader> logger)
    {
        _logger = logger;
        
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(15);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "PavamanDroneConfigurator/1.0");
        
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PavamanDroneConfigurator",
            "FirmwareCache");
        
        Directory.CreateDirectory(_cacheDirectory);
    }
    
    /// <summary>
    /// Fetches the firmware manifest from ArduPilot servers - Mission Planner compatible
    /// </summary>
    public async Task<MissionPlannerManifest?> GetManifestAsync(bool forceRefresh = false, CancellationToken ct = default)
    {
        if (!forceRefresh && _cachedManifest != null && 
            DateTime.Now - _manifestCacheTime < _manifestCacheExpiry)
        {
            return _cachedManifest;
        }
        
        _logger.LogInformation("Fetching firmware manifest from ArduPilot...");
        
        try
        {
            string manifestJson;
            
            // Try gzipped version first (like Mission Planner)
            try
            {
                using var response = await _httpClient.GetAsync(MANIFEST_URL, ct);
                response.EnsureSuccessStatusCode();
                
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await using var gzipStream = new System.IO.Compression.GZipStream(stream, System.IO.Compression.CompressionMode.Decompress);
                using var reader = new StreamReader(gzipStream);
                manifestJson = await reader.ReadToEndAsync(ct);
            }
            catch
            {
                // Fallback to non-gzipped version
                _logger.LogWarning("Gzipped manifest failed, trying fallback URL");
                manifestJson = await _httpClient.GetStringAsync(MANIFEST_URL_FALLBACK, ct);
            }
            
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                AllowTrailingCommas = true
            };
            
            _cachedManifest = JsonSerializer.Deserialize<MissionPlannerManifest>(manifestJson, options);
            _manifestCacheTime = DateTime.Now;
            
            // Parse latest versions for each vehicle type
            ParseLatestVersions();
            
            _logger.LogInformation("Manifest loaded: {Count} firmware entries", _cachedManifest?.Firmware?.Count ?? 0);
            
            return _cachedManifest;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch firmware manifest");
            return null;
        }
    }
    
    /// <summary>
    /// Parses latest versions for each vehicle type from manifest
    /// </summary>
    private void ParseLatestVersions()
    {
        _latestVersions.Clear();
        
        if (_cachedManifest?.Firmware == null) return;
        
        // Group by vehicle type and find latest stable for each
        var groups = _cachedManifest.Firmware
            .Where(f => f.Latest && 
                       (f.MavFirmwareVersionType?.Equals("OFFICIAL", StringComparison.OrdinalIgnoreCase) == true ||
                        f.MavFirmwareVersionType?.Equals("STABLE", StringComparison.OrdinalIgnoreCase) == true))
            .GroupBy(f => f.VehicleType ?? f.MavType ?? "Unknown")
            .ToList();
        
        foreach (var group in groups)
        {
            var latest = group.OrderByDescending(f => f.MavFirmwareVersionInt).FirstOrDefault();
            if (latest != null && !string.IsNullOrEmpty(latest.MavFirmwareVersion))
            {
                _latestVersions[group.Key] = latest.MavFirmwareVersion;
            }
        }
    }
    
    /// <summary>
    /// Gets the latest version string for a vehicle type
    /// </summary>
    public string GetLatestVersion(string vehicleType)
    {
        if (_latestVersions.TryGetValue(vehicleType, out var version))
        {
            return $"V{version} OFFICIAL";
        }
        return "V4.6.3 OFFICIAL"; // Default fallback
    }
    
    /// <summary>
    /// Gets available firmware for a specific vehicle type and board - Mission Planner compatible
    /// </summary>
    public async Task<IReadOnlyList<FirmwareInfo>> GetAvailableFirmwareAsync(
        string vehicleType,
        string? boardId = null,
        string? releaseType = null,
        CancellationToken ct = default)
    {
        var manifest = await GetManifestAsync(false, ct);
        if (manifest?.Firmware == null)
        {
            return Array.Empty<FirmwareInfo>();
        }
        
        var query = manifest.Firmware.AsEnumerable();
        
        // Filter by vehicle type (matches Mission Planner logic)
        if (!string.IsNullOrEmpty(vehicleType))
        {
            query = query.Where(f => 
                f.VehicleType?.Contains(vehicleType, StringComparison.OrdinalIgnoreCase) == true ||
                f.MavType?.Contains(vehicleType, StringComparison.OrdinalIgnoreCase) == true ||
                f.Url?.Contains($"/{vehicleType}/", StringComparison.OrdinalIgnoreCase) == true);
        }
        
        // Filter by board
        if (!string.IsNullOrEmpty(boardId))
        {
            query = query.Where(f =>
                f.BoardId?.Equals(boardId, StringComparison.OrdinalIgnoreCase) == true ||
                f.Platform?.Equals(boardId, StringComparison.OrdinalIgnoreCase) == true);
        }
        
        // Filter by release type
        if (!string.IsNullOrEmpty(releaseType))
        {
            query = query.Where(f =>
                f.MavFirmwareVersionType?.Equals(releaseType, StringComparison.OrdinalIgnoreCase) == true);
        }
        else
        {
            // Default to stable/official only
            query = query.Where(f =>
                f.MavFirmwareVersionType?.Equals("OFFICIAL", StringComparison.OrdinalIgnoreCase) == true ||
                f.MavFirmwareVersionType?.Equals("STABLE", StringComparison.OrdinalIgnoreCase) == true);
        }
        
        // Filter for flashable formats (apj preferred, like Mission Planner)
        query = query.Where(f =>
            f.Format?.Equals("apj", StringComparison.OrdinalIgnoreCase) == true);
        
        var results = query
            .Select(f => new FirmwareInfo
            {
                VehicleType = f.VehicleType ?? f.MavType ?? "Unknown",
                BoardId = f.BoardId ?? f.Platform ?? "Unknown",
                Version = f.MavFirmwareVersion ?? "Unknown",
                ReleaseType = f.MavFirmwareVersionType ?? "OFFICIAL",
                Url = f.Url ?? "",
                Format = f.Format ?? "",
                GitHash = f.GitHash ?? "",
                IsLatest = f.Latest,
                Platform = f.Platform ?? ""
            })
            .OrderByDescending(f => f.IsLatest)
            .ThenByDescending(f => f.Version)
            .ToList();
        
        return results.AsReadOnly();
    }
    
    /// <summary>
    /// Gets list of available boards for a vehicle type
    /// </summary>
    public async Task<IReadOnlyList<string>> GetAvailableBoardsAsync(string vehicleType, CancellationToken ct = default)
    {
        var manifest = await GetManifestAsync(false, ct);
        if (manifest?.Firmware == null)
        {
            return Array.Empty<string>();
        }
        
        var boards = manifest.Firmware
            .Where(f => 
                f.VehicleType?.Contains(vehicleType, StringComparison.OrdinalIgnoreCase) == true ||
                f.MavType?.Contains(vehicleType, StringComparison.OrdinalIgnoreCase) == true)
            .Where(f => !string.IsNullOrEmpty(f.Platform))
            .Where(f => f.Format?.Equals("apj", StringComparison.OrdinalIgnoreCase) == true)
            .Select(f => f.Platform ?? "")
            .Where(b => !string.IsNullOrEmpty(b))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(b => b)
            .ToList();
        
        return boards.AsReadOnly();
    }
    
    /// <summary>
    /// Gets the best firmware URL for a vehicle type and detected board
    /// </summary>
    public async Task<string?> GetFirmwareUrlAsync(string vehicleType, string boardPlatform, CancellationToken ct = default)
    {
        var firmwares = await GetAvailableFirmwareAsync(vehicleType, boardPlatform, null, ct);
        var latest = firmwares.FirstOrDefault(f => f.IsLatest) ?? firmwares.FirstOrDefault();
        return latest?.Url;
    }
    
    /// <summary>
    /// Downloads a firmware file with progress reporting
    /// </summary>
    public async Task<string?> DownloadFirmwareAsync(
        string url,
        IProgress<DownloadProgressEventArgs>? progress = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Downloading firmware from: {Url}", url);
        
        try
        {
            var uri = new Uri(url);
            var fileName = Path.GetFileName(uri.LocalPath);
            var localPath = Path.Combine(_cacheDirectory, fileName);
            
            // Check cache
            if (File.Exists(localPath))
            {
                var fileInfo = new FileInfo(localPath);
                if (DateTime.Now - fileInfo.LastWriteTime < TimeSpan.FromDays(1))
                {
                    _logger.LogInformation("Using cached firmware: {Path}", localPath);
                    return localPath;
                }
            }
            
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            
            var buffer = new byte[8192];
            long totalBytesRead = 0;
            int bytesRead;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            
            while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                totalBytesRead += bytesRead;
                
                var progressArgs = new DownloadProgressEventArgs
                {
                    BytesReceived = totalBytesRead,
                    TotalBytes = totalBytes > 0 ? totalBytes : totalBytesRead,
                    ProgressPercent = totalBytes > 0 ? (double)totalBytesRead / totalBytes * 100 : 0,
                    Speed = totalBytesRead / sw.Elapsed.TotalSeconds,
                    FileName = fileName
                };
                
                progress?.Report(progressArgs);
                DownloadProgress?.Invoke(this, progressArgs);
            }
            
            _logger.LogInformation("Download complete: {Path} ({Size} KB)", localPath, totalBytesRead / 1024);
            return localPath;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Download cancelled");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Download failed: {Url}", url);
            return null;
        }
    }
    
    /// <summary>
    /// Clears the firmware cache
    /// </summary>
    public void ClearCache()
    {
        try
        {
            if (Directory.Exists(_cacheDirectory))
            {
                foreach (var file in Directory.GetFiles(_cacheDirectory))
                {
                    try { File.Delete(file); }
                    catch { /* ignore */ }
                }
            }
            _cachedManifest = null;
            _manifestCacheTime = DateTime.MinValue;
            _latestVersions.Clear();
            _logger.LogInformation("Firmware cache cleared");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear cache");
        }
    }
    
    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

/// <summary>
/// Mission Planner compatible manifest structure
/// </summary>
public class MissionPlannerManifest
{
    [JsonPropertyName("format-version")]
    public string? FormatVersion { get; set; }
    
    [JsonPropertyName("firmware")]
    public List<MissionPlannerFirmwareEntry>? Firmware { get; set; }
}

/// <summary>
/// Mission Planner compatible firmware entry
/// </summary>
public class MissionPlannerFirmwareEntry
{
    [JsonPropertyName("mav-type")]
    public string? MavType { get; set; }
    
    [JsonPropertyName("vehicletype")]
    public string? VehicleType { get; set; }
    
    [JsonPropertyName("board_id")]
    public string? BoardId { get; set; }
    
    [JsonPropertyName("platform")]
    public string? Platform { get; set; }
    
    [JsonPropertyName("format")]
    public string? Format { get; set; }
    
    [JsonPropertyName("url")]
    public string? Url { get; set; }
    
    [JsonPropertyName("git-sha")]
    public string? GitHash { get; set; }
    
    [JsonPropertyName("mav-firmware-version")]
    public string? MavFirmwareVersion { get; set; }
    
    [JsonPropertyName("mav-firmware-version-type")]
    public string? MavFirmwareVersionType { get; set; }
    
    [JsonPropertyName("mav-firmware-version-int")]
    public int MavFirmwareVersionInt { get; set; }
    
    [JsonPropertyName("latest")]
    public bool Latest { get; set; }
    
    [JsonPropertyName("frame")]
    public string? Frame { get; set; }
    
    [JsonPropertyName("brand_name")]
    public string? BrandName { get; set; }
    
    [JsonPropertyName("manufacturer")]
    public string? Manufacturer { get; set; }
    
    [JsonPropertyName("USBID")]
    public List<string>? UsbIds { get; set; }
    
    [JsonPropertyName("bootloader_str")]
    public List<string>? BootloaderStrings { get; set; }
}

/// <summary>
/// Simplified firmware info for UI
/// </summary>
public class FirmwareInfo
{
    public string VehicleType { get; set; } = string.Empty;
    public string BoardId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string ReleaseType { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string GitHash { get; set; } = string.Empty;
    public bool IsLatest { get; set; }
    public string Platform { get; set; } = string.Empty;
    
    public string DisplayVersion => $"V{Version} {ReleaseType.ToUpperInvariant()}";
}

/// <summary>
/// Download progress event arguments
/// </summary>
public class DownloadProgressEventArgs : EventArgs
{
    public long BytesReceived { get; set; }
    public long TotalBytes { get; set; }
    public double ProgressPercent { get; set; }
    public double Speed { get; set; }
    public string FileName { get; set; } = string.Empty;
    
    public string SpeedText => Speed switch
    {
        >= 1024 * 1024 => $"{Speed / (1024 * 1024):F1} MB/s",
        >= 1024 => $"{Speed / 1024:F1} KB/s",
        _ => $"{Speed:F0} B/s"
    };
    
    public string ProgressText => TotalBytes > 0
        ? $"{BytesReceived / 1024} / {TotalBytes / 1024} KB"
        : $"{BytesReceived / 1024} KB";
}
