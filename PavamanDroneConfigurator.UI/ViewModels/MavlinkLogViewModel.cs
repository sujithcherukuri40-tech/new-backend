using System;
using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.UI.ViewModels;

/// <summary>
/// ViewModel for the MAVLink log viewer panel.
/// Captures and displays real-time MAVLink message traffic from the drone.
/// </summary>
public partial class MavlinkLogViewModel : ObservableObject, IDisposable
{
    private const int MaxLogEntries = 2000;

    private readonly IConnectionService _connectionService;
    private readonly ObservableCollection<string> _logEntries = new();
    private static readonly ILogger<MavlinkLogViewModel> _logger =
        LoggerFactory.Create(b => b.AddConsole()).CreateLogger<MavlinkLogViewModel>();

    [ObservableProperty]
    private bool _isPaused;

    public ObservableCollection<string> LogEntries => _logEntries;

    public MavlinkLogViewModel(IConnectionService connectionService)
    {
        _connectionService = connectionService;

        // Subscribe to all telemetry events to build the log
        _connectionService.HeartbeatDataReceived += OnHeartbeatDataReceived;
        _connectionService.SysStatusReceived += OnSysStatusReceived;
        _connectionService.GpsRawIntReceived += OnGpsRawIntReceived;
        _connectionService.AttitudeReceived += OnAttitudeReceived;
        _connectionService.GlobalPositionIntReceived += OnGlobalPositionIntReceived;
        _connectionService.VfrHudReceived += OnVfrHudReceived;
        _connectionService.StatusTextReceived += OnStatusTextReceived;
        _connectionService.CommandAckReceived += OnCommandAckReceived;
    }

    private void AppendEntry(string message)
    {
        if (IsPaused) return;
        Dispatcher.UIThread.Post(() =>
        {
            var entry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            _logEntries.Add(entry);
            if (_logEntries.Count > MaxLogEntries)
                _logEntries.RemoveAt(0);
        });
    }

    private void OnHeartbeatDataReceived(object? sender, HeartbeatDataEventArgs e)
    {
        AppendEntry($"#000 HEARTBEAT          SYS:{e.SystemId} COMP:{e.ComponentId} armed={e.IsArmed} mode={e.CustomMode}");
    }

    private void OnSysStatusReceived(object? sender, SysStatusEventArgs e)
    {
        AppendEntry($"#001 SYS_STATUS         SYS:1 COMP:1 volt={e.BatteryVoltage:F2}V curr={e.BatteryCurrent:F1}A rem={e.BatteryRemaining}%");
    }

    private void OnGpsRawIntReceived(object? sender, GpsRawIntEventArgs e)
    {
        AppendEntry($"#024 GPS_RAW_INT        SYS:1 COMP:1 fix={e.FixType} sats={e.SatellitesVisible} hdop={e.Hdop:F1}");
    }

    private void OnAttitudeReceived(object? sender, AttitudeEventArgs e)
    {
        AppendEntry($"#030 ATTITUDE           SYS:1 COMP:1 roll={e.Roll:F2} pitch={e.Pitch:F2} yaw={e.Yaw:F2}");
    }

    private void OnGlobalPositionIntReceived(object? sender, GlobalPositionIntEventArgs e)
    {
        AppendEntry($"#033 GLOBAL_POSITION_INT SYS:1 COMP:1 lat={e.Latitude:F5} lon={e.Longitude:F5} alt={e.AltitudeRelative:F1}m");
    }

    private void OnVfrHudReceived(object? sender, VfrHudEventArgs e)
    {
        AppendEntry($"#074 VFR_HUD            SYS:1 COMP:1 spd={e.GroundSpeed:F1} hdg={e.Heading:F0} thr={e.Throttle}% climb={e.ClimbRate:F1}");
    }

    private void OnStatusTextReceived(object? sender, StatusTextEventArgs e)
    {
        AppendEntry($"#253 STATUSTEXT         SYS:1 COMP:1 sev={e.Severity} \"{e.Text}\"");
    }

    private void OnCommandAckReceived(object? sender, CommandAckEventArgs e)
    {
        AppendEntry($"#077 COMMAND_ACK        SYS:1 COMP:1 cmd={e.Command} result={e.Result}");
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Dispatcher.UIThread.Post(() => _logEntries.Clear());
    }

    [RelayCommand]
    private void ExportLogs()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                $"mavlink_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            File.WriteAllLines(path, _logEntries);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to export MAVLink log to file");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied when exporting MAVLink log");
        }
    }

    public void Dispose()
    {
        _connectionService.HeartbeatDataReceived -= OnHeartbeatDataReceived;
        _connectionService.SysStatusReceived -= OnSysStatusReceived;
        _connectionService.GpsRawIntReceived -= OnGpsRawIntReceived;
        _connectionService.AttitudeReceived -= OnAttitudeReceived;
        _connectionService.GlobalPositionIntReceived -= OnGlobalPositionIntReceived;
        _connectionService.VfrHudReceived -= OnVfrHudReceived;
        _connectionService.StatusTextReceived -= OnStatusTextReceived;
        _connectionService.CommandAckReceived -= OnCommandAckReceived;
    }
}
