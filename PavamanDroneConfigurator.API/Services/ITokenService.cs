using PavamanDroneConfigurator.API.Models;

namespace PavamanDroneConfigurator.API.Services;

/// <summary>
/// Service interface for JWT and refresh token operations.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generate a JWT access token for a user.
    /// </summary>
    /// <param name="user">The user to generate a token for.</param>
    /// <returns>JWT access token string.</returns>
    string GenerateAccessToken(User user);

    /// <summary>
    /// Generate a refresh token for a user.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="ipAddress">The IP address of the request.</param>
    /// <returns>The refresh token entity.</returns>
    Task<RefreshToken> GenerateRefreshTokenAsync(Guid userId, string? ipAddress);

    /// <summary>
    /// Validate and retrieve a refresh token.
    /// </summary>
    /// <param name="token">The refresh token string.</param>
    /// <returns>The refresh token entity if valid, null otherwise.</returns>
    Task<RefreshToken?> ValidateRefreshTokenAsync(string token);

    /// <summary>
    /// Revoke a refresh token.
    /// </summary>
    /// <param name="token">The refresh token string.</param>
    /// <param name="reason">Reason for revocation.</param>
    Task RevokeRefreshTokenAsync(string token, string reason);

    /// <summary>
    /// Revoke all refresh tokens for a user.
    /// </summary>
    /// <param name="userId">The user's ID.</param>
    /// <param name="reason">Reason for revocation.</param>
    Task RevokeAllUserTokensAsync(Guid userId, string reason);

    /// <summary>
    /// Get the access token expiry time in seconds.
    /// </summary>
    int GetAccessTokenExpirySeconds();
}
