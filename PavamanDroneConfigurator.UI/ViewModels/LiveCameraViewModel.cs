using System;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the standalone Live Camera modal window.
/// Manages video stream lifecycle, connection-aware error states,
/// and live frame rendering.
/// </summary>
public partial class LiveCameraViewModel : ViewModelBase
{
    private readonly IConnectionService _connectionService;
    private readonly IVideoStreamingService _videoStreamingService;

    #region Observable Properties

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isVideoStreaming;

    [ObservableProperty]
    private bool _isStreamLoading;

    [ObservableProperty]
    private bool _hasStreamError;

    [ObservableProperty]
    private string _streamErrorMessage = string.Empty;

    [ObservableProperty]
    private string _videoStreamUrl = "rtsp://192.168.1.1:8554/live";

    [ObservableProperty]
    private string _videoStreamStatus = "Not connected";

    [ObservableProperty]
    private string _statusText = "Ready";

    /// <summary>Current video frame decoded from the stream.</summary>
    [ObservableProperty]
    private Bitmap? _currentVideoFrame;

    #endregion

    public LiveCameraViewModel(
        IConnectionService connectionService,
        IVideoStreamingService videoStreamingService)
    {
        _connectionService = connectionService;
        _videoStreamingService = videoStreamingService;

        _videoStreamingService.StreamingStateChanged += OnStreamingStateChanged;
        _videoStreamingService.StatusChanged += OnStatusChanged;
        _videoStreamingService.FrameReceived += OnFrameReceived;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;

        IsConnected = _connectionService.IsConnected;
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            if (!connected && IsVideoStreaming)
            {
                HasStreamError = true;
                StreamErrorMessage = "Drone disconnected";
                VideoStreamStatus = "MAVLink connection lost.";
            }
        });
    }

    private void OnStreamingStateChanged(object? sender, bool isStreaming)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsVideoStreaming = isStreaming;
            if (isStreaming)
            {
                IsStreamLoading = false;
                HasStreamError = false;
                StreamErrorMessage = string.Empty;
                StatusText = "● LIVE";
            }
            else if (!HasStreamError)
            {
                StatusText = "Stream stopped";
            }
        });
    }

    private void OnStatusChanged(object? sender, string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            VideoStreamStatus = status;

            if (status.StartsWith("Error:", StringComparison.OrdinalIgnoreCase))
            {
                IsStreamLoading = false;
                HasStreamError = true;
                StreamErrorMessage = status;
                StatusText = "Error";
            }
            else if (status.StartsWith("Connecting", StringComparison.OrdinalIgnoreCase))
            {
                IsStreamLoading = true;
                HasStreamError = false;
                StreamErrorMessage = string.Empty;
                StatusText = "Connecting…";
            }
        });
    }

    private void OnFrameReceived(object? sender, byte[] frameData)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                using var ms = new System.IO.MemoryStream(frameData);
                var bitmap = new Bitmap(ms);
                var oldFrame = CurrentVideoFrame;
                CurrentVideoFrame = bitmap;
                oldFrame?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LiveCameraWindow] Frame decode error: {ex.Message}");
            }
        });
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task StartStreamAsync()
    {
        if (!IsConnected)
        {
            HasStreamError = true;
            StreamErrorMessage = "Drone not connected";
            VideoStreamStatus = "MAVLink connection is unavailable. Connect to drone first.";
            return;
        }

        HasStreamError = false;
        StreamErrorMessage = string.Empty;
        IsStreamLoading = true;
        _videoStreamingService.StreamUrl = VideoStreamUrl;
        await _videoStreamingService.StartAsync();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task StopStreamAsync()
    {
        await _videoStreamingService.StopAsync();
        IsStreamLoading = false;
        StatusText = "Stream stopped";
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ToggleStreamAsync()
    {
        if (IsVideoStreaming || IsStreamLoading)
            await StopStreamAsync();
        else
            await StartStreamAsync();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task RetryStreamAsync()
    {
        await StartStreamAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _videoStreamingService.StreamingStateChanged -= OnStreamingStateChanged;
            _videoStreamingService.StatusChanged -= OnStatusChanged;
            _videoStreamingService.FrameReceived -= OnFrameReceived;
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            CurrentVideoFrame?.Dispose();
        }
        base.Dispose(disposing);
    }
}
