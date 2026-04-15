using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Extension/wrapper for parameter lock enforcement in UI context.
/// Since UI doesn't have direct access to API services, this provides a simple check mechanism.
/// In production, the UI would call an API endpoint to check locks.
/// </summary>
public class ParameterLockValidator
{
    private readonly HashSet<string> _lockedParams = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private Guid? _currentUserId;
    private string? _currentDeviceId;
    private DateTime _lastRefresh = DateTime.MinValue;
    private const int CACHE_DURATION_MINUTES = 5;

    /// <summary>
    /// Update the locked parameters cache.
    /// In production, this would fetch from API.
    /// </summary>
    public void UpdateLockedParameters(Guid userId, string? deviceId, List<string> lockedParams)
    {
        lock (_lock)
        {
            _currentUserId = userId;
            _currentDeviceId = deviceId;
            _lockedParams.Clear();
            foreach (var param in lockedParams)
            {
                _lockedParams.Add(param);
            }
            _lastRefresh = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Check if a parameter is locked.
    /// </summary>
    public bool IsParameterLocked(string parameterName)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
            return false;

        lock (_lock)
        {
            return _lockedParams.Contains(parameterName);
        }
    }

    /// <summary>
    /// Get all currently locked parameters.
    /// </summary>
    public List<string> GetLockedParameters()
    {
        lock (_lock)
        {
            return _lockedParams.ToList();
        }
    }

    /// <summary>
    /// Check if cache is expired.
    /// </summary>
    public bool IsCacheExpired => (DateTime.UtcNow - _lastRefresh).TotalMinutes > CACHE_DURATION_MINUTES;

    /// <summary>
    /// Clear the cache.
    /// </summary>
    public void Clear()
    {
        lock (_lock)
        {
            _lockedParams.Clear();
            _currentUserId = null;
            _currentDeviceId = null;
            _lastRefresh = DateTime.MinValue;
        }
    }
}
