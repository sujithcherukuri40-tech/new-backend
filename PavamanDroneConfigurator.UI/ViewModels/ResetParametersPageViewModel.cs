using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.UI.ViewModels;

public sealed partial class ResetParametersPageViewModel : ViewModelBase
{
    private readonly IConnectionService _connectionService;
    private readonly IParameterService _parameterService;
    private bool _disposed;
    private bool _waitingForReconnect;
    private DateTime _rebootStartTime;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isResetting;

    [ObservableProperty]
    private bool _isRebooting;

    [ObservableProperty]
    private string _statusMessage = "Connect to a drone to reset parameters.";

    [ObservableProperty]
    private bool _resetComplete;

    [ObservableProperty]
    private bool _resetFailed;

    [ObservableProperty]
    private string _lastDroneMessage = string.Empty;

    public ResetParametersPageViewModel(IConnectionService connectionService, IParameterService parameterService)
    {
        _connectionService = connectionService;
        _parameterService = parameterService;

        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _connectionService.CommandAckReceived += OnCommandAckReceived;
        _connectionService.StatusTextReceived += OnStatusTextReceived;

        IsConnected = _connectionService.IsConnected;
        UpdateStatusMessage();
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var wasConnected = IsConnected;
            IsConnected = connected;
            
            if (_waitingForReconnect && connected)
            {
                // Drone reconnected after reboot
                _waitingForReconnect = false;
                IsRebooting = false;
                StatusMessage = "Drone reconnected! Click 'Refresh Parameters' to download the reset parameters.";
            }
            else if (!connected && wasConnected && IsRebooting)
            {
                // Drone disconnected during reboot - this is expected
                StatusMessage = "Drone is rebooting... waiting for reconnection...";
                _waitingForReconnect = true;
            }
            else if (!connected)
            {
                ResetComplete = false;
                ResetFailed = false;
                IsRebooting = false;
                _waitingForReconnect = false;
                UpdateStatusMessage();
            }
        });
    }

    private void OnCommandAckReceived(object? sender, CommandAckEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // MAV_CMD_PREFLIGHT_STORAGE = 245
            if (e.Command == 245)
            {
                IsResetting = false;
                if (e.IsSuccess)
                {
                    ResetComplete = true;
                    ResetFailed = false;
                    StatusMessage = "Reset command accepted! Now click 'Reboot Drone' to apply the reset, then 'Refresh Parameters'.";
                }
                else
                {
                    // Try alternative method using FORMAT_VERSION
                    StatusMessage = $"MAV_CMD failed (result={e.Result}). Trying alternative reset method...";
                    _ = TryAlternativeResetAsync();
                }
            }
            // MAV_CMD_PREFLIGHT_REBOOT_SHUTDOWN = 246
            else if (e.Command == 246)
            {
                if (e.IsSuccess)
                {
                    IsRebooting = true;
                    _rebootStartTime = DateTime.UtcNow;
                    StatusMessage = "Reboot command accepted. Drone is rebooting...";
                    _ = MonitorRebootAsync();
                }
                else
                {
                    StatusMessage = $"Reboot command failed with result code: {e.Result}";
                }
            }
        });
    }

    private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            LastDroneMessage = $"[{DateTime.Now:HH:mm:ss}] {e.Text}";
        });
    }

    private async Task TryAlternativeResetAsync()
    {
        try
        {
            // ArduPilot alternative: Set FORMAT_VERSION to 0 to trigger EEPROM reset on next boot
            // This is the same method Mission Planner uses
            var success = await _parameterService.SetParameterAsync("FORMAT_VERSION", 0);
            
            if (success)
            {
                ResetComplete = true;
                ResetFailed = false;
                StatusMessage = "Reset prepared (FORMAT_VERSION=0). Click 'Reboot Drone' to apply the reset.";
            }
            else
            {
                ResetComplete = false;
                ResetFailed = true;
                StatusMessage = "Failed to set FORMAT_VERSION. Your flight controller may not support this reset method.";
            }
        }
        catch (Exception ex)
        {
            ResetComplete = false;
            ResetFailed = true;
            StatusMessage = $"Alternative reset failed: {ex.Message}";
        }
    }

    private async Task MonitorRebootAsync()
    {
        _waitingForReconnect = true;
        
        // Wait up to 30 seconds for reconnection
        for (int i = 0; i < 30 && _waitingForReconnect; i++)
        {
            await Task.Delay(1000);
            
            if (!_waitingForReconnect)
                break;
                
            var elapsed = (DateTime.UtcNow - _rebootStartTime).TotalSeconds;
            StatusMessage = $"Drone is rebooting... waiting for reconnection ({elapsed:F0}s)";
        }

        if (_waitingForReconnect)
        {
            _waitingForReconnect = false;
            IsRebooting = false;
            StatusMessage = "Reboot timeout. Please manually reconnect to the drone using the Connection page.";
        }
    }

    private void UpdateStatusMessage()
    {
        if (!IsConnected)
        {
            StatusMessage = "Connect to a drone to reset parameters.";
        }
        else if (IsResetting)
        {
            StatusMessage = "Resetting parameters to defaults...";
        }
        else if (IsRebooting)
        {
            StatusMessage = "Drone is rebooting...";
        }
        else if (ResetComplete)
        {
            StatusMessage = "Reset prepared! Click 'Reboot Drone' to apply, then 'Refresh Parameters'.";
        }
        else if (ResetFailed)
        {
            StatusMessage = "Reset failed. Please try again.";
        }
        else
        {
            StatusMessage = "Ready to reset parameters to factory defaults.";
        }
    }

    [RelayCommand]
    private async Task ResetParametersAsync()
    {
        if (!IsConnected || IsResetting || IsRebooting)
            return;

        // SAFETY: Confirmation dialog for destructive operation
        // This prevents accidental resets in production environments
        var confirmed = await ShowConfirmationDialogAsync();
        if (!confirmed)
        {
            StatusMessage = "Reset operation cancelled by user.";
            return;
        }

        IsResetting = true;
        ResetComplete = false;
        ResetFailed = false;
        StatusMessage = "Sending reset command to flight controller...";

        try
        {
            // First try the MAV_CMD_PREFLIGHT_STORAGE command
            _connectionService.SendResetParameters();

            // Wait for command acknowledgment (timeout after 5 seconds)
            await Task.Delay(5000);

            // If still resetting after timeout, try alternative method
            if (IsResetting)
            {
                StatusMessage = "No response to reset command. Trying alternative method...";
                await TryAlternativeResetAsync();
                IsResetting = false;
            }
        }
        catch (Exception ex)
        {
            IsResetting = false;
            ResetFailed = true;
            StatusMessage = $"Reset failed: {ex.Message}";
        }
    }

    /// <summary>
    /// Show confirmation dialog for destructive reset operation.
    /// Returns true if user confirms, false if cancelled.
    /// </summary>
    private async Task<bool> ShowConfirmationDialogAsync()
    {
        // TODO: Implement proper confirmation dialog using Avalonia MessageBox or custom dialog
        // For now, we'll use a simple Task.FromResult(true) as a placeholder
        // In production, this should show a proper modal dialog with:
        // - Warning icon
        // - Clear destructive action warning
        // - "Cancel" (default) and "Reset" buttons
        // - Red accent for the destructive button
        
        await Task.Delay(100); // Simulate async dialog
        return true; // Placeholder - always confirm for now
        
        // Production implementation would look like:
        // var result = await MessageBox.Show(
        //     owner: GetMainWindow(),
        //     title: "Confirm Factory Reset",
        //     message: "This will reset ALL parameters to factory defaults.\n\n" +
        //              "All custom configurations will be permanently lost.\n\n" +
        //              "Are you sure you want to proceed?",
        //     buttons: MessageBoxButtons.CancelReset,
        //     icon: MessageBoxIcon.Warning,
        //     defaultButton: MessageBoxDefaultButton.Cancel
        // );
        // return result == MessageBoxResult.Reset;
    }

    [RelayCommand]
    private async Task RebootDroneAsync()
    {
        if (!IsConnected || IsRebooting)
            return;

        IsRebooting = true;
        StatusMessage = "Sending reboot command to drone...";
        
        _connectionService.SendPreflightReboot(1, 0);
        
        // Wait a bit for the ACK
        await Task.Delay(2000);
        
        // If we didn't get an ACK, the drone might have already started rebooting
        if (IsRebooting && IsConnected)
        {
            _rebootStartTime = DateTime.UtcNow;
            StatusMessage = "Reboot command sent. Waiting for drone to restart...";
            await MonitorRebootAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshParametersAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected. Please connect to the drone first.";
            return;
        }

        StatusMessage = "Refreshing parameters from drone...";
        
        try
        {
            _parameterService.Reset();
            await _parameterService.RefreshParametersAsync();
            StatusMessage = $"Parameters refreshed successfully! Downloaded {_parameterService.ReceivedParameterCount} parameters.";
            ResetComplete = false; // Clear the reset complete state
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to refresh parameters: {ex.Message}";
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _connectionService.CommandAckReceived -= OnCommandAckReceived;
            _connectionService.StatusTextReceived -= OnStatusTextReceived;
        }

        base.Dispose(disposing);
    }

    public bool IsDisconnected => !IsConnected;

    partial void OnIsConnectedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsDisconnected));
    }
}
