using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models.Auth;

namespace PavamanDroneConfigurator.UI.ViewModels.Auth;

/// <summary>
/// Global authentication session manager.
/// This ViewModel is the single source of truth for the UI authentication state.
/// All auth-related navigation decisions should be based on CurrentState.
/// </summary>
public sealed partial class AuthSessionViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly ITokenStorage _tokenStorage;
    private readonly ILogger<AuthSessionViewModel> _logger;

    [ObservableProperty]
    private AuthState _currentState = AuthState.CreateUnauthenticated();

    [ObservableProperty]
    private bool _isInitializing;

    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Event raised when the authentication state changes.
    /// </summary>
    public event EventHandler<AuthState>? StateChanged;

    public AuthSessionViewModel(
        IAuthService authService,
        ITokenStorage tokenStorage,
        ILogger<AuthSessionViewModel> logger)
    {
        _authService = authService;
        _tokenStorage = tokenStorage;
        _logger = logger;
    }

    /// <summary>
    /// Initialize the auth session by checking for existing tokens.
    /// Called on app startup after splash screen.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsInitializing) return;

        IsInitializing = true;
        try
        {
            _logger.LogInformation("Initializing auth session...");

            // Check if we have stored tokens
            var hasTokens = await _tokenStorage.HasTokensAsync(cancellationToken);
            if (!hasTokens)
            {
                _logger.LogDebug("No stored tokens found");
                UpdateState(AuthState.CreateUnauthenticated());
                return;
            }

            // Validate tokens with backend
            var result = await _authService.GetCurrentUserAsync(cancellationToken);
            if (result.Success)
            {
                UpdateState(result.State);
                _logger.LogInformation("Auth session restored: {Status}", result.State.Status);
            }
            else
            {
                _logger.LogWarning("Failed to restore auth session: {Error}", result.ErrorMessage);
                UpdateState(AuthState.CreateUnauthenticated());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing auth session");
            UpdateState(AuthState.CreateUnauthenticated());
        }
        finally
        {
            IsInitializing = false;
        }
    }

    /// <summary>
    /// Attempt to log in with credentials.
    /// </summary>
    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (IsLoading) return AuthResult.Failed("Operation in progress", AuthErrorCode.Unknown);

        IsLoading = true;
        try
        {
            var request = new LoginRequest
            {
                Email = email,
                Password = password
            };

            var result = await _authService.LoginAsync(request, cancellationToken);

            if (result.Success)
            {
                UpdateState(result.State);
                _logger.LogInformation("Login successful: {Status}", result.State.Status);
            }
            else
            {
                _logger.LogWarning("Login failed: {Error}", result.ErrorMessage);
            }

            return result;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Register a new user account.
    /// </summary>
    public async Task<AuthResult> RegisterAsync(
        string fullName,
        string email,
        string password,
        string confirmPassword,
        CancellationToken cancellationToken = default)
    {
        if (IsLoading) return AuthResult.Failed("Operation in progress", AuthErrorCode.Unknown);

        IsLoading = true;
        try
        {
            var request = new RegisterRequest
            {
                FullName = fullName,
                Email = email,
                Password = password,
                ConfirmPassword = confirmPassword
            };

            var result = await _authService.RegisterAsync(request, cancellationToken);

            if (result.Success)
            {
                UpdateState(result.State);
                _logger.LogInformation("Registration successful: {Status}", result.State.Status);
            }
            else
            {
                _logger.LogWarning("Registration failed: {Error}", result.ErrorMessage);
            }

            return result;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Log out the current user.
    /// </summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading) return;

        IsLoading = true;
        try
        {
            await _authService.LogoutAsync(cancellationToken);
            UpdateState(AuthState.CreateUnauthenticated());
            _logger.LogInformation("User logged out");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refresh the current user's state from the backend.
    /// Useful to check if admin approval has been granted.
    /// </summary>
    public async Task<AuthResult> RefreshStateAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading) return AuthResult.Failed("Operation in progress", AuthErrorCode.Unknown);

        IsLoading = true;
        try
        {
            var result = await _authService.GetCurrentUserAsync(cancellationToken);

            if (result.Success)
            {
                UpdateState(result.State);
                _logger.LogInformation("State refreshed: {Status}", result.State.Status);
            }

            return result;
        }
        finally
        {
            IsLoading = false;
        }
    }

#if DEBUG
    /// <summary>
    /// Set authentication state directly (DEBUG only).
    /// Used for quick login during development.
    /// </summary>
    public void SetDevAuthState(AuthState state)
    {
        UpdateState(state);
        _logger.LogWarning("DEV: Auth state set manually to {Status}", state.Status);
    }
#endif

    private void UpdateState(AuthState newState)
    {
        var oldState = CurrentState;
        CurrentState = newState;

        if (oldState.Status != newState.Status)
        {
            _logger.LogDebug("Auth state changed: {OldStatus} -> {NewStatus}", oldState.Status, newState.Status);
            StateChanged?.Invoke(this, newState);
        }
    }
}
