using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PavamanDroneConfigurator.API.DTOs;
using PavamanDroneConfigurator.API.Exceptions;
using PavamanDroneConfigurator.Core.Interfaces;
using IAuthService = PavamanDroneConfigurator.API.Services.IAuthService;

namespace PavamanDroneConfigurator.API.Controllers;

/// <summary>
/// Mobile API for the KFT Drone Configurator Kotlin application.
/// Provides authentication and parameter lock endpoints.
/// Base URL: http://13.235.13.233:5000/mobile
/// </summary>
[ApiController]
[Route("mobile")]
[Produces("application/json")]
public class MobileController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IParamLockService _paramLockService;
    private readonly ILogger<MobileController> _logger;

    public MobileController(
        IAuthService authService,
        IParamLockService paramLockService,
        ILogger<MobileController> logger)
    {
        _authService = authService;
        _paramLockService = paramLockService;
        _logger = logger;
    }

    // =========================================================================
    // AUTH ENDPOINTS
    // =========================================================================

    /// <summary>
    /// POST /mobile/auth/login
    /// Authenticate a user with email and password.
    /// Returns tokens on success. Account must be approved by admin before login is allowed.
    /// </summary>
    /// <remarks>
    /// Sample request:
    ///
    ///     POST /mobile/auth/login
    ///     {
    ///         "email": "user@example.com",
    ///         "password": "YourPassword123!"
    ///     }
    ///
    /// Sample response:
    ///
    ///     {
    ///         "accessToken": "eyJhbGci...",
    ///         "refreshToken": "abc123...",
    ///         "expiresIn": 900,
    ///         "user": {
    ///             "id": "uuid",
    ///             "email": "user@example.com",
    ///             "fullName": "John Doe",
    ///             "role": "User"
    ///         }
    ///     }
    ///
    /// Error codes:
    /// - INVALID_CREDENTIALS: Wrong email or password
    /// - ACCOUNT_NOT_APPROVED: Admin has not approved this account yet
    /// - ACCOUNT_DISABLED: Account has been disabled
    /// - ACCOUNT_LOCKED: Too many failed attempts, try again later
    /// </remarks>
    [HttpPost("auth/login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(MobileLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MobileErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(MobileErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<MobileLoginResponse>> Login([FromBody] LoginRequest request)
    {
        try
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var authResponse = await _authService.LoginAsync(request, ipAddress);

            if (authResponse.Tokens == null)
            {
                return StatusCode(403, new MobileErrorResponse
                {
                    Code = "ACCOUNT_NOT_APPROVED",
                    Message = "Your account is pending admin approval."
                });
            }

            _logger.LogInformation("Mobile login: {Email}", request.Email);

            return Ok(new MobileLoginResponse
            {
                AccessToken = authResponse.Tokens.AccessToken,
                RefreshToken = authResponse.Tokens.RefreshToken,
                ExpiresIn = authResponse.Tokens.ExpiresIn,
                User = new MobileUserDto
                {
                    Id = authResponse.User.Id,
                    Email = authResponse.User.Email,
                    FullName = authResponse.User.FullName,
                    Role = authResponse.User.Role
                }
            });
        }
        catch (AuthException ex) when (ex.StatusCode == 403)
        {
            return StatusCode(403, new MobileErrorResponse { Code = ex.Code, Message = ex.Message });
        }
        catch (AuthException ex)
        {
            return Unauthorized(new MobileErrorResponse { Code = ex.Code, Message = ex.Message });
        }
    }

    /// <summary>
    /// POST /mobile/auth/refresh
    /// Exchange a valid refresh token for a new access token.
    /// Call this when the access token expires (HTTP 401 received).
    /// </summary>
    /// <remarks>
    /// Sample request:
    ///
    ///     POST /mobile/auth/refresh
    ///     {
    ///         "refreshToken": "abc123..."
    ///     }
    ///
    /// Sample response: same shape as /mobile/auth/login
    /// </remarks>
    [HttpPost("auth/refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(MobileLoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MobileErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MobileLoginResponse>> RefreshToken([FromBody] MobileRefreshRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return Unauthorized(new MobileErrorResponse { Code = "INVALID_TOKEN", Message = "Refresh token is required." });

        try
        {
            var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
            var authResponse = await _authService.RefreshTokenAsync(request.RefreshToken, ipAddress);

            if (authResponse.Tokens == null)
                return Unauthorized(new MobileErrorResponse { Code = "TOKEN_EXPIRED", Message = "Refresh token is invalid or expired." });

            return Ok(new MobileLoginResponse
            {
                AccessToken = authResponse.Tokens.AccessToken,
                RefreshToken = authResponse.Tokens.RefreshToken,
                ExpiresIn = authResponse.Tokens.ExpiresIn,
                User = new MobileUserDto
                {
                    Id = authResponse.User.Id,
                    Email = authResponse.User.Email,
                    FullName = authResponse.User.FullName,
                    Role = authResponse.User.Role
                }
            });
        }
        catch (AuthException ex)
        {
            return Unauthorized(new MobileErrorResponse { Code = ex.Code, Message = ex.Message });
        }
    }

    /// <summary>
    /// GET /mobile/auth/me
    /// Returns the currently authenticated user's profile.
    /// Requires Authorization: Bearer {accessToken} header.
    /// </summary>
    [HttpGet("auth/me")]
    [Authorize]
    [EnableRateLimiting("fixed")]
    [ProducesResponseType(typeof(MobileUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MobileErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MobileUserDto>> GetMe()
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new MobileErrorResponse { Code = "INVALID_TOKEN", Message = "Invalid token." });

        var authResponse = await _authService.GetCurrentUserAsync(userId.Value);
        return Ok(new MobileUserDto
        {
            Id = authResponse.User.Id,
            Email = authResponse.User.Email,
            FullName = authResponse.User.FullName,
            Role = authResponse.User.Role
        });
    }

    // =========================================================================
    // PARAMETER LOCK ENDPOINTS
    // =========================================================================

    /// <summary>
    /// GET /mobile/params/locked
    /// Returns the list of parameter names locked by the admin for the current user.
    /// These parameters must be shown as read-only / disabled in the Kotlin app —
    /// the app must NOT allow the user to write these values to the drone.
    ///
    /// Requires Authorization: Bearer {accessToken} header.
    /// </summary>
    /// <param name="deviceId">
    /// Optional. The unique identifier of the drone/device being configured (e.g. serial number or SYSID).
    /// When supplied, device-specific locks are merged with user-wide locks.
    /// When omitted, all locked params across all the user's locks are returned.
    /// </param>
    /// <remarks>
    /// Sample request:
    ///
    ///     GET /mobile/params/locked?deviceId=DRONE_SN_001
    ///     Authorization: Bearer eyJhbGci...
    ///
    /// Sample response:
    ///
    ///     {
    ///         "lockedParams": ["ARSPD_FBW_MAX", "ARSPD_FBW_MIN", "ANGLE_MAX"],
    ///         "count": 3,
    ///         "deviceId": "DRONE_SN_001"
    ///     }
    ///
    /// When a parameter name appears in "lockedParams":
    ///   - Display it with a lock icon or greyed-out style
    ///   - Disable any input field that would write this parameter to the drone
    ///   - You may still READ the current value from the drone; only WRITE is blocked
    /// </remarks>
    [HttpGet("params/locked")]
    [Authorize]
    [EnableRateLimiting("fixed")]
    [ProducesResponseType(typeof(MobileLockedParamsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MobileErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MobileLockedParamsResponse>> GetLockedParams(
        [FromQuery] string? deviceId = null)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new MobileErrorResponse { Code = "INVALID_TOKEN", Message = "Invalid token." });

        try
        {
            var lockedParams = await _paramLockService.GetLockedParamsAsync(userId.Value, deviceId);

            // If deviceId is provided, also merge user-wide locks (no deviceId)
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                var userWideLocks = await _paramLockService.GetLockedParamsAsync(userId.Value, null);
                var merged = new HashSet<string>(lockedParams, StringComparer.OrdinalIgnoreCase);
                foreach (var p in userWideLocks) merged.Add(p);
                lockedParams = merged.OrderBy(p => p).ToList();
            }
            else
            {
                lockedParams = lockedParams.OrderBy(p => p).ToList();
            }

            _logger.LogInformation("Mobile: user {UserId} fetched {Count} locked params (device={DeviceId})",
                userId, lockedParams.Count, deviceId ?? "all");

            return Ok(new MobileLockedParamsResponse
            {
                LockedParams = lockedParams,
                Count = lockedParams.Count,
                DeviceId = deviceId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mobile: failed to get locked params for user {UserId}", userId);
            return StatusCode(500, new MobileErrorResponse
            {
                Code = "INTERNAL_ERROR",
                Message = "Failed to retrieve locked parameters."
            });
        }
    }

    /// <summary>
    /// POST /mobile/params/is-locked
    /// Check whether a specific parameter is locked for the current user/device.
    /// Useful for a quick check before allowing a write operation.
    /// </summary>
    /// <remarks>
    /// Sample request:
    ///
    ///     POST /mobile/params/is-locked
    ///     Authorization: Bearer eyJhbGci...
    ///     {
    ///         "paramName": "ARSPD_FBW_MAX",
    ///         "deviceId": "DRONE_SN_001"
    ///     }
    ///
    /// Sample response:
    ///
    ///     {
    ///         "paramName": "ARSPD_FBW_MAX",
    ///         "isLocked": true
    ///     }
    /// </remarks>
    [HttpPost("params/is-locked")]
    [Authorize]
    [EnableRateLimiting("fixed")]
    [ProducesResponseType(typeof(MobileIsLockedResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(MobileErrorResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MobileIsLockedResponse>> IsParamLocked([FromBody] MobileIsLockedRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return Unauthorized(new MobileErrorResponse { Code = "INVALID_TOKEN", Message = "Invalid token." });

        if (string.IsNullOrWhiteSpace(request.ParamName))
            return BadRequest(new MobileErrorResponse { Code = "BAD_REQUEST", Message = "paramName is required." });

        try
        {
            var isLocked = await _paramLockService.IsParamLockedAsync(userId.Value, request.DeviceId, request.ParamName);
            return Ok(new MobileIsLockedResponse
            {
                ParamName = request.ParamName,
                IsLocked = isLocked
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mobile: failed to check lock for param {Param}", request.ParamName);
            return StatusCode(500, new MobileErrorResponse { Code = "INTERNAL_ERROR", Message = "Check failed." });
        }
    }

    // =========================================================================
    // HELPERS
    // =========================================================================

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}

// =========================================================================
// Mobile-specific DTOs
// =========================================================================

/// <summary>Response returned on successful login or token refresh.</summary>
public class MobileLoginResponse
{
    public required string AccessToken { get; set; }
    public required string RefreshToken { get; set; }
    /// <summary>Access token lifetime in seconds (default 900 = 15 min).</summary>
    public int ExpiresIn { get; set; }
    public required MobileUserDto User { get; set; }
}

public class MobileUserDto
{
    public required string Id { get; set; }
    public required string Email { get; set; }
    public required string FullName { get; set; }
    /// <summary>"User" or "Admin"</summary>
    public required string Role { get; set; }
}

public class MobileRefreshRequest
{
    public required string RefreshToken { get; set; }
}

public class MobileLockedParamsResponse
{
    /// <summary>
    /// Parameter names that are locked. The app must disable write for these.
    /// </summary>
    public List<string> LockedParams { get; set; } = new();
    public int Count { get; set; }
    /// <summary>Device ID filter that was applied, or null if none.</summary>
    public string? DeviceId { get; set; }
}

public class MobileIsLockedRequest
{
    public required string ParamName { get; set; }
    public string? DeviceId { get; set; }
}

public class MobileIsLockedResponse
{
    public required string ParamName { get; set; }
    public bool IsLocked { get; set; }
}

public class MobileErrorResponse
{
    public required string Code { get; set; }
    public required string Message { get; set; }
}
