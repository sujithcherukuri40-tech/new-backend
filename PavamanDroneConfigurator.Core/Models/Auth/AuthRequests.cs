namespace PavamanDroneConfigurator.Core.Models.Auth;

/// <summary>
/// Request model for user login.
/// </summary>
public sealed record LoginRequest
{
    /// <summary>
    /// User's email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// User's password.
    /// </summary>
    public required string Password { get; init; }
}

/// <summary>
/// Request model for user registration.
/// </summary>
public sealed record RegisterRequest
{
    /// <summary>
    /// User's full name.
    /// </summary>
    public required string FullName { get; init; }

    /// <summary>
    /// User's email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// User's password.
    /// </summary>
    public required string Password { get; init; }

    /// <summary>
    /// Password confirmation (must match Password).
    /// </summary>
    public required string ConfirmPassword { get; init; }
}

/// <summary>
/// Token data received from the backend.
/// The UI does not parse or validate tokens - backend is the source of truth.
/// </summary>
public sealed record TokenData
{
    /// <summary>
    /// The access token for API authentication.
    /// </summary>
    public required string AccessToken { get; init; }

    /// <summary>
    /// The refresh token for obtaining new access tokens.
    /// </summary>
    public required string RefreshToken { get; init; }

    /// <summary>
    /// When the access token expires (from backend).
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }
}
