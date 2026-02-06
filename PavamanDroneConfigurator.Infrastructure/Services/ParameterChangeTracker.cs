using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Service for tracking parameter changes and logging them to S3
/// </summary>
public class ParameterChangeTracker
{
    private readonly AwsS3Service _s3Service;
    private readonly ILogger<ParameterChangeTracker> _logger;
    private readonly Dictionary<string, float> _parameterSnapshots = new();
    private readonly List<ParameterChange> _pendingChanges = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    
    public ParameterChangeTracker(AwsS3Service s3Service, ILogger<ParameterChangeTracker> logger)
    {
        _s3Service = s3Service;
        _logger = logger;
    }
    
    /// <summary>
    /// Track a parameter change (delta only)
    /// </summary>
    public async Task TrackParameterChangeAsync(string paramName, float oldValue, float newValue)
    {
        await _lock.WaitAsync();
        try
        {
            // Only track if value actually changed
            if (Math.Abs(oldValue - newValue) < 0.0001f)
                return;
            
            var change = new ParameterChange
            {
                ParamName = paramName,
                OldValue = oldValue,
                NewValue = newValue,
                ChangedAt = DateTime.UtcNow
            };
            
            _pendingChanges.Add(change);
            _logger.LogDebug("Tracked parameter change: {Param} {Old} -> {New}", paramName, oldValue, newValue);
        }
        finally
        {
            _lock.Release();
        }
    }
    
    /// <summary>
    /// Flush pending changes to S3
    /// </summary>
    public async Task FlushChangesAsync(string userId, string fcId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_pendingChanges.Count == 0)
            {
                _logger.LogDebug("No parameter changes to flush");
                return;
            }
            
            _logger.LogInformation("Flushing {Count} parameter changes to S3", _pendingChanges.Count);
            
            await _s3Service.AppendParameterChangesAsync(userId, fcId, _pendingChanges.ToList(), cancellationToken);
            
            _pendingChanges.Clear();
            _logger.LogInformation("Parameter changes flushed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to flush parameter changes to S3");
            // Don't throw - keep changes in memory for retry
        }
        finally
        {
            _lock.Release();
        }
    }
    
    /// <summary>
    /// Take a snapshot of current parameters (for comparison)
    /// </summary>
    public void TakeSnapshot(Dictionary<string, float> parameters)
    {
        _lock.Wait();
        try
        {
            _parameterSnapshots.Clear();
            foreach (var kvp in parameters)
            {
                _parameterSnapshots[kvp.Key] = kvp.Value;
            }
            _logger.LogDebug("Parameter snapshot taken: {Count} parameters", parameters.Count);
        }
        finally
        {
            _lock.Release();
        }
    }
    
    /// <summary>
    /// Compare current parameters with snapshot and track changes
    /// </summary>
    public async Task CompareAndTrackChangesAsync(Dictionary<string, float> currentParameters)
    {
        await _lock.WaitAsync();
        try
        {
            foreach (var kvp in currentParameters)
            {
                if (_parameterSnapshots.TryGetValue(kvp.Key, out var oldValue))
                {
                    if (Math.Abs(oldValue - kvp.Value) >= 0.0001f)
                    {
                        var change = new ParameterChange
                        {
                            ParamName = kvp.Key,
                            OldValue = oldValue,
                            NewValue = kvp.Value,
                            ChangedAt = DateTime.UtcNow
                        };
                        _pendingChanges.Add(change);
                        
                        // Update snapshot
                        _parameterSnapshots[kvp.Key] = kvp.Value;
                    }
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    
    /// <summary>
    /// Get pending changes count
    /// </summary>
    public int GetPendingChangesCount()
    {
        _lock.Wait();
        try
        {
            return _pendingChanges.Count;
        }
        finally
        {
            _lock.Release();
        }
    }
}
