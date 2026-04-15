using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Infrastructure.Services.Auth;
using PavamanDroneConfigurator.UI.Models;

namespace PavamanDroneConfigurator.UI.ViewModels.Admin;

/// <summary>
/// ViewModel for parameter lock management page.
/// Allows admins to create, update, and delete parameter locks for users.
/// </summary>
public partial class ParameterLockManagementViewModel : ViewModelBase
{
    private readonly ParamLockApiService _paramLockApiService;
    private readonly AdminApiService _adminApiService;
    private readonly ILogger<ParameterLockManagementViewModel> _logger;
    private string? _currentToken;

    [ObservableProperty]
    private ObservableCollection<ParamLockModel> _paramLocks = new();

    [ObservableProperty]
    private ObservableCollection<ParamLockModel> _filteredLocks = new();

    [ObservableProperty]
    private ParamLockModel? _selectedLock;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private int _totalLocksCount;

    [ObservableProperty]
    private int _totalUsersWithLocks;

    [ObservableProperty]
    private int _totalLockedParams;

    // Create/Edit Dialog Properties
    [ObservableProperty]
    private bool _isCreateDialogOpen;

    [ObservableProperty]
    private bool _isEditMode;

    [ObservableProperty]
    private string _dialogTitle = "Create Parameter Lock";

    [ObservableProperty]
    private ObservableCollection<UserItemModel> _availableUsers = new();

    [ObservableProperty]
    private UserItemModel? _selectedUser;

    [ObservableProperty]
    private string _deviceId = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ParameterItemModel> _availableParameters = new();

    [ObservableProperty]
    private ObservableCollection<ParameterItemModel> _filteredParameters = new();

    [ObservableProperty]
    private string _paramSearchText = string.Empty;

    [ObservableProperty]
    private int _selectedParamCount;

    [ObservableProperty]
    private bool _applyToAllDevices = true;

    public ParameterLockManagementViewModel(
        ParamLockApiService paramLockApiService,
        AdminApiService adminApiService,
        ILogger<ParameterLockManagementViewModel> logger)
    {
        _paramLockApiService = paramLockApiService;
        _adminApiService = adminApiService;
        _logger = logger;
    }

    public async Task InitializeAsync(string token)
    {
        _currentToken = token;
        await RefreshLocksAsync();
        LoadCommonParameters();
    }

    [RelayCommand]
    private async Task RefreshLocksAsync()
    {
        if (string.IsNullOrEmpty(_currentToken))
        {
            StatusMessage = "Not authenticated";
            return;
        }

        IsLoading = true;
        StatusMessage = "Loading parameter locks...";

        try
        {
            var locks = await _paramLockApiService.GetAllLocksAsync(_currentToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ParamLocks.Clear();
                foreach (var lockInfo in locks)
                {
                    ParamLocks.Add(new ParamLockModel
                    {
                        Id = lockInfo.Id,
                        UserId = lockInfo.UserId,
                        UserName = lockInfo.UserName,
                        UserEmail = lockInfo.UserEmail,
                        DeviceId = lockInfo.DeviceId,
                        ParamCount = lockInfo.ParamCount,
                        LockedParams = lockInfo.LockedParams,
                        CreatedAt = lockInfo.CreatedAt,
                        CreatedBy = lockInfo.CreatedBy,
                        CreatedByName = lockInfo.CreatedByName,
                        UpdatedAt = lockInfo.UpdatedAt,
                        IsActive = lockInfo.IsActive
                    });
                }

                UpdateStatistics();
                FilterLocks();
                StatusMessage = $"Loaded {ParamLocks.Count} parameter locks";
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load parameter locks");
            StatusMessage = "Failed to load parameter locks";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task OpenCreateDialogAsync()
    {
        IsEditMode = false;
        DialogTitle = "Create Parameter Lock";
        DeviceId = string.Empty;
        ApplyToAllDevices = true;
        ParamSearchText = string.Empty;
        SelectedUser = null;

        // Clear parameter selections
        foreach (var param in AvailableParameters)
        {
            param.IsSelected = false;
            param.IsCurrentlyLocked = false;
        }

        // Load users if needed
        if (AvailableUsers.Count == 0)
        {
            await LoadUsersAsync();
        }

        FilterParameters();
        UpdateSelectedParamCount();
        IsCreateDialogOpen = true;
    }

    [RelayCommand]
    private async Task OpenEditDialogAsync(ParamLockModel? lockModel)
    {
        if (lockModel == null) return;

        IsEditMode = true;
        DialogTitle = $"Edit Parameter Lock - {lockModel.UserName}";
        SelectedLock = lockModel;
        DeviceId = lockModel.DeviceId ?? string.Empty;
        ApplyToAllDevices = string.IsNullOrWhiteSpace(lockModel.DeviceId);

        // Set parameter selections
        foreach (var param in AvailableParameters)
        {
            param.IsSelected = lockModel.LockedParams.Contains(param.Name, StringComparer.OrdinalIgnoreCase);
            param.IsCurrentlyLocked = param.IsSelected;
        }

        FilterParameters();
        UpdateSelectedParamCount();
        IsCreateDialogOpen = true;
    }

    [RelayCommand]
    private void CloseDialog()
    {
        IsCreateDialogOpen = false;
        SelectedLock = null;
    }

    [RelayCommand]
    private async Task SaveLockAsync()
    {
        if (string.IsNullOrEmpty(_currentToken))
        {
            StatusMessage = "Not authenticated";
            return;
        }

        if (SelectedUser == null && !IsEditMode)
        {
            StatusMessage = "Please select a user";
            return;
        }

        var selectedParams = AvailableParameters.Where(p => p.IsSelected).Select(p => p.Name).ToList();

        if (selectedParams.Count == 0)
        {
            StatusMessage = "Please select at least one parameter to lock";
            return;
        }

        IsBusy = true;
        StatusMessage = IsEditMode ? "Updating lock..." : "Creating lock...";

        try
        {
            ParamLockResponse? response;

            if (IsEditMode && SelectedLock != null)
            {
                var request = new UpdateParamLockRequest
                {
                    LockId = SelectedLock.Id,
                    Params = selectedParams
                };

                response = await _paramLockApiService.UpdateLockAsync(request, _currentToken);
            }
            else
            {
                var request = new CreateParamLockRequest
                {
                    UserId = SelectedUser!.UserId,
                    DeviceId = ApplyToAllDevices ? null : DeviceId.Trim(),
                    Params = selectedParams
                };

                response = await _paramLockApiService.CreateLockAsync(request, _currentToken);
            }

            if (response?.Success == true)
            {
                StatusMessage = response.Message ?? "Lock saved successfully";
                IsCreateDialogOpen = false;
                await RefreshLocksAsync();
            }
            else
            {
                StatusMessage = response?.Message ?? "Failed to save lock";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save parameter lock");
            StatusMessage = "Error saving lock";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteLockAsync(ParamLockModel? lockModel)
    {
        if (lockModel == null || string.IsNullOrEmpty(_currentToken)) return;

        // In production, show confirmation dialog
        IsBusy = true;
        StatusMessage = $"Deleting lock for {lockModel.UserName}...";

        try
        {
            var success = await _paramLockApiService.DeleteLockAsync(lockModel.Id, _currentToken);

            if (success)
            {
                StatusMessage = "Lock deleted successfully";
                await RefreshLocksAsync();
            }
            else
            {
                StatusMessage = "Failed to delete lock";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete lock {LockId}", lockModel.Id);
            StatusMessage = "Error deleting lock";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        FilterLocks();
    }

    partial void OnParamSearchTextChanged(string value)
    {
        FilterParameters();
    }

    private void FilterLocks()
    {
        FilteredLocks.Clear();

        var query = ParamLocks.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.ToLower();
            query = query.Where(l =>
                (l.UserName?.ToLower().Contains(search) ?? false) ||
                (l.UserEmail?.ToLower().Contains(search) ?? false) ||
                (l.DeviceId?.ToLower().Contains(search) ?? false));
        }

        foreach (var lockItem in query)
        {
            FilteredLocks.Add(lockItem);
        }
    }

    private void FilterParameters()
    {
        FilteredParameters.Clear();

        var query = AvailableParameters.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(ParamSearchText))
        {
            var search = ParamSearchText.ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(search) ||
                (p.Description?.ToLower().Contains(search) ?? false) ||
                (p.Group?.ToLower().Contains(search) ?? false));
        }

        // Show selected first, then locked, then others
        foreach (var param in query.OrderByDescending(p => p.IsSelected)
                                   .ThenByDescending(p => p.IsCurrentlyLocked)
                                   .ThenBy(p => p.Group)
                                   .ThenBy(p => p.Name))
        {
            FilteredParameters.Add(param);
        }

        UpdateSelectedParamCount();
    }

    private void UpdateSelectedParamCount()
    {
        SelectedParamCount = AvailableParameters.Count(p => p.IsSelected);
    }

    private void UpdateStatistics()
    {
        TotalLocksCount = ParamLocks.Count;
        TotalUsersWithLocks = ParamLocks.Select(l => l.UserId).Distinct().Count();
        TotalLockedParams = ParamLocks.Sum(l => l.ParamCount);
    }

    private async Task LoadUsersAsync()
    {
        if (string.IsNullOrEmpty(_currentToken)) return;

        try
        {
            var response = await _adminApiService.GetAllUsersAsync();

            AvailableUsers.Clear();
            foreach (var user in response.Users.Where(u => u.Role != "Admin"))
            {
                if (!Guid.TryParse(user.Id, out var userId))
                    continue;

                AvailableUsers.Add(new UserItemModel
                {
                    UserId = userId,
                    FullName = user.FullName,
                    Email = user.Email,
                    Role = user.Role,
                    ExistingLocksCount = ParamLocks.Count(l => l.UserId == userId)
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load users");
        }
    }

    private void LoadCommonParameters()
    {
        // Common ArduPilot/PX4 parameters that are typically locked
        var commonParams = new[]
        {
            // Flight Controller Type
            ("SYSID_THISMAV", "MAVLink system ID", "System"),
            ("SYSID_MYGCS", "Ground station MAVLink ID", "System"),
            
            // Safety
            ("ARMING_CHECK", "Arming safety checks", "Safety"),
            ("BRD_SAFETYENABLE", "Enable hardware safety switch", "Safety"),
            ("FENCE_ENABLE", "Geofence enable", "Safety"),
            ("FENCE_ACTION", "Geofence breach action", "Safety"),
            ("FS_THR_ENABLE", "Throttle failsafe enable", "Safety"),
            ("FS_GCS_ENABLE", "GCS failsafe enable", "Safety"),
            
            // Calibration
            ("COMPASS_USE", "Use compass for navigation", "Calibration"),
            ("COMPASS_AUTODEC", "Auto declination", "Calibration"),
            ("INS_GYR_CAL", "Gyro calibration", "Calibration"),
            ("INS_ACC_BODYFIX", "Accelerometer body-fixed", "Calibration"),
            
            // Flight Modes
            ("FLTMODE1", "Flight mode 1", "Flight Modes"),
            ("FLTMODE2", "Flight mode 2", "Flight Modes"),
            ("FLTMODE3", "Flight mode 3", "Flight Modes"),
            ("FLTMODE4", "Flight mode 4", "Flight Modes"),
            ("FLTMODE5", "Flight mode 5", "Flight Modes"),
            ("FLTMODE6", "Flight mode 6", "Flight Modes"),
            
            // Battery
            ("BATT_CAPACITY", "Battery capacity (mAh)", "Battery"),
            ("BATT_LOW_VOLT", "Low battery voltage", "Battery"),
            ("BATT_CRT_VOLT", "Critical battery voltage", "Battery"),
            ("BATT_FS_LOW_ACT", "Low battery failsafe action", "Battery"),
            
            // GPS
            ("GPS_TYPE", "GPS type", "GPS"),
            ("GPS_AUTO_SWITCH", "Auto GPS switching", "GPS"),
            
            // RC
            ("RC1_MIN", "RC channel 1 minimum", "RC Input"),
            ("RC1_MAX", "RC channel 1 maximum", "RC Input"),
            ("RC1_TRIM", "RC channel 1 trim", "RC Input"),
            
            // Motor/ESC
            ("MOT_PWM_MIN", "Motor PWM minimum", "Motors"),
            ("MOT_PWM_MAX", "Motor PWM maximum", "Motors"),
            ("MOT_SPIN_MIN", "Motor spin minimum", "Motors"),
            ("MOT_SPIN_MAX", "Motor spin maximum", "Motors"),
            
            // PID Tuning (critical)
            ("ATC_RAT_RLL_P", "Roll rate P gain", "PID Tuning"),
            ("ATC_RAT_PIT_P", "Pitch rate P gain", "PID Tuning"),
            ("ATC_RAT_YAW_P", "Yaw rate P gain", "PID Tuning"),
            ("PSC_VELXY_P", "Velocity XY P gain", "PID Tuning"),
            ("PSC_POSZ_P", "Position Z P gain", "PID Tuning")
        };

        AvailableParameters.Clear();
        foreach (var (name, desc, group) in commonParams)
        {
            AvailableParameters.Add(new ParameterItemModel
            {
                Name = name,
                Description = desc,
                Group = group,
                IsSelected = false
            });
        }

        FilterParameters();
    }

    [RelayCommand]
    private void ToggleParameter(ParameterItemModel? param)
    {
        if (param == null) return;
        param.IsSelected = !param.IsSelected;
        UpdateSelectedParamCount();
    }

    [RelayCommand]
    private void SelectAllParameters()
    {
        foreach (var param in FilteredParameters)
        {
            param.IsSelected = true;
        }
        UpdateSelectedParamCount();
    }

    [RelayCommand]
    private void ClearAllParameters()
    {
        foreach (var param in AvailableParameters)
        {
            param.IsSelected = false;
        }
        UpdateSelectedParamCount();
    }
}
