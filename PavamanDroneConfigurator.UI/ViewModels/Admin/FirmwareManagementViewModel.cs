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
/// Connects to AWS S3 for actual cloud storage operations.
/// </summary>
public partial class FirmwareManagementViewModel : ViewModelBase
{
    private readonly AwsS3Service? _s3Service;
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
        AwsS3Service s3Service,
        ILogger<FirmwareManagementViewModel> logger)
    {
        _s3Service = s3Service;
        _logger = logger;
        
        // DON'T load firmwares in constructor - defer until page is opened
        // This prevents slow startup when S3 is not accessible
        // _ = LoadFirmwaresAsync();  // REMOVED - call InitializeAsync when page is shown
    }
    
    /// <summary>
    /// Parameterless constructor for design-time support
    /// </summary>
    public FirmwareManagementViewModel()
    {
        _s3Service = null;
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
    /// Uploads firmware to AWS S3 cloud storage
    /// </summary>
    [RelayCommand]
    private async Task UploadFirmwareAsync()
    {
        if (!CanUpload || _s3Service == null) return;
        
        try
        {
            IsUploading = true;
            UploadProgress = 0;
            UploadStatusText = "Preparing upload...";
            
            // Build filename: name-version.apj
            var safeFileName = $"{SanitizeFileName(NewFirmwareName!)}-{NewFirmwareVersion}{Path.GetExtension(SelectedFirmwareFilePath)}";
            
            _logger?.LogInformation("Uploading firmware: {FileName} to S3", safeFileName);
            
            // Upload progress simulation (S3 SDK doesn't provide real progress for small files)
            UploadProgress = 20;
            UploadStatusText = "Uploading to cloud...";
            
            // Actually upload to S3
            var result = await _s3Service.UploadFirmwareAsync(SelectedFirmwareFilePath!, safeFileName);
            
            UploadProgress = 80;
            UploadStatusText = "Finalizing...";
            
            // Create firmware item from result
            var firmware = new FirmwareItem
            {
                Id = Guid.NewGuid().ToString(),
                Name = NewFirmwareName ?? "",
                Version = NewFirmwareVersion ?? "",
                Description = NewFirmwareDescription ?? "",
                VehicleType = SelectedVehicleType ?? "Copter",
                FilePath = result.Key,
                FileName = result.FileName,
                FileSize = result.Size,
                UploadedDate = DateTime.Now,
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
            
            _logger?.LogInformation("Firmware uploaded successfully: {FileName}", safeFileName);
            
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to upload firmware");
            UploadStatusText = $"\u274C Error: {ex.Message}";
        }
        finally
        {
            IsUploading = false;
            await Task.Delay(3000);
            UploadStatusText = string.Empty;
        }
    }
    
    /// <summary>
    /// Loads all firmwares from S3 cloud
    /// </summary>
    [RelayCommand]
    private async Task RefreshFirmwaresAsync()
    {
        await LoadFirmwaresAsync();
    }
    
    /// <summary>
    /// Downloads firmware from cloud
    /// </summary>
    [RelayCommand]
    private async Task DownloadFirmwareAsync(FirmwareItem? firmware)
    {
        if (firmware == null || _s3Service == null) return;
        
        try
        {
            _logger?.LogInformation("Downloading firmware: {FileName}", firmware.FileName);
            
            var localPath = await _s3Service.DownloadFirmwareAsync(firmware.FilePath);
            
            firmware.DownloadCount++;
            
            _logger?.LogInformation("Firmware downloaded to: {Path}", localPath);
            
            // TODO: Open file location or show notification
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to download firmware: {FileName}", firmware.FileName);
        }
    }
    
    /// <summary>
    /// Edits firmware metadata
    /// </summary>
    [RelayCommand]
    private async Task EditFirmwareAsync(FirmwareItem? firmware)
    {
        if (firmware == null) return;
        
        // TODO: Implement edit dialog
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Deletes firmware from S3 cloud
    /// </summary>
    [RelayCommand]
    private async Task DeleteFirmwareAsync(FirmwareItem? firmware)
    {
        if (firmware == null || _s3Service == null) return;
        
        try
        {
            _logger?.LogInformation("Deleting firmware: {FileName}", firmware.FileName);
            
            // Delete from S3
            var success = await _s3Service.DeleteFirmwareAsync(firmware.FilePath);
            
            if (success)
            {
                Firmwares.Remove(firmware);
                TotalFirmwareCount = Firmwares.Count;
                
                var totalBytes = Firmwares.Sum(f => f.FileSize);
                TotalStorageUsed = FormatFileSize(totalBytes);
                
                OnPropertyChanged(nameof(HasNoFirmwares));
                
                _logger?.LogInformation("Firmware deleted: {FileName}", firmware.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to delete firmware: {FileName}", firmware.FileName);
        }
    }
    
    #endregion
    
    #region Private Methods
    
    private async Task LoadFirmwaresAsync()
    {
        if (_s3Service == null)
        {
            // Design-time: just return
            return;
        }
        
        IsLoadingFirmwares = true;
        
        try
        {
            _logger?.LogInformation("Loading firmwares from S3...");
            
            // Get actual firmware list from S3
            var s3Firmwares = await _s3Service.ListFirmwareFilesAsync();
            
            Firmwares.Clear();
            
            foreach (var s3Firmware in s3Firmwares)
            {
                Firmwares.Add(new FirmwareItem
                {
                    Id = s3Firmware.Key,
                    Name = s3Firmware.DisplayName,
                    Version = ExtractVersionFromFileName(s3Firmware.FileName),
                    Description = "",
                    VehicleType = s3Firmware.VehicleType,
                    FilePath = s3Firmware.Key,
                    FileName = s3Firmware.FileName,
                    FileSize = s3Firmware.Size,
                    UploadedDate = s3Firmware.LastModified,
                    DownloadCount = 0
                });
            }
            
            TotalFirmwareCount = Firmwares.Count;
            var totalBytes = Firmwares.Sum(f => f.FileSize);
            TotalStorageUsed = FormatFileSize(totalBytes);
            
            OnPropertyChanged(nameof(HasNoFirmwares));
            
            _logger?.LogInformation("Loaded {Count} firmwares from S3", Firmwares.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load firmwares from S3");
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

