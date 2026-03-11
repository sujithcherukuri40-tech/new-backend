using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using System.Collections.Concurrent;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Parameter service that downloads parameters from drone like Mission Planner.
/// Uses aggressive retry strategy for missing parameters.
/// Enriches parameters with metadata immediately upon receipt.
/// </summary>
public class ParameterService : IParameterService
{
    private readonly ILogger<ParameterService> _logger;
    private readonly IConnectionService _connectionService;
    private readonly IParameterMetadataService _metadataService;
    private readonly ConcurrentDictionary<string, DroneParameter> _parameters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<DroneParameter>> _pendingWrites = new();
    private readonly HashSet<int> _receivedIndices = new();
    private readonly object _lock = new();
    
    private TaskCompletionSource<bool>? _downloadComplete;
    private CancellationTokenSource? _downloadCts;
    private ushort? _expectedCount;
    private volatile bool _downloading;
    private volatile bool _downloadDone;
    private int _received;

    public event EventHandler<string>? ParameterUpdated;
    public event EventHandler? ParameterDownloadStarted;
    public event EventHandler<bool>? ParameterDownloadCompleted;
    public event EventHandler? ParameterDownloadProgressChanged;

    public bool IsParameterDownloadInProgress => _downloading;
    public bool IsParameterDownloadComplete => _downloadDone;
    public int ReceivedParameterCount => _received;
    public int? ExpectedParameterCount => _expectedCount;

    public ParameterService(
        ILogger<ParameterService> logger, 
        IConnectionService connectionService,
        IParameterMetadataService metadataService)
    {
        _logger = logger;
        _connectionService = connectionService;
        _metadataService = metadataService;
        _connectionService.ParamValueReceived += OnParamReceived;
        _connectionService.ConnectionStateChanged += OnConnectionChanged;
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        if (!connected) Reset();
    }

    private void OnParamReceived(object? sender, MavlinkParamValueEventArgs e)
    {
        // ? OPTIMIZATION: Create parameter with value from drone
        var param = new DroneParameter
        {
            Name = e.Parameter.Name,
            Value = e.Parameter.Value,
            OriginalValue = e.Parameter.Value
        };
        
        // ? OPTIMIZED: Enrich with metadata asynchronously to avoid blocking
        // This prevents UI freezing during large parameter downloads
        Task.Run(() =>
        {
            _metadataService.EnrichParameter(param);
            
            // If no description from metadata, provide a default
            if (string.IsNullOrEmpty(param.Description))
            {
                param.Description = $"Parameter {param.Name}";
            }
        });
        
        _parameters[param.Name] = param;
        
        // ? OPTIMIZED: Reduced logging verbosity - only log every 100 params
        if (_received % 100 == 0 || _received < 10)
        {
            _logger.LogDebug("Received param: {Name} = {Value} [{Index}/{Count}]", 
                param.Name, param.Value, e.ParamIndex, e.ParamCount);
        }

        bool isNew;
        lock (_lock)
        {
            if (!_expectedCount.HasValue && e.ParamCount > 0)
            {
                _expectedCount = e.ParamCount;
                _logger.LogInformation("Total parameter count from drone: {Count}", e.ParamCount);
            }

            isNew = _receivedIndices.Add(e.ParamIndex);
            _received = _receivedIndices.Count;

            // Check completion
            if (_expectedCount.HasValue && _received >= _expectedCount.Value)
            {
                _downloadComplete?.TrySetResult(true);
            }
        }

        if (isNew)
        {
            ParameterUpdated?.Invoke(this, param.Name);
            
            // ? OPTIMIZED: Update progress less frequently to reduce UI overhead
            if (_received % 100 == 0 || (_expectedCount.HasValue && _received >= _expectedCount.Value - 10))
            {
                ParameterDownloadProgressChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        // Handle pending write confirmations
        if (_pendingWrites.TryRemove(param.Name, out var tcs))
        {
            tcs.TrySetResult(param);
        }
    }

    public Task<List<DroneParameter>> GetAllParametersAsync()
    {
        var list = _parameters.Values.OrderBy(p => p.Name).ToList();
        _logger.LogInformation("GetAllParametersAsync returning {Count} parameters", list.Count);
        return Task.FromResult(list);
    }

    public Task<DroneParameter?> GetParameterAsync(string name)
    {
        _parameters.TryGetValue(name, out var param);
        return Task.FromResult(param);
    }

    public async Task<bool> SetParameterAsync(string name, float value)
    {
        if (!_connectionService.IsConnected) return false;

        var tcs = new TaskCompletionSource<DroneParameter>();
        _pendingWrites[name] = tcs;

        _connectionService.SendParamSet(new ParameterWriteRequest(name, value));

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(3000));
        _pendingWrites.TryRemove(name, out _);

        return completed == tcs.Task;
    }

    public async Task RefreshParametersAsync()
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning("Cannot refresh: not connected");
            return;
        }

        // Stop any existing download
        _downloadCts?.Cancel();
        
        // Reset state
        _parameters.Clear();
        lock (_lock)
        {
            _receivedIndices.Clear();
            _expectedCount = null;
        }
        _received = 0;
        _downloading = true;
        _downloadDone = false;
        _downloadComplete = new TaskCompletionSource<bool>();
        _downloadCts = new CancellationTokenSource();

        _logger.LogInformation("=== Starting OPTIMIZED parameter download from drone ===");
        
        ParameterDownloadStarted?.Invoke(this, EventArgs.Empty);
        ParameterDownloadProgressChanged?.Invoke(this, EventArgs.Empty);

        try
        {
            var ct = _downloadCts.Token;

            // ? OPTIMIZATION: Send initial PARAM_REQUEST_LIST
            _logger.LogInformation("Sending PARAM_REQUEST_LIST...");
            _connectionService.SendParamRequestList();

            // ? OPTIMIZED: Reduced initial wait from 3000ms to 1500ms
            await Task.Delay(1500, ct);
            
            _logger.LogInformation("After initial wait: received {Count} parameters", _received);

            // ? OPTIMIZED: Aggressive retry with parallel parameter requests
            for (int retry = 0; retry < 5 && !ct.IsCancellationRequested; retry++) // ? Reduced from 10 to 5 retries
            {
                int expected;
                int received;
                List<int> missing;
                bool hasExpected;
                bool isComplete;

                lock (_lock)
                {
                    hasExpected = _expectedCount.HasValue;
                    expected = _expectedCount ?? 0;
                    received = _receivedIndices.Count;
                    isComplete = hasExpected && received >= expected;
                    
                    if (hasExpected && !isComplete)
                    {
                        missing = Enumerable.Range(0, expected)
                            .Where(i => !_receivedIndices.Contains(i))
                            .ToList();
                    }
                    else
                    {
                        missing = new List<int>();
                    }
                }

                if (isComplete)
                {
                    _logger.LogInformation("All {Count} parameters received from drone!", expected);
                    break;
                }

                if (!hasExpected)
                {
                    // No response yet - resend request
                    _logger.LogWarning("No parameters received yet, resending PARAM_REQUEST_LIST (attempt {N}/5)", retry + 1);
                    _connectionService.SendParamRequestList();
                    await Task.Delay(1500, ct); // ? Reduced from 3000ms
                    continue;
                }

                if (missing.Count == 0)
                {
                    _logger.LogInformation("Download complete: {Received}/{Expected}", received, expected);
                    break;
                }

                _logger.LogInformation("Retry {N}: {Received}/{Expected} params, requesting {Missing} missing", 
                    retry + 1, received, expected, missing.Count);

                // ? OPTIMIZED: Request missing parameters in LARGER chunks for faster download
                foreach (var chunk in missing.Chunk(10)) // ? Increased from 5 to 10 params per batch
                {
                    if (ct.IsCancellationRequested) break;

                    // ? PARALLEL REQUEST: Send all requests in chunk without delay
                    foreach (var idx in chunk)
                    {
                        _connectionService.SendParamRequestRead((ushort)idx);
                    }
                    await Task.Delay(100, ct); // ? Reduced from 200ms to 100ms
                }

                // ? OPTIMIZED: Reduced wait time for responses
                await Task.Delay(1000, ct); // ? Reduced from 2000ms to 1000ms
                
                // ? OPTIMIZATION: Update progress more frequently for better UX
                ParameterDownloadProgressChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Parameter download cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during parameter download");
        }

        // Finalize
        _downloading = false;
        _downloadDone = _received > 0;

        _logger.LogInformation("=== Parameter download finished: {Received} parameters from drone ===", _received);
        
        // ? OPTIMIZATION: Don't log samples - saves processing time
        // Removed sample logging for faster completion

        ParameterDownloadCompleted?.Invoke(this, _downloadDone);
        ParameterDownloadProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        _logger.LogInformation("ParameterService Reset called");
        _downloadCts?.Cancel();
        _parameters.Clear();
        lock (_lock)
        {
            _receivedIndices.Clear();
            _expectedCount = null;
        }
        _received = 0;
        _downloading = false;
        _downloadDone = false;
        _downloadComplete?.TrySetCanceled();
        _downloadComplete = null;

        foreach (var tcs in _pendingWrites.Values)
            tcs.TrySetCanceled();
        _pendingWrites.Clear();

        ParameterDownloadProgressChanged?.Invoke(this, EventArgs.Empty);
    }
    
    /// <inheritdoc/>
    public void ClearParameters()
    {
        _logger.LogInformation("ParameterService ClearParameters called - preparing for fresh connection");
        
        // Cancel any ongoing download
        _downloadCts?.Cancel();
        
        // Clear all cached parameters
        _parameters.Clear();
        
        lock (_lock)
        {
            _receivedIndices.Clear();
            _expectedCount = null;
        }
        
        _received = 0;
        _downloading = false;
        _downloadDone = false;
        _downloadComplete?.TrySetCanceled();
        _downloadComplete = null;

        // Clear pending writes
        foreach (var tcs in _pendingWrites.Values)
            tcs.TrySetCanceled();
        _pendingWrites.Clear();

        // Notify listeners that state has been cleared
        ParameterDownloadProgressChanged?.Invoke(this, EventArgs.Empty);
        
        _logger.LogInformation("ParameterService state cleared - ready for fresh parameter download");
    }
}
