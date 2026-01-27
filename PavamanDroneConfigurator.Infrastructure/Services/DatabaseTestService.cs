using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Infrastructure.Data;
using System;
using System.Threading.Tasks;

namespace PavamanDroneConfigurator.Infrastructure.Services;

/// <summary>
/// Service to test and verify PostgreSQL database connection.
/// This is a temporary service for Step 1 verification.
/// </summary>
public class DatabaseTestService
{
    private readonly DroneDbContext _dbContext;
    private readonly ILogger<DatabaseTestService> _logger;

    public DatabaseTestService(DroneDbContext dbContext, ILogger<DatabaseTestService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Tests the database connection by opening a connection and executing a simple query.
    /// </summary>
    /// <returns>True if connection successful, false otherwise</returns>
    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            _logger.LogInformation("Testing PostgreSQL database connection...");
            
            // Try to open connection
            await _dbContext.Database.OpenConnectionAsync();
            
            _logger.LogInformation("? Database connection successful!");
            _logger.LogInformation("Connected to: {DatabaseName}", _dbContext.Database.GetDbConnection().Database);
            _logger.LogInformation("Server version: {ServerVersion}", _dbContext.Database.GetDbConnection().ServerVersion);
            
            // Close connection
            await _dbContext.Database.CloseConnectionAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "? Database connection failed: {Message}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Gets connection information without actually connecting (for debugging).
    /// </summary>
    public string GetConnectionInfo()
    {
        try
        {
            var connection = _dbContext.Database.GetDbConnection();
            return $"Database: {connection.Database}, DataSource: {connection.DataSource}";
        }
        catch (Exception ex)
        {
            return $"Error getting connection info: {ex.Message}";
        }
    }
}
