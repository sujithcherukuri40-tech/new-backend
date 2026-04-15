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
    [NotifyCanExecuteChangedFor(nameof(LoginCommand))]
    private bool _acceptedTerms;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _acceptedTermsError;

    /// <summary>
    /// Event raised when user wants to navigate to registration.
    /// </summary>
    public event EventHandler? NavigateToRegisterRequested;

    /// <summary>
    /// Event raised when login is successful.
    /// The parameter indicates the resulting auth state.
    /// </summary>
    public event EventHandler<AuthState>? LoginSucceeded;

    /// <summary>
    /// Event raised when user wants to view the Terms and Conditions.
    /// </summary>
    public event EventHandler? ViewTermsRequested;

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
               AcceptedTerms &&
               !IsLoading;
    }

    [RelayCommand]
    private void NavigateToRegister()
    {
        ClearError();
        NavigateToRegisterRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? NavigateToForgotPasswordRequested;

    [RelayCommand]
    private void NavigateToForgotPassword()
    {
        ClearError();
        NavigateToForgotPasswordRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ViewTerms()
    {
        ViewTermsRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnAcceptedTermsChanged(bool value)
    {
        // Clear the terms error when user checks the checkbox
        if (value)
        {
            AcceptedTermsError = null;
        }
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

        if (!AcceptedTerms)
        {
            AcceptedTermsError = "You must accept the Terms and Conditions to continue.";
            ShowError("Please accept the Terms and Conditions to continue.");
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
        AcceptedTermsError = null;
    }

    /// <summary>
    /// Reset the form to initial state.
    /// </summary>
    public void Reset()
    {
        Email = string.Empty;
        Password = string.Empty;
        AcceptedTerms = false;
        ClearError();
        IsLoading = false;
    }
}
