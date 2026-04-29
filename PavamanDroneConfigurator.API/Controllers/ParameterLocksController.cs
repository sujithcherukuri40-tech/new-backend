using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PavamanDroneConfigurator.API.DTOs;
using PavamanDroneConfigurator.Core.Interfaces;
using System.Security.Claims;

namespace PavamanDroneConfigurator.API.Controllers;

/// <summary>
/// Admin endpoints for managing parameter locks.
/// Allows admins to lock specific parameters for users/devices.
/// </summary>
[ApiController]
[Route("admin/parameter-locks")]
[Authorize(Roles = "Admin")]
[EnableRateLimiting("admin")]
[Produces("application/json")]
public class ParameterLocksController : ControllerBase
{
    private readonly IParamLockService _paramLockService;
    private readonly ILogger<ParameterLocksController> _logger;

    public ParameterLocksController(
        IParamLockService paramLockService,
        ILogger<ParameterLocksController> logger)
    {
        _paramLockService = paramLockService;
        _logger = logger;
    }

    /// <summary>
    /// Create a new parameter lock for a user/device.
    /// </summary>
    /// <param name="request">Lock creation request</param>
    /// <returns>Created lock information</returns>
    /// <response code="200">Lock created successfully</response>
    /// <response code="400">Invalid request</response>
    /// <response code="401">Not authenticated</response>
    /// <response code="403">Not authorized (admin only)</response>
    [HttpPost]
    [ProducesResponseType(typeof(ParamLockResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ParamLockResponse>> CreateLock([FromBody] CreateParamLockRequest request)
    {
        try
        {
            var adminUserId = GetCurrentUserId();
            if (adminUserId == null)
            {
                return Unauthorized(new ErrorResponse { Message = "Invalid token", Code = "INVALID_TOKEN" });
            }

            var s3Key = await _paramLockService.CreateParamLockAsync(
                request.UserId,
                request.DeviceId,
                request.Params,
                adminUserId.Value,
                request.ParamValues.Count > 0 ? request.ParamValues : null);

            _logger.LogInformation("Admin {AdminId} created parameter lock for user {UserId}, device {DeviceId}, {Count} params",
                adminUserId, request.UserId, request.DeviceId, request.Params.Count);

            return Ok(new ParamLockResponse
            {
                Success = true,
                Message = "Parameter lock created successfully",
                S3Key = s3Key,
                ParamCount = request.Params.Count
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Message = ex.Message, Code = "INVALID_REQUEST" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create parameter lock");
            return StatusCode(500, new ErrorResponse
            {
                Message = "Failed to create parameter lock",
                Code = "CREATE_LOCK_FAILED"
            });
        }
    }

    /// <summary>
    /// Update an existing parameter lock.
    /// </summary>
    /// <param name="request">Lock update request</param>
    /// <returns>Updated lock information</returns>
    [HttpPut]
    [ProducesResponseType(typeof(ParamLockResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ParamLockResponse>> UpdateLock([FromBody] UpdateParamLockRequest request)
    {
        try
        {
            var adminUserId = GetCurrentUserId();
            if (adminUserId == null)
            {
                return Unauthorized(new ErrorResponse { Message = "Invalid token", Code = "INVALID_TOKEN" });
            }

            var s3Key = await _paramLockService.UpdateParamLockAsync(
                request.LockId,
                request.Params,
                adminUserId.Value,
                request.ParamValues.Count > 0 ? request.ParamValues : null);

            _logger.LogInformation("Admin {AdminId} updated parameter lock {LockId}, {Count} params",
                adminUserId, request.LockId, request.Params.Count);

            return Ok(new ParamLockResponse
            {
                Success = true,
                Message = "Parameter lock updated successfully",
                S3Key = s3Key,
                LockId = request.LockId,
                ParamCount = request.Params.Count
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ErrorResponse { Message = ex.Message, Code = "INVALID_REQUEST" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update parameter lock {LockId}", request.LockId);
            return StatusCode(500, new ErrorResponse
            {
                Message = "Failed to update parameter lock",
                Code = "UPDATE_LOCK_FAILED"
            });
        }
    }

    /// <summary>
    /// Delete (deactivate) a parameter lock.
    /// </summary>
    /// <param name="lockId">Lock ID to delete</param>
    /// <returns>Success status</returns>
    [HttpDelete("{lockId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteLock(int lockId)
    {
        try
        {
            var success = await _paramLockService.DeleteParamLockAsync(lockId);

            if (!success)
            {
                return NotFound(new ErrorResponse
                {
                    Message = $"Lock {lockId} not found",
                    Code = "LOCK_NOT_FOUND"
                });
            }

            var adminUserId = GetCurrentUserId();
            _logger.LogInformation("Admin {AdminId} deleted parameter lock {LockId}", adminUserId, lockId);

            return Ok(new { message = "Parameter lock deleted successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete parameter lock {LockId}", lockId);
            return StatusCode(500, new ErrorResponse
            {
                Message = "Failed to delete parameter lock",
                Code = "DELETE_LOCK_FAILED"
            });
        }
    }

    /// <summary>
    /// Get all active parameter locks (admin overview).
    /// </summary>
    /// <returns>List of all parameter locks</returns>
    [HttpGet]
    [ProducesResponseType(typeof(List<ParamLockInfo>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ParamLockInfo>>> GetAllLocks()
    {
        try
        {
            var locks = await _paramLockService.GetAllLocksAsync();
            return Ok(locks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve parameter locks");
            return StatusCode(500, new ErrorResponse
            {
                Message = "Failed to retrieve parameter locks",
                Code = "GET_LOCKS_FAILED"
            });
        }
    }

    /// <summary>
    /// Get parameter locks for a specific user.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <returns>List of user's parameter locks</returns>
    [HttpGet("user/{userId}")]
    [ProducesResponseType(typeof(List<ParamLockInfo>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<ParamLockInfo>>> GetUserLocks(Guid userId)
    {
        try
        {
            var locks = await _paramLockService.GetUserLocksAsync(userId);
            return Ok(locks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve locks for user {UserId}", userId);
            return StatusCode(500, new ErrorResponse
            {
                Message = "Failed to retrieve user locks",
                Code = "GET_USER_LOCKS_FAILED"
            });
        }
    }

    /// <summary>
    /// Check which parameters are locked for a user/device.
    /// </summary>
    /// <param name="request">Check request</param>
    /// <returns>List of locked parameters</returns>
    [HttpPost("check")]
    [Authorize] // Allow any authenticated user to check their own locks, admins can check any
    [ProducesResponseType(typeof(LockedParamsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<LockedParamsResponse>> CheckLockedParams([FromBody] CheckLockedParamsRequest request)
    {
        try
        {
            // Allow users to check their own locks, or admins to check any user
            var currentUserId = GetCurrentUserId();
            var isAdmin = User.IsInRole("Admin");

            if (!isAdmin && currentUserId != request.UserId)
            {
                return Forbid();
            }

            var lockedParams = await _paramLockService.GetLockedParamsAsync(request.UserId, request.DeviceId);

            return Ok(new LockedParamsResponse
            {
                UserId = request.UserId,
                DeviceId = request.DeviceId,
                LockedParams = lockedParams,
                Count = lockedParams.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check locked params for user {UserId}", request.UserId);
            return StatusCode(500, new ErrorResponse
            {
                Message = "Failed to check locked parameters",
                Code = "CHECK_LOCKS_FAILED"
            });
        }
    }

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
}
