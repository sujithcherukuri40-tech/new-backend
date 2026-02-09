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
    private IAmazonS3? _s3Client;
    private readonly ILogger<AwsS3Service> _logger;
    private const string BucketName = "drone-config-param-logs";
    private const string FirmwarePrefix = "firmwares/";
    private const string ParamsLogsPrefix = "params-logs/";
    private bool _initializationFailed = false;
    private string? _initializationError = null;
    
    public AwsS3Service(ILogger<AwsS3Service> logger)
    {
        _logger = logger;
        _logger.LogInformation("AWS S3 Service created (lazy initialization)");
    }
    
    private IAmazonS3 GetS3Client()
    {
        if (_initializationFailed)
        {
            throw new InvalidOperationException($"S3 client initialization failed: {_initializationError}");
        }
        
        if (_s3Client == null)
        {
            try
            {
                _logger.LogInformation("Initializing S3 client for region ap-south-1...");
                
                var config = new AmazonS3Config
                {
                    RegionEndpoint = Amazon.RegionEndpoint.APSouth1,
                    Timeout = TimeSpan.FromSeconds(30),
                    MaxErrorRetry = 3
                };
                
                _s3Client = new AmazonS3Client(config);
                _logger.LogInformation("AWS S3 Service initialized successfully for bucket: {Bucket}", BucketName);
            }
            catch (Exception ex)
            {
                _initializationFailed = true;
                _initializationError = ex.Message;
                _logger.LogError(ex, "Failed to initialize S3 client");
                throw new InvalidOperationException($"Failed to initialize S3 client: {ex.Message}", ex);
            }
        }
        
        return _s3Client;
    }
    
    /// <summary>
    /// List all firmware files (.apj) from S3 - FOR USERS (In-App source)
    /// NOW RETRIEVES METADATA: name, version, description
    /// </summary>
    public async Task<List<S3FirmwareInfo>> ListFirmwareFilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetS3Client();
            _logger.LogInformation("Listing firmware files from S3 bucket: {Bucket}/{Prefix}", BucketName, FirmwarePrefix);
            
            var request = new ListObjectsV2Request
            {
                BucketName = BucketName,
                Prefix = FirmwarePrefix
            };
            
            var response = await client.ListObjectsV2Async(request, cancellationToken);
            
            var firmwares = new List<S3FirmwareInfo>();
            
            foreach (var obj in response.S3Objects.Where(o => o.Key.EndsWith(".apj", StringComparison.OrdinalIgnoreCase)))
            {
                // Get object metadata to retrieve custom metadata
                try
                {
                    var metadataRequest = new GetObjectMetadataRequest
                    {
                        BucketName = BucketName,
                        Key = obj.Key
                    };
                    
                    var metadataResponse = await client.GetObjectMetadataAsync(metadataRequest, cancellationToken);
                    
                    var firmwareInfo = new S3FirmwareInfo
                    {
                        Key = obj.Key,
                        FileName = Path.GetFileName(obj.Key),
                        Size = obj.Size,
                        LastModified = obj.LastModified,
                        VehicleType = InferVehicleTypeFromFileName(obj.Key)
                    };
                    
                    // Extract custom metadata
                    if (metadataResponse.Metadata["x-amz-meta-firmware-name"] != null)
                        firmwareInfo.FirmwareName = metadataResponse.Metadata["x-amz-meta-firmware-name"];
                    
                    if (metadataResponse.Metadata["x-amz-meta-firmware-version"] != null)
                        firmwareInfo.FirmwareVersion = metadataResponse.Metadata["x-amz-meta-firmware-version"];
                    
                    if (metadataResponse.Metadata["x-amz-meta-firmware-description"] != null)
                        firmwareInfo.FirmwareDescription = metadataResponse.Metadata["x-amz-meta-firmware-description"];
                    
                    firmwares.Add(firmwareInfo);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get metadata for {Key}, using default info", obj.Key);
                    
                    // Fallback: add without metadata
                    firmwares.Add(new S3FirmwareInfo
                    {
                        Key = obj.Key,
                        FileName = Path.GetFileName(obj.Key),
                        Size = obj.Size,
                        LastModified = obj.LastModified,
                        VehicleType = InferVehicleTypeFromFileName(obj.Key)
                    });
                }
            }
            
            firmwares = firmwares.OrderBy(f => f.VehicleType).ThenBy(f => f.FileName).ToList();
            _logger.LogInformation("Found {Count} firmware files in S3", firmwares.Count);
            
            foreach (var fw in firmwares)
            {
                var displayInfo = !string.IsNullOrWhiteSpace(fw.FirmwareName) 
                    ? $"{fw.FirmwareName} v{fw.FirmwareVersion}" 
                    : fw.FileName;
                _logger.LogDebug("Firmware: {DisplayInfo} ({Type}) - {Size}", displayInfo, fw.VehicleType, fw.SizeDisplay);
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
    /// Generate a presigned URL for downloading a firmware file
    /// </summary>
    public string GeneratePresignedUrl(string s3Key, TimeSpan expiration)
    {
        try
        {
            var client = GetS3Client();
            var request = new GetPreSignedUrlRequest
            {
                BucketName = BucketName,
                Key = s3Key,
                Expires = DateTime.UtcNow.Add(expiration)
            };
            
            return client.GetPreSignedURL(request);
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
            var client = GetS3Client();
            _logger.LogInformation("Downloading firmware from S3: {Key}", s3Key);
            
            var fileName = Path.GetFileName(s3Key);
            var tempDir = Path.Combine(Path.GetTempPath(), "PavamanDroneConfigurator", "Firmwares");
            Directory.CreateDirectory(tempDir);
            var localPath = Path.Combine(tempDir, fileName);
            
            var request = new GetObjectRequest { BucketName = BucketName, Key = s3Key };
            using var response = await client.GetObjectAsync(request, cancellationToken);
            await response.WriteResponseStreamToFileAsync(localPath, false, cancellationToken);
            
            _logger.LogInformation("Firmware downloaded to: {Path}", localPath);
            return localPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download firmware from S3: {Key}", s3Key);
            throw;
        }
    }
    
    /// <summary>
    /// Upload firmware file to S3
    /// NOW SUPPORTS METADATA: name, version, description
    /// </summary>
    public async Task<S3FirmwareInfo> UploadFirmwareAsync(
        string localFilePath, 
        string? customFileName = null,
        string? firmwareName = null,
        string? firmwareVersion = null,
        string? firmwareDescription = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetS3Client();
            if (!File.Exists(localFilePath))
                throw new FileNotFoundException($"Firmware file not found: {localFilePath}");
            
            var fileName = customFileName ?? Path.GetFileName(localFilePath);
            var s3Key = $"{FirmwarePrefix}{fileName}";
            var fileInfo = new FileInfo(localFilePath);
            
            var request = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = s3Key,
                FilePath = localFilePath,
                ContentType = "application/octet-stream",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };
            
            // Add custom metadata if provided
            if (!string.IsNullOrWhiteSpace(firmwareName))
                request.Metadata["x-amz-meta-firmware-name"] = firmwareName;
            if (!string.IsNullOrWhiteSpace(firmwareVersion))
                request.Metadata["x-amz-meta-firmware-version"] = firmwareVersion;
            if (!string.IsNullOrWhiteSpace(firmwareDescription))
                request.Metadata["x-amz-meta-firmware-description"] = firmwareDescription;
            
            await client.PutObjectAsync(request, cancellationToken);
            _logger.LogInformation("Firmware uploaded: {Key}", s3Key);
            
            return new S3FirmwareInfo
            {
                Key = s3Key,
                FileName = fileName,
                Size = fileInfo.Length,
                LastModified = DateTime.UtcNow,
                VehicleType = InferVehicleTypeFromFileName(fileName),
                FirmwareName = firmwareName,
                FirmwareVersion = firmwareVersion,
                FirmwareDescription = firmwareDescription
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload firmware to S3");
            throw;
        }
    }
    
    /// <summary>
    /// Delete firmware file from S3
    /// </summary>
    public async Task<bool> DeleteFirmwareAsync(string s3Key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetS3Client();
            await client.DeleteObjectAsync(BucketName, s3Key, cancellationToken);
            _logger.LogInformation("Firmware deleted: {Key}", s3Key);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete firmware: {Key}", s3Key);
            return false;
        }
    }
    
    /// <summary>
    /// Upload parameter change log to S3
    /// </summary>
    public async Task UploadParameterChangeLogAsync(
        string userId, string fcId, string csvContent, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetS3Client();
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var s3Key = $"{ParamsLogsPrefix}user_{userId}/drone_{fcId}/params_{timestamp}.csv";
            
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvContent));
            var request = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = s3Key,
                InputStream = stream,
                ContentType = "text/csv",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };
            
            await client.PutObjectAsync(request, cancellationToken);
            _logger.LogInformation("Parameter log uploaded: {Key}", s3Key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload parameter log");
            throw;
        }
    }
    
    /// <summary>
    /// Append parameter changes
    /// </summary>
    public async Task AppendParameterChangesAsync(
        string userId, string fcId, List<ParameterChange> changes,
        CancellationToken cancellationToken = default)
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine("param_name,old_value,new_value,changed_at");
        foreach (var change in changes)
            csv.AppendLine($"{change.ParamName},{change.OldValue},{change.NewValue},{change.ChangedAt:yyyy-MM-dd HH:mm:ss}");
        
        await UploadParameterChangeLogAsync(userId, fcId, csv.ToString(), cancellationToken);
    }
    
    /// <summary>
    /// Check if S3 bucket is accessible (health check)
    /// </summary>
    public async Task<bool> IsS3AccessibleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetS3Client();
            var request = new ListObjectsV2Request { BucketName = BucketName, MaxKeys = 1 };
            await client.ListObjectsV2Async(request, cancellationToken);
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
        if (name.Contains("arducopter") || name.Contains("copter")) return "Copter";
        if (name.Contains("plane")) return "Plane";
        if (name.Contains("rover")) return "Rover";
        if (name.Contains("sub")) return "Sub";
        if (name.Contains("heli")) return "Heli";
        if (name.Contains("tracker")) return "Tracker";
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
    public string? FirmwareName { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? FirmwareDescription { get; set; }
    
    public string DisplayName => !string.IsNullOrWhiteSpace(FirmwareName) 
        ? FirmwareName : Path.GetFileNameWithoutExtension(FileName);
    
    public string SizeDisplay => FormatBytes(Size);
    
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
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
