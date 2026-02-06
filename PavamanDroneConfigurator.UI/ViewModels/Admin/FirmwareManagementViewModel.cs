using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Infrastructure.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.UI.ViewModels.Admin;

/// <summary>
/// ViewModel for Firmware Management admin panel.
/// PRODUCTION: Connects to backend API which handles AWS S3 operations.
/// Desktop app never accesses AWS directly for security.
/// </summary>
public partial class FirmwareManagementViewModel : ViewModelBase
{
    private readonly FirmwareApiService _firmwareApi;
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
    
    public bool CanUpload => !string.IsNullOrEmpty(NewFirmwareName) 
                             && !string.IsNullOrEmpty(NewFirmwareVersion) 
                             && !string.IsNullOrEmpty(SelectedFirmwareFilePath)
                             && !IsUploading;
    
    public bool HasNoFirmwares => !IsLoadingFirmwares && Firmwares.Count == 0;
    
    #endregion
    
    #region Constructor
    
    /// <summary>
    /// Constructor with DI for production use
    /// </summary>
    public FirmwareManagementViewModel(
        FirmwareApiService firmwareApi,
        ILogger<FirmwareManagementViewModel> logger)
    {
        _firmwareApi = firmwareApi;
        _logger = logger;
        
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
        if (!IsLoadingFirmwares && Firmwares.Count == 0)
        {
            await LoadFirmwaresAsync();
        }
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
            
            // Upload via API
            var progress = new Progress<int>(percent =>
            {
                UploadProgress = percent;
            });
            
            var result = await _firmwareApi.UploadFirmwareAsync(
                SelectedFirmwareFilePath!,
                safeFileName,
                progress
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
            StatusMessage = $"Downloading {firmware.FileName}...";
            _logger?.LogInformation("Downloading firmware: {FileName}", firmware.FileName);
            
            // Download using presigned URL from firmware metadata
            var progress = new Progress<int>(percent =>
            {
                StatusMessage = $"Downloading {firmware.FileName}... {percent}%";
            });
            
            var localPath = await _firmwareApi.DownloadFirmwareAsync(
                firmware.DownloadUrl,
                firmware.FileName,
                progress
            );
            
            firmware.DownloadCount++;
            StatusMessage = $"? Downloaded to: {localPath}";
            
            _logger?.LogInformation("Firmware downloaded to: {Path}", localPath);
            
            // TODO: Open file location
            await Task.Delay(3000);
            StatusMessage = string.Empty;
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
                
                OnPropertyChanged(nameof(HasNoFirmwares));
                
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
            
            OnPropertyChanged(nameof(HasNoFirmwares));
            
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

