using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using System.Collections.Concurrent;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Telemetry service that aggregates MAVLink messages into a unified telemetry model.
/// Provides throttled updates (10Hz) to subscribers for efficient UI updates.
/// Thread-safe and production-ready.
/// </summary>
public class TelemetryService : ITelemetryService, IDisposable
{
    private readonly ILogger<TelemetryService> _logger;
    private readonly IConnectionService _connectionService;
    private readonly object _telemetryLock = new();
    
    private TelemetryModel _currentTelemetry = new();
    private readonly ConcurrentQueue<(double Lat, double Lon, double Alt, DateTime Time)> _flightPath = new();
    private const int MaxFlightPathPoints = 5000;
    
    // Throttling for 10Hz updates
    private System.Timers.Timer? _updateTimer;
    private bool _hasPendingUpdate;
    private DateTime _lastPositionUpdate = DateTime.MinValue;
    private DateTime _lastTelemetryAvailableCheck = DateTime.MinValue;
    private bool _isReceivingTelemetry;
    private bool _disposed;
    
    // Throttle interval (100ms = 10Hz)
    private const int UpdateIntervalMs = 100;
    
    // Telemetry timeout (if no data for 3 seconds, consider telemetry unavailable)
    private const int TelemetryTimeoutMs = 3000;

    public TelemetryModel CurrentTelemetry
    {
        get
        {
            lock (_telemetryLock)
            {
                return _currentTelemetry.Clone();
            }
        }
    }

    public bool IsReceivingTelemetry => _isReceivingTelemetry;
    
    public bool HasValidPosition
    {
        get
        {
            lock (_telemetryLock)
            {
                return _currentTelemetry.HasValidPosition;
            }
        }
    }

    public event EventHandler<TelemetryModel>? TelemetryUpdated;
    public event EventHandler<PositionChangedEventArgs>? PositionChanged;
    public event EventHandler<AttitudeChangedEventArgs>? AttitudeChanged;
    public event EventHandler<BatteryStatusEventArgs>? BatteryStatusChanged;
    public event EventHandler<GpsStatusEventArgs>? GpsStatusChanged;
    public event EventHandler<bool>? TelemetryAvailabilityChanged;

    public TelemetryService(
        ILogger<TelemetryService> logger,
        IConnectionService connectionService)
    {
        _logger = logger;
        _connectionService = connectionService;
        
        // Subscribe to connection events
        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
    }

    public void Start()
    {
        if (_updateTimer != null) return;
        
        _logger.LogInformation("[TelemetryService] Starting telemetry service");
        
        // Subscribe to MAVLink events
        _connectionService.GlobalPositionIntReceived += OnGlobalPositionInt;
        _connectionService.AttitudeReceived += OnAttitude;
        _connectionService.VfrHudReceived += OnVfrHud;
        _connectionService.GpsRawIntReceived += OnGpsRawInt;
        _connectionService.SysStatusReceived += OnSysStatus;
        _connectionService.HeartbeatDataReceived += OnHeartbeatData;
        
        // Start update timer (10Hz)
        _updateTimer = new System.Timers.Timer(UpdateIntervalMs);
        _updateTimer.Elapsed += OnUpdateTimerElapsed;
        _updateTimer.Start();
        
        // Request telemetry data streams from the drone
        // This is required for ArduPilot to start streaming telemetry
        RequestTelemetryStreams();
        
        _logger.LogInformation("[TelemetryService] Telemetry service started (10Hz updates)");
    }

    /// <summary>
    /// Request telemetry data streams from the drone.
    /// ArduPilot requires explicit stream requests to start sending telemetry.
    /// </summary>
    private void RequestTelemetryStreams()
    {
        try
        {
            _logger.LogInformation("[TelemetryService] ========== REQUESTING TELEMETRY STREAMS ==========");
            
            // Wait a bit for connection to stabilize
            Task.Delay(500).Wait();
            
            _logger.LogInformation("[TelemetryService] Sending REQUEST_DATA_STREAM commands...");
            
            // Request POSITION stream (GLOBAL_POSITION_INT) at 4Hz
            _connectionService.SendRequestDataStream(6, 4, 1);
            _logger.LogDebug("[TelemetryService] ? Stream 6 (POSITION) at 4Hz");
            
            // Request EXTENDED_STATUS stream (SYS_STATUS, GPS_RAW_INT) at 2Hz
            _connectionService.SendRequestDataStream(2, 2, 1);
            _logger.LogDebug("[TelemetryService] ? Stream 2 (EXTENDED_STATUS) at 2Hz");
            
            // Request EXTRA1 stream (ATTITUDE) at 10Hz for smooth attitude updates
            _connectionService.SendRequestDataStream(10, 10, 1);
            _logger.LogDebug("[TelemetryService] ? Stream 10 (EXTRA1/ATTITUDE) at 10Hz");
            
            // Request EXTRA2 stream (VFR_HUD) at 4Hz
            _connectionService.SendRequestDataStream(11, 4, 1);
            _logger.LogDebug("[TelemetryService] ? Stream 11 (EXTRA2/VFR_HUD) at 4Hz");
            
            // Request RC_CHANNELS at 2Hz
            _connectionService.SendRequestDataStream(3, 2, 1);
            _logger.LogDebug("[TelemetryService] ? Stream 3 (RC_CHANNELS) at 2Hz");
            
            // Request RAW_SENSORS stream at 2Hz
            _connectionService.SendRequestDataStream(1, 2, 1);
            _logger.LogDebug("[TelemetryService] ? Stream 1 (RAW_SENSORS) at 2Hz");
            
            // Small delay between REQUEST_DATA_STREAM and SET_MESSAGE_INTERVAL
            Task.Delay(300).Wait();
            
            _logger.LogInformation("[TelemetryService] Sending SET_MESSAGE_INTERVAL commands...");
            
            // Also use SET_MESSAGE_INTERVAL for specific messages (more reliable on newer firmware)
            // GLOBAL_POSITION_INT (33) at 4Hz (250000 us)
            _connectionService.SendSetMessageInterval(33, 250000);
            _logger.LogDebug("[TelemetryService] ? MSG 33 (GLOBAL_POSITION_INT) at 250ms");
            
            // ATTITUDE (30) at 10Hz (100000 us)
            _connectionService.SendSetMessageInterval(30, 100000);
            _logger.LogDebug("[TelemetryService] ? MSG 30 (ATTITUDE) at 100ms");
            
            // VFR_HUD (74) at 4Hz (250000 us)
            _connectionService.SendSetMessageInterval(74, 250000);
            _logger.LogDebug("[TelemetryService] ? MSG 74 (VFR_HUD) at 250ms");
            
            // SYS_STATUS (1) at 2Hz (500000 us)
            _connectionService.SendSetMessageInterval(1, 500000);
            _logger.LogDebug("[TelemetryService] ? MSG 1 (SYS_STATUS) at 500ms");
            
            // GPS_RAW_INT (24) at 2Hz (500000 us)
            _connectionService.SendSetMessageInterval(24, 500000);
            _logger.LogDebug("[TelemetryService] ? MSG 24 (GPS_RAW_INT) at 500ms");
            
            // HEARTBEAT (0) - ensure we get heartbeat with full data
            _connectionService.SendSetMessageInterval(0, 1000000);
            _logger.LogDebug("[TelemetryService] ? MSG 0 (HEARTBEAT) at 1000ms");
            
            _logger.LogInformation("[TelemetryService] ========== ALL STREAM REQUESTS SENT ==========");
            _logger.LogWarning("[TelemetryService] If you still see zeros:");
            _logger.LogWarning("[TelemetryService]   1. Check drone has GPS lock (needs satellites)");
            _logger.LogWarning("[TelemetryService]   2. Check drone is powered on and armed");
            _logger.LogWarning("[TelemetryService]   3. Try restarting the drone");
            _logger.LogWarning("[TelemetryService]   4. Check MAVLink parameter SR0_* or SR1_* stream rates");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TelemetryService] Error requesting telemetry streams");
        }
    }

    public void Stop()
    {
        _logger.LogInformation("[TelemetryService] Stopping telemetry service");
        
        // Unsubscribe from MAVLink events
        _connectionService.GlobalPositionIntReceived -= OnGlobalPositionInt;
        _connectionService.AttitudeReceived -= OnAttitude;
        _connectionService.VfrHudReceived -= OnVfrHud;
        _connectionService.GpsRawIntReceived -= OnGpsRawInt;
        _connectionService.SysStatusReceived -= OnSysStatus;
        _connectionService.HeartbeatDataReceived -= OnHeartbeatData;
        
        // Stop timer
        _updateTimer?.Stop();
        _updateTimer?.Dispose();
        _updateTimer = null;
        
        _isReceivingTelemetry = false;
        TelemetryAvailabilityChanged?.Invoke(this, false);
    }

    public void Clear()
    {
        lock (_telemetryLock)
        {
            _currentTelemetry = new TelemetryModel();
        }
        
        ClearFlightPath();
        _isReceivingTelemetry = false;
        TelemetryAvailabilityChanged?.Invoke(this, false);
    }

    public IReadOnlyList<(double Latitude, double Longitude, double Altitude, DateTime Timestamp)> GetFlightPath()
    {
        return _flightPath.ToArray().Select(p => (p.Lat, p.Lon, p.Alt, p.Time)).ToList();
    }

    public void ClearFlightPath()
    {
        while (_flightPath.TryDequeue(out _)) { }
    }

    #region MAVLink Event Handlers

    private void OnGlobalPositionInt(object? sender, GlobalPositionIntEventArgs e)
    {
        lock (_telemetryLock)
        {
            _currentTelemetry.Latitude = e.Latitude;
            _currentTelemetry.Longitude = e.Longitude;
            _currentTelemetry.AltitudeMsl = e.AltitudeMsl;
            _currentTelemetry.AltitudeRelative = e.AltitudeRelative;
            _currentTelemetry.GroundSpeedX = e.VelocityX;
            _currentTelemetry.GroundSpeedY = e.VelocityY;
            _currentTelemetry.VerticalSpeed = e.VelocityZ;
            _currentTelemetry.Heading = e.Heading;
            _currentTelemetry.LastPositionUpdate = DateTime.UtcNow;
            
            // DEBUG: Log position data
            _logger.LogDebug("[TelemetryService] Position: Lat={Lat:F6}, Lon={Lon:F6}, Alt={Alt:F1}m, Hdg={Hdg:F0}°", 
                e.Latitude, e.Longitude, e.AltitudeRelative, e.Heading);
        }
        
        // Add to flight path (throttled)
        var now = DateTime.UtcNow;
        if ((now - _lastPositionUpdate).TotalMilliseconds > 500 && 
            Math.Abs(e.Latitude) > 0.0001 && Math.Abs(e.Longitude) > 0.0001)
        {
            _lastPositionUpdate = now;
            
            // Add to path, removing old points if needed
            while (_flightPath.Count >= MaxFlightPathPoints)
            {
                _flightPath.TryDequeue(out _);
            }
            _flightPath.Enqueue((e.Latitude, e.Longitude, e.AltitudeRelative, now));
            
            _logger.LogInformation("[TelemetryService] Valid position added to flight path: {Count} points", _flightPath.Count);
        }
        
        _hasPendingUpdate = true;
        UpdateTelemetryAvailability();
    }

    private void OnAttitude(object? sender, AttitudeEventArgs e)
    {
        lock (_telemetryLock)
        {
            _currentTelemetry.Roll = e.Roll;
            _currentTelemetry.Pitch = e.Pitch;
            _currentTelemetry.Yaw = e.Yaw;
            _currentTelemetry.RollSpeed = e.RollSpeed;
            _currentTelemetry.PitchSpeed = e.PitchSpeed;
            _currentTelemetry.YawSpeed = e.YawSpeed;
            _currentTelemetry.LastAttitudeUpdate = DateTime.UtcNow;
            
            // DEBUG: Log attitude data occasionally
            if (DateTime.UtcNow.Second % 5 == 0)
            {
                _logger.LogDebug("[TelemetryService] Attitude: Roll={Roll:F1}°, Pitch={Pitch:F1}°, Yaw={Yaw:F1}°", 
                    e.Roll * 180 / Math.PI, e.Pitch * 180 / Math.PI, e.Yaw * 180 / Math.PI);
            }
        }
        
        _hasPendingUpdate = true;
        UpdateTelemetryAvailability();
    }

    private void OnVfrHud(object? sender, VfrHudEventArgs e)
    {
        lock (_telemetryLock)
        {
            _currentTelemetry.Airspeed = e.Airspeed;
            _currentTelemetry.GroundSpeed = e.GroundSpeed;
            _currentTelemetry.Throttle = e.Throttle;
            _currentTelemetry.ClimbRate = e.ClimbRate;
            
            // DEBUG: Log speed data
            _logger.LogDebug("[TelemetryService] VFR: GndSpd={GndSpd:F1}m/s, AirSpd={AirSpd:F1}m/s, Thr={Thr}%, ClimbRate={CR:F1}m/s", 
                e.GroundSpeed, e.Airspeed, e.Throttle, e.ClimbRate);
        }
        
        _hasPendingUpdate = true;
        UpdateTelemetryAvailability();
    }

    private void OnGpsRawInt(object? sender, GpsRawIntEventArgs e)
    {
        lock (_telemetryLock)
        {
            _currentTelemetry.GpsFixType = e.FixType;
            _currentTelemetry.SatelliteCount = e.SatellitesVisible;
            _currentTelemetry.Hdop = e.Hdop;
            _currentTelemetry.Vdop = e.Vdop;
            _currentTelemetry.GpsAltitude = e.Altitude;
            _currentTelemetry.LastGpsUpdate = DateTime.UtcNow;
            
            // DEBUG: Log GPS status
            _logger.LogInformation("[TelemetryService] GPS: Fix={FixType}, Sats={Sats}, HDOP={HDOP:F1}", 
                e.FixType, e.SatellitesVisible, e.Hdop);
        }
        
        // Raise GPS status event
        GpsStatusChanged?.Invoke(this, new GpsStatusEventArgs
        {
            FixType = e.FixType,
            SatelliteCount = e.SatellitesVisible,
            Hdop = e.Hdop,
            HasValidFix = e.FixType >= 2
        });
        
        _hasPendingUpdate = true;
        UpdateTelemetryAvailability();
    }

    private void OnSysStatus(object? sender, SysStatusEventArgs e)
    {
        lock (_telemetryLock)
        {
            _currentTelemetry.BatteryVoltage = e.BatteryVoltage;
            _currentTelemetry.BatteryCurrent = e.BatteryCurrent;
            _currentTelemetry.BatteryRemaining = e.BatteryRemaining;
            
            // DEBUG: Log battery status
            _logger.LogDebug("[TelemetryService] Battery: {Voltage:F1}V, {Current:F1}A, {Remaining}%", 
                e.BatteryVoltage, e.BatteryCurrent, e.BatteryRemaining);
        }
        
        // Raise battery status event
        BatteryStatusChanged?.Invoke(this, new BatteryStatusEventArgs
        {
            Voltage = e.BatteryVoltage,
            Current = e.BatteryCurrent,
            RemainingPercent = e.BatteryRemaining
        });
        
        _hasPendingUpdate = true;
        UpdateTelemetryAvailability();
    }

    private void OnHeartbeatData(object? sender, HeartbeatDataEventArgs e)
    {
        lock (_telemetryLock)
        {
            _currentTelemetry.IsArmed = e.IsArmed;
            _currentTelemetry.FlightMode = GetFlightModeName(e.VehicleType, e.CustomMode);
            _currentTelemetry.VehicleType = GetVehicleTypeName(e.VehicleType);
            _currentTelemetry.LastHeartbeatUpdate = DateTime.UtcNow;
            
            // DEBUG: Log heartbeat data
            _logger.LogDebug("[TelemetryService] Heartbeat: Armed={Armed}, Mode={Mode}, Type={Type}", 
                e.IsArmed, _currentTelemetry.FlightMode, _currentTelemetry.VehicleType);
        }
        
        _hasPendingUpdate = true;
        UpdateTelemetryAvailability();
    }

    #endregion

    #region Timer and Update Logic

    private void OnUpdateTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (!_hasPendingUpdate) return;
        _hasPendingUpdate = false;
        
        TelemetryModel snapshot;
        lock (_telemetryLock)
        {
            snapshot = _currentTelemetry.Clone();
        }
        
        // Raise consolidated update event
        TelemetryUpdated?.Invoke(this, snapshot);
        
        // Raise position changed if we have valid position
        if (snapshot.HasValidPosition)
        {
            PositionChanged?.Invoke(this, new PositionChangedEventArgs
            {
                Latitude = snapshot.Latitude,
                Longitude = snapshot.Longitude,
                AltitudeRelative = snapshot.AltitudeRelative,
                Heading = snapshot.Heading,
                GroundSpeed = snapshot.GroundSpeed,
                Timestamp = snapshot.LastPositionUpdate
            });
        }
        
        // Raise attitude changed
        AttitudeChanged?.Invoke(this, new AttitudeChangedEventArgs
        {
            Roll = snapshot.Roll,
            Pitch = snapshot.Pitch,
            Yaw = snapshot.Yaw,
            Timestamp = snapshot.LastAttitudeUpdate
        });
    }

    private void UpdateTelemetryAvailability()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastTelemetryAvailableCheck).TotalMilliseconds < 500)
            return;
        
        _lastTelemetryAvailableCheck = now;
        
        var wasReceiving = _isReceivingTelemetry;
        
        lock (_telemetryLock)
        {
            var lastUpdate = new[]
            {
                _currentTelemetry.LastPositionUpdate,
                _currentTelemetry.LastAttitudeUpdate,
                _currentTelemetry.LastHeartbeatUpdate
            }.Max();
            
            _isReceivingTelemetry = (now - lastUpdate).TotalMilliseconds < TelemetryTimeoutMs;
        }
        
        if (wasReceiving != _isReceivingTelemetry)
        {
            _logger.LogInformation("[TelemetryService] Telemetry availability changed: {Available}", _isReceivingTelemetry);
            TelemetryAvailabilityChanged?.Invoke(this, _isReceivingTelemetry);
        }
    }

    #endregion

    #region Connection Events

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        if (connected)
        {
            Start();
        }
        else
        {
            Stop();
            Clear();
        }
    }

    #endregion

    #region Helper Methods

    private static string GetFlightModeName(byte vehicleType, uint customMode)
    {
        // ArduCopter modes (vehicle type 2)
        if (vehicleType == 2)
        {
            return customMode switch
            {
                0 => "Stabilize",
                1 => "Acro",
                2 => "AltHold",
                3 => "Auto",
                4 => "Guided",
                5 => "Loiter",
                6 => "RTL",
                7 => "Circle",
                9 => "Land",
                11 => "Drift",
                13 => "Sport",
                14 => "Flip",
                15 => "AutoTune",
                16 => "PosHold",
                17 => "Brake",
                18 => "Throw",
                19 => "Avoid_ADSB",
                20 => "Guided_NoGPS",
                21 => "Smart_RTL",
                22 => "FlowHold",
                23 => "Follow",
                24 => "ZigZag",
                25 => "SystemId",
                26 => "Heli_Autorotate",
                27 => "Auto_RTL",
                _ => $"Mode {customMode}"
            };
        }
        
        // ArduPlane modes (vehicle type 1)
        if (vehicleType == 1)
        {
            return customMode switch
            {
                0 => "Manual",
                1 => "Circle",
                2 => "Stabilize",
                3 => "Training",
                4 => "Acro",
                5 => "FlyByWireA",
                6 => "FlyByWireB",
                7 => "Cruise",
                8 => "AutoTune",
                10 => "Auto",
                11 => "RTL",
                12 => "Loiter",
                14 => "Avoid_ADSB",
                15 => "Guided",
                17 => "QSTABILIZE",
                18 => "QHOVER",
                19 => "QLOITER",
                20 => "QLAND",
                21 => "QRTL",
                22 => "QAUTOTUNE",
                23 => "QACRO",
                _ => $"Mode {customMode}"
            };
        }
        
        return $"Mode {customMode}";
    }

    private static string GetVehicleTypeName(byte vehicleType)
    {
        return vehicleType switch
        {
            0 => "Generic",
            1 => "Fixed Wing",
            2 => "Copter",
            3 => "VTOL",
            4 => "Antenna Tracker",
            5 => "GCS",
            6 => "Airship",
            7 => "Free Balloon",
            8 => "Rocket",
            9 => "Ground Rover",
            10 => "Surface Boat",
            11 => "Submarine",
            12 => "Hexarotor",
            13 => "Octorotor",
            14 => "Tricopter",
            15 => "Flapping Wing",
            16 => "Kite",
            17 => "Onboard Companion",
            18 => "Two-Rotor VTOL",
            19 => "Quad-Rotor VTOL",
            20 => "Tilt-Rotor VTOL",
            21 => "Sim Vehicle",
            _ => $"Type {vehicleType}"
        };
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        Stop();
        _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
    }
}
