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
    private readonly SemaphoreSlim _lock = new(1, 1);

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

    public async Task StoreTokensAsync(TokenData tokenData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tokenData);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Serialize to JSON
            var json = JsonSerializer.Serialize(tokenData);
            var plainBytes = Encoding.UTF8.GetBytes(json);

            // Encrypt using DPAPI (Windows-specific, user-scope)
            var encryptedBytes = ProtectedData.Protect(
                plainBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            );

            // Write to file
            await File.WriteAllBytesAsync(_tokenFilePath, encryptedBytes, cancellationToken);

            // Update cache
            _cachedTokens = tokenData;
            _cacheLoaded = true;

            _logger.LogDebug("Tokens stored securely");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store tokens securely");
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TokenData?> GetTokensAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Return cached if available
            if (_cacheLoaded)
            {
                return _cachedTokens;
            }

            // Check if file exists
            if (!File.Exists(_tokenFilePath))
            {
                _cacheLoaded = true;
                _cachedTokens = null;
                return null;
            }

            // Read and decrypt
            var encryptedBytes = await File.ReadAllBytesAsync(_tokenFilePath, cancellationToken);

            var plainBytes = ProtectedData.Unprotect(
                encryptedBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser
            );

            var json = Encoding.UTF8.GetString(plainBytes);
            _cachedTokens = JsonSerializer.Deserialize<TokenData>(json);
            _cacheLoaded = true;

            return _cachedTokens;
        }
        catch (CryptographicException ex)
        {
            // Token file corrupted or encrypted by different user
            _logger.LogWarning(ex, "Failed to decrypt tokens - clearing corrupted data");
            await ClearTokensInternalAsync();
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve tokens");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearTokensAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await ClearTokensInternalAsync();
            _logger.LogDebug("Tokens cleared");
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task ClearTokensInternalAsync()
    {
        _cachedTokens = null;
        _cacheLoaded = true;

        if (File.Exists(_tokenFilePath))
        {
            try
            {
                File.Delete(_tokenFilePath);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete token file");
            }
        }

        return Task.CompletedTask;
    }

    public async Task<bool> HasTokensAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await GetTokensAsync(cancellationToken);
        return tokens != null;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var tokens = await GetTokensAsync(cancellationToken);
        return tokens?.AccessToken;
    }

    public async Task<bool> IsTokenExpiringSoonAsync(int bufferSeconds = 30, CancellationToken cancellationToken = default)
    {
        var tokens = await GetTokensAsync(cancellationToken);
        if (tokens == null)
        {
            return true; // No tokens = expired
        }

        var expiryWithBuffer = tokens.ExpiresAt.AddSeconds(-bufferSeconds);
        return DateTimeOffset.UtcNow >= expiryWithBuffer;
    }
}
