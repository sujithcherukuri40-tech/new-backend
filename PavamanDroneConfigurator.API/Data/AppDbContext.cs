using Microsoft.EntityFrameworkCore;
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
    }
}
