using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Infrastructure.Services;
using System;
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
    private readonly ILogger<ParamLogsViewModel>? _logger;

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
    
    public ParamLogsViewModel(FirmwareApiService apiService, ILogger<ParamLogsViewModel>? logger = null)
    {
        _apiService = apiService;
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
    /// Initialize the page - load param logs
    /// </summary>
    public async Task InitializeAsync()
    {
        if (!IsLoading && ParamLogs.Count == 0)
        {
            await LoadParamLogsAsync();
        }
    }
    
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
                // Search can match userId, droneId, or userName
                queryParams.Add($"search={Uri.EscapeDataString(SearchText)}");
            }
            
            // Only add filters if not "All Users" or "All Drones"
            if (!string.IsNullOrWhiteSpace(SelectedUserId) && SelectedUserId != "All Users")
                queryParams.Add($"userId={Uri.EscapeDataString(SelectedUserId)}");
            
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
                    ParamLogs.Add(new ParamLogItem
                    {
                        Key = log.Key,
                        FileName = log.FileName,
                        UserId = log.UserId,
                        UserName = log.UserName,
                        DroneId = log.DroneId,
                        Timestamp = log.Timestamp,
                        Size = log.Size,
                        SizeDisplay = log.SizeDisplay
                    });
                }
                
                TotalCount = result.TotalCount;
                TotalPages = result.TotalPages;
                
                // Update filter options
                AvailableUsers.Clear();
                AvailableUsers.Add("All Users"); // Friendly "All" option
                foreach (var user in result.AvailableUsers.Where(u => !string.IsNullOrWhiteSpace(u)))
                {
                    AvailableUsers.Add(user);
                }
                
                AvailableDrones.Clear();
                AvailableDrones.Add("All Drones"); // Friendly "All" option
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
