using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.API.Models;

namespace PavamanDroneConfigurator.API.Data;

/// <summary>
/// Database seeder for creating default admin user.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// Seeds the database with default admin user if it doesn't exist.
    /// </summary>
    public static async Task SeedAsync(AppDbContext context, ILogger logger)
    {
        try
        {
            // Check if admin user already exists
            var adminEmail = "admin@droneconfig.local";
            var existingAdmin = await context.Users
                .FirstOrDefaultAsync(u => u.Email == adminEmail);

            if (existingAdmin == null)
            {
                // Create default admin user
                var adminUser = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Admin User",
                    Email = adminEmail,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                    IsApproved = true, // Admin is pre-approved
                    Role = UserRole.Admin,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                context.Users.Add(adminUser);
                await context.SaveChangesAsync();

                logger.LogInformation("? Default admin user created: {Email}", adminEmail);
                logger.LogInformation("   Email: {Email}", adminEmail);
                logger.LogInformation("   Password: Admin@123");
                logger.LogWarning("??  IMPORTANT: Change the default admin password after first login!");
            }
            else
            {
                logger.LogInformation("??  Admin user already exists: {Email}", adminEmail);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "? Error seeding database");
        }
    }
}
