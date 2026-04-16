using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Models.Auth;
using System;

namespace PavamanDroneConfigurator.UI.ViewModels.Auth;

public sealed partial class AuthShellViewModel : ViewModelBase
{
    private readonly AuthSessionViewModel _authSession;
    private readonly IServiceProvider _services;
    private readonly ILogger<AuthShellViewModel> _logger;

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private bool _isInitializing = true;

    [ObservableProperty]
    private string? _initializingMessage = "Checking authentication...";

    private LoginViewModel? _loginViewModel;
    private RegisterViewModel? _registerViewModel;
    private PendingApprovalViewModel? _pendingApprovalViewModel;
    private ForgotPasswordViewModel? _forgotPasswordViewModel;

    /// <summary>
    /// Event raised when authentication is successful and app should proceed.
    /// </summary>
    public event EventHandler? AuthenticationCompleted;

    /// <summary>
    /// Event raised when user closes the auth shell without authenticating.
    /// </summary>
    public event EventHandler? AuthenticationCancelled;

    public AuthShellViewModel(
        AuthSessionViewModel authSession,
        IServiceProvider services,
        ILogger<AuthShellViewModel> logger)
    {
        _authSession = authSession;
        _services = services;
        _logger = logger;
        _authSession.StateChanged += OnAuthStateChanged;
    }

    /// <summary>
    /// Initialize the auth shell and navigate to appropriate view.
    /// Fast initialization with aggressive timeout to prevent UI hangs.
    /// </summary>
    public async Task InitializeAsync()
    {
        IsInitializing = true;
        InitializingMessage = "Checking authentication...";
        
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1000));
            await _authSession.InitializeAsync(cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Auth init error: {ex.Message}");
        }
        finally
        {
            IsInitializing = false;
        }
        
        // ALWAYS navigate - either to main window or login
        if (_authSession.CurrentState.IsAuthenticated)
        {
            AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
        }
        else if (_authSession.CurrentState.IsPendingApproval)
        {
            ShowPendingApproval();
        }
        else
        {
            ShowLogin();
        }
    }

    private void NavigateBasedOnAuthState(AuthState state)
    {
        if (state.IsAuthenticated)
        {
            _logger.LogInformation("User is authenticated, transitioning to main window");
            AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
        }
        else if (state.IsPendingApproval)
        {
            _logger.LogInformation("User is pending approval, showing pending approval screen");
            ShowPendingApproval();
        }
        else
        {
            _logger.LogInformation("User is not authenticated, showing login screen");
            ShowLogin();
        }
    }

    private void OnAuthStateChanged(object? sender, AuthState newState)
    {
        if (newState.IsAuthenticated)
        {
            AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Not authenticated - show login
            ShowLogin();
        }
    }

    private void ShowLogin()
    {
        _loginViewModel ??= CreateLoginViewModel();
        _loginViewModel.Reset();
        CurrentView = _loginViewModel;
        _logger.LogDebug("Login view displayed");
    }

    private void ShowForgotPassword()
    {
        _forgotPasswordViewModel ??= CreateForgotPasswordViewModel();
        _forgotPasswordViewModel.Reset();
        CurrentView = _forgotPasswordViewModel;
        _logger.LogDebug("Forgot password view displayed");
    }

    private void ShowRegister()
    {
        _registerViewModel ??= CreateRegisterViewModel();
        _registerViewModel.Reset();
        CurrentView = _registerViewModel;
        _logger.LogDebug("Register view displayed");
    }

    private void ShowPendingApproval()
    {
        _pendingApprovalViewModel ??= CreatePendingApprovalViewModel();
        CurrentView = _pendingApprovalViewModel;
        _logger.LogDebug("Pending approval view displayed");
    }

    private LoginViewModel CreateLoginViewModel()
    {
        var vm = _services.GetRequiredService<LoginViewModel>();
        vm.NavigateToRegisterRequested += (_, _) => ShowRegister();
        vm.NavigateToForgotPasswordRequested += (_, _) => ShowForgotPassword();
        vm.LoginSucceeded += (_, state) =>
        {
            if (state.IsAuthenticated)
                AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
            else if (state.IsPendingApproval)
                ShowPendingApproval();
        };
        return vm;
    }

    private RegisterViewModel CreateRegisterViewModel()
    {
        var vm = _services.GetRequiredService<RegisterViewModel>();
        vm.NavigateToLoginRequested += (_, _) => ShowLogin();
        vm.RegistrationSucceeded += (_, state) =>
        {
            if (state.IsPendingApproval)
                ShowPendingApproval();
            else if (state.IsAuthenticated)
                AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
        };
        return vm;
    }

    private PendingApprovalViewModel CreatePendingApprovalViewModel()
    {
        var vm = _services.GetRequiredService<PendingApprovalViewModel>();
        vm.LogoutCompleted += (_, _) => ShowLogin();
        vm.ApprovalGranted += (_, _) => AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
        return vm;
    }

    private ForgotPasswordViewModel CreateForgotPasswordViewModel()
    {
        var vm = _services.GetRequiredService<ForgotPasswordViewModel>();
        vm.NavigateToLoginRequested += (_, _) => ShowLogin();
        vm.ResetSucceeded += (_, _) => ShowLogin();
        return vm;
    }

    /// <summary>
    /// Handle the shell window being closed.
    /// </summary>
    public void OnWindowClosing()
    {
        if (!_authSession.CurrentState.IsAuthenticated)
        {
            _logger.LogInformation("Auth shell closed without authentication, cancelling");
            AuthenticationCancelled?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _authSession.StateChanged -= OnAuthStateChanged;
            (_loginViewModel as IDisposable)?.Dispose();
            (_registerViewModel as IDisposable)?.Dispose();
            (_pendingApprovalViewModel as IDisposable)?.Dispose();
        }
        base.Dispose(disposing);
    }
}
