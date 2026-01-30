namespace PavamanDroneConfigurator.Core.Models.Auth;

/// <summary>
/// Information about an authenticated user.
/// This data comes from the backend and should not contain sensitive information.
/// </summary>
public sealed record UserInfo
{
    /// <summary>
    /// Unique identifier for the user.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// User's email address.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// User's display name.
    /// </summary>
    public required string FullName { get; init; }

    /// <summary>
    /// Whether the user's account has been approved by an administrator.
    /// </summary>
    public bool IsApproved { get; init; }

    /// <summary>
    /// User's role (User or Admin).
    /// </summary>
    public string Role { get; init; } = "User";

    /// <summary>
    /// Whether the user is an administrator.
    /// </summary>
    public bool IsAdmin => Role?.Equals("Admin", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// When the user's account was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// When the user last logged in successfully. Null if never logged in.
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; init; }
}
