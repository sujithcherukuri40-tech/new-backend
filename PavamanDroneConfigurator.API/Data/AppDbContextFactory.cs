using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PavamanDroneConfigurator.API.Data;

/// <summary>
/// Factory for creating AppDbContext at design time for EF Core migrations.
/// This allows migrations to work without a database connection.
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        // Use a dummy connection string for design-time operations
        // This is only used for generating migrations, not at runtime
        optionsBuilder.UseNpgsql("Host=localhost;Database=drone_configurator;Username=postgres;Password=dummy");

        return new AppDbContext(optionsBuilder.Options);
    }
}
