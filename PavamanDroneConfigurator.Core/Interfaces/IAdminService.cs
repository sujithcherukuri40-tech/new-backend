using PavamanDroneConfigurator.Core.Models.Auth;

namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Service interface for admin operations (frontend).
/// </summary>
public interface IAdminService
{
    /// <summary>
    /// Get all users for admin panel.
    /// </summary>
    Task<AdminUsersResponse> GetAllUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Approve or disapprove a user.
    /// </summary>
    Task<bool> ApproveUserAsync(string userId, bool approve, CancellationToken cancellationToken = default);

    /// <summary>
    /// Change a user's role.
    /// </summary>
    Task<bool> ChangeUserRoleAsync(string userId, string newRole, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a user permanently.
    /// </summary>
    Task<bool> DeleteUserAsync(string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Response model for admin users list.
/// </summary>
public sealed record AdminUsersResponse
{
    public required List<AdminUserListItem> Users { get; init; }
    public int TotalCount { get; init; }
}

/// <summary>
/// User list item for admin panel.
/// </summary>
public sealed record AdminUserListItem
{
    public required string Id { get; init; }
    public required string FullName { get; init; }
    public required string Email { get; init; }
    public bool IsApproved { get; init; }
    public required string Role { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
}
