using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.UI.ViewModels.Auth;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the Entry Page - the gateway after authentication.
/// Provides two paths: Firmware flashing (no connection required) or Connect to Drone.
/// Admin users also see a dashboard button for user management.
/// </summary>
public partial class EntryPageViewModel : ViewModelBase
{
    private readonly AuthSessionViewModel _authSession;

    /// <summary>
    /// Event raised when user wants to flash firmware (opens standalone firmware window).
    /// </summary>
    public event EventHandler? FirmwareRequested;

    /// <summary>
    /// Event raised when user wants to connect to a drone (navigates to ConnectionShell).
    /// </summary>
    public event EventHandler? ConnectRequested;

    /// <summary>
    /// Event raised when user wants to exit the application.
    /// </summary>
    public event EventHandler? ExitRequested;

    /// <summary>
    /// Event raised when admin user wants to open the admin dashboard.
    /// </summary>
    public event EventHandler? AdminDashboardRequested;

    [ObservableProperty]
    private string _welcomeMessage = "Welcome to Pavaman Drone Configurator";

    [ObservableProperty]
    private string _subtitleMessage = "Choose an option to get started";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPendingApprovals))]
    [NotifyPropertyChangedFor(nameof(PendingApprovalsDisplay))]
    private int _pendingApprovalsCount;

    /// <summary>
    /// Indicates if the current user is an admin (shows admin dashboard button).
    /// </summary>
    public bool IsAdmin => _authSession.CurrentState.User?.IsAdmin ?? false;

    /// <summary>
    /// Indicates if there are pending approvals to review.
    /// </summary>
    public bool HasPendingApprovals => PendingApprovalsCount > 0;

    /// <summary>
    /// Display text for pending approvals badge (shows "9+" for counts over 9).
    /// </summary>
    public string PendingApprovalsDisplay => PendingApprovalsCount > 9 ? "9+" : PendingApprovalsCount.ToString();

    public EntryPageViewModel(AuthSessionViewModel authSession)
    {
        _authSession = authSession;
    }

    /// <summary>
    /// Updates the pending approvals count (called from external source).
    /// </summary>
    public void UpdatePendingApprovalsCount(int count)
    {
        PendingApprovalsCount = count;
    }

    /// <summary>
    /// Opens the firmware flashing window (no MAVLink connection required).
    /// </summary>
    [RelayCommand]
    private void FlashFirmware()
    {
        FirmwareRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Navigates to the connection setup flow.
    /// </summary>
    [RelayCommand]
    private void ConnectToDrone()
    {
        ConnectRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Opens the admin dashboard for user management.
    /// Only available for admin users.
    /// </summary>
    [RelayCommand]
    private void OpenAdminDashboard()
    {
        if (IsAdmin)
        {
            AdminDashboardRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Exits the application.
    /// </summary>
    [RelayCommand]
    private void Exit()
    {
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }
}
