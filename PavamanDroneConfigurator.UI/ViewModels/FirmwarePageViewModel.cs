using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the Firmware Flashing page - Mission Planner compatible.
/// Displays all vehicle types with version info and supports beta/custom firmware.
/// </summary>
public partial class FirmwarePageViewModel : ViewModelBase
{
    private readonly IFirmwareService _firmwareService;
    private readonly IConnectionService? _connectionService;
    private readonly ILogger<FirmwarePageViewModel> _logger;
    private CancellationTokenSource? _operationCts;

    #region Mode Selection
    [ObservableProperty] private FirmwarePageMode _currentMode = FirmwarePageMode.FirmwareUpgrade;
    [ObservableProperty] private bool _isFirmwareUpgradeMode = true;
    [ObservableProperty] private bool _isBootloaderUpdateMode;
    [ObservableProperty] private FirmwareUpgradeMode _upgradeMode = FirmwareUpgradeMode.Automatic;
    [ObservableProperty] private bool _isAutomaticMode = true;
    [ObservableProperty] private bool _isManualMode;
    [ObservableProperty] private bool _showBetaFirmware;
    [ObservableProperty] private bool _showAllOptions;
    #endregion

    #region Firmware Source Selection
    public ObservableCollection<FirmwareSourceOption> FirmwareSources { get; } = new();
    [ObservableProperty] private FirmwareSourceOption? _selectedFirmwareSource;
    #endregion

    #region Vehicle Types
    public ObservableCollection<VehicleTypeItem> VehicleTypes { get; } = new();
    [ObservableProperty] private VehicleTypeItem? _selectedVehicleType;
    #endregion

    #region Firmware Selection
    public ObservableCollection<FirmwareVersionItem> AvailableFirmwareVersions { get; } = new();
    [ObservableProperty] private FirmwareVersionItem? _selectedFirmwareVersion;
    [ObservableProperty] private string _selectedFilePath = string.Empty;
    [ObservableProperty] private string _selectedFileName = string.Empty;
    #endregion

    #region Board Detection
    [ObservableProperty] private bool _isBoardDetected;
    [ObservableProperty] private string _detectedBoardName = "No board detected";
    [ObservableProperty] private string _detectedBoardPort = string.Empty;
    [ObservableProperty] private bool _isInBootloader;
    [ObservableProperty] private string _currentFirmwareVersion = string.Empty;
    #endregion

    #region Progress and Status
    [ObservableProperty] private bool _isOperationInProgress;
    [ObservableProperty] private bool _isLoadingManifest;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _statusMessage = "Select a vehicle type to install firmware";
    [ObservableProperty] private string _detailMessage = string.Empty;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isSuccess;
    #endregion

    #region Log Messages
    public ObservableCollection<string> LogMessages { get; } = new();
    #endregion

    #region Confirmation Dialog
    [ObservableProperty] private bool _showConfirmationDialog;
    [ObservableProperty] private string _confirmationMessage = string.Empty;
    [ObservableProperty] private VehicleTypeItem? _pendingVehicleType;
    #endregion
    
    #region Platform Selection Dialog
    [ObservableProperty] private bool _showPlatformSelectionDialog;
    [ObservableProperty] private string _platformSelectionMessage = string.Empty;
    public ObservableCollection<string> AvailablePlatformVariants { get; } = new();
    [ObservableProperty] private string? _selectedPlatformVariant;
    #endregion
    
    #region Reconnect Prompt Dialog
    [ObservableProperty] private bool _showReconnectPromptDialog;
    [ObservableProperty] private string _reconnectPromptMessage = string.Empty;
    #endregion

    public FirmwarePageViewModel(
        IFirmwareService firmwareService, 
        ILogger<FirmwarePageViewModel> logger,
        IConnectionService? connectionService = null)
    {
        _firmwareService = firmwareService;
        _logger = logger;
        _connectionService = connectionService;

        _firmwareService.ProgressChanged += OnProgressChanged;
        _firmwareService.BoardDetected += OnBoardDetected;
        _firmwareService.FlashCompleted += OnFlashCompleted;
        _firmwareService.LogMessage += OnLogMessage;

        LoadFirmwareSources();
        LoadVehicleTypes();
        _ = LoadFirmwareVersionsAsync();
    }

    private void LoadFirmwareSources()
    {
        FirmwareSources.Clear();
        FirmwareSources.Add(new FirmwareSourceOption { Source = FirmwareSource.InApp, DisplayName = "In-App (offline)" });
        FirmwareSources.Add(new FirmwareSourceOption { Source = FirmwareSource.WebLatest, DisplayName = "Web: Latest" });
        FirmwareSources.Add(new FirmwareSourceOption { Source = FirmwareSource.WebBeta, DisplayName = "Web: Beta" });
        SelectedFirmwareSource = FirmwareSources.First();
    }

    private void LoadVehicleTypes()
    {
        var types = CommonBoards.AvailableVehicleTypes.OrderBy(t => t.DisplayOrder);
        foreach (var type in types)
        {
            VehicleTypes.Add(new VehicleTypeItem
            {
                Id = type.Id,
                Name = type.Name,
                Description = type.Description,
                ImagePath = type.ImagePath,
                ArduPilotId = type.ArduPilotId,
                MavType = type.MavType,
                VersionText = "Loading..."
            });
        }
    }

    private async Task LoadFirmwareVersionsAsync()
    {
        var source = SelectedFirmwareSource?.Source ?? FirmwareSource.InApp;
        try
        {
            IsLoadingManifest = true;
            switch (source)
            {
                case FirmwareSource.InApp:
                    await LoadLocalVersionsAsync();
                    break;
                case FirmwareSource.WebLatest:
                    await LoadWebVersionsAsync(releaseType: null);
                    break;
                case FirmwareSource.WebBeta:
                    await LoadWebVersionsAsync(releaseType: "BETA");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load firmware versions");
            AddLog($"Warning: Could not load firmware versions - {ex.Message}");
            foreach (var vehicleType in VehicleTypes)
            {
                vehicleType.VersionText = "Unavailable";
            }
        }
        finally
        {
            IsLoadingManifest = false;
        }
    }

    private async Task LoadLocalVersionsAsync()
    {
        StatusMessage = "Looking for local firmware files...";
        AddLog($"Scanning {_firmwareService.LocalFirmwareDirectory} for firmware files...");

        foreach (var vehicleType in VehicleTypes)
        {
            var path = await _firmwareService.GetLocalFirmwarePathAsync(vehicleType.Id);
            vehicleType.VersionText = path != null ? "Local file" : "Add file in FirmwareLocal";
        }
    }

    private async Task LoadWebVersionsAsync(string? releaseType)
    {
        StatusMessage = "Loading firmware versions from ArduPilot...";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        foreach (var vehicleType in VehicleTypes)
        {
            try
            {
                var versions = await _firmwareService.GetAvailableFirmwareVersionsAsync(vehicleType.ArduPilotId, "fmuv2", releaseType, cts.Token);
                var latest = versions.FirstOrDefault(v => v.IsLatest) ?? versions.FirstOrDefault();
                vehicleType.VersionText = latest != null ? $"V{latest.Version} {latest.ReleaseType}" : "No version";
            }
            catch
            {
                vehicleType.VersionText = "Offline";
            }
        }
        AddLog("Firmware versions loaded");
    }

    partial void OnSelectedFirmwareSourceChanged(FirmwareSourceOption? value)
    {
        _ = LoadFirmwareVersionsAsync();
    }

    #region Mode Selection Commands
    partial void OnIsFirmwareUpgradeModeChanged(bool value)
    {
        if (value)
        {
            CurrentMode = FirmwarePageMode.FirmwareUpgrade;
            IsBootloaderUpdateMode = false;
            StatusMessage = "Select a vehicle type to install firmware";
        }
    }

    partial void OnIsBootloaderUpdateModeChanged(bool value)
    {
        if (value)
        {
            CurrentMode = FirmwarePageMode.BootloaderUpdate;
            IsFirmwareUpgradeMode = false;
            StatusMessage = "Click Update to update the bootloader";
        }
    }

    partial void OnIsAutomaticModeChanged(bool value)
    {
        if (value)
        {
            UpgradeMode = FirmwareUpgradeMode.Automatic;
            IsManualMode = false;
        }
    }

    partial void OnIsManualModeChanged(bool value)
    {
        if (value)
        {
            UpgradeMode = FirmwareUpgradeMode.Manual;
            IsAutomaticMode = false;
        }
    }

    [RelayCommand] private void SelectFirmwareUpgradeMode() => IsFirmwareUpgradeMode = true;
    [RelayCommand] private void SelectBootloaderUpdateMode() => IsBootloaderUpdateMode = true;
    [RelayCommand] private void SelectAutomaticMode() => IsAutomaticMode = true;
    [RelayCommand] private void SelectManualMode() => IsManualMode = true;
    [RelayCommand] private void ToggleBetaFirmware() { ShowBetaFirmware = !ShowBetaFirmware; AddLog(ShowBetaFirmware ? "Showing BETA firmwares" : "Showing OFFICIAL firmwares only"); }
    [RelayCommand] private void ToggleAllOptions() => ShowAllOptions = !ShowAllOptions;
    [RelayCommand] private Task RefreshFirmwareVersionsAsync() => LoadFirmwareVersionsAsync();
    #endregion

    #region Vehicle Type Selection

    /// <summary>
    /// Handles vehicle type selection - shows confirmation dialog like Mission Planner
    /// </summary>
    [RelayCommand]
    private async Task SelectVehicleTypeAsync(VehicleTypeItem? vehicleType)
    {
        if (vehicleType == null) return;
        
        SelectedVehicleType = vehicleType;
        AddLog($"Selected: {vehicleType.Description}");

        // In Automatic mode, show confirmation dialog before flashing
        if (IsAutomaticMode)
        {
            // Check if connected via MAVLink - warn user
            if (_connectionService?.IsConnected == true)
            {
                ConfirmationMessage = $"You are about to install {vehicleType.ArduPilotId} firmware ({vehicleType.Description}).\n\n" +
                                      "The current MAVLink connection will be closed.\n" +
                                      "Continue with firmware installation?";
            }
            else
            {
                ConfirmationMessage = $"You are about to install {vehicleType.ArduPilotId} firmware ({vehicleType.Description}).\n\n" +
                                      "Please ensure your flight controller is connected.\n" +
                                      "Continue with firmware installation?";
            }
            
            PendingVehicleType = vehicleType;
            ShowConfirmationDialog = true;
        }
        else
        {
            StatusMessage = $"Ready to install {vehicleType.Description} firmware";
        }
    }

    /// <summary>
    /// User confirmed the firmware installation
    /// </summary>
    [RelayCommand]
    private async Task ConfirmInstallationAsync()
    {
        ShowConfirmationDialog = false;
        
        if (PendingVehicleType != null)
        {
            await FlashSelectedFirmwareAsync();
        }
        
        PendingVehicleType = null;
    }

    /// <summary>
    /// User cancelled the confirmation dialog
    /// </summary>
    [RelayCommand]
    private void CancelConfirmation()
    {
        ShowConfirmationDialog = false;
        PendingVehicleType = null;
        StatusMessage = "Installation cancelled";
    }

    /// <summary>
    /// User confirmed platform selection
    /// </summary>
    [RelayCommand]
    private async Task ConfirmPlatformSelectionAsync()
    {
        ShowPlatformSelectionDialog = false;
        
        if (PendingVehicleType != null && !string.IsNullOrEmpty(SelectedPlatformVariant))
        {
            await FlashSelectedFirmwareWithPlatformAsync(SelectedPlatformVariant);
        }
        
        SelectedPlatformVariant = null;
        AvailablePlatformVariants.Clear();
    }

    /// <summary>
    /// User cancelled platform selection dialog
    /// </summary>
    [RelayCommand]
    private void CancelPlatformSelection()
    {
        ShowPlatformSelectionDialog = false;
        PendingVehicleType = null;
        SelectedPlatformVariant = null;
        AvailablePlatformVariants.Clear();
        StatusMessage = "Installation cancelled";
    }

    /// <summary>
    /// User acknowledged reconnect prompt
    /// </summary>
    [RelayCommand]
    private void AcknowledgeReconnectPrompt()
    {
        ShowReconnectPromptDialog = false;
    }

    #endregion

    #region Firmware Flashing Commands

    [RelayCommand]
    private async Task FlashSelectedFirmwareAsync()
    {
        var vehicleType = PendingVehicleType ?? SelectedVehicleType;
        
        if (vehicleType == null)
        {
            StatusMessage = "Please select a vehicle type first";
            return;
        }

        var source = SelectedFirmwareSource?.Source ?? FirmwareSource.InApp;
        
        // For web sources, detect board and check for platform variants
        if (source == FirmwareSource.WebLatest || source == FirmwareSource.WebBeta)
        {
            _operationCts = new CancellationTokenSource();
            
            try
            {
                IsOperationInProgress = true;
                IsError = false;
                IsSuccess = false;
                ProgressPercent = 0;
                
                // Close existing MAVLink connection first (like Mission Planner)
                if (_connectionService?.IsConnected == true)
                {
                    AddLog("Closing existing MAVLink connection...");
                    StatusMessage = "Closing connection...";
                    await _connectionService.DisconnectAsync();
                    await Task.Delay(500);
                }
                
                StatusMessage = "Scanning for board...";
                AddLog("Scanning for connected flight controllers...");
                
                // Quick board detection with short timeout
                using var detectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var board = await _firmwareService.DetectBoardAsync(detectCts.Token);
                
                if (board == null)
                {
                    // Wait for bootloader with user feedback
                    StatusMessage = "No running board found. Waiting for bootloader...";
                    AddLog("Put your board in bootloader mode (hold BOOT button while connecting)");
                    
                    board = await _firmwareService.WaitForBootloaderAsync(
                        TimeSpan.FromSeconds(30), _operationCts.Token);
                }
                
                if (board == null)
                {
                    IsOperationInProgress = false;
                    IsError = true;
                    StatusMessage = "No board detected. Please connect your flight controller.";
                    AddLog("Board detection timed out");
                    return;
                }
                
                var basePlatform = board.BoardId ?? "fmuv2";
                AddLog($"Board detected: {board.BoardName} ({basePlatform}) on {board.SerialPort}");
                StatusMessage = $"Detected: {board.BoardName}";
                
                // Update UI with detected board
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    IsBoardDetected = true;
                    DetectedBoardName = board.BoardName;
                    DetectedBoardPort = board.SerialPort;
                    IsInBootloader = board.IsInBootloader;
                });
                
                // Check for multiple platform variants
                StatusMessage = "Checking firmware variants...";
                AddLog($"Checking platform variants for {basePlatform}...");
                
                var releaseType = source == FirmwareSource.WebBeta ? "BETA" : null;
                var variants = await _firmwareService.GetAvailablePlatformVariantsAsync(
                    vehicleType.ArduPilotId, basePlatform, releaseType, _operationCts.Token);
                
                AddLog($"Found {variants.Count} firmware variant(s)");
                
                if (variants.Count > 1)
                {
                    // Show platform selection dialog on UI thread
                    IsOperationInProgress = false;
                    
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        AvailablePlatformVariants.Clear();
                        foreach (var variant in variants)
                        {
                            AvailablePlatformVariants.Add(variant);
                        }
                        SelectedPlatformVariant = variants.FirstOrDefault(); // Default to first (base variant)
                        PlatformSelectionMessage = $"Multiple firmware variants are available for {board.BoardName}.\n\n" +
                                                   "Select the variant to install:";
                        AddLog("Showing platform selection dialog...");
                        ShowPlatformSelectionDialog = true;
                    });
                    return;
                }
                else if (variants.Count == 1)
                {
                    // Single variant - proceed directly
                    AddLog($"Using platform: {variants[0]}");
                    await FlashSelectedFirmwareWithPlatformAsync(variants[0]);
                    return;
                }
                else
                {
                    // No specific variants found - use base platform
                    AddLog($"No specific variants found, using base platform: {basePlatform}");
                    await FlashSelectedFirmwareWithPlatformAsync(basePlatform);
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Operation cancelled";
                AddLog("Operation cancelled by user");
                IsOperationInProgress = false;
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during board detection");
                AddLog($"Error: {ex.Message}");
                // Continue with default behavior
                IsOperationInProgress = false;
            }
        }

        // Default path (InApp source or fallback)
        await FlashSelectedFirmwareWithPlatformAsync(null);
    }
    
    private async Task FlashSelectedFirmwareWithPlatformAsync(string? selectedPlatform)
    {
        var vehicleType = PendingVehicleType ?? SelectedVehicleType;
        
        if (vehicleType == null)
        {
            StatusMessage = "Please select a vehicle type first";
            return;
        }

        var source = SelectedFirmwareSource?.Source ?? FirmwareSource.InApp;

        try
        {
            IsOperationInProgress = true;
            IsError = false;
            IsSuccess = false;
            ProgressPercent = 0;
            _operationCts = new CancellationTokenSource();

            AddLog($"Starting firmware installation for {vehicleType.Description}...");
            if (!string.IsNullOrEmpty(selectedPlatform))
            {
                AddLog($"Selected platform: {selectedPlatform}");
            }

            // Close existing MAVLink connection first (like Mission Planner)
            if (_connectionService?.IsConnected == true)
            {
                AddLog("Closing existing MAVLink connection...");
                StatusMessage = "Closing connection...";
                await _connectionService.DisconnectAsync();
                await Task.Delay(500); // Allow port to release
            }

            StatusMessage = "Detecting board...";

            switch (source)
            {
                case FirmwareSource.InApp:
                    var localPath = await _firmwareService.GetLocalFirmwarePathAsync(vehicleType.ArduPilotId, _operationCts.Token);
                    if (string.IsNullOrEmpty(localPath))
                    {
                        IsError = true;
                        StatusMessage = $"No local firmware found. Add files to {_firmwareService.LocalFirmwareDirectory}";
                        AddLog(StatusMessage);
                        return;
                    }
                    await FlashFromFileAsync(localPath);
                    break;

                case FirmwareSource.WebLatest:
                    await FlashFromWebWithPlatformAsync(vehicleType, selectedPlatform, null);
                    break;

                case FirmwareSource.WebBeta:
                    await FlashFromWebWithPlatformAsync(vehicleType, selectedPlatform, "BETA");
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Installation cancelled";
            AddLog("Installation cancelled by user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firmware installation failed");
            IsError = true;
            StatusMessage = $"Error: {ex.Message}";
            AddLog($"Error: {ex.Message}");
        }
        finally
        {
            IsOperationInProgress = false;
            PendingVehicleType = null;
            _operationCts?.Dispose();
            _operationCts = null;
        }
    }

    private async Task FlashFromWebWithPlatformAsync(VehicleTypeItem vehicleType, string? selectedPlatform, string? releaseType)
    {
        if (_operationCts == null) return;

        var firmwareType = new FirmwareType
        {
            Id = vehicleType.Id,
            Name = vehicleType.Name,
            Description = vehicleType.Description,
            ArduPilotId = vehicleType.ArduPilotId
        };

        // Pass selected platform to firmware service
        var result = await _firmwareService.FlashFirmwareAsync(firmwareType, selectedPlatform, releaseType, _operationCts.Token);
        
        if (result.Success)
        {
            IsSuccess = true;
            StatusMessage = $"Firmware installed successfully! {result.FirmwareVersion}";
            AddLog($"Installation completed in {result.Duration.TotalSeconds:F1}s");
            
            // Show reconnect prompt like Mission Planner
            ShowReconnectPrompt();
        }
        else
        {
            IsError = true;
            StatusMessage = result.Message;
            AddLog($"Installation failed: {result.Message}");
        }
    }
    
    private void ShowReconnectPrompt()
    {
        ReconnectPromptMessage = "Firmware installation complete!\n\n" +
                                 "Please disconnect and reconnect your flight controller to use the new firmware.\n\n" +
                                 "The board will reboot automatically. Wait a few seconds, then reconnect.";
        ShowReconnectPromptDialog = true;
    }

    private Task FlashFromWebAsync(VehicleTypeItem vehicleType, string? releaseType)
    {
        return FlashFromWebWithPlatformAsync(vehicleType, null, releaseType);
    }

    private async Task FlashFromFileAsync(string path)
    {
        if (_operationCts == null) return;
        var result = await _firmwareService.FlashFirmwareFromFileAsync(path, _operationCts.Token);
        if (result.Success)
        {
            IsSuccess = true;
            StatusMessage = "Firmware installed successfully!";
            AddLog($"Flash completed in {result.Duration.TotalSeconds:F1}s");
        }
        else
        {
            IsError = true;
            StatusMessage = result.Message;
            AddLog($"Flash failed: {result.Message}");
        }
    }

    [RelayCommand] private Task BrowseFirmwareFileAsync() => Task.CompletedTask; // implemented in code-behind

    public async Task SetFirmwareFileAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return;
        SelectedFilePath = filePath;
        SelectedFileName = Path.GetFileName(filePath);
        var (isValid, message) = await _firmwareService.ValidateFirmwareFileAsync(filePath);
        if (isValid)
        {
            AddLog($"Selected firmware file: {SelectedFileName}");
            StatusMessage = $"Ready to flash: {SelectedFileName}";
        }
        else
        {
            IsError = true;
            StatusMessage = message;
            AddLog($"Invalid file: {message}");
        }
    }

    [RelayCommand]
    private async Task FlashCustomFirmwareAsync()
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
        {
            StatusMessage = "Please select a firmware file first";
            return;
        }

        try
        {
            IsOperationInProgress = true;
            IsError = false;
            IsSuccess = false;
            ProgressPercent = 0;
            _operationCts = new CancellationTokenSource();

            AddLog($"Starting custom firmware flash: {SelectedFileName}");
            StatusMessage = "Waiting for board in bootloader mode...";
            DetailMessage = "Connect board while holding boot button";

            var result = await _firmwareService.FlashFirmwareFromFileAsync(SelectedFilePath, _operationCts.Token);

            if (result.Success)
            {
                IsSuccess = true;
                StatusMessage = "Custom firmware installed successfully!";
                AddLog($"Flash completed in {result.Duration.TotalSeconds:F1}s");
            }
            else
            {
                IsError = true;
                StatusMessage = result.Message;
                AddLog($"Flash failed: {result.Message}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Custom firmware flash failed");
            IsError = true;
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsOperationInProgress = false;
            _operationCts?.Dispose();
            _operationCts = null;
        }
    }
    #endregion

    #region Bootloader Commands
    [RelayCommand]
    private async Task UpdateBootloaderAsync()
    {
        try
        {
            IsOperationInProgress = true;
            IsError = false;
            IsSuccess = false;
            ProgressPercent = 0;

            _operationCts = new CancellationTokenSource();

            AddLog("Starting bootloader update...");
            StatusMessage = "Updating bootloader...";

            var result = await _firmwareService.UpdateBootloaderAsync(_operationCts.Token);

            if (result.Success)
            {
                IsSuccess = true;
                StatusMessage = "Bootloader updated successfully!";
                AddLog("Bootloader update completed");
            }
            else
            {
                IsError = true;
                StatusMessage = result.Message;
                AddLog($"Bootloader update failed: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bootloader update failed");
            IsError = true;
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsOperationInProgress = false;
            _operationCts?.Dispose();
            _operationCts = null;
        }
    }

    [RelayCommand]
    private async Task ForceBootloaderAsync()
    {
        AddLog("Force bootloader - connect board now...");
        StatusMessage = "Waiting for board in bootloader mode...";

        try
        {
            var board = await _firmwareService.WaitForBootloaderAsync(TimeSpan.FromSeconds(30));
            if (board != null)
            {
                IsBoardDetected = true;
                DetectedBoardName = board.BoardName;
                DetectedBoardPort = board.SerialPort;
                StatusMessage = $"Bootloader detected: {board.BoardName}";
                AddLog($"Bootloader detected on {board.SerialPort}");
            }
            else
            {
                StatusMessage = "No bootloader detected - timeout";
                AddLog("Bootloader detection timeout");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            AddLog($"Error: {ex.Message}");
        }
    }
    #endregion

    #region Board Detection
    [RelayCommand]
    private async Task DetectBoardAsync()
    {
        try
        {
            StatusMessage = "Detecting board...";
            AddLog("Scanning for connected boards...");

            var board = await _firmwareService.DetectBoardAsync();

            if (board != null)
            {
                IsBoardDetected = true;
                DetectedBoardName = board.BoardName;
                DetectedBoardPort = board.SerialPort;
                IsInBootloader = board.IsInBootloader;
                CurrentFirmwareVersion = board.CurrentFirmware;

                StatusMessage = $"Detected: {board.BoardName} on {board.SerialPort}";
                AddLog($"Board: {board.BoardName} ({(board.IsInBootloader ? "Bootloader" : "Running")})");
            }
            else
            {
                IsBoardDetected = false;
                DetectedBoardName = "No board detected";
                DetectedBoardPort = string.Empty;
                StatusMessage = "No compatible board detected";
                AddLog("No board detected. Connect board and try again.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Board detection failed");
            StatusMessage = $"Detection failed: {ex.Message}";
        }
    }
    #endregion

    #region Utility Commands
    [RelayCommand]
    private void CancelOperation()
    {
        _operationCts?.Cancel();
        _firmwareService.CancelOperation();
        AddLog("Cancelling operation...");
        StatusMessage = "Cancelling...";
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogMessages.Clear();
    }
    #endregion

    #region Event Handlers
    private void OnProgressChanged(object? sender, FirmwareProgress e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ProgressPercent = e.ProgressPercent;
            StatusMessage = e.StatusMessage;
            DetailMessage = e.DetailMessage;
            IsError = e.IsError;
        });
    }

    private void OnBoardDetected(object? sender, DetectedBoard e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsBoardDetected = true;
            DetectedBoardName = e.BoardName;
            DetectedBoardPort = e.SerialPort;
            IsInBootloader = e.IsInBootloader;
            CurrentFirmwareVersion = e.CurrentFirmware;
            AddLog($"Board detected: {e.BoardName}");
        });
    }

    private void OnFlashCompleted(object? sender, FirmwareFlashResult e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (e.Success)
            {
                IsSuccess = true;
                ProgressPercent = 100;
                StatusMessage = "Done! - Reconnect to your flight controller";
                AddLog($"Firmware {e.FirmwareVersion} installed successfully in {e.Duration.TotalSeconds:F1}s");
            }
            else
            {
                IsError = true;
                StatusMessage = e.Message;
                AddLog($"Flash failed: {e.Message}");
            }
        });
    }

    private void OnLogMessage(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() => AddLog(message));
    }
    #endregion

    #region Helpers
    private void AddLog(string message)
    {
        var timestampedMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogMessages.Add(timestampedMessage);

        while (LogMessages.Count > 500)
        {
            LogMessages.RemoveAt(0);
        }
    }
    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _firmwareService.ProgressChanged -= OnProgressChanged;
            _firmwareService.BoardDetected -= OnBoardDetected;
            _firmwareService.FlashCompleted -= OnFlashCompleted;
            _firmwareService.LogMessage -= OnLogMessage;

            _operationCts?.Cancel();
            _operationCts?.Dispose();
        }

        base.Dispose(disposing);
    }
}

#region Supporting Types

public enum FirmwarePageMode
{
    FirmwareUpgrade,
    BootloaderUpdate
}

public enum FirmwareUpgradeMode
{
    Automatic,
    Manual
}

public partial class VehicleTypeItem : ObservableObject
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public string ArduPilotId { get; set; } = string.Empty;
    public int MavType { get; set; }

    [ObservableProperty]
    private string _versionText = string.Empty;

    /// <summary>
    /// Display name with version (like Mission Planner)
    /// </summary>
    public string DisplayName => $"{Name}\n{VersionText}";
}

public class FirmwareVersionItem
{
    public string Version { get; set; } = string.Empty;
    public string ReleaseType { get; set; } = string.Empty;
    public string BoardType { get; set; } = string.Empty;
    public string DownloadUrl { get; set; } = string.Empty;
    public bool IsLatest { get; set; }

    public string DisplayName => IsLatest ? $"{Version} (Latest)" : $"{Version} ({ReleaseType})";
}

public enum FirmwareSource
{
    InApp,
    WebLatest,
    WebBeta
}

public class FirmwareSourceOption
{
    public FirmwareSource Source { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

#endregion
