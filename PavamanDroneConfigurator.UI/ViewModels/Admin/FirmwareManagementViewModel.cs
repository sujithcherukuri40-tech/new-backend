using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Infrastructure.Services;
using PavamanDroneConfigurator.Infrastructure.Services.Auth;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.UI.ViewModels.Admin;

/// <summary>
/// ViewModel for KFT Firmware Management admin panel.
/// PRODUCTION: Connects to backend API which handles AWS S3 operations.
/// Desktop app never accesses AWS directly for security.
/// </summary>
public partial class FirmwareManagementViewModel : ViewModelBase
{
    private readonly FirmwareApiService _firmwareApi;
    private readonly AdminApiService? _adminApiService;
    private readonly ILogger<FirmwareManagementViewModel>? _logger;

    #region Properties
    
    // Upload Form
    [ObservableProperty] private string? _newFirmwareName;
    [ObservableProperty] private string? _newFirmwareVersion;
    [ObservableProperty] private string? _newFirmwareDescription;
    [ObservableProperty] private string? _selectedVehicleType = "Copter";
    [ObservableProperty] private string? _selectedFirmwareFilePath;
    [ObservableProperty] private string? _selectedFirmwareFileName;
    
    // Upload Progress
    [ObservableProperty] private bool _isUploading;
    [ObservableProperty] private double _uploadProgress;
    [ObservableProperty] private string? _uploadStatusText;
    
    // List Management
    [ObservableProperty] private bool _isLoadingFirmwares;
    [ObservableProperty] private FirmwareItem? _selectedFirmware;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _hasError;
    
    // Stats
    [ObservableProperty] private int _totalFirmwareCount;
    [ObservableProperty] private string? _totalStorageUsed = "0 MB";
    
    public ObservableCollection<string> VehicleTypes { get; } = new()
    {
        "Copter",
        "Plane",
        "Rover",
        "Helicopter",
        "Sub",
        "Antenna Tracker"
    };
    
    public ObservableCollection<FirmwareItem> Firmwares { get; } = new();
    
    // User Assignment
    [ObservableProperty] private Core.Interfaces.AdminUserListItem? _selectedUser;
    [ObservableProperty] private FirmwareItem? _firmwareToAssign;
    [ObservableProperty] private bool _isLoadingUsers;
    [ObservableProperty] private bool _isLoadingUserFirmwares;
    [ObservableProperty] private bool _isAssigning;
    [ObservableProperty] private string? _assignStatusMessage;
    
    public ObservableCollection<Core.Interfaces.AdminUserListItem> Users { get; } = new();
    public ObservableCollection<UserFirmwareDto> SelectedUserFirmwares { get; } = new();
    public ObservableCollection<FirmwareItem> FilteredFirmwares { get; } = new();

    [ObservableProperty] private string? _firmwareSearchText;

    // ── Google-style filter chips & sort ─────────────────────────────────────
    /// Vehicle type filter chip selected by the user. "All" means no filter.
    [ObservableProperty] private string _vehicleTypeFilter = "All";

    /// Sort field: "name" | "version" | "size" | "date"
    [ObservableProperty] private string _sortField = "date";

    /// Sort direction: true = ascending
    [ObservableProperty] private bool _sortAscending = false;

    public int FilteredFirmwareCount => FilteredFirmwares.Count;

    public bool CanUpload => !string.IsNullOrEmpty(NewFirmwareName) 
                             && !string.IsNullOrEmpty(NewFirmwareVersion) 
                             && !string.IsNullOrEmpty(SelectedFirmwareFilePath)
                             && !IsUploading;
    
    public bool HasNoFirmwares => !IsLoadingFirmwares && FilteredFirmwares.Count == 0;
    public bool HasNoUserFirmwares => !IsLoadingUserFirmwares && SelectedUserFirmwares.Count == 0;
    public bool CanAssign => SelectedUser != null && FirmwareToAssign != null && !IsAssigning;
    
    #endregion
    
    #region Constructor
    
    /// <summary>
    /// Constructor with DI for production use
    /// </summary>
    public FirmwareManagementViewModel(
        FirmwareApiService firmwareApi,
        ILogger<FirmwareManagementViewModel> logger,
        AdminApiService? adminApiService = null)
    {
        _firmwareApi = firmwareApi;
        _logger = logger;
        _adminApiService = adminApiService;
        SelectedUserFirmwares.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoUserFirmwares));
        
        // DON'T load firmwares in constructor - defer until page is opened
        // This prevents slow startup when API is not accessible
    }
    
    /// <summary>
    /// Parameterless constructor for design-time support
    /// </summary>
    public FirmwareManagementViewModel()
    {
        _firmwareApi = null!;
        _logger = null;
    }

    /// <summary>
    /// Call this when the page is first shown to load firmwares from S3.
    /// </summary>
    public async Task InitializeAsync()
    {
        var tasks = new System.Collections.Generic.List<Task>();
        if (!IsLoadingFirmwares && Firmwares.Count == 0)
            tasks.Add(LoadFirmwaresAsync());
        if (!IsLoadingUsers && Users.Count == 0)
            tasks.Add(LoadUsersAsync());
        if (tasks.Count > 0)
            await Task.WhenAll(tasks);
    }
    
    partial void OnIsLoadingUserFirmwaresChanged(bool value) => OnPropertyChanged(nameof(HasNoUserFirmwares));

    partial void OnSelectedUserChanged(Core.Interfaces.AdminUserListItem? value)
    {
        OnPropertyChanged(nameof(CanAssign));
        AssignFirmwareToUserCommand.NotifyCanExecuteChanged();
        if (value != null)
            _ = LoadUserFirmwaresAsync(value.Id);
        else
            SelectedUserFirmwares.Clear();
    }

    partial void OnFirmwareToAssignChanged(FirmwareItem? value)
    {
        OnPropertyChanged(nameof(CanAssign));
        AssignFirmwareToUserCommand.NotifyCanExecuteChanged();
    }

    partial void OnFirmwareSearchTextChanged(string? value) => ApplyFirmwareFilter();
    partial void OnVehicleTypeFilterChanged(string value) => ApplyFirmwareFilter();
    partial void OnSortFieldChanged(string value) => ApplyFirmwareFilter();
    partial void OnSortAscendingChanged(bool value) => ApplyFirmwareFilter();

    [RelayCommand]
    private void SetVehicleFilter(string filter)
    {
        VehicleTypeFilter = filter;
    }

    [RelayCommand]
    private void SetSort(string field)
    {
        if (SortField == field)
            SortAscending = !SortAscending; // toggle direction
        else
        {
            SortField = field;
            SortAscending = field == "name" || field == "version";
        }
    }

    private void ApplyFirmwareFilter()
    {
        FilteredFirmwares.Clear();
        var search = FirmwareSearchText?.Trim().ToLowerInvariant();

        IEnumerable<FirmwareItem> source = Firmwares;

        // Text search
        if (!string.IsNullOrEmpty(search))
            source = source.Where(f =>
                (f.FirmwareName?.ToLowerInvariant().Contains(search) == true) ||
                (f.FirmwareVersion?.ToLowerInvariant().Contains(search) == true) ||
                (f.VehicleType?.ToLowerInvariant().Contains(search) == true) ||
                (f.FileName?.ToLowerInvariant().Contains(search) == true));

        // Vehicle type chip filter
        if (!string.IsNullOrEmpty(VehicleTypeFilter) && VehicleTypeFilter != "All")
            source = source.Where(f =>
                string.Equals(f.VehicleType, VehicleTypeFilter, StringComparison.OrdinalIgnoreCase));

        // Sort
        source = SortField switch
        {
            "name"    => SortAscending ? source.OrderBy(f => f.FirmwareName)    : source.OrderByDescending(f => f.FirmwareName),
            "version" => SortAscending ? source.OrderBy(f => f.FirmwareVersion) : source.OrderByDescending(f => f.FirmwareVersion),
            "size"    => SortAscending ? source.OrderBy(f => f.FileSize)   : source.OrderByDescending(f => f.FileSize),
            _         => SortAscending ? source.OrderBy(f => f.LastModified)    : source.OrderByDescending(f => f.LastModified),
        };

        foreach (var item in source)
            FilteredFirmwares.Add(item);

        OnPropertyChanged(nameof(HasNoFirmwares));
        OnPropertyChanged(nameof(FilteredFirmwareCount));
    }
    
    /// <summary>
    /// Force refresh - clears cache and reloads
    /// </summary>
    public async Task ForceRefreshAsync()
    {
        if (!IsLoadingFirmwares)
        {
            await LoadFirmwaresAsync();
        }
    }
    
    #endregion
    
    #region File Selection
    
    /// <summary>
    /// Sets the selected firmware file path
    /// </summary>
    public Task SetFirmwareFileAsync(string filePath)
    {
        if (File.Exists(filePath))
        {
            SelectedFirmwareFilePath = filePath;
            SelectedFirmwareFileName = Path.GetFileName(filePath);
            OnPropertyChanged(nameof(CanUpload));
        }
        return Task.CompletedTask;
    }
    
    #endregion
    
    #region Commands
    
    /// <summary>
    /// Uploads firmware to AWS S3 cloud storage via API
    /// </summary>
    [RelayCommand]
    private async Task UploadFirmwareAsync()
    {
        if (!CanUpload || _firmwareApi == null) return;
        
        try
        {
            IsUploading = true;
            UploadProgress = 0;
            UploadStatusText = "Preparing upload...";
            
            // Build filename: name-version.ext
            var ext = Path.GetExtension(SelectedFirmwareFilePath);
            var safeFileName = $"{SanitizeFileName(NewFirmwareName!)}-{NewFirmwareVersion}{ext}";
            
            _logger?.LogInformation("Uploading firmware: {FileName} to API", safeFileName);
            
            UploadProgress = 10;
            UploadStatusText = "Uploading to cloud...";
            
            // Upload via API with metadata
            var progress = new Progress<int>(percent =>
            {
                UploadProgress = percent;
            });
            
            var result = await _firmwareApi.UploadFirmwareAsync(
                SelectedFirmwareFilePath!,
                safeFileName,
                NewFirmwareName,           // firmwareName
                NewFirmwareVersion,        // firmwareVersion  
                NewFirmwareDescription,    // firmwareDescription
                progress                   // progress
            );
            
            UploadProgress = 90;
            UploadStatusText = "Finalizing...";
            
            // Create firmware item from result
            var firmware = new FirmwareItem
            {
                Id = result.Key,
                Name = NewFirmwareName ?? "",
                Version = NewFirmwareVersion ?? "",
                Description = NewFirmwareDescription ?? "",
                VehicleType = SelectedVehicleType ?? "Copter",
                FilePath = result.Key,
                FileName = result.FileName,
                FileSize = result.Size,
                UploadedDate = result.LastModified,
                DownloadCount = 0
            };
            
            // Add to list
            Firmwares.Insert(0, firmware);
            TotalFirmwareCount = Firmwares.Count;

            // Calculate storage
            var totalBytes = Firmwares.Sum(f => f.FileSize);
            TotalStorageUsed = FormatFileSize(totalBytes);

            ApplyFirmwareFilter();
            
            // Clear form
            ClearUploadForm();
            
            UploadProgress = 100;
            UploadStatusText = "\u2714 Upload completed successfully!";
            StatusMessage = $"? Uploaded {firmware.FileName}";
            
            _logger?.LogInformation("Firmware uploaded successfully: {FileName}", safeFileName);
            
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to upload firmware");
            UploadStatusText = $"\u274C Error: {ex.Message}";
            StatusMessage = $"? Upload failed: {ex.Message}";
        }
        finally
        {
            IsUploading = false;
            await Task.Delay(3000);
            UploadStatusText = string.Empty;
        }
    }
    
    /// <summary>
    /// Loads all firmwares from S3 cloud (forces refresh)
    /// </summary>
    [RelayCommand]
    private async Task RefreshFirmwaresAsync()
    {
        await ForceRefreshAsync();
    }
    
    /// <summary>
    /// Downloads firmware from cloud via API
    /// </summary>
    [RelayCommand]
    private async Task DownloadFirmwareAsync(FirmwareItem? firmware)
    {
        if (firmware == null || _firmwareApi == null) return;
        
        try
        {
            // Show save file dialog
            var saveDialog = new Avalonia.Platform.Storage.FilePickerSaveOptions
            {
                Title = "Save Firmware File",
                SuggestedFileName = firmware.FileName,
                DefaultExtension = Path.GetExtension(firmware.FileName),
                FileTypeChoices = new[]
                {
                    new Avalonia.Platform.Storage.FilePickerFileType("All Firmware Files")
                    {
                        Patterns = new[] { "*.hex", "*.bin", "*.apj", "*.px4" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("Intel HEX Files (*.hex)")
                    {
                        Patterns = new[] { "*.hex" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("Binary Files (*.bin)")
                    {
                        Patterns = new[] { "*.bin" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("ArduPilot JSON (*.apj)")
                    {
                        Patterns = new[] { "*.apj" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("PX4 Firmware (*.px4)")
                    {
                        Patterns = new[] { "*.px4" }
                    },
                    new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                    {
                        Patterns = new[] { "*.*" }
                    }
                },
                ShowOverwritePrompt = true
            };
            
            // Get the main window to show dialog
            var topLevel = Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
            
            if (topLevel == null)
            {
                StatusMessage = "? Cannot show save dialog";
                return;
            }
            
            var file = await topLevel.StorageProvider.SaveFilePickerAsync(saveDialog);
            
            if (file == null)
            {
                // User cancelled
                return;
            }
            
            StatusMessage = $"Downloading {firmware.FileName}...";
            _logger?.LogInformation("Downloading firmware: {FileName}", firmware.FileName);
            
            // Download using presigned URL from firmware metadata
            var progress = new Progress<int>(percent =>
            {
                StatusMessage = $"Downloading {firmware.FileName}... {percent}%";
            });
            
            // Download to temporary location first
            var tempPath = await _firmwareApi.DownloadFirmwareAsync(
                firmware.DownloadUrl,
                firmware.FileName,
                progress
            );
            
            // Copy to user-selected location
            if (File.Exists(tempPath))
            {
                using var sourceStream = File.OpenRead(tempPath);
                using var destStream = await file.OpenWriteAsync();
                await sourceStream.CopyToAsync(destStream);
                
                // Clean up temp file
                File.Delete(tempPath);
                
                firmware.DownloadCount++;
                StatusMessage = $"? Downloaded successfully!";
                
                _logger?.LogInformation("Firmware saved to: {Path}", file.Path);
                
                await Task.Delay(3000);
                StatusMessage = string.Empty;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to download firmware: {FileName}", firmware.FileName);
            StatusMessage = $"? Download failed: {ex.Message}";
            await Task.Delay(3000);
            StatusMessage = string.Empty;
        }
    }
    
    /// <summary>
    /// Edits firmware metadata
    /// </summary>
    [RelayCommand]
    private async Task EditFirmwareAsync(FirmwareItem? firmware)
    {
        if (firmware == null) return;
        
        StatusMessage = "?? Edit feature coming soon";
        await Task.Delay(2000);
        StatusMessage = string.Empty;
    }
    
    /// <summary>
    /// Assigns a firmware to the selected user
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanAssign))]
    private async Task AssignFirmwareToUserAsync()
    {
        if (SelectedUser == null || FirmwareToAssign == null || _adminApiService == null) return;
        
        try
        {
            IsAssigning = true;
            AssignStatusMessage = $"Assigning {FirmwareToAssign.FirmwareName} to {SelectedUser.FullName}...";
            
            await _adminApiService.AssignFirmwareToUserAsync(SelectedUser.Id, FirmwareToAssign.FilePath);
            
            AssignStatusMessage = $"✔ Assigned {FirmwareToAssign.FirmwareName} to {SelectedUser.FullName}";
            _logger?.LogInformation("Assigned firmware {Firmware} to user {User}", FirmwareToAssign.FileName, SelectedUser.FullName);
            
            // Refresh user's firmware list
            await LoadUserFirmwaresAsync(SelectedUser.Id);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to assign firmware to user");
            AssignStatusMessage = $"✘ Failed: {ex.Message}";
        }
        finally
        {
            IsAssigning = false;
            await Task.Delay(3000);
            AssignStatusMessage = string.Empty;
        }
    }
    
    /// <summary>
    /// Removes a firmware assignment from the selected user
    /// </summary>
    [RelayCommand]
    private async Task RemoveUserFirmwareAsync(UserFirmwareDto? assignment)
    {
        if (assignment == null || SelectedUser == null || _adminApiService == null) return;
        
        try
        {
            AssignStatusMessage = $"Removing {assignment.FirmwareName ?? assignment.FileName}...";
            
            var success = await _adminApiService.RemoveUserFirmwareAsync(SelectedUser.Id, assignment.Id.ToString());
            if (success)
            {
                SelectedUserFirmwares.Remove(assignment);
                AssignStatusMessage = $"✔ Removed assignment";
            }
            else
            {
                AssignStatusMessage = $"✘ Failed to remove assignment";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to remove user firmware assignment");
            AssignStatusMessage = $"✘ Error: {ex.Message}";
        }
        finally
        {
            await Task.Delay(3000);
            AssignStatusMessage = string.Empty;
        }
    }
    
    /// <summary>
    /// Deletes firmware from S3 cloud via API
    /// </summary>
    [RelayCommand]
    private async Task DeleteFirmwareAsync(FirmwareItem? firmware)
    {
        if (firmware == null || _firmwareApi == null) return;
        
        try
        {
            StatusMessage = $"Deleting {firmware.FileName}...";
            _logger?.LogInformation("User requested delete for firmware: {FileName}", firmware.FileName);
            
            // Delete via API
            var success = await _firmwareApi.DeleteFirmwareAsync(firmware.FilePath);
            
            if (success)
            {
                Firmwares.Remove(firmware);
                TotalFirmwareCount = Firmwares.Count;

                var totalBytes = Firmwares.Sum(f => f.FileSize);
                TotalStorageUsed = FormatFileSize(totalBytes);

                ApplyFirmwareFilter();
                
                StatusMessage = $"? Deleted {firmware.FileName}";
                _logger?.LogInformation("Firmware deleted successfully: {FileName}", firmware.FileName);
            }
            else
            {
                StatusMessage = $"? Failed to delete {firmware.FileName}";
                _logger?.LogWarning("Failed to delete firmware from S3: {FileName}", firmware.FileName);
            }
            
            await Task.Delay(3000);
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error deleting firmware: {FileName}", firmware?.FileName ?? "unknown");
            StatusMessage = $"? Delete failed: {ex.Message}";
            await Task.Delay(3000);
            StatusMessage = string.Empty;
        }
    }
    
    #endregion
    
    #region Private Methods
    
    private async Task LoadUsersAsync()
    {
        if (_adminApiService == null) return;
        
        IsLoadingUsers = true;
        try
        {
            var response = await _adminApiService.GetAllUsersAsync();
            Users.Clear();
            foreach (var user in response.Users.Where(u => u.IsApproved))
                Users.Add(user);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load users");
        }
        finally
        {
            IsLoadingUsers = false;
        }
    }
    
    private async Task LoadUserFirmwaresAsync(string userId)
    {
        if (_adminApiService == null) return;
        
        IsLoadingUserFirmwares = true;
        SelectedUserFirmwares.Clear();
        try
        {
            var firmwares = await _adminApiService.GetUserFirmwaresAsync(userId);
            foreach (var fw in firmwares)
                SelectedUserFirmwares.Add(fw);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load firmwares for user {UserId}", userId);
        }
        finally
        {
            IsLoadingUserFirmwares = false;
        }
    }
    
    private async Task LoadFirmwaresAsync()
    {
        if (_firmwareApi == null)
        {
            HasError = true;
            StatusMessage = "?? API Service not initialized - design mode";
            return;
        }
        
        IsLoadingFirmwares = true;
        HasError = false;
        StatusMessage = "Loading firmwares from cloud...";
        
        try
        {
            _logger?.LogInformation("Loading firmwares from API...");
            
            // Get firmware list from backend API (which accesses S3)
            var firmwares = await _firmwareApi.GetInAppFirmwaresAsync();
            
            Firmwares.Clear();
            
            foreach (var firmware in firmwares)
            {
                Firmwares.Add(new FirmwareItem
                {
                    Id = firmware.Key,
                    Name = firmware.DisplayName,
                    Version = ExtractVersionFromFileName(firmware.FileName),
                    Description = "",
                    VehicleType = firmware.VehicleType,
                    FilePath = firmware.Key,
                    FileName = firmware.FileName,
                    FileSize = firmware.Size,
                    UploadedDate = firmware.LastModified,
                    DownloadCount = 0,
                    DownloadUrl = firmware.DownloadUrl
                });
            }
            
            TotalFirmwareCount = Firmwares.Count;
            var totalBytes = Firmwares.Sum(f => f.FileSize);
            TotalStorageUsed = FormatFileSize(totalBytes);
            
            ApplyFirmwareFilter();
            StatusMessage = $"? Loaded {Firmwares.Count} firmware(s) successfully";
            _logger?.LogInformation("Loaded {Count} firmwares from API", Firmwares.Count);
        }
        catch (HttpRequestException ex)
        {
            HasError = true;
            StatusMessage = $"? Cannot connect to server: {ex.Message}";
            _logger?.LogError(ex, "Failed to connect to API");
        }
        catch (Exception ex)
        {
            HasError = true;
            StatusMessage = $"? Failed to load firmwares: {ex.Message}";
            _logger?.LogError(ex, "Failed to load firmwares from API");
        }
        finally
        {
            IsLoadingFirmwares = false;
        }
    }
    
    private void ClearUploadForm()
    {
        NewFirmwareName = string.Empty;
        NewFirmwareVersion = string.Empty;
        NewFirmwareDescription = string.Empty;
        SelectedFirmwareFilePath = string.Empty;
        SelectedFirmwareFileName = string.Empty;
        SelectedVehicleType = "Copter";
        OnPropertyChanged(nameof(CanUpload));
    }
    
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
    
    private static string SanitizeFileName(string name)
    {
        // Remove/replace invalid characters
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));
        return sanitized.Replace(" ", "-").ToLowerInvariant();
    }
    
    private static string ExtractVersionFromFileName(string fileName)
    {
        // Try to extract version like "4.5.2" from filename
        var name = Path.GetFileNameWithoutExtension(fileName);
        var parts = name.Split('-');
        
        foreach (var part in parts.Reverse())
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(part, @"^\d+\.\d+(\.\d+)?$"))
            {
                return part;
            }
        }
        
        return "1.0.0";
    }
    
    #endregion
}

/// <summary>
/// Represents a firmware item in the cloud
/// </summary>
public class FirmwareItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string VehicleType { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime UploadedDate { get; set; }
    public int DownloadCount { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
    
    // Alias properties for XAML binding compatibility
    public string FirmwareName => Name;
    public string FirmwareVersion => Version;
    public string FirmwareDescription => Description;
    public string SizeDisplay => FileSizeFormatted;
    public DateTime LastModified => UploadedDate;
    
    public string FileSizeFormatted => FormatSize(FileSize);
    public string UploadedDateFormatted => UploadedDate.ToString("MMM dd, yyyy");
    
    private static string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

