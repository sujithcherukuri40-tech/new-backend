namespace PavamanDroneConfigurator.API.Models;

/// <summary>
/// Refresh token entity for JWT token rotation.
/// Stored in database to enable revocation and token rotation.
/// </summary>
public class RefreshToken
{
    /// <summary>
    /// Unique identifier for the refresh token.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// The user this token belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// The refresh token value (hashed for security).
    /// </summary>
    public required string Token { get; set; }

    /// <summary>
    /// When this token expires.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Whether this token has been revoked (e.g., on logout).
    /// </summary>
    public bool Revoked { get; set; }

    /// <summary>
    /// When this token was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// IP address from which the token was created.
    /// </summary>
    public string? CreatedByIp { get; set; }

    /// <summary>
    /// When this token was revoked (if applicable).
    /// </summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>
    /// Reason for revocation.
    /// </summary>
    public string? RevokedReason { get; set; }

    /// <summary>
    /// Navigation property to user.
    /// </summary>
    public User? User { get; set; }

    /// <summary>
    /// Check if the token is currently valid.
    /// </summary>
    public bool IsValid => !Revoked && DateTimeOffset.UtcNow < ExpiresAt;
}
