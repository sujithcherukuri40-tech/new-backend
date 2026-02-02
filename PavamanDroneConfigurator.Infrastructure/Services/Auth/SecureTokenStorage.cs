using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models.Auth;

namespace PavamanDroneConfigurator.Infrastructure.Services.Auth;

/// <summary>
/// Secure token storage using Windows Data Protection API (DPAPI).
/// Tokens are encrypted at rest and stored in the user's app data folder.
/// </summary>
public sealed class SecureTokenStorage : ITokenStorage
{
    private readonly ILogger<SecureTokenStorage> _logger;
    private readonly string _tokenFilePath;

    // In-memory cache for performance
    private TokenData? _cachedTokens;
    private bool _cacheLoaded;

    public SecureTokenStorage(ILogger<SecureTokenStorage> logger)
    {
        _logger = logger;

        // Store in user's app data folder
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PavamanDroneConfigurator",
            "Auth"
        );

        Directory.CreateDirectory(appDataPath);
        _tokenFilePath = Path.Combine(appDataPath, "tokens.dat");
    }

    public Task<bool> HasTokensAsync(CancellationToken cancellationToken = default)
    {
        if (_cacheLoaded) return Task.FromResult(_cachedTokens != null);
        return Task.FromResult(File.Exists(_tokenFilePath));
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        LoadTokensIfNeeded();
        return Task.FromResult(_cachedTokens?.AccessToken);
    }

    public Task<TokenData?> GetTokensAsync(CancellationToken cancellationToken = default)
    {
        LoadTokensIfNeeded();
        return Task.FromResult(_cachedTokens);
    }

    public Task StoreTokensAsync(TokenData tokenData, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(tokenData);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_tokenFilePath, encryptedBytes);
            _cachedTokens = tokenData;
            _cacheLoaded = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store tokens securely");
        }
        return Task.CompletedTask;
    }

    public Task ClearTokensAsync(CancellationToken cancellationToken = default)
    {
        _cachedTokens = null;
        _cacheLoaded = true;
        try { if (File.Exists(_tokenFilePath)) File.Delete(_tokenFilePath); } catch { }
        return Task.CompletedTask;
    }

    public Task<bool> IsTokenExpiringSoonAsync(int bufferSeconds = 30, CancellationToken cancellationToken = default)
    {
        LoadTokensIfNeeded();
        if (_cachedTokens == null) return Task.FromResult(true);
        return Task.FromResult(DateTimeOffset.UtcNow >= _cachedTokens.ExpiresAt.AddSeconds(-bufferSeconds));
    }

    private void LoadTokensIfNeeded()
    {
        if (_cacheLoaded) return;
        _cacheLoaded = true;

        if (!File.Exists(_tokenFilePath)) return;

        try
        {
            var encryptedBytes = File.ReadAllBytes(_tokenFilePath);
            var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            _cachedTokens = JsonSerializer.Deserialize<TokenData>(Encoding.UTF8.GetString(plainBytes));
        }
        catch
        {
            _cachedTokens = null;
            try { File.Delete(_tokenFilePath); } catch { }
        }
    }
}
