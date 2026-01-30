using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Models.Auth;

namespace PavamanDroneConfigurator.UI.ViewModels.Auth;

/// <summary>
/// ViewModel for the Pending Approval screen.
/// Displayed when a user has registered but admin has not yet approved access.
/// </summary>
public sealed partial class PendingApprovalViewModel : ViewModelBase
{
    private readonly AuthSessionViewModel _authSession;

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _userEmail = string.Empty;

    [ObservableProperty]
    private bool _isCheckingStatus;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _hasStatusMessage;

    [ObservableProperty]
    private bool _isStatusError;

    /// <summary>
    /// Event raised when user logs out.
    /// </summary>
    public event EventHandler? LogoutCompleted;

    /// <summary>
    /// Event raised when admin approval has been granted.
    /// </summary>
    public event EventHandler<AuthState>? ApprovalGranted;

    public PendingApprovalViewModel(AuthSessionViewModel authSession)
    {
        _authSession = authSession;
        
        // Subscribe to state changes
        _authSession.StateChanged += OnAuthStateChanged;
        
        // Initialize from current state
        UpdateFromCurrentState();
    }

    private void OnAuthStateChanged(object? sender, AuthState newState)
    {
        UpdateFromCurrentState();
        
        // Check if approval was granted
        if (newState.IsAuthenticated)
        {
            ApprovalGranted?.Invoke(this, newState);
        }
    }

    private void UpdateFromCurrentState()
    {
        var user = _authSession.CurrentState.User;
        if (user != null)
        {
            UserName = user.FullName;
            UserEmail = user.Email;
        }
    }

    [RelayCommand]
    private async Task CheckStatusAsync()
    {
        if (IsCheckingStatus) return;

        IsCheckingStatus = true;
        ClearStatusMessage();

        try
        {
            var result = await _authSession.RefreshStateAsync();

            if (result.Success)
            {
                if (result.State.IsAuthenticated)
                {
                    ShowStatusMessage("Your account has been approved! Redirecting...", isError: false);
                    // The state change handler will trigger navigation
                }
                else if (result.State.IsPendingApproval)
                {
                    ShowStatusMessage("Your account is still pending approval. Please check back later.", isError: false);
                }
                else
                {
                    ShowStatusMessage("Your session has expired. Please log in again.", isError: true);
                }
            }
            else
            {
                ShowStatusMessage(result.ErrorMessage ?? "Unable to check status. Please try again.", isError: true);
            }
        }
        finally
        {
            IsCheckingStatus = false;
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        await _authSession.LogoutAsync();
        LogoutCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ShowStatusMessage(string message, bool isError)
    {
        StatusMessage = message;
        HasStatusMessage = true;
        IsStatusError = isError;
    }

    private void ClearStatusMessage()
    {
        StatusMessage = null;
        HasStatusMessage = false;
        IsStatusError = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _authSession.StateChanged -= OnAuthStateChanged;
        }
        base.Dispose(disposing);
    }
}
