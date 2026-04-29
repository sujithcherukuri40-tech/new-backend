using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PavamanDroneConfigurator.Core.Interfaces;
using System.Security.Claims;

namespace PavamanDroneConfigurator.API.Controllers;

/// <summary>
/// User-facing parameter locks endpoint.
/// Authenticated users can fetch their own locked parameters.
/// </summary>
[ApiController]
[Route("api/parameter-locks")]
[Authorize]
[EnableRateLimiting("fixed")]
[Produces("application/json")]
public class UserParameterLocksController : ControllerBase
{
    private readonly IParamLockService _paramLockService;
    private readonly ILogger<UserParameterLocksController> _logger;

    public UserParameterLocksController(
        IParamLockService paramLockService,
        ILogger<UserParameterLocksController> logger)
    {
        _paramLockService = paramLockService;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/parameter-locks/my
    /// Returns all active parameter locks for the currently authenticated user.
    /// Includes the full locked parameter lists fetched from S3.
    /// </summary>
    [HttpGet("my")]
    public async Task<ActionResult<MyLockedParamsResponse>> GetMyLockedParams(
        [FromQuery] string? deviceId = null)
    {
        try
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(userIdClaim) || !Guid.TryParse(userIdClaim, out var userId))
                return Unauthorized(new { error = "Invalid token" });

            var locks = await _paramLockService.GetUserLocksAsync(userId);

            // Merge all locked params across active locks for this user
            // (device-specific locks override global if deviceId is supplied)
            List<string> effectiveLockedParams;

            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                // Device-specific: prefer device locks, fall back to global
                var deviceLock = locks.FirstOrDefault(l => l.DeviceId == deviceId);
                var globalLock = locks.FirstOrDefault(l => string.IsNullOrWhiteSpace(l.DeviceId));

                var combined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (globalLock != null)
                    foreach (var p in globalLock.LockedParams) combined.Add(p);
                if (deviceLock != null)
                    foreach (var p in deviceLock.LockedParams) combined.Add(p);
                effectiveLockedParams = combined.OrderBy(p => p).ToList();
            }
            else
            {
                // No device — return all locked params across all locks
                var combined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in locks)
                    foreach (var p in l.LockedParams) combined.Add(p);
                effectiveLockedParams = combined.OrderBy(p => p).ToList();
            }

            _logger.LogInformation("User {UserId} fetched {Count} locked params (device={DeviceId})",
                userId, effectiveLockedParams.Count, deviceId ?? "all");

            return Ok(new MyLockedParamsResponse
            {
                UserId = userId,
                DeviceId = deviceId,
                LockedParams = effectiveLockedParams,
                Count = effectiveLockedParams.Count,
                LockCount = locks.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get locked params for current user");
            return StatusCode(500, new { error = "Failed to retrieve locked parameters" });
        }
    }
}

public class MyLockedParamsResponse
{
    public Guid UserId { get; set; }
    public string? DeviceId { get; set; }
    public List<string> LockedParams { get; set; } = new();
    public int Count { get; set; }
    public int LockCount { get; set; }
}
