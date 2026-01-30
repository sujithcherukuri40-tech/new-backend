namespace PavamanDroneConfigurator.API.Exceptions;

/// <summary>
/// Custom exception for authentication errors.
/// Used to return consistent error responses to the client.
/// </summary>
public class AuthException : Exception
{
    /// <summary>
    /// Machine-readable error code.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// HTTP status code to return.
    /// </summary>
    public int StatusCode { get; }

    public AuthException(string message, string code, int statusCode = 400)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }
}
