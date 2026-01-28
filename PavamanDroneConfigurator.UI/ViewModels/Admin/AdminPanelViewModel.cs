using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.UI.ViewModels.Admin;

/// <summary>
/// ViewModel for admin panel - user management.
/// </summary>
public sealed partial class AdminPanelViewModel : ViewModelBase
{
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminPanelViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<UserListItem> _users = new();

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public AdminPanelViewModel(
        IAdminService adminService,
        ILogger<AdminPanelViewModel> logger)
    {
        _adminService = adminService;
        _logger = logger;
    }

    /// <summary>
    /// Initialize and load users.
    /// </summary>
    public async Task InitializeAsync()
    {
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Loading users...";
        try
        {
            var response = await _adminService.GetAllUsersAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Users.Clear();
                foreach (var user in response.Users)
                {
                    Users.Add(new UserListItem
                    {
                        Id = user.Id,
                        FullName = user.FullName,
                        Email = user.Email,
                        IsApproved = user.IsApproved,
                        Role = user.Role,
                        CreatedAt = user.CreatedAt,
                        LastLoginAt = user.LastLoginAt
                    });
                }
            });

            StatusMessage = $"Loaded {Users.Count} users";
            _logger.LogInformation("Loaded {Count} users in admin panel", Users.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load users");
            StatusMessage = "Failed to load users";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ToggleApprovalAsync(UserListItem user)
    {
        if (IsBusy || user == null) return;

        IsBusy = true;
        var newApprovalState = !user.IsApproved;
        StatusMessage = $"{(newApprovalState ? "Approving" : "Disapproving")} {user.FullName}...";

        try
        {
            var success = await _adminService.ApproveUserAsync(user.Id, newApprovalState);

            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    user.IsApproved = newApprovalState;
                });

                StatusMessage = $"{user.FullName} {(newApprovalState ? "approved" : "disapproved")} successfully";
                _logger.LogInformation("User {UserId} approval set to {Approved}", user.Id, newApprovalState);
            }
            else
            {
                StatusMessage = "Operation failed";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to toggle approval for user {UserId}", user.Id);
            StatusMessage = $"Failed to update {user.FullName}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ChangeRoleAsync(UserListItem user)
    {
        if (IsBusy || user == null) return;

        // Toggle between User and Admin
        var newRole = user.Role == "Admin" ? "User" : "Admin";

        IsBusy = true;
        StatusMessage = $"Changing {user.FullName}'s role to {newRole}...";

        try
        {
            var success = await _adminService.ChangeUserRoleAsync(user.Id, newRole);

            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    user.Role = newRole;
                });

                StatusMessage = $"{user.FullName} is now {newRole}";
                _logger.LogInformation("User {UserId} role changed to {Role}", user.Id, newRole);
            }
            else
            {
                StatusMessage = "Role change failed";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change role for user {UserId}", user.Id);
            StatusMessage = $"Failed to change role for {user.FullName}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>
/// User list item for display in admin panel.
/// </summary>
public sealed partial class UserListItem : ObservableObject
{
    public required string Id { get; init; }

    public required string FullName { get; init; }

    public required string Email { get; init; }

    [ObservableProperty]
    private bool _isApproved;

    [ObservableProperty]
    private string _role = "User";

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastLoginAt { get; init; }
}
