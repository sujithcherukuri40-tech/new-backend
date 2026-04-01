namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Service for managing a drone camera video stream (RTSP / UDP).
/// The implementation is designed to run off the UI thread; use the
/// events to receive frame/state notifications on the UI thread.
/// </summary>
public interface IVideoStreamingService : IDisposable
{
    /// <summary>Current RTSP or UDP stream URL (e.g. rtsp://192.168.1.1:8554/live).</summary>
    string StreamUrl { get; set; }

    /// <summary>True while the stream is actively connected and delivering frames.</summary>
    bool IsStreaming { get; }

    /// <summary>Human-readable status message (e.g. "Connected", "Disconnected", "Error: …").</summary>
    string StatusMessage { get; }

    /// <summary>Raised (on any thread) when <see cref="IsStreaming"/> changes.</summary>
    event EventHandler<bool>? StreamingStateChanged;

    /// <summary>Raised (on any thread) when <see cref="StatusMessage"/> changes.</summary>
    event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Raised when a new video frame is available.
    /// The byte[] contains a JPEG-encoded frame; consumers should decode on the UI thread.
    /// </summary>
    event EventHandler<byte[]>? FrameReceived;

    /// <summary>Start connecting and streaming from <see cref="StreamUrl"/>.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stop the stream and release network resources.</summary>
    Task StopAsync();
}
