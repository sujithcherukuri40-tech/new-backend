using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PavamanDroneConfigurator.API.DTOs;
using PavamanDroneConfigurator.API.Exceptions;
using PavamanDroneConfigurator.API.Models;
using PavamanDroneConfigurator.API.Services;

namespace PavamanDroneConfigurator.API.Controllers;

/// <summary>
/// Admin controller for user management.
/// Only accessible to users with Admin role.
/// </summary>
[ApiController]
[Route("admin")]
[Authorize(Roles = "Admin")]
[EnableRateLimiting("admin")] // SECURITY: Apply stricter rate limiting to admin endpoints
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IAdminService adminService, ILogger<AdminController> logger)
    {
        _adminService = adminService;
        _logger = logger;
    }

    /// <summary>
    /// Get all users for admin panel.
    /// </summary>
    /// <returns>List of all users.</returns>
    /// <response code="200">Users list returned.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Not authorized (non-admin).</response>
    [HttpGet("users")]
    [ProducesResponseType(typeof(UsersListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<UsersListResponse>> GetAllUsers()
    {
        try
        {
            var adminId = GetCurrentUserId();
            _logger.LogInformation("Admin {AdminId} requesting user list", adminId);

            var response = await _adminService.GetAllUsersAsync();
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving users list");
            return StatusCode(500, new ErrorResponse
            {
                Message = "Failed to retrieve users",
                Code = "SERVER_ERROR"
            });
        }
    }

    /// <summary>
    /// Approve or disapprove a user.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="request">Approval request.</param>
    /// <returns>Success response.</returns>
    /// <response code="200">User approval status updated.</response>
    /// <response code="404">User not found.</response>
    [HttpPost("users/{userId}/approve")]
    [ProducesResponseType(typeof(SuccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SuccessResponse>> ApproveUser(
        [FromRoute] Guid userId,
        [FromBody] ApproveUserRequest request)
    {
        try
        {
            var adminId = GetCurrentUserId();
            _logger.LogInformation("Admin {AdminId} setting approval for user {UserId} to {Approve}",
                adminId, userId, request.Approve);

            await _adminService.ApproveUserAsync(userId, request.Approve);

            return Ok(new SuccessResponse
            {
                Message = request.Approve
                    ? "User approved successfully"
                    : "User approval revoked successfully"
            });
        }
        catch (AuthException ex) when (ex.StatusCode == 404)
        {
            return NotFound(new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving user {UserId}", userId);
            return StatusCode(500, new ErrorResponse
            {
                Message = "Failed to update user approval",
                Code = "SERVER_ERROR"
            });
        }
    }

    /// <summary>
    /// Change a user's role.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <param name="request">Role change request.</param>
    /// <returns>Success response.</returns>
    /// <response code="200">User role updated.</response>
    /// <response code="400">Invalid role.</response>
    /// <response code="404">User not found.</response>
    [HttpPost("users/{userId}/role")]
    [ProducesResponseType(typeof(SuccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SuccessResponse>> ChangeUserRole(
        [FromRoute] Guid userId,
        [FromBody] ChangeUserRoleRequest request)
    {
        try
        {
            // Validate role
            if (!Enum.TryParse<UserRole>(request.Role, out var newRole))
            {
                return BadRequest(new ErrorResponse
                {
                    Message = $"Invalid role: {request.Role}",
                    Code = "INVALID_ROLE"
                });
            }

            var adminId = GetCurrentUserId();
            
            // SECURITY: Prevent admin from demoting themselves
            if (adminId == userId && newRole != UserRole.Admin)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = "Cannot change your own role",
                    Code = "SELF_ROLE_CHANGE_NOT_ALLOWED"
                });
            }
            
            _logger.LogInformation("Admin {AdminId} changing role for user {UserId} to {Role}",
                adminId, userId, newRole);

            await _adminService.ChangeUserRoleAsync(userId, newRole);

            return Ok(new SuccessResponse
            {
                Message = $"User role changed to {newRole} successfully"
            });
        }
        catch (AuthException ex) when (ex.StatusCode == 404)
        {
            return NotFound(new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing role for user {UserId}", userId);
            return StatusCode(500, new ErrorResponse
            {
                Message = "Failed to change user role",
                Code = "SERVER_ERROR"
            });
        }
    }

    /// <summary>
    /// Delete a user permanently.
    /// </summary>
    /// <param name="userId">User ID.</param>
    /// <returns>Success response.</returns>
    /// <response code="200">User deleted successfully.</response>
    /// <response code="404">User not found.</response>
    [HttpDelete("users/{userId}")]
    [ProducesResponseType(typeof(SuccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<SuccessResponse>> DeleteUser([FromRoute] Guid userId)
    {
        try
        {
            var adminId = GetCurrentUserId();
            
            // Prevent self-deletion
            if (adminId == userId)
            {
                return BadRequest(new ErrorResponse
                {
                    Message = "Cannot delete your own account",
                    Code = "SELF_DELETE_NOT_ALLOWED"
                });
            }

            _logger.LogInformation("Admin {AdminId} deleting user {UserId}", adminId, userId);

            await _adminService.DeleteUserAsync(userId);

            return Ok(new SuccessResponse
            {
                Message = "User deleted successfully"
            });
        }
        catch (AuthException ex) when (ex.StatusCode == 404)
        {
            return NotFound(new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting user {UserId}", userId);
            return StatusCode(500, new ErrorResponse
            {
                Message = "Failed to delete user",
                Code = "SERVER_ERROR"
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
