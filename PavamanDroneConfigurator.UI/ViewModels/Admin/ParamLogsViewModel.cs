using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Infrastructure.Services;
using PavamanDroneConfigurator.Infrastructure.Services.Auth;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.UI.ViewModels.Admin;

/// <summary>
/// ViewModel for Parameter Logs admin page.
/// Displays parameter change history from S3 with filtering capabilities.
/// </summary>
public partial class ParamLogsViewModel : ViewModelBase
{
    private readonly FirmwareApiService? _apiService;
    private readonly AdminApiService? _adminApiService;
    private readonly ILogger<ParamLogsViewModel>? _logger;

    // Maps userId (UUID) -> "FullName (email)" for display in filter
    private readonly Dictionary<string, string> _userIdToDisplayName = new(StringComparer.OrdinalIgnoreCase);
    // Reverse map: displayName -> userId for API filtering
    private readonly Dictionary<string, string> _displayNameToUserId = new(StringComparer.OrdinalIgnoreCase);

    #region Backing Fields
    private string? _selectedUserId;
    private string? _selectedDroneId;
    private string? _searchText;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private ParamLogItem? _selectedLog;
    private bool _isLoadingContent;
    private bool _isLoading;
    private string? _statusMessage;
    private bool _hasError;
    private int _currentPage = 1;
    private int _totalPages = 1;
    private int _totalCount;
    private int _pageSize = 50;
    #endregion

    #region Properties
    
    public string? SelectedUserId
    {
        get => _selectedUserId;
        set => SetProperty(ref _selectedUserId, value);
    }

    public string? SelectedDroneId
    {
        get => _selectedDroneId;
        set => SetProperty(ref _selectedDroneId, value);
    }

    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                // Trigger search immediately when text changes
                CurrentPage = 1;
                _ = LoadParamLogsAsync();
            }
        }
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set => SetProperty(ref _fromDate, value);
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set => SetProperty(ref _toDate, value);
    }

    public ParamLogItem? SelectedLog
    {
        get => _selectedLog;
        set
        {
            if (SetProperty(ref _selectedLog, value))
            {
                OnPropertyChanged(nameof(HasSelectedLog));
            }
        }
    }

    public bool IsLoadingContent
    {
        get => _isLoadingContent;
        set => SetProperty(ref _isLoadingContent, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(HasNoLogs));
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public int CurrentPage
    {
        get => _currentPage;
        set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(HasPreviousPage));
                OnPropertyChanged(nameof(HasNextPage));
            }
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        set
        {
            if (SetProperty(ref _totalPages, value))
            {
                OnPropertyChanged(nameof(HasNextPage));
            }
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        set => SetProperty(ref _totalCount, value);
    }

    public int PageSize
    {
        get => _pageSize;
        set => SetProperty(ref _pageSize, value);
    }
    
    // Collections
    public ObservableCollection<string> AvailableUsers { get; } = new();
    public ObservableCollection<string> AvailableDrones { get; } = new();
    public ObservableCollection<ParamLogItem> ParamLogs { get; } = new();
    public ObservableCollection<ParamChangeItem> SelectedLogChanges { get; } = new();
    
    // Computed Properties
    public bool HasPreviousPage => CurrentPage > 1;
    public bool HasNextPage => CurrentPage < TotalPages;
    public bool HasNoLogs => !IsLoading && ParamLogs.Count == 0;
    public bool HasSelectedLog => SelectedLog != null;
    public bool HasNoChanges => HasSelectedLog && !IsLoadingContent && SelectedLogChanges.Count == 0;
    public bool HasChangesToShow => HasSelectedLog && !IsLoadingContent && SelectedLogChanges.Count > 0;
    
    #endregion
    
    #region Constructor
    
    public ParamLogsViewModel(
        FirmwareApiService apiService,
        AdminApiService? adminApiService = null,
        ILogger<ParamLogsViewModel>? logger = null)
    {
        _apiService = apiService;
        _adminApiService = adminApiService;
        _logger = logger;
        
        // Set default date range (last 30 days)
        _toDate = DateTime.Today;
        _fromDate = DateTime.Today.AddDays(-30);
    }
    
    /// <summary>
    /// Parameterless constructor for design-time support
    /// </summary>
    public ParamLogsViewModel()
    {
        _apiService = null;
        _logger = null;
        
        // Add design-time data
        ParamLogs.Add(new ParamLogItem 
        { 
            Key = "params-logs/user_admin/drone_FC001/params_20250115_103045.csv",
            UserId = "admin",
            DroneId = "FC001",
            Timestamp = DateTime.Now.AddHours(-2),
            SizeDisplay = "1.2 KB"
        });
    }
    
    /// <summary>
    /// Initialize the page - load param logs and available users for filtering
    /// </summary>
    public async Task InitializeAsync()
    {
        // Run user-list load and log load in parallel for faster startup
        var tasks = new List<Task>();
        if (_adminApiService != null && _userIdToDisplayName.Count == 0)
            tasks.Add(LoadUsersForFilterAsync());
        if (!IsLoading && ParamLogs.Count == 0)
            tasks.Add(LoadParamLogsAsync());
        if (tasks.Count > 0)
            await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Loads the full user list from AdminApiService so the User filter dropdown
    /// shows real names instead of raw UUIDs.
    /// </summary>
    private async Task LoadUsersForFilterAsync()
    {
        if (_adminApiService == null) return;
        try
        {
            var response = await _adminApiService.GetAllUsersAsync();
            _userIdToDisplayName.Clear();
            _displayNameToUserId.Clear();
            foreach (var user in response.Users)
            {
                if (string.IsNullOrWhiteSpace(user.Id)) continue;
                var display = !string.IsNullOrWhiteSpace(user.FullName)
                    ? $"{user.FullName} ({user.Email})"
                    : user.Email ?? user.Id;
                _userIdToDisplayName[user.Id] = display;
                _displayNameToUserId[display] = user.Id;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Could not pre-load users for ParamLogs filter; will fall back to log-derived list");
        }
    }

    /// <summary>
    /// Returns a display-friendly name for a user ID, or the raw ID if unknown.
    /// </summary>
    private string GetUserDisplay(string userId) =>
        _userIdToDisplayName.TryGetValue(userId, out var name) ? name : userId;
    
    #endregion
    
    #region Commands
    
    [RelayCommand]
    private async Task LoadParamLogsAsync()
    {
        if (_apiService == null) return;
        
        IsLoading = true;
        HasError = false;
        StatusMessage = "Loading parameter logs...";
        
        try
        {
            _logger?.LogInformation("Loading param logs from API");
            
            // Build query string
            var queryParams = new System.Collections.Generic.List<string>
            {
                $"page={CurrentPage}",
                $"pageSize={PageSize}"
            };
            
            // Add search text filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                queryParams.Add($"search={Uri.EscapeDataString(SearchText)}");
            }
            
            // If a display name is selected in the filter, resolve it back to a userId
            if (!string.IsNullOrWhiteSpace(SelectedUserId) && SelectedUserId != "All Users")
            {
                // Try to map display name -> userId, otherwise pass as-is (may be a raw userId)
                var actualUserId = _displayNameToUserId.TryGetValue(SelectedUserId, out var uid) ? uid : SelectedUserId;
                queryParams.Add($"userId={Uri.EscapeDataString(actualUserId)}");
            }
            
            if (!string.IsNullOrWhiteSpace(SelectedDroneId) && SelectedDroneId != "All Drones")
                queryParams.Add($"droneId={Uri.EscapeDataString(SelectedDroneId)}");
            
            if (FromDate.HasValue)
                queryParams.Add($"fromDate={FromDate.Value:yyyy-MM-dd}");
            
            if (ToDate.HasValue)
                queryParams.Add($"toDate={ToDate.Value:yyyy-MM-dd}");
            
            var queryString = string.Join("&", queryParams);
            var result = await _apiService.GetParamLogsAsync(queryString);
            
            if (result != null)
            {
                ParamLogs.Clear();
                foreach (var log in result.Logs)
                {
                    // Prefer backend-provided UserName, then our admin-user lookup, then raw ID
                    var resolvedName = !string.IsNullOrWhiteSpace(log.UserName)
                        ? log.UserName
                        : _userIdToDisplayName.TryGetValue(log.UserId, out var mapped)
                            ? mapped
                            : null;

                    ParamLogs.Add(new ParamLogItem
                    {
                        Key = log.Key,
                        FileName = log.FileName,
                        UserId = log.UserId,
                        UserName = resolvedName ?? log.UserName,
                        DroneId = log.DroneId,
                        Timestamp = log.Timestamp,
                        Size = log.Size,
                        SizeDisplay = log.SizeDisplay
                    });
                }
                
                TotalCount = result.TotalCount;
                TotalPages = result.TotalPages;
                
                // Build AvailableUsers: use admin-loaded names where possible, fall back to log-derived list
                AvailableUsers.Clear();
                AvailableUsers.Add("All Users");

                // Merge: prefer the display names we already loaded from admin API;
                // fall back to the log-derived user list for any users not in admin list
                var logDerivedUsers = result.AvailableUsers
                    .Where(u => !string.IsNullOrWhiteSpace(u))
                    .ToList();

                if (_userIdToDisplayName.Count > 0)
                {
                    // Use the admin-loaded display names, ordered by display name
                    foreach (var display in _userIdToDisplayName.Values.OrderBy(x => x))
                        AvailableUsers.Add(display);
                }
                else
                {
                    // No admin API users loaded – show what the logs returned
                    foreach (var user in logDerivedUsers)
                        AvailableUsers.Add(user);
                }
                
                AvailableDrones.Clear();
                AvailableDrones.Add("All Drones");
                foreach (var drone in result.AvailableDrones.Where(d => !string.IsNullOrWhiteSpace(d)))
                {
                    AvailableDrones.Add(drone);
                }
                
                StatusMessage = $"Loaded {ParamLogs.Count} of {TotalCount} log(s)";
                _logger?.LogInformation("Loaded {Count} param logs", ParamLogs.Count);
            }
            else
            {
                StatusMessage = "No logs found";
            }
            
            OnPropertyChanged(nameof(HasNoLogs));
            OnPropertyChanged(nameof(HasPreviousPage));
            OnPropertyChanged(nameof(HasNextPage));
        }
        catch (HttpRequestException ex)
        {
            HasError = true;
            StatusMessage = $"Cannot connect to server: {ex.Message}";
            _logger?.LogError(ex, "Failed to connect to API");
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"Failed to load logs: {ex.Message}";
            _logger?.LogError(ex, "Failed to load param logs");
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        CurrentPage = 1;
        await LoadParamLogsAsync();
    }
    
    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        SearchText = null;
        SelectedUserId = null;
        SelectedDroneId = null;
        FromDate = DateTime.Today.AddDays(-30);
        ToDate = DateTime.Today;
        CurrentPage = 1;
        await LoadParamLogsAsync();
    }
    
    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (HasPreviousPage)
        {
            CurrentPage--;
            await LoadParamLogsAsync();
        }
    }
    
    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (HasNextPage)
        {
            CurrentPage++;
            await LoadParamLogsAsync();
        }
    }
    
    
    [RelayCommand]
    private async Task ViewLogAsync(ParamLogItem? log)
    {
        if (log == null || _apiService == null) return;
        
        SelectedLog = log;
        IsLoadingContent = true;
        SelectedLogChanges.Clear();
        StatusMessage = null;
        
        try
        {
            _logger?.LogInformation("Loading param log content: {Key}", log.Key);
            
            var result = await _apiService.GetParamLogContentAsync(log.Key);
            
            // Update log with username from CSV metadata if available
            if (!string.IsNullOrEmpty(result?.UserName))
            {
                log.UserName = result.UserName;
                OnPropertyChanged(nameof(SelectedLog));
            }
            
            if (result?.Changes != null && result.Changes.Count > 0)
            {
                foreach (var change in result.Changes)
                {
                    SelectedLogChanges.Add(new ParamChangeItem
                    {
                        ParamName = change.ParamName,
                        OldValue = change.OldValue,
                        NewValue = change.NewValue,
                        ChangedAt = change.ChangedAt
                    });
                }
                StatusMessage = $"Loaded {SelectedLogChanges.Count} parameter change(s)";
            }
            else
            {
                StatusMessage = "No parameter changes found in this log";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load param log content: {Key}", log.Key);
            StatusMessage = $"Failed to load log content: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsLoadingContent = false;
            OnPropertyChanged(nameof(HasSelectedLog));
            OnPropertyChanged(nameof(HasNoChanges));
            OnPropertyChanged(nameof(HasChangesToShow));
        }
    }
    
    [RelayCommand]
    private async Task DownloadLogAsync(ParamLogItem? log)
    {
        if (log == null || _apiService == null) return;
        
        try
        {
            _logger?.LogInformation("Getting download URL for: {Key}", log.Key);
            
            var downloadUrl = await _apiService.GetParamLogDownloadUrlAsync(log.Key);
            
            if (!string.IsNullOrEmpty(downloadUrl))
            {
                // Open in browser for download
                var process = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = downloadUrl,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(process);
                
                StatusMessage = $"Download started for {log.FileName}";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to download log: {Key}", log.Key);
            StatusMessage = $"Download failed: {ex.Message}";
        }
    }
    
    [RelayCommand]
    private void CloseLogDetails()
    {
        SelectedLog = null;
        SelectedLogChanges.Clear();
        OnPropertyChanged(nameof(HasSelectedLog));
    }
    
    #endregion
}

#region View Models

public class ParamLogItem
{
    public string Key { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string DroneId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public long Size { get; set; }
    public string SizeDisplay { get; set; } = string.Empty;
    
    public string TimestampDisplay => Timestamp.ToString("yyyy-MM-dd HH:mm:ss");
    
    /// <summary>
    /// Display name: shows UserName if available, otherwise truncated UserId
    /// </summary>
    public string UserDisplay => !string.IsNullOrEmpty(UserName) ? UserName : UserIdShort;
    
    /// <summary>
    /// Short version of User ID for display (first 12 chars + ...)
    /// </summary>
    public string UserIdShort => UserId.Length > 15 ? UserId[..12] + "..." : UserId;
}

public class ParamChangeItem
{
    public string ParamName { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public string ChangedAt { get; set; } = string.Empty;
}

#endregion
