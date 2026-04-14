using Microsoft.EntityFrameworkCore;
using PavamanDroneConfigurator.API.Data;
using PavamanDroneConfigurator.API.DTOs;
using PavamanDroneConfigurator.API.Exceptions;
using PavamanDroneConfigurator.API.Models;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.API.Services;

/// <summary>
/// Service for authentication operations.
/// </summary>
public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;
    private readonly IEmailService _emailService;

    // Account lockout settings
    private readonly int _maxFailedAttempts;
    private readonly int _lockoutDurationMinutes;

    // Password policy settings
    private readonly int _minPasswordLength;
    private readonly bool _requireSpecialChar;
    private readonly bool _requireUppercase;
    private readonly bool _requireLowercase;
    private readonly bool _requireDigit;

    // Common weak passwords to reject
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password123", "123456", "12345678", "qwerty", "admin", "letmein",
        "welcome", "monkey", "dragon", "master", "abc123", "111111", "baseball",
        "iloveyou", "trustno1", "sunshine", "princess", "admin123", "password1"
    };

    public AuthService(
        AppDbContext context,
        ITokenService tokenService,
        IConfiguration configuration,
        ILogger<AuthService> logger,
        IEmailService emailService)
    {
        _context = context;
        _tokenService = tokenService;
        _configuration = configuration;
        _logger = logger;
        _emailService = emailService;

        // Load security settings from configuration
        var lockoutConfig = configuration.GetSection("Security:AccountLockout");
        _maxFailedAttempts = int.Parse(lockoutConfig["MaxFailedAttempts"] ?? "5");
        _lockoutDurationMinutes = int.Parse(lockoutConfig["LockoutDurationMinutes"] ?? "15");

        var passwordConfig = configuration.GetSection("Security:PasswordPolicy");
        _minPasswordLength = int.Parse(passwordConfig["MinimumLength"] ?? "12");
        _requireSpecialChar = bool.Parse(passwordConfig["RequireSpecialCharacter"] ?? "true");
        _requireUppercase = bool.Parse(passwordConfig["RequireUppercase"] ?? "true");
        _requireLowercase = bool.Parse(passwordConfig["RequireLowercase"] ?? "true");
        _requireDigit = bool.Parse(passwordConfig["RequireDigit"] ?? "true");
    }

    /// <inheritdoc />
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        // Check if email already exists
        var existingUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());

        if (existingUser != null)
        {
            _logger.LogWarning("Registration failed: Email {Email} already exists", request.Email);
            throw new AuthException("An account with this email already exists", "EMAIL_EXISTS");
        }

        // Validate password complexity
        var passwordValidation = ValidatePassword(request.Password, request.Email, request.FullName);
        if (!passwordValidation.IsValid)
        {
            throw new AuthException(passwordValidation.ErrorMessage!, "WEAK_PASSWORD");
        }

        // Create new user (NOT approved by default)
        var user = new User
        {
            Id = Guid.NewGuid(),
            FullName = request.FullName.Trim(),
            Email = request.Email.ToLower().Trim(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            IsApproved = false, // CRITICAL: New users must be approved by admin
            Role = UserRole.User,
            CreatedAt = DateTimeOffset.UtcNow,
            MustChangePassword = false,
            FailedLoginAttempts = 0,
            LockoutEnd = null
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("New user registered: {Email} (pending approval)", user.Email);

        // Generate and send OTP
        var otp = new Random().Next(100000, 999999).ToString();
        try
        {
            await _emailService.SendOtpEmailAsync(request.Email, otp);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send OTP to new user {Email}", request.Email);
        }

        // Return response without tokens (user is pending approval)
        return new AuthResponse
        {
            User = MapToUserDto(user),
            Tokens = null // No tokens for unapproved users
        };
    }

    /// <inheritdoc />
    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());

        if (user == null)
        {
            _logger.LogWarning("Login failed: User not found for email {Email}", request.Email);
            // Use same error message to prevent user enumeration
            throw new AuthException("Invalid email or password", "INVALID_CREDENTIALS");
        }

        // Check if account is locked
        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            var remainingMinutes = (int)(user.LockoutEnd.Value - DateTimeOffset.UtcNow).TotalMinutes + 1;
            _logger.LogWarning("Login denied: Account {UserId} is locked until {LockoutEnd}", user.Id, user.LockoutEnd);
            throw new AuthException(
                $"Account is temporarily locked. Please try again in {remainingMinutes} minutes.",
                "ACCOUNT_LOCKED",
                403);
        }

        // Verify password
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            // Increment failed attempts
            user.FailedLoginAttempts++;
            
            if (user.FailedLoginAttempts >= _maxFailedAttempts)
            {
                user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(_lockoutDurationMinutes);
                _logger.LogWarning("Account {UserId} locked after {Attempts} failed attempts", 
                    user.Id, user.FailedLoginAttempts);
            }
            
            await _context.SaveChangesAsync();
            
            _logger.LogWarning("Login failed: Invalid password for user {UserId} (attempt {Attempt}/{Max})", 
                user.Id, user.FailedLoginAttempts, _maxFailedAttempts);
            throw new AuthException("Invalid email or password", "INVALID_CREDENTIALS");
        }

        // Check if user is approved - CRITICAL BUSINESS RULE
        if (!user.IsApproved)
        {
            _logger.LogWarning("Login denied: User {UserId} is pending approval", user.Id);
            throw new AuthException(
                "Your account is pending approval. Please wait for an administrator to approve your access.",
                "ACCOUNT_PENDING_APPROVAL",
                403);
        }

        // Reset failed login attempts on successful login
        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync();

        // Generate tokens
        var accessToken = _tokenService.GenerateAccessToken(user);
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user.Id, ipAddress);

        _logger.LogInformation("User {UserId} logged in successfully", user.Id);

        return new AuthResponse
        {
            User = MapToUserDto(user),
            Tokens = new TokenDto
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken.Token,
                ExpiresIn = _tokenService.GetAccessTokenExpirySeconds()
            }
        };
    }

    /// <inheritdoc />
    public async Task<AuthResponse> GetCurrentUserAsync(Guid userId)
    {
        var user = await _context.Users.FindAsync(userId);

        if (user == null)
        {
            throw new AuthException("User not found", "USER_NOT_FOUND", 404);
        }

        return new AuthResponse
        {
            User = MapToUserDto(user),
            Tokens = null // Don't return new tokens for /me endpoint
        };
    }

    /// <inheritdoc />
    public async Task LogoutAsync(Guid userId, string? refreshToken)
    {
        if (!string.IsNullOrEmpty(refreshToken))
        {
            await _tokenService.RevokeRefreshTokenAsync(refreshToken, "User logout");
        }
        else
        {
            // Revoke all tokens if no specific token provided
            await _tokenService.RevokeAllUserTokensAsync(userId, "User logout (all tokens)");
        }

        _logger.LogInformation("User {UserId} logged out", userId);
    }

    /// <inheritdoc />
    public async Task<AuthResponse> RefreshTokenAsync(string refreshToken, string? ipAddress)
    {
        var token = await _tokenService.ValidateRefreshTokenAsync(refreshToken);

        if (token?.User == null)
        {
            throw new AuthException("Invalid or expired refresh token", "SESSION_EXPIRED", 401);
        }

        var user = token.User;

        // Check if user is still approved (admin might have revoked access)
        if (!user.IsApproved)
        {
            await _tokenService.RevokeRefreshTokenAsync(refreshToken, "User no longer approved");
            throw new AuthException(
                "Your account access has been revoked. Please contact an administrator.",
                "ACCOUNT_DISABLED",
                403);
        }

        // Rotate refresh token (revoke old, create new)
        await _tokenService.RevokeRefreshTokenAsync(refreshToken, "Replaced by new token");
        var newRefreshToken = await _tokenService.GenerateRefreshTokenAsync(user.Id, ipAddress);

        // Generate new access token
        var newAccessToken = _tokenService.GenerateAccessToken(user);

        _logger.LogDebug("Tokens refreshed for user {UserId}", user.Id);

        return new AuthResponse
        {
            User = MapToUserDto(user),
            Tokens = new TokenDto
            {
                AccessToken = newAccessToken,
                RefreshToken = newRefreshToken.Token,
                ExpiresIn = _tokenService.GetAccessTokenExpirySeconds()
            }
        };
    }

    /// <inheritdoc />
    public async Task<User?> GetUserByIdAsync(Guid userId)
    {
        return await _context.Users.FindAsync(userId);
    }

    private static UserDto MapToUserDto(User user)
    {
        return new UserDto
        {
            Id = user.Id.ToString(),
            Email = user.Email,
            FullName = user.FullName,
            IsApproved = user.IsApproved,
            Role = user.Role.ToString(),
            CreatedAt = user.CreatedAt
        };
    }

    /// <summary>
    /// Validates password against security policy.
    /// </summary>
    private (bool IsValid, string? ErrorMessage) ValidatePassword(string password, string email, string fullName)
    {
        var errors = new List<string>();

        // Check minimum length
        if (password.Length < _minPasswordLength)
        {
            errors.Add($"at least {_minPasswordLength} characters");
        }

        // Check for uppercase
        if (_requireUppercase && !password.Any(char.IsUpper))
        {
            errors.Add("one uppercase letter");
        }

        // Check for lowercase
        if (_requireLowercase && !password.Any(char.IsLower))
        {
            errors.Add("one lowercase letter");
        }

        // Check for digit
        if (_requireDigit && !password.Any(char.IsDigit))
        {
            errors.Add("one number");
        }

        // Check for special character
        if (_requireSpecialChar && !password.Any(c => !char.IsLetterOrDigit(c)))
        {
            errors.Add("one special character (!@#$%^&*...)");
        }

        if (errors.Count > 0)
        {
            return (false, $"Password must contain {string.Join(", ", errors)}");
        }

        // Check for common passwords
        if (CommonPasswords.Contains(password))
        {
            return (false, "This password is too common. Please choose a stronger password.");
        }

        // Check if password contains email or name
        var emailPrefix = email.Split('@')[0].ToLower();
        var nameParts = fullName.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        if (password.ToLower().Contains(emailPrefix) && emailPrefix.Length >= 3)
        {
            return (false, "Password cannot contain your email address");
        }

        foreach (var namePart in nameParts.Where(n => n.Length >= 3))
        {
            if (password.ToLower().Contains(namePart))
            {
                return (false, "Password cannot contain your name");
            }
        }

        // Check for sequential patterns
        if (HasSequentialPattern(password))
        {
            return (false, "Password cannot contain sequential characters (abc, 123, etc.)");
        }

        return (true, null);
    }

    /// <summary>
    /// Checks for sequential character patterns.
    /// </summary>
    private static bool HasSequentialPattern(string password)
    {
        if (password.Length < 4) return false;

        for (int i = 0; i < password.Length - 3; i++)
        {
            // Check for ascending sequence
            if (password[i] + 1 == password[i + 1] && 
                password[i + 1] + 1 == password[i + 2] && 
                password[i + 2] + 1 == password[i + 3])
            {
                return true;
            }

            // Check for descending sequence
            if (password[i] - 1 == password[i + 1] && 
                password[i + 1] - 1 == password[i + 2] && 
                password[i + 2] - 1 == password[i + 3])
            {
                return true;
            }

            // Check for repeated characters
            if (password[i] == password[i + 1] && 
                password[i + 1] == password[i + 2] && 
                password[i + 2] == password[i + 3])
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task ForgotPasswordAsync(string email)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

        if (user == null)
        {
            // Do not leak user existence
            _logger.LogWarning("Forgot password requested for non-existent email {Email}", email);
            return;
        }

        var resetToken = Guid.NewGuid().ToString("N");
        // A full implementation would save this reset token/expiry to the database.
        
        var resetBaseUrl = _configuration["App:PasswordResetBaseUrl"]?.TrimEnd('/')
            ?? "https://app.pavamandrone.com/reset-password";
        var resetLink = $"{resetBaseUrl}?token={resetToken}";

        try
        {
            await _emailService.SendPasswordResetEmailAsync(email, resetLink);
            _logger.LogInformation("Password reset email sent to {Email}", email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", email);
            throw;
        }
    }
}
