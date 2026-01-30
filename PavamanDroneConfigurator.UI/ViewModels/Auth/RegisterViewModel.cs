using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Models.Auth;

namespace PavamanDroneConfigurator.UI.ViewModels.Auth;

/// <summary>
/// ViewModel for the Registration screen.
/// Handles user input validation and delegates registration to AuthSessionViewModel.
/// </summary>
public sealed partial class RegisterViewModel : ViewModelBase
{
    private readonly AuthSessionViewModel _authSession;

    private const int MinPasswordLength = 8;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    private string _fullName = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    private string _email = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegisterCommand))]
    private string _confirmPassword = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isLoading;

    // Field-specific validation messages
    [ObservableProperty]
    private string? _fullNameError;

    [ObservableProperty]
    private string? _emailError;

    [ObservableProperty]
    private string? _passwordError;

    [ObservableProperty]
    private string? _confirmPasswordError;

    /// <summary>
    /// Event raised when user wants to navigate back to login.
    /// </summary>
    public event EventHandler? NavigateToLoginRequested;

    /// <summary>
    /// Event raised when registration is successful.
    /// The parameter indicates the resulting auth state (typically PendingApproval).
    /// </summary>
    public event EventHandler<AuthState>? RegistrationSucceeded;

    public RegisterViewModel(AuthSessionViewModel authSession)
    {
        _authSession = authSession;
    }

    [RelayCommand(CanExecute = nameof(CanRegister))]
    private async Task RegisterAsync()
    {
        ClearErrors();

        // Client-side validation
        if (!ValidateInputs())
        {
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _authSession.RegisterAsync(
                FullName.Trim(),
                Email.Trim(),
                Password,
                ConfirmPassword
            );

            if (result.Success)
            {
                // Clear sensitive data
                Password = string.Empty;
                ConfirmPassword = string.Empty;
                RegistrationSucceeded?.Invoke(this, result.State);
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

    private bool CanRegister()
    {
        return !string.IsNullOrWhiteSpace(FullName) &&
               !string.IsNullOrWhiteSpace(Email) &&
               !string.IsNullOrWhiteSpace(Password) &&
               !string.IsNullOrWhiteSpace(ConfirmPassword) &&
               !IsLoading;
    }

    [RelayCommand]
    private void NavigateToLogin()
    {
        ClearErrors();
        NavigateToLoginRequested?.Invoke(this, EventArgs.Empty);
    }

    private bool ValidateInputs()
    {
        var isValid = true;

        // Validate full name
        if (string.IsNullOrWhiteSpace(FullName))
        {
            FullNameError = "Full name is required.";
            isValid = false;
        }
        else if (FullName.Trim().Length < 2)
        {
            FullNameError = "Full name must be at least 2 characters.";
            isValid = false;
        }
        else if (FullName.Trim().Length > 100)
        {
            FullNameError = "Full name must be less than 100 characters.";
            isValid = false;
        }

        // Validate email
        if (string.IsNullOrWhiteSpace(Email))
        {
            EmailError = "Email is required.";
            isValid = false;
        }
        else if (!new EmailAddressAttribute().IsValid(Email.Trim()))
        {
            EmailError = "Please enter a valid email address.";
            isValid = false;
        }

        // Validate password
        if (string.IsNullOrWhiteSpace(Password))
        {
            PasswordError = "Password is required.";
            isValid = false;
        }
        else if (Password.Length < MinPasswordLength)
        {
            PasswordError = $"Password must be at least {MinPasswordLength} characters.";
            isValid = false;
        }
        else if (!HasRequiredPasswordComplexity(Password))
        {
            PasswordError = "Password must contain at least one uppercase letter, one lowercase letter, and one number.";
            isValid = false;
        }

        // Validate confirm password
        if (string.IsNullOrWhiteSpace(ConfirmPassword))
        {
            ConfirmPasswordError = "Please confirm your password.";
            isValid = false;
        }
        else if (Password != ConfirmPassword)
        {
            ConfirmPasswordError = "Passwords do not match.";
            isValid = false;
        }

        if (!isValid)
        {
            HasError = true;
        }

        return isValid;
    }

    private static bool HasRequiredPasswordComplexity(string password)
    {
        var hasUppercase = false;
        var hasLowercase = false;
        var hasDigit = false;

        foreach (var c in password)
        {
            if (char.IsUpper(c)) hasUppercase = true;
            else if (char.IsLower(c)) hasLowercase = true;
            else if (char.IsDigit(c)) hasDigit = true;

            if (hasUppercase && hasLowercase && hasDigit)
                return true;
        }

        return hasUppercase && hasLowercase && hasDigit;
    }

    private static string GetUserFriendlyErrorMessage(AuthResult result)
    {
        return result.ErrorCode switch
        {
            AuthErrorCode.EmailAlreadyExists => "An account with this email already exists. Please use a different email or log in.",
            AuthErrorCode.WeakPassword => "The password does not meet security requirements. Please choose a stronger password.",
            AuthErrorCode.ValidationError => result.ErrorMessage ?? "Please check your input and try again.",
            AuthErrorCode.NetworkError => "Unable to connect to the server. Please check your internet connection and try again.",
            AuthErrorCode.Timeout => "The request timed out. Please try again.",
            AuthErrorCode.ServerError => "The server is temporarily unavailable. Please try again later.",
            _ => result.ErrorMessage ?? "An error occurred during registration. Please try again."
        };
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    private void ClearErrors()
    {
        ErrorMessage = null;
        HasError = false;
        FullNameError = null;
        EmailError = null;
        PasswordError = null;
        ConfirmPasswordError = null;
    }

    /// <summary>
    /// Reset the form to initial state.
    /// </summary>
    public void Reset()
    {
        FullName = string.Empty;
        Email = string.Empty;
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        ClearErrors();
        IsLoading = false;
    }
}
