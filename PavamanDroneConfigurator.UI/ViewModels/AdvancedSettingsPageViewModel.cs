using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the Advanced Settings page that displays ArduPilot parameter metadata.
/// Provides search, filter, and detailed view capabilities for all available parameters.
/// Loads parameter definitions from the official ArduPilot JSON metadata file.
/// </summary>
public partial class AdvancedSettingsPageViewModel : ViewModelBase
{
    private readonly IArduPilotMetadataLoader _metadataLoader;

    #region Observable Properties - Page State

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private string _statusMessage = "Loading parameter metadata...";

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    #endregion

    #region Observable Properties - Data Collections

    [ObservableProperty]
    private ObservableCollection<ArduPilotParameterMetadata> _allParameters = new();

    [ObservableProperty]
    private ObservableCollection<ArduPilotParameterMetadata> _filteredParameters = new();

    [ObservableProperty]
    private ObservableCollection<string> _groups = new();

    #endregion

    #region Observable Properties - Filters

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _selectedGroup = "All";

    #endregion

    #region Observable Properties - Selected Parameter Details

    [ObservableProperty]
    private ArduPilotParameterMetadata? _selectedParameter;

    [ObservableProperty]
    private bool _hasSelectedParameter;

    [ObservableProperty]
    private string _selectedParamName = string.Empty;

    [ObservableProperty]
    private string _selectedParamDisplayName = string.Empty;

    [ObservableProperty]
    private string _selectedParamDescription = string.Empty;

    [ObservableProperty]
    private string _selectedParamGroup = string.Empty;

    [ObservableProperty]
    private string _selectedParamRange = string.Empty;

    [ObservableProperty]
    private string _selectedParamUnits = string.Empty;

    [ObservableProperty]
    private string _selectedParamIncrement = string.Empty;

    [ObservableProperty]
    private string _selectedParamUserType = string.Empty;

    [ObservableProperty]
    private bool _selectedParamRequiresReboot;

    [ObservableProperty]
    private bool _selectedParamIsReadOnly;

    [ObservableProperty]
    private bool _hasEnumOptions;

    [ObservableProperty]
    private ObservableCollection<ParameterEnumOption> _enumOptions = new();

    [ObservableProperty]
    private bool _hasBitmaskOptions;

    [ObservableProperty]
    private ObservableCollection<ParameterBitmaskOption> _bitmaskOptions = new();

    #endregion

    #region Observable Properties - Statistics

    [ObservableProperty]
    private int _totalParameterCount;

    [ObservableProperty]
    private int _filteredParameterCount;

    [ObservableProperty]
    private int _groupCount;

    [ObservableProperty]
    private int _parametersWithRanges;

    [ObservableProperty]
    private int _parametersWithEnums;

    #endregion

    /// <summary>
    /// Whether to show the empty state message (no parameters found).
    /// </summary>
    public bool ShowEmptyState => !IsLoading && FilteredParameters.Count == 0 && IsLoaded;

    public AdvancedSettingsPageViewModel(IArduPilotMetadataLoader metadataLoader)
    {
        _metadataLoader = metadataLoader;
        
        // Load metadata asynchronously when the page is created
        _ = LoadMetadataAsync();
    }

    /// <summary>
    /// Loads parameter metadata from the JSON file.
    /// </summary>
    [RelayCommand]
    private async Task LoadMetadataAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;
        StatusMessage = "Loading parameter metadata from file...";

        try
        {
            var metadata = await _metadataLoader.LoadAllMetadataAsync();

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AllParameters.Clear();
                foreach (var param in metadata)
                {
                    AllParameters.Add(param);
                }

                // Load groups
                Groups.Clear();
                Groups.Add("All");
                foreach (var group in _metadataLoader.GetGroups())
                {
                    Groups.Add(group);
                }

                // Update statistics
                var stats = _metadataLoader.GetStatistics();
                TotalParameterCount = stats.TotalParameters;
                GroupCount = stats.TotalGroups;
                ParametersWithRanges = stats.ParametersWithRanges;
                ParametersWithEnums = stats.ParametersWithEnums;

                // Apply initial filter
                ApplyFilter();

                IsLoaded = true;
                StatusMessage = $"Loaded {TotalParameterCount} parameters in {GroupCount} groups";
            });
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Failed to load metadata: {ex.Message}";
            StatusMessage = "Error loading parameter metadata";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Refreshes the metadata from the file.
    /// </summary>
    [RelayCommand]
    private async Task RefreshMetadataAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        StatusMessage = "Refreshing parameter metadata...";

        try
        {
            await _metadataLoader.RefreshAsync();
            await LoadMetadataAsync();
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Failed to refresh metadata: {ex.Message}";
            StatusMessage = "Error refreshing parameter metadata";
            IsLoading = false;
        }
    }

    /// <summary>
    /// Updates the selected parameter details when selection changes.
    /// </summary>
    partial void OnSelectedParameterChanged(ArduPilotParameterMetadata? value)
    {
        UpdateSelectedParameterDetails(value);
    }

    /// <summary>
    /// Notifies when IsLoading changes to update empty state visibility.
    /// </summary>
    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    /// <summary>
    /// Notifies when IsLoaded changes to update empty state visibility.
    /// </summary>
    partial void OnIsLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    /// <summary>
    /// Applies the filter when search text changes.
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// Applies the filter when selected group changes.
    /// </summary>
    partial void OnSelectedGroupChanged(string value)
    {
        ApplyFilter();
    }

    /// <summary>
    /// Applies current search and group filters to the parameter list.
    /// </summary>
    private void ApplyFilter()
    {
        FilteredParameters.Clear();

        IEnumerable<ArduPilotParameterMetadata> filtered = AllParameters;

        // Apply group filter
        if (!string.IsNullOrEmpty(SelectedGroup) && !SelectedGroup.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            filtered = filtered.Where(p => 
                p.Group.Equals(SelectedGroup, StringComparison.OrdinalIgnoreCase));
        }

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var searchLower = SearchText.ToLowerInvariant();
            filtered = filtered.Where(p =>
                p.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                (p.DisplayName?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (p.Description?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var param in filtered)
        {
            FilteredParameters.Add(param);
        }

        FilteredParameterCount = FilteredParameters.Count;
        OnPropertyChanged(nameof(ShowEmptyState));
        
        // Update status message
        if (!string.IsNullOrWhiteSpace(SearchText) || !SelectedGroup.Equals("All", StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = $"Showing {FilteredParameterCount} of {TotalParameterCount} parameters";
        }
        else
        {
            StatusMessage = $"Loaded {TotalParameterCount} parameters in {GroupCount} groups";
        }
    }

    /// <summary>
    /// Updates the detail panel with information about the selected parameter.
    /// </summary>
    private void UpdateSelectedParameterDetails(ArduPilotParameterMetadata? param)
    {
        HasSelectedParameter = param != null;
        EnumOptions.Clear();
        BitmaskOptions.Clear();
        HasEnumOptions = false;
        HasBitmaskOptions = false;

        if (param == null)
        {
            SelectedParamName = string.Empty;
            SelectedParamDisplayName = string.Empty;
            SelectedParamDescription = string.Empty;
            SelectedParamGroup = string.Empty;
            SelectedParamRange = string.Empty;
            SelectedParamUnits = string.Empty;
            SelectedParamIncrement = string.Empty;
            SelectedParamUserType = string.Empty;
            SelectedParamRequiresReboot = false;
            SelectedParamIsReadOnly = false;
            return;
        }

        SelectedParamName = param.Name;
        SelectedParamDisplayName = param.DisplayNameOrName;
        SelectedParamDescription = param.Description ?? "No description available";
        SelectedParamGroup = param.Group;
        SelectedParamRange = param.RangeDisplay;
        SelectedParamUnits = param.Units ?? "None";
        SelectedParamIncrement = param.Increment ?? "Not specified";
        SelectedParamUserType = param.User ?? "Standard";
        SelectedParamRequiresReboot = param.IsRebootRequired;
        SelectedParamIsReadOnly = param.IsReadOnly;

        // Populate enum options
        if (param.Values != null && param.Values.Count > 0)
        {
            foreach (var kvp in param.Values.OrderBy(x => 
                int.TryParse(x.Key, out var num) ? num : int.MaxValue))
            {
                if (int.TryParse(kvp.Key, out var value))
                {
                    EnumOptions.Add(new ParameterEnumOption
                    {
                        Value = value,
                        Label = kvp.Value
                    });
                }
            }
            HasEnumOptions = EnumOptions.Count > 0;
        }

        // Populate bitmask options
        if (param.Bitmask != null && param.Bitmask.Count > 0)
        {
            foreach (var kvp in param.Bitmask.OrderBy(x => 
                int.TryParse(x.Key, out var num) ? num : int.MaxValue))
            {
                if (int.TryParse(kvp.Key, out var bitPos))
                {
                    BitmaskOptions.Add(new ParameterBitmaskOption
                    {
                        BitPosition = bitPos,
                        Label = kvp.Value
                    });
                }
            }
            HasBitmaskOptions = BitmaskOptions.Count > 0;
        }
    }

    /// <summary>
    /// Clears the current search and group filter.
    /// </summary>
    [RelayCommand]
    private void ClearFilters()
    {
        SearchText = string.Empty;
        SelectedGroup = "All";
    }

    /// <summary>
    /// Copies the selected parameter name to clipboard.
    /// </summary>
    [RelayCommand]
    private async Task CopyParameterNameAsync()
    {
        if (SelectedParameter == null)
            return;

        try
        {
            var clipboard = GetClipboard();
            if (clipboard != null)
            {
                await clipboard.SetTextAsync(SelectedParameter.Name);
                StatusMessage = $"Copied '{SelectedParameter.Name}' to clipboard";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to copy to clipboard: {ex.Message}";
        }
    }

    /// <summary>
    /// Gets the clipboard from the main window.
    /// </summary>
    private IClipboard? GetClipboard()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow?.Clipboard;
        }
        return null;
    }
}
