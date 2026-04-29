namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Service for managing parameter locks.
/// Allows admins to lock specific parameters for users/devices, preventing modifications.
/// </summary>
public interface IParamLockService
{
    /// <summary>
    /// Create a new parameter lock for a user/device.
    /// Uploads locked parameters to S3 and stores metadata in database.
    /// </summary>
    /// <param name="userId">User ID to lock parameters for</param>
    /// <param name="deviceId">Device ID (null for user-wide lock)</param>
    /// <param name="paramKeys">List of parameter keys to lock</param>
    /// <param name="adminUserId">Admin user creating the lock</param>
    /// <param name="paramValues">Optional values per parameter for drift detection on user login</param>
    /// <returns>S3 key where the lock data is stored</returns>
    Task<string> CreateParamLockAsync(Guid userId, string? deviceId, List<string> paramKeys, Guid adminUserId, Dictionary<string, float>? paramValues = null);

    /// <summary>
    /// Update an existing parameter lock.
    /// </summary>
    /// <param name="lockId">Lock ID to update</param>
    /// <param name="paramKeys">Updated list of parameter keys</param>
    /// <param name="adminUserId">Admin user updating the lock</param>
    /// <param name="paramValues">Optional values per parameter for drift detection on user login</param>
    /// <returns>Updated S3 key</returns>
    Task<string> UpdateParamLockAsync(int lockId, List<string> paramKeys, Guid adminUserId, Dictionary<string, float>? paramValues = null);

    /// <summary>
    /// Get locked parameters for a specific user/device.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="deviceId">Device ID (null for user-wide locks)</param>
    /// <returns>List of locked parameter keys</returns>
    Task<List<string>> GetLockedParamsAsync(Guid userId, string? deviceId);

    /// <summary>
    /// Check if a specific parameter is locked for a user/device.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="deviceId">Device ID (null for user-wide check)</param>
    /// <param name="paramKey">Parameter key to check</param>
    /// <returns>True if parameter is locked</returns>
    Task<bool> IsParamLockedAsync(Guid userId, string? deviceId, string paramKey);

    /// <summary>
    /// Delete a parameter lock.
    /// </summary>
    /// <param name="lockId">Lock ID to delete</param>
    /// <returns>True if deleted successfully</returns>
    Task<bool> DeleteParamLockAsync(int lockId);

    /// <summary>
    /// Get all parameter locks for a user.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <returns>List of lock metadata</returns>
    Task<List<ParamLockInfo>> GetUserLocksAsync(Guid userId);

    /// <summary>
    /// Get all active parameter locks (admin view).
    /// </summary>
    /// <returns>List of all lock metadata</returns>
    Task<List<ParamLockInfo>> GetAllLocksAsync();
}

/// <summary>
/// Parameter lock information (metadata).
/// </summary>
public class ParamLockInfo
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public string? UserEmail { get; set; }
    public string? DeviceId { get; set; }
    public int ParamCount { get; set; }
    public List<string> LockedParams { get; set; } = new();
    /// <summary>Locked values per parameter name. Populated from S3 when available.</summary>
    public Dictionary<string, float> LockedParamValues { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsActive { get; set; }
}
