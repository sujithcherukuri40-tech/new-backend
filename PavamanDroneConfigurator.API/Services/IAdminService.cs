using PavamanDroneConfigurator.API.DTOs;
using PavamanDroneConfigurator.API.Models;

namespace PavamanDroneConfigurator.API.Services;

/// <summary>
/// Service interface for admin operations.
/// </summary>
public interface IAdminService
{
    Task<UsersListResponse> GetAllUsersAsync();
    Task<bool> ApproveUserAsync(Guid userId, bool approve);
    Task<bool> ChangeUserRoleAsync(Guid userId, UserRole newRole);
    Task<bool> DeleteUserAsync(Guid userId);
    Task<User?> GetUserByIdAsync(Guid userId);

    // Firmware assignment
    Task<UserFirmwareResponse> AssignFirmwareToUserAsync(
        Guid userId, Guid adminId, string s3Key, string fileName,
        string firmwareName, string firmwareVersion, string? description,
        string vehicleType, long fileSize, string? displayName);
    Task<List<UserFirmwareResponse>> GetUserFirmwaresAsync(Guid userId);
    Task<List<UserFirmwareResponse>> GetMyFirmwaresAsync(Guid userId);
    Task<bool> RemoveUserFirmwareAsync(Guid userId, Guid firmwareId);
}
