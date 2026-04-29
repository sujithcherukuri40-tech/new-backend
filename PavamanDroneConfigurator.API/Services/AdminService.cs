using Microsoft.EntityFrameworkCore;
using PavamanDroneConfigurator.API.Data;
using PavamanDroneConfigurator.API.Data.Entities;
using PavamanDroneConfigurator.API.DTOs;
using PavamanDroneConfigurator.API.Exceptions;
using PavamanDroneConfigurator.API.Models;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Infrastructure.Services;

namespace PavamanDroneConfigurator.API.Services;

/// <summary>
/// Service for admin user management operations.
/// </summary>
public class AdminService : IAdminService
{
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IEmailService _emailService;
    private readonly AwsS3Service _s3Service;
    private readonly ILogger<AdminService> _logger;

    public AdminService(
        AppDbContext context,
        ITokenService tokenService,
        IEmailService emailService,
        AwsS3Service s3Service,
        ILogger<AdminService> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _emailService = emailService;
        _s3Service = s3Service;
        _logger = logger;
    }

    public async Task<UsersListResponse> GetAllUsersAsync()
    {
        var users = await _context.Users
            .OrderByDescending(u => u.CreatedAt)
            .Select(u => new UserListItemDto
            {
                Id = u.Id.ToString(),
                FullName = u.FullName,
                Email = u.Email,
                IsApproved = u.IsApproved,
                Role = u.Role.ToString(),
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt
            })
            .ToListAsync();

        _logger.LogInformation("Retrieved {Count} users for admin panel", users.Count);

        return new UsersListResponse
        {
            Users = users,
            TotalCount = users.Count
        };
    }

    public async Task<bool> ApproveUserAsync(Guid userId, bool approve)
    {
        var user = await _context.Users.FindAsync(userId);

        if (user == null)
        {
            _logger.LogWarning("Cannot approve user {UserId} - user not found", userId);
            throw new AuthException("User not found", "USER_NOT_FOUND", 404);
        }

        if (user.IsApproved == approve)
        {
            // Already in desired state
            return true;
        }

        user.IsApproved = approve;
        await _context.SaveChangesAsync();

        // If disapproving, revoke all tokens
        if (!approve)
        {
            await _tokenService.RevokeAllUserTokensAsync(userId, "User approval revoked by admin");
        }

        // Send email notification (non-critical — don't fail if email fails)
        try
        {
            await _emailService.SendApprovalEmailAsync(user.Email, user.FullName, approve);
        }
        catch (Exception emailEx)
        {
            _logger.LogWarning(emailEx, "Failed to send approval email to user {UserId} ({Email})", userId, user.Email);
        }

        _logger.LogInformation("User {UserId} ({Email}) approval set to {Approved} by admin",
            userId, user.Email, approve);

        return true;
    }

    public async Task<bool> ChangeUserRoleAsync(Guid userId, UserRole newRole)
    {
        var user = await _context.Users.FindAsync(userId);

        if (user == null)
        {
            _logger.LogWarning("Cannot change role for user {UserId} - user not found", userId);
            throw new AuthException("User not found", "USER_NOT_FOUND", 404);
        }

        if (user.Role == newRole)
        {
            // Already has this role
            return true;
        }

        user.Role = newRole;
        await _context.SaveChangesAsync();

        // Revoke all tokens so user must re-authenticate with new role claims
        await _tokenService.RevokeAllUserTokensAsync(userId, $"Role changed to {newRole} by admin");

        _logger.LogInformation("User {UserId} ({Email}) role changed to {Role} by admin",
            userId, user.Email, newRole);

        return true;
    }

    public async Task<bool> DeleteUserAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId);

        if (user == null)
        {
            _logger.LogWarning("Cannot delete user {UserId} - user not found", userId);
            throw new AuthException("User not found", "USER_NOT_FOUND", 404);
        }

        // Revoke all tokens first
        await _tokenService.RevokeAllUserTokensAsync(userId, "User deleted by admin");

        // Remove the user
        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} ({Email}) deleted by admin", userId, user.Email);

        return true;
    }

    public async Task<User?> GetUserByIdAsync(Guid userId)
    {
        return await _context.Users.FindAsync(userId);
    }

    public async Task<UserFirmwareResponse> AssignFirmwareToUserAsync(
        Guid userId, Guid adminId, string s3Key, string fileName,
        string firmwareName, string firmwareVersion, string? description,
        string vehicleType, long fileSize, string? displayName)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new AuthException("User not found", "USER_NOT_FOUND", 404);

        // Check if already assigned
        var existing = await _context.UserFirmwares
            .FirstOrDefaultAsync(f => f.UserId == userId && f.S3Key == s3Key && f.IsActive);
        if (existing != null)
            throw new InvalidOperationException("This firmware is already assigned to the user");

        var entity = new UserFirmwareEntity
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            S3Key = s3Key,
            FileName = fileName,
            DisplayName = displayName ?? fileName,
            FirmwareName = firmwareName,
            FirmwareVersion = firmwareVersion,
            Description = description,
            VehicleType = vehicleType,
            FileSize = fileSize,
            IsActive = true,
            UploadedAt = DateTime.UtcNow,
            UploadedBy = adminId,
            AssignedAt = DateTime.UtcNow,
            DownloadCount = 0
        };

        _context.UserFirmwares.Add(entity);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Admin {AdminId} assigned firmware {S3Key} to user {UserId}", adminId, s3Key, userId);

        // Send email notification (non-critical)
        try
        {
            await _emailService.SendFirmwareAssignmentEmailAsync(user.Email, user.FullName, firmwareName, firmwareVersion);
        }
        catch (Exception emailEx)
        {
            _logger.LogWarning(emailEx, "Failed to send firmware assignment email to {Email}", user.Email);
        }

        var adminUser = await _context.Users.FindAsync(adminId);
        return MapToFirmwareResponse(entity, user, adminUser);
    }

    public async Task<List<UserFirmwareResponse>> GetUserFirmwaresAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new AuthException("User not found", "USER_NOT_FOUND", 404);

        var firmwares = await _context.UserFirmwares
            .Where(f => f.UserId == userId && f.IsActive)
            .OrderByDescending(f => f.AssignedAt)
            .ToListAsync();

        var adminIds = firmwares.Select(f => f.UploadedBy).Distinct().ToList();
        var adminUsers = await _context.Users
            .Where(u => adminIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id);

        return firmwares.Select(f =>
        {
            adminUsers.TryGetValue(f.UploadedBy, out var adminUser);
            return MapToFirmwareResponse(f, user, adminUser);
        }).ToList();
    }

    public async Task<List<UserFirmwareResponse>> GetMyFirmwaresAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId)
            ?? throw new AuthException("User not found", "USER_NOT_FOUND", 404);

        var firmwares = await _context.UserFirmwares
            .Where(f => f.UserId == userId && f.IsActive)
            .OrderByDescending(f => f.AssignedAt)
            .ToListAsync();

        return firmwares.Select(f => MapToFirmwareResponse(f, user, null)).ToList();
    }

    public async Task<bool> RemoveUserFirmwareAsync(Guid userId, Guid firmwareId)
    {
        var entity = await _context.UserFirmwares
            .FirstOrDefaultAsync(f => f.Id == firmwareId && f.UserId == userId && f.IsActive);

        if (entity == null)
            throw new AuthException("Firmware assignment not found", "FIRMWARE_NOT_FOUND", 404);

        entity.IsActive = false;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Firmware assignment {FirmwareId} removed from user {UserId}", firmwareId, userId);
        return true;
    }

    private UserFirmwareResponse MapToFirmwareResponse(UserFirmwareEntity entity, User user, User? uploadedByUser)
    {
        return new UserFirmwareResponse
        {
            Id = entity.Id,
            UserId = entity.UserId,
            UserName = user.FullName,
            UserEmail = user.Email,
            S3Key = entity.S3Key,
            FileName = entity.FileName,
            DisplayName = entity.DisplayName,
            FirmwareName = entity.FirmwareName,
            FirmwareVersion = entity.FirmwareVersion,
            Description = entity.Description,
            VehicleType = entity.VehicleType ?? "Copter",
            FileSize = entity.FileSize,
            FileSizeDisplay = FormatFileSize(entity.FileSize),
            UploadedAt = entity.UploadedAt,
            UploadedBy = entity.UploadedBy,
            UploadedByName = uploadedByUser?.FullName ?? "Admin",
            AssignedAt = entity.AssignedAt,
            IsActive = entity.IsActive,
            LastDownloaded = entity.LastDownloaded,
            DownloadCount = entity.DownloadCount
        };
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024):F1} MB";
    }
}
