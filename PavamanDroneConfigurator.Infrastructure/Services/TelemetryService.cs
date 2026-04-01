using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models;
using System.Collections.Concurrent;
using System.Text;

namespace PavamanDroneConfigurator.Infrastructure.Services;

public sealed class TelemetryService : ITelemetryService, IDisposable
{
    private const string LogPrefix = "[TelemetrySM]";

    private static readonly EventId ConnectionStateEvent = new(4100, nameof(ConnectionStateEvent));
    private static readonly EventId TransitionEvent = new(4101, nameof(TransitionEvent));
    private static readonly EventId StreamRequestEvent = new(4102, nameof(StreamRequestEvent));
    private static readonly EventId StartupAttemptEvent = new(4103, nameof(StartupAttemptEvent));
    private static readonly EventId FirstFrameEvent = new(4104, nameof(FirstFrameEvent));
    private static readonly EventId StaleEvent = new(4105, nameof(StaleEvent));
    private static readonly EventId HealthSnapshotEvent = new(4106, nameof(HealthSnapshotEvent));
    private static readonly EventId ParseErrorEvent = new(4107, nameof(ParseErrorEvent));

    private readonly ILogger<TelemetryService> _logger;
    private readonly IConnectionService _connectionService;
    private readonly object _stateLock = new();
    private readonly object _lifecycleLock = new();
    private readonly SemaphoreSlim _streamRequestGate = new(1, 1);

    private readonly ConcurrentQueue<(double Lat, double Lon, double Alt, DateTime Time)> _flightPath = new();
    private readonly Queue<string> _transitionHistory = new();

    private TelemetryModel _currentTelemetry = new();
    private TelemetryServiceState _state = TelemetryServiceState.Disconnected;
    private TelemetryStaleReason _staleReason = TelemetryStaleReason.None;
    private bool _isReceivingTelemetry;
    private bool _disposed;
    private bool _started;
    private bool _isSubscribed;
    private bool _firstValidFrameLogged;
    private bool _hasPendingUpdate;

    private DateTime _lastAnyValidFrameUtc = DateTime.MinValue;
    private DateTime _lastHeartbeatUtc = DateTime.MinValue;
    private DateTime _lastPositionUtc = DateTime.MinValue;
    private DateTime _lastGpsUtc = DateTime.MinValue;
    private DateTime _lastAttitudeUtc = DateTime.MinValue;
    private DateTime _lastVfrUtc = DateTime.MinValue;
    private DateTime _lastSysStatusUtc = DateTime.MinValue;

    private DateTime _lastEmittedPositionUtc = DateTime.MinValue;
    private DateTime _lastEmittedAttitudeUtc = DateTime.MinValue;
    private DateTime _lastHealthSnapshotLogUtc = DateTime.MinValue;
    private DateTime _nextStreamRequestDueUtc = DateTime.MinValue;
    private DateTime _nextMaintenanceRequestDueUtc = DateTime.MinValue;
    private DateTime _lastPositionPathPointUtc = DateTime.MinValue;
    private string _lastValidFrameType = "None";
    private string _lastHealthSignature = string.Empty;

    private int _startupAttemptCount;
    private int _recoveryAttemptCount;
    private int _positionUpdateCount;
    private int _crcFailureCount;

    private CancellationTokenSource? _lifecycleCts;
    private Task? _stateLoopTask;
    private System.Timers.Timer? _updateTimer;

    private readonly TelemetryServiceOptions _options;
    private const int UpdateIntervalMs = 50; // 20 Hz – reduces perceived telemetry lag
    private const int MaxFlightPathPoints = 5000;

    private static readonly (int StreamId, int RateHz, string Name)[] StreamRequests =
    {
        (0, 4, "ALL"),
        (2, 2, "EXTENDED_STATUS"),
        (6, 4, "POSITION"),
        (10, 10, "EXTRA1"),
        (11, 4, "EXTRA2")
    };

    private static readonly (int MessageId, int RateHz, string Name)[] MessageIntervals =
    {
        (0, 1, "HEARTBEAT"),
        (1, 2, "SYS_STATUS"),
        (24, 2, "GPS_RAW_INT"),
        (30, 10, "ATTITUDE"),
        (33, 4, "GLOBAL_POSITION_INT"),
        (74, 4, "VFR_HUD")
    };

    public TelemetryService(ILogger<TelemetryService> logger, IConnectionService connectionService)
    {
        _logger = logger;
        _connectionService = connectionService;
        _options = TelemetryServiceOptions.Default;

        _connectionService.ConnectionStateChanged += OnConnectionStateChanged;
        
        // Auto-start if already connected
        if (_connectionService.IsConnected)
        {
            _logger.LogInformation("{Prefix} Already connected on construction, auto-starting", LogPrefix);
            Start();
        }
    }

    public TelemetryModel CurrentTelemetry
    {
        get
        {
            lock (_stateLock)
            {
                return _currentTelemetry.Clone();
            }
        }
    }

    public TelemetryServiceState CurrentState
    {
        get
        {
            lock (_stateLock)
            {
                return _state;
            }
        }
    }

    public TelemetryHealthStatus CurrentHealth => BuildHealthSnapshot(DateTime.UtcNow);

    public bool IsReceivingTelemetry => CurrentState == TelemetryServiceState.TelemetryActive;

    public bool HasValidPosition
    {
        get
        {
            lock (_stateLock)
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
    public event EventHandler<TelemetryHealthStatus>? TelemetryHealthChanged;

    public void Start()
    {
        lock (_lifecycleLock)
        {
            if (_disposed)
            {
                _logger.LogWarning("{Prefix} Cannot start - disposed", LogPrefix);
                return;
            }
            
            if (_started)
            {
                _logger.LogDebug("{Prefix} Already started", LogPrefix);
                return;
            }

            SubscribeTelemetryEvents();

            _lifecycleCts = new CancellationTokenSource();
            _updateTimer = new System.Timers.Timer(UpdateIntervalMs);
            _updateTimer.Elapsed += OnUpdateTimerElapsed;
            _updateTimer.Start();

            _stateLoopTask = Task.Run(() => RunStateLoopAsync(_lifecycleCts.Token));
            _started = true;
        }

        _logger.LogInformation(ConnectionStateEvent, "{Prefix} Started", LogPrefix);
        
        // If already connected, trigger connection handling
        if (_connectionService.IsConnected)
        {
            HandleConnected();
            RequestStreams();
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Task? loopTask;

        lock (_lifecycleLock)
        {
            if (!_started)
            {
                return;
            }

            cts = _lifecycleCts;
            loopTask = _stateLoopTask;
            _lifecycleCts = null;
            _stateLoopTask = null;

            _updateTimer?.Stop();
            _updateTimer?.Dispose();
            _updateTimer = null;

            UnsubscribeTelemetryEvents();
            _started = false;
        }

        cts?.Cancel();
        try
        {
            loopTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "{Prefix} State loop stop wait interrupted", LogPrefix);
        }
        finally
        {
            cts?.Dispose();
        }

        TransitionState(TelemetryServiceState.Disconnected, "service stop", TelemetryStaleReason.None);
    }

    public void Clear()
    {
        lock (_stateLock)
        {
            _currentTelemetry = new TelemetryModel();
            _hasPendingUpdate = false;
            _firstValidFrameLogged = false;
            _lastAnyValidFrameUtc = DateTime.MinValue;
            _lastHeartbeatUtc = DateTime.MinValue;
            _lastPositionUtc = DateTime.MinValue;
            _lastGpsUtc = DateTime.MinValue;
            _lastAttitudeUtc = DateTime.MinValue;
            _lastVfrUtc = DateTime.MinValue;
            _lastSysStatusUtc = DateTime.MinValue;
            _lastValidFrameType = "None";
            _startupAttemptCount = 0;
            _recoveryAttemptCount = 0;
            _positionUpdateCount = 0;
            _crcFailureCount = 0;
            _nextStreamRequestDueUtc = DateTime.MinValue;
            _nextMaintenanceRequestDueUtc = DateTime.MinValue;
            _lastHealthSnapshotLogUtc = DateTime.MinValue;
            _lastEmittedPositionUtc = DateTime.MinValue;
            _lastEmittedAttitudeUtc = DateTime.MinValue;
            _lastPositionPathPointUtc = DateTime.MinValue;
        }

        // Preserve flight path data so it remains visible after disconnect.
        // Flight path is only cleared explicitly by the user via ClearFlightPath().
        PublishHealthIfChanged();
    }

    public void RequestStreams()
    {
        if (!_connectionService.IsConnected)
        {
            _logger.LogWarning(StreamRequestEvent, "{Prefix} RequestStreams ignored, disconnected", LogPrefix);
            return;
        }

        // Ensure we're started
        if (!_started)
        {
            Start();
        }

        lock (_stateLock)
        {
            _nextStreamRequestDueUtc = DateTime.UtcNow;
        }

        TransitionState(TelemetryServiceState.NegotiatingStreams, "manual stream request", TelemetryStaleReason.None);

        var ct = _lifecycleCts?.Token ?? CancellationToken.None;
        _ = Task.Run(() => SendStreamNegotiationBurstAsync("manual request", ct));
    }

    public IReadOnlyList<(double Latitude, double Longitude, double Altitude, DateTime Timestamp)> GetFlightPath()
    {
        return _flightPath.ToArray().Select(p => (p.Lat, p.Lon, p.Alt, p.Time)).ToList();
    }

    public void ClearFlightPath()
    {
        while (_flightPath.TryDequeue(out _))
        {
        }
    }

    public string GetTelemetryDebugDump()
    {
        var snapshot = BuildHealthSnapshot(DateTime.UtcNow);
        List<string> transitions;
        TelemetryModel currentTelemetry;
        lock (_stateLock)
        {
            transitions = _transitionHistory.ToList();
            currentTelemetry = _currentTelemetry.Clone();
        }

        var builder = new StringBuilder();
        builder.AppendLine("=== Telemetry Debug Dump ===");
        builder.AppendLine($"State: {snapshot.State}");
        builder.AppendLine($"Connected: {snapshot.IsConnected}");
        builder.AppendLine($"Receiving: {snapshot.IsReceivingTelemetry}");
        builder.AppendLine($"StaleReason: {snapshot.StaleReason}");
        builder.AppendLine($"LastValidFrame: {snapshot.LastValidFrameType} @ {snapshot.LastValidFrameTime:O}");
        builder.AppendLine($"Position Updates: {_positionUpdateCount}");
        builder.AppendLine($"Current Position: Lat={currentTelemetry.Latitude:F6}, Lon={currentTelemetry.Longitude:F6}");
        builder.AppendLine($"HasValidPosition: {currentTelemetry.HasValidPosition}");
        builder.AppendLine($"GpsFixType: {currentTelemetry.GpsFixType}");
        builder.AppendLine($"Ages(s): hb={snapshot.HeartbeatAge.TotalSeconds:F1}, pos={snapshot.PositionAge.TotalSeconds:F1}, gps={snapshot.GpsAge.TotalSeconds:F1}, att={snapshot.AttitudeAge.TotalSeconds:F1}, vfr={snapshot.VfrAge.TotalSeconds:F1}, sys={snapshot.SysStatusAge.TotalSeconds:F1}");
        builder.AppendLine($"Retries: startup={snapshot.StartupAttemptCount}, recovery={snapshot.RecoveryAttemptCount}");
        builder.AppendLine($"CRC: failures={snapshot.CrcFailureCount}, lastFrameValid={snapshot.LastCrcValid}");
        builder.AppendLine("Recent transitions:");
        foreach (var transition in transitions)
        {
            builder.AppendLine($" - {transition}");
        }

        return builder.ToString();
    }

    private async Task RunStateLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ProcessStateLoopTickAsync(ct);
                await Task.Delay(_options.StateLoopInterval, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Prefix} State loop error", LogPrefix);
            }
        }
    }

    private async Task ProcessStateLoopTickAsync(CancellationToken ct)
    {
        if (!_connectionService.IsConnected)
        {
            return;
        }

        var now = DateTime.UtcNow;

        if (ShouldTransitionToStale(now, out var staleReason))
        {
            TransitionState(TelemetryServiceState.TelemetryStaleRecovering, "stale timeout exceeded", staleReason);
            lock (_stateLock)
            {
                _nextStreamRequestDueUtc = now;
            }
        }

        if (TryScheduleBurst(now, out var reason, out var attempt, out var isStartup))
        {
            if (isStartup)
            {
                _logger.LogInformation(StartupAttemptEvent, "{Prefix} Startup attempt #{Attempt} ({Reason})", LogPrefix, attempt, reason);
            }

            await SendStreamNegotiationBurstAsync(reason, ct);
        }

        if (ShouldLogHealthSnapshot(now))
        {
            LogHealthSnapshot(now);
        }
    }

    private bool TryScheduleBurst(DateTime now, out string reason, out int attempt, out bool isStartup)
    {
        reason = string.Empty;
        attempt = 0;
        isStartup = false;

        lock (_stateLock)
        {
            if (_state == TelemetryServiceState.ConnectedAwaitingTelemetry && now >= _nextStreamRequestDueUtc)
            {
                _state = TelemetryServiceState.NegotiatingStreams;
                _startupAttemptCount++;
                attempt = _startupAttemptCount;
                isStartup = true;
                reason = "startup immediate";
                _nextStreamRequestDueUtc = now + (_startupAttemptCount < _options.MaxRapidStartupRetries
                    ? _options.StartupRetryInterval
                    : _options.SlowRetryInterval);
                AddTransitionUnsafe(TelemetryServiceState.ConnectedAwaitingTelemetry, TelemetryServiceState.NegotiatingStreams, "initial burst");
                return true;
            }

            if (_state == TelemetryServiceState.NegotiatingStreams && now >= _nextStreamRequestDueUtc)
            {
                _startupAttemptCount++;
                attempt = _startupAttemptCount;
                isStartup = true;
                reason = _startupAttemptCount <= _options.MaxRapidStartupRetries ? "startup retry" : "startup low-frequency retry";
                _nextStreamRequestDueUtc = now + (_startupAttemptCount < _options.MaxRapidStartupRetries
                    ? _options.StartupRetryInterval
                    : _options.SlowRetryInterval);
                return true;
            }

            if (_state == TelemetryServiceState.TelemetryActive && now >= _nextMaintenanceRequestDueUtc)
            {
                reason = "active maintenance refresh";
                _nextMaintenanceRequestDueUtc = now + _options.MaintenanceRefreshInterval;
                return true;
            }

            if (_state == TelemetryServiceState.TelemetryStaleRecovering && now >= _nextStreamRequestDueUtc)
            {
                _recoveryAttemptCount++;
                attempt = _recoveryAttemptCount;
                reason = "stale recovery";
                _nextStreamRequestDueUtc = now + _options.RecoveryRetryInterval;
                return true;
            }
        }

        return false;
    }

    private bool ShouldTransitionToStale(DateTime now, out TelemetryStaleReason staleReason)
    {
        staleReason = TelemetryStaleReason.None;
        lock (_stateLock)
        {
            if (_state != TelemetryServiceState.TelemetryActive || _lastAnyValidFrameUtc == DateTime.MinValue)
            {
                return false;
            }

            if (now - _lastAnyValidFrameUtc <= _options.StaleTimeout)
            {
                return false;
            }

            staleReason = ComputeStaleReasonUnsafe(now);
            return true;
        }
    }

    private bool ShouldLogHealthSnapshot(DateTime now)
    {
        lock (_stateLock)
        {
            if (!_connectionService.IsConnected)
            {
                return false;
            }

            if (_lastHealthSnapshotLogUtc != DateTime.MinValue && now - _lastHealthSnapshotLogUtc < _options.HealthSnapshotInterval)
            {
                return false;
            }

            _lastHealthSnapshotLogUtc = now;
            return true;
        }
    }

    private void LogHealthSnapshot(DateTime now)
    {
        var health = BuildHealthSnapshot(now);
        TelemetryModel currentTelemetry;
        lock (_stateLock)
        {
            currentTelemetry = _currentTelemetry.Clone();
        }
        
        _logger.LogInformation(HealthSnapshotEvent,
            "{Prefix} Health: state={State}, pos=({Lat:F4},{Lon:F4}), gps={GpsFix}, ages(s): hb={Hb:F1}, pos={Pos:F1}, gps={Gps:F1}, att={Att:F1}",
            LogPrefix,
            health.State,
            currentTelemetry.Latitude,
            currentTelemetry.Longitude,
            currentTelemetry.GpsFixType,
            health.HeartbeatAge.TotalSeconds,
            health.PositionAge.TotalSeconds,
            health.GpsAge.TotalSeconds,
            health.AttitudeAge.TotalSeconds);
    }

    private async Task SendStreamNegotiationBurstAsync(string reason, CancellationToken ct)
    {
        if (!_connectionService.IsConnected)
        {
            return;
        }

        var gateTaken = false;
        try
        {
            gateTaken = await _streamRequestGate.WaitAsync(TimeSpan.FromSeconds(1), ct);
            if (!gateTaken)
            {
                return;
            }

            _logger.LogInformation(StreamRequestEvent, "{Prefix} Sending ASV negotiation burst ({Reason})", LogPrefix, reason);

            foreach (var stream in StreamRequests)
            {
                _connectionService.SendTelemetryNegotiationCommand(
                    TelemetryNegotiationCommand.ForDataStream(stream.StreamId, stream.RateHz, 1, stream.Name));
                await Task.Delay(_options.BurstCommandGap, ct);
            }

            foreach (var message in MessageIntervals)
            {
                _connectionService.SendTelemetryNegotiationCommand(
                    TelemetryNegotiationCommand.ForMessageInterval(message.MessageId, message.RateHz, message.Name));
                await Task.Delay(_options.BurstCommandGap, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Prefix} Stream negotiation burst failed ({Reason})", LogPrefix, reason);
        }
        finally
        {
            if (gateTaken)
            {
                _streamRequestGate.Release();
            }
        }
    }

    private void OnGlobalPositionInt(object? sender, GlobalPositionIntEventArgs e)
    {
        try
        {
            var now = DateTime.UtcNow;
            
            // ALWAYS log first few position messages for debugging
            _positionUpdateCount++;
            if (_positionUpdateCount <= 5 || _positionUpdateCount % 50 == 0)
            {
                _logger.LogInformation("{Prefix} GLOBAL_POSITION_INT #{Count}: lat={Lat:F6}, lon={Lon:F6}, alt={Alt:F1}m",
                    LogPrefix, _positionUpdateCount, e.Latitude, e.Longitude, e.AltitudeRelative);
            }
            
            // Accept ALL position frames from SITL - don't validate too strictly
            // Only reject truly invalid data (NaN, infinity, or clearly wrong)
            if (!double.IsFinite(e.Latitude) || !double.IsFinite(e.Longitude))
            {
                _logger.LogWarning("{Prefix} Invalid position: non-finite values", LogPrefix);
                return;
            }

            lock (_stateLock)
            {
                _currentTelemetry.Latitude = e.Latitude;
                _currentTelemetry.Longitude = e.Longitude;
                _currentTelemetry.AltitudeMsl = e.AltitudeMsl;
                _currentTelemetry.AltitudeRelative = e.AltitudeRelative;
                _currentTelemetry.GroundSpeedX = e.VelocityX;
                _currentTelemetry.GroundSpeedY = e.VelocityY;
                _currentTelemetry.VerticalSpeed = e.VelocityZ;
                if (!double.IsNaN(e.Heading) && e.Heading is >= 0 and <= 360)
                {
                    _currentTelemetry.Heading = e.Heading;
                }
                _currentTelemetry.LastPositionUpdate = now;
                _lastPositionUtc = now;
                _hasPendingUpdate = true;
            }

            AddFlightPathPoint(e, now);
            TrackCrcStatus("GLOBAL_POSITION_INT", e.CrcValid);
            MarkValidFrame("GLOBAL_POSITION_INT", now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ParseErrorEvent, ex,
                "{Prefix} Parse failure msg=33 lat={Lat} lon={Lon} alt={Alt}",
                LogPrefix, e.Latitude, e.Longitude, e.AltitudeRelative);
        }
    }

    private void OnAttitude(object? sender, AttitudeEventArgs e)
    {
        try
        {
            if (!IsValidAttitudeFrame(e))
            {
                return;
            }

            var now = DateTime.UtcNow;
            lock (_stateLock)
            {
                _currentTelemetry.Roll = e.Roll;
                _currentTelemetry.Pitch = e.Pitch;
                _currentTelemetry.Yaw = NormalizeYaw(e.Yaw);
                _currentTelemetry.RollSpeed = e.RollSpeed;
                _currentTelemetry.PitchSpeed = e.PitchSpeed;
                _currentTelemetry.YawSpeed = e.YawSpeed;
                _currentTelemetry.LastAttitudeUpdate = now;
                _lastAttitudeUtc = now;
                _hasPendingUpdate = true;
            }

            TrackCrcStatus("ATTITUDE", e.CrcValid);
            MarkValidFrame("ATTITUDE", now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ParseErrorEvent, ex,
                "{Prefix} Parse failure msg=30 roll={Roll} pitch={Pitch} yaw={Yaw}",
                LogPrefix, e.Roll, e.Pitch, e.Yaw);
        }
    }

    private void OnVfrHud(object? sender, VfrHudEventArgs e)
    {
        try
        {
            if (!IsValidVfrFrame(e))
            {
                return;
            }

            var now = DateTime.UtcNow;
            lock (_stateLock)
            {
                _currentTelemetry.Airspeed = e.Airspeed;
                _currentTelemetry.GroundSpeed = e.GroundSpeed;
                _currentTelemetry.Throttle = Math.Clamp(e.Throttle, 0, 100);
                _currentTelemetry.ClimbRate = e.ClimbRate;
                _lastVfrUtc = now;
                _hasPendingUpdate = true;
            }

            TrackCrcStatus("VFR_HUD", e.CrcValid);
            MarkValidFrame("VFR_HUD", now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ParseErrorEvent, ex,
                "{Prefix} Parse failure msg=74 gnd={Ground} air={Air} thr={Throttle}",
                LogPrefix, e.GroundSpeed, e.Airspeed, e.Throttle);
        }
    }

    private void OnGpsRawInt(object? sender, GpsRawIntEventArgs e)
    {
        try
        {
            // Accept all GPS frames - SITL may report unusual values
            var now = DateTime.UtcNow;
            lock (_stateLock)
            {
                _currentTelemetry.GpsFixType = e.FixType;
                _currentTelemetry.SatelliteCount = e.SatellitesVisible;
                if (!double.IsNaN(e.Hdop) && e.Hdop >= 0)
                {
                    _currentTelemetry.Hdop = e.Hdop;
                }
                if (!double.IsNaN(e.Vdop) && e.Vdop >= 0)
                {
                    _currentTelemetry.Vdop = e.Vdop;
                }
                _currentTelemetry.GpsAltitude = e.Altitude;
                _currentTelemetry.LastGpsUpdate = now;
                _lastGpsUtc = now;
                _hasPendingUpdate = true;
            }

            GpsStatusChanged?.Invoke(this, new GpsStatusEventArgs
            {
                FixType = e.FixType,
                SatelliteCount = e.SatellitesVisible,
                Hdop = double.IsNaN(e.Hdop) ? 0 : e.Hdop,
                HasValidFix = e.FixType >= 1 // Accept any GPS activity for SITL
            });

            TrackCrcStatus("GPS_RAW_INT", e.CrcValid);
            MarkValidFrame("GPS_RAW_INT", now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ParseErrorEvent, ex,
                "{Prefix} Parse failure msg=24 fix={Fix} sats={Sats} hdop={Hdop} vdop={Vdop}",
                LogPrefix, e.FixType, e.SatellitesVisible, e.Hdop, e.Vdop);
        }
    }

    private void OnSysStatus(object? sender, SysStatusEventArgs e)
    {
        try
        {
            // Accept all sys status - SITL may have unusual battery values
            var now = DateTime.UtcNow;
            lock (_stateLock)
            {
                _currentTelemetry.BatteryVoltage = e.BatteryVoltage;
                if (!double.IsNaN(e.BatteryCurrent))
                {
                    _currentTelemetry.BatteryCurrent = e.BatteryCurrent;
                }
                _currentTelemetry.BatteryRemaining = e.BatteryRemaining;
                _lastSysStatusUtc = now;
                _hasPendingUpdate = true;
            }

            BatteryStatusChanged?.Invoke(this, new BatteryStatusEventArgs
            {
                Voltage = e.BatteryVoltage,
                Current = double.IsNaN(e.BatteryCurrent) ? -1 : e.BatteryCurrent,
                RemainingPercent = e.BatteryRemaining
            });

            TrackCrcStatus("SYS_STATUS", e.CrcValid);
            MarkValidFrame("SYS_STATUS", now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ParseErrorEvent, ex,
                "{Prefix} Parse failure msg=1 voltage={Voltage} current={Current} remaining={Remaining}",
                LogPrefix, e.BatteryVoltage, e.BatteryCurrent, e.BatteryRemaining);
        }
    }

    private void OnHeartbeatData(object? sender, HeartbeatDataEventArgs e)
    {
        try
        {
            if (!IsValidHeartbeatFrame(e))
            {
                return;
            }

            var now = DateTime.UtcNow;
            lock (_stateLock)
            {
                _currentTelemetry.IsArmed = e.IsArmed;
                _currentTelemetry.FlightMode = GetFlightModeName(e.VehicleType, e.CustomMode);
                _currentTelemetry.VehicleType = GetVehicleTypeName(e.VehicleType);
                _currentTelemetry.SystemStatus = GetSystemStatusName(e.SystemStatus);
                _currentTelemetry.LastHeartbeatUpdate = now;
                _lastHeartbeatUtc = now;
                _hasPendingUpdate = true;
            }

            TrackCrcStatus("HEARTBEAT", e.CrcValid);
            MarkValidFrame("HEARTBEAT", now);
        }
        catch (Exception ex)
        {
            _logger.LogError(ParseErrorEvent, ex,
                "{Prefix} Parse failure msg=0 vehicle={Vehicle} mode={Mode} status={Status}",
                LogPrefix, e.VehicleType, e.CustomMode, e.SystemStatus);
        }
    }

    private void OnUpdateTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        TelemetryModel snapshot;
        bool hasUpdate;
        DateTime positionTimestamp;
        DateTime attitudeTimestamp;

        lock (_stateLock)
        {
            hasUpdate = _hasPendingUpdate;
            if (!hasUpdate)
            {
                return;
            }

            _hasPendingUpdate = false;
            snapshot = _currentTelemetry.Clone();
            positionTimestamp = snapshot.LastPositionUpdate;
            attitudeTimestamp = snapshot.LastAttitudeUpdate;
        }

        // ALWAYS emit TelemetryUpdated when we have pending updates
        TelemetryUpdated?.Invoke(this, snapshot);

        // ALWAYS emit PositionChanged if we have a position update - NO VALIDATION
        if (positionTimestamp > _lastEmittedPositionUtc)
        {
            _lastEmittedPositionUtc = positionTimestamp;
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

        if (attitudeTimestamp > _lastEmittedAttitudeUtc)
        {
            _lastEmittedAttitudeUtc = attitudeTimestamp;
            AttitudeChanged?.Invoke(this, new AttitudeChangedEventArgs
            {
                Roll = snapshot.Roll,
                Pitch = snapshot.Pitch,
                Yaw = snapshot.Yaw,
                Timestamp = snapshot.LastAttitudeUpdate
            });
        }
    }

    private void OnConnectionStateChanged(object? sender, bool connected)
    {
        _logger.LogInformation(ConnectionStateEvent, "{Prefix} Connection state changed: {Connected}", LogPrefix, connected);

        if (connected)
        {
            // Auto-start on connection
            Start();
            return;
        }

        HandleDisconnected();
    }

    private void HandleConnected()
    {
        var now = DateTime.UtcNow;
        lock (_stateLock)
        {
            _startupAttemptCount = 0;
            _recoveryAttemptCount = 0;
            _positionUpdateCount = 0;
            _firstValidFrameLogged = false;
            _nextStreamRequestDueUtc = now;
            _nextMaintenanceRequestDueUtc = now + _options.MaintenanceRefreshInterval;
        }

        TransitionState(TelemetryServiceState.ConnectedAwaitingTelemetry, "connection established", TelemetryStaleReason.None);
    }

    private void HandleDisconnected()
    {
        Stop();
        Clear();
        TransitionState(TelemetryServiceState.Disconnected, "connection lost", TelemetryStaleReason.None);
    }

    private void MarkValidFrame(string messageType, DateTime now)
    {
        var shouldActivate = false;
        var shouldLogFirst = false;

        lock (_stateLock)
        {
            if (_lastAnyValidFrameUtc == DateTime.MinValue)
            {
                shouldLogFirst = !_firstValidFrameLogged;
                _firstValidFrameLogged = true;
            }

            _lastAnyValidFrameUtc = now;
            _lastValidFrameType = messageType;

            if (_state is TelemetryServiceState.NegotiatingStreams or TelemetryServiceState.TelemetryStaleRecovering or TelemetryServiceState.ConnectedAwaitingTelemetry)
            {
                shouldActivate = HasActivationCriteriaUnsafe(now);
            }
        }

        if (shouldLogFirst)
        {
            var messageId = GetMessageIdFromType(messageType);
            _logger.LogInformation(FirstFrameEvent,
                "{Prefix} First valid frame: msgId={MessageId} type={Type}",
                LogPrefix,
                messageId,
                messageType);
        }

        if (shouldActivate)
        {
            TransitionState(TelemetryServiceState.TelemetryActive, $"recovered by {messageType}", TelemetryStaleReason.None);
            lock (_stateLock)
            {
                _nextMaintenanceRequestDueUtc = now + _options.MaintenanceRefreshInterval;
                _recoveryAttemptCount = 0;
            }
        }
    }

    /// <summary>
    /// Records the CRC validation outcome for the received telemetry frame.
    /// When <paramref name="crcValid"/> is <c>false</c> the failure counter is
    /// incremented and a warning is logged, but the data itself is accepted so
    /// the telemetry stream is never interrupted.
    /// </summary>
    private void TrackCrcStatus(string messageType, bool crcValid)
    {
        lock (_stateLock)
        {
            _currentTelemetry.LastCrcValid = crcValid;
            if (!crcValid)
            {
                _crcFailureCount++;
                _currentTelemetry.CrcFailureCount = _crcFailureCount;
            }
        }

        if (!crcValid)
        {
            _logger.LogWarning(ParseErrorEvent,
                "{Prefix} CRC validation failed for {MsgType} frame (total failures: {Count}) - frame accepted to keep telemetry stream flowing",
                LogPrefix, messageType, _crcFailureCount);
        }
    }

    private bool HasActivationCriteriaUnsafe(DateTime now)
    {
        // For SITL compatibility: activate telemetry if we have ANY valid frame recently
        // Don't require heartbeat for activation - some setups may not send it first
        var hasAnyRecentFrame = _lastAnyValidFrameUtc != DateTime.MinValue && (now - _lastAnyValidFrameUtc) <= _options.StartupTimeout;
        return hasAnyRecentFrame;
    }

    private void TransitionState(TelemetryServiceState newState, string reason, TelemetryStaleReason staleReason)
    {
        TelemetryServiceState oldState;
        bool availabilityChanged;
        bool shouldRaise;

        lock (_stateLock)
        {
            oldState = _state;
            availabilityChanged = _isReceivingTelemetry != (newState == TelemetryServiceState.TelemetryActive);
            shouldRaise = oldState != newState || _staleReason != staleReason || availabilityChanged;

            if (!shouldRaise)
            {
                return;
            }

            _state = newState;
            _staleReason = staleReason;
            _isReceivingTelemetry = newState == TelemetryServiceState.TelemetryActive;
            AddTransitionUnsafe(oldState, newState, reason);
        }

        _logger.LogInformation(TransitionEvent,
            "{Prefix} Transition {From} -> {To}, reason={Reason}, stale={Stale}",
            LogPrefix, oldState, newState, reason, staleReason);

        if (availabilityChanged)
        {
            TelemetryAvailabilityChanged?.Invoke(this, _isReceivingTelemetry);
        }

        if (newState == TelemetryServiceState.TelemetryStaleRecovering)
        {
            _logger.LogWarning(StaleEvent, "{Prefix} Entered stale recovery ({Reason})", LogPrefix, staleReason);
        }

        PublishHealthIfChanged();
    }

    private void PublishHealthIfChanged()
    {
        var health = BuildHealthSnapshot(DateTime.UtcNow);
        var signature = $"{health.State}|{health.StaleReason}|{health.IsConnected}|{health.IsReceivingTelemetry}|{health.LastValidFrameType}";

        lock (_stateLock)
        {
            if (_lastHealthSignature == signature)
            {
                return;
            }

            _lastHealthSignature = signature;
        }

        TelemetryHealthChanged?.Invoke(this, health);
    }

    private TelemetryHealthStatus BuildHealthSnapshot(DateTime now)
    {
        lock (_stateLock)
        {
            return new TelemetryHealthStatus
            {
                State = _state,
                IsConnected = _connectionService.IsConnected,
                IsReceivingTelemetry = _state == TelemetryServiceState.TelemetryActive,
                StaleReason = _staleReason,
                LastValidFrameType = _lastValidFrameType,
                LastValidFrameTime = _lastAnyValidFrameUtc,
                LastHeartbeatTime = _lastHeartbeatUtc,
                LastPositionTime = _lastPositionUtc,
                LastGpsTime = _lastGpsUtc,
                LastAttitudeTime = _lastAttitudeUtc,
                LastVfrTime = _lastVfrUtc,
                LastSysStatusTime = _lastSysStatusUtc,
                HeartbeatAge = GetAge(now, _lastHeartbeatUtc),
                PositionAge = GetAge(now, _lastPositionUtc),
                GpsAge = GetAge(now, _lastGpsUtc),
                AttitudeAge = GetAge(now, _lastAttitudeUtc),
                VfrAge = GetAge(now, _lastVfrUtc),
                SysStatusAge = GetAge(now, _lastSysStatusUtc),
                StartupAttemptCount = _startupAttemptCount,
                RecoveryAttemptCount = _recoveryAttemptCount,
                CrcFailureCount = _crcFailureCount,
                LastCrcValid = _currentTelemetry.LastCrcValid
            };
        }
    }

    private TelemetryStaleReason ComputeStaleReasonUnsafe(DateTime now)
    {
        if (!IsFreshUnsafe(_lastHeartbeatUtc, now, _options.StaleTimeout)) return TelemetryStaleReason.NoHeartbeat;
        if (!IsFreshUnsafe(_lastPositionUtc, now, _options.StaleTimeout)) return TelemetryStaleReason.NoPosition;
        if (!IsFreshUnsafe(_lastAttitudeUtc, now, _options.StaleTimeout)) return TelemetryStaleReason.NoAttitude;
        if (!IsFreshUnsafe(_lastGpsUtc, now, _options.StaleTimeout)) return TelemetryStaleReason.NoGps;
        if (!IsFreshUnsafe(_lastVfrUtc, now, _options.StaleTimeout)) return TelemetryStaleReason.NoVfr;
        if (!IsFreshUnsafe(_lastSysStatusUtc, now, _options.StaleTimeout)) return TelemetryStaleReason.NoSysStatus;
        return TelemetryStaleReason.NoRecentFrames;
    }

    private void AddTransitionUnsafe(TelemetryServiceState from, TelemetryServiceState to, string reason)
    {
        var line = $"{DateTime.UtcNow:O} {from} -> {to} ({reason})";
        _transitionHistory.Enqueue(line);
        while (_transitionHistory.Count > _options.TransitionHistorySize)
        {
            _transitionHistory.Dequeue();
        }
    }

    private void AddFlightPathPoint(GlobalPositionIntEventArgs e, DateTime now)
    {
        if ((now - _lastPositionPathPointUtc) < _options.FlightPathMinInterval)
        {
            return;
        }

        // Only skip truly zero coordinates
        if (e.Latitude == 0.0 && e.Longitude == 0.0)
        {
            return;
        }

        _lastPositionPathPointUtc = now;

        while (_flightPath.Count >= MaxFlightPathPoints)
        {
            _flightPath.TryDequeue(out _);
        }

        _flightPath.Enqueue((e.Latitude, e.Longitude, e.AltitudeRelative, now));
    }

    private void SubscribeTelemetryEvents()
    {
        if (_isSubscribed)
        {
            return;
        }

        _connectionService.GlobalPositionIntReceived += OnGlobalPositionInt;
        _connectionService.AttitudeReceived += OnAttitude;
        _connectionService.VfrHudReceived += OnVfrHud;
        _connectionService.GpsRawIntReceived += OnGpsRawInt;
        _connectionService.SysStatusReceived += OnSysStatus;
        _connectionService.HeartbeatDataReceived += OnHeartbeatData;
        _isSubscribed = true;
        
        _logger.LogInformation("{Prefix} Subscribed to telemetry events", LogPrefix);
    }

    private void UnsubscribeTelemetryEvents()
    {
        if (!_isSubscribed)
        {
            return;
        }

        _connectionService.GlobalPositionIntReceived -= OnGlobalPositionInt;
        _connectionService.AttitudeReceived -= OnAttitude;
        _connectionService.VfrHudReceived -= OnVfrHud;
        _connectionService.GpsRawIntReceived -= OnGpsRawInt;
        _connectionService.SysStatusReceived -= OnSysStatus;
        _connectionService.HeartbeatDataReceived -= OnHeartbeatData;
        _isSubscribed = false;
        
        _logger.LogInformation("{Prefix} Unsubscribed from telemetry events", LogPrefix);
    }

    private static bool IsValidHeartbeatFrame(HeartbeatDataEventArgs e)
    {
        return e.VehicleType <= 30 && e.SystemId != 0;
    }

    private static bool IsValidAttitudeFrame(AttitudeEventArgs e)
    {
        return double.IsFinite(e.Roll)
               && double.IsFinite(e.Pitch)
               && double.IsFinite(e.Yaw);
    }

    private static bool IsValidVfrFrame(VfrHudEventArgs e)
    {
        return double.IsFinite(e.Airspeed)
               && double.IsFinite(e.GroundSpeed)
               && double.IsFinite(e.ClimbRate);
    }

    private static bool IsFreshUnsafe(DateTime timestamp, DateTime now, TimeSpan threshold)
    {
        return timestamp != DateTime.MinValue && now - timestamp <= threshold;
    }

    private static TimeSpan GetAge(DateTime now, DateTime timestamp)
    {
        return timestamp == DateTime.MinValue ? TimeSpan.MaxValue : now - timestamp;
    }

    private static double NormalizeYaw(double yaw)
    {
        if (yaw < 0)
        {
            yaw += 360;
        }

        if (yaw >= 360)
        {
            yaw %= 360;
        }

        return yaw;
    }

    private static int GetMessageIdFromType(string messageType)
    {
        return messageType switch
        {
            "HEARTBEAT" => 0,
            "SYS_STATUS" => 1,
            "GPS_RAW_INT" => 24,
            "ATTITUDE" => 30,
            "GLOBAL_POSITION_INT" => 33,
            "VFR_HUD" => 74,
            _ => -1
        };
    }

    private static string GetSystemStatusName(byte systemStatus)
    {
        return systemStatus switch
        {
            0 => "Uninitialized",
            1 => "Boot",
            2 => "Calibrating",
            3 => "Standby",
            4 => "Active",
            5 => "Critical",
            6 => "Emergency",
            7 => "Power Off",
            8 => "Terminated",
            _ => $"Status {systemStatus}"
        };
    }

    private static string GetFlightModeName(byte vehicleType, uint customMode)
    {
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _connectionService.ConnectionStateChanged -= OnConnectionStateChanged;
        _streamRequestGate.Dispose();
    }
}

internal sealed class TelemetryServiceOptions
{
    public static TelemetryServiceOptions Default { get; } = new();

    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan StaleTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan StartupRetryInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan RecoveryRetryInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan SlowRetryInterval { get; init; } = TimeSpan.FromSeconds(12);
    public TimeSpan MaintenanceRefreshInterval { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan HealthSnapshotInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan StateLoopInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan FlightPathMinInterval { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan BurstCommandGap { get; init; } = TimeSpan.FromMilliseconds(60);
    public int MaxRapidStartupRetries { get; init; } = 5;
    public int TransitionHistorySize { get; init; } = 30;
}
