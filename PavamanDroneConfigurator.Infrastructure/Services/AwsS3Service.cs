using Amazon.S3;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
    private readonly string _bucketName;
    private readonly string _region;
    private const string FirmwarePrefix = "firmwares/";
    private const string ParamsLogsPrefix = "params-logs/";
    private bool _initializationFailed = false;
    private string? _initializationError = null;
    
    public AwsS3Service(ILogger<AwsS3Service> logger, IConfiguration? configuration = null)
    {
        _logger = logger;
        
        // Get bucket name from configuration/environment
        _bucketName = Environment.GetEnvironmentVariable("AWS_S3_BUCKET_NAME")
            ?? configuration?["S3:BucketName"]
            ?? configuration?["AWS:S3:BucketName"]
            ?? "drone-config-param-logs";
            
        _region = Environment.GetEnvironmentVariable("AWS_S3_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_REGION")
            ?? configuration?["AWS:Region"]
            ?? configuration?["AWS:S3:Region"]
            ?? "ap-south-1";
            
        _logger.LogInformation("[S3] Service created - Bucket: {Bucket}, Region: {Region}", _bucketName, _region);
    }

    public string GeneratePreSignedUrl(string key, int expiryMinutes)
    {
        var safeExpiryMinutes = Math.Max(expiryMinutes, 1);
        return GeneratePresignedUrl(key, TimeSpan.FromMinutes(safeExpiryMinutes));
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
                _logger.LogInformation("[S3] Initializing client for region {Region}...", _region);
                
                var regionEndpoint = Amazon.RegionEndpoint.GetBySystemName(_region);
                
                var config = new AmazonS3Config
                {
                    RegionEndpoint = regionEndpoint,
                    Timeout = TimeSpan.FromSeconds(30),
                    MaxErrorRetry = 3
                };
                
                // Uses default credential chain: EC2 IAM role, env vars, or ~/.aws/credentials
                _s3Client = new AmazonS3Client(config);
                _logger.LogInformation("[S3] Client initialized for bucket: {Bucket}", _bucketName);
            }
            catch (AmazonServiceException ex)
            {
                _initializationFailed = true;
                _initializationError = $"AWS Error: {ex.ErrorCode} - {ex.Message}";
                _logger.LogError(ex, "[S3] AWS service error during initialization: {ErrorCode}", ex.ErrorCode);
                throw new InvalidOperationException(_initializationError, ex);
            }
            catch (Exception ex)
            {
                _initializationFailed = true;
                _initializationError = ex.Message;
                _logger.LogError(ex, "[S3] Failed to initialize client");
                throw new InvalidOperationException($"Failed to initialize S3 client: {ex.Message}", ex);
            }
        }
        
        return _s3Client;
    }
    
    /// <summary>
    /// List all firmware files from S3
    /// Supports: .apj, .px4, .bin, .hex
    /// </summary>
    public async Task<List<S3FirmwareInfo>> ListFirmwareFilesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetS3Client();
            _logger.LogInformation("[S3] Listing firmware files from {Bucket}/{Prefix}", _bucketName, FirmwarePrefix);
            
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = FirmwarePrefix
            };
            
            var firmwares = new List<S3FirmwareInfo>();
            var supportedExtensions = new[] { ".apj", ".px4", ".bin", ".hex" };
            
            ListObjectsV2Response response;
            do
            {
                response = await client.ListObjectsV2Async(request, cancellationToken);
                
                foreach (var obj in response.S3Objects.Where(o => 
                    supportedExtensions.Any(ext => o.Key.EndsWith(ext, StringComparison.OrdinalIgnoreCase))))
                {
                    try
                    {
                        var metadataRequest = new GetObjectMetadataRequest
                        {
                            BucketName = _bucketName,
                            Key = obj.Key
                        };
                        
                        var metadataResponse = await client.GetObjectMetadataAsync(metadataRequest, cancellationToken);
                        
                        var firmwareInfo = new S3FirmwareInfo
                        {
                            Key = obj.Key,
                            FileName = Path.GetFileName(obj.Key),
                            Size = obj.Size ?? 0,
                            LastModified = obj.LastModified ?? DateTime.UtcNow,
                            VehicleType = InferVehicleTypeFromFileName(obj.Key)
                        };
                        
                        // Extract custom metadata (AWS adds x-amz-meta- prefix automatically)
                        if (metadataResponse.Metadata["firmware-name"] != null)
                            firmwareInfo.FirmwareName = metadataResponse.Metadata["firmware-name"];
                        if (metadataResponse.Metadata["firmware-version"] != null)
                            firmwareInfo.FirmwareVersion = metadataResponse.Metadata["firmware-version"];
                        if (metadataResponse.Metadata["firmware-description"] != null)
                            firmwareInfo.FirmwareDescription = metadataResponse.Metadata["firmware-description"];
                        
                        firmwares.Add(firmwareInfo);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[S3] Failed to get metadata for {Key}", obj.Key);
                        // Add without metadata
                        firmwares.Add(new S3FirmwareInfo
                        {
                            Key = obj.Key,
                            FileName = Path.GetFileName(obj.Key),
                            Size = obj.Size ?? 0,
                            LastModified = obj.LastModified ?? DateTime.UtcNow,
                            VehicleType = InferVehicleTypeFromFileName(obj.Key)
                        });
                    }
                }
                
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated == true);
            
            firmwares = firmwares.OrderBy(f => f.VehicleType).ThenBy(f => f.FileName).ToList();
            _logger.LogInformation("[S3] Found {Count} firmware files", firmwares.Count);
            
            return firmwares;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "[S3] AWS error listing firmwares: {ErrorCode}", ex.ErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[S3] Failed to list firmware files");
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
                BucketName = _bucketName,
                Key = s3Key,
                Expires = DateTime.UtcNow.Add(expiration)
            };
            
            return client.GetPreSignedURL(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[S3] Failed to generate presigned URL for {Key}", s3Key);
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
            _logger.LogInformation("[S3] Downloading firmware: {Key}", s3Key);
            
            var fileName = Path.GetFileName(s3Key);
            var tempDir = Path.Combine(Path.GetTempPath(), "PavamanDroneConfigurator", "Firmwares");
            Directory.CreateDirectory(tempDir);
            var localPath = Path.Combine(tempDir, fileName);
            
            var request = new GetObjectRequest { BucketName = _bucketName, Key = s3Key };
            using var response = await client.GetObjectAsync(request, cancellationToken);
            await response.WriteResponseStreamToFileAsync(localPath, false, cancellationToken);
            
            _logger.LogInformation("[S3] Firmware downloaded to: {Path}", localPath);
            return localPath;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "[S3] AWS error downloading {Key}: {ErrorCode}", s3Key, ex.ErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[S3] Failed to download firmware: {Key}", s3Key);
            throw;
        }
    }
    
    /// <summary>
    /// Upload firmware file to S3 with optional metadata
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
            
            _logger.LogInformation("[S3] Uploading firmware: {Key} ({Size} bytes)", s3Key, fileInfo.Length);
            
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = s3Key,
                FilePath = localFilePath,
                ContentType = "application/octet-stream",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };
            
            // Add custom metadata
            if (!string.IsNullOrWhiteSpace(firmwareName))
                request.Metadata["firmware-name"] = firmwareName;
            if (!string.IsNullOrWhiteSpace(firmwareVersion))
                request.Metadata["firmware-version"] = firmwareVersion;
            if (!string.IsNullOrWhiteSpace(firmwareDescription))
                request.Metadata["firmware-description"] = firmwareDescription;
            
            await client.PutObjectAsync(request, cancellationToken);
            _logger.LogInformation("[S3] Firmware uploaded: {Key}", s3Key);
            
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
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "[S3] AWS error uploading firmware: {ErrorCode}", ex.ErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[S3] Failed to upload firmware");
            throw;
        }
    }

    public async Task<string> UploadFirmwareAsync(Stream fileStream, string fileName, string version)
    {
        if (fileStream == null) throw new ArgumentNullException(nameof(fileStream));
        if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name is required", nameof(fileName));
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Version is required", nameof(version));

        try
        {
            var client = GetS3Client();
            var safeFileName = Path.GetFileName(fileName.Trim());
            var safeVersion = version.Trim().Replace("/", "-").Replace("\\", "-");
            var key = $"firmware/{safeVersion}/{safeFileName}";

            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = fileStream,
                ContentType = "application/octet-stream",
                AutoCloseStream = false,
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };

            await client.PutObjectAsync(request);
            _logger.LogInformation("[S3] Firmware stream uploaded: {Key}", key);
            return key;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "[S3] AWS error uploading firmware stream: {ErrorCode}", ex.ErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[S3] Failed to upload firmware stream");
            throw;
        }
    }

    public async Task BackupParamsAsync(string deviceId, string jsonData)
    {
        if (string.IsNullOrWhiteSpace(deviceId)) throw new ArgumentException("Device ID is required", nameof(deviceId));
        if (jsonData == null) throw new ArgumentNullException(nameof(jsonData));

        try
        {
            var client = GetS3Client();
            var safeDeviceId = deviceId.Trim().Replace("/", "-").Replace("\\", "-");
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var key = $"params-backup/{safeDeviceId}/{timestamp}.json";

            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonData));
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = stream,
                ContentType = "application/json",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };

            await client.PutObjectAsync(request);
            _logger.LogInformation("[S3] Parameter backup uploaded: {Key}", key);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "[S3] AWS error uploading parameter backup: {ErrorCode}", ex.ErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[S3] Failed to upload parameter backup");
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
            await client.DeleteObjectAsync(_bucketName, s3Key, cancellationToken);
            _logger.LogInformation("[S3] Firmware deleted: {Key}", s3Key);
            return true;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "[S3] AWS error deleting {Key}: {ErrorCode}", s3Key, ex.ErrorCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[S3] Failed to delete firmware: {Key}", s3Key);
            return false;
        }
    }
    
    /// <summary>
    /// Upload parameter change log to S3
    /// </summary>
    public async Task UploadParameterChangeLogAsync(
        string userId, 
        string? userName,
        string fcId, 
        string csvContent, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetS3Client();
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            
            var safeUserName = string.IsNullOrEmpty(userName) 
                ? "unknown" 
                : System.Text.RegularExpressions.Regex.Replace(userName, @"[^a-zA-Z0-9_\-\.]", "_");
            
            var safeDroneId = string.IsNullOrEmpty(fcId)
                ? "unknown"
                : System.Text.RegularExpressions.Regex.Replace(fcId, @"[^a-zA-Z0-9_\-]", "_");
            
            var s3Key = $"{ParamsLogsPrefix}user_{userId}_{safeUserName}/drone_{safeDroneId}/params_{timestamp}.csv";
            
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(csvContent));
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = s3Key,
                InputStream = stream,
                ContentType = "text/csv",
                ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256
            };
            
            if (!string.IsNullOrEmpty(userName))
                request.Metadata.Add("username", userName);
            
            await client.PutObjectAsync(request, cancellationToken);
            _logger.LogInformation("[S3] Parameter log uploaded: {Key}", s3Key);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "[S3] AWS error uploading param log: {ErrorCode}", ex.ErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[S3] Failed to upload parameter log");
            throw;
        }
    }
    
    public Task UploadParameterChangeLogAsync(string userId, string fcId, string csvContent, CancellationToken cancellationToken = default)
        => UploadParameterChangeLogAsync(userId, null, fcId, csvContent, cancellationToken);
    
    /// <summary>
    /// Append parameter changes with metadata header in CSV
    /// </summary>
    public async Task AppendParameterChangesAsync(
        string userId, 
        string? userName,
        string fcId, 
        List<ParameterChange> changes,
        CancellationToken cancellationToken = default)
    {
        var csv = new System.Text.StringBuilder();
        csv.AppendLine($"# user_name={userName ?? "unknown"}");
        csv.AppendLine($"# user_id={userId}");
        csv.AppendLine($"# drone_id={fcId}");
        csv.AppendLine($"# timestamp={DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
        csv.AppendLine("param_name,old_value,new_value,changed_at");
        foreach (var change in changes)
            csv.AppendLine($"{change.ParamName},{change.OldValue},{change.NewValue},{change.ChangedAt:yyyy-MM-dd HH:mm:ss}");
        
        await UploadParameterChangeLogAsync(userId, userName, fcId, csv.ToString(), cancellationToken);
    }
    
    public Task AppendParameterChangesAsync(string userId, string fcId, List<ParameterChange> changes, CancellationToken cancellationToken = default)
        => AppendParameterChangesAsync(userId, null, fcId, changes, cancellationToken);
    
    /// <summary>
    /// List all parameter log files from S3
    /// </summary>
    public async Task<List<ParamLogEntry>> ListParameterLogsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetS3Client();
            _logger.LogInformation("[S3] Listing parameter logs from {Bucket}/{Prefix}", _bucketName, ParamsLogsPrefix);
            
            var logs = new List<ParamLogEntry>();
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = ParamsLogsPrefix
            };
            
            ListObjectsV2Response response;
            do
            {
                response = await client.ListObjectsV2Async(request, cancellationToken);
                
                foreach (var obj in response.S3Objects.Where(o => o.Key.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)))
                {
                    var entry = ParseParamLogKey(obj.Key, obj.Size ?? 0, obj.LastModified ?? DateTime.UtcNow);
                    if (entry != null)
                        logs.Add(entry);
                }
                
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated == true);
            
            _logger.LogInformation("[S3] Found {Count} parameter log files", logs.Count);
            return logs;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "[S3] AWS error listing param logs: {ErrorCode}", ex.ErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[S3] Failed to list parameter logs");
            throw;
        }
    }
    
    /// <summary>
    /// Get contents of a parameter log file
    /// </summary>
    public async Task<string> GetParameterLogContentAsync(string s3Key, CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetS3Client();
            _logger.LogInformation("[S3] Reading parameter log: {Key}", s3Key);
            
            var request = new GetObjectRequest { BucketName = _bucketName, Key = s3Key };
            using var response = await client.GetObjectAsync(request, cancellationToken);
            using var reader = new StreamReader(response.ResponseStream);
            
            return await reader.ReadToEndAsync(cancellationToken);
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "[S3] AWS error reading param log: {ErrorCode}", ex.ErrorCode);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[S3] Failed to read parameter log: {Key}", s3Key);
            throw;
        }
    }
    
    private ParamLogEntry? ParseParamLogKey(string key, long size, DateTime lastModified)
    {
        try
        {
            var parts = key.Split('/');
            if (parts.Length < 4) return null;
            
            var userPart = parts[1];
            string userId, userName = string.Empty;
            
            if (userPart.StartsWith("user_"))
            {
                var userInfo = userPart.Replace("user_", "");
                var underscoreIndex = userInfo.IndexOf('_');
                
                if (underscoreIndex > 0)
                {
                    userId = userInfo[..underscoreIndex];
                    userName = userInfo[(underscoreIndex + 1)..].Replace("_", " ");
                }
                else
                {
                    userId = userInfo;
                }
            }
            else
            {
                userId = userPart;
            }
            
            var dronePart = parts[2];
            string droneId = dronePart.StartsWith("drone_") ? dronePart.Replace("drone_", "") 
                           : dronePart.StartsWith("board_") ? dronePart.Replace("board_", "") 
                           : dronePart;
            
            var fileName = parts[3];
            var timestampStr = fileName.Replace("params_", "").Replace(".csv", "");
            DateTime timestamp = lastModified;
            
            if (DateTime.TryParseExact(timestampStr, "yyyyMMdd_HHmmss", 
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
            {
                timestamp = parsed;
            }
            
            return new ParamLogEntry
            {
                Key = key,
                FileName = fileName,
                UserId = userId,
                UserName = !string.IsNullOrEmpty(userName) ? userName : null,
                DroneId = droneId,
                Timestamp = timestamp,
                Size = size,
                SizeDisplay = FormatBytes(size)
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[S3] Failed to parse param log key: {Key}", key);
            return null;
        }
    }
    
    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1) { order++; len /= 1024; }
        return $"{len:0.##} {sizes[order]}";
    }
    
    /// <summary>
    /// Get storage statistics for firmwares
    /// </summary>
    public async Task<StorageStats> GetFirmwareStorageStatsAsync(CancellationToken cancellationToken = default)
    {
        return await GetStorageStatsAsync(FirmwarePrefix, cancellationToken);
    }
    
    /// <summary>
    /// Get storage statistics for parameter logs
    /// </summary>
    public async Task<StorageStats> GetParamLogsStorageStatsAsync(CancellationToken cancellationToken = default)
    {
        return await GetStorageStatsAsync(ParamsLogsPrefix, cancellationToken);
    }
    
    private async Task<StorageStats> GetStorageStatsAsync(string prefix, CancellationToken cancellationToken)
    {
        try
        {
            var client = GetS3Client();
            var request = new ListObjectsV2Request { BucketName = _bucketName, Prefix = prefix };
            
            long totalBytes = 0;
            int fileCount = 0;
            
            ListObjectsV2Response response;
            do
            {
                response = await client.ListObjectsV2Async(request, cancellationToken);
                foreach (var obj in response.S3Objects)
                {
                    totalBytes += obj.Size ?? 0;
                    fileCount++;
                }
                request.ContinuationToken = response.NextContinuationToken;
            }
            while (response.IsTruncated == true);
            
            return new StorageStats { TotalBytes = totalBytes, TotalSizeFormatted = FormatBytes(totalBytes), FileCount = fileCount };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[S3] Failed to get storage stats for {Prefix}", prefix);
            return new StorageStats();
        }
    }
    
    /// <summary>
    /// Check if S3 bucket is accessible
    /// </summary>
    public async Task<bool> IsS3AccessibleAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var client = GetS3Client();
            var request = new ListObjectsV2Request { BucketName = _bucketName, MaxKeys = 1 };
            await client.ListObjectsV2Async(request, cancellationToken);
            _logger.LogInformation("[S3] Bucket accessible: {Bucket}", _bucketName);
            return true;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "[S3] Bucket NOT accessible: {Bucket} - {ErrorCode}", _bucketName, ex.ErrorCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[S3] Bucket NOT accessible: {Bucket}", _bucketName);
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

// ============================================================================
// Data Models
// ============================================================================

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

public class ParameterChange
{
    public string ParamName { get; set; } = string.Empty;
    public float OldValue { get; set; }
    public float NewValue { get; set; }
    public DateTime ChangedAt { get; set; }
}

public class ParamLogEntry
{
    public string Key { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string DroneId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public long Size { get; set; }
    public string SizeDisplay { get; set; } = string.Empty;
}

public class StorageStats
{
    public long TotalBytes { get; set; }
    public string TotalSizeFormatted { get; set; } = "0 B";
    public int FileCount { get; set; }
}
