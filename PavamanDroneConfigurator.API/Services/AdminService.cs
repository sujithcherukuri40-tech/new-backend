using Microsoft.EntityFrameworkCore;
using PavamanDroneConfigurator.API.Data;
using PavamanDroneConfigurator.API.DTOs;
using PavamanDroneConfigurator.API.Exceptions;
using PavamanDroneConfigurator.API.Models;

namespace PavamanDroneConfigurator.API.Services;

/// <summary>
/// Service for admin user management operations.
/// </summary>
public class AdminService : IAdminService
{
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AdminService> _logger;

    public AdminService(
        AppDbContext context,
        ITokenService tokenService,
        ILogger<AdminService> logger)
    {
        _context = context;
        _tokenService = tokenService;
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

        _logger.LogInformation("User {UserId} ({Email}) role changed to {Role} by admin",
            userId, user.Email, newRole);

        return true;
    }

    public async Task<User?> GetUserByIdAsync(Guid userId)
    {
        return await _context.Users.FindAsync(userId);
    }
}
