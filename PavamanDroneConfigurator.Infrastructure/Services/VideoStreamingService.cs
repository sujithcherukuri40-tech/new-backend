using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Video streaming service that connects to an RTSP or UDP camera feed.
///
/// This implementation is a clean-architecture stub that manages stream
/// lifecycle state.  Actual frame decoding can be wired in by replacing
/// the <c>RunStreamLoopAsync</c> method with a LibVLCSharp / FFmpeg pipe
/// reader once the relevant native library is available in the deployment.
///
/// The service is intentionally decoupled from any UI framework so that
/// the same class works in unit tests and non-Avalonia hosts.
/// </summary>
public sealed class VideoStreamingService : IVideoStreamingService
{
    private readonly ILogger<VideoStreamingService> _logger;
    private readonly object _lock = new();

    private string _streamUrl = "rtsp://192.168.1.1:8554/live";
    private bool _isStreaming;
    private string _statusMessage = "Idle";
    private bool _disposed;

    private CancellationTokenSource? _streamCts;
    private Task? _streamTask;

    public VideoStreamingService(ILogger<VideoStreamingService> logger)
    {
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // IVideoStreamingService
    // -----------------------------------------------------------------------

    public string StreamUrl
    {
        get { lock (_lock) { return _streamUrl; } }
        set { lock (_lock) { _streamUrl = value; } }
    }

    public bool IsStreaming
    {
        get { lock (_lock) { return _isStreaming; } }
        private set
        {
            bool changed;
            lock (_lock)
            {
                changed = _isStreaming != value;
                _isStreaming = value;
            }
            if (changed)
            {
                StreamingStateChanged?.Invoke(this, value);
            }
        }
    }

    public string StatusMessage
    {
        get { lock (_lock) { return _statusMessage; } }
        private set
        {
            lock (_lock) { _statusMessage = value; }
            StatusChanged?.Invoke(this, value);
        }
    }

    public event EventHandler<bool>? StreamingStateChanged;
    public event EventHandler<string>? StatusChanged;
    public event EventHandler<byte[]>? FrameReceived;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(VideoStreamingService));

        // Stop any existing stream before starting a new one.
        await StopAsync().ConfigureAwait(false);

        string url;
        lock (_lock) { url = _streamUrl; }

        _logger.LogInformation("[VideoStream] Connecting to {Url}", url);
        StatusMessage = $"Connecting to {url}…";

        _streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _streamTask = Task.Run(() => RunStreamLoopAsync(url, _streamCts.Token), _streamCts.Token);
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? task;
        lock (_lock)
        {
            cts = _streamCts;
            task = _streamTask;
            _streamCts = null;
            _streamTask = null;
        }

        if (cts != null)
        {
            await cts.CancelAsync().ConfigureAwait(false);
            if (task != null)
            {
                try { await task.ConfigureAwait(false); }
                catch (OperationCanceledException) { /* expected */ }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[VideoStream] Stream task ended with error on stop");
                }
            }
            cts.Dispose();
        }

        IsStreaming = false;
        StatusMessage = "Disconnected";
        _logger.LogInformation("[VideoStream] Stream stopped");
    }

    // -----------------------------------------------------------------------
    // Stream loop
    // -----------------------------------------------------------------------

    /// <summary>
    /// Main stream loop running on a background thread.
    ///
    /// To integrate a real video library replace the body of this method:
    ///   • LibVLCSharp: create a <c>LibVLC</c> instance, open a <c>MediaPlayer</c>
    ///     with the URL, enable the video callback API and forward raw frames here.
    ///   • FFmpeg pipe: start an <c>ffmpeg</c> process with mjpeg output on stdout
    ///     and read JPEG frames from its stdout stream.
    ///
    /// For now the loop simulates a connected-but-no-frame state so that the
    /// rest of the UI wiring (start/stop, status labels) can be verified.
    /// </summary>
    private async Task RunStreamLoopAsync(string url, CancellationToken ct)
    {
        try
        {
            // Simulate network handshake delay.
            await Task.Delay(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);

            IsStreaming = true;
            StatusMessage = $"Connected – {url}";
            _logger.LogInformation("[VideoStream] Stream active: {Url}", url);

            // Keep-alive loop: in a real implementation this would be
            // replaced by a frame-read / decode loop.
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
                _logger.LogTrace("[VideoStream] Stream heartbeat");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[VideoStream] Stream loop cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VideoStream] Stream loop error for {Url}", url);
            IsStreaming = false;
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsStreaming = false;
        }
    }

    // -----------------------------------------------------------------------
    // IDisposable
    // -----------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = StopAsync();
    }
}
