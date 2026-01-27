using Microsoft.EntityFrameworkCore;
using PavamanDroneConfigurator.Infrastructure.Data.Entities;

namespace PavamanDroneConfigurator.Infrastructure.Data;

/// <summary>
/// Database context for Drone Configurator application.
/// Manages connection to PostgreSQL database on AWS RDS.
/// </summary>
public class DroneDbContext : DbContext
{
    public DroneDbContext(DbContextOptions<DroneDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Drones/vehicles configured through the application
    /// </summary>
    public DbSet<DroneEntity> Drones { get; set; }

    /// <summary>
    /// Historical record of parameter changes
    /// </summary>
    public DbSet<ParameterHistoryEntity> ParameterHistory { get; set; }

    /// <summary>
    /// Calibration events and results
    /// </summary>
    public DbSet<CalibrationRecordEntity> CalibrationRecords { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Configure DroneEntity
        modelBuilder.Entity<DroneEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.SerialNumber).IsUnique();
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // Configure ParameterHistoryEntity
        modelBuilder.Entity<ParameterHistoryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DroneId, e.ParameterName, e.ChangedAt });
            entity.Property(e => e.ChangedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            // Foreign key relationship
            entity.HasOne(e => e.Drone)
                .WithMany(d => d.ParameterHistory)
                .HasForeignKey(e => e.DroneId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure CalibrationRecordEntity
        modelBuilder.Entity<CalibrationRecordEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.DroneId, e.CalibrationType, e.StartedAt });
            entity.Property(e => e.StartedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            
            // Foreign key relationship
            entity.HasOne(e => e.Drone)
                .WithMany(d => d.CalibrationRecords)
                .HasForeignKey(e => e.DroneId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
