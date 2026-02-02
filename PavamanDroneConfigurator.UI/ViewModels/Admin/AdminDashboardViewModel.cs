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

// <summary>
/// ViewModel for admin dashboard - user management CRM.
/// </summary>
public sealed partial class AdminDashboardViewModel : ViewModelBase
{
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminDashboardViewModel> _logger;
    private bool _isInitialized;

    [ObservableProperty]
    private ObservableCollection<UserListItem> _users = new();

    [ObservableProperty]
    private ObservableCollection<UserListItem> _filteredUsers = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _statusFilter = 0; // All

    [ObservableProperty]
    private int _roleFilter = 0; // All

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    // Stats
    public int TotalCount => Users.Count;
    public int PendingCount => Users.Count(u => !u.IsApproved);
    public int ApprovedCount => Users.Count(u => u.IsApproved);
    public int AdminCount => Users.Count(u => u.Role == "Admin");
    public int UserCount => Users.Count(u => u.Role == "User");

    public bool ShowEmptyState => !IsBusy && FilteredUsers.Count == 0 && _isInitialized;

    public AdminDashboardViewModel(
        IAdminService adminService,
        ILogger<AdminDashboardViewModel> logger)
    {
        _adminService = adminService;
        _logger = logger;

        PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SearchText) || 
                e.PropertyName == nameof(StatusFilter) || 
                e.PropertyName == nameof(RoleFilter))
            {
                ApplyFilters();
            }
        };
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;
        
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
                        SelectedRole = user.Role,
                        CreatedAt = user.CreatedAt,
                        LastLoginAt = user.LastLoginAt
                    });
                }

                ApplyFilters();
                NotifyStatsChanged();
            });

            StatusMessage = $"? Loaded {Users.Count} users";
            _logger.LogInformation("Loaded {Count} users ({Pending} pending)", Users.Count, PendingCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load users");
            StatusMessage = "? Failed to load users";
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
        var newState = !user.IsApproved;
        StatusMessage = $"{(newState ? "Approving" : "Revoking")} {user.FullName}...";

        try
        {
            if (newState && user.SelectedRole != user.Role)
            {
                var roleSuccess = await _adminService.ChangeUserRoleAsync(user.Id, user.SelectedRole);
                if (!roleSuccess)
                {
                    StatusMessage = "? Failed to update role";
                    return;
                }
            }

            var success = await _adminService.ApproveUserAsync(user.Id, newState);

            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    user.IsApproved = newState;
                    if (newState) user.Role = user.SelectedRole;
                    NotifyStatsChanged();
                });

                StatusMessage = newState 
                    ? $"? {user.FullName} approved as {user.SelectedRole}" 
                    : $"? {user.FullName}'s access revoked";
            }
            else
            {
                StatusMessage = "? Operation failed";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update user");
            StatusMessage = $"? Failed to update {user.FullName}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpdateRoleAsync(UserListItem user)
    {
        if (IsBusy || user == null || user.SelectedRole == user.Role) return;

        IsBusy = true;
        StatusMessage = $"Changing {user.FullName}'s role...";

        try
        {
            var success = await _adminService.ChangeUserRoleAsync(user.Id, user.SelectedRole);

            if (success)
            {
                user.Role = user.SelectedRole;
                NotifyStatsChanged();
                StatusMessage = $"? {user.FullName} is now {user.SelectedRole}";
            }
            else
            {
                user.SelectedRole = user.Role;
                StatusMessage = "? Role change failed";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change role");
            user.SelectedRole = user.Role;
            StatusMessage = "? Failed to change role";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteUserAsync(UserListItem user)
    {
        if (IsBusy || user == null) return;

        IsBusy = true;
        StatusMessage = $"Deleting {user.FullName}...";

        try
        {
            var success = await _adminService.DeleteUserAsync(user.Id);

            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Users.Remove(user);
                    FilteredUsers.Remove(user);
                    NotifyStatsChanged();
                });

                StatusMessage = $"? {user.FullName} has been deleted";
                _logger.LogInformation("Deleted user {Email}", user.Email);
            }
            else
            {
                StatusMessage = "? Failed to delete user";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete user {Email}", user.Email);
            StatusMessage = $"? Failed to delete {user.FullName}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        StatusFilter = 0;
        RoleFilter = 0;
        StatusMessage = "Filters cleared";
    }

    private void ApplyFilters()
    {
        var filtered = Users.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLowerInvariant();
            filtered = filtered.Where(u => 
                u.FullName.ToLowerInvariant().Contains(search) ||
                u.Email.ToLowerInvariant().Contains(search));
        }

        if (StatusFilter == 1) filtered = filtered.Where(u => u.IsApproved);
        if (StatusFilter == 2) filtered = filtered.Where(u => !u.IsApproved);
        if (RoleFilter == 1) filtered = filtered.Where(u => u.Role == "Admin");
        if (RoleFilter == 2) filtered = filtered.Where(u => u.Role == "User");

        FilteredUsers.Clear();
        foreach (var user in filtered) FilteredUsers.Add(user);

        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void NotifyStatsChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(ApprovedCount));
        OnPropertyChanged(nameof(AdminCount));
        OnPropertyChanged(nameof(UserCount));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));
}

public sealed partial class UserListItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isApproved;

    [ObservableProperty]
    private string _role = "User";

    [ObservableProperty]
    private string _selectedRole = "User";

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    public string[] AvailableRoles { get; } = ["User", "Admin"];

    public string StatusText => IsApproved ? "Approved" : "Pending";
    public string StatusColor => IsApproved ? "#16A34A" : "#F59E0B";
    public string ApprovalButtonText => IsApproved ? "Revoke" : "Approve";
    public string LastLoginDisplay => LastLoginAt.HasValue 
        ? LastLoginAt.Value.LocalDateTime.ToString("MMM dd, yyyy HH:mm") 
        : "Never";

    partial void OnIsApprovedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(ApprovalButtonText));
    }

    partial void OnSelectedRoleChanged(string value) => OnPropertyChanged(nameof(ApprovalButtonText));
}
