using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Models.Auth;

namespace PavamanDroneConfigurator.UI.ViewModels.Auth;

/// <summary>
/// ViewModel for the Login screen.
/// Handles user input validation and delegates authentication to AuthSessionViewModel.
/// </summary>
public sealed partial class LoginViewModel : ViewModelBase
{
    private readonly AuthSessionViewModel _authSession;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _email = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Whether to show the direct login button (development only).
    /// </summary>
    public bool ShowDirectLogin => 
#if DEBUG
        true;
#else
        false;
#endif

    /// <summary>
    /// Event raised when user wants to navigate to registration.
    /// </summary>
    public event EventHandler? NavigateToRegisterRequested;

    /// <summary>
    /// Event raised when login is successful.
    /// The parameter indicates the resulting auth state.
    /// </summary>
    public event EventHandler<AuthState>? LoginSucceeded;

    public LoginViewModel(AuthSessionViewModel authSession)
    {
        _authSession = authSession;
    }

    [RelayCommand(CanExecute = nameof(CanLogin))]
    private async Task LoginAsync()
    {
        ClearError();

        // Client-side validation
        if (!ValidateInputs())
        {
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _authSession.LoginAsync(Email.Trim(), Password);

            if (result.Success)
            {
                // Clear sensitive data
                Password = string.Empty;
                LoginSucceeded?.Invoke(this, result.State);
            }
            else
            {
                ShowError(GetUserFriendlyErrorMessage(result));
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanLogin()
    {
        return !string.IsNullOrWhiteSpace(Email) &&
               !string.IsNullOrWhiteSpace(Password) &&
               !IsLoading;
    }

    [RelayCommand]
    private void NavigateToRegister()
    {
        ClearError();
        NavigateToRegisterRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand(CanExecute = nameof(CanDirectLogin))]
    private async Task DirectLoginAsync()
    {
        ClearError();

        IsLoading = true;
        try
        {
            // Create a fake authenticated state for development
            var fakeUserInfo = new Core.Models.Auth.UserInfo
            {
                Id = "dev-admin-id",
                Email = "admin@droneconfig.local",
                FullName = "Dev Admin",
                IsApproved = true,
                Role = "Admin",
                CreatedAt = DateTimeOffset.UtcNow
            };

            var authState = Core.Models.Auth.AuthState.CreateAuthenticated(fakeUserInfo);
            
#if DEBUG
            // Update the auth session with the fake state (DEBUG only)
            _authSession.SetDevAuthState(authState);
#endif
            
            // Trigger the login success event
            await Task.Delay(100); // Small delay to show loading state
            LoginSucceeded?.Invoke(this, authState);
        }
        catch (Exception ex)
        {
            ShowError($"Quick login failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanDirectLogin()
    {
        return !IsLoading;
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(Email))
        {
            ShowError("Please enter your email address.");
            return false;
        }

        if (!new EmailAddressAttribute().IsValid(Email.Trim()))
        {
            ShowError("Please enter a valid email address.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ShowError("Please enter your password.");
            return false;
        }

        return true;
    }

    private static string GetUserFriendlyErrorMessage(AuthResult result)
    {
        return result.ErrorCode switch
        {
            AuthErrorCode.InvalidCredentials => "Invalid email or password. Please try again.",
            AuthErrorCode.AccountPendingApproval => "Your account is pending approval. Please wait for an administrator to approve your access.",
            AuthErrorCode.AccountDisabled => "Your account has been disabled. Please contact support.",
            AuthErrorCode.NetworkError => "Unable to connect to the server. Please check your internet connection and try again.",
            AuthErrorCode.Timeout => "The request timed out. Please try again.",
            AuthErrorCode.ServerError => "The server is temporarily unavailable. Please try again later.",
            AuthErrorCode.SessionExpired => "Your session has expired. Please log in again.",
            _ => result.ErrorMessage ?? "An error occurred. Please try again."
        };
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    private void ClearError()
    {
        ErrorMessage = null;
        HasError = false;
    }

    /// <summary>
    /// Reset the form to initial state.
    /// </summary>
    public void Reset()
    {
        Email = string.Empty;
        Password = string.Empty;
        ClearError();
        IsLoading = false;
    }
}
