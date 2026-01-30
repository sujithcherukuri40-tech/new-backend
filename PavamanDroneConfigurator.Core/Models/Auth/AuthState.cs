namespace PavamanDroneConfigurator.Core.Models.Auth;

/// <summary>
/// Represents the possible authentication states of the application.
/// The UI is driven entirely by these states.
/// </summary>
public enum AuthStatus
{
    /// <summary>
    /// User has not logged in or session is invalid.
    /// </summary>
    Unauthenticated,

    /// <summary>
    /// User is fully authenticated and approved to access the application.
    /// </summary>
    Authenticated,

    /// <summary>
    /// User has registered but admin has not yet approved access.
    /// </summary>
    PendingApproval
}

/// <summary>
/// Immutable representation of the current authentication state.
/// This is the single source of truth for UI navigation decisions.
/// </summary>
public sealed record AuthState
{
    /// <summary>
    /// The current authentication status.
    /// </summary>
    public AuthStatus Status { get; init; } = AuthStatus.Unauthenticated;

    /// <summary>
    /// Information about the authenticated user. Null when unauthenticated.
    /// </summary>
    public UserInfo? User { get; init; }

    /// <summary>
    /// Whether the user is fully authenticated and can access the main application.
    /// </summary>
    public bool IsAuthenticated => Status == AuthStatus.Authenticated;

    /// <summary>
    /// Whether the user is pending admin approval.
    /// </summary>
    public bool IsPendingApproval => Status == AuthStatus.PendingApproval;

    /// <summary>
    /// Whether the user is not authenticated at all.
    /// </summary>
    public bool IsUnauthenticated => Status == AuthStatus.Unauthenticated;

    /// <summary>
    /// Creates an unauthenticated state.
    /// </summary>
    public static AuthState CreateUnauthenticated() => new()
    {
        Status = AuthStatus.Unauthenticated,
        User = null
    };

    /// <summary>
    /// Creates an authenticated state with user info.
    /// </summary>
    public static AuthState CreateAuthenticated(UserInfo user) => new()
    {
        Status = AuthStatus.Authenticated,
        User = user ?? throw new ArgumentNullException(nameof(user))
    };

    /// <summary>
    /// Creates a pending approval state with user info.
    /// </summary>
    public static AuthState CreatePendingApproval(UserInfo user) => new()
    {
        Status = AuthStatus.PendingApproval,
        User = user ?? throw new ArgumentNullException(nameof(user))
    };
}
