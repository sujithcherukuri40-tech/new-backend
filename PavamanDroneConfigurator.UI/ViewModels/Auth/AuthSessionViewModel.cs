using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models.Auth;

namespace PavamanDroneConfigurator.UI.ViewModels.Auth;

public sealed partial class AuthSessionViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly ITokenStorage _tokenStorage;
    private readonly ILogger<AuthSessionViewModel> _logger;

    [ObservableProperty]
    private AuthState _currentState = AuthState.CreateUnauthenticated();

    [ObservableProperty]
    private bool _isLoading;

    public event EventHandler<AuthState>? StateChanged;

    public AuthSessionViewModel(IAuthService authService, ITokenStorage tokenStorage, ILogger<AuthSessionViewModel> logger)
    {
        _authService = authService;
        _tokenStorage = tokenStorage;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Initializing authentication session");

            // Fast check: do we have tokens stored locally?
            var hasTokens = false;
            try
            {
                hasTokens = await _tokenStorage.HasTokensAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check token storage");
            }
            
            if (!hasTokens)
            {
                _logger.LogInformation("No stored tokens found, user needs to log in");
                CurrentState = AuthState.CreateUnauthenticated();
                return;
            }

            _logger.LogInformation("Stored tokens found, validating with server");

            // Very aggressive timeout for startup - we want the app to show login quickly
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMilliseconds(500)); // REDUCED TO 500ms - fail fast!
            
            try
            {
                var result = await _authService.GetCurrentUserAsync(cts.Token);
                
                if (result.Success && result.State.IsAuthenticated)
                {
                    _logger.LogInformation("Session validation successful for user: {Email}", 
                        result.State.User?.Email ?? "Unknown");
                    CurrentState = result.State;
                }
                else
                {
                    _logger.LogWarning("Session validation failed, clearing tokens");
                    try { await _tokenStorage.ClearTokensAsync(CancellationToken.None); } catch { }
                    CurrentState = AuthState.CreateUnauthenticated();
                }
            }
            catch (OperationCanceledException)
            {
                // Timeout or cancellation - assume offline, show login
                _logger.LogWarning("Session validation timed out after 500ms, showing login screen");
                try { await _tokenStorage.ClearTokensAsync(CancellationToken.None); } catch { }
                CurrentState = AuthState.CreateUnauthenticated();
            }
            catch (Exception ex)
            {
                // Any error - clear tokens and show login
                _logger.LogWarning(ex, "Failed to validate existing session, clearing tokens");
                try { await _tokenStorage.ClearTokensAsync(CancellationToken.None); } catch { }
                CurrentState = AuthState.CreateUnauthenticated();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auth initialization failed critically");
            CurrentState = AuthState.CreateUnauthenticated();
        }
    }

    public async Task<AuthResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (IsLoading) return AuthResult.Failed("Operation in progress", AuthErrorCode.Unknown);
        
        _logger.LogInformation("Login attempt for user: {Email}", email);
        IsLoading = true;
        
        try
        {
            var result = await _authService.LoginAsync(
                new LoginRequest { Email = email, Password = password }, 
                cancellationToken);
            
            if (result.Success)
            {
                _logger.LogInformation("Login successful for user: {Email}", email);
                CurrentState = result.State;
                StateChanged?.Invoke(this, result.State);
            }
            else
            {
                _logger.LogWarning("Login failed for user: {Email}, Error: {ErrorCode}", 
                    email, result.ErrorCode);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during login for user: {Email}", email);
            return AuthResult.Failed("An unexpected error occurred", AuthErrorCode.Unknown);
        }
        finally 
        { 
            IsLoading = false; 
        }
    }

    public async Task<AuthResult> RegisterAsync(string fullName, string email, string password, string confirmPassword, CancellationToken cancellationToken = default)
    {
        if (IsLoading) return AuthResult.Failed("Operation in progress", AuthErrorCode.Unknown);
        
        _logger.LogInformation("Registration attempt for user: {Email}", email);
        IsLoading = true;
        
        try
        {
            var result = await _authService.RegisterAsync(new RegisterRequest
            {
                FullName = fullName, 
                Email = email, 
                Password = password, 
                ConfirmPassword = confirmPassword
            }, cancellationToken);
            
            if (result.Success)
            {
                _logger.LogInformation("Registration successful for user: {Email}", email);
                CurrentState = result.State;
                StateChanged?.Invoke(this, result.State);
            }
            else
            {
                _logger.LogWarning("Registration failed for user: {Email}, Error: {ErrorCode}", 
                    email, result.ErrorCode);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during registration for user: {Email}", email);
            return AuthResult.Failed("An unexpected error occurred", AuthErrorCode.Unknown);
        }
        finally 
        { 
            IsLoading = false; 
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Logging out user: {Email}", CurrentState.User?.Email ?? "Unknown");
        
        try 
        { 
            await _tokenStorage.ClearTokensAsync(CancellationToken.None); 
        } 
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error clearing tokens during logout");
        }
        
        // Fire and forget - don't block logout on API call
        _ = Task.Run(async () => 
        { 
            try 
            { 
                await _authService.LogoutAsync(default); 
            } 
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error notifying server of logout");
            }
        });
        
        CurrentState = AuthState.CreateUnauthenticated();
        StateChanged?.Invoke(this, CurrentState);
    }

    public async Task<AuthResult> RefreshStateAsync(CancellationToken cancellationToken = default)
    {
        if (IsLoading) return AuthResult.Failed("Operation in progress", AuthErrorCode.Unknown);
        
        _logger.LogDebug("Refreshing authentication state");
        IsLoading = true;
        
        try
        {
            var result = await _authService.GetCurrentUserAsync(cancellationToken);
            
            if (result.Success)
            {
                _logger.LogDebug("State refresh successful");
                CurrentState = result.State;
                StateChanged?.Invoke(this, result.State);
            }
            else
            {
                _logger.LogWarning("State refresh failed: {ErrorCode}", result.ErrorCode);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during state refresh");
            return AuthResult.Failed("An unexpected error occurred", AuthErrorCode.Unknown);
        }
        finally 
        { 
            IsLoading = false; 
        }
    }
}
