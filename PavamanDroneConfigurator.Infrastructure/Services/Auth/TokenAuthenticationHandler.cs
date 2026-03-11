using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;

namespace PavamanDroneConfigurator.Infrastructure.Services.Auth;

/// <summary>
/// HTTP message handler that automatically attaches JWT access token to all requests.
/// Used for API services that require authentication (Admin endpoints, param-logs, etc.)
/// Detects session expiration (401 Unauthorized) and triggers re-login flow.
/// </summary>
public sealed class TokenAuthenticationHandler : DelegatingHandler
{
    private readonly ITokenStorage _tokenStorage;
    private readonly ILogger<TokenAuthenticationHandler> _logger;
    
    /// <summary>
    /// Event raised when a 401 Unauthorized response is received, indicating session expiration.
    /// Subscribe to this event to redirect user to login screen.
    /// </summary>
    public static event EventHandler? SessionExpired;

    public TokenAuthenticationHandler(
        ITokenStorage tokenStorage,
        ILogger<TokenAuthenticationHandler> logger)
    {
        _tokenStorage = tokenStorage;
        _logger = logger;
    }
    
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // Get the current access token
        var accessToken = await _tokenStorage.GetAccessTokenAsync(cancellationToken);

        if (!string.IsNullOrEmpty(accessToken))
        {
            // Attach JWT token to Authorization header
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            _logger.LogDebug("Attached JWT token to request: {Method} {Uri}", request.Method, request.RequestUri);
        }
        else
        {
            _logger.LogWarning("No access token available for request: {Method} {Uri}", request.Method, request.RequestUri);
        }

        // Send the request
        var response = await base.SendAsync(request, cancellationToken);
        
        // Check for 401 Unauthorized - indicates session expiration
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning("Received 401 Unauthorized - session expired for request: {Method} {Uri}", 
                request.Method, request.RequestUri);
            
            // Clear tokens locally
            try
            {
                await _tokenStorage.ClearTokensAsync(CancellationToken.None);
                _logger.LogInformation("Cleared expired tokens from storage");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clear tokens after session expiration");
            }
            
            // Raise session expired event to trigger re-login
            OnSessionExpired();
        }

        return response;
    }
    
    /// <summary>
    /// Raises the SessionExpired event to notify subscribers that re-login is required.
    /// </summary>
    private static void OnSessionExpired()
    {
        SessionExpired?.Invoke(null, EventArgs.Empty);
    }
    
    /// <summary>
    /// Manually trigger session expiration (for testing or explicit logout scenarios).
    /// </summary>
    public static void TriggerSessionExpired()
    {
        OnSessionExpired();
    }
}