namespace PavamanDroneConfigurator.API.DTOs;

/// <summary>
/// Response DTO for successful authentication.
/// This matches what the Avalonia UI expects from the backend.
/// </summary>
public class AuthResponse
{
    /// <summary>
    /// User information.
    /// </summary>
    public required UserDto User { get; set; }

    /// <summary>
    /// Token information (null if user is pending approval).
    /// </summary>
    public TokenDto? Tokens { get; set; }
}

/// <summary>
/// User information DTO.
/// </summary>
public class UserDto
{
    /// <summary>
    /// User's unique identifier.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// User's email address.
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    /// User's full name.
    /// </summary>
    public required string FullName { get; set; }

    /// <summary>
    /// Whether the user has been approved by an admin.
    /// </summary>
    public bool IsApproved { get; set; }

    /// <summary>
    /// User's role.
    /// </summary>
    public required string Role { get; set; }

    /// <summary>
    /// When the user account was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// Token information DTO.
/// </summary>
public class TokenDto
{
    /// <summary>
    /// JWT access token for API authentication.
    /// </summary>
    public required string AccessToken { get; set; }

    /// <summary>
    /// Refresh token for obtaining new access tokens.
    /// </summary>
    public required string RefreshToken { get; set; }

    /// <summary>
    /// Access token lifetime in seconds.
    /// </summary>
    public int ExpiresIn { get; set; }
}
