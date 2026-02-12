using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.API.Models;
using System.Security.Cryptography;

namespace PavamanDroneConfigurator.API.Data;

/// <summary>
/// Database seeder for creating default admin user.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// Seeds the database with default admin user if it doesn't exist.
    /// Uses ADMIN_PASSWORD environment variable or generates a secure random password.
    /// </summary>
    public static async Task SeedAsync(AppDbContext context, ILogger logger)
    {
        try
        {
            // Check if admin user already exists
            var adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL") ?? "admin@droneconfig.local";
            var existingAdmin = await context.Users
                .FirstOrDefaultAsync(u => u.Email == adminEmail);

            if (existingAdmin == null)
            {
                // Get password from environment variable or generate secure random password
                var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");
                var isGeneratedPassword = false;
                
                if (string.IsNullOrEmpty(adminPassword))
                {
                    adminPassword = GenerateSecurePassword(16);
                    isGeneratedPassword = true;
                }
                
                // Validate password meets minimum requirements
                if (adminPassword.Length < 12)
                {
                    logger.LogError("? ADMIN_PASSWORD must be at least 12 characters");
                    throw new InvalidOperationException("Admin password does not meet minimum length requirements (12 characters)");
                }

                // Create default admin user
                var adminUser = new User
                {
                    Id = Guid.NewGuid(),
                    FullName = "Admin User",
                    Email = adminEmail,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                    IsApproved = true, // Admin is pre-approved
                    Role = UserRole.Admin,
                    CreatedAt = DateTimeOffset.UtcNow,
                    MustChangePassword = isGeneratedPassword // Force password change if auto-generated
                };

                context.Users.Add(adminUser);
                await context.SaveChangesAsync();

                logger.LogInformation("? Default admin user created: {Email}", adminEmail);
                
                if (isGeneratedPassword)
                {
                    logger.LogWarning("??  Generated admin password: {Password}", adminPassword);
                    logger.LogWarning("??  CRITICAL: Save this password securely and change it immediately!");
                    logger.LogWarning("??  For production, set ADMIN_PASSWORD environment variable before deployment.");
                }
                else
                {
                    logger.LogInformation("? Admin user created with password from ADMIN_PASSWORD environment variable");
                    logger.LogWarning("??  IMPORTANT: Change the admin password after first login!");
                }
            }
            else
            {
                logger.LogInformation("??  Admin user already exists: {Email}", adminEmail);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "? Error seeding database");
            throw;
        }
    }
    
    /// <summary>
    /// Generates a cryptographically secure random password.
    /// </summary>
    private static string GenerateSecurePassword(int length)
    {
        const string uppercase = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        const string lowercase = "abcdefghijklmnopqrstuvwxyz";
        const string digits = "0123456789";
        const string special = "!@#$%^&*()_+-=[]{}|;:,.<>?";
        const string allChars = uppercase + lowercase + digits + special;
        
        var password = new char[length];
        var randomBytes = new byte[length];
        
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        
        // Ensure at least one of each required character type
        password[0] = uppercase[randomBytes[0] % uppercase.Length];
        password[1] = lowercase[randomBytes[1] % lowercase.Length];
        password[2] = digits[randomBytes[2] % digits.Length];
        password[3] = special[randomBytes[3] % special.Length];
        
        // Fill remaining characters
        for (int i = 4; i < length; i++)
        {
            password[i] = allChars[randomBytes[i] % allChars.Length];
        }
        
        // Shuffle the password
        return ShuffleString(new string(password));
    }
    
    private static string ShuffleString(string str)
    {
        var chars = str.ToCharArray();
        var randomBytes = new byte[chars.Length];
        
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(randomBytes);
        }
        
        for (int i = chars.Length - 1; i > 0; i--)
        {
            int j = randomBytes[i] % (i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        
        return new string(chars);
    }
}
