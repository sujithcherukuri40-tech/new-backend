using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using PavamanDroneConfigurator.Core.Models.Auth;

namespace PavamanDroneConfigurator.UI.ViewModels.Auth;

/// <summary>
/// Shell ViewModel that manages navigation between auth screens.
/// Uses state-driven navigation - the current view is determined by AuthState.
/// </summary>
public sealed partial class AuthShellViewModel : ViewModelBase
{
    private readonly AuthSessionViewModel _authSession;
    private readonly IServiceProvider _services;

    [ObservableProperty]
    private ViewModelBase? _currentView;

    [ObservableProperty]
    private bool _isInitializing = true;

    [ObservableProperty]
    private string? _initializingMessage = "Checking authentication...";

    // Cached ViewModels
    private LoginViewModel? _loginViewModel;
    private RegisterViewModel? _registerViewModel;
    private PendingApprovalViewModel? _pendingApprovalViewModel;

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
        IServiceProvider services)
    {
        _authSession = authSession;
        _services = services;

        // Subscribe to auth state changes
        _authSession.StateChanged += OnAuthStateChanged;
    }

    /// <summary>
    /// Initialize the auth shell and navigate to appropriate view.
    /// </summary>
    public async Task InitializeAsync()
    {
        IsInitializing = true;
        InitializingMessage = "Checking authentication...";

        try
        {
            // Initialize auth session (checks for existing tokens)
            await _authSession.InitializeAsync();

            // Navigate based on current state
            NavigateToStateView(_authSession.CurrentState);
        }
        finally
        {
            IsInitializing = false;
        }
    }

    private void OnAuthStateChanged(object? sender, AuthState newState)
    {
        // Handle state changes and navigate accordingly
        if (newState.IsAuthenticated)
        {
            // Fully authenticated - close auth shell
            AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
        }
        else
        {
            // Navigate to appropriate view
            NavigateToStateView(newState);
        }
    }

    private void NavigateToStateView(AuthState state)
    {
        switch (state.Status)
        {
            case AuthStatus.Unauthenticated:
                ShowLogin();
                break;

            case AuthStatus.PendingApproval:
                ShowPendingApproval();
                break;

            case AuthStatus.Authenticated:
                // Already authenticated - this shouldn't happen during auth flow
                AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void ShowLogin()
    {
        _loginViewModel ??= CreateLoginViewModel();
        _loginViewModel.Reset();
        CurrentView = _loginViewModel;
    }

    private void ShowRegister()
    {
        _registerViewModel ??= CreateRegisterViewModel();
        _registerViewModel.Reset();
        CurrentView = _registerViewModel;
    }

    private void ShowPendingApproval()
    {
        _pendingApprovalViewModel ??= CreatePendingApprovalViewModel();
        CurrentView = _pendingApprovalViewModel;
    }

    private LoginViewModel CreateLoginViewModel()
    {
        var vm = _services.GetRequiredService<LoginViewModel>();
        
        vm.NavigateToRegisterRequested += (_, _) => ShowRegister();
        vm.LoginSucceeded += (_, state) =>
        {
            if (state.IsAuthenticated)
            {
                AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
            }
            else if (state.IsPendingApproval)
            {
                ShowPendingApproval();
            }
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
            {
                ShowPendingApproval();
            }
            else if (state.IsAuthenticated)
            {
                AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
            }
        };

        return vm;
    }

    private PendingApprovalViewModel CreatePendingApprovalViewModel()
    {
        var vm = _services.GetRequiredService<PendingApprovalViewModel>();
        
        vm.LogoutCompleted += (_, _) => ShowLogin();
        vm.ApprovalGranted += (_, _) =>
        {
            AuthenticationCompleted?.Invoke(this, EventArgs.Empty);
        };

        return vm;
    }

    /// <summary>
    /// Handle the shell window being closed.
    /// </summary>
    public void OnWindowClosing()
    {
        if (!_authSession.CurrentState.IsAuthenticated)
        {
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
