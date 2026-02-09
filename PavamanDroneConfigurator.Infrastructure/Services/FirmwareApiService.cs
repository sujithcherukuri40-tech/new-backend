using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Service for fetching firmware metadata from backend API
/// </summary>
public class FirmwareApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<FirmwareApiService> _logger;
    
    public FirmwareApiService(HttpClient httpClient, ILogger<FirmwareApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }
    
    /// <summary>
    /// Fetch in-app firmware list from backend API
    /// </summary>
    public async Task<List<S3FirmwareMetadata>> GetInAppFirmwaresAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching in-app firmwares from API");
            
            var response = await _httpClient.GetAsync("/api/firmware/inapp", cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var firmwares = await response.Content.ReadFromJsonAsync<List<S3FirmwareMetadata>>(cancellationToken);
            
            _logger.LogInformation("Received {Count} firmwares from API", firmwares?.Count ?? 0);
            return firmwares ?? new List<S3FirmwareMetadata>();
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch firmwares from API - network error");
            throw new Exception("Unable to connect to firmware server. Please check your internet connection.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch firmwares from API");
            throw new Exception("Failed to load firmware list from cloud storage.", ex);
        }
    }
    
    /// <summary>
    /// Download firmware file using presigned URL
    /// </summary>
    public async Task<string> DownloadFirmwareAsync(
        string downloadUrl, 
        string fileName,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading firmware: {FileName}", fileName);
            
            var tempDir = Path.Combine(Path.GetTempPath(), "PavamanDroneConfigurator", "Firmwares");
            Directory.CreateDirectory(tempDir);
            
            var localPath = Path.Combine(tempDir, fileName);
            
            using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var bytesRead = 0L;
            
            using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
            
            var buffer = new byte[8192];
            int read;
            
            while ((read = await contentStream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                bytesRead += read;
                
                if (totalBytes > 0 && progress != null)
                {
                    var progressPercent = (int)((bytesRead * 100) / totalBytes);
                    progress.Report(progressPercent);
                }
            }
            
            _logger.LogInformation("Firmware downloaded successfully to: {Path}", localPath);
            return localPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download firmware");
            throw new Exception($"Failed to download firmware: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Firmware metadata from API (matches FirmwareController.FirmwareMetadata)
/// </summary>
public class S3FirmwareMetadata
{
    public string Key { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string VehicleType { get; set; } = string.Empty;
    public long Size { get; set; }
    public string SizeDisplay { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    
    // Custom metadata
    public string? FirmwareName { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? FirmwareDescription { get; set; }
}
