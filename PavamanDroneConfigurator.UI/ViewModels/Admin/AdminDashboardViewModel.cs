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
/// ViewModel for admin dashboard - user management CRM.
/// </summary>
public sealed partial class AdminDashboardViewModel : ViewModelBase
{
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminDashboardViewModel> _logger;
    private bool _isInitialized;
    private string? _currentUserId; // Track logged-in admin

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

    // Add User Dialog
    [ObservableProperty]
    private bool _isAddUserDialogOpen;

    [ObservableProperty]
    private string _newUserFullName = string.Empty;

    [ObservableProperty]
    private string _newUserEmail = string.Empty;

    [ObservableProperty]
    private string _newUserRole = "User";

    [ObservableProperty]
    private string _newUserEmailError = string.Empty;

    [ObservableProperty]
    private string _newUserFullNameError = string.Empty;

    // Delete User Confirmation Dialog
    [ObservableProperty]
    private bool _isDeleteConfirmationDialogOpen;

    [ObservableProperty]
    private UserListItem? _userToDelete;

    public int PendingCount => FilteredUsers.Count(u => !u.IsApproved);

    public bool ShowEmptyState => !IsBusy && FilteredUsers.Count == 0 && _isInitialized;

    public string[] AvailableRolesForNewUser { get; } = new[] { "User", "Admin" };

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
                        LastLoginAt = user.LastLoginAt,
                        IsCurrentUser = false // TODO: Get from auth session
                    });
                }

                ApplyFilters();
            });

            StatusMessage = $"Loaded {Users.Count} users ({PendingCount} pending approval)";
            _logger.LogInformation("Loaded {Count} users ({Pending} pending)", Users.Count, PendingCount);
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
        var newState = !user.IsApproved;
        StatusMessage = $"{(newState ? "Approving" : "Disapproving")} {user.FullName}...";

        try
        {
            if (newState && user.SelectedRole != user.Role)
            {
                var roleSuccess = await _adminService.ChangeUserRoleAsync(user.Id, user.SelectedRole);
                if (!roleSuccess)
                {
                    StatusMessage = "Failed to update role";
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
                    OnPropertyChanged(nameof(PendingCount));
                });

                StatusMessage = newState 
                    ? $"? {user.FullName} approved as {user.SelectedRole}" 
                    : $"?? {user.FullName}'s access revoked";
            }
            else
            {
                StatusMessage = "Operation failed";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update user");
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
        if (IsBusy || user == null || user.SelectedRole == user.Role) return;

        IsBusy = true;
        StatusMessage = $"Changing {user.FullName}'s role...";

        try
        {
            var success = await _adminService.ChangeUserRoleAsync(user.Id, user.SelectedRole);

            if (success)
            {
                user.Role = user.SelectedRole;
                StatusMessage = $"? {user.FullName} is now {user.SelectedRole}";
            }
            else
            {
                user.SelectedRole = user.Role;
                StatusMessage = "Role change failed";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change role");
            user.SelectedRole = user.Role;
            StatusMessage = "Failed to change role";
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

    #region Add User

    [RelayCommand]
    private void OpenAddUserDialog()
    {
        // Reset form
        NewUserFullName = string.Empty;
        NewUserEmail = string.Empty;
        NewUserRole = "User";
        NewUserEmailError = string.Empty;
        NewUserFullNameError = string.Empty;
        
        IsAddUserDialogOpen = true;
        _logger.LogInformation("Add User dialog opened");
    }

    [RelayCommand]
    private void CancelAddUser()
    {
        IsAddUserDialogOpen = false;
        _logger.LogInformation("Add User dialog cancelled");
    }

    [RelayCommand]
    private async Task CreateUserAsync()
    {
        // Validation
        NewUserFullNameError = string.Empty;
        NewUserEmailError = string.Empty;

        bool isValid = true;

        if (string.IsNullOrWhiteSpace(NewUserFullName))
        {
            NewUserFullNameError = "Full name is required";
            isValid = false;
        }

        if (string.IsNullOrWhiteSpace(NewUserEmail))
        {
            NewUserEmailError = "Email is required";
            isValid = false;
        }
        else if (!IsValidEmail(NewUserEmail))
        {
            NewUserEmailError = "Invalid email format";
            isValid = false;
        }
        else if (Users.Any(u => u.Email.Equals(NewUserEmail, StringComparison.OrdinalIgnoreCase)))
        {
            NewUserEmailError = "Email already exists";
            isValid = false;
        }

        if (!isValid) return;

        IsBusy = true;
        StatusMessage = $"Creating user {NewUserEmail}...";

        try
        {
            // Call service to create user (stub for now)
            var success = await CreateUserInBackendAsync(NewUserFullName, NewUserEmail, NewUserRole);

            if (success)
            {
                // Add to list immediately with Pending status
                var newUser = new UserListItem
                {
                    Id = Guid.NewGuid().ToString(), // Temporary ID until backend assigns real one
                    FullName = NewUserFullName,
                    Email = NewUserEmail,
                    Role = NewUserRole,
                    SelectedRole = NewUserRole,
                    IsApproved = false, // Pending by default
                    CreatedAt = DateTimeOffset.UtcNow,
                    LastLoginAt = null,
                    IsCurrentUser = false
                };

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Users.Add(newUser);
                    ApplyFilters();
                    OnPropertyChanged(nameof(PendingCount));
                });

                StatusMessage = $"? User {NewUserEmail} created successfully (Pending approval)";
                _logger.LogInformation("User created: {Email}", NewUserEmail);

                IsAddUserDialogOpen = false;

                // Refresh to get actual data from backend
                await Task.Delay(500);
                await RefreshAsync();
            }
            else
            {
                StatusMessage = "Failed to create user";
                NewUserEmailError = "User creation failed. Please try again.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create user");
            StatusMessage = "Failed to create user";
            NewUserEmailError = "An error occurred. Please try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> CreateUserInBackendAsync(string fullName, string email, string role)
    {
        // TODO: Implement actual API call
        // For now, simulate success
        await Task.Delay(500);
        return true;
        
        // Future implementation:
        // return await _adminService.CreateUserAsync(fullName, email, role);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Delete User

    [RelayCommand]
    private void OpenDeleteConfirmation(UserListItem user)
    {
        if (user == null) return;

        UserToDelete = user;
        IsDeleteConfirmationDialogOpen = true;
        _logger.LogInformation("Delete confirmation dialog opened for user: {Email}", user.Email);
    }

    [RelayCommand]
    private void CancelDelete()
    {
        IsDeleteConfirmationDialogOpen = false;
        UserToDelete = null;
    }

    [RelayCommand]
    private async Task ConfirmDeleteAsync()
    {
        if (UserToDelete == null) return;

        var userToDelete = UserToDelete;
        IsDeleteConfirmationDialogOpen = false;
        UserToDelete = null;

        IsBusy = true;
        StatusMessage = $"Deleting {userToDelete.FullName}...";

        try
        {
            // Call service to delete user (stub for now)
            var success = await DeleteUserInBackendAsync(userToDelete.Id);

            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Users.Remove(userToDelete);
                    ApplyFilters();
                    OnPropertyChanged(nameof(PendingCount));
                });

                StatusMessage = $"? User {userToDelete.Email} deleted successfully";
                _logger.LogInformation("User deleted: {Email}", userToDelete.Email);
            }
            else
            {
                StatusMessage = "Failed to delete user";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete user");
            StatusMessage = $"Failed to delete {userToDelete.FullName}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<bool> DeleteUserInBackendAsync(string userId)
    {
        // TODO: Implement actual API call
        // For now, simulate success
        await Task.Delay(500);
        return true;
        
        // Future implementation:
        // return await _adminService.DeleteUserAsync(userId);
    }

    #endregion

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

        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(ShowEmptyState));
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

    public bool IsCurrentUser { get; set; } // True if this is the logged-in admin
    public bool IsSystemAdmin => Email.Contains("@system") || Email.Contains("admin@"); // Simple heuristic

    public bool CanDelete => !IsCurrentUser && !IsSystemAdmin; // Disable delete for current user and system admin

    public string[] AvailableRoles { get; } = new[] { "User", "Admin" };

    public string StatusText => IsApproved ? "Approved" : "Pending";
    public string StatusColor => IsApproved ? "#16A34A" : "#F59E0B";
    public string ApprovalButtonText => IsApproved ? "?? Revoke Access" : $"? Approve as {SelectedRole}";

    partial void OnIsApprovedChanged(bool value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusColor));
        OnPropertyChanged(nameof(ApprovalButtonText));
    }

    partial void OnSelectedRoleChanged(string value) => OnPropertyChanged(nameof(ApprovalButtonText));
}
