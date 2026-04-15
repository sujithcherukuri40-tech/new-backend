using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models.Auth;

namespace PavamanDroneConfigurator.UI.ViewModels.Auth;

/// <summary>
/// Two-step forgot password flow:
///   Step 1 — enter email → backend sends 6-digit OTP
///   Step 2 — enter OTP + new password → backend validates and resets
/// </summary>
public sealed partial class ForgotPasswordViewModel : ViewModelBase
{
    private readonly IAuthService _authService;

    // ── Step control ─────────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isStep1 = true;   // email entry

    [ObservableProperty]
    private bool _isStep2;          // OTP + new password entry

    // ── Step 1 ────────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCodeCommand))]
    private string _email = string.Empty;

    // ── Step 2 ────────────────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetPasswordCommand))]
    private string _code = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetPasswordCommand))]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetPasswordCommand))]
    private string _confirmPassword = string.Empty;

    // ── Status ────────────────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private bool _isSuccess;

    [ObservableProperty]
    private string _successMessage = string.Empty;

    // ── Events ────────────────────────────────────────────────────────────────
    /// <summary>Navigate back to the login screen.</summary>
    public event EventHandler? NavigateToLoginRequested;

    /// <summary>Raised when password is reset successfully.</summary>
    public event EventHandler? ResetSucceeded;

    public ForgotPasswordViewModel(IAuthService authService)
    {
        _authService = authService;
    }

    // ── Step 1: Send OTP ─────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanSendCode))]
    private async Task SendCodeAsync()
    {
        ClearError();

        if (string.IsNullOrWhiteSpace(Email))
        {
            ShowError("Please enter your email address.");
            return;
        }

        if (!new EmailAddressAttribute().IsValid(Email.Trim()))
        {
            ShowError("Please enter a valid email address.");
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _authService.ForgotPasswordAsync(Email.Trim());

            // Always move to step 2 regardless of whether email exists
            // (prevents user enumeration)
            IsStep1 = false;
            IsStep2 = true;
            SuccessMessage = $"A 6-digit code has been sent to {Email.Trim()} (if it exists). Please check your inbox.";
            IsSuccess = true;
        }
        catch (Exception)
        {
            // Swallow — move to step 2 anyway
            IsStep1 = false;
            IsStep2 = true;
            SuccessMessage = $"A 6-digit code has been sent to {Email.Trim()} (if it exists).";
            IsSuccess = true;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanSendCode() =>
        !string.IsNullOrWhiteSpace(Email) && !IsLoading;

    // ── Step 2: Reset Password ────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanResetPassword))]
    private async Task ResetPasswordAsync()
    {
        ClearError();
        IsSuccess = false;

        if (string.IsNullOrWhiteSpace(Code) || Code.Trim().Length != 6)
        {
            ShowError("Please enter the 6-digit code from your email.");
            return;
        }

        if (string.IsNullOrWhiteSpace(NewPassword))
        {
            ShowError("Please enter a new password.");
            return;
        }

        if (NewPassword != ConfirmPassword)
        {
            ShowError("Passwords do not match.");
            return;
        }

        if (NewPassword.Length < 8)
        {
            ShowError("Password must be at least 8 characters.");
            return;
        }

        IsLoading = true;
        try
        {
            var result = await _authService.ResetPasswordAsync(
                Email.Trim(),
                Code.Trim(),
                NewPassword);

            if (result.Success)
            {
                IsSuccess = true;
                SuccessMessage = "Password reset successfully! You can now sign in with your new password.";
                ResetSucceeded?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ShowError(result.ErrorMessage ?? "Failed to reset password. Please try again.");
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private bool CanResetPassword() =>
        !string.IsNullOrWhiteSpace(Code) &&
        !string.IsNullOrWhiteSpace(NewPassword) &&
        !string.IsNullOrWhiteSpace(ConfirmPassword) &&
        !IsLoading;

    // ── Navigation ────────────────────────────────────────────────────────────
    [RelayCommand]
    private void NavigateToLogin()
    {
        NavigateToLoginRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void GoBackToStep1()
    {
        ClearError();
        IsSuccess = false;
        IsStep2 = false;
        IsStep1 = true;
        Code = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
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

    public void Reset()
    {
        Email = string.Empty;
        Code = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        IsStep1 = true;
        IsStep2 = false;
        IsLoading = false;
        IsSuccess = false;
        ClearError();
    }
}
