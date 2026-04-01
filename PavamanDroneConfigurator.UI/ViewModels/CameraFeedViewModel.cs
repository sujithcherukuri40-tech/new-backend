using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the live camera feed panel.
/// Manages stream URI configuration and playback state.
/// Actual video decoding requires LibVLCSharp (LibVLCSharp.Avalonia) to be present at runtime.
/// </summary>
public partial class CameraFeedViewModel : ObservableObject, System.IDisposable
{
    [ObservableProperty]
    private string _streamUri = string.Empty;

    [ObservableProperty]
    private bool _isStreaming;

    [ObservableProperty]
    private string _streamStatus = "Not connected";

    [ObservableProperty]
    private string _streamResolution = "--";

    [ObservableProperty]
    private int _streamLatencyMs;

    /// <summary>
    /// Start streaming from the configured URI.
    /// </summary>
    [RelayCommand]
    private void StartStream()
    {
        if (string.IsNullOrWhiteSpace(StreamUri))
        {
            StreamStatus = "No stream URI configured";
            return;
        }

        IsStreaming = true;
        StreamStatus = $"Connecting to {StreamUri}";
        // NOTE: Actual video decode via LibVLCSharp requires the native VLC libraries.
        // Wire up LibVLCSharp MediaPlayer here when the native packages are available.
    }

    /// <summary>
    /// Stop the current stream.
    /// </summary>
    [RelayCommand]
    private void StopStream()
    {
        IsStreaming = false;
        StreamStatus = "Stopped";
    }

    public void Dispose()
    {
        IsStreaming = false;
    }
}
