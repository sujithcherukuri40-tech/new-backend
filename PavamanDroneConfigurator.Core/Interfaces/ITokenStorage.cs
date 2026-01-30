using PavamanDroneConfigurator.Core.Models.Auth;

namespace PavamanDroneConfigurator.Core.Interfaces;

/// <summary>
/// Secure storage interface for authentication tokens.
/// Implementations should use platform-appropriate secure storage.
/// </summary>
public interface ITokenStorage
{
    /// <summary>
    /// Stores token data securely.
    /// </summary>
    /// <param name="tokenData">Token data to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreTokensAsync(TokenData tokenData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves stored token data.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Token data if available, null otherwise.</returns>
    Task<TokenData?> GetTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears all stored tokens.
    /// Called on logout or when tokens are invalid.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ClearTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if valid tokens are stored.
    /// Does not validate with backend - just checks local storage.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if tokens exist in storage.</returns>
    Task<bool> HasTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current access token if available.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Access token string or null.</returns>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the access token is about to expire (within buffer time).
    /// </summary>
    /// <param name="bufferSeconds">Seconds before expiry to consider as "expiring".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if token is expiring soon or already expired.</returns>
    Task<bool> IsTokenExpiringSoonAsync(int bufferSeconds = 30, CancellationToken cancellationToken = default);
}
