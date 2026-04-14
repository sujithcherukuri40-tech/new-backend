using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PavamanDroneConfigurator.API.DTOs;
using PavamanDroneConfigurator.API.Exceptions;
using PavamanDroneConfigurator.API.Services;

namespace PavamanDroneConfigurator.API.Controllers;

/// <summary>
/// Authentication controller for user registration, login, and token management.
/// Implements the auth flow for the Drone Configurator desktop application.
/// </summary>
[ApiController]
[Route("auth")]
[Produces("application/json")]
[EnableRateLimiting("auth")] // Apply stricter rate limiting to all auth endpoints
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(IAuthService authService, ILogger<AuthController> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Register a new user account.
    /// New users are created with IsApproved = false and cannot login until approved by an admin.
    /// </summary>
    /// <param name="request">Registration details.</param>
    /// <returns>User info (no tokens for unapproved users).</returns>
    /// <response code="200">Registration successful, pending approval.</response>
    /// <response code="400">Invalid request or email already exists.</response>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var response = await _authService.RegisterAsync(request);
            _logger.LogInformation("User registered: {Email}", request.Email);
            return Ok(response);
        }
        catch (AuthException ex) when (ex.Code == "EMAIL_EXISTS")
        {
            return Conflict(new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
        catch (AuthException ex)
        {
            return BadRequest(new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
    }

    /// <summary>
    /// Authenticate a user with email and password.
    /// Returns tokens only for approved users.
    /// </summary>
    /// <param name="request">Login credentials.</param>
    /// <returns>Auth response with tokens if approved.</returns>
    /// <response code="200">Login successful.</response>
    /// <response code="401">Invalid credentials.</response>
    /// <response code="403">Account pending approval or disabled.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request)
    {
        try
        {
            var ipAddress = GetClientIpAddress();
            var response = await _authService.LoginAsync(request, ipAddress);
            return Ok(response);
        }
        catch (AuthException ex) when (ex.StatusCode == 403)
        {
            return StatusCode(403, new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
        catch (AuthException ex)
        {
            return Unauthorized(new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
    }

    /// <summary>
    /// Get the current authenticated user's information.
    /// Validates the JWT and returns user profile.
    /// </summary>
    /// <returns>Current user info.</returns>
    /// <response code="200">User info returned.</response>
    /// <response code="401">Not authenticated.</response>
    [HttpGet("me")]
    [Authorize]
    [EnableRateLimiting("fixed")] // Less strict rate limiting for authenticated requests
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> GetCurrentUser()
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized(new ErrorResponse 
                { 
                    Message = "Invalid token", 
                    Code = "INVALID_TOKEN" 
                });
            }

            var response = await _authService.GetCurrentUserAsync(userId.Value);
            return Ok(response);
        }
        catch (AuthException ex)
        {
            return StatusCode(ex.StatusCode, new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
    }

    /// <summary>
    /// Logout the current user by revoking their refresh token.
    /// </summary>
    /// <returns>Success status.</returns>
    /// <response code="200">Logout successful.</response>
    /// <response code="401">Not authenticated.</response>
    [HttpPost("logout")]
    [Authorize]
    [EnableRateLimiting("fixed")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout()
    {
        var userId = GetCurrentUserId();
        if (userId == null)
        {
            return Unauthorized(new ErrorResponse 
            { 
                Message = "Invalid token", 
                Code = "INVALID_TOKEN" 
            });
        }

        // Get refresh token from request body if provided
        string? refreshToken = null;
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();
            if (!string.IsNullOrEmpty(body))
            {
                var json = System.Text.Json.JsonDocument.Parse(body);
                if (json.RootElement.TryGetProperty("refreshToken", out var tokenElement))
                {
                    refreshToken = tokenElement.GetString();
                }
            }
        }
        catch
        {
            // Ignore parsing errors, will revoke all tokens
        }

        await _authService.LogoutAsync(userId.Value, refreshToken);

        _logger.LogInformation("User {UserId} logged out", userId);
        return Ok(new { message = "Logged out successfully" });
    }

    /// <summary>
    /// Refresh the access token using a valid refresh token.
    /// Implements token rotation (old token is revoked, new tokens are issued).
    /// </summary>
    /// <param name="request">Refresh token.</param>
    /// <returns>New tokens.</returns>
    /// <response code="200">Tokens refreshed.</response>
    /// <response code="401">Invalid or expired refresh token.</response>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<AuthResponse>> RefreshToken([FromBody] RefreshTokenRequest request)
    {
        try
        {
            var ipAddress = GetClientIpAddress();
            var response = await _authService.RefreshTokenAsync(request.RefreshToken, ipAddress);
            return Ok(response);
        }
        catch (AuthException ex) when (ex.StatusCode == 403)
        {
            return StatusCode(403, new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
        catch (AuthException ex)
        {
            return Unauthorized(new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
    }

    /// <summary>
    /// Initiate forgot password flow.
    /// Always returns success message to avoid user enumeration.
    /// </summary>
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        try
        {
            await _authService.ForgotPasswordAsync(request.Email);
            return Ok(new { message = "If the email exists, a password reset link has been sent." });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Forgot password request failed for {Email}", request.Email);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse
                {
                    Message = "Unable to process forgot password request",
                    Code = "FORGOT_PASSWORD_FAILED"
                });
        }
    }

    /// <summary>
    /// Get the current user's ID from JWT claims.
    /// </summary>
    private Guid? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
        {
            return null;
        }

        return userId;
    }

    /// <summary>
    /// Get the client's IP address from the request.
    /// </summary>
    private string? GetClientIpAddress()
    {
        // Check for forwarded IP (behind proxy/load balancer)
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in the chain (original client)
            return forwardedFor.Split(',')[0].Trim();
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
