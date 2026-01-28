namespace PavamanDroneConfigurator.Core.Models.Auth;

/// <summary>
/// Result of an authentication operation (login, register, refresh).
/// All auth operations return this type.
/// </summary>
public sealed record AuthResult
{
    /// <summary>
    /// Whether the operation was successful.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The resulting authentication state after the operation.
    /// </summary>
    public AuthState State { get; init; } = AuthState.CreateUnauthenticated();

    /// <summary>
    /// Error message if the operation failed. Null on success.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Error code from the backend for programmatic handling.
    /// </summary>
    public AuthErrorCode ErrorCode { get; init; } = AuthErrorCode.None;

    /// <summary>
    /// Creates a successful result with authenticated state.
    /// </summary>
    public static AuthResult Succeeded(AuthState state) => new()
    {
        Success = true,
        State = state,
        ErrorMessage = null,
        ErrorCode = AuthErrorCode.None
    };

    /// <summary>
    /// Creates a failed result with error details.
    /// </summary>
    public static AuthResult Failed(string errorMessage, AuthErrorCode errorCode = AuthErrorCode.Unknown) => new()
    {
        Success = false,
        State = AuthState.CreateUnauthenticated(),
        ErrorMessage = errorMessage,
        ErrorCode = errorCode
    };

    /// <summary>
    /// Creates a network error result.
    /// </summary>
    public static AuthResult NetworkError(string message = "Unable to connect to the server. Please check your internet connection.") => new()
    {
        Success = false,
        State = AuthState.CreateUnauthenticated(),
        ErrorMessage = message,
        ErrorCode = AuthErrorCode.NetworkError
    };

    /// <summary>
    /// Creates a timeout error result.
    /// </summary>
    public static AuthResult Timeout(string message = "The request timed out. Please try again.") => new()
    {
        Success = false,
        State = AuthState.CreateUnauthenticated(),
        ErrorMessage = message,
        ErrorCode = AuthErrorCode.Timeout
    };
}

/// <summary>
/// Error codes for authentication failures.
/// </summary>
public enum AuthErrorCode
{
    /// <summary>
    /// No error.
    /// </summary>
    None,

    /// <summary>
    /// Unknown or unspecified error.
    /// </summary>
    Unknown,

    /// <summary>
    /// Invalid email or password.
    /// </summary>
    InvalidCredentials,

    /// <summary>
    /// Account is pending admin approval.
    /// </summary>
    AccountPendingApproval,

    /// <summary>
    /// Account has been disabled or locked.
    /// </summary>
    AccountDisabled,

    /// <summary>
    /// Email already registered.
    /// </summary>
    EmailAlreadyExists,

    /// <summary>
    /// Password does not meet requirements.
    /// </summary>
    WeakPassword,

    /// <summary>
    /// Session has expired and needs re-authentication.
    /// </summary>
    SessionExpired,

    /// <summary>
    /// Network connectivity issue.
    /// </summary>
    NetworkError,

    /// <summary>
    /// Request timed out.
    /// </summary>
    Timeout,

    /// <summary>
    /// Server returned an error.
    /// </summary>
    ServerError,

    /// <summary>
    /// Invalid request data.
    /// </summary>
    ValidationError
}
