using Microsoft.AspNetCore.Mvc;
using PavamanDroneConfigurator.Infrastructure.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.API.Controllers;

[ApiController]
[Route("api/param-logs")]
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
            return StatusCode(503, new { status = "unhealthy", error = ex.Message });
        }
    }
    
    /// <summary>
    /// GET /api/param-logs
    /// List all parameter log files from S3 with optional filters
    /// </summary>
    [HttpGet]
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
            _logger.LogInformation(
                "Listing param logs: search={Search}, userId={UserId}, droneId={DroneId}, from={From}, to={To}",
                search, userId, droneId, fromDate, toDate);
            
            var logs = await _s3Service.ListParameterLogsAsync(cancellationToken);
            
            // Apply filters
            var filtered = logs.AsEnumerable();
            
            // Search filter (matches userId, droneId, userName, or filename)
            if (!string.IsNullOrWhiteSpace(search))
            {
                var searchLower = search.ToLower();
                filtered = filtered.Where(l => 
                    l.UserId.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                    l.DroneId.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                    l.FileName.Contains(searchLower, StringComparison.OrdinalIgnoreCase) ||
                    (l.UserName != null && l.UserName.Contains(searchLower, StringComparison.OrdinalIgnoreCase)) ||
                    l.Timestamp.ToString("yyyy-MM-dd").Contains(searchLower));
            }
            
            if (!string.IsNullOrWhiteSpace(userId))
            {
                // Match by UserId OR UserName
                filtered = filtered.Where(l => 
                    l.UserId.Contains(userId, StringComparison.OrdinalIgnoreCase) ||
                    (l.UserName != null && l.UserName.Contains(userId, StringComparison.OrdinalIgnoreCase)));
            }
            
            if (!string.IsNullOrWhiteSpace(droneId))
            {
                filtered = filtered.Where(l => l.DroneId.Contains(droneId, StringComparison.OrdinalIgnoreCase));
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
            // Use UserName for display (fallback to UserId if no name)
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
            return StatusCode(500, new { error = $"Failed to list param logs: {ex.Message}" });
        }
    }
    
    /// <summary>
    /// GET /api/param-logs/{key}
    /// Get contents of a specific parameter log CSV file
    /// </summary>
    [HttpGet("{**key}")]
    public async Task<ActionResult<ParamLogContentResponse>> GetParamLogContent(
        string key,
        CancellationToken cancellationToken)
    {
        try
        {
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
            return StatusCode(500, new { error = $"Failed to get param log: {ex.Message}" });
        }
    }
    
    /// <summary>
    /// GET /api/param-logs/download/{key}
    /// Get presigned URL for downloading a param log file
    /// </summary>
    [HttpGet("download/{**key}")]
    public ActionResult GetDownloadUrl(string key)
    {
        try
        {
            _logger.LogInformation("Generating download URL for param log: {Key}", key);
            
            var downloadUrl = _s3Service.GeneratePresignedUrl(key, TimeSpan.FromHours(1));
            
            return Ok(new { downloadUrl, expiresIn = 3600 });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate download URL: {Key}", key);
            return StatusCode(500, new { error = $"Download failed: {ex.Message}" });
        }
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
            if (trimmedLine.StartsWith("#"))
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
