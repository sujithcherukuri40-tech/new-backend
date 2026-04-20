using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.API.Data;
using PavamanDroneConfigurator.API.Data.Entities;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Infrastructure.Services;
using System.Text.Json;

namespace PavamanDroneConfigurator.API.Services;

/// <summary>
/// Service for managing parameter locks.
/// Stores lock metadata in RDS and actual parameter lists in S3 as JSON.
/// </summary>
public class ParamLockService : IParamLockService
{
    private readonly AppDbContext _context;
    private readonly AwsS3Service _s3Service;
    private readonly ILogger<ParamLockService> _logger;

    private const string S3_LOCK_PREFIX = "locked-firmware-params";

    public ParamLockService(
        AppDbContext context,
        AwsS3Service s3Service,
        ILogger<ParamLockService> logger)
    {
        _context = context;
        _s3Service = s3Service;
        _logger = logger;
    }

    public async Task<string> CreateParamLockAsync(Guid userId, string? deviceId, List<string> paramKeys, Guid adminUserId)
    {
        if (paramKeys == null || paramKeys.Count == 0)
            throw new ArgumentException("At least one parameter must be locked", nameof(paramKeys));

        // Check if user exists
        var userExists = await _context.Users.AnyAsync(u => u.Id == userId);
        if (!userExists)
            throw new ArgumentException($"User {userId} not found", nameof(userId));

        // Generate S3 key
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var devicePart = string.IsNullOrWhiteSpace(deviceId) ? "global" : SanitizeForS3(deviceId);
        var s3Key = $"{S3_LOCK_PREFIX}/{userId}/{devicePart}/{timestamp}.json";

        // Create JSON payload
        var lockData = new
        {
            lockedParams = paramKeys.Distinct().OrderBy(p => p).ToList(),
            userId = userId.ToString(),
            deviceId = deviceId,
            createdAt = DateTime.UtcNow,
            createdBy = adminUserId.ToString()
        };

        var json = JsonSerializer.Serialize(lockData, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        try
        {
            // Upload to S3
            await _s3Service.UploadJsonAsync(s3Key, json);
            _logger.LogInformation("Uploaded parameter lock to S3: {S3Key}", s3Key);

            // Check if lock already exists for this user/device
            var existingLock = await _context.Set<ParameterLockEntity>()
                .FirstOrDefaultAsync(l => l.UserId == userId && l.DeviceId == deviceId && l.IsActive);

            if (existingLock != null)
            {
                // Update existing lock
                existingLock.S3Key = s3Key;
                existingLock.ParamCount = paramKeys.Count;
                existingLock.UpdatedAt = DateTimeOffset.UtcNow;
                _logger.LogInformation("Updated existing parameter lock {LockId} for user {UserId}", existingLock.Id, userId);
            }
            else
            {
                // Create new lock
                var lockEntity = new ParameterLockEntity
                {
                    UserId = userId,
                    DeviceId = deviceId,
                    S3Key = s3Key,
                    ParamCount = paramKeys.Count,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = adminUserId,
                    IsActive = true
                };

                _context.Set<ParameterLockEntity>().Add(lockEntity);
                _logger.LogInformation("Created new parameter lock for user {UserId}, device {DeviceId}, {Count} params",
                    userId, deviceId ?? "global", paramKeys.Count);
            }

            await _context.SaveChangesAsync();
            return s3Key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create parameter lock for user {UserId}", userId);
            throw;
        }
    }

    public async Task<string> UpdateParamLockAsync(int lockId, List<string> paramKeys, Guid adminUserId)
    {
        if (paramKeys == null || paramKeys.Count == 0)
            throw new ArgumentException("At least one parameter must be locked", nameof(paramKeys));

        var lockEntity = await _context.Set<ParameterLockEntity>()
            .FirstOrDefaultAsync(l => l.Id == lockId);

        if (lockEntity == null)
            throw new ArgumentException($"Lock {lockId} not found", nameof(lockId));

        // Create new S3 key for updated lock
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var devicePart = string.IsNullOrWhiteSpace(lockEntity.DeviceId) ? "global" : SanitizeForS3(lockEntity.DeviceId);
        var s3Key = $"{S3_LOCK_PREFIX}/{lockEntity.UserId}/{devicePart}/{timestamp}.json";

        // Create JSON payload
        var lockData = new
        {
            lockedParams = paramKeys.Distinct().OrderBy(p => p).ToList(),
            userId = lockEntity.UserId.ToString(),
            deviceId = lockEntity.DeviceId,
            createdAt = lockEntity.CreatedAt,
            updatedAt = DateTime.UtcNow,
            updatedBy = adminUserId.ToString()
        };

        var json = JsonSerializer.Serialize(lockData, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        try
        {
            // Upload new version to S3
            await _s3Service.UploadJsonAsync(s3Key, json);
            _logger.LogInformation("Uploaded updated parameter lock to S3: {S3Key}", s3Key);

            // Delete old S3 object (cleanup, non-critical)
            try
            {
                await _s3Service.DeleteObjectAsync(lockEntity.S3Key);
            }
            catch (Exception deleteEx)
            {
                _logger.LogWarning(deleteEx, "Failed to clean up old S3 lock object: {S3Key}", lockEntity.S3Key);
            }

            // Update database record
            lockEntity.S3Key = s3Key;
            lockEntity.ParamCount = paramKeys.Count;
            lockEntity.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();
            _logger.LogInformation("Updated parameter lock {LockId}", lockId);

            return s3Key;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update parameter lock {LockId}", lockId);
            throw;
        }
    }

    public async Task<List<string>> GetLockedParamsAsync(Guid userId, string? deviceId)
    {
        try
        {
            // Try to get device-specific lock first, then fall back to user-wide lock
            var lockEntity = await _context.Set<ParameterLockEntity>()
                .Where(l => l.UserId == userId && l.IsActive)
                .Where(l => l.DeviceId == deviceId || (deviceId != null && l.DeviceId == null))
                .OrderByDescending(l => l.DeviceId != null) // Prefer device-specific locks
                .ThenByDescending(l => l.CreatedAt)
                .FirstOrDefaultAsync();

            if (lockEntity == null)
            {
                _logger.LogDebug("No parameter locks found for user {UserId}, device {DeviceId}", userId, deviceId);
                return new List<string>();
            }

            // Fetch JSON from S3
            var json = await _s3Service.GetObjectAsStringAsync(lockEntity.S3Key);
            var lockData = JsonSerializer.Deserialize<LockedParamsJson>(json);

            if (lockData?.LockedParams == null)
            {
                _logger.LogWarning("Invalid lock data in S3 for key {S3Key}", lockEntity.S3Key);
                return new List<string>();
            }

            _logger.LogInformation("Retrieved {Count} locked parameters for user {UserId}, device {DeviceId}",
                lockData.LockedParams.Count, userId, deviceId);

            return lockData.LockedParams;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get locked parameters for user {UserId}, device {DeviceId}", userId, deviceId);
            return new List<string>();
        }
    }

    public async Task<bool> IsParamLockedAsync(Guid userId, string? deviceId, string paramKey)
    {
        if (string.IsNullOrWhiteSpace(paramKey))
            return false;

        var lockedParams = await GetLockedParamsAsync(userId, deviceId);
        return lockedParams.Contains(paramKey, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> DeleteParamLockAsync(int lockId)
    {
        try
        {
            var lockEntity = await _context.Set<ParameterLockEntity>()
                .FirstOrDefaultAsync(l => l.Id == lockId);

            if (lockEntity == null)
            {
                _logger.LogWarning("Lock {LockId} not found for deletion", lockId);
                return false;
            }

            // Soft delete - mark as inactive
            lockEntity.IsActive = false;
            lockEntity.UpdatedAt = DateTimeOffset.UtcNow;

            await _context.SaveChangesAsync();

            // Optionally delete S3 object (commented out for audit trail)
            // await _s3Service.DeleteObjectAsync(lockEntity.S3Key);

            _logger.LogInformation("Deleted (deactivated) parameter lock {LockId}", lockId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete parameter lock {LockId}", lockId);
            return false;
        }
    }

    public async Task<List<ParamLockInfo>> GetUserLocksAsync(Guid userId)
    {
        try
        {
            var locks = await _context.Set<ParameterLockEntity>()
                .Where(l => l.UserId == userId && l.IsActive)
                .Include(l => l.User)
                .Include(l => l.CreatedByUser)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            var result = new List<ParamLockInfo>();

            foreach (var lockEntity in locks)
            {
                var info = new ParamLockInfo
                {
                    Id = lockEntity.Id,
                    UserId = lockEntity.UserId,
                    UserName = lockEntity.User?.FullName,
                    UserEmail = lockEntity.User?.Email,
                    DeviceId = lockEntity.DeviceId,
                    ParamCount = lockEntity.ParamCount,
                    CreatedAt = lockEntity.CreatedAt,
                    CreatedBy = lockEntity.CreatedBy,
                    CreatedByName = lockEntity.CreatedByUser?.FullName,
                    UpdatedAt = lockEntity.UpdatedAt,
                    IsActive = lockEntity.IsActive
                };

                // Optionally load actual param list from S3
                try
                {
                    var json = await _s3Service.GetObjectAsStringAsync(lockEntity.S3Key);
                    var lockData = JsonSerializer.Deserialize<LockedParamsJson>(json);
                    info.LockedParams = lockData?.LockedParams ?? new List<string>();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load locked params from S3 for lock {LockId}", lockEntity.Id);
                }

                result.Add(info);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get locks for user {UserId}", userId);
            return new List<ParamLockInfo>();
        }
    }

    public async Task<List<ParamLockInfo>> GetAllLocksAsync()
    {
        try
        {
            var locks = await _context.Set<ParameterLockEntity>()
                .Where(l => l.IsActive)
                .Include(l => l.User)
                .Include(l => l.CreatedByUser)
                .OrderByDescending(l => l.CreatedAt)
                .ToListAsync();

            var result = new List<ParamLockInfo>();

            foreach (var lockEntity in locks)
            {
                var info = new ParamLockInfo
                {
                    Id = lockEntity.Id,
                    UserId = lockEntity.UserId,
                    UserName = lockEntity.User?.FullName,
                    UserEmail = lockEntity.User?.Email,
                    DeviceId = lockEntity.DeviceId,
                    ParamCount = lockEntity.ParamCount,
                    CreatedAt = lockEntity.CreatedAt,
                    CreatedBy = lockEntity.CreatedBy,
                    CreatedByName = lockEntity.CreatedByUser?.FullName,
                    UpdatedAt = lockEntity.UpdatedAt,
                    IsActive = lockEntity.IsActive
                };

                // Load param list summary (first 5 params for overview)
                try
                {
                    var json = await _s3Service.GetObjectAsStringAsync(lockEntity.S3Key);
                    var lockData = JsonSerializer.Deserialize<LockedParamsJson>(json);
                    info.LockedParams = lockData?.LockedParams?.Take(5).ToList() ?? new List<string>();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load locked params from S3 for lock {LockId}", lockEntity.Id);
                }

                result.Add(info);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all parameter locks");
            return new List<ParamLockInfo>();
        }
    }

    private static string SanitizeForS3(string input)
    {
        return System.Text.RegularExpressions.Regex.Replace(input, @"[^a-zA-Z0-9_\-]", "_");
    }

    private class LockedParamsJson
    {
        public List<string> LockedParams { get; set; } = new();
    }
}
