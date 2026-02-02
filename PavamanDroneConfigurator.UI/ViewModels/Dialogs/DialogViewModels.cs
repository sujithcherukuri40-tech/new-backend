using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PavamanDroneConfigurator.UI.ViewModels.Dialogs;

/// <summary>
/// Base view model for all dialog types
/// </summary>
public partial class DialogViewModelBase : ViewModelBase
{
    [ObservableProperty]
    private bool _isVisible;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    public virtual void Show(string title, string message)
    {
        Title = title;
        Message = message;
        IsVisible = true;
    }

    [RelayCommand]
    public virtual void Hide()
    {
        IsVisible = false;
    }
}

/// <summary>
/// Connecting dialog with progress steps
/// </summary>
public partial class ConnectingDialogViewModel : DialogViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepText))]
    private ConnectingStep _currentStep = ConnectingStep.EstablishingLink;

    [ObservableProperty]
    private bool _isSpinning = true;

    public string StepText => CurrentStep switch
    {
        ConnectingStep.EstablishingLink => "Establishing link...",
        ConnectingStep.WaitingForHeartbeat => "Waiting for heartbeat...",
        ConnectingStep.SyncingParameters => "Syncing parameters...",
        ConnectingStep.Finalizing => "Finalizing connection...",
        _ => "Connecting..."
    };

    public void SetStep(ConnectingStep step)
    {
        CurrentStep = step;
    }
}

public enum ConnectingStep
{
    EstablishingLink,
    WaitingForHeartbeat,
    SyncingParameters,
    Finalizing
}

/// <summary>
/// Success dialog for connection established
/// </summary>
public partial class ConnectionSuccessDialogViewModel : DialogViewModelBase
{
    [ObservableProperty]
    private string _firmwareVersion = "Unknown";

    [ObservableProperty]
    private string _vehicleType = "Unknown";

    [ObservableProperty]
    private string _connectionType = "Unknown";

    [ObservableProperty]
    private int _parameterCount;

    public void Show(string firmware, string vehicle, string connection, int parameters)
    {
        FirmwareVersion = firmware;
        VehicleType = vehicle;
        ConnectionType = connection;
        ParameterCount = parameters;
        Title = "Connected Successfully";
        Message = "Your drone is now connected and ready for configuration.";
        IsVisible = true;
    }
}

/// <summary>
/// Error dialog with retry option
/// </summary>
public partial class ErrorDialogViewModel : DialogViewModelBase
{
    [ObservableProperty]
    private string _errorCode = string.Empty;

    [ObservableProperty]
    private bool _canRetry = true;

    public event EventHandler? RetryRequested;
    public event EventHandler? CloseRequested;

    public void Show(string title, string message, string? errorCode = null, bool canRetry = true)
    {
        Title = title;
        Message = message;
        ErrorCode = errorCode ?? string.Empty;
        CanRetry = canRetry;
        IsVisible = true;
    }

    [RelayCommand]
    public void Retry()
    {
        Hide();
        RetryRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void Close()
    {
        Hide();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Loading overlay for parameter operations
/// </summary>
public partial class LoadingOverlayViewModel : DialogViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepText))]
    private LoadingStep _currentStep = LoadingStep.Requesting;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = "0 / 0";

    public string StepText => CurrentStep switch
    {
        LoadingStep.Requesting => "Requesting parameters...",
        LoadingStep.Downloading => "Downloading parameters...",
        LoadingStep.Validating => "Validating data...",
        LoadingStep.Applying => "Applying configuration...",
        LoadingStep.Complete => "Complete!",
        _ => "Loading..."
    };

    public void UpdateProgress(int current, int total, LoadingStep step)
    {
        CurrentStep = step;
        ProgressValue = total > 0 ? (current * 100.0 / total) : 0;
        ProgressText = $"{current} / {total}";
    }
}

public enum LoadingStep
{
    Requesting,
    Downloading,
    Validating,
    Applying,
    Complete
}
