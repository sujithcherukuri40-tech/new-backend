using System;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the Telemetry Dashboard page.
/// Displays real-time MAVLink telemetry data from the connected drone:
///   MSG 0   HEARTBEAT         – flight mode, armed status, vehicle type
///   MSG 1   SYS_STATUS        – battery voltage / current / remaining
///   MSG 24  GPS_RAW_INT       – GPS fix type, satellite count, HDOP, VDOP
///   MSG 30  ATTITUDE          – roll, pitch, yaw and angular rates
///   MSG 33  GLOBAL_POSITION_INT – lat, lon, alt, heading, velocities
///   MSG 74  VFR_HUD           – airspeed, ground speed, climb rate, throttle
/// </summary>
public partial class TelemetryPageViewModel : ViewModelBase
{
    private readonly ITelemetryService _telemetryService;
    private readonly IConnectionService _connectionService;

    // ─── Connection ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isConnected;

    [ObservableProperty]
    private string _connectionStatus = "Disconnected";

    [ObservableProperty]
    private bool _isReceivingTelemetry;

    [ObservableProperty]
    private string _telemetryStatus = "No telemetry";

    // ─── HEARTBEAT (MSG 0) ────────────────────────────────────────────────────

    [ObservableProperty]
    private string _flightMode = "Unknown";

    [ObservableProperty]
    private bool _isArmed;

    [ObservableProperty]
    private string _armedStatus = "DISARMED";

    [ObservableProperty]
    private string _vehicleType = "Unknown";

    [ObservableProperty]
    private string _systemStatus = "Unknown";

    [ObservableProperty]
    private DateTime _lastHeartbeat = DateTime.MinValue;

    [ObservableProperty]
    private string _lastHeartbeatText = "--";

    // ─── SYS_STATUS (MSG 1) – Battery ─────────────────────────────────────────

    [ObservableProperty]
    private double _batteryVoltage;

    [ObservableProperty]
    private double _batteryCurrent;

    [ObservableProperty]
    private int _batteryRemaining = -1;

    [ObservableProperty]
    private string _batteryRemainingText = "--";

    [ObservableProperty]
    private string _batteryStatusColor = "#9CA3AF";

    // ─── GPS_RAW_INT (MSG 24) ─────────────────────────────────────────────────

    [ObservableProperty]
    private int _gpsFixType;

    [ObservableProperty]
    private string _gpsFixTypeDescription = "No GPS";

    [ObservableProperty]
    private int _satelliteCount;

    [ObservableProperty]
    private double _hdop;

    [ObservableProperty]
    private double _vdop;

    [ObservableProperty]
    private string _gpsStatusColor = "#EF4444";

    // ─── ATTITUDE (MSG 30) ────────────────────────────────────────────────────

    [ObservableProperty]
    private double _roll;

    [ObservableProperty]
    private double _pitch;

    [ObservableProperty]
    private double _yaw;

    [ObservableProperty]
    private double _rollSpeed;

    [ObservableProperty]
    private double _pitchSpeed;

    [ObservableProperty]
    private double _yawSpeed;

    [ObservableProperty]
    private DateTime _lastAttitudeUpdate = DateTime.MinValue;

    // ─── GLOBAL_POSITION_INT (MSG 33) ─────────────────────────────────────────

    [ObservableProperty]
    private double _latitude;

    [ObservableProperty]
    private double _longitude;

    [ObservableProperty]
    private double _altitudeMsl;

    [ObservableProperty]
    private double _altitudeRelative;

    [ObservableProperty]
    private double _heading;

    [ObservableProperty]
    private double _velX;

    [ObservableProperty]
    private double _velY;

    [ObservableProperty]
    private double _velZ;

    [ObservableProperty]
    private string _positionText = "N/A";

    [ObservableProperty]
    private DateTime _lastPositionUpdate = DateTime.MinValue;

    // ─── VFR_HUD (MSG 74) ─────────────────────────────────────────────────────

    [ObservableProperty]
    private double _airspeed;

    [ObservableProperty]
    private double _groundSpeed;

    [ObservableProperty]
    private int _throttle;

    [ObservableProperty]
    private double _climbRate;

    // ─── Constructor ──────────────────────────────────────────────────────────

    public TelemetryPageViewModel(
        ITelemetryService telemetryService,
        IConnectionService connectionService)
    {
        _telemetryService = telemetryService;
        _connectionService = connectionService;

        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        _telemetryService.TelemetryUpdated += OnTelemetryUpdated;
        _telemetryService.TelemetryAvailabilityChanged += OnTelemetryAvailabilityChanged;

        IsConnected = _connectionService.IsConnected;
        UpdateConnectionStatus();

        // Populate from current snapshot if already streaming
        if (_telemetryService.IsReceivingTelemetry)
        {
            ApplyTelemetry(_telemetryService.CurrentTelemetry);
        }
        
        // Force re-request telemetry streams when this page is opened
        // This ensures we get ATTITUDE, VFR_HUD, GPS data even if initial request failed
        if (IsConnected)
        {
            _telemetryService.RequestStreams();
        }
    }

    // ─── Event handlers ───────────────────────────────────────────────────────

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            UpdateConnectionStatus();
            if (!connected)
            {
                ResetTelemetry();
            }
        });
    }

    private void OnTelemetryUpdated(object? sender, TelemetryModel telemetry)
    {
        Dispatcher.UIThread.Post(() => ApplyTelemetry(telemetry));
    }

    private void OnTelemetryAvailabilityChanged(object? sender, bool available)
    {
        Dispatcher.UIThread.Post(() =>
        {
            IsReceivingTelemetry = available;
            TelemetryStatus = available ? "Receiving" : "No telemetry";
        });
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void UpdateConnectionStatus()
    {
        ConnectionStatus = IsConnected ? "Connected" : "Disconnected";
        if (!IsConnected)
        {
            IsReceivingTelemetry = false;
            TelemetryStatus = "No telemetry";
        }
    }

    private void ApplyTelemetry(TelemetryModel t)
    {
        // HEARTBEAT
        FlightMode   = t.FlightMode;
        IsArmed      = t.IsArmed;
        ArmedStatus  = t.IsArmed ? "ARMED" : "DISARMED";
        VehicleType  = t.VehicleType;
        SystemStatus = t.SystemStatus;
        LastHeartbeat = t.LastHeartbeatUpdate;
        LastHeartbeatText = t.LastHeartbeatUpdate != DateTime.MinValue
            ? t.LastHeartbeatUpdate.ToLocalTime().ToString("HH:mm:ss")
            : "--";

        // SYS_STATUS – Battery
        BatteryVoltage   = t.BatteryVoltage;
        BatteryCurrent   = t.BatteryCurrent;
        BatteryRemaining = t.BatteryRemaining;
        BatteryRemainingText = t.BatteryRemaining >= 0 ? $"{t.BatteryRemaining}%" : "--";
        BatteryStatusColor = t.BatteryRemaining switch
        {
            >= 50 => "#22C55E",
            >= 20 => "#F59E0B",
            >= 0  => "#EF4444",
            _     => "#9CA3AF"
        };

        // GPS_RAW_INT
        GpsFixType            = t.GpsFixType;
        GpsFixTypeDescription = t.GpsFixTypeDescription;
        SatelliteCount        = t.SatelliteCount;
        Hdop                  = t.Hdop;
        Vdop                  = t.Vdop;
        GpsStatusColor        = t.GpsFixType >= 3 ? "#22C55E"
                              : t.GpsFixType == 2 ? "#F59E0B"
                              : "#EF4444";

        // ATTITUDE
        Roll       = Math.Round(t.Roll,  1);
        Pitch      = Math.Round(t.Pitch, 1);
        Yaw        = Math.Round(t.Yaw,   1);
        RollSpeed  = Math.Round(t.RollSpeed,  1);
        PitchSpeed = Math.Round(t.PitchSpeed, 1);
        YawSpeed   = Math.Round(t.YawSpeed,   1);
        LastAttitudeUpdate = t.LastAttitudeUpdate;

        // GLOBAL_POSITION_INT
        Latitude         = t.Latitude;
        Longitude        = t.Longitude;
        AltitudeMsl      = Math.Round(t.AltitudeMsl,      1);
        AltitudeRelative = Math.Round(t.AltitudeRelative, 1);
        Heading          = Math.Round(t.Heading,           1);
        VelX             = Math.Round(t.GroundSpeedX, 2);
        VelY             = Math.Round(t.GroundSpeedY, 2);
        VelZ             = Math.Round(t.VerticalSpeed, 2);
        PositionText     = t.HasValidPosition
            ? $"{t.Latitude:F6}°, {t.Longitude:F6}°"
            : "No Fix";
        LastPositionUpdate = t.LastPositionUpdate;

        // VFR_HUD
        Airspeed    = Math.Round(t.Airspeed,     1);
        GroundSpeed = Math.Round(t.GroundSpeed,  1);
        Throttle    = t.Throttle;
        ClimbRate   = Math.Round(t.ClimbRate,    2);

        IsReceivingTelemetry = true;
        TelemetryStatus = "Receiving";
    }

    private void ResetTelemetry()
    {
        // HEARTBEAT
        FlightMode = "Unknown";
        IsArmed = false;
        ArmedStatus = "DISARMED";
        VehicleType = "Unknown";
        SystemStatus = "Unknown";
        LastHeartbeatText = "--";
        LastHeartbeat = DateTime.MinValue;

        // SYS_STATUS – Battery
        BatteryVoltage = 0;
        BatteryCurrent = 0;
        BatteryRemaining = -1;
        BatteryRemainingText = "--";
        BatteryStatusColor = "#9CA3AF";

        // GPS_RAW_INT
        GpsFixType = 0;
        GpsFixTypeDescription = "No GPS";
        SatelliteCount = 0;
        Hdop = 0;
        Vdop = 0;
        GpsStatusColor = "#EF4444";

        // ATTITUDE
        Roll = 0;
        Pitch = 0;
        Yaw = 0;
        RollSpeed = 0;
        PitchSpeed = 0;
        YawSpeed = 0;

        // GLOBAL_POSITION_INT
        Latitude = 0;
        Longitude = 0;
        AltitudeMsl = 0;
        AltitudeRelative = 0;
        Heading = 0;
        VelX = 0;
        VelY = 0;
        VelZ = 0;
        PositionText = "N/A";

        // VFR_HUD
        Airspeed = 0;
        GroundSpeed = 0;
        Throttle = 0;
        ClimbRate = 0;

        IsReceivingTelemetry = false;
        TelemetryStatus = "No telemetry";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
            _telemetryService.TelemetryUpdated -= OnTelemetryUpdated;
            _telemetryService.TelemetryAvailabilityChanged -= OnTelemetryAvailabilityChanged;
        }
        base.Dispose(disposing);
    }
}
