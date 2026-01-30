using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.UI.ViewModels.Admin;

/// <summary>
/// ViewModel for admin dashboard - overview and statistics.
/// </summary>
public sealed partial class AdminDashboardViewModel : ViewModelBase
{
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminDashboardViewModel> _logger;

    [ObservableProperty]
    private int _totalUsers;

    [ObservableProperty]
    private int _pendingApprovals;

    [ObservableProperty]
    private int _activeAdmins;

    [ObservableProperty]
    private int _recentlyApproved;

    [ObservableProperty]
    private double _approvalRate;

    [ObservableProperty]
    private string _lastApprovalTime = "N/A";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public AdminDashboardViewModel(
        IAdminService adminService,
        ILogger<AdminDashboardViewModel> logger)
    {
        _adminService = adminService;
        _logger = logger;
    }

    /// <summary>
    /// Initialize and load statistics.
    /// </summary>
    public async Task InitializeAsync()
    {
        await RefreshStatisticsAsync();
    }

    [RelayCommand]
    private async Task RefreshStatisticsAsync()
    {
        if (IsBusy) return;

        IsBusy = true;
        StatusMessage = "Loading statistics...";
        try
        {
            var response = await _adminService.GetAllUsersAsync();
            var users = response.Users;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // Total users
                TotalUsers = users.Count;

                // Pending approvals
                PendingApprovals = users.Count(u => !u.IsApproved);

                // Active admins
                ActiveAdmins = users.Count(u => u.IsApproved && u.Role == "Admin");

                // Recently approved (last 24 hours)
                var oneDayAgo = DateTimeOffset.UtcNow.AddDays(-1);
                RecentlyApproved = users.Count(u => 
                    u.IsApproved && 
                    u.LastLoginAt.HasValue && 
                    u.LastLoginAt.Value >= oneDayAgo);

                // Approval rate
                var approvedUsers = users.Count(u => u.IsApproved);
                ApprovalRate = TotalUsers > 0 
                    ? Math.Round((double)approvedUsers / TotalUsers * 100, 1) 
                    : 0;

                // Last approval time
                var lastApproved = users
                    .Where(u => u.IsApproved && u.LastLoginAt.HasValue)
                    .OrderByDescending(u => u.LastLoginAt)
                    .FirstOrDefault();

                if (lastApproved?.LastLoginAt != null)
                {
                    var elapsed = DateTimeOffset.UtcNow - lastApproved.LastLoginAt.Value;
                    LastApprovalTime = elapsed.TotalHours < 24 
                        ? $"{elapsed.Hours}h {elapsed.Minutes}m ago"
                        : lastApproved.LastLoginAt.Value.ToString("MMM dd, HH:mm");
                }
                else
                {
                    LastApprovalTime = "N/A";
                }
            });

            StatusMessage = $"Statistics updated at {DateTime.Now:HH:mm:ss}";
            _logger.LogInformation("Dashboard statistics loaded: {Total} total, {Pending} pending", 
                TotalUsers, PendingApprovals);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard statistics");
            StatusMessage = "Failed to load statistics";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
