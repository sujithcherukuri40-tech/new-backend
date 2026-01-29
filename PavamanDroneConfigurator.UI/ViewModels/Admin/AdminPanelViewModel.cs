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

    /// <summary>
    /// Count of users pending approval.
    /// </summary>
    public int PendingCount => Users.Count(u => !u.IsApproved);

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
                        SelectedRole = user.Role, // Set initial selected role
                        CreatedAt = user.CreatedAt,
                        LastLoginAt = user.LastLoginAt
                    });
                }

                OnPropertyChanged(nameof(PendingCount));
            });

            StatusMessage = $"Loaded {Users.Count} users ({PendingCount} pending approval)";
            _logger.LogInformation("Loaded {Count} users in admin panel ({Pending} pending)", Users.Count, PendingCount);
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
    private async Task ApproveUserAsync(UserListItem user)
    {
        if (IsBusy || user == null) return;

        IsBusy = true;
        var newApprovalState = !user.IsApproved;
        StatusMessage = $"{(newApprovalState ? "Approving" : "Disapproving")} {user.FullName}...";

        try
        {
            // If approving a new user, update their role first if it was changed
            if (newApprovalState && user.SelectedRole != user.Role)
            {
                var roleSuccess = await _adminService.ChangeUserRoleAsync(user.Id, user.SelectedRole);
                if (!roleSuccess)
                {
                    StatusMessage = "Failed to update role";
                    return;
                }
            }

            var success = await _adminService.ApproveUserAsync(user.Id, newApprovalState);

            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    user.IsApproved = newApprovalState;
                    if (newApprovalState)
                    {
                        user.Role = user.SelectedRole; // Update displayed role
                    }
                    OnPropertyChanged(nameof(PendingCount));
                });

                StatusMessage = newApprovalState 
                    ? $"? {user.FullName} approved as {user.SelectedRole}" 
                    : $"? {user.FullName}'s access revoked";
                
                _logger.LogInformation("User {UserId} approval set to {Approved} with role {Role}", 
                    user.Id, newApprovalState, user.SelectedRole);
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
    private async Task UpdateRoleAsync(UserListItem user)
    {
        if (IsBusy || user == null) return;

        // Check if role was actually changed
        if (user.SelectedRole == user.Role)
        {
            StatusMessage = $"{user.FullName} already has {user.Role} role";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Changing {user.FullName}'s role to {user.SelectedRole}...";

        try
        {
            var success = await _adminService.ChangeUserRoleAsync(user.Id, user.SelectedRole);

            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    user.Role = user.SelectedRole;
                });

                StatusMessage = $"? {user.FullName} is now {user.SelectedRole}";
                _logger.LogInformation("User {UserId} role changed to {Role}", user.Id, user.SelectedRole);
            }
            else
            {
                // Revert selection on failure
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    user.SelectedRole = user.Role;
                });
                StatusMessage = "Role change failed";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change role for user {UserId}", user.Id);
            
            // Revert selection on error
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                user.SelectedRole = user.Role;
            });
            
            StatusMessage = $"Failed to change role for {user.FullName}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnUsersChanged(ObservableCollection<UserListItem> value)
    {
        OnPropertyChanged(nameof(PendingCount));
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

    [ObservableProperty]
    private string _selectedRole = "User";

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? LastLoginAt { get; init; }

    /// <summary>
    /// Available roles for selection.
    /// </summary>
    public string[] AvailableRoles { get; } = new[] { "User", "Admin" };

    /// <summary>
    /// Status text for display.
    /// </summary>
    public string StatusText => IsApproved ? "Approved" : "Pending";

    /// <summary>
    /// Status color for display.
    /// </summary>
    public string StatusColor => IsApproved ? "#16A34A" : "#F59E0B";

    /// <summary>
    /// Approval button text.
    /// </summary>
    public string ApprovalButtonText => IsApproved ? "? Revoke" : "? Approve";

    partial void OnIsApprovedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(ApprovalButtonText));
    }
}
