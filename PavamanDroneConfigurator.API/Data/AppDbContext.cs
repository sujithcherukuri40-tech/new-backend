using Microsoft.EntityFrameworkCore;
using PavamanDroneConfigurator.API.Data.Entities;
using PavamanDroneConfigurator.API.Models;

namespace PavamanDroneConfigurator.API.Data;

/// <summary>
/// Application database context for authentication.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Users table.
    /// </summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>
    /// Refresh tokens table.
    /// </summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>
    /// Parameter locks table.
    /// </summary>
    public DbSet<ParameterLockEntity> ParameterLocks => Set<ParameterLockEntity>();

    /// <summary>
    /// User-specific firmwares table.
    /// </summary>
    public DbSet<UserFirmwareEntity> UserFirmwares => Set<UserFirmwareEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User configuration
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.FullName)
                .HasColumnName("full_name")
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(e => e.Email)
                .HasColumnName("email")
                .HasMaxLength(256)
                .IsRequired();

            entity.HasIndex(e => e.Email)
                .IsUnique();

            entity.Property(e => e.PasswordHash)
                .HasColumnName("password_hash")
                .HasMaxLength(256)
                .IsRequired();

            entity.Property(e => e.IsApproved)
                .HasColumnName("is_approved")
                .HasDefaultValue(false);

            entity.Property(e => e.Role)
                .HasColumnName("role")
                .HasConversion<string>()
                .HasMaxLength(20)
                .HasDefaultValue(UserRole.User);

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.LastLoginAt)
                .HasColumnName("last_login_at");

            // New security fields
            entity.Property(e => e.MustChangePassword)
                .HasColumnName("must_change_password")
                .HasDefaultValue(false);

            entity.Property(e => e.FailedLoginAttempts)
                .HasColumnName("failed_login_attempts")
                .HasDefaultValue(0);

            entity.Property(e => e.LockoutEnd)
                .HasColumnName("lockout_end");

            entity.Property(e => e.PasswordResetToken)
                .HasColumnName("password_reset_token")
                .HasMaxLength(512);

            entity.Property(e => e.PasswordResetTokenExpiry)
                .HasColumnName("password_reset_token_expiry");

            // Relationship to refresh tokens
            entity.HasMany(e => e.RefreshTokens)
                .WithOne(e => e.User)
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // RefreshToken configuration
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasDefaultValueSql("gen_random_uuid()");

            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(e => e.Token)
                .HasColumnName("token")
                .HasMaxLength(512)
                .IsRequired();

            entity.HasIndex(e => e.Token)
                .IsUnique();

            entity.Property(e => e.ExpiresAt)
                .HasColumnName("expires_at")
                .IsRequired();

            entity.Property(e => e.Revoked)
                .HasColumnName("revoked")
                .HasDefaultValue(false);

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.CreatedByIp)
                .HasColumnName("created_by_ip")
                .HasMaxLength(45);

            entity.Property(e => e.RevokedAt)
                .HasColumnName("revoked_at");

            entity.Property(e => e.RevokedReason)
                .HasColumnName("revoked_reason")
                .HasMaxLength(256);

            // Index for cleanup queries
            entity.HasIndex(e => new { e.UserId, e.Revoked, e.ExpiresAt });
        });

        // ParameterLock configuration
        modelBuilder.Entity<ParameterLockEntity>(entity =>
        {
            entity.ToTable("parameter_locks");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(e => e.DeviceId)
                .HasColumnName("device_id")
                .HasMaxLength(100);

            entity.Property(e => e.S3Key)
                .HasColumnName("s3_key")
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(e => e.ParamCount)
                .HasColumnName("param_count")
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            entity.Property(e => e.CreatedBy)
                .HasColumnName("created_by")
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .HasColumnName("updated_at");

            entity.Property(e => e.IsActive)
                .HasColumnName("is_active")
                .HasDefaultValue(true);

            // Indexes
            entity.HasIndex(e => new { e.UserId, e.DeviceId })
                .HasDatabaseName("IX_parameter_locks_user_id_device_id");

            entity.HasIndex(e => e.IsActive)
                .HasDatabaseName("IX_parameter_locks_is_active");

            entity.HasIndex(e => e.CreatedBy)
                .HasDatabaseName("IX_parameter_locks_created_by");

            // Foreign keys
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.CreatedByUser)
                .WithMany()
                .HasForeignKey(e => e.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // UserFirmware configuration
        modelBuilder.Entity<UserFirmwareEntity>(entity =>
        {
            entity.ToTable("user_firmwares");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .ValueGeneratedOnAdd();

            entity.Property(e => e.UserId)
                .HasColumnName("user_id")
                .IsRequired();

            entity.Property(e => e.S3Key)
                .HasColumnName("s3_key")
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(e => e.FileName)
                .HasColumnName("file_name")
                .HasMaxLength(255)
                .IsRequired();

            entity.Property(e => e.DisplayName)
                .HasColumnName("display_name")
                .HasMaxLength(255);

            entity.Property(e => e.FirmwareName)
                .HasColumnName("firmware_name")
                .HasMaxLength(255);

            entity.Property(e => e.FirmwareVersion)
                .HasColumnName("firmware_version")
                .HasMaxLength(50);

            entity.Property(e => e.Description)
                .HasColumnName("description")
                .HasMaxLength(1000);

            entity.Property(e => e.VehicleType)
                .HasColumnName("vehicle_type")
                .HasMaxLength(50)
                .IsRequired()
                .HasDefaultValue("Copter");

            entity.Property(e => e.FileSize)
                .HasColumnName("file_size")
                .IsRequired();

            entity.Property(e => e.UploadedAt)
                .HasColumnName("uploaded_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.UploadedBy)
                .HasColumnName("uploaded_by")
                .IsRequired();

            entity.Property(e => e.AssignedAt)
                .HasColumnName("assigned_at")
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.Property(e => e.IsActive)
                .HasColumnName("is_active")
                .HasDefaultValue(true);

            entity.Property(e => e.LastDownloaded)
                .HasColumnName("last_downloaded");

            entity.Property(e => e.DownloadCount)
                .HasColumnName("download_count")
                .HasDefaultValue(0);

            // Indexes
            entity.HasIndex(e => e.UserId)
                .HasDatabaseName("IX_user_firmwares_user_id");

            entity.HasIndex(e => e.S3Key)
                .HasDatabaseName("IX_user_firmwares_s3_key");

            entity.HasIndex(e => new { e.UserId, e.IsActive })
                .HasDatabaseName("IX_user_firmwares_user_id_is_active");

            entity.HasIndex(e => e.UploadedBy)
                .HasDatabaseName("IX_user_firmwares_uploaded_by");

            entity.HasIndex(e => e.VehicleType)
                .HasDatabaseName("IX_user_firmwares_vehicle_type");

            // Foreign keys
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.UploadedByUser)
                .WithMany()
                .HasForeignKey(e => e.UploadedBy)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
