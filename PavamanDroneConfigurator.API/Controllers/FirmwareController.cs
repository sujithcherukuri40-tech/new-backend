using Microsoft.AspNetCore.Mvc;
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
            
            // Validate file extension
            var allowedExtensions = new[] { ".apj", ".px4", ".bin" };
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExtensions.Contains(extension))
            {
                return BadRequest(new { error = $"Invalid file type. Allowed: {string.Join(", ", allowedExtensions)}" });
            }
            
            // Validate file size (max 10MB)
            if (file.Length > 10 * 1024 * 1024)
            {
                return BadRequest(new { error = "File too large (max 10MB)" });
            }
            
            _logger.LogInformation("Admin uploading firmware: {FileName} ({Size} bytes)", file.FileName, file.Length);
            
            // Save to temp file
            var tempPath = Path.GetTempFileName();
            try
            {
                using (var stream = new FileStream(tempPath, FileMode.Create))
                {
                    await file.CopyToAsync(stream, cancellationToken);
                }
                
                // Upload to S3 with metadata
                var fileName = string.IsNullOrWhiteSpace(customFileName) ? file.FileName : customFileName;
                var s3Info = await _s3Service.UploadFirmwareAsync(
                    tempPath, 
                    fileName,
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
            return StatusCode(500, new { error = $"Upload failed: {ex.Message}" });
        }
    }
    
    /// <summary>
    /// ADMIN: DELETE /api/firmware/admin/{key}
    /// Delete firmware file from S3 (Admin only)
    /// </summary>
    [HttpDelete("admin/{**key}")]
    public async Task<ActionResult> DeleteFirmware(string key, CancellationToken cancellationToken)
    {
        try
        {
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
            return StatusCode(500, new { error = $"Delete failed: {ex.Message}" });
        }
    }
    
    /// <summary>
    /// GET /api/firmware/health
    /// Check S3 connectivity (health check)
    /// </summary>
    [HttpGet("health")]
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
            return StatusCode(503, new { status = "unhealthy", error = ex.Message });
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
