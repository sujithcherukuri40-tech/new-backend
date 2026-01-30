using PavamanDroneConfigurator.Core.Models.Auth;

namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Service interface for authentication operations.
/// Makes real HTTP calls to the backend API.
/// All business logic decisions are made by the backend.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Attempts to log in with email and password.
    /// </summary>
    /// <param name="request">Login credentials.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authentication result with state.</returns>
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a new user account.
    /// Note: New accounts require admin approval before access is granted.
    /// </summary>
    /// <param name="request">Registration details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authentication result with pending approval state on success.</returns>
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current user info from the backend.
    /// Used to validate existing sessions on app startup.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authentication result with current state.</returns>
    Task<AuthResult> GetCurrentUserAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs out the current user and invalidates tokens.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if logout was successful.</returns>
    Task<bool> LogoutAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to refresh the access token using the refresh token.
    /// Called automatically when access token is about to expire.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Authentication result with refreshed state.</returns>
    Task<AuthResult> RefreshTokenAsync(CancellationToken cancellationToken = default);
}
