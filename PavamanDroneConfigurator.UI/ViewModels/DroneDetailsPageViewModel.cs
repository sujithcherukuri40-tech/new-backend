using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using System;
using System.Threading;

namespace PavamanDroneConfigurator.UI.ViewModels;

public partial class DroneDetailsPageViewModel : ViewModelBase
{
    private readonly IDroneInfoService _droneInfoService;
    private readonly IConnectionService _connectionService;
    
    /// <summary>
    /// Timer for throttling UI updates to prevent flickering.
    /// Increased from 500ms to 1000ms to reduce UI refresh rate and prevent flickering.
    /// </summary>
    private Timer? _updateThrottleTimer;
    private DroneInfo? _pendingUpdate;
    private readonly object _updateLock = new();
    
    /// <summary>
    /// Throttle interval for UI updates. Set to 1000ms to prevent flickering from frequent heartbeat updates.
    /// This ensures the UI only updates once per second at most.
    /// </summary>
    private const int UPDATE_THROTTLE_MS = 1000;

    [ObservableProperty]
    private string _droneId = "Not Connected";

    [ObservableProperty]
    private string _fcId = "Not Connected";

    [ObservableProperty]
    private string _firmwareVersion = "-";

    [ObservableProperty]
    private string _codeChecksum = "-";

    [ObservableProperty]
    private string _dataChecksum = "-";

    [ObservableProperty]
    private string _vehicleType = "-";

    [ObservableProperty]
    private string _autopilotType = "-";

    [ObservableProperty]
    private string _boardType = "-";

    [ObservableProperty]
    private string _flightMode = "-";

    [ObservableProperty]
    private bool _isArmed;

    [ObservableProperty]
    private byte _systemId;

    [ObservableProperty]
    private byte _componentId;

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = "Connect to a drone to view details";

    public DroneDetailsPageViewModel(
        IDroneInfoService droneInfoService,
        IConnectionService connectionService)
    {
        _droneInfoService = droneInfoService;
        _connectionService = connectionService;

        // Subscribe to events
        _droneInfoService.DroneInfoUpdated += OnDroneInfoUpdated;
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;

        // Initialize state
        IsConnected = _connectionService.IsConnected;
        if (IsConnected)
        {
            _ = RefreshAsync();
        }
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            if (connected)
            {
                StatusMessage = "Loading drone details...";
                _ = RefreshAsync();
            }
            else
            {
                ClearDroneInfo();
                StatusMessage = "Connect to a drone to view details";
                
                // Stop throttle timer on disconnect
                lock (_updateLock)
                {
                    _updateThrottleTimer?.Dispose();
                    _updateThrottleTimer = null;
                    _pendingUpdate = null;
                }
            }
        });
    }

    private void OnDroneInfoUpdated(object? sender, DroneInfo info)
    {
        // Throttle updates to prevent flickering from frequent heartbeat updates
        lock (_updateLock)
        {
            _pendingUpdate = info;
            
            // If timer is not running, start it and update immediately
            if (_updateThrottleTimer == null)
            {
                // Apply update immediately for first update
                Dispatcher.UIThread.Post(() => ApplyPendingUpdate());
                
                // Start throttle timer for subsequent updates
                _updateThrottleTimer = new Timer(
                    OnThrottleTimerTick,
                    null,
                    UPDATE_THROTTLE_MS,
                    UPDATE_THROTTLE_MS);
            }
        }
    }
    
    private void OnThrottleTimerTick(object? state)
    {
        DroneInfo? updateToApply = null;
        
        lock (_updateLock)
        {
            if (_pendingUpdate != null)
            {
                updateToApply = _pendingUpdate;
                _pendingUpdate = null;
            }
        }
        
        if (updateToApply != null)
        {
            Dispatcher.UIThread.Post(() => ApplyUpdate(updateToApply));
        }
    }
    
    private void ApplyPendingUpdate()
    {
        DroneInfo? updateToApply;
        lock (_updateLock)
        {
            updateToApply = _pendingUpdate;
            _pendingUpdate = null;
        }
        
        if (updateToApply != null)
        {
            ApplyUpdate(updateToApply);
        }
    }
    
    /// <summary>
    /// Applies drone info update to UI properties only if values have changed.
    /// This prevents unnecessary property change notifications that cause flickering.
    /// </summary>
    private void ApplyUpdate(DroneInfo info)
    {
        // Only update properties that have actually changed to minimize UI refreshes
        if (DroneId != info.DroneId)
            DroneId = info.DroneId;
        if (FcId != info.FcId)
            FcId = info.FcId;
        if (FirmwareVersion != info.FirmwareVersion)
            FirmwareVersion = info.FirmwareVersion;
        if (CodeChecksum != info.CodeChecksum)
            CodeChecksum = info.CodeChecksum;
        if (DataChecksum != info.DataChecksum)
            DataChecksum = info.DataChecksum;
        if (VehicleType != info.VehicleType)
            VehicleType = info.VehicleType;
        if (AutopilotType != info.AutopilotType)
            AutopilotType = info.AutopilotType;
        if (BoardType != info.BoardType)
            BoardType = info.BoardType;
        if (FlightMode != info.FlightMode)
            FlightMode = info.FlightMode;
        if (IsArmed != info.IsArmed)
            IsArmed = info.IsArmed;
        if (SystemId != info.SystemId)
            SystemId = info.SystemId;
        if (ComponentId != info.ComponentId)
            ComponentId = info.ComponentId;
        
        if (StatusMessage != "Drone information loaded")
            StatusMessage = "Drone information loaded";
    }

    private void ClearDroneInfo()
    {
        DroneId = "Not Connected";
        FcId = "Not Connected";
        FirmwareVersion = "-";
        CodeChecksum = "-";
        DataChecksum = "-";
        VehicleType = "-";
        AutopilotType = "-";
        BoardType = "-";
        FlightMode = "-";
        IsArmed = false;
        SystemId = 0;
        ComponentId = 0;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!IsConnected)
        {
            StatusMessage = "Not connected to a drone";
            return;
        }

        IsLoading = true;
        StatusMessage = "Refreshing drone details...";

        try
        {
            await _droneInfoService.RefreshDroneInfoAsync();
            var info = await _droneInfoService.GetDroneInfoAsync();
            
            if (info != null)
            {
                ApplyUpdate(info);
            }
            else
            {
                StatusMessage = "Unable to retrieve drone information";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _droneInfoService.DroneInfoUpdated -= OnDroneInfoUpdated;
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            
            lock (_updateLock)
            {
                _updateThrottleTimer?.Dispose();
                _updateThrottleTimer = null;
            }
        }
        base.Dispose(disposing);
    }
}
