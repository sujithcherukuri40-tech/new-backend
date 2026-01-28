using Microsoft.EntityFrameworkCore;
using PavamanDroneConfigurator.API.Data;
using PavamanDroneConfigurator.API.DTOs;
using PavamanDroneConfigurator.API.Exceptions;
using PavamanDroneConfigurator.API.Models;

namespace PavamanDroneConfigurator.API.Services;

/// <summary>
/// Service for authentication operations.
/// </summary>
public class AuthService : IAuthService
{
    private readonly AppDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        AppDbContext context,
        ITokenService tokenService,
        ILogger<AuthService> logger)
    {
        _context = context;
        _tokenService = tokenService;
        _logger = logger;
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
        if (!IsPasswordValid(request.Password))
        {
            throw new AuthException(
                "Password must contain at least one uppercase letter, one lowercase letter, and one number",
                "WEAK_PASSWORD");
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
            CreatedAt = DateTimeOffset.UtcNow
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        _logger.LogInformation("New user registered: {Email} (pending approval)", user.Email);

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
            throw new AuthException("Invalid email or password", "INVALID_CREDENTIALS");
        }

        // Verify password
        if (!BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed: Invalid password for user {UserId}", user.Id);
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

        // Update last login
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

    private static bool IsPasswordValid(string password)
    {
        var hasUppercase = false;
        var hasLowercase = false;
        var hasDigit = false;

        foreach (var c in password)
        {
            if (char.IsUpper(c)) hasUppercase = true;
            else if (char.IsLower(c)) hasLowercase = true;
            else if (char.IsDigit(c)) hasDigit = true;

            if (hasUppercase && hasLowercase && hasDigit)
                return true;
        }

        return hasUppercase && hasLowercase && hasDigit;
    }
}
