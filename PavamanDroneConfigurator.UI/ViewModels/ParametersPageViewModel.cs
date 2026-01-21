using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.UI.Views;

namespace PavamanDroneConfigurator.UI.ViewModels;

public partial class ParametersPageViewModel : ViewModelBase
{
    private readonly IParameterService _parameterService;
    private readonly IConnectionService _connectionService;
    private readonly IExportService _exportService;
    private readonly IImportService _importService;
    private readonly IParameterMetadataService _metadataService;
    
    // Track original values for change detection
    private readonly Dictionary<string, float> _originalValues = new();

    // Track if parameters are fully loaded to prevent progress updates from overwriting
    private bool _parametersLoaded;
    
    // Track if we're currently saving to prevent recursive saves
    private bool _isSaving;

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
    private bool _hasUnsavedChanges;
    
    [ObservableProperty]
    private bool _isRefreshing;

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

    public ParametersPageViewModel(
        IParameterService parameterService, 
        IConnectionService connectionService, 
        IExportService exportService,
        IImportService importService,
        IParameterMetadataService metadataService)
    {
        _parameterService = parameterService;
        _connectionService = connectionService;
        _exportService = exportService;
        _importService = importService;
        _metadataService = metadataService;

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
            _ = LoadParametersIntoGridAsync();
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
        ApplyFilter();
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
        Dispatcher.UIThread.Post(async () =>
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

    private async Task LoadParametersIntoGridAsync()
    {
        try
        {
            UnsubscribeFromParameterChanges();
            
            var allParams = await _parameterService.GetAllParametersAsync();
            
            Parameters.Clear();
            FilteredParameters.Clear();
            _originalValues.Clear();
            
            foreach (var p in allParams)
            {
                // Always enrich with metadata
                _metadataService.EnrichParameter(p);
                
                var meta = _metadataService.GetMetadata(p.Name);
                if (meta != null)
                {
                    // Set default value (0 if not specified in metadata)
                    p.DefaultValue = meta.DefaultValue;
                    
                    // Set step size (1 if not specified)
                    p.StepSize = meta.StepSize ?? 1.0f;
                    
                    // Set min/max from metadata
                    p.MinValue = meta.Min;
                    p.MaxValue = meta.Max;
                    
                    // Set units
                    p.Units = meta.Units;
                    
                    // Set description
                    p.Description = meta.Description;
                    
                    // Populate options (enum or bitmask)
                    if (meta.Values != null && meta.Values.Count > 0)
                    {
                        p.Options.Clear();
                        foreach (var kvp in meta.Values.OrderBy(x => x.Key))
                        {
                            p.Options.Add(new ParameterOption
                            {
                                Value = kvp.Key,
                                Label = kvp.Value
                            });
                        }
                        
                        // If bitmask, initialize selected options from current value
                        if (p.IsBitmask)
                        {
                            p.InitializeBitmaskFromValue();
                        }
                    }
                }
                else
                {
                    // No metadata - set defaults
                    p.DefaultValue = 0;
                    p.StepSize = 1.0f;
                }
                
                // Store original value
                _originalValues[p.Name] = p.Value;
                p.OriginalValue = p.Value;
                
                // Subscribe to property changes
                p.PropertyChanged += OnParameterPropertyChanged;
                
                // Add to collection
                Parameters.Add(p);
            }
            
            // Apply filters to populate FilteredParameters
            ApplyFilter();
            
            // Update counts
            TotalParameterCount = Parameters.Count;
            ModifiedParameterCount = 0;
            HasUnsavedChanges = false;
            
            // Notify UI
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
        
        // Validate the value before saving
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
        
        UpdateModifiedCount();
        _ = SaveParameterToVehicleAsync(parameter);
    }

    private void UpdateModifiedCount()
    {
        ModifiedParameterCount = Parameters.Count(p => p.IsModified);
        HasUnsavedChanges = ModifiedParameterCount > 0;
    }

    private async Task SaveParameterToVehicleAsync(DroneParameter parameter)
    {
        if (!_connectionService.IsConnected || _isSaving)
        {
            return;
        }

        _isSaving = true;
        try
        {
            StatusMessage = $"Updating {parameter.Name}...";
            
            var success = await _parameterService.SetParameterAsync(parameter.Name, parameter.Value);
            
            if (success)
            {
                parameter.MarkAsSaved();
                _originalValues[parameter.Name] = parameter.Value;
                UpdateModifiedCount();
                StatusMessage = $"? Updated {parameter.Name} = {parameter.Value}";
            }
            else
            {
                StatusMessage = $"? Failed to update {parameter.Name}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error updating {parameter.Name}: {ex.Message}";
        }
        finally
        {
            _isSaving = false;
        }
    }

    [RelayCommand]
    private void SetParameterValue(int value)
    {
        if (SelectedParameter != null)
        {
            SelectedParameter.Value = value;
        }
    }

    [RelayCommand]
    private async Task RefreshParametersAsync()
    {
        if (!_connectionService.IsConnected)
        {
            StatusMessage = "Not connected";
            return;
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
                    ApplyFilter();
                    UpdateModifiedCount();

                    var actionText = mergeWithExisting ? "merged" : "replaced";
                    StatusMessage = $"? Successfully {actionText} {importedParams.Count} parameters from file";
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
    /// Applies the current search and group filters to update FilteredParameters.
    /// </summary>
    private void ApplyFilter()
    {
        if (Parameters == null)
            return;

        IEnumerable<DroneParameter> filtered = Parameters;

        // Apply group filter
        if (!string.IsNullOrWhiteSpace(SelectedGroup) && SelectedGroup != "All")
        {
            filtered = filtered.Where(p =>
            {
                var meta = _metadataService.GetMetadata(p.Name);
                return meta != null && meta.Group == SelectedGroup;
            });
        }

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            filtered = filtered.Where(p =>
                p.Name.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ||
                (p.Description?.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // Sort by name
        filtered = filtered.OrderBy(p => p.Name);

        // Clear and populate FilteredParameters
        FilteredParameters.Clear();
        foreach (var p in filtered)
        {
            FilteredParameters.Add(p);
        }

        LoadedParameterCount = FilteredParameters.Count;
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
        ApplyFilter();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
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
