using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.UI.ViewModels.Admin;

/// <summary>
/// ViewModel for Firmware Management admin panel
/// </summary>
public partial class FirmwareManagementViewModel : ViewModelBase
{
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
    
    public bool CanUpload => !string.IsNullOrEmpty(_newFirmwareName) 
                             && !string.IsNullOrEmpty(_newFirmwareVersion) 
                             && !string.IsNullOrEmpty(_selectedFirmwareFilePath)
                             && !_isUploading;
    
    public bool HasNoFirmwares => !_isLoadingFirmwares && Firmwares.Count == 0;
    
    #endregion
    
    #region Constructor
    
    public FirmwareManagementViewModel()
    {
        // Load existing firmwares on initialization
        _ = LoadFirmwaresAsync();
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
            _selectedFirmwareFilePath = filePath;
            _selectedFirmwareFileName = Path.GetFileName(filePath);
            OnPropertyChanged(nameof(CanUpload));
        }
        return Task.CompletedTask;
    }
    
    #endregion
    
    #region Commands
    
    /// <summary>
    /// Uploads firmware to cloud storage
    /// </summary>
    [RelayCommand]
    private async Task UploadFirmwareAsync()
    {
        if (!CanUpload) return;
        
        try
        {
            _isUploading = true;
            _uploadProgress = 0;
            _uploadStatusText = "Preparing upload...";
            
            // Simulate upload progress (replace with actual API call)
            for (int i = 0; i <= 100; i += 10)
            {
                _uploadProgress = i;
                _uploadStatusText = $"Uploading... {i}%";
                await Task.Delay(200);
            }
            
            // Create new firmware item
            var firmware = new FirmwareItem
            {
                Id = Guid.NewGuid().ToString(),
                Name = _newFirmwareName ?? "",
                Version = _newFirmwareVersion ?? "",
                Description = _newFirmwareDescription ?? "",
                VehicleType = _selectedVehicleType ?? "Copter",
                FilePath = _selectedFirmwareFilePath ?? "",
                FileName = _selectedFirmwareFileName ?? "",
                FileSize = new FileInfo(_selectedFirmwareFilePath!).Length,
                UploadedDate = DateTime.Now,
                DownloadCount = 0
            };
            
            // TODO: Call API to upload to cloud
            // await _firmwareService.UploadFirmwareAsync(firmware);
            
            // Add to list
            Firmwares.Add(firmware);
            _totalFirmwareCount = Firmwares.Count;
            
            // Calculate storage
            var totalBytes = Firmwares.Sum(f => f.FileSize);
            _totalStorageUsed = FormatFileSize(totalBytes);
            
            // Clear form
            ClearUploadForm();
            
            _uploadStatusText = "? Upload completed successfully!";
            await Task.Delay(2000);
        }
        catch (Exception ex)
        {
            _uploadStatusText = $"? Error: {ex.Message}";
        }
        finally
        {
            _isUploading = false;
            await Task.Delay(3000);
            _uploadStatusText = string.Empty;
        }
    }
    
    /// <summary>
    /// Loads all firmwares from cloud
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
    private async Task DownloadFirmwareAsync(FirmwareItem firmware)
    {
        if (firmware == null) return;
        
        // TODO: Implement download logic
        firmware.DownloadCount++;
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Edits firmware metadata
    /// </summary>
    [RelayCommand]
    private async Task EditFirmwareAsync(FirmwareItem firmware)
    {
        if (firmware == null) return;
        
        // TODO: Implement edit dialog
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// Deletes firmware from cloud
    /// </summary>
    [RelayCommand]
    private async Task DeleteFirmwareAsync(FirmwareItem firmware)
    {
        if (firmware == null) return;
        
        // TODO: Show confirmation dialog
        // if (confirmed)
        {
            Firmwares.Remove(firmware);
            _totalFirmwareCount = Firmwares.Count;
            
            var totalBytes = Firmwares.Sum(f => f.FileSize);
            _totalStorageUsed = FormatFileSize(totalBytes);
        }
        
        await Task.CompletedTask;
    }
    
    #endregion
    
    #region Private Methods
    
    private async Task LoadFirmwaresAsync()
    {
        _isLoadingFirmwares = true;
        
        try
        {
            // Simulate API call
            await Task.Delay(1000);
            
            // TODO: Replace with actual API call
            // var firmwares = await _firmwareService.GetAllFirmwaresAsync();
            
            // For now, add dummy data if empty
            if (Firmwares.Count == 0)
            {
                Firmwares.Add(new FirmwareItem
                {
                    Id = "1",
                    Name = "Pavaman AgriCopter Pro",
                    Version = "4.5.2",
                    Description = "Optimized for agricultural spraying",
                    VehicleType = "Copter",
                    FileName = "pavaman-agri-copter-4.5.2.apj",
                    FileSize = 2048000,
                    UploadedDate = DateTime.Now.AddDays(-5),
                    DownloadCount = 42
                });
                
                Firmwares.Add(new FirmwareItem
                {
                    Id = "2",
                    Name = "Pavaman SurveyPlane",
                    Version = "4.5.1",
                    Description = "Survey and mapping configuration",
                    VehicleType = "Plane",
                    FileName = "pavaman-survey-plane-4.5.1.apj",
                    FileSize = 1856000,
                    UploadedDate = DateTime.Now.AddDays(-12),
                    DownloadCount = 28
                });
            }
            
            _totalFirmwareCount = Firmwares.Count;
            var totalBytes = Firmwares.Sum(f => f.FileSize);
            _totalStorageUsed = FormatFileSize(totalBytes);
            
            OnPropertyChanged(nameof(HasNoFirmwares));
        }
        finally
        {
            _isLoadingFirmwares = false;
        }
    }
    
    private void ClearUploadForm()
    {
        _newFirmwareName = string.Empty;
        _newFirmwareVersion = string.Empty;
        _newFirmwareDescription = string.Empty;
        _selectedFirmwareFilePath = string.Empty;
        _selectedFirmwareFileName = string.Empty;
        _selectedVehicleType = "Copter";
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

