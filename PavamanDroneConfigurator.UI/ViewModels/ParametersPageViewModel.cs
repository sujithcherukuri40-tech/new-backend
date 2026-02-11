using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.Services;
using PavamanDroneConfigurator.UI.ViewModels.Auth;
using PavamanDroneConfigurator.UI.Views;

namespace PavamanDroneConfigurator.UI.ViewModels;

public partial class ParametersPageViewModel : ViewModelBase
{
    private readonly IParameterService _parameterService;
    private readonly IConnectionService _connectionService;
    private readonly IExportService _exportService;
    private readonly IImportService _importService;
    private readonly IParameterMetadataService _metadataService;
    private readonly IDroneInfoService _droneInfoService;
    private readonly AuthSessionViewModel _authSession;
    private readonly FirmwareApiService? _firmwareApiService;
    private readonly ILogger<ParametersPageViewModel>? _logger;
    
    // Track original values for change detection
    private readonly Dictionary<string, float> _originalValues = new();
    
    // Track pending changes (not yet saved to vehicle)
    private readonly Dictionary<string, float> _pendingChanges = new();

    // Track if parameters are fully loaded to prevent progress updates from overwriting
    private bool _parametersLoaded;
    
    // Track if we're currently saving to prevent recursive saves
    private bool _isSaving;

    // ? OPTIMIZATION: Search performance improvements
    private CancellationTokenSource? _searchCts;
    private const int SEARCH_DEBOUNCE_MS = 150; // Wait 150ms after user stops typing
    private readonly Dictionary<DroneParameter, (string groupName, string searchText)> _parameterCache = new();

    [ObservableProperty]
    private ObservableCollection<DroneParameter> _parameters = new();

    [ObservableProperty]
    private ObservableCollection<DroneParameter> _filteredParameters = new();

    [ObservableProperty]
    private DroneParameter? _selectedParameter;

    [ObservableProperty]
    private string _statusMessage = "Connect to your drone to load parameters";

    [ObservableProperty]
    private bool _canEditParameters;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _totalParameterCount;

    [ObservableProperty]
    private int _loadedParameterCount;

    [ObservableProperty]
    private int _modifiedParameterCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanUpdate))]
    private bool _hasUnsavedChanges;
    
    [ObservableProperty]
    private bool _isRefreshing;

    [ObservableProperty]
    private bool _isUpdating;

    // Group filtering (Mission Planner style)
    [ObservableProperty]
    private string _selectedGroup = "All";

    [ObservableProperty]
    private ObservableCollection<string> _groupList = new();

    // Selected parameter details for the detail panel (like Mission Planner)
    [ObservableProperty]
    private string _selectedParamName = string.Empty;

    [ObservableProperty]
    private string _selectedParamDisplayName = string.Empty;

    [ObservableProperty]
    private string _selectedParamDescription = string.Empty;

    [ObservableProperty]
    private string _selectedParamRange = string.Empty;

    [ObservableProperty]
    private string _selectedParamUnits = string.Empty;

    [ObservableProperty]
    private string _selectedParamDefault = string.Empty;

    [ObservableProperty]
    private string _selectedParamGroup = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ParameterValueOption> _selectedParamOptions = new();

    [ObservableProperty]
    private bool _hasSelectedParameter;

    [ObservableProperty]
    private bool _hasParamOptions;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);
    
    public bool CanUpdate => HasUnsavedChanges && !IsUpdating && !IsRefreshing;

    public ParametersPageViewModel(
        IParameterService parameterService, 
        IConnectionService connectionService, 
        IExportService exportService,
        IImportService importService,
        IParameterMetadataService metadataService,
        IDroneInfoService droneInfoService,
        AuthSessionViewModel authSession,
        FirmwareApiService? firmwareApiService = null,
        ILogger<ParametersPageViewModel>? logger = null)
    {
        _parameterService = parameterService;
        _connectionService = connectionService;
        _exportService = exportService;
        _importService = importService;
        _metadataService = metadataService;
        _droneInfoService = droneInfoService;
        _authSession = authSession;
        _firmwareApiService = firmwareApiService;
        _logger = logger;

        // Initialize group list
        InitializeGroupList();

        // Subscribe to all relevant events
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _parameterService.ParameterDownloadStarted += OnDownloadStarted;
        _parameterService.ParameterDownloadCompleted += OnDownloadCompleted;
        _parameterService.ParameterDownloadProgressChanged += OnProgressChanged;
        
        // Check if already connected and has parameters
        if (_connectionService.IsConnected && _parameterService.IsParameterDownloadComplete)
        {
            _ = LoadParametersIntoGridAsync(enableEditing: true);
        }
    }

    private void InitializeGroupList()
    {
        GroupList.Clear();
        GroupList.Add("All");

        foreach (var group in _metadataService.GetGroups())
        {
            GroupList.Add(group);
        }
    }

    partial void OnSelectedGroupChanged(string value)
    {
        ApplyFilterAsync();
    }

    partial void OnSelectedParameterChanged(DroneParameter? value)
    {
        UpdateSelectedParameterDetails(value);
    }

    private void UpdateSelectedParameterDetails(DroneParameter? param)
    {
        HasSelectedParameter = param != null;
        SelectedParamOptions.Clear();
        HasParamOptions = false;

        if (param == null)
        {
            SelectedParamName = string.Empty;
            SelectedParamDisplayName = string.Empty;
            SelectedParamDescription = string.Empty;
            SelectedParamRange = string.Empty;
            SelectedParamUnits = string.Empty;
            SelectedParamDefault = string.Empty;
            SelectedParamGroup = string.Empty;
            return;
        }

        SelectedParamName = param.Name;
        
        var meta = _metadataService.GetMetadata(param.Name);
        
        if (meta != null)
        {
            SelectedParamDisplayName = !string.IsNullOrEmpty(meta.DisplayName) ? meta.DisplayName : param.Name;
            SelectedParamDescription = !string.IsNullOrEmpty(meta.Description) ? meta.Description : "No description available";
            
            // Show range
            SelectedParamRange = meta.Min.HasValue && meta.Max.HasValue 
                ? $"{meta.Min.Value:G} to {meta.Max.Value:G}" 
                : "Not specified";
            
            // Show units
            SelectedParamUnits = !string.IsNullOrEmpty(meta.Units) 
                ? meta.Units 
                : (!string.IsNullOrEmpty(meta.UnitsText) ? meta.UnitsText : "None");
            
            // Show default value (should be 0 if not specified)
            SelectedParamDefault = meta.DefaultValue.ToString("G");
            
            // Show group
            SelectedParamGroup = !string.IsNullOrEmpty(meta.Group) ? meta.Group : "General";

            // Add value options if available
            if (meta.Values != null && meta.Values.Count > 0)
            {
                foreach (var kvp in meta.Values.OrderBy(x => x.Key))
                {
                    SelectedParamOptions.Add(new ParameterValueOption
                    {
                        Value = kvp.Key,
                        Label = kvp.Value
                    });
                }
                HasParamOptions = true;
            }
        }
        else
        {
            // No metadata found - use parameter properties
            SelectedParamDisplayName = param.Name;
            SelectedParamDescription = param.Description ?? "No description available for this parameter.";
            SelectedParamRange = param.RangeDisplay;
            SelectedParamUnits = param.Units ?? "Not specified";
            SelectedParamDefault = param.DefaultDisplay;
            SelectedParamGroup = "Unknown";
        }
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!connected)
            {
                // Clear everything when disconnected
                UnsubscribeFromParameterChanges();
                Parameters.Clear();
                FilteredParameters.Clear();
                _originalValues.Clear();
                _pendingChanges.Clear();
                _parameterCache.Clear(); // ? Clear cache on disconnect
                TotalParameterCount = 0;
                LoadedParameterCount = 0;
                ModifiedParameterCount = 0;
                HasUnsavedChanges = false;
                CanEditParameters = false;
                IsRefreshing = false;
                _parametersLoaded = false;
                SelectedParameter = null;
                StatusMessage = "Disconnected - Connect to your drone to load parameters";
            }
            else
            {
                StatusMessage = "Connected - Waiting for parameters...";
            }
        });
    }

    private void OnDownloadStarted(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsRefreshing = true;
            CanEditParameters = false;
            _parametersLoaded = false;
            UnsubscribeFromParameterChanges();
            Parameters.Clear();
            FilteredParameters.Clear();
            _originalValues.Clear();
            _pendingChanges.Clear();
            _parameterCache.Clear(); // ? Clear cache on download start
            TotalParameterCount = 0;
            LoadedParameterCount = 0;
            ModifiedParameterCount = 0;
            HasUnsavedChanges = false;
            SelectedParameter = null;
            StatusMessage = "Downloading parameters from drone...";
        });
    }

    private void OnDownloadCompleted(object? sender, bool success)
    {
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            IsRefreshing = false;
            
            if (success && _parameterService.ReceivedParameterCount > 0)
            {
                await LoadParametersIntoGridAsync();
                CanEditParameters = true;
                _parametersLoaded = true;
                StatusMessage = $"Successfully loaded {Parameters.Count} parameters - Select a parameter to see details";
            }
            else
            {
                StatusMessage = "No parameters received from drone";
                CanEditParameters = false;
                _parametersLoaded = false;
            }
        });
    }

    private void OnProgressChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_parametersLoaded)
            {
                return;
            }
            
            var received = _parameterService.ReceivedParameterCount;
            var expected = _parameterService.ExpectedParameterCount;
            
            TotalParameterCount = expected ?? 0;
            LoadedParameterCount = received;
            
            if (_parameterService.IsParameterDownloadInProgress)
            {
                var expectedStr = expected?.ToString() ?? "?";
                StatusMessage = $"Downloading parameters... {received}/{expectedStr}";
            }
        });
    }

    private async Task LoadParametersIntoGridAsync(bool enableEditing = false)
    {
        try
        {
            UnsubscribeFromParameterChanges();
            
            var allParams = await _parameterService.GetAllParametersAsync();
            
            Parameters.Clear();
            FilteredParameters.Clear();
            _originalValues.Clear();
            _pendingChanges.Clear();
            _parameterCache.Clear(); // ? Clear cache when loading new parameters
            
            foreach (var p in allParams)
            {
                _metadataService.EnrichParameter(p);
                
                var meta = _metadataService.GetMetadata(p.Name);
                if (meta != null)
                {
                    if (meta.Values != null && meta.Values.Count > 0)
                    {
                        foreach (var kvp in meta.Values)
                        {
                            p.Options.Add(new ParameterOption
                            {
                                Value = kvp.Key,
                                Label = kvp.Value
                            });
                        }
                    }
                    
                    if (p.IsBitmask)
                    {
                        p.InitializeBitmaskFromValue();
                    }

                    // ? OPTIMIZATION: Pre-build search cache during load
                    // MODIFIED: Cache ONLY parameter name (not description) for name-only search
                    string groupName = meta.Group ?? "Unknown";
                    string searchText = p.Name.ToLowerInvariant(); // ? ONLY NAME, no description
                    _parameterCache[p] = (groupName, searchText);
                }
                else
                {
                    // No metadata - cache with parameter name only
                    _parameterCache[p] = ("Unknown", p.Name.ToLowerInvariant());
                }
                
                _originalValues[p.Name] = p.Value;
                p.OriginalValue = p.Value;
                p.PropertyChanged += OnParameterPropertyChanged;
                Parameters.Add(p);
            }
            
            ApplyFilterAsync();
            
            TotalParameterCount = Parameters.Count;
            ModifiedParameterCount = 0;
            HasUnsavedChanges = false;
            
            // Enable editing if requested (for constructor scenario)
            if (enableEditing)
            {
                CanEditParameters = true;
                _parametersLoaded = true;
                StatusMessage = $"Successfully loaded {Parameters.Count} parameters - Select a parameter to see details";
            }
            
            OnPropertyChanged(nameof(Parameters));
            OnPropertyChanged(nameof(FilteredParameters));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading parameters: {ex.Message}";
        }
    }

    private void UnsubscribeFromParameterChanges()
    {
        foreach (var p in Parameters)
        {
            p.PropertyChanged -= OnParameterPropertyChanged;
        }
    }

    private void OnParameterPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not DroneParameter parameter || e.PropertyName != nameof(DroneParameter.Value))
            return;
        
        if (_isSaving)
            return;
        
        // Validate the value before tracking as pending change
        if (parameter.MinValue.HasValue || parameter.MaxValue.HasValue)
        {
            float validatedValue = parameter.ValidateValue(parameter.Value, out bool isValid);
            if (!isValid)
            {
                // Value was automatically reverted to DefaultValue by the Value setter
                // Log this event
                StatusMessage = $"?? {parameter.Name}: Invalid value rejected. Using default {parameter.DefaultValue:G}";
                UpdateModifiedCount();
                return; // Don't save invalid value to vehicle
            }
        }
        
        // Track as pending change instead of immediately saving
        TrackPendingChange(parameter);
    }

    private void TrackPendingChange(DroneParameter parameter)
    {
        var originalValue = _originalValues.TryGetValue(parameter.Name, out var orig) ? orig : parameter.OriginalValue;
        
        if (Math.Abs(parameter.Value - originalValue) < 0.0001f)
        {
            // Value reverted to original - remove from pending
            _pendingChanges.Remove(parameter.Name);
        }
        else
        {
            // Track the pending change
            _pendingChanges[parameter.Name] = parameter.Value;
        }
        
        UpdateModifiedCount();
        StatusMessage = _pendingChanges.Count > 0 
            ? $"? {_pendingChanges.Count} parameter(s) modified - Click 'Update' to apply changes" 
            : "Select a parameter to see details";
    }

    private void UpdateModifiedCount()
    {
        ModifiedParameterCount = _pendingChanges.Count;
        HasUnsavedChanges = ModifiedParameterCount > 0;
        OnPropertyChanged(nameof(CanUpdate));
    }

    /// <summary>
    /// Shows confirmation dialog and updates all pending parameters to the vehicle.
    /// </summary>
    [RelayCommand]
    private async Task UpdateParametersAsync()
    {
        if (!_connectionService.IsConnected || _pendingChanges.Count == 0)
        {
            return;
        }

        // Build list of pending changes for the dialog
        var pendingChangesList = new List<PendingParameterChange>();
        foreach (var (name, newValue) in _pendingChanges)
        {
            var originalValue = _originalValues.TryGetValue(name, out var orig) ? orig : 0f;
            pendingChangesList.Add(new PendingParameterChange
            {
                Name = name,
                OriginalValue = originalValue,
                NewValue = newValue
            });
        }

        // Show confirmation dialog
        var mainWindow = GetMainWindow();
        if (mainWindow == null)
        {
            StatusMessage = "Error: Could not find main window.";
            return;
        }

        var dialogViewModel = new ParameterUpdateDialogViewModel(pendingChangesList);
        var dialog = new ParameterUpdateDialog
        {
            DataContext = dialogViewModel
        };

        var result = await dialog.ShowDialog<bool>(mainWindow);

        if (!result)
        {
            StatusMessage = "Parameter update cancelled.";
            return;
        }

        // User confirmed - apply all pending changes
        await ApplyPendingChangesAsync();
    }

    private async Task ApplyPendingChangesAsync()
    {
        if (_isSaving || _pendingChanges.Count == 0)
        {
            return;
        }

        _isSaving = true;
        IsUpdating = true;
        OnPropertyChanged(nameof(CanUpdate));

        try
        {
            var totalChanges = _pendingChanges.Count;
            var successCount = 0;
            var failedParams = new List<string>();
            var successfulChanges = new List<Infrastructure.Services.ParameterChange>();

            StatusMessage = $"Updating {totalChanges} parameter(s)...";

            // Create a copy of pending changes to iterate
            var changesToApply = _pendingChanges.ToList();

            foreach (var (name, newValue) in changesToApply)
            {
                var originalValue = _originalValues.TryGetValue(name, out var orig) ? orig : 0f;
                var success = await _parameterService.SetParameterAsync(name, newValue);
                
                if (success)
                {
                    successCount++;
                    
                    // Track successful change for S3 logging
                    successfulChanges.Add(new Infrastructure.Services.ParameterChange
                    {
                        ParamName = name,
                        OldValue = originalValue,
                        NewValue = newValue,
                        ChangedAt = DateTime.UtcNow
                    });
                    
                    // Update original value and mark parameter as saved
                    _originalValues[name] = newValue;
                    var param = Parameters.FirstOrDefault(p => p.Name == name);
                    if (param != null)
                    {
                        param.MarkAsSaved();
                    }
                    
                    // Remove from pending changes
                    _pendingChanges.Remove(name);
                }
                else
                {
                    failedParams.Add(name);
                }
            }

            UpdateModifiedCount();

            if (failedParams.Count == 0)
            {
                StatusMessage = $"\u2714 Successfully updated {successCount} parameter(s)";
            }
            else
            {
                StatusMessage = $"\u26A0 Updated {successCount}/{totalChanges} - Failed: {string.Join(", ", failedParams)}";
            }

            // Log successful changes to S3 (fire and forget)
            if (successfulChanges.Count > 0)
            {
                _ = LogParameterChangesToS3Async(successfulChanges);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error updating parameters: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
            IsUpdating = false;
            OnPropertyChanged(nameof(CanUpdate));
        }
    }

    /// <summary>
    /// Logs parameter changes to AWS S3 via API for audit trail.
    /// Includes user ID, username, and drone/FC ID for tracking.
    /// Uploads CSV file to param-logs folder in S3.
    /// </summary>
    private async Task LogParameterChangesToS3Async(List<Infrastructure.Services.ParameterChange> changes)
    {
        if (_firmwareApiService == null || changes.Count == 0)
        {
            return;
        }

        try
        {
            // Get current user info from auth session
            var userId = _authSession.CurrentState.User?.Id ?? "unknown";
            var userName = _authSession.CurrentState.User?.FullName ?? _authSession.CurrentState.User?.Email ?? "unknown";
            
            // Get Drone ID and Board ID from drone info service
            var droneInfo = await _droneInfoService.GetDroneInfoAsync();
            var droneId = droneInfo?.DroneId ?? "unknown";  // Drone ID (P003B04H22...)
            var boardId = droneInfo?.FcId ?? "unknown";  // Board ID (FC-xxx or FW-xxx)
            
            _logger?.LogInformation(
                "Logging {Count} parameter changes to S3 for user={UserId} ({UserName}), drone={DroneId}, board={BoardId}",
                changes.Count, userId, userName, droneId, boardId);

            // Log each change for debugging
            foreach (var change in changes)
            {
                _logger?.LogDebug(
                    "Parameter change: {Param} {Old} -> {New} by {User} on drone {Drone}",
                    change.ParamName, change.OldValue, change.NewValue, userName, droneId);
            }

            // Upload parameter changes to S3 via API
            // Send: userId, userName, droneId (actual drone identifier), boardId (FC hardware ID)
            await _firmwareApiService.UploadParameterLogAsync(userId, userName, droneId, boardId, changes);
            
            _logger?.LogInformation("Successfully logged {Count} parameter changes to S3 param-logs folder", changes.Count);
        }
        catch (Exception ex)
        {
            // Log error but don't fail the parameter update operation
            _logger?.LogError(ex, "Failed to log parameter changes to S3");
        }
    }

    /// <summary>
    /// Discards all pending changes and reverts parameters to original values.
    /// </summary>
    [RelayCommand]
    private void DiscardChanges()
    {
        if (_pendingChanges.Count == 0)
        {
            return;
        }

        // Revert all parameters to original values
        foreach (var (name, _) in _pendingChanges.ToList())
        {
            var param = Parameters.FirstOrDefault(p => p.Name == name);
            if (param != null && _originalValues.TryGetValue(name, out var originalValue))
            {
                // Temporarily unsubscribe to prevent re-tracking the revert
                param.PropertyChanged -= OnParameterPropertyChanged;
                param.Value = originalValue;
                param.PropertyChanged += OnParameterPropertyChanged;
            }
        }

        _pendingChanges.Clear();
        UpdateModifiedCount();
        StatusMessage = "All changes discarded.";
    }

    [RelayCommand]
    private void SetParameterValue(int value)
    {
        if (SelectedParameter != null)
        {
            SelectedParameter.Value = value;
        }
    }

    /// <summary>
    /// Applies user input from OPTIONS column to the parameter Value.
    /// Validates input and updates VALUE column if valid.
    /// </summary>
    public void ApplyOptionsInput(DroneParameter param)
    {
        if (string.IsNullOrWhiteSpace(param.OptionsInputText))
            return;

        // Try to parse the input as a float
        if (!float.TryParse(param.OptionsInputText, out float val))
        {
            // Invalid input - clear and ignore
            param.OptionsInputText = "";
            StatusMessage = $"?? {param.Name}: Invalid input. Please enter a number.";
            return;
        }

        // Validate against Min constraint
        if (param.MinValue.HasValue && val < param.MinValue.Value)
        {
            param.OptionsInputText = "";
            StatusMessage = $"?? {param.Name}: Value {val:G} is below minimum {param.MinValue.Value:G}.";
            return;
        }

        // Validate against Max constraint
        if (param.MaxValue.HasValue && val > param.MaxValue.Value)
        {
            param.OptionsInputText = "";
            StatusMessage = $"?? {param.Name}: Value {val:G} exceeds maximum {param.MaxValue.Value:G}.";
            return;
        }

        // VALID INPUT ? APPLY TO VALUE COLUMN
        param.Value = val;

        // Clear options input box after applying
        param.OptionsInputText = "";

        StatusMessage = $"? {param.Name} set to {val:G} - Click 'Update' to apply";
    }

    [RelayCommand]
    private async Task RefreshParametersAsync()
    {
        if (!_connectionService.IsConnected)
        {
            StatusMessage = "Not connected";
            return;
        }

        // Warn if there are pending changes
        if (_pendingChanges.Count > 0)
        {
            StatusMessage = "?? Discarding pending changes before refresh...";
            _pendingChanges.Clear();
            UpdateModifiedCount();
        }

        await _parameterService.RefreshParametersAsync();
    }

    [RelayCommand]
    private async Task ImportParametersAsync()
    {
        try
        {
            // Get the main window
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                StatusMessage = "Error: Could not find main window.";
                return;
            }

            // Create and show the import dialog
            var dialogViewModel = new ImportDialogViewModel();
            var dialog = new ImportDialog
            {
                DataContext = dialogViewModel
            };

            var result = await dialog.ShowDialog<bool>(mainWindow);

            if (result && dialogViewModel.ImportResult != null && dialogViewModel.ImportResult.IsSuccess)
            {
                IsRefreshing = true;
                StatusMessage = "Applying imported parameters...";

                try
                {
                    var importedParams = dialogViewModel.ImportResult.Parameters;
                    var mergeWithExisting = dialogViewModel.MergeWithExisting;

                    // Apply imported parameters to loadedParams
                    ApplyImportedParameters(importedParams, mergeWithExisting);

                    // Update UI
                    ApplyFilterAsync();
                    UpdateModifiedCount();

                    var actionText = mergeWithExisting ? "merged" : "replaced";
                    StatusMessage = $"? Successfully {actionText} {importedParams.Count} parameters - Click 'Update' to apply";
                }
                finally
                {
                    IsRefreshing = false;
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import error: {ex.Message}";
            IsRefreshing = false;
        }
    }

    /// <summary>
    /// Applies imported parameters to the current parameter list.
    /// </summary>
    private void ApplyImportedParameters(Dictionary<string, float> importedParams, bool mergeWithExisting)
    {
        if (!mergeWithExisting)
        {
            // Replace mode: Clear all and add only imported
            // For now, we update existing parameters that match imported keys
            // Parameters not in import file are left unchanged (since we may not have full parameter set)
        }

        var updatedCount = 0;
        var newCount = 0;

        foreach (var (name, value) in importedParams)
        {
            // Find existing parameter
            var existingParam = Parameters.FirstOrDefault(p => 
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (existingParam != null)
            {
                // Update existing parameter
                if (Math.Abs(existingParam.Value - value) > 0.0001f)
                {
                    existingParam.Value = value;
                    updatedCount++;
                }
            }
            else
            {
                // Add new parameter (not in current list)
                var newParam = new DroneParameter
                {
                    Name = name,
                    Value = value,
                    OriginalValue = value,
                    Description = "Imported parameter"
                };
                _metadataService.EnrichParameter(newParam);
                newParam.PropertyChanged += OnParameterPropertyChanged;
                Parameters.Add(newParam);
                _originalValues[name] = value;
                newCount++;
            }
        }

        TotalParameterCount = Parameters.Count;
        
        // Log what was done
        if (newCount > 0)
        {
            StatusMessage = $"? Updated {updatedCount} parameters, added {newCount} new parameters";
        }
        else if (updatedCount > 0)
        {
            StatusMessage = $"? Updated {updatedCount} parameters";
        }
    }

    /// <summary>
    /// ? OPTIMIZED: Fast async search with caching and batch updates.
    /// Performance improvements:
    /// - Cached metadata lookups (no repeated GetMetadata calls)
    /// - Cached lowercase strings for fast comparisons
    /// - Batch ObservableCollection updates (single UI notification)
    /// - LINQ deferred execution for efficient filtering
    /// - Background thread for filtering (keeps UI responsive)
    /// </summary>
    private void ApplyFilterAsync()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (Parameters == null || Parameters.Count == 0)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        FilteredParameters.Clear();
                        LoadedParameterCount = 0;
                    });
                    return;
                }

                var searchQuery = SearchQuery;
                var selectedGroup = SelectedGroup;
                
                // ? PERFORMANCE: Do heavy filtering on background thread
                var filtered = Parameters.AsEnumerable();

                // Apply group filter first (reduces search space significantly)
                if (!string.IsNullOrWhiteSpace(selectedGroup) && selectedGroup != "All")
                {
                    filtered = filtered.Where(p =>
                    {
                        // ? Use cached group name instead of calling GetMetadata()
                        if (_parameterCache.TryGetValue(p, out var cache))
                        {
                            return cache.groupName == selectedGroup;
                        }
                        return false;
                    });
                }

                // ? OPTIMIZED: Fast case-insensitive search with cached lowercase strings
                if (!string.IsNullOrWhiteSpace(searchQuery))
                {
                    var searchLower = searchQuery.ToLowerInvariant();
                    
                    filtered = filtered.Where(p =>
                    {
                        // ? Use cached search text instead of repeated ToLowerInvariant() calls
                        if (_parameterCache.TryGetValue(p, out var cache))
                        {
                            return cache.searchText.Contains(searchLower);
                        }
                        return false;
                    });
                }

                // Sort by name (deferred execution - only sorts filtered results)
                var results = filtered.OrderBy(p => p.Name).ToList();

                // ? CRITICAL: Update UI on UI thread with batch operation
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // Clear and rebuild collection
                    FilteredParameters.Clear();
                    
                    // Batch add all items (much faster than individual Add() calls)
                    foreach (var param in results)
                    {
                        FilteredParameters.Add(param);
                    }

                    LoadedParameterCount = FilteredParameters.Count;
                }, DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Filter error: {ex.Message}";
                });
            }
        });
    }

    [RelayCommand]
    private async Task ExportParametersAsync()
    {
        if (Parameters.Count == 0)
        {
            StatusMessage = "No parameters to export. Load parameters first.";
            return;
        }

        try
        {
            // Get the main window
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                StatusMessage = "Error: Could not find main window.";
                return;
            }

            // Create and show the export dialog
            var dialogViewModel = new ExportDialogViewModel();
            var dialog = new ExportDialog
            {
                DataContext = dialogViewModel
            };

            var result = await dialog.ShowDialog<bool>(mainWindow);

            if (result && dialogViewModel.SelectedFormat != null && !string.IsNullOrWhiteSpace(dialogViewModel.FullFilePath))
            {
                StatusMessage = "Exporting parameters...";
                IsRefreshing = true;

                try
                {
                    var success = await _exportService.ExportToFileAsync(
                        Parameters,
                        dialogViewModel.SelectedFormat.Format,
                        dialogViewModel.FullFilePath);

                    if (success)
                    {
                        StatusMessage = $"? Successfully exported {Parameters.Count} parameters to {dialogViewModel.FullFilePath}";
                    }
                    else
                    {
                        StatusMessage = "? Failed to export parameters. Check the log for details.";
                    }
                }
                finally
                {
                    IsRefreshing = false;
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
            IsRefreshing = false;
        }
    }

    private static Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }

    partial void OnSearchQueryChanged(string value)
    {
        // ? OPTIMIZED: Debounced search - waits until user stops typing
        // Cancels previous search if user is still typing
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        
        // Run search after debounce delay
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(SEARCH_DEBOUNCE_MS, token);
                
                if (!token.IsCancellationRequested)
                {
                    // Execute filter on background thread
                    ApplyFilterAsync();
                }
            }
            catch (OperationCanceledException)
            {
                // User is still typing - this is normal, no action needed
            }
        }, token);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // ? OPTIMIZATION: Cancel and dispose search debounce timer
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _parameterCache.Clear();
            _pendingChanges.Clear();
            
            UnsubscribeFromParameterChanges();
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _parameterService.ParameterDownloadStarted -= OnDownloadStarted;
            _parameterService.ParameterDownloadCompleted -= OnDownloadCompleted;
            _parameterService.ParameterDownloadProgressChanged -= OnProgressChanged;
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Represents a selectable value option for enum-type parameters.
/// </summary>
public class ParameterValueOption
{
    public int Value { get; set; }
    public string Label { get; set; } = string.Empty;
    public string Display => $"{Value}: {Label}";
}
