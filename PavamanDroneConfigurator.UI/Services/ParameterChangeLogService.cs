using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Infrastructure.Services;
using PavamanDroneConfigurator.UI.ViewModels.Auth;

namespace PavamanDroneConfigurator.UI.Services;

/// <summary>
/// Singleton service that logs parameter changes to S3 from any page in the app.
/// Inject this wherever parameters are written; call LogAsync fire-and-forget.
/// </summary>
public class ParameterChangeLogService
{
    private readonly FirmwareApiService? _firmwareApiService;
    private readonly AuthSessionViewModel _authSession;
    private readonly IDroneInfoService _droneInfoService;
    private readonly ILogger<ParameterChangeLogService>? _logger;

    public ParameterChangeLogService(
        AuthSessionViewModel authSession,
        IDroneInfoService droneInfoService,
        FirmwareApiService? firmwareApiService = null,
        ILogger<ParameterChangeLogService>? logger = null)
    {
        _authSession = authSession;
        _droneInfoService = droneInfoService;
        _firmwareApiService = firmwareApiService;
        _logger = logger;
    }

    /// <summary>
    /// Logs multiple parameter changes to S3. Safe to call fire-and-forget.
    /// <param name="source">Human-readable source label shown in logs (e.g. "SprayingConfig").</param>
    /// </summary>
    public async Task LogAsync(
        IEnumerable<(string Name, float OldValue, float NewValue)> changes,
        string? source = null)
    {
        if (_firmwareApiService == null) return;

        var list = changes
            .Select(c => new ParameterChange
            {
                ParamName = c.Name,
                OldValue  = c.OldValue,
                NewValue  = c.NewValue,
                ChangedAt = DateTime.UtcNow
            })
            .ToList();

        if (list.Count == 0) return;

        try
        {
            var userId   = _authSession.CurrentState.User?.Id ?? "unknown";
            var userName = _authSession.CurrentState.User?.FullName
                        ?? _authSession.CurrentState.User?.Email
                        ?? "unknown";

            var droneInfo = await _droneInfoService.GetDroneInfoAsync();
            var droneId   = droneInfo?.DroneId ?? "unknown";
            var boardId   = droneInfo?.FcId    ?? "unknown";

            _logger?.LogInformation(
                "[{Source}] Logging {Count} param change(s) for user={User}, drone={Drone}",
                source ?? "App", list.Count, userName, droneId);

            await _firmwareApiService.UploadParameterLogAsync(userId, userName, droneId, boardId, list);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[{Source}] Failed to upload parameter changes to S3", source ?? "App");
        }
    }

    /// <summary>Convenience overload for a single parameter change.</summary>
    public Task LogAsync(string name, float oldValue, float newValue, string? source = null)
        => LogAsync(new[] { (name, oldValue, newValue) }, source);
}
