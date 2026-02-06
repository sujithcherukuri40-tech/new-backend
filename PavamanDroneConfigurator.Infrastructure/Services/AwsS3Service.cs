using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// AWS S3 service for accessing firmware files and parameter logs.
/// PRODUCTION: Uses EC2 IAM Role for credentials (no explicit keys).
/// </summary>
public class AwsS3Service : IDisposable
{
    private readonly IAmazonS3 _s3Client;
    private readonly ILogger<AwsS3Service> _logger;
    private const string BucketName = "drone-config-param-logs";
    private const string FirmwarePrefix = "firmwares/";
    private const string ParamsLogsPrefix = "params-logs/";
    
    public AwsS3Service(ILogger<AwsS3Service> logger)
    {
        _logger = logger;
        
        // PRODUCTION: Initialize S3 client with EC2 IAM Role (auto-discovers credentials)
        // No explicit AWS_ACCESS_KEY_ID or AWS_SECRET_ACCESS_KEY needed
        // Credentials are automatically provided by EC2 instance metadata service
        _s3Client = new AmazonS3Client(Amazon.RegionEndpoint.APSouth1);
        
        _logger.LogInformation("AWS S3 Service initialized with EC2 IAM Role for bucket: {Bucket}", BucketName);
    }
    
    /// <summary>
    /// List all firmware files (.apj) from S3 - FOR USERS (In-App source)
    /// </summary>
    public async Task<List<S3FirmwareInfo>> ListFirmwareFilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Listing firmware files from S3 bucket: {Bucket}/{Prefix}", BucketName, FirmwarePrefix);
            
            var request = new ListObjectsV2Request
            {
                BucketName = BucketName,
                Prefix = FirmwarePrefix
            };
            
            var response = await _s3Client.ListObjectsV2Async(request, cancellationToken);
            
            var firmwares = response.S3Objects
                .Where(obj => obj.Key.EndsWith(".apj", StringComparison.OrdinalIgnoreCase))
                .Select(obj => new S3FirmwareInfo
                {
                    Key = obj.Key,
                    FileName = Path.GetFileName(obj.Key),
                    Size = obj.Size,
                    LastModified = obj.LastModified,
                    VehicleType = InferVehicleTypeFromFileName(obj.Key)
                })
                .OrderBy(f => f.VehicleType)
                .ThenBy(f => f.FileName)
                .ToList();
            
            _logger.LogInformation("Found {Count} firmware files in S3", firmwares.Count);
            
            foreach (var fw in firmwares)
            {
                _logger.LogDebug("Firmware: {File} ({Type}) - {Size}", fw.FileName, fw.VehicleType, fw.SizeDisplay);
            }
            
            return firmwares;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list firmware files from S3");
            throw;
        }
    }
    
    /// <summary>
    /// Generate a presigned URL for downloading a firmware file (1 hour expiry)
    /// </summary>
    public string GeneratePresignedUrl(string s3Key, TimeSpan expiration)
    {
        try
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = BucketName,
                Key = s3Key,
                Expires = DateTime.UtcNow.Add(expiration)
            };
            
            var url = _s3Client.GetPreSignedURL(request);
            _logger.LogDebug("Generated presigned URL for {Key}, expires in {Expiration}", s3Key, expiration);
            
            return url;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate presigned URL for {Key}", s3Key);
            throw;
        }
    }
    
    /// <summary>
    /// Download firmware file from S3 to local temporary directory
    /// </summary>
    public async Task<string> DownloadFirmwareAsync(string s3Key, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading firmware from S3: {Key}", s3Key);
            
            var fileName = Path.GetFileName(s3Key);
            var tempDir = Path.Combine(Path.GetTempPath(), "PavamanDroneConfigurator", "Firmwares");
            Directory.CreateDirectory(tempDir);
            
            var localPath = Path.Combine(tempDir, fileName);
            
            var request = new GetObjectRequest
            {
                BucketName = BucketName,
                Key = s3Key
            };
            
            using var response = await _s3Client.GetObjectAsync(request, cancellationToken);
            await response.WriteResponseStreamToFileAsync(localPath, false, cancellationToken);
            
            _logger.LogInformation("Firmware downloaded to: {Path} ({Size} bytes)", localPath, new FileInfo(localPath).Length);
            return localPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download firmware from S3: {Key}", s3Key);
            throw;
        }
    }
    
    /// <summary>
    /// ADMIN: Upload firmware file to S3 (for admin firmware upload feature)
    /// </summary>
    public async Task<S3FirmwareInfo> UploadFirmwareAsync(
        string localFilePath, 
        string? customFileName = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(localFilePath))
            {
                throw new FileNotFoundException($"Firmware file not found: {localFilePath}");
            }
            
            var fileName = customFileName ?? Path.GetFileName(localFilePath);
            var s3Key = $"{FirmwarePrefix}{fileName}";
            
            _logger.LogInformation("Uploading firmware to S3: {Key}", s3Key);
            
            var fileInfo = new FileInfo(localFilePath);
            
            var request = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = s3Key,
                FilePath = localFilePath,
                ContentType = "application/octet-stream",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256 // Enable encryption
            };
            
            var response = await _s3Client.PutObjectAsync(request, cancellationToken);
            
            _logger.LogInformation("Firmware uploaded successfully: {Key} ({Size} bytes)", s3Key, fileInfo.Length);
            
            return new S3FirmwareInfo
            {
                Key = s3Key,
                FileName = fileName,
                Size = fileInfo.Length,
                LastModified = DateTime.UtcNow,
                VehicleType = InferVehicleTypeFromFileName(fileName)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload firmware to S3");
            throw;
        }
    }
    
    /// <summary>
    /// ADMIN: Delete firmware file from S3
    /// </summary>
    public async Task<bool> DeleteFirmwareAsync(string s3Key, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Deleting firmware from S3: {Key}", s3Key);
            
            var request = new DeleteObjectRequest
            {
                BucketName = BucketName,
                Key = s3Key
            };
            
            await _s3Client.DeleteObjectAsync(request, cancellationToken);
            
            _logger.LogInformation("Firmware deleted successfully: {Key}", s3Key);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete firmware from S3: {Key}", s3Key);
            return false;
        }
    }
    
    /// <summary>
    /// Upload parameter change log to S3
    /// </summary>
    public async Task UploadParameterChangeLogAsync(
        string userId, 
        string fcId, 
        string csvContent, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var s3Key = $"{ParamsLogsPrefix}user_{userId}/drone_{fcId}/params_{timestamp}.csv";
            
            _logger.LogInformation("Uploading parameter change log to S3: {Key}", s3Key);
            
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvContent));
            
            var request = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = s3Key,
                InputStream = stream,
                ContentType = "text/csv",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };
            
            await _s3Client.PutObjectAsync(request, cancellationToken);
            
            _logger.LogInformation("Parameter change log uploaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload parameter change log to S3");
            throw;
        }
    }
    
    /// <summary>
    /// Append parameter changes to existing log or create new one
    /// </summary>
    public async Task AppendParameterChangesAsync(
        string userId,
        string fcId,
        List<ParameterChange> changes,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var s3Key = $"{ParamsLogsPrefix}user_{userId}/drone_{fcId}/params_{timestamp}.csv";
            
            // Build CSV content
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("param_name,old_value,new_value,changed_at");
            
            foreach (var change in changes)
            {
                csv.AppendLine($"{change.ParamName},{change.OldValue},{change.NewValue},{change.ChangedAt:yyyy-MM-dd HH:mm:ss}");
            }
            
            await UploadParameterChangeLogAsync(userId, fcId, csv.ToString(), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to append parameter changes");
            throw;
        }
    }
    
    /// <summary>
    /// Check if S3 bucket is accessible (health check)
    /// </summary>
    public async Task<bool> IsS3AccessibleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new ListObjectsV2Request
            {
                BucketName = BucketName,
                MaxKeys = 1
            };
            
            await _s3Client.ListObjectsV2Async(request, cancellationToken);
            _logger.LogInformation("S3 bucket is accessible: {Bucket}", BucketName);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "S3 bucket is NOT accessible: {Bucket}", BucketName);
            return false;
        }
    }
    
    private static string InferVehicleTypeFromFileName(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        
        if (name.Contains("arducopter") || name.Contains("copter"))
            return "Copter";
        if (name.Contains("plane"))
            return "Plane";
        if (name.Contains("rover"))
            return "Rover";
        if (name.Contains("sub"))
            return "Sub";
        if (name.Contains("heli"))
            return "Heli";
        if (name.Contains("tracker"))
            return "Tracker";
        
        return "Unknown";
    }
    
    public void Dispose()
    {
        _s3Client?.Dispose();
    }
}

/// <summary>
/// Firmware file information from S3
/// </summary>
public class S3FirmwareInfo
{
    public string Key { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastModified { get; set; }
    public string VehicleType { get; set; } = string.Empty;
    
    public string DisplayName => Path.GetFileNameWithoutExtension(FileName);
    public string SizeDisplay => FormatBytes(Size);
    
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

/// <summary>
/// Parameter change record for CSV logging
/// </summary>
public class ParameterChange
{
    public string ParamName { get; set; } = string.Empty;
    public float OldValue { get; set; }
    public float NewValue { get; set; }
    public DateTime ChangedAt { get; set; }
}
