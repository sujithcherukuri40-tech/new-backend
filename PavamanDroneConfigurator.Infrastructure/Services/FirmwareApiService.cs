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
    
    /// <summary>
    /// Upload firmware file to cloud storage via API with metadata
    /// </summary>
    public async Task<S3FirmwareMetadata> UploadFirmwareAsync(
        string filePath,
        string? customFileName = null,
        string? firmwareName = null,
        string? firmwareVersion = null,
        string? firmwareDescription = null,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Uploading firmware: {FilePath}", filePath);
            
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("Firmware file not found", filePath);
            }
            
            using var form = new MultipartFormDataContent();
            using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var streamContent = new StreamContent(fileStream);
            
            var fileName = customFileName ?? Path.GetFileName(filePath);
            form.Add(streamContent, "file", fileName);
            
            if (!string.IsNullOrEmpty(customFileName))
            {
                form.Add(new StringContent(customFileName), "customFileName");
            }
            
            // Add metadata fields
            if (!string.IsNullOrEmpty(firmwareName))
            {
                form.Add(new StringContent(firmwareName), "firmwareName");
            }
            
            if (!string.IsNullOrEmpty(firmwareVersion))
            {
                form.Add(new StringContent(firmwareVersion), "firmwareVersion");
            }
            
            if (!string.IsNullOrEmpty(firmwareDescription))
            {
                form.Add(new StringContent(firmwareDescription), "firmwareDescription");
            }
            
            var response = await _httpClient.PostAsync("/api/firmware/admin/upload", form, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<S3FirmwareMetadata>(cancellationToken);
            
            if (result == null)
            {
                throw new InvalidOperationException("Server returned null response");
            }
            
            _logger.LogInformation("Firmware uploaded successfully: {Key}", result.Key);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload firmware");
            throw new Exception($"Failed to upload firmware: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// Delete firmware from cloud storage via API
    /// </summary>
    public async Task<bool> DeleteFirmwareAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting firmware: {Key}", key);
            
            var encodedKey = Uri.EscapeDataString(key);
            var response = await _httpClient.DeleteAsync($"/api/firmware/admin/{encodedKey}", cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Firmware deleted successfully: {Key}", key);
                return true;
            }
            else
            {
                _logger.LogWarning("Failed to delete firmware {Key}: {StatusCode}", key, response.StatusCode);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete firmware: {Key}", key);
            return false;
        }
    }
    
    /// <summary>
    /// Get download URL for firmware
    /// </summary>
    public async Task<string> GetDownloadUrlAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var encodedKey = Uri.EscapeDataString(key);
            var response = await _httpClient.GetAsync($"/api/firmware/download/{encodedKey}", cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<DownloadUrlResponse>(cancellationToken);
            return result?.DownloadUrl ?? throw new InvalidOperationException("No download URL returned");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get download URL for: {Key}", key);
            throw new Exception($"Failed to get download URL: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// Check S3 health via API
    /// </summary>
    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/firmware/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Health check failed");
            return false;
        }
    }
    
    /// <summary>
    /// Upload parameter change log to S3 via API
    /// </summary>
    public async Task UploadParameterLogAsync(
        string userId,
        string? droneId,
        string? fcId,
        List<ParameterChange> changes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation(
                "Uploading parameter log: user={UserId}, drone={DroneId}, fc={FcId}, changes={Count}",
                userId, droneId ?? "unknown", fcId ?? "unknown", changes.Count);
            
            var request = new ParameterLogRequest
            {
                UserId = userId,
                DroneId = droneId,
                FcId = fcId,
                Changes = changes.Select(c => new ParameterChangeDto
                {
                    ParamName = c.ParamName,
                    OldValue = c.OldValue,
                    NewValue = c.NewValue,
                    ChangedAt = c.ChangedAt
                }).ToList()
            };
            
            var response = await _httpClient.PostAsJsonAsync("/api/firmware/param-logs", request, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            _logger.LogInformation("Parameter log uploaded successfully: {Count} changes", changes.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload parameter log");
            throw new Exception($"Failed to upload parameter log: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// Get list of parameter logs with optional filters
    /// </summary>
    public async Task<ParamLogListResponse?> GetParamLogsAsync(string queryString, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching param logs with query: {Query}", queryString);
            var response = await _httpClient.GetAsync($"/api/param-logs?{queryString}", cancellationToken);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadFromJsonAsync<ParamLogListResponse>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch param logs");
            throw;
        }
    }
    
    /// <summary>
    /// Get content of a specific parameter log file
    /// </summary>
    public async Task<ParamLogContentResponse?> GetParamLogContentAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var encodedKey = Uri.EscapeDataString(key);
            _logger.LogInformation("Fetching param log content: {Key}", key);
            var response = await _httpClient.GetAsync($"/api/param-logs/{encodedKey}", cancellationToken);
            response.EnsureSuccessStatusCode();
            
            return await response.Content.ReadFromJsonAsync<ParamLogContentResponse>(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch param log content: {Key}", key);
            throw;
        }
    }
    
    /// <summary>
    /// Get presigned download URL for a parameter log file
    /// </summary>
    public async Task<string?> GetParamLogDownloadUrlAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var encodedKey = Uri.EscapeDataString(key);
            _logger.LogInformation("Getting download URL for param log: {Key}", key);
            var response = await _httpClient.GetAsync($"/api/param-logs/download/{encodedKey}", cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var result = await response.Content.ReadFromJsonAsync<DownloadUrlResponse>(cancellationToken);
            return result?.DownloadUrl;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get download URL for param log: {Key}", key);
            throw;
        }
    }
    
    private class DownloadUrlResponse
    {
        public string DownloadUrl { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
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

/// <summary>
/// Request model for parameter log upload
/// </summary>
public class ParameterLogRequest
{
    public string UserId { get; set; } = string.Empty;
    public string? DroneId { get; set; }
    public string? FcId { get; set; }
    public List<ParameterChangeDto> Changes { get; set; } = new();
}

/// <summary>
/// Parameter change DTO for API
/// </summary>
public class ParameterChangeDto
{
    public string ParamName { get; set; } = string.Empty;
    public float OldValue { get; set; }
    public float NewValue { get; set; }
    public DateTime? ChangedAt { get; set; }
}

/// <summary>
/// Response for listing parameter logs
/// </summary>
public class ParamLogListResponse
{
    public List<ParamLogDto> Logs { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<string> AvailableUsers { get; set; } = new();
    public List<string> AvailableDrones { get; set; } = new();
}

/// <summary>
/// Parameter log metadata
/// </summary>
public class ParamLogDto
{
    public string Key { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string DroneId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public long Size { get; set; }
    public string SizeDisplay { get; set; } = string.Empty;
}

/// <summary>
/// Response for parameter log content
/// </summary>
public class ParamLogContentResponse
{
    public string Key { get; set; } = string.Empty;
    public string RawContent { get; set; } = string.Empty;
    public List<ParamChangeDetailDto> Changes { get; set; } = new();
}

/// <summary>
/// Parameter change detail from CSV
/// </summary>
public class ParamChangeDetailDto
{
    public string ParamName { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public string ChangedAt { get; set; } = string.Empty;
}
