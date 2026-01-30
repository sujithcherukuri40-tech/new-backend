namespace PavamanDroneConfigurator.API.DTOs;

/// <summary>
/// DTO for user list in admin panel.
/// </summary>
public class UserListItemDto
{
    public required string Id { get; set; }
    public required string FullName { get; set; }
    public required string Email { get; set; }
    public bool IsApproved { get; set; }
    public required string Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
}

/// <summary>
/// Response for admin users list.
/// </summary>
public class UsersListResponse
{
    public required List<UserListItemDto> Users { get; set; }
    public int TotalCount { get; set; }
}

/// <summary>
/// Request to approve a user.
/// </summary>
public class ApproveUserRequest
{
    public bool Approve { get; set; } = true;
}

/// <summary>
/// Request to change user role.
/// </summary>
public class ChangeUserRoleRequest
{
    public required string Role { get; set; }
}

/// <summary>
/// Generic success response.
/// </summary>
public class SuccessResponse
{
    public required string Message { get; set; }
}
