using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PavamanDroneConfigurator.API.Data;
using PavamanDroneConfigurator.API.Models;

namespace PavamanDroneConfigurator.API.Services;

/// <summary>
/// Service for JWT and refresh token operations.
/// </summary>
public class TokenService : ITokenService
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TokenService> _logger;

    public TokenService(
        AppDbContext context,
        IConfiguration configuration,
        ILogger<TokenService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    /// <inheritdoc />
    public string GenerateAccessToken(User user)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var secretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY")
            ?? jwtSettings["SecretKey"]
            ?? throw new InvalidOperationException("JWT secret key not configured");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, user.Role.ToString()),
            new("approved", user.IsApproved.ToString().ToLower())
        };

        var expiryMinutes = int.Parse(jwtSettings["AccessTokenMinutes"] ?? "15");
        var expiry = DateTime.UtcNow.AddMinutes(expiryMinutes);

        var token = new JwtSecurityToken(
            issuer: jwtSettings["Issuer"] ?? "DroneConfigurator",
            audience: jwtSettings["Audience"] ?? "DroneConfiguratorClient",
            claims: claims,
            expires: expiry,
            signingCredentials: credentials
        );

        _logger.LogDebug("Generated access token for user {UserId}, expires at {Expiry}", user.Id, expiry);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc />
    public async Task<RefreshToken> GenerateRefreshTokenAsync(Guid userId, string? ipAddress)
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var refreshDays = int.Parse(jwtSettings["RefreshTokenDays"] ?? "7");

        // Generate a cryptographically secure random token
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var tokenValue = Convert.ToBase64String(randomBytes);

        var refreshToken = new RefreshToken
        {
            UserId = userId,
            Token = tokenValue,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(refreshDays),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByIp = ipAddress
        };

        _context.RefreshTokens.Add(refreshToken);
        await _context.SaveChangesAsync();

        _logger.LogDebug("Generated refresh token for user {UserId}, expires at {Expiry}", 
            userId, refreshToken.ExpiresAt);

        return refreshToken;
    }

    /// <inheritdoc />
    public async Task<RefreshToken?> ValidateRefreshTokenAsync(string token)
    {
        var refreshToken = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == token);

        if (refreshToken == null)
        {
            _logger.LogWarning("Refresh token not found");
            return null;
        }

        if (!refreshToken.IsValid)
        {
            _logger.LogWarning("Refresh token is invalid (revoked or expired) for user {UserId}", 
                refreshToken.UserId);
            return null;
        }

        return refreshToken;
    }

    /// <inheritdoc />
    public async Task RevokeRefreshTokenAsync(string token, string reason)
    {
        var refreshToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == token);

        if (refreshToken != null && !refreshToken.Revoked)
        {
            refreshToken.Revoked = true;
            refreshToken.RevokedAt = DateTimeOffset.UtcNow;
            refreshToken.RevokedReason = reason;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Revoked refresh token for user {UserId}: {Reason}", 
                refreshToken.UserId, reason);
        }
    }

    /// <inheritdoc />
    public async Task RevokeAllUserTokensAsync(Guid userId, string reason)
    {
        var tokens = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && !rt.Revoked)
            .ToListAsync();

        foreach (var token in tokens)
        {
            token.Revoked = true;
            token.RevokedAt = DateTimeOffset.UtcNow;
            token.RevokedReason = reason;
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Revoked {Count} refresh tokens for user {UserId}: {Reason}", 
            tokens.Count, userId, reason);
    }

    /// <inheritdoc />
    public int GetAccessTokenExpirySeconds()
    {
        var jwtSettings = _configuration.GetSection("Jwt");
        var expiryMinutes = int.Parse(jwtSettings["AccessTokenMinutes"] ?? "15");
        return expiryMinutes * 60;
    }
}
