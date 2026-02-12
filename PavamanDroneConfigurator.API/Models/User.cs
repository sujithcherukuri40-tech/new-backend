namespace PavamanDroneConfigurator.API.Models;

/// <summary>
/// User entity for authentication.
/// Users must be approved by an admin before they can access the system.
/// </summary>
public class User
{
    /// <summary>
    /// Unique identifier for the user.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// User's full name.
    /// </summary>
    public required string FullName { get; set; }

    /// <summary>
    /// User's email address (unique, used for login).
    /// </summary>
    public required string Email { get; set; }

    /// <summary>
    /// BCrypt hashed password.
    /// </summary>
    public required string PasswordHash { get; set; }

    /// <summary>
    /// Whether the user has been approved by an administrator.
    /// New users default to false and cannot login until approved.
    /// </summary>
    public bool IsApproved { get; set; }

    /// <summary>
    /// User's role in the system.
    /// </summary>
    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>
    /// When the user account was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the user last logged in successfully.
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Whether the user must change their password on next login.
    /// Set to true when password is auto-generated or reset by admin.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// Number of consecutive failed login attempts.
    /// Used for account lockout protection.
    /// </summary>
    public int FailedLoginAttempts { get; set; }

    /// <summary>
    /// When the account was locked due to failed login attempts.
    /// Null if account is not locked.
    /// </summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>
    /// Navigation property for refresh tokens.
    /// </summary>
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

/// <summary>
/// User roles for authorization.
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Standard user with basic access.
    /// </summary>
    User = 0,

    /// <summary>
    /// Administrator with full access including user approval.
    /// </summary>
    Admin = 1
}
