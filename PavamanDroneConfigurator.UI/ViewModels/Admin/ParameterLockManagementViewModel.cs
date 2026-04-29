using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
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
    private readonly IParameterService _parameterService;
    private readonly ITokenStorage _tokenStorage;
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

    [ObservableProperty]
    private string _usersLoadError = string.Empty;

    // Dialog user search/filter
    [ObservableProperty]
    private string _dialogUserSearchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<UserItemModel> _filteredDialogUsers = new();

    [ObservableProperty]
    private string _dialogStatusMessage = string.Empty;

    public ParameterLockManagementViewModel(
        ParamLockApiService paramLockApiService,
        AdminApiService adminApiService,
        IParameterService parameterService,
        ITokenStorage tokenStorage,
        ILogger<ParameterLockManagementViewModel> logger)
    {
        _paramLockApiService = paramLockApiService;
        _adminApiService = adminApiService;
        _parameterService = parameterService;
        _tokenStorage = tokenStorage;
        _logger = logger;
    }

    public async Task InitializeAsync(string token)
    {
        _currentToken = token;
        await RefreshLocksAsync();
    }

    /// <summary>
    /// Gets a valid token — uses the stored token if available, otherwise
    /// fetches fresh from ITokenStorage to handle race conditions and token refresh.
    /// </summary>
    private async Task<string?> GetTokenAsync()
    {
        if (!string.IsNullOrEmpty(_currentToken))
            return _currentToken;

        var fresh = await _tokenStorage.GetAccessTokenAsync();
        if (!string.IsNullOrEmpty(fresh))
            _currentToken = fresh;

        return _currentToken;
    }

    [RelayCommand]
    private async Task RefreshLocksAsync()
    {
        var token = await GetTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            StatusMessage = "Not authenticated";
            return;
        }

        IsLoading = true;
        StatusMessage = "Loading parameter locks...";

        try
        {
            var locks = await _paramLockApiService.GetAllLocksAsync(token);

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
        DialogUserSearchText = string.Empty;
        SelectedUser = null;
        DialogStatusMessage = string.Empty;

        // Clear parameter selections
        foreach (var param in AvailableParameters)
        {
            param.IsSelected = false;
            param.IsCurrentlyLocked = false;
        }

        // Load live parameters from the connected drone, or fall back to hardcoded list
        await LoadParametersAsync();

        // Always reload users fresh
        await LoadUsersAsync();

        // Clear any previous user selection highlight
        foreach (var u in AvailableUsers)
            u.IsSelected = false;

        FilterDialogUsers();
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
        ParamSearchText = string.Empty;
        DialogStatusMessage = string.Empty;

        // Load live parameters
        await LoadParametersAsync();

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
        var token = await GetTokenAsync();
        if (string.IsNullOrEmpty(token))
        {
            DialogStatusMessage = "Not authenticated. Please log in again.";
            return;
        }

        if (SelectedUser == null && !IsEditMode)
        {
            DialogStatusMessage = "Please select a user";
            return;
        }

        var selectedParams = AvailableParameters.Where(p => p.IsSelected).Select(p => p.Name).ToList();
        var selectedParamValues = AvailableParameters
            .Where(p => p.IsSelected)
            .ToDictionary(p => p.Name, p => p.Value);

        if (selectedParams.Count == 0)
        {
            DialogStatusMessage = "Please select at least one parameter to lock";
            return;
        }

        IsBusy = true;
        DialogStatusMessage = IsEditMode ? "Updating lock..." : "Creating lock...";

        try
        {
            ParamLockResponse? response;

            if (IsEditMode && SelectedLock != null)
            {
                var request = new UpdateParamLockRequest
                {
                    LockId = SelectedLock.Id,
                    Params = selectedParams,
                    ParamValues = selectedParamValues
                };

                response = await _paramLockApiService.UpdateLockAsync(request, token);
            }
            else
            {
                var request = new CreateParamLockRequest
                {
                    UserId = SelectedUser!.UserId,
                    DeviceId = ApplyToAllDevices ? null : DeviceId.Trim(),
                    Params = selectedParams,
                    ParamValues = selectedParamValues
                };

                response = await _paramLockApiService.CreateLockAsync(request, token);
            }

            if (response?.Success == true)
            {
                DialogStatusMessage = response.Message ?? "Lock saved successfully";
                IsCreateDialogOpen = false;
                await RefreshLocksAsync();
            }
            else
            {
                DialogStatusMessage = response?.Message ?? "Failed to save lock";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save parameter lock");
            DialogStatusMessage = "Error saving lock";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteLockAsync(ParamLockModel? lockModel)
    {
        if (lockModel == null) return;
        var token = await GetTokenAsync();
        if (string.IsNullOrEmpty(token)) return;

        // In production, show confirmation dialog
        IsBusy = true;
        StatusMessage = $"Deleting lock for {lockModel.UserName}...";

        try
        {
            var success = await _paramLockApiService.DeleteLockAsync(lockModel.Id, token);

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

    partial void OnDialogUserSearchTextChanged(string value)
    {
        FilterDialogUsers();
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
        UsersLoadError = string.Empty;
        try
        {
            var response = await _adminApiService.GetAllUsersAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableUsers.Clear();
                foreach (var user in response.Users)
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

                if (AvailableUsers.Count == 0)
                    UsersLoadError = "No users found on the server.";

                FilterDialogUsers();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load users");
            UsersLoadError = $"Failed to load users: {ex.Message}";
        }
    }

    private void FilterDialogUsers()
    {
        FilteredDialogUsers.Clear();
        var query = AvailableUsers.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(DialogUserSearchText))
        {
            var s = DialogUserSearchText.ToLower();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(s) ||
                u.Email.ToLower().Contains(s));
        }
        foreach (var user in query.OrderBy(u => u.FullName))
            FilteredDialogUsers.Add(user);
    }

    [RelayCommand]
    private void SelectDialogUser(UserItemModel? user)
    {
        if (user == null) return;
        foreach (var u in AvailableUsers)
            u.IsSelected = false;
        user.IsSelected = true;
        SelectedUser = user;
    }

    private async Task LoadParametersAsync()
    {
        AvailableParameters.Clear();

        if (_parameterService.IsParameterDownloadComplete)
        {
            // Load all live parameters from the connected drone
            var droneParams = await _parameterService.GetAllParametersAsync();
            foreach (var dp in droneParams.OrderBy(p => p.Name))
            {
                var item = new ParameterItemModel
                {
                    Name = dp.Name,
                    Description = dp.Description ?? string.Empty,
                    Group = GetGroupFromName(dp.Name),
                    Value = dp.Value,
                    IsSelected = false
                };
                item.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ParameterItemModel.IsSelected))
                        UpdateSelectedParamCount();
                };
                AvailableParameters.Add(item);
            }
        }
        else
        {
            // Drone not connected — load hardcoded fallback list
            LoadFallbackParameters();
        }

        FilterParameters();
    }

    private static string GetGroupFromName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "General";
        var prefix = name.Contains('_') ? name[..name.IndexOf('_')] : name;
        return prefix switch
        {
            "ACCEL" or "ACC" or "INS" or "COMPASS" => "Calibration",
            "ATC" or "PSC" or "PID" or "ANGLE" or "RATE" => "PID Tuning",
            "ARMING" or "BRD" or "FENCE" or "FS" => "Safety",
            "BATT" => "Battery",
            "EK2" or "EK3" => "EKF",
            "FLTMODE" or "SCHED" => "Flight Modes",
            "GPS" => "GPS",
            "MOT" or "ESC" => "Motors",
            "RC" or "RCMAP" => "RC Input",
            "SERVO" => "Servo",
            "SERIAL" => "Serial",
            "SYSID" or "SYS" => "System",
            "TERRAIN" => "Terrain",
            "WPNAV" or "LOIT" or "LAND" or "RTL" => "Navigation",
            "LOG" => "Logging",
            "NTF" => "Notifications",
            "RNG" or "RNGFND" => "RangeFinder",
            "BARO" => "Barometer",
            "CAN" or "UAVCAN" or "DRONE" => "CAN",
            "CAM" => "Camera",
            "MNT" => "Gimbal",
            "FLOW" => "OpticalFlow",
            "MAG" => "Magnetometer",
            "VISO" or "VISUAL" => "VisualOdometry",
            "AVOID" or "OA" => "ObjectAvoidance",
            "SPRAY" => "Spraying",
            _ => prefix.Length > 0 ? char.ToUpperInvariant(prefix[0]) + prefix[1..].ToLowerInvariant() : "General"
        };
    }

    private void LoadFallbackParameters()
    {
        var commonParams = new[]
        {
            ("SYSID_THISMAV", "MAVLink system ID", "System"),
            ("ARMING_CHECK", "Arming safety checks", "Safety"),
            ("BRD_SAFETYENABLE", "Enable hardware safety switch", "Safety"),
            ("FENCE_ENABLE", "Geofence enable", "Safety"),
            ("FS_THR_ENABLE", "Throttle failsafe enable", "Safety"),
            ("FS_GCS_ENABLE", "GCS failsafe enable", "Safety"),
            ("COMPASS_USE", "Use compass for navigation", "Calibration"),
            ("COMPASS_AUTODEC", "Auto declination", "Calibration"),
            ("INS_GYR_CAL", "Gyro calibration", "Calibration"),
            ("INS_ACC_BODYFIX", "Accelerometer body-fixed", "Calibration"),
            ("FLTMODE1", "Flight mode 1", "Flight Modes"),
            ("FLTMODE2", "Flight mode 2", "Flight Modes"),
            ("FLTMODE3", "Flight mode 3", "Flight Modes"),
            ("FLTMODE4", "Flight mode 4", "Flight Modes"),
            ("FLTMODE5", "Flight mode 5", "Flight Modes"),
            ("FLTMODE6", "Flight mode 6", "Flight Modes"),
            ("BATT_CAPACITY", "Battery capacity (mAh)", "Battery"),
            ("BATT_LOW_VOLT", "Low battery voltage", "Battery"),
            ("BATT_CRT_VOLT", "Critical battery voltage", "Battery"),
            ("BATT_FS_LOW_ACT", "Low battery failsafe action", "Battery"),
            ("GPS_TYPE", "GPS type", "GPS"),
            ("GPS_AUTO_SWITCH", "Auto GPS switching", "GPS"),
            ("MOT_PWM_MIN", "Motor PWM minimum", "Motors"),
            ("MOT_PWM_MAX", "Motor PWM maximum", "Motors"),
            ("MOT_SPIN_MIN", "Motor spin minimum", "Motors"),
            ("MOT_SPIN_MAX", "Motor spin maximum", "Motors"),
            ("ATC_RAT_RLL_P", "Roll rate P gain", "PID Tuning"),
            ("ATC_RAT_PIT_P", "Pitch rate P gain", "PID Tuning"),
            ("ATC_RAT_YAW_P", "Yaw rate P gain", "PID Tuning"),
        };

        foreach (var (name, desc, group) in commonParams)
        {
            var item = new ParameterItemModel
            {
                Name = name,
                Description = desc,
                Group = group,
                IsSelected = false
            };
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(ParameterItemModel.IsSelected))
                    UpdateSelectedParamCount();
            };
            AvailableParameters.Add(item);
        }
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
