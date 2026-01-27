using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace PavamanDroneConfigurator.Infrastructure.Data;

/// <summary>
/// Design-time factory for creating DbContext instances during migrations.
/// This is used by EF Core tools (dotnet ef) to create migrations.
/// </summary>
public class DroneDbContextFactory : IDesignTimeDbContextFactory<DroneDbContext>
{
    public DroneDbContext CreateDbContext(string[] args)
    {
        // Build configuration to read from appsettings.json
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Path.Combine(Directory.GetCurrentDirectory(), "../PavamanDroneConfigurator.UI"))
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        // Get connection string
        var connectionString = configuration.GetConnectionString("PostgresDb");

        // Create DbContextOptions
        var optionsBuilder = new DbContextOptionsBuilder<DroneDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new DroneDbContext(optionsBuilder.Options);
    }
}
