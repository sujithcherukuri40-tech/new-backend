using System.ComponentModel.DataAnnotations;

namespace PavamanDroneConfigurator.API.DTOs;

/// <summary>
/// Request DTO for token refresh.
/// </summary>
public class RefreshTokenRequest
{
    /// <summary>
    /// The refresh token to use for getting new tokens.
    /// </summary>
    [Required(ErrorMessage = "Refresh token is required")]
    public required string RefreshToken { get; set; }
}

/// <summary>
/// Error response DTO for consistent error handling.
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Human-readable error message.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Machine-readable error code.
    /// </summary>
    public required string Code { get; set; }

    /// <summary>
    /// Additional error details (optional).
    /// </summary>
    public Dictionary<string, string[]>? Errors { get; set; }
}
