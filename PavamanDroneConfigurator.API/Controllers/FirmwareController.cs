using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PavamanDroneConfigurator.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FirmwareController : ControllerBase
{
    private readonly AwsS3Service _s3Service;
    private readonly ILogger<FirmwareController> _logger;
    
    // Allowed firmware file extensions
    private static readonly string[] AllowedExtensions = { ".apj", ".px4", ".bin" };
    
    // Firmware file magic bytes for validation
    private static readonly Dictionary<string, byte[]> FileMagicBytes = new()
    {
        { ".apj", new byte[] { 0x7B } }, // JSON format starts with {
        { ".bin", Array.Empty<byte>() }, // Binary files don't have specific magic
        { ".px4", Array.Empty<byte>() }
    };
    
    public FirmwareController(AwsS3Service s3Service, ILogger<FirmwareController> logger)
    {
        _s3Service = s3Service;
        _logger = logger;
    }
    
    /// <summary>
    /// GET /api/firmware/inapp
    /// Lists all firmware files available in S3 with presigned download URLs (FOR USERS)
    /// </summary>
    [HttpGet("inapp")]
    [Authorize] // SECURITY: Require authentication to list firmware
    [EnableRateLimiting("fixed")]
    public async Task<ActionResult<List<FirmwareMetadata>>> GetInAppFirmwares(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Fetching in-app firmwares from S3");
            
            var firmwares = await _s3Service.ListFirmwareFilesAsync(cancellationToken);
            
            var metadata = firmwares.Select(f => new FirmwareMetadata
            {
                Key = f.Key,
                FileName = f.FileName,
                DisplayName = f.DisplayName,
                VehicleType = f.VehicleType,
                Size = f.Size,
                SizeDisplay = f.SizeDisplay,
                LastModified = f.LastModified,
                DownloadUrl = _s3Service.GeneratePresignedUrl(f.Key, TimeSpan.FromHours(1)),
                FirmwareName = f.FirmwareName,
                FirmwareVersion = f.FirmwareVersion,
                FirmwareDescription = f.FirmwareDescription
            }).ToList();
            
            _logger.LogInformation("Returning {Count} firmware files", metadata.Count);
            return Ok(metadata);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch in-app firmwares");
            return StatusCode(500, new { error = "Failed to fetch firmwares from cloud storage" });
        }
    }
    
    /// <summary>
    /// ADMIN: POST /api/firmware/admin/upload
    /// Upload firmware file to S3 (Admin only)
    /// </summary>
    [HttpPost("admin/upload")]
    [Authorize(Roles = "Admin")] // SECURITY: Require Admin role
    [EnableRateLimiting("admin")]
    public async Task<ActionResult<FirmwareMetadata>> UploadFirmware(
        [FromForm] IFormFile file, 
        [FromForm] string? customFileName,
        [FromForm] string? firmwareName,
        [FromForm] string? firmwareVersion,
        [FromForm] string? firmwareDescription,
        CancellationToken cancellationToken)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file uploaded" });
            }
            
            // SECURITY: Validate file extension
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
            {
                _logger.LogWarning("Rejected firmware upload with invalid extension: {Extension}", extension);
                return BadRequest(new { error = $"Invalid file type. Allowed: {string.Join(", ", AllowedExtensions)}" });
            }
            
            // SECURITY: Validate file size (max 50MB for firmware files)
            const long maxFileSize = 50 * 1024 * 1024;
            if (file.Length > maxFileSize)
            {
                _logger.LogWarning("Rejected firmware upload - file too large: {Size} bytes", file.Length);
                return BadRequest(new { error = "File too large (max 50MB)" });
            }
            
            // SECURITY: Validate file name to prevent path traversal
            var sanitizedFileName = SanitizeFileName(customFileName ?? file.FileName);
            if (string.IsNullOrEmpty(sanitizedFileName))
            {
                return BadRequest(new { error = "Invalid file name" });
            }
            
            _logger.LogInformation("Admin uploading firmware: {FileName} ({Size} bytes)", sanitizedFileName, file.Length);
            
            // Save to temp file
            var tempPath = Path.GetTempFileName();
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream, cancellationToken);
                }
                
                // SECURITY: Validate file content (magic bytes) for APJ files
                if (extension == ".apj" && !await ValidateFileContentAsync(tempPath, extension))
                {
                    _logger.LogWarning("Rejected firmware upload - invalid file content for extension: {Extension}", extension);
                    return BadRequest(new { error = "File content does not match expected format" });
                }
                
                // Upload to S3 with metadata
                var s3Info = await _s3Service.UploadFirmwareAsync(
                    tempPath, 
                    sanitizedFileName,
                    firmwareName,
                    firmwareVersion,
                    firmwareDescription,
                    cancellationToken);
                
                // Generate metadata response
                var metadata = new FirmwareMetadata
                {
                    Key = s3Info.Key,
                    FileName = s3Info.FileName,
                    DisplayName = s3Info.DisplayName,
                    VehicleType = s3Info.VehicleType,
                    Size = s3Info.Size,
                    SizeDisplay = s3Info.SizeDisplay,
                    LastModified = s3Info.LastModified,
                    DownloadUrl = _s3Service.GeneratePresignedUrl(s3Info.Key, TimeSpan.FromHours(1)),
                    FirmwareName = s3Info.FirmwareName,
                    FirmwareVersion = s3Info.FirmwareVersion,
                    FirmwareDescription = s3Info.FirmwareDescription
                };
                
                _logger.LogInformation("Firmware uploaded successfully: {Key}", s3Info.Key);
                return Ok(metadata);
            }
            finally
            {
                // Cleanup temp file
                try { System.IO.File.Delete(tempPath); } catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload firmware");
            return StatusCode(500, new { error = "Upload failed. Please try again." });
        }
    }
    
    /// <summary>
    /// ADMIN: DELETE /api/firmware/admin/{key}
    /// Delete firmware file from S3 (Admin only)
    /// </summary>
    [HttpDelete("admin/{**key}")]
    [Authorize(Roles = "Admin")] // SECURITY: Require Admin role
    [EnableRateLimiting("admin")]
    public async Task<ActionResult> DeleteFirmware(string key, CancellationToken cancellationToken)
    {
        try
        {
            // SECURITY: Validate and sanitize the S3 key
            if (string.IsNullOrWhiteSpace(key))
            {
                return BadRequest(new { error = "Invalid key" });
            }
            
            // SECURITY: Ensure key is within the firmware folder
            if (!key.StartsWith("firmwares/", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Rejected firmware delete request - invalid key path: {Key}", key);
                return BadRequest(new { error = "Invalid firmware key" });
            }
            
            // SECURITY: Prevent path traversal
            if (key.Contains("..") || key.Contains("//"))
            {
                _logger.LogWarning("Rejected firmware delete request - path traversal attempt: {Key}", key);
                return BadRequest(new { error = "Invalid key format" });
            }
            
            _logger.LogInformation("Admin deleting firmware: {Key}", key);
            
            var success = await _s3Service.DeleteFirmwareAsync(key, cancellationToken);
            
            if (success)
            {
                return Ok(new { message = "Firmware deleted successfully" });
            }
            else
            {
                return StatusCode(500, new { error = "Failed to delete firmware" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete firmware: {Key}", key);
            return StatusCode(500, new { error = "Delete failed. Please try again." });
        }
    }
    
    /// <summary>
    /// GET /api/firmware/download/{key}
    /// Download firmware file (generates presigned URL for direct S3 download)
    /// </summary>
    [HttpGet("download/{**key}")]
    [Authorize] // SECURITY: Require authentication
    [EnableRateLimiting("fixed")]
    public ActionResult DownloadFirmware(string key)
    {
        try
        {
            // SECURITY: Validate key
            if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("firmwares/", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "Invalid firmware key" });
            }
            
            if (key.Contains("..") || key.Contains("//"))
            {
                return BadRequest(new { error = "Invalid key format" });
            }
            
            _logger.LogInformation("Generating download URL for firmware: {Key}", key);
            
            // Generate presigned URL valid for 1 hour
            var downloadUrl = _s3Service.GeneratePresignedUrl(key, TimeSpan.FromHours(1));
            
            return Ok(new { downloadUrl, expiresIn = 3600 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate download URL: {Key}", key);
            return StatusCode(500, new { error = "Download failed. Please try again." });
        }
    }
    
    /// <summary>
    /// GET /api/firmware/health
    /// Check S3 connectivity (health check)
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous] // Health check can be public
    public async Task<ActionResult> HealthCheck(CancellationToken cancellationToken)
    {
        try
        {
            var isAccessible = await _s3Service.IsS3AccessibleAsync(cancellationToken);
            
            if (isAccessible)
            {
                return Ok(new { status = "healthy", message = "S3 bucket is accessible" });
            }
            else
            {
                return StatusCode(503, new { status = "unhealthy", message = "S3 bucket is not accessible" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(503, new { status = "unhealthy", error = "Service unavailable" });
        }
    }
    
    /// <summary>
    /// GET /api/firmware/storage-stats
    /// Get storage statistics for firmwares
    /// </summary>
    [HttpGet("storage-stats")]
    [Authorize(Roles = "Admin")] // SECURITY: Admin only
    [EnableRateLimiting("admin")]
    public async Task<ActionResult> GetStorageStats(CancellationToken cancellationToken)
    {
        try
        {
            var stats = await _s3Service.GetFirmwareStorageStatsAsync(cancellationToken);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get storage stats");
            return StatusCode(500, new { error = "Failed to get storage statistics" });
        }
    }
    
    /// <summary>
    /// POST /api/firmware/param-logs
    /// Upload parameter change log to S3 (param-logs folder)
    /// </summary>
    [HttpPost("param-logs")]
    [Authorize] // SECURITY: Require authentication
    [EnableRateLimiting("fixed")]
    public async Task<ActionResult> UploadParameterLog(
        [FromBody] ParameterLogRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            if (request?.Changes == null || request.Changes.Count == 0)
            {
                return BadRequest(new { error = "No parameter changes provided" });
            }
            
            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                return BadRequest(new { error = "UserId is required" });
            }
            
            // SECURITY: Limit the number of changes per request
            const int maxChanges = 1000;
            if (request.Changes.Count > maxChanges)
            {
                return BadRequest(new { error = $"Too many changes. Maximum {maxChanges} allowed per request." });
            }
            
            _logger.LogInformation(
                "Uploading parameter log for user={UserId} ({UserName}), drone={DroneId}, fc={FcId}, changes={Count}",
                request.UserId, request.UserName ?? "unknown", request.DroneId ?? "unknown", request.FcId ?? "unknown", request.Changes.Count);
            
            // Convert to ParameterChange list for S3 service
            var changes = request.Changes.Select(c => new Infrastructure.Services.ParameterChange
            {
                ParamName = c.ParamName,
                OldValue = c.OldValue,
                NewValue = c.NewValue,
                ChangedAt = c.ChangedAt ?? DateTime.UtcNow
            }).ToList();
            
            // Upload to S3 using DroneId (not FcId) for folder organization
            await _s3Service.AppendParameterChangesAsync(
                request.UserId,
                request.UserName,
                request.DroneId ?? "unknown",  // Use Drone ID for folder structure
                changes,
                cancellationToken);
            
            _logger.LogInformation("Parameter log uploaded successfully: {Count} changes", changes.Count);
            
            return Ok(new { 
                message = "Parameter log uploaded successfully",
                changeCount = changes.Count,
                userId = request.UserId,
                userName = request.UserName,
                droneId = request.DroneId,
                fcId = request.FcId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload parameter log");
            return StatusCode(500, new { error = "Upload failed. Please try again." });
        }
    }
    
    /// <summary>
    /// Sanitizes file name to prevent path traversal attacks.
    /// </summary>
    private static string? SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;
        
        // Remove path components
        fileName = Path.GetFileName(fileName);
        
        // Remove dangerous characters
        var invalidChars = Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToArray();
        foreach (var c in invalidChars)
        {
            fileName = fileName.Replace(c, '_');
        }
        
        // Prevent hidden files
        if (fileName.StartsWith('.'))
        {
            fileName = "_" + fileName[1..];
        }
        
        // Limit length
        if (fileName.Length > 255)
        {
            var extension = Path.GetExtension(fileName);
            fileName = fileName[..(255 - extension.Length)] + extension;
        }
        
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }
    
    /// <summary>
    /// Validates file content by checking magic bytes.
    /// </summary>
    private static async Task<bool> ValidateFileContentAsync(string filePath, string extension)
    {
        if (!FileMagicBytes.TryGetValue(extension, out var expectedBytes) || expectedBytes.Length == 0)
        {
            return true; // No validation for this extension
        }
        
        try
        {
            var buffer = new byte[expectedBytes.Length];
            await using var stream = System.IO.File.OpenRead(filePath);
            var bytesRead = await stream.ReadAsync(buffer);
            
            if (bytesRead < expectedBytes.Length)
                return false;
            
            return buffer.SequenceEqual(expectedBytes);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Firmware metadata returned by API
/// </summary>
public class FirmwareMetadata
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
    public string? UserName { get; set; }
    public string? DroneId { get; set; }
    public string? FcId { get; set; }
    public List<ParameterChangeDto> Changes { get; set; } = new();
}

/// <summary>
/// Parameter change data transfer object
/// </summary>
public class ParameterChangeDto
{
    public string ParamName { get; set; } = string.Empty;
    public float OldValue { get; set; }
    public float NewValue { get; set; }
    public DateTime? ChangedAt { get; set; }
}
