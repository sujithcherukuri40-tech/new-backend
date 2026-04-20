using Microsoft.EntityFrameworkCore;
using PavamanDroneConfigurator.API.Data;

namespace PavamanDroneConfigurator.API.Services;

/// <summary>
/// Background service that periodically purges expired and revoked refresh tokens
/// from the database to prevent unbounded table growth.
/// Runs every 24 hours and deletes tokens that are both expired AND older than 30 days.
/// </summary>
public class TokenCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TokenCleanupService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(24);
    private readonly int _retentionDays = 30;

    public TokenCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<TokenCleanupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Token cleanup service started (interval: {Interval}h, retention: {Retention}d)",
            _interval.TotalHours, _retentionDays);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupExpiredTokensAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token cleanup");
            }

            try
            {
                await Task.Delay(_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
                break;
            }
        }

        _logger.LogInformation("Token cleanup service stopped");
    }

    private async Task CleanupExpiredTokensAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoffDate = DateTimeOffset.UtcNow.AddDays(-_retentionDays);

        // Delete tokens that are expired AND older than retention period
        var deletedCount = await context.RefreshTokens
            .Where(t => t.ExpiresAt < DateTimeOffset.UtcNow && t.CreatedAt < cutoffDate)
            .ExecuteDeleteAsync(cancellationToken);

        // Also delete revoked tokens older than retention period
        var revokedCount = await context.RefreshTokens
            .Where(t => t.Revoked && t.RevokedAt != null && t.RevokedAt < cutoffDate)
            .ExecuteDeleteAsync(cancellationToken);

        if (deletedCount > 0 || revokedCount > 0)
        {
            _logger.LogInformation(
                "Token cleanup completed: {ExpiredCount} expired + {RevokedCount} revoked tokens removed",
                deletedCount, revokedCount);
        }
        else
        {
            _logger.LogDebug("Token cleanup completed: no tokens to remove");
        }
    }
}
