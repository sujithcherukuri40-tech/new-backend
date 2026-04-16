using PavamanDroneConfigurator.API.DTOs;
using PavamanDroneConfigurator.API.Models;

namespace PavamanDroneConfigurator.API.Services;

/// <summary>
/// Service interface for authentication operations.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Register a new user.
    /// New users are created with IsApproved = false.
    /// </summary>
    /// <param name="request">Registration request.</param>
    /// <returns>Auth response with user info (no tokens for unapproved users).</returns>
    Task<AuthResponse> RegisterAsync(RegisterRequest request);

    /// <summary>
    /// Authenticate a user with email and password.
    /// Returns 403 if user is not approved.
    /// </summary>
    /// <param name="request">Login request.</param>
    /// <param name="ipAddress">IP address of the request.</param>
    /// <returns>Auth response with tokens if approved.</returns>
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress);

    /// <summary>
    /// Get the current user's information.
    /// </summary>
    /// <param name="userId">The user's ID from JWT claims.</param>
    /// <returns>Auth response with user info.</returns>
    Task<AuthResponse> GetCurrentUserAsync(Guid userId);

    /// <summary>
    /// Logout the user by revoking their refresh token.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="refreshToken">The refresh token to revoke.</param>
    Task LogoutAsync(Guid userId, string? refreshToken);

    /// <summary>
    /// Refresh the access token using a valid refresh token.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="ipAddress">IP address of the request.</param>
    /// <returns>Auth response with new tokens.</returns>
    Task<AuthResponse> RefreshTokenAsync(string refreshToken, string? ipAddress);

    /// <summary>
    /// Get a user by ID.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <returns>The user entity or null.</returns>
    Task<User?> GetUserByIdAsync(Guid userId);

    /// <summary>
    /// Initiate the forgot password process for a user.
    /// Always returns without error to avoid user enumeration.
    /// </summary>
    /// <param name="email">The user's email address.</param>
    Task ForgotPasswordAsync(string email);

    /// <summary>
    /// Reset a user's password using the 6-digit OTP sent by email.
    /// </summary>
    /// <param name="email">The user's email address.</param>
    /// <param name="code">The 6-digit reset code from the email.</param>
    /// <param name="newPassword">The new password to set.</param>
    Task ResetPasswordAsync(string email, string code, string newPassword);
}
