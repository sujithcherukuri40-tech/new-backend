using PavamanDroneConfigurator.API.DTOs;
using PavamanDroneConfigurator.API.Models;

namespace PavamanDroneConfigurator.API.Services;

/// <summary>
/// Service interface for admin operations.
/// </summary>
public interface IAdminService
{
    /// <summary>
    /// Get all users for admin panel.
    /// </summary>
    Task<UsersListResponse> GetAllUsersAsync();

    /// <summary>
    /// Approve or disapprove a user.
    /// </summary>
    Task<bool> ApproveUserAsync(Guid userId, bool approve);

    /// <summary>
    /// Change a user's role.
    /// </summary>
    Task<bool> ChangeUserRoleAsync(Guid userId, UserRole newRole);

    /// <summary>
    /// Get a user by ID.
    /// </summary>
    Task<User?> GetUserByIdAsync(Guid userId);
}
