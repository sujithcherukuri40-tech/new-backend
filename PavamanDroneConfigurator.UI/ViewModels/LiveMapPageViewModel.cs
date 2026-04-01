using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using PavamanDroneConfigurator.Infrastructure.Services;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the Live Map page - Real-time drone telemetry visualization.
/// Supports multiple view modes: Top-Down, 3D Chase, Free Roam
/// Integrates with TelemetryService for live data updates.
/// </summary>
public partial class LiveMapPageViewModel : ViewModelBase
{
    private const string LiveStatusOnline = "LIVE";
    private const string LiveStatusOffline = "OFFLINE";

    // PWM output range for servo-based spray/gimbal control
    private const int PwmMin = 1000;
    private const int PwmRange = 1000; // PwmMax (2000) - PwmMin (1000)

    private readonly IConnectionService _connectionService;
    private readonly ITelemetryService _telemetryService;
    private readonly IVideoStreamingService _videoStreamingService;
    private readonly List<(double Lat, double Lon)> _flightPath = new();

    #region Observable Properties - Connection Status

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    #endregion

    #region Observable Properties - GPS & Position

    [ObservableProperty]
    private double _latitude;

    [ObservableProperty]
    private double _longitude;

    [ObservableProperty]
    private double _altitude;

    [ObservableProperty]
    private double _heading;

    [ObservableProperty]
    private int _satelliteCount;

    [ObservableProperty]
    private double _hdop;

    [ObservableProperty]
    private string _gpsFixType = "No GPS";

    [ObservableProperty]
    private bool _hasValidPosition;

    [ObservableProperty]
    private string _positionString = "N/A";

    #endregion

    #region Observable Properties - Flight Data

    [ObservableProperty]
    private double _groundSpeed;

    [ObservableProperty]
    private double _airspeed;

    [ObservableProperty]
    private double _climbRate;

    [ObservableProperty]
    private double _verticalSpeed;

    [ObservableProperty]
    private int _throttle;

    [ObservableProperty]
    private string _flightMode = "Unknown";

    [ObservableProperty]
    private bool _isArmed;

    [ObservableProperty]
    private string _armedStatus = "Disarmed";

    #endregion

    #region Observable Properties - Battery

    [ObservableProperty]
    private double _batteryVoltage;

    [ObservableProperty]
    private double _batteryCurrent;

    [ObservableProperty]
    private int _batteryRemaining = -1;

    [ObservableProperty]
    private string _batteryStatus = "N/A";

    #endregion

    #region Observable Properties - Attitude

    [ObservableProperty]
    private double _roll;

    [ObservableProperty]
    private double _pitch;

    [ObservableProperty]
    private double _yaw;

    #endregion

    #region Observable Properties - Spray System

    [ObservableProperty]
    private bool _isSprayEnabled;

    [ObservableProperty]
    private double _flowRate;

    [ObservableProperty]
    private string _flowRateStatus = "OFF";

    [ObservableProperty]
    private double _tankLevel = 100;

    [ObservableProperty]
    private double _areaSprayedAcres;

    [ObservableProperty]
    private double _totalConsumedLiters;

    [ObservableProperty]
    private bool _isSprayPanelVisible;

    [ObservableProperty]
    private int _sprayPumpChannel = 9;

    [ObservableProperty]
    private int _sprayPumpPwmOn = 1900;

    [ObservableProperty]
    private int _sprayPumpPwmOff = 1100;

    [ObservableProperty]
    private int _sprayRelayNumber;

    [ObservableProperty]
    private int _sprayPumpSpeedPercent = 80;

    [ObservableProperty]
    private bool _isSprayRelayEnabled;

    [ObservableProperty]
    private string _sprayStatus = "Idle";

    #endregion

    #region Observable Properties - Camera System

    [ObservableProperty]
    private bool _isCameraConnected;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private string _cameraStatus = "N/A";

    [ObservableProperty]
    private int _photoCount;

    [ObservableProperty]
    private string _recordingTime = "00:00";

    [ObservableProperty]
    private float _gimbalTiltAngle;

    [ObservableProperty]
    private float _gimbalPanAngle;

    [ObservableProperty]
    private string _gimbalStatus = "Idle";

    #endregion

    #region Observable Properties - Mission Data

    [ObservableProperty]
    private string _waypointStatus = "N/A";

    [ObservableProperty]
    private int _totalWaypointCount;

    [ObservableProperty]
    private double _distanceToHome;

    [ObservableProperty]
    private double _distanceToWaypoint;

    [ObservableProperty]
    private string _etaToWaypoint = "N/A";

    [ObservableProperty]
    private double _totalFlightDistance;

    [ObservableProperty]
    private string _flightTime = "00:00:00";

    #endregion

    #region Observable Properties - UI State

    [ObservableProperty]
    private bool _isReceivingTelemetry;

    public string LiveStatusText => IsReceivingTelemetry ? LiveStatusOnline : LiveStatusOffline;

    [ObservableProperty]
    private bool _followDrone = true;

    [ObservableProperty]
    private int _selectedMapTypeIndex;

    [ObservableProperty]
    private bool _showFlightPath = true;

    [ObservableProperty]
    private bool _showSprayOverlay;

    [ObservableProperty]
    private string _mapTypeLabel = "Satellite";

    [ObservableProperty]
    private string _activeMissionTool = "none";

    [ObservableProperty]
    private int _missionItemCount;

    [ObservableProperty]
    private string _missionProgressText = "Mission: 0 items";

    [ObservableProperty]
    private bool _isCameraVisible;

    [ObservableProperty]
    private int _selectedViewModeIndex;

    [ObservableProperty]
    private string _viewModeLabel = "Top-Down";

    [ObservableProperty]
    private bool _isTopDownView = true;

    [ObservableProperty]
    private bool _isChase3DView;

    [ObservableProperty]
    private bool _isFreeRoamView;

    // Right panel (Plot / Camera) state
    [ObservableProperty]
    private bool _showRightPanel;

    [ObservableProperty]
    private bool _isPlotTabActive = true;

    [ObservableProperty]
    private bool _isCameraTabActive;

    // Default altitude for new waypoints (metres AGL)
    [ObservableProperty]
    private double _defaultAltitude = 30;

    // Mission summary stats
    [ObservableProperty]
    private double _missionTotalDistanceKm;

    [ObservableProperty]
    private string _missionEstimatedTime = "0:00";

    #region Observable Properties - Video Streaming

    [ObservableProperty]
    private bool _isVideoStreaming;

    [ObservableProperty]
    private string _videoStreamUrl = "rtsp://192.168.1.1:8554/live";

    [ObservableProperty]
    private string _videoStreamStatus = "Not connected";

    #endregion

    #endregion

    partial void OnIsReceivingTelemetryChanged(bool value)
    {
        OnPropertyChanged(nameof(LiveStatusText));
    }

    partial void OnVideoStreamUrlChanged(string value)
    {
        _videoStreamingService.StreamUrl = value;
    }

    partial void OnMissionItemCountChanged(int value)
    {
        TotalWaypointCount = value;
        WaypointStatus = value > 0 ? value.ToString() : "N/A";
    }

    // Mission waypoints for Plot tab
    public ObservableCollection<MissionItem> MissionItems { get; } = new();

    // Flight path for map display
    public IReadOnlyList<(double Lat, double Lon)> FlightPath => _flightPath;

    // Events to notify view of updates
    public event EventHandler<DronePositionUpdateEventArgs>? PositionUpdated;
    public event EventHandler<int>? BatteryUpdated;
    public event EventHandler? FlightPathCleared;
    public event EventHandler? RecenterRequested;
    public event EventHandler<ViewModeChangedEventArgs>? ViewModeChanged;
    public event EventHandler<string>? MapTypeChanged;
    public event EventHandler<bool>? FollowChanged;
    public event EventHandler<string>? MissionToolChanged;

    /// <summary>Raised when the user clicks the "MAVLink Logs" button; the View opens the window.</summary>
    public event EventHandler? OpenMavlinkLogsRequested;

    // Default center (India)
    private const double DEFAULT_LAT = 20.5937;
    private const double DEFAULT_LNG = 78.9629;

    private DateTime _flightStartTime;
    private double _lastLat, _lastLon;
    private (double Lat, double Lon)? _homePosition;

    public LiveMapPageViewModel(
        IConnectionService connectionService,
        ITelemetryService telemetryService,
        IVideoStreamingService videoStreamingService)
    {
        _connectionService = connectionService;
        _telemetryService = telemetryService;
        _videoStreamingService = videoStreamingService;

        // Wire up video streaming state events
        _videoStreamingService.StreamingStateChanged += OnVideoStreamingStateChanged;
        _videoStreamingService.StatusChanged += OnVideoStreamStatusChanged;

        // Subscribe to connection events
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        
        // Subscribe to telemetry events
        _telemetryService.TelemetryUpdated += OnTelemetryUpdated;
        _telemetryService.PositionChanged += OnPositionChanged;
        _telemetryService.BatteryStatusChanged += OnBatteryStatusChanged;
        _telemetryService.GpsStatusChanged += OnGpsStatusChanged;
        _telemetryService.TelemetryAvailabilityChanged += OnTelemetryAvailabilityChanged;

        // Initialize state
        IsConnected = _connectionService.IsConnected;
        ConnectionStatus = IsConnected ? "Connected" : "Disconnected";
        
        // Start telemetry service if already connected
        if (IsConnected)
        {
            _telemetryService.Start();
            // Force re-request telemetry streams when this page is opened
            // This ensures we get ATTITUDE, VFR_HUD, GPS data even if initial request failed
            _telemetryService.RequestStreams();
            _flightStartTime = DateTime.Now;
        }
    }

    #region Event Handlers

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            ConnectionStatus = connected ? "Connected" : "Disconnected";
            
            if (connected)
            {
                _telemetryService.Start();
                _flightStartTime = DateTime.Now;
            }
            else
            {
                _telemetryService.Stop();
                ResetTelemetryDisplay();
            }
        });
    }

    private void OnTelemetryUpdated(object? sender, TelemetryModel telemetry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Update all telemetry properties
            Latitude = telemetry.Latitude;
            Longitude = telemetry.Longitude;
            Altitude = telemetry.AltitudeRelative;
            Heading = telemetry.Heading;

            var resolvedGroundSpeed = telemetry.GroundSpeed;
            if (resolvedGroundSpeed <= 0.001)
            {
                resolvedGroundSpeed = telemetry.GroundSpeedMagnitude;
            }

            GroundSpeed = resolvedGroundSpeed;
            Airspeed = telemetry.Airspeed;
            ClimbRate = telemetry.ClimbRate;
            VerticalSpeed = telemetry.VerticalSpeed;
            Throttle = telemetry.Throttle;
            FlightMode = telemetry.FlightMode;
            IsArmed = telemetry.IsArmed;
            ArmedStatus = telemetry.IsArmed ? "ARMED" : "Disarmed";
            Roll = telemetry.Roll;
            Pitch = telemetry.Pitch;
            Yaw = telemetry.Yaw;
            
            // Check for actual valid position (non-zero coordinates)
            bool hasActualPosition = Math.Abs(telemetry.Latitude) > 0.0001 || Math.Abs(telemetry.Longitude) > 0.0001;
            HasValidPosition = hasActualPosition;
            
            // Update position string
            if (HasValidPosition)
            {
                PositionString = $"{Latitude:F6}, {Longitude:F6}";
            }
            else
            {
                PositionString = "Waiting for GPS...";
            }

            // Update flight time
            if (IsConnected)
            {
                var elapsed = DateTime.Now - _flightStartTime;
                FlightTime = elapsed.ToString(@"hh\:mm\:ss");
            }

            // Set home position on first valid fix
            if (!_homePosition.HasValue && HasValidPosition)
            {
                _homePosition = (Latitude, Longitude);
            }

            // Calculate distance to home
            if (HasValidPosition && _homePosition.HasValue)
            {
                DistanceToHome = CalculateDistance(Latitude, Longitude, _homePosition.Value.Lat, _homePosition.Value.Lon);
            }

            // Add to flight path if valid position and moved significantly
            if (HasValidPosition && ShowFlightPath)
            {
                var dist = CalculateDistance(Latitude, Longitude, _lastLat, _lastLon);
                if (dist > 0.5 || _flightPath.Count == 0) // More than 0.5m moved
                {
                    _flightPath.Add((Latitude, Longitude));
                    TotalFlightDistance += dist / 1000.0; // Convert to km
                    _lastLat = Latitude;
                    _lastLon = Longitude;
                    
                    // Limit path points
                    if (_flightPath.Count > 5000)
                    {
                        _flightPath.RemoveAt(0);
                    }
                }
            }

            // Notify view of position update ONLY with actual valid coordinates
            if (HasValidPosition)
            {
                PositionUpdated?.Invoke(this, new DronePositionUpdateEventArgs
                {
                    Latitude = Latitude,
                    Longitude = Longitude,
                    Altitude = Altitude,
                    Heading = Heading,
                    Pitch = Pitch,
                    Roll = Roll,
                    GroundSpeed = GroundSpeed,
                    VerticalSpeed = VerticalSpeed,
                    IsArmed = IsArmed,
                    SatelliteCount = SatelliteCount,
                    FlightMode = FlightMode,
                    FlowRate = FlowRate
                });
            }
        });
    }

    private void OnPositionChanged(object? sender, PositionChangedEventArgs e)
    {
        // Already handled in OnTelemetryUpdated
    }

    private void OnBatteryStatusChanged(object? sender, BatteryStatusEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            BatteryVoltage = e.Voltage;
            BatteryCurrent = e.Current;
            BatteryRemaining = e.RemainingPercent;
            
            if (e.RemainingPercent >= 0)
            {
                BatteryStatus = $"{e.RemainingPercent}%";
            }
            else
            {
                BatteryStatus = $"{e.Voltage:F1}V";
            }

            BatteryUpdated?.Invoke(this, e.RemainingPercent);
        });
    }

    private void OnGpsStatusChanged(object? sender, GpsStatusEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SatelliteCount = e.SatelliteCount;
            Hdop = e.Hdop;
            GpsFixType = e.FixType switch
            {
                0 => "No GPS",
                1 => "No Fix",
                2 => "2D Fix",
                3 => "3D Fix",
                4 => "DGPS",
                5 => "RTK Float",
                6 => "RTK Fixed",
                _ => $"Type {e.FixType}"
            };
        });
    }

    private void OnTelemetryAvailabilityChanged(object? sender, bool available)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsReceivingTelemetry = available;
        });
    }

    private void OnVideoStreamingStateChanged(object? sender, bool isStreaming)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsVideoStreaming = isStreaming;
            if (!isStreaming && IsCameraVisible)
            {
                // Keep the panel open so the user sees the status message.
            }
        });
    }

    private void OnVideoStreamStatusChanged(object? sender, string status)
    {
        Dispatcher.UIThread.Post(() =>
        {
            VideoStreamStatus = status;
        });
    }

    private void ResetTelemetryDisplay()
    {
        Latitude = 0;
        Longitude = 0;
        Altitude = 0;
        Heading = 0;
        GroundSpeed = 0;
        Airspeed = 0;
        ClimbRate = 0;
        VerticalSpeed = 0;
        Throttle = 0;
        FlightMode = "Unknown";
        IsArmed = false;
        ArmedStatus = "Disarmed";
        Roll = 0;
        Pitch = 0;
        Yaw = 0;
        SatelliteCount = 0;
        Hdop = 0;
        GpsFixType = "No GPS";
        BatteryVoltage = 0;
        BatteryCurrent = 0;
        BatteryRemaining = -1;
        BatteryStatus = "N/A";
        HasValidPosition = false;
        IsReceivingTelemetry = false;
        PositionString = "N/A";
        FlightTime = "00:00:00";
        DistanceToHome = 0;
        _homePosition = null;
        
        // Reset spray/camera
        IsSprayEnabled = false;
        IsSprayRelayEnabled = false;
        FlowRate = 0;
        FlowRateStatus = "OFF";
        SprayStatus = "Idle";
        IsSprayPanelVisible = false;
        IsCameraConnected = false;
        IsRecording = false;
        CameraStatus = "N/A";
        GimbalTiltAngle = 0;
        GimbalPanAngle = 0;
        GimbalStatus = "Idle";
    }

    #endregion

    #region Commands

    [RelayCommand]
    private void ToggleFollowDrone()
    {
        FollowDrone = !FollowDrone;
        FollowChanged?.Invoke(this, FollowDrone);
    }

    [RelayCommand]
    private void RecenterMap()
    {
        RecenterRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void RefreshTelemetry()
    {
        if (!IsConnected)
            return;

        // Force re-request telemetry streams from the drone
        _telemetryService.RequestStreams();
        
        Console.WriteLine("[LiveMapPage] ??? Manual telemetry refresh requested");
    }

    [RelayCommand]
    private void ClearFlightPath()
    {
        _flightPath.Clear();
        TotalFlightDistance = 0;
        _homePosition = null;
        FlightPathCleared?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void ChangeMapType()
    {
        // Cycle through map types: 0=Satellite, 1=Street, 2=Terrain, 3=Hybrid
        SelectedMapTypeIndex = (SelectedMapTypeIndex + 1) % 4;
        MapTypeLabel = SelectedMapTypeIndex switch
        {
            0 => "Satellite",
            1 => "Roadmap",
            2 => "Terrain",
            3 => "Hybrid",
            _ => "Satellite"
        };

        MapTypeChanged?.Invoke(this, GetMapTypeName());
    }

    [RelayCommand]
    private void SetViewModeTopDown()
    {
        SelectedViewModeIndex = 0;
        ViewModeLabel = "Top-Down";
        IsTopDownView = true;
        IsChase3DView = false;
        IsFreeRoamView = false;
        ViewModeChanged?.Invoke(this, new ViewModeChangedEventArgs { ViewMode = "topdown" });
    }

    [RelayCommand]
    private void SetViewModeChase3D()
    {
        SelectedViewModeIndex = 1;
        ViewModeLabel = "3D Chase";
        IsTopDownView = false;
        IsChase3DView = true;
        IsFreeRoamView = false;
        ViewModeChanged?.Invoke(this, new ViewModeChangedEventArgs { ViewMode = "chase3d" });
    }

    [RelayCommand]
    private void SetViewModeFreeRoam()
    {
        SelectedViewModeIndex = 2;
        ViewModeLabel = "Free Roam";
        IsTopDownView = false;
        IsChase3DView = false;
        IsFreeRoamView = true;
        ViewModeChanged?.Invoke(this, new ViewModeChangedEventArgs { ViewMode = "free" });
    }

    [RelayCommand]
    private void CycleViewMode()
    {
        SelectedViewModeIndex = (SelectedViewModeIndex + 1) % 3;
        switch (SelectedViewModeIndex)
        {
            case 0:
                SetViewModeTopDown();
                break;
            case 1:
                SetViewModeChase3D();
                break;
            case 2:
                SetViewModeFreeRoam();
                break;
        }
    }

    [RelayCommand]
    private void ToggleFlightPath()
    {
        ShowFlightPath = !ShowFlightPath;
    }

    [RelayCommand]
    private void ToggleSprayOverlay()
    {
        ShowSprayOverlay = !ShowSprayOverlay;
    }

    [RelayCommand]
    private void ToggleSpray()
    {
        IsSprayEnabled = !IsSprayEnabled;
        if (IsSprayEnabled)
        {
            _connectionService.SendDoSetServo(SprayPumpChannel, SprayPumpPwmOn);
            FlowRateStatus = $"{FlowRate:F1} L/min";
            SprayStatus = "Spraying";
        }
        else
        {
            _connectionService.SendDoSetServo(SprayPumpChannel, SprayPumpPwmOff);
            FlowRateStatus = "OFF";
            SprayStatus = "Idle";
        }
    }

    [RelayCommand]
    private void ToggleSprayRelay()
    {
        IsSprayRelayEnabled = !IsSprayRelayEnabled;
        _connectionService.SendDoSetRelay(SprayRelayNumber, IsSprayRelayEnabled);
        FlowRateStatus = IsSprayRelayEnabled ? $"{FlowRate:F1} L/min" : "OFF";
        SprayStatus = IsSprayRelayEnabled ? "Spraying (Relay)" : "Idle";
    }

    [RelayCommand]
    private void ToggleSprayPanel()
    {
        IsSprayPanelVisible = !IsSprayPanelVisible;
    }

    [RelayCommand]
    private void SetSprayPumpSpeed(string? speedParam)
    {
        if (int.TryParse(speedParam, out int speed))
        {
            SprayPumpSpeedPercent = Math.Clamp(speed, 0, 100);
        }
        int pwm = PwmMin + (int)(SprayPumpSpeedPercent / 100.0 * PwmRange);
        SprayPumpPwmOn = pwm;
        if (IsSprayEnabled)
        {
            _connectionService.SendDoSetServo(SprayPumpChannel, pwm);
            SprayStatus = $"Spraying at {SprayPumpSpeedPercent}%";
        }
    }

    [RelayCommand]
    private void TakePhoto()
    {
        _connectionService.SendImageStartCapture(0, 1);
        PhotoCount++;
        CameraStatus = "Photo taken";
    }

    [RelayCommand]
    private void ToggleRecording()
    {
        IsRecording = !IsRecording;
        if (IsRecording)
        {
            _connectionService.SendVideoStartCapture();
            CameraStatus = "Recording";
        }
        else
        {
            _connectionService.SendVideoStopCapture();
            CameraStatus = "Ready";
        }
    }

    // ── Gimbal Control Commands ────────────────────────────────────────────

    [RelayCommand]
    private void GimbalTiltUp()
    {
        GimbalTiltAngle = Math.Clamp(GimbalTiltAngle + 5f, -90f, 30f);
        _connectionService.SendDoMountControl(GimbalTiltAngle, 0, GimbalPanAngle);
        GimbalStatus = $"Tilt: {GimbalTiltAngle:F0}°";
    }

    [RelayCommand]
    private void GimbalTiltDown()
    {
        GimbalTiltAngle = Math.Clamp(GimbalTiltAngle - 5f, -90f, 30f);
        _connectionService.SendDoMountControl(GimbalTiltAngle, 0, GimbalPanAngle);
        GimbalStatus = $"Tilt: {GimbalTiltAngle:F0}°";
    }

    [RelayCommand]
    private void GimbalPanLeft()
    {
        GimbalPanAngle = Math.Clamp(GimbalPanAngle - 5f, -180f, 180f);
        _connectionService.SendDoMountControl(GimbalTiltAngle, 0, GimbalPanAngle);
        GimbalStatus = $"Pan: {GimbalPanAngle:F0}°";
    }

    [RelayCommand]
    private void GimbalPanRight()
    {
        GimbalPanAngle = Math.Clamp(GimbalPanAngle + 5f, -180f, 180f);
        _connectionService.SendDoMountControl(GimbalTiltAngle, 0, GimbalPanAngle);
        GimbalStatus = $"Pan: {GimbalPanAngle:F0}°";
    }

    [RelayCommand]
    private void GimbalCenter()
    {
        GimbalTiltAngle = 0;
        GimbalPanAngle = 0;
        _connectionService.SendDoMountControl(0, 0, 0);
        GimbalStatus = "Centered";
    }

    [RelayCommand]
    private void GimbalLookDown()
    {
        GimbalTiltAngle = -90;
        GimbalPanAngle = 0;
        _connectionService.SendDoMountControl(-90, 0, 0);
        GimbalStatus = "Nadir";
    }

    [RelayCommand]
    private void OpenCameraView()
    {
        IsCameraVisible = true;
    }

    [RelayCommand]
    private void CloseCameraView()
    {
        IsCameraVisible = false;
    }

    /// <summary>Start the RTSP/UDP video stream from the drone camera.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task StartVideoStreamAsync()
    {
        _videoStreamingService.StreamUrl = VideoStreamUrl;
        await _videoStreamingService.StartAsync();
        IsCameraVisible = true;
    }

    /// <summary>Stop the current video stream.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task StopVideoStreamAsync()
    {
        await _videoStreamingService.StopAsync();
    }

    /// <summary>Toggle the video stream on / off.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ToggleVideoStreamAsync()
    {
        if (IsVideoStreaming)
            await StopVideoStreamAsync();
        else
            await StartVideoStreamAsync();
    }

    /// <summary>
    /// Toggle the camera panel visibility and start/stop the stream accordingly.
    /// Satisfies the MVVM requirement: ToggleCameraCommand.
    /// </summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task ToggleCameraAsync()
    {
        if (IsCameraVisible)
        {
            IsCameraVisible = false;
            if (IsVideoStreaming)
                await _videoStreamingService.StopAsync();
        }
        else
        {
            IsCameraVisible = true;
            if (!IsVideoStreaming)
            {
                _videoStreamingService.StreamUrl = VideoStreamUrl;
                await _videoStreamingService.StartAsync();
            }
        }
    }

    /// <summary>Open the MAVLink logs window (handled by the View).</summary>
    [RelayCommand]
    private void OpenMavlinkLogs() => RaiseMavlinkLogsRequested();

    /// <summary>
    /// Open the MAVLink logs window.
    /// Satisfies the MVVM requirement: OpenLogsCommand.
    /// </summary>
    [RelayCommand]
    private void OpenLogs() => RaiseMavlinkLogsRequested();

    private void RaiseMavlinkLogsRequested() =>
        OpenMavlinkLogsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void SelectMissionToolWaypoint()
    {
        ActiveMissionTool = "waypoint";
        MissionToolChanged?.Invoke(this, ActiveMissionTool);
    }

    [RelayCommand]
    private void SelectMissionToolHome()
    {
        ActiveMissionTool = "home";
        MissionToolChanged?.Invoke(this, ActiveMissionTool);
    }

    [RelayCommand]
    private void SelectMissionToolSurvey()
    {
        ActiveMissionTool = "survey";
        MissionToolChanged?.Invoke(this, ActiveMissionTool);
    }

    [RelayCommand]
    private void SelectMissionToolOrbit()
    {
        ActiveMissionTool = "orbit";
        MissionToolChanged?.Invoke(this, ActiveMissionTool);
    }

    [RelayCommand]
    private void SelectMissionToolRtl()
    {
        ActiveMissionTool = "rtl";
        MissionToolChanged?.Invoke(this, ActiveMissionTool);
        RegisterMissionItem("RTL");
    }

    [RelayCommand]
    private void SelectMissionToolLand()
    {
        ActiveMissionTool = "land";
        MissionToolChanged?.Invoke(this, ActiveMissionTool);
    }

    [RelayCommand]
    private void ClearMissionItems()
    {
        MissionItems.Clear();
        MissionItemCount = 0;
        MissionProgressText = "Mission: 0 items";
        MissionTotalDistanceKm = 0;
        MissionEstimatedTime = "0:00";
        ActiveMissionTool = "none";
        MissionToolChanged?.Invoke(this, ActiveMissionTool);
    }

    public void RegisterMissionItem(string commandName, double? latitude = null, double? longitude = null, double? altitude = null)
    {
        // Map the caller-supplied name to the correct MAVLink command type.
        // SURVEY and HOME both generate a NavWaypoint entry so the position
        // is captured; they can be disambiguated via the DisplayName if needed.
        var cmd = commandName.ToUpperInvariant() switch
        {
            "RTL"     => MissionCommandType.NavReturnToLaunch,
            "LAND"    => MissionCommandType.NavLand,
            "TAKEOFF" => MissionCommandType.NavTakeoff,
            "ORBIT"   => MissionCommandType.NavLoiterTurns,
            _         => MissionCommandType.NavWaypoint  // WP / HOME / SURVEY
        };

        // Use the drone's last-known position as an initial coordinate hint.
        // Actual placement comes from map click events (OnWaypointPlaced etc.),
        // which call this method after the map has already rendered the marker;
        // subsequent MoveWaypoint calls can update the stored coordinates.
        var item = new MissionItem
        {
            Index = MissionItems.Count,
            Command = cmd,
            Altitude = (float)(altitude ?? DefaultAltitude),
            Latitude = latitude ?? Latitude,
            Longitude = longitude ?? Longitude,
            Autocontinue = true
        };

        MissionItems.Add(item);
        MissionItemCount = MissionItems.Count;
        MissionProgressText = $"Mission: {MissionItemCount} item{(MissionItemCount != 1 ? "s" : "")}";
        RecalculateMissionStats();
    }

    public bool RemoveMissionItemAt(int index)
    {
        if (index < 0 || index >= MissionItems.Count)
            return false;

        MissionItems.RemoveAt(index);

        for (int i = 0; i < MissionItems.Count; i++)
            MissionItems[i].Index = i;

        MissionItemCount = MissionItems.Count;
        MissionProgressText = MissionItemCount > 0
            ? $"Mission: {MissionItemCount} item{(MissionItemCount != 1 ? "s" : "")}"
            : "Mission: 0 items";
        RecalculateMissionStats();
        return true;
    }

    public void RefreshMissionStats()
    {
        RecalculateMissionStats();
    }

    /// <summary>
    /// Recalculate mission total distance and estimated time from waypoints.
    /// Skips items that have no GPS fix yet (both lat and lon at exactly 0.0
    /// is used as a sentinel; real Gulf-of-Guinea locations are vanishingly rare
    /// for agricultural/drone-config use-cases this app targets).
    /// </summary>
    private void RecalculateMissionStats()
    {
        // Default cruise speed used when no speed waypoint has been defined.
        const double DefaultCruiseSpeedMs = 5.0;

        double totalM = 0;
        for (int i = 1; i < MissionItems.Count; i++)
        {
            var prev = MissionItems[i - 1];
            var curr = MissionItems[i];
            if (prev.Latitude == 0 && prev.Longitude == 0) continue;
            if (curr.Latitude == 0 && curr.Longitude == 0) continue;
            totalM += CalculateDistance(prev.Latitude, prev.Longitude, curr.Latitude, curr.Longitude);
        }

        MissionTotalDistanceKm = totalM / 1000.0;
        var secs = totalM / DefaultCruiseSpeedMs;
        MissionEstimatedTime = TimeSpan.FromSeconds(secs).ToString(@"m\:ss");
    }

    // ── Right panel / tab toggles ──────────────────────────────────────────

    [RelayCommand]
    private void ToggleRightPanel()
    {
        ShowRightPanel = !ShowRightPanel;
    }

    [RelayCommand]
    private void ShowPlotTab()
    {
        IsPlotTabActive = true;
        IsCameraTabActive = false;
        ShowRightPanel = true;
    }

    [RelayCommand]
    private void ShowCameraTab()
    {
        IsPlotTabActive = false;
        IsCameraTabActive = true;
        ShowRightPanel = true;
    }

    [RelayCommand]
    private void RemoveMissionItem(MissionItem item)
    {
        if (MissionItems.Remove(item))
        {
            // Re-index remaining items
            for (int i = 0; i < MissionItems.Count; i++)
                MissionItems[i].Index = i;

            MissionItemCount = MissionItems.Count;
            MissionProgressText = MissionItemCount > 0
                ? $"Mission: {MissionItemCount} item{(MissionItemCount != 1 ? "s" : "")}"
                : "Mission: 0 items";
            RecalculateMissionStats();
        }
    }

    /// <summary>
    /// Gets the map type name for map providers
    /// </summary>
    public string GetMapTypeName()
    {
        return SelectedMapTypeIndex switch
        {
            0 => "satellite",
            1 => "roadmap",
            2 => "terrain",
            3 => "hybrid",
            _ => "satellite"
        };
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Calculate distance between two GPS points in meters using Haversine formula
    /// </summary>
    private static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // Earth radius in meters
        var dLat = (lat2 - lat1) * Math.PI / 180;
        var dLon = (lon2 - lon1) * Math.PI / 180;
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    #endregion

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _telemetryService.TelemetryUpdated -= OnTelemetryUpdated;
            _telemetryService.PositionChanged -= OnPositionChanged;
            _telemetryService.BatteryStatusChanged -= OnBatteryStatusChanged;
            _telemetryService.GpsStatusChanged -= OnGpsStatusChanged;
            _telemetryService.TelemetryAvailabilityChanged -= OnTelemetryAvailabilityChanged;
            _videoStreamingService.StreamingStateChanged -= OnVideoStreamingStateChanged;
            _videoStreamingService.StatusChanged -= OnVideoStreamStatusChanged;
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Event args for drone position updates with full telemetry
/// </summary>
public class DronePositionUpdateEventArgs : EventArgs
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double Altitude { get; set; }
    public double Heading { get; set; }
    public double Pitch { get; set; }
    public double Roll { get; set; }
    public double GroundSpeed { get; set; }
    public double VerticalSpeed { get; set; }
    public bool IsArmed { get; set; }
    public int SatelliteCount { get; set; }
    public string FlightMode { get; set; } = "Unknown";
    public double FlowRate { get; set; }
}

/// <summary>
/// Event args for view mode changes
/// </summary>
public class ViewModeChangedEventArgs : EventArgs
{
    public string ViewMode { get; set; } = "topdown";
}
