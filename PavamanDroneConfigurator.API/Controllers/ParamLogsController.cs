using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PavamanDroneConfigurator.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.API.Controllers;

[ApiController]
[Route("api/param-logs")]
[Authorize] // SECURITY: Require authentication for all endpoints
[EnableRateLimiting("fixed")]
public class ParamLogsController : ControllerBase
{
    private readonly AwsS3Service _s3Service;
    private readonly ILogger<ParamLogsController> _logger;
    
    public ParamLogsController(AwsS3Service s3Service, ILogger<ParamLogsController> logger)
    {
        _s3Service = s3Service;
        _logger = logger;
    }
    
    /// <summary>
    /// GET /api/param-logs/health
    /// Health check for param logs API
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
                return Ok(new { status = "healthy", message = "S3 bucket is accessible for param logs" });
            }
            else
            {
                return StatusCode(503, new { status = "unhealthy", message = "S3 bucket is not accessible" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Param logs health check failed");
            return StatusCode(503, new { status = "unhealthy", error = "Service unavailable" });
        }
    }
    
    /// <summary>
    /// GET /api/param-logs/storage-stats
    /// Get storage statistics for parameter logs (Admin only)
    /// </summary>
    [HttpGet("storage-stats")]
    [Authorize(Roles = "Admin")] // SECURITY: Admin only
    [EnableRateLimiting("admin")]
    public async Task<ActionResult> GetStorageStats(CancellationToken cancellationToken)
    {
        try
        {
            var stats = await _s3Service.GetParamLogsStorageStatsAsync(cancellationToken);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get param logs storage stats");
            return StatusCode(500, new { error = "Failed to get storage statistics" });
        }
    }
    
    /// <summary>
    /// GET /api/param-logs
    /// List all parameter log files from S3 with optional filters (Admin only)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin")] // SECURITY: Admin only - contains all user data
    [EnableRateLimiting("admin")]
    public async Task<ActionResult<ParamLogListResponse>> ListParamLogs(
        [FromQuery] string? search = null,
        [FromQuery] string? userId = null,
        [FromQuery] string? droneId = null,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // SECURITY: Validate pagination parameters
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100; // Limit max page size
            
            _logger.LogInformation(
                "Listing param logs: search={Search}, userId={UserId}, droneId={DroneId}, from={From}, to={To}",
                search, userId, droneId, fromDate, toDate);
            
            var logs = await _s3Service.ListParameterLogsAsync(cancellationToken);
            
            // Apply filters
            var filtered = logs.AsEnumerable();
            
            // SECURITY: Sanitize search input
            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = SanitizeSearchInput(search).ToLower();
                filtered = filtered.Where(l => 
                    l.UserId.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                    l.DroneId.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                    l.FileName.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                    (l.UserName != null && l.UserName.Contains(searchLower, StringComparison.OrdinalIgnoreCase)) ||
                    l.Timestamp.ToString("yyyy-MM-dd").Contains(searchLower));
            }
            
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var sanitizedUserId = SanitizeSearchInput(userId);
                filtered = filtered.Where(l => 
                    l.UserId.Contains(sanitizedUserId, StringComparison.OrdinalIgnoreCase) ||
                    (l.UserName != null && l.UserName.Contains(sanitizedUserId, StringComparison.OrdinalIgnoreCase)));
            }
            
            if (!string.IsNullOrWhiteSpace(droneId))
            {
                var sanitizedDroneId = SanitizeSearchInput(droneId);
                filtered = filtered.Where(l => l.DroneId.Contains(sanitizedDroneId, StringComparison.OrdinalIgnoreCase));
            }
            
            if (fromDate.HasValue)
            {
                filtered = filtered.Where(l => l.Timestamp >= fromDate.Value);
            }
            
            if (toDate.HasValue)
            {
                filtered = filtered.Where(l => l.Timestamp <= toDate.Value.AddDays(1));
            }
            
            // Sort by timestamp descending (newest first)
            var sortedLogs = filtered.OrderByDescending(l => l.Timestamp).ToList();
            
            // Pagination
            var totalCount = sortedLogs.Count;
            var pagedLogs = sortedLogs
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            
            // Get unique users and drones for filter dropdowns
            var allUsers = logs
                .Select(l => !string.IsNullOrWhiteSpace(l.UserName) ? l.UserName : l.UserId)
                .Distinct()
                .OrderBy(x => x)
                .ToList();
            var allDrones = logs.Select(l => l.DroneId).Distinct().OrderBy(x => x).ToList();
            
            return Ok(new ParamLogListResponse
            {
                Logs = pagedLogs,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                AvailableUsers = allUsers,
                AvailableDrones = allDrones
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list param logs");
            return StatusCode(500, new { error = "Failed to list parameter logs" });
        }
    }
    
    /// <summary>
    /// GET /api/param-logs/{key}
    /// Get contents of a specific parameter log CSV file (Admin only)
    /// </summary>
    [HttpGet("{**key}")]
    [Authorize(Roles = "Admin")] // SECURITY: Admin only
    [EnableRateLimiting("admin")]
    public async Task<ActionResult<ParamLogContentResponse>> GetParamLogContent(
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
            // SECURITY: Validate and sanitize the key
            if (string.IsNullOrWhiteSpace(key))
            {
                return BadRequest(new { error = "Invalid key" });
            }
            
            // SECURITY: Ensure key is within the params-logs folder
            if (!key.StartsWith("params-logs/", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Rejected param log request - invalid key path: {Key}", key);
                return BadRequest(new { error = "Invalid parameter log key" });
            }
            
            // SECURITY: Prevent path traversal
            if (key.Contains("..") || key.Contains("//"))
            {
                _logger.LogWarning("Rejected param log request - path traversal attempt: {Key}", key);
                return BadRequest(new { error = "Invalid key format" });
            }
            
            _logger.LogInformation("Getting param log content: {Key}", key);
            
            var content = await _s3Service.GetParameterLogContentAsync(key, cancellationToken);
            
            // Parse CSV content and extract metadata
            var (changes, metadata) = ParseCsvContent(content);
            
            return Ok(new ParamLogContentResponse
            {
                Key = key,
                RawContent = content,
                Changes = changes,
                UserName = metadata.GetValueOrDefault("user_name"),
                BoardId = metadata.GetValueOrDefault("board_id")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get param log content: {Key}", key);
            return StatusCode(500, new { error = "Failed to get parameter log" });
        }
    }
    
    /// <summary>
    /// GET /api/param-logs/download/{key}
    /// Get presigned URL for downloading a param log file (Admin only)
    /// </summary>
    [HttpGet("download/{**key}")]
    [Authorize(Roles = "Admin")] // SECURITY: Admin only
    [EnableRateLimiting("admin")]
    public ActionResult GetDownloadUrl(string key)
    {
        try
        {
            // SECURITY: Validate key
            if (string.IsNullOrWhiteSpace(key) || !key.StartsWith("params-logs/", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "Invalid parameter log key" });
            }
            
            if (key.Contains("..") || key.Contains("//"))
            {
                return BadRequest(new { error = "Invalid key format" });
            }
            
            _logger.LogInformation("Generating download URL for param log: {Key}", key);
            
            var downloadUrl = _s3Service.GeneratePresignedUrl(key, TimeSpan.FromHours(1));
            
            return Ok(new { downloadUrl, expiresIn = 3600 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate download URL: {Key}", key);
            return StatusCode(500, new { error = "Download failed" });
        }
    }
    
    /// <summary>
    /// Sanitizes search input to prevent injection attacks.
    /// </summary>
    private static string SanitizeSearchInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;
        
        // Remove potentially dangerous characters
        return new string(input.Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '@' || c == '.' || c == ' ').ToArray());
    }
    
    private (List<ParamChangeEntry> Changes, Dictionary<string, string> Metadata) ParseCsvContent(string csvContent)
    {
        var changes = new List<ParamChangeEntry>();
        var metadata = new Dictionary<string, string>();
        
        if (string.IsNullOrWhiteSpace(csvContent))
            return (changes, metadata);
        
        var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            
            // Parse metadata comments (e.g., "# user_name=John Doe")
            if (trimmedLine.StartsWith('#'))
            {
                var metaLine = trimmedLine.TrimStart('#', ' ');
                var eqIndex = metaLine.IndexOf('=');
                if (eqIndex > 0)
                {
                    var key = metaLine[..eqIndex].Trim();
                    var value = metaLine[(eqIndex + 1)..].Trim();
                    metadata[key] = value;
                }
                continue;
            }
            
            // Skip header line
            if (trimmedLine.StartsWith("param_name", StringComparison.OrdinalIgnoreCase))
                continue;
            
            // Parse data lines
            var parts = trimmedLine.Split(',');
            if (parts.Length >= 4)
            {
                changes.Add(new ParamChangeEntry
                {
                    ParamName = parts[0].Trim(),
                    OldValue = parts[1].Trim(),
                    NewValue = parts[2].Trim(),
                    ChangedAt = parts[3].Trim()
                });
            }
        }
        
        return (changes, metadata);
    }
}

#region Response Models

public class ParamLogListResponse
{
    public List<ParamLogEntry> Logs { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public List<string> AvailableUsers { get; set; } = new();
    public List<string> AvailableDrones { get; set; } = new();
}

public class ParamLogContentResponse
{
    public string Key { get; set; } = string.Empty;
    public string RawContent { get; set; } = string.Empty;
    public List<ParamChangeEntry> Changes { get; set; } = new();
    public string? UserName { get; set; }
    public string? BoardId { get; set; }
}

public class ParamChangeEntry
{
    public string ParamName { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public string ChangedAt { get; set; } = string.Empty;
}

#endregion
