using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.UI.ViewModels.Admin;

/// <summary>
/// ViewModel for admin panel - user management.
/// Now deprecated - use AdminDashboardViewModel instead.
/// </summary>
public sealed partial class AdminPanelViewModel : ViewModelBase
{
    private readonly IAdminService _adminService;
    private readonly ILogger<AdminPanelViewModel> _logger;

    public AdminPanelViewModel(
        IAdminService adminService,
        ILogger<AdminPanelViewModel> logger)
    {
        _adminService = adminService;
        _logger = logger;
    }
}
