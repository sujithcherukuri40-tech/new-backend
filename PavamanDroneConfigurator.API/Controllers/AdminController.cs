using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PavamanDroneConfigurator.API.DTOs;
using PavamanDroneConfigurator.API.Exceptions;
using PavamanDroneConfigurator.API.Models;
using PavamanDroneConfigurator.API.Services;
using PavamanDroneConfigurator.Infrastructure.Services;

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
    private readonly AwsS3Service _s3Service;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IAdminService adminService, AwsS3Service s3Service, ILogger<AdminController> logger)
    {
        _adminService = adminService;
        _s3Service = s3Service;
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

    // =====================================================================
    // FIRMWARE ASSIGNMENT ENDPOINTS
    // =====================================================================

    /// <summary>
    /// Assign an existing S3 firmware to a user.
    /// POST /admin/users/{userId}/firmware
    /// </summary>
    [HttpPost("users/{userId}/firmware")]
    [ProducesResponseType(typeof(UserFirmwareResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserFirmwareResponse>> AssignFirmwareToUser(
        [FromRoute] Guid userId,
        [FromBody] AssignFirmwareRequest request)
    {
        try
        {
            var adminId = GetCurrentUserId();
            if (adminId == null)
                return Unauthorized(new ErrorResponse { Message = "Invalid token", Code = "INVALID_TOKEN" });

            if (string.IsNullOrWhiteSpace(request.S3Key))
                return BadRequest(new ErrorResponse { Message = "S3Key is required", Code = "INVALID_REQUEST" });

            // Fetch firmware metadata from S3 to get file details
            var allFirmwares = await _s3Service.ListFirmwareFilesAsync();
            var s3Info = allFirmwares.FirstOrDefault(f => f.Key == request.S3Key);
            if (s3Info == null)
                return NotFound(new ErrorResponse { Message = "Firmware not found in storage", Code = "FIRMWARE_NOT_FOUND" });

            var response = await _adminService.AssignFirmwareToUserAsync(
                userId, adminId.Value,
                s3Info.Key, s3Info.FileName,
                s3Info.FirmwareName ?? s3Info.FileName,
                s3Info.FirmwareVersion ?? "Unknown",
                s3Info.FirmwareDescription,
                s3Info.VehicleType ?? "Copter",
                s3Info.Size,
                s3Info.DisplayName);

            response.DownloadUrl = _s3Service.GeneratePresignedUrl(response.S3Key, TimeSpan.FromHours(1));

            _logger.LogInformation("Admin {AdminId} assigned firmware {S3Key} to user {UserId}", adminId, request.S3Key, userId);
            return Ok(response);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new ErrorResponse { Message = ex.Message, Code = "ALREADY_ASSIGNED" });
        }
        catch (AuthException ex) when (ex.StatusCode == 404)
        {
            return NotFound(new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error assigning firmware to user {UserId}", userId);
            return StatusCode(500, new ErrorResponse { Message = "Failed to assign firmware", Code = "SERVER_ERROR" });
        }
    }

    /// <summary>
    /// Upload firmware and directly assign to a user.
    /// POST /admin/users/{userId}/firmware/upload
    /// </summary>
    [HttpPost("users/{userId}/firmware/upload")]
    [RequestSizeLimit(52_428_800)]
    [ProducesResponseType(typeof(UserFirmwareResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserFirmwareResponse>> UploadAndAssignFirmware(
        [FromRoute] Guid userId,
        [FromForm] IFormFile file,
        [FromForm] string? firmwareName,
        [FromForm] string? firmwareVersion,
        [FromForm] string? description,
        [FromForm] string? vehicleType,
        CancellationToken cancellationToken)
    {
        try
        {
            var adminId = GetCurrentUserId();
            if (adminId == null)
                return Unauthorized(new ErrorResponse { Message = "Invalid token", Code = "INVALID_TOKEN" });

            if (file == null || file.Length == 0)
                return BadRequest(new ErrorResponse { Message = "No file uploaded", Code = "NO_FILE" });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            string[] allowed = { ".apj", ".px4", ".bin", ".hex" };
            if (!allowed.Contains(ext))
                return BadRequest(new ErrorResponse { Message = $"Invalid file type. Allowed: {string.Join(", ", allowed)}", Code = "INVALID_FILE_TYPE" });

            if (file.Length > 50 * 1024 * 1024)
                return BadRequest(new ErrorResponse { Message = "File too large (max 50MB)", Code = "FILE_TOO_LARGE" });

            var sanitized = SanitizeFileName(file.FileName);
            if (string.IsNullOrEmpty(sanitized))
                return BadRequest(new ErrorResponse { Message = "Invalid file name", Code = "INVALID_FILE_NAME" });

            var tempPath = Path.GetTempFileName();
            try
            {
                using (var fs = new FileStream(tempPath, FileMode.Create))
                    await file.CopyToAsync(fs, cancellationToken);

                var s3Info = await _s3Service.UploadFirmwareAsync(
                    tempPath, sanitized, firmwareName, firmwareVersion, description, cancellationToken);

                var response = await _adminService.AssignFirmwareToUserAsync(
                    userId, adminId.Value,
                    s3Info.Key, s3Info.FileName,
                    firmwareName ?? s3Info.FirmwareName ?? s3Info.FileName,
                    firmwareVersion ?? s3Info.FirmwareVersion ?? "Unknown",
                    description ?? s3Info.FirmwareDescription,
                    vehicleType ?? s3Info.VehicleType ?? "Copter",
                    s3Info.Size,
                    s3Info.DisplayName);

                response.DownloadUrl = _s3Service.GeneratePresignedUrl(response.S3Key, TimeSpan.FromHours(1));
                return Ok(response);
            }
            finally
            {
                try { System.IO.File.Delete(tempPath); } catch { }
            }
        }
        catch (AuthException ex) when (ex.StatusCode == 404)
        {
            return NotFound(new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading and assigning firmware to user {UserId}", userId);
            return StatusCode(500, new ErrorResponse { Message = "Failed to upload firmware", Code = "SERVER_ERROR" });
        }
    }

    /// <summary>
    /// Get all firmwares assigned to a user.
    /// GET /admin/users/{userId}/firmware
    /// </summary>
    [HttpGet("users/{userId}/firmware")]
    [ProducesResponseType(typeof(List<UserFirmwareResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<UserFirmwareResponse>>> GetUserFirmwares([FromRoute] Guid userId)
    {
        try
        {
            var firmwares = await _adminService.GetUserFirmwaresAsync(userId);

            // Generate fresh presigned download URLs
            foreach (var f in firmwares)
                f.DownloadUrl = _s3Service.GeneratePresignedUrl(f.S3Key, TimeSpan.FromHours(1));

            return Ok(firmwares);
        }
        catch (AuthException ex) when (ex.StatusCode == 404)
        {
            return NotFound(new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting firmwares for user {UserId}", userId);
            return StatusCode(500, new ErrorResponse { Message = "Failed to get user firmwares", Code = "SERVER_ERROR" });
        }
    }

    /// <summary>
    /// Remove a firmware assignment from a user.
    /// DELETE /admin/users/{userId}/firmware/{firmwareId}
    /// </summary>
    [HttpDelete("users/{userId}/firmware/{firmwareId}")]
    [ProducesResponseType(typeof(SuccessResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SuccessResponse>> RemoveUserFirmware(
        [FromRoute] Guid userId,
        [FromRoute] Guid firmwareId)
    {
        try
        {
            await _adminService.RemoveUserFirmwareAsync(userId, firmwareId);
            var adminId = GetCurrentUserId();
            _logger.LogInformation("Admin {AdminId} removed firmware {FirmwareId} from user {UserId}", adminId, firmwareId, userId);
            return Ok(new SuccessResponse { Message = "Firmware assignment removed successfully" });
        }
        catch (AuthException ex) when (ex.StatusCode == 404)
        {
            return NotFound(new ErrorResponse { Message = ex.Message, Code = ex.Code });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing firmware {FirmwareId} from user {UserId}", firmwareId, userId);
            return StatusCode(500, new ErrorResponse { Message = "Failed to remove firmware assignment", Code = "SERVER_ERROR" });
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var sanitized = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9_\-\.]", "_");
        return string.IsNullOrEmpty(sanitized) ? string.Empty : sanitized + ext;
    }
}
