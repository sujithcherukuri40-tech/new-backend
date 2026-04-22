using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Infrastructure.Services;
using PavamanDroneConfigurator.Infrastructure.Services.Auth;

namespace PavamanDroneConfigurator.UI.ViewModels.Admin;

// <summary>
/// ViewModel for admin dashboard - user management CRM.
/// </summary>
public sealed partial class AdminDashboardViewModel : ViewModelBase
{
    private readonly IAdminService _adminService;
    private readonly FirmwareApiService? _firmwareApiService;
    private readonly ILogger<AdminDashboardViewModel> _logger;
    private readonly ITokenStorage _tokenStorage;
    private bool _isInitialized;
    
    // Navigation callback - set by MainWindow
    public Action<string>? NavigateToPage { get; set; }

    /// <summary>
    /// Embedded Param Lock management sub-viewmodel for the Param Lock tab.
    /// </summary>
    public ParameterLockManagementViewModel ParamLockVm { get; }

    /// <summary>
    /// Embedded Firmware Management sub-viewmodel for the Firmware Management tab.
    /// </summary>
    public FirmwareManagementViewModel FirmwareManagementVm { get; }

    /// <summary>
    /// Embedded Parameter Logs sub-viewmodel for the Parameter Logs tab.
    /// </summary>
    public ParamLogsViewModel ParamLogsVm { get; }

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

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    // S3 Storage Analytics
    
    private string _s3TotalStorageValue = "Unlimited";
    public string S3TotalStorage
    {
        get => _s3TotalStorageValue;
        set => SetProperty(ref _s3TotalStorageValue, value);
    }
    
    private string _s3UsedStorageValue = "0 MB";
    public string S3UsedStorage
    {
        get => _s3UsedStorageValue;
        set => SetProperty(ref _s3UsedStorageValue, value);
    }
    
    private double _s3UsagePercentValue = 0;
    public double S3UsagePercent
    {
        get => _s3UsagePercentValue;
        set => SetProperty(ref _s3UsagePercentValue, value);
    }
    
    private int _totalParamLogsValue = 0;
    public int TotalParamLogs
    {
        get => _totalParamLogsValue;
        set => SetProperty(ref _totalParamLogsValue, value);
    }
    
    private int _totalFirmwareFilesValue = 0;
    public int TotalFirmwareFiles
    {
        get => _totalFirmwareFilesValue;
        set => SetProperty(ref _totalFirmwareFilesValue, value);
    }

    // User Activity Analytics
    private ObservableCollection<ActivityDataItem> _userActivityDataCollection = new();
    public ObservableCollection<ActivityDataItem> UserActivityData
    {
        get => _userActivityDataCollection;
        set => SetProperty(ref _userActivityDataCollection, value);
    }
    
    private ObservableCollection<DailyUsageItem> _dailyUsageDataCollection = new();
    public ObservableCollection<DailyUsageItem> DailyUsageData
    {
        get => _dailyUsageDataCollection;
        set => SetProperty(ref _dailyUsageDataCollection, value);
    }

    // Stats - computed from real user data
    public int TotalCount => Users.Count;
    public int PendingCount => Users.Count(u => !u.IsApproved);
    public int ApprovedCount => Users.Count(u => u.IsApproved);
    public int AdminCount => Users.Count(u => u.Role == "Admin");
    public int UserCount => Users.Count(u => u.Role == "User");

    public bool ShowEmptyState => !IsBusy && FilteredUsers.Count == 0 && _isInitialized;

    public AdminDashboardViewModel(
        IAdminService adminService,
        ILogger<AdminDashboardViewModel> logger,
        ITokenStorage tokenStorage,
        ParameterLockManagementViewModel paramLockVm,
        FirmwareManagementViewModel firmwareManagementVm,
        ParamLogsViewModel paramLogsVm,
        FirmwareApiService? firmwareApiService = null)
    {
        _adminService = adminService;
        _logger = logger;
        _tokenStorage = tokenStorage;
        ParamLockVm = paramLockVm;
        FirmwareManagementVm = firmwareManagementVm;
        ParamLogsVm = paramLogsVm;
        _firmwareApiService = firmwareApiService;

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
        await LoadAnalyticsAsync();

        // Initialize embedded Param Lock VM
        try
        {
            var token = await _tokenStorage.GetAccessTokenAsync();
            if (!string.IsNullOrEmpty(token))
            {
                await ParamLockVm.InitializeAsync(token);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Param Lock VM");
        }

        // Initialize Firmware Management VM
        try
        {
            await FirmwareManagementVm.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Firmware Management VM");
        }

        // Initialize Param Logs VM
        try
        {
            await ParamLogsVm.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to initialize Param Logs VM");
        }
    }
    
    private async Task LoadAnalyticsAsync()
    {
        try
        {
            // Load real param logs data from API
            if (_firmwareApiService != null)
            {
                // Load storage stats in parallel
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var firmwareStatsTask = _firmwareApiService.GetStorageStatsAsync("firmware");
                        var paramLogsStatsTask = _firmwareApiService.GetStorageStatsAsync("param-logs");
                        
                        await Task.WhenAll(firmwareStatsTask, paramLogsStatsTask);
                        
                        var firmwareStats = await firmwareStatsTask;
                        var paramLogsStats = await paramLogsStatsTask;
                        
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            if (firmwareStats != null)
                            {
                                TotalFirmwareFiles = firmwareStats.FileCount;
                            }
                            
                            if (paramLogsStats != null)
                            {
                                TotalParamLogs = paramLogsStats.FileCount;
                            }
                            
                            // Calculate total usage (no limit for S3)
                            var totalBytes = (firmwareStats?.TotalBytes ?? 0) + (paramLogsStats?.TotalBytes ?? 0);
                            S3UsedStorage = FormatFileSize(totalBytes);
                            S3UsagePercent = 0; // S3 has no limit
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load storage analytics");
                    }
                });
                
                // Load param logs count
                try
                {
                    var paramLogsResponse = await _firmwareApiService.GetParamLogsAsync("page=1&pageSize=1");
                    if (paramLogsResponse != null)
                    {
                        TotalParamLogs = paramLogsResponse.TotalCount;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load param logs analytics");
                }
                
                // Load firmware count
                try
                {
                    var firmwares = await _firmwareApiService.GetInAppFirmwaresAsync();
                    TotalFirmwareFiles = firmwares?.Count ?? 0;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load firmware analytics");
                }
            }
            
            // Build user activity data from real user stats
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UserActivityData.Clear();
                UserActivityData.Add(new ActivityDataItem { Activity = "Total Users", Count = TotalCount, Color = "#667EEA" });
                UserActivityData.Add(new ActivityDataItem { Activity = "Approved", Count = ApprovedCount, Color = "#10B981" });
                UserActivityData.Add(new ActivityDataItem { Activity = "Pending", Count = PendingCount, Color = "#F59E0B" });
                UserActivityData.Add(new ActivityDataItem { Activity = "Administrators", Count = AdminCount, Color = "#EF4444" });
                
                // Generate activity chart data based on user creation dates (last 7 days)
                DailyUsageData.Clear();
                for (int i = 6; i >= 0; i--)
                {
                    var date = DateTime.Now.Date.AddDays(-i);
                    var nextDate = date.AddDays(1);
                    
                    var usersJoined = Users.Count(u => u.CreatedAt.Date >= date && u.CreatedAt.Date < nextDate);
                    var usersApproved = Users.Count(u => u.IsApproved && u.CreatedAt.Date >= date && u.CreatedAt.Date < nextDate);
                    
                    DailyUsageData.Add(new DailyUsageItem
                    {
                        Date = date.ToString("MMM dd"),
                        ActiveUsers = Math.Max(usersJoined, 1), // At least 1 for visibility
                        ParamChanges = TotalParamLogs > 0 ? TotalParamLogs / 7 : 0, // Distribute evenly
                        FirmwareDownloads = TotalFirmwareFiles > 0 ? TotalFirmwareFiles / 7 : 0
                    });
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load analytics");
        }
    }
    
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
    
    // Navigation commands for cards - navigate to actual pages
    [RelayCommand]
    private void NavigateToPending()
    {
        // Set filter to pending and scroll to user table
        StatusFilter = 2;
        StatusMessage = $"Showing {PendingCount} pending user(s)";
    }
    
    [RelayCommand]
    private void NavigateToApproved()
    {
        // Set filter to approved and scroll to user table
        StatusFilter = 1;
        StatusMessage = $"Showing {ApprovedCount} approved user(s)";
    }
    
    [RelayCommand]
    private void NavigateToAdmins()
    {
        StatusFilter = 0;
        RoleFilter = 1;
        StatusMessage = $"Showing {AdminCount} administrator(s)";
    }
    
    [RelayCommand]
    private void NavigateToAllUsers()
    {
        ClearFilters();
        StatusMessage = $"Showing all {TotalCount} user(s)";
    }
    
    [RelayCommand]
    private void NavigateToParamLogs()
    {
        SelectedTabIndex = 2; // Switch to Parameter Logs tab
    }
    
    [RelayCommand]
    private void NavigateToFirmware()
    {
        SelectedTabIndex = 1; // Switch to Firmware Management tab
    }
    
    /// <summary>
    /// Set filter preset when navigating from another page
    /// </summary>
    public void SetFilterPreset(string filter)
    {
        switch (filter.ToLower())
        {
            case "pending":
                StatusFilter = 2;
                break;
            case "approved":
                StatusFilter = 1;
                break;
            case "admin":
                RoleFilter = 1;
                break;
            default:
                ClearFilters();
                break;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Loading users...";
        try
        {
            _logger.LogInformation("Refreshing user list...");
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

            // Reload storage stats
            await LoadAnalyticsAsync();

            StatusMessage = $"\u2714 Loaded {Users.Count} users";
            _logger.LogInformation("Loaded {Count} users ({Pending} pending)", Users.Count, PendingCount);
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "HTTP error loading users: {Message}", httpEx.Message);
            StatusMessage = $"\u274C {httpEx.Message}";
        }
        catch (InvalidOperationException authEx)
        {
            _logger.LogError(authEx, "Authentication error: {Message}", authEx.Message);
            StatusMessage = $"\u274C {authEx.Message}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load users: {Message}", ex.Message);
            StatusMessage = $"\u274C Failed to load users: {ex.Message}";
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
            // If approving and role changed, update role first
            if (newState && user.SelectedRole != user.Role)
            {
                await _adminService.ChangeUserRoleAsync(user.Id, user.SelectedRole);
            }

            // Then approve/revoke
            await _adminService.ApproveUserAsync(user.Id, newState);

            // Success - update UI
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                user.IsApproved = newState;
                if (newState) user.Role = user.SelectedRole;
                NotifyStatsChanged();
            });

            StatusMessage = newState 
                ? $"\u2714 {user.FullName} approved as {user.SelectedRole}" 
                : $"\u26D4 {user.FullName}'s access revoked";
        }
        catch (HttpRequestException httpEx)
        {
            _logger.LogError(httpEx, "HTTP error updating user {UserId}", user.Id);
            StatusMessage = $"\u274C Connection error: {httpEx.Message}";
        }
        catch (InvalidOperationException invEx)
        {
            _logger.LogError(invEx, "Not authenticated");
            StatusMessage = "\u274C Not authenticated - please login again";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update user {UserId}", user.Id);
            StatusMessage = $"\u274C Error: {ex.Message}";
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
                StatusMessage = $"\u2714 {user.FullName} is now {user.SelectedRole}";
            }
            else
            {
                user.SelectedRole = user.Role;
                StatusMessage = "\u274C Role change failed";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change role");
            user.SelectedRole = user.Role;
            StatusMessage = "\u274C Failed to change role";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NavigateToFirmwareManagement()
    {
        SelectedTabIndex = 1; // Switch to Firmware Management tab
    }

    [RelayCommand]
    private async Task DeleteUserAsync(UserListItem user)
    {
        if (IsBusy || user == null) return;

        IsBusy = true;
        StatusMessage = $"Deleting {user.FullName}...";

        try
        {
            _logger.LogInformation("Attempting to delete user: {Email} (ID: {Id})", user.Email, user.Id);
            
            var success = await _adminService.DeleteUserAsync(user.Id);

            if (success)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Users.Remove(user);
                    FilteredUsers.Remove(user);
                    NotifyStatsChanged();
                });

                StatusMessage = $"\u2714 {user.FullName} has been deleted";
                _logger.LogInformation("Successfully deleted user {Email}", user.Email);
            }
            else
            {
                var errorMsg = "\u274C Failed to delete user - check if you have admin permissions";
                StatusMessage = errorMsg;
                _logger.LogWarning("Delete user failed for {Email} - service returned false", user.Email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while deleting user {Email}: {Message}", user.Email, ex.Message);
            StatusMessage = $"\u274C Error: {ex.Message}";
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

/// <summary>
/// Data model for daily usage statistics
/// </summary>
public class DailyUsageItem
{
    public string Date { get; set; } = string.Empty;
    public int ActiveUsers { get; set; }
    public int ParamChanges { get; set; }
    public int FirmwareDownloads { get; set; }
}

/// <summary>
/// Data model for user activity breakdown
/// </summary>
public class ActivityDataItem
{
    public string Activity { get; set; } = string.Empty;
    public int Count { get; set; }
    public string Color { get; set; } = "#919ccdff";
}
