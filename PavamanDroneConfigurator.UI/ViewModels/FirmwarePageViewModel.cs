using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the Firmware Flashing page - Mission Planner compatible.
/// </summary>
public partial class FirmwarePageViewModel : ViewModelBase
{
    private readonly IFirmwareService _firmwareService;
    private readonly IConnectionService? _connectionService;
    private readonly FirmwareApiService? _firmwareApiService;
    private readonly ILogger<FirmwarePageViewModel> _logger;
    private readonly ViewModels.Auth.AuthSessionViewModel? _authSession;
    private readonly PavamanDroneConfigurator.Core.Interfaces.ITokenStorage? _tokenStorage;
    private CancellationTokenSource? _operationCts;

    #region Mode Selection
    [ObservableProperty] private FirmwarePageMode _currentMode = FirmwarePageMode.FirmwareUpgrade;
    [ObservableProperty] private bool _isFirmwareUpgradeMode = true;
    [ObservableProperty] private bool _isBootloaderUpdateMode;
    [ObservableProperty] private FirmwareUpgradeMode _upgradeMode = FirmwareUpgradeMode.Automatic;
    [ObservableProperty] private bool _isAutomaticMode = false;
    [ObservableProperty] private bool _isManualMode = false;
    [ObservableProperty] private bool _isAdminMode = false;
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
    
    #region Bootloader Mode Dialog
    [ObservableProperty] private bool _showBootloaderModeDialog;
    [ObservableProperty] private string _bootloaderModeMessage = string.Empty;
    private TaskCompletionSource<bool>? _bootloaderDialogTcs;
    #endregion
    
    #region Connection Status
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionStatusText = "Disconnected";
    [ObservableProperty] private string _connectionStatusColor = "#EF4444";
    #endregion
    
    #region Vehicle Groups
    public ObservableCollection<VehicleTypeGroup> VehicleGroups { get; } = new();
    #endregion
    
    #region Flashing Progress Dialog
    [ObservableProperty] private bool _showFlashingProgressDialog;
    [ObservableProperty] private FlashingStep _currentFlashingStep = FlashingStep.Idle;
    [ObservableProperty] private string _flashingStepText = string.Empty;
    [ObservableProperty] private string _estimatedTimeRemaining = string.Empty;
    [ObservableProperty] private bool _canCancelFlashing = true;
    #endregion
    
    #region Failure Dialog
    [ObservableProperty] private bool _showFailureDialog;
    [ObservableProperty] private string _failureMessage = string.Empty;
    [ObservableProperty] private string _failureSuggestion = string.Empty;
    #endregion
    
    #region Log Panel
    [ObservableProperty] private bool _isLogPanelExpanded;
    [ObservableProperty] private LogFilterLevel _selectedLogFilter = LogFilterLevel.Info;
    [ObservableProperty] private bool _autoScrollLogs = true;
    #endregion
    
    #region Admin Upload
    [ObservableProperty] private string _adminUploadFilePath = string.Empty;
    [ObservableProperty] private string _adminUploadFileName = string.Empty;
    [ObservableProperty] private string _adminFirmwareName = string.Empty;
    [ObservableProperty] private string _adminFirmwareVersion = string.Empty;
    [ObservableProperty] private string _adminFirmwareDescription = string.Empty;
    [ObservableProperty] private bool _isUploading;
    [ObservableProperty] private double _uploadProgressPercent;
    
    /// <summary>
    /// List of S3 firmwares for admin management
    /// </summary>
    public ObservableCollection<S3FirmwareMetadata> S3Firmwares { get; } = new();
    [ObservableProperty] private S3FirmwareMetadata? _selectedS3Firmware;
    
    /// <summary>
    /// List of custom firmwares from S3 (for In-App source display)
    /// </summary>
    public ObservableCollection<CustomFirmwareItem> CustomFirmwares { get; } = new();
    [ObservableProperty] private CustomFirmwareItem? _selectedCustomFirmware;
    [ObservableProperty] private bool _isInAppSource;
    [ObservableProperty] private bool _hasNoCustomFirmwares;
    #endregion

    public FirmwarePageViewModel(
        IFirmwareService firmwareService, 
        ILogger<FirmwarePageViewModel> logger,
        IConnectionService? connectionService = null,
        FirmwareApiService? firmwareApiService = null,
        ViewModels.Auth.AuthSessionViewModel? authSession = null,
        PavamanDroneConfigurator.Core.Interfaces.ITokenStorage? tokenStorage = null)
    {
        _firmwareService = firmwareService;
        _logger = logger;
        _connectionService = connectionService;
        _firmwareApiService = firmwareApiService;
        _authSession = authSession;
        _tokenStorage = tokenStorage;

        _firmwareService.ProgressChanged += OnProgressChanged;
        _firmwareService.BoardDetected += OnBoardDetected;
        _firmwareService.FlashCompleted += OnFlashCompleted;
        _firmwareService.LogMessage += OnLogMessage;

        LoadFirmwareSources();
        LoadVehicleTypes();
        UpdateConnectionStatus();
        _ = LoadFirmwareVersionsAsync();
    }

    private void LoadFirmwareSources()
    {
        FirmwareSources.Clear();
        FirmwareSources.Add(new FirmwareSourceOption { Source = FirmwareSource.InApp, DisplayName = "KFT Firmware (Assigned)" });
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
                VersionText = "Loading...",
                IsLoading = true
            });
        }
        
        // Build grouped vehicle types
        LoadVehicleGroups();
    }
    
    private void LoadVehicleGroups()
    {
        VehicleGroups.Clear();
        
        // Group by category
        var groupedItems = VehicleTypes
            .GroupBy(v => v.Category)
            .OrderBy(g => g.Key);
        
        foreach (var group in groupedItems)
        {
            var vehicleGroup = new VehicleTypeGroup
            {
                Name = group.Key switch
                {
                    VehicleCategory.Ground => "\U0001F697 Ground",        // Car emoji
                    VehicleCategory.FixedWing => "\u2708 Fixed Wing",     // Airplane
                    VehicleCategory.Multirotor => "\U0001F681 Multirotor", // Helicopter
                    VehicleCategory.Specialized => "\u2699 Specialized",   // Gear
                    _ => "Other"
                },
                Category = group.Key,
                IsExpanded = true
            };
            
            foreach (var item in group)
            {
                vehicleGroup.Items.Add(item);
            }
            
            VehicleGroups.Add(vehicleGroup);
        }
    }
    
    private void UpdateConnectionStatus()
    {
        if (_connectionService?.IsConnected == true)
        {
            IsConnected = true;
            ConnectionStatusText = "Connected";
            ConnectionStatusColor = "#22C55E"; // Green
        }
        else
        {
            IsConnected = false;
            ConnectionStatusText = "Disconnected";
            ConnectionStatusColor = "#EF4444"; // Red
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
        StatusMessage = "Loading firmwares from cloud storage...";
        AddLog("Fetching firmware list from S3...");

        try
        {
            if (_firmwareApiService != null)
            {
                // If authenticated, fetch only user-assigned firmwares; fall back to all firmwares
                var authState = _authSession?.CurrentState;
                bool useUserEndpoint = authState?.IsAuthenticated == true && _tokenStorage != null;
                
                List<CustomFirmwareItem> items = new();

                if (useUserEndpoint)
                {
                    try
                    {
                        var accessToken = await _tokenStorage!.GetAccessTokenAsync();
                        if (!string.IsNullOrEmpty(accessToken))
                        {
                            var userFirmwares = await _firmwareApiService.GetMyFirmwaresAsync(accessToken);
                            foreach (var fw in userFirmwares)
                            {
                                items.Add(new CustomFirmwareItem
                                {
                                    Key = fw.S3Key,
                                    FileName = fw.FileName,
                                    DisplayName = fw.DisplayName ?? fw.FileName,
                                    FirmwareName = fw.FirmwareName,
                                    FirmwareVersion = fw.FirmwareVersion,
                                    FirmwareDescription = fw.Description,
                                    VehicleType = fw.VehicleType,
                                    Size = fw.FileSize,
                                    SizeDisplay = fw.FileSizeDisplay,
                                    LastModified = fw.AssignedAt,
                                    DownloadUrl = fw.DownloadUrl ?? string.Empty,
                                    IsSelected = false
                                });
                            }
                            AddLog($"Loaded {items.Count} assigned firmware(s) for your account");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fetch user-assigned firmwares, falling back to all firmwares");
                        // Fall through to global fetch
                        items.Clear();
                    }
                }

                // If no user firmwares loaded, show empty state
                if (items.Count == 0)
                {
                    AddLog("No firmwares assigned to your account. Contact admin to get firmware assigned.");
                    HasNoCustomFirmwares = true;
                    foreach (var vehicleType in VehicleTypes)
                    {
                        vehicleType.VersionText = "No firmware assigned";
                        vehicleType.Availability = FirmwareAvailability.NotSupported;
                    }
                    StatusMessage = "No firmware assigned to your account. Please contact admin.";
                    return;
                }

                // Clear and populate CustomFirmwares collection
                CustomFirmwares.Clear();

                if (items.Count > 0)
                {
                    AddLog($"Found {items.Count} firmware(s)");
                    StatusMessage = $"Loaded {items.Count} firmware(s) from S3";

                    foreach (var customItem in items)
                    {
                        CustomFirmwares.Add(customItem);

                        var logInfo = !string.IsNullOrWhiteSpace(customItem.FirmwareName)
                            ? $"{customItem.FirmwareName} v{customItem.FirmwareVersion}"
                            : customItem.FileName;
                        AddLog($"  {customItem.VehicleType}: {logInfo} ({customItem.SizeDisplay})");

                        if (!string.IsNullOrWhiteSpace(customItem.FirmwareDescription))
                            AddLog($"    Description: {customItem.FirmwareDescription}");
                    }

                    HasNoCustomFirmwares = false;

                    // Match to vehicle types for legacy support
                    foreach (var vehicleType in VehicleTypes)
                    {
                        var match = items.FirstOrDefault(f =>
                            f.VehicleType.Equals(vehicleType.ArduPilotId, StringComparison.OrdinalIgnoreCase) ||
                            (vehicleType.ArduPilotId == "Copter" && f.VehicleType == "Copter") ||
                            (vehicleType.ArduPilotId == "Plane" && f.VehicleType == "Plane") ||
                            (vehicleType.ArduPilotId == "Rover" && f.VehicleType == "Rover"));

                        if (match != null)
                        {
                            var displayText = "S3: ";
                            if (!string.IsNullOrWhiteSpace(match.FirmwareName))
                            {
                                displayText += match.FirmwareName;
                                if (!string.IsNullOrWhiteSpace(match.FirmwareVersion))
                                    displayText += $" v{match.FirmwareVersion}";
                            }
                            else
                            {
                                displayText += match.DisplayName;
                            }
                            displayText += $" ({match.SizeDisplay})";
                            vehicleType.VersionText = displayText;
                            vehicleType.Availability = FirmwareAvailability.Available;
                        }
                        else
                        {
                            vehicleType.VersionText = "Not in S3";
                            vehicleType.Availability = FirmwareAvailability.NotSupported;
                        }
                    }
                }
                else
                {
                    AddLog("No firmwares found. Upload firmwares via the Admin Panel.");
                    HasNoCustomFirmwares = true;
                    foreach (var vehicleType in VehicleTypes)
                    {
                        vehicleType.VersionText = "No firmwares available";
                        vehicleType.Availability = FirmwareAvailability.NotSupported;
                    }
                    StatusMessage = "No firmwares found. Admin can upload and assign firmwares.";
                }
            }
            else
            {
                // Fallback to local directory scan
                AddLog($"FirmwareApiService not available. Scanning {_firmwareService.LocalFirmwareDirectory}...");
                StatusMessage = "Looking for local firmware files...";
                
                foreach (var vehicleType in VehicleTypes)
                {
                    var path = await _firmwareService.GetLocalFirmwarePathAsync(vehicleType.Id);
                    if (path != null)
                    {
                        vehicleType.VersionText = "Local file";
                        vehicleType.Availability = FirmwareAvailability.LocalOnly;
                    }
                    else
                    {
                        vehicleType.VersionText = "Add file in FirmwareLocal";
                        vehicleType.Availability = FirmwareAvailability.NotSupported;
                    }
                }
                
                StatusMessage = "Using local firmware directory";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load firmwares from S3");
            AddLog($"Error loading from S3: {ex.Message}");
            AddLog("Falling back to local directory scan...");
            
            // Fallback to local directory
            foreach (var vehicleType in VehicleTypes)
            {
                var path = await _firmwareService.GetLocalFirmwarePathAsync(vehicleType.Id);
                vehicleType.VersionText = path != null ? "Local file" : "Unavailable";
                vehicleType.Availability = path != null ? FirmwareAvailability.LocalOnly : FirmwareAvailability.NotSupported;
            }
            
            StatusMessage = "Using local firmware directory (S3 unavailable)";
        }
        finally
        {
            foreach (var vehicleType in VehicleTypes)
            {
                vehicleType.IsLoading = false;
            }
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
    
    partial void OnSelectedFirmwareSourceChanged(FirmwareSourceOption? value)
    {
        if (value != null)
        {
            IsInAppSource = value.Source == FirmwareSource.InApp;
            _ = LoadFirmwareVersionsAsync();
        }
    }
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
    /// Handles custom firmware selection (for In-App source)
    /// </summary>
    [RelayCommand]
    private async Task SelectCustomFirmwareAsync(CustomFirmwareItem? firmware)
    {
        if (firmware == null) return;
        
        // Deselect all custom firmwares
        foreach (var fw in CustomFirmwares)
        {
            fw.IsSelected = false;
        }
        
        // Select the clicked firmware
        firmware.IsSelected = true;
        SelectedCustomFirmware = firmware;
        
        AddLog($"Selected custom firmware: {firmware.DisplayName} v{firmware.FirmwareVersion}");
        StatusMessage = $"Ready to install {firmware.DisplayName} v{firmware.FirmwareVersion}";
        
        // Show confirmation dialog
        if (_connectionService?.IsConnected == true)
        {
            ConfirmationMessage = $"You are about to install custom firmware:\n\n" +
                                  $"{firmware.DisplayName} v{firmware.FirmwareVersion}\n" +
                                  $"Type: {firmware.VehicleType}\n" +
                                  $"Size: {firmware.SizeDisplay}\n\n" +
                                  "The current MAVLink connection will be closed.\n" +
                                  "Continue with firmware installation?";
        }
        else
        {
            ConfirmationMessage = $"You are about to install custom firmware:\n\n" +
                                  $"{firmware.DisplayName} v{firmware.FirmwareVersion}\n" +
                                  $"Type: {firmware.VehicleType}\n" +
                                  $"Size: {firmware.SizeDisplay}\n\n" +
                                  "Please ensure your flight controller is connected.\n" +
                                  "Continue with firmware installation?";
        }
        
        ShowConfirmationDialog = true;
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
        
        // Only clear PendingVehicleType if we're not showing the platform selection dialog
        // If platform selection dialog is shown, we need to keep PendingVehicleType for
        // the subsequent ConfirmPlatformSelectionAsync call
        if (!ShowPlatformSelectionDialog)
        {
            PendingVehicleType = null;
        }
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
    
    /// <summary>
    /// Shows a blocking dialog prompting the user to put the board in bootloader mode.
    /// This is equivalent to Mission Planner's "Please unplug the board and plug back in" dialog.
    /// </summary>
    private async Task<bool> ShowBootloaderModePromptAsync(string message, CancellationToken ct)
    {
        _bootloaderDialogTcs = new TaskCompletionSource<bool>();
        
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            BootloaderModeMessage = message;
            ShowBootloaderModeDialog = true;
        });
        
        // Wait for user response or cancellation
        using var registration = ct.Register(() => 
        {
            _bootloaderDialogTcs?.TrySetResult(false);
        });
        
        return await _bootloaderDialogTcs.Task;
    }
    
    /// <summary>
    /// User confirmed bootloader mode dialog - proceed with operation
    /// </summary>
    [RelayCommand]
    private void ConfirmBootloaderMode()
    {
        ShowBootloaderModeDialog = false;
        _bootloaderDialogTcs?.TrySetResult(true);
    }
    
    /// <summary>
    /// User cancelled bootloader mode dialog
    /// </summary>
    [RelayCommand]
    private void CancelBootloaderMode()
    {
        ShowBootloaderModeDialog = false;
        _bootloaderDialogTcs?.TrySetResult(false);
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
                    // CRITICAL: Show blocking dialog like Mission Planner does
                    // Mission Planner shows "Please unplug the board and plug back in" dialog
                    AddLog("No running board found. Showing bootloader mode prompt...");
                    
                    var userConfirmed = await ShowBootloaderModePromptAsync(
                        "No board detected.\n\n" +
                        "Please unplug the board and plug back in while holding the BOOT button.\n\n" +
                        "Click OK when ready, or Cancel to abort.",
                        _operationCts.Token);
                    
                    if (!userConfirmed)
                    {
                        IsOperationInProgress = false;
                        StatusMessage = "Installation cancelled";
                        AddLog("User cancelled bootloader mode prompt");
                        return;
                    }
                    
                    // Wait for bootloader with user feedback
                    StatusMessage = "Waiting for bootloader...";
                    DetailMessage = "Hold BOOT button while connecting";
                    AddLog("Waiting for board in bootloader mode...");
                    
                    board = await _firmwareService.WaitForBootloaderAsync(
                        TimeSpan.FromSeconds(30), _operationCts.Token);
                }
                
                if (board == null)
                {
                    IsOperationInProgress = false;
                    IsError = true;
                    StatusMessage = "No board detected. Please connect your flight controller.";
                    DetailMessage = "Try holding the BOOT button while connecting";
                    AddLog("Board detection timed out");
                    return;
                }
                
                // CRITICAL: Use the bootloader-reported board ID (authoritative source)
                // Never default to "fmuv2" - this prevents wrong firmware being flashed
                string basePlatform;
                if (board.BoardIdNumeric > 0)
                {
                    basePlatform = BoardCompatibility.GetPlatformName(board.BoardIdNumeric);
                    AddLog($"Using bootloader-reported board_type {board.BoardIdNumeric} -> platform: {basePlatform}");
                }
                else if (!string.IsNullOrEmpty(board.BoardId))
                {
                    basePlatform = board.BoardId;
                    AddLog($"Using USB-detected platform: {basePlatform}");
                }
                else
                {
                    IsOperationInProgress = false;
                    IsError = true;
                    StatusMessage = "Could not determine board type.";
                    AddLog("ERROR: Could not determine board type - neither bootloader nor USB detection returned valid platform");
                    return;
                }
                
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
            //PendingVehicleType = null;
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

    // BrowseFirmwareFile is implemented in code-behind (FirmwarePage.axaml.cs) via Click handler
    // No command needed here

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

            // Step 1: Close existing MAVLink connection first (like Mission Planner)
            if (_connectionService?.IsConnected == true)
            {
                AddLog("Closing existing MAVLink connection...");
                StatusMessage = "Closing connection...";
                await _connectionService.DisconnectAsync();
                await Task.Delay(500);
            }

            // Step 2: Detect board with quick timeout
            StatusMessage = "Scanning for board...";
            AddLog("Scanning for connected flight controllers...");

            using var detectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var board = await _firmwareService.DetectBoardAsync(detectCts.Token);

            // Step 3: If no board detected OR board is not in bootloader, try to enter bootloader
            if (board == null || !board.IsInBootloader)
            {
                if (board != null)
                {
                    AddLog($"Board detected but not in bootloader: {board.BoardName}");
                }
                else
                {
                    AddLog("No running board found. Attempting to reboot to bootloader...");
                }
                
                // CRITICAL FIX: Send reboot command before showing dialog
                // This matches the behavior of online firmware flashing
                StatusMessage = "Rebooting to bootloader...";
                AddLog("Sending reboot-to-bootloader command...");
                
                var rebootSuccess = await _firmwareService.AttemptRebootToBootloaderAsync(_operationCts.Token);
                
                if (rebootSuccess)
                {
                    AddLog("Reboot command sent. Waiting for USB re-enumeration...");
                    StatusMessage = "Waiting for USB re-enumeration...";
                    await Task.Delay(2500, _operationCts.Token); // USB_REENUMERATION_DELAY_MS
                }
                else
                {
                    // Reboot command failed - board may not be connected or not responding
                    // Show user prompt to manually enter bootloader
                    AddLog("Automatic reboot failed. Showing manual bootloader mode prompt...");
                    
                    var userConfirmed = await ShowBootloaderModePromptAsync(
                        "Could not automatically reboot to bootloader.\n\n" +
                        "Please unplug the board and plug back in while holding the BOOT button.\n\n" +
                        "Click OK when ready, or Cancel to abort.",
                        _operationCts.Token);

                    if (!userConfirmed)
                    {
                        IsOperationInProgress = false;
                        StatusMessage = "Installation cancelled";
                        AddLog("User cancelled bootloader mode prompt");
                        return;
                    }
                }

                // Wait for bootloader with user feedback
                StatusMessage = "Waiting for bootloader...";
                DetailMessage = "Looking for board in bootloader mode...";
                AddLog("Waiting for board in bootloader mode...");

                board = await _firmwareService.WaitForBootloaderAsync(
                    TimeSpan.FromSeconds(30), _operationCts.Token);
            }

            if (board == null)
            {
                IsOperationInProgress = false;
                IsError = true;
                StatusMessage = "No board detected in bootloader mode.";
                DetailMessage = "Try holding the BOOT button while connecting";
                AddLog("Board detection timed out");
                return;
            }

            // Step 4: Update UI with detected board info
            AddLog($"Board detected in bootloader: {board.BoardName} (board_type={board.BoardIdNumeric}) on {board.SerialPort}");
            StatusMessage = $"Detected: {board.BoardName}";

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsBoardDetected = true;
                DetectedBoardName = board.BoardName;
                DetectedBoardPort = board.SerialPort;
                IsInBootloader = board.IsInBootloader;
            });

            // Step 5: Flash custom firmware
            StatusMessage = "Flashing custom firmware...";
            DetailMessage = "Please wait...";

            var result = await _firmwareService.FlashFirmwareFromFileAsync(SelectedFilePath, _operationCts.Token);

            if (result.Success)
            {
                IsSuccess = true;
                StatusMessage = "Custom firmware installed successfully!";
                AddLog($"Flash completed in {result.Duration.TotalSeconds:F1}s");

                // Step 6: Show reconnect prompt like Mission Planner
                ShowReconnectPrompt();
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
            AddLog("Operation cancelled by user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Custom firmware flash failed");
            IsError = true;
            StatusMessage = $"Error: {ex.Message}";
            AddLog($"Error: {ex.Message}");
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
        ShowFlashingProgressDialog = false;
    }

    [RelayCommand]
    private void ClearLog()
    {
        LogMessages.Clear();
    }
    
    [RelayCommand]
    private void CloseFailureDialog()
    {
        ShowFailureDialog = false;
        FailureMessage = string.Empty;
        FailureSuggestion = string.Empty;
    }
    
    [RelayCommand]
    private async Task RetryFlashingAsync()
    {
        ShowFailureDialog = false;
        
        // Retry with pending vehicle type if available
        if (PendingVehicleType != null)
        {
            await FlashSelectedFirmwareAsync();
        }
    }
    
    [RelayCommand]
    private void ToggleLogPanel()
    {
        IsLogPanelExpanded = !IsLogPanelExpanded;
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
            
            // Map firmware state to flashing step
            CurrentFlashingStep = e.State switch
            {
                FirmwareFlashState.DownloadingFirmware => FlashingStep.Downloading,
                FirmwareFlashState.ErasingFlash => FlashingStep.Verifying,
                FirmwareFlashState.Programming => FlashingStep.Uploading,
                FirmwareFlashState.Verifying => FlashingStep.Verifying,
                FirmwareFlashState.Rebooting => FlashingStep.Finalizing,
                FirmwareFlashState.Completed => FlashingStep.Complete,
                _ => CurrentFlashingStep
            };
            
            FlashingStepText = CurrentFlashingStep switch
            {
                FlashingStep.Downloading => "Downloading firmware...",
                FlashingStep.Verifying => "Verifying firmware...",
                FlashingStep.Uploading => "Uploading to board...",
                FlashingStep.Finalizing => "Finalizing...",
                FlashingStep.Complete => "Complete!",
                _ => e.StatusMessage
            };
            
            // Disable cancel once flashing starts
            CanCancelFlashing = e.State == FirmwareFlashState.DownloadingFirmware || 
                               e.State == FirmwareFlashState.DetectingBoard ||
                               e.State == FirmwareFlashState.WaitingForBootloader;
            
            // Update estimated time if available
            if (e.EstimatedTimeRemaining.HasValue)
            {
                EstimatedTimeRemaining = $"~{e.EstimatedTimeRemaining.Value.TotalSeconds:F0}s remaining";
            }
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

/// <summary>
/// Flashing progress step indicators
/// </summary>
public enum FlashingStep
{
    Idle,
    Downloading,
    Verifying,
    Uploading,
    Finalizing,
    Complete
}

/// <summary>
/// Log filter level for log panel
/// </summary>
public enum LogFilterLevel
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Vehicle category for grouping firmware tiles
/// </summary>
public enum VehicleCategory
{
    Ground,      // Rover
    FixedWing,   // Plane
    Multirotor,  // Copter variants
    Specialized  // Sub, AntennaTracker, etc.
}

/// <summary>
/// Firmware availability status for badges
/// </summary>
public enum FirmwareAvailability
{
    Available,    // Green - Available online
    LocalOnly,    // Yellow - Local file only
    NotSupported  // Red - Not supported for this board
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
    
    [ObservableProperty]
    private bool _isSelected;
    
    [ObservableProperty]
    private bool _isLoading;
    
    [ObservableProperty]
    private FirmwareAvailability _availability = FirmwareAvailability.Available;
    
    /// <summary>
    /// Vehicle category for grouping
    /// </summary>
    public VehicleCategory Category => ArduPilotId switch
    {
        "Rover" => VehicleCategory.Ground,
        "Plane" => VehicleCategory.FixedWing,
        "Copter" or "Copter-heli" => VehicleCategory.Multirotor,
        _ => VehicleCategory.Specialized
    };
    
    /// <summary>
    /// Category display name with icon
    /// </summary>
    public string CategoryDisplayName => Category switch
    {
        VehicleCategory.Ground => "\U0001F697 Ground",        // Car emoji
        VehicleCategory.FixedWing => "\u2708 Fixed Wing",     // Airplane
        VehicleCategory.Multirotor => "\U0001F681 Multirotor", // Helicopter
        VehicleCategory.Specialized => "\u2699 Specialized",   // Gear
        _ => "Other"
    };

    /// <summary>
    /// Display name with version (like Mission Planner)
    /// </summary>
    public string DisplayName => $"{Name}\n{VersionText}";
    
    /// <summary>
    /// Availability badge color (for binding)
    /// </summary>
    public string AvailabilityBadgeColor => Availability switch
    {
        FirmwareAvailability.Available => "#22C55E",
        FirmwareAvailability.LocalOnly => "#F59E0B",
        FirmwareAvailability.NotSupported => "#EF4444",
        _ => "#94A3B8"
    };
    
    /// <summary>
    /// Availability tooltip text
    /// </summary>
    public string AvailabilityText => Availability switch
    {
        FirmwareAvailability.Available => "Available",
        FirmwareAvailability.LocalOnly => "Local only",
        FirmwareAvailability.NotSupported => "Not supported",
        _ => "Unknown"
    };
}

/// <summary>
/// Group of vehicle types for collapsible sections
/// </summary>
public partial class VehicleTypeGroup : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public VehicleCategory Category { get; set; }
    public ObservableCollection<VehicleTypeItem> Items { get; set; } = new();
    
    [ObservableProperty]
    private bool _isExpanded = true;
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
