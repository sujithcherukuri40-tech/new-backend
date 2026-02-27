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
/// </summary>
public sealed class TokenAuthenticationHandler : DelegatingHandler
{
    private readonly ITokenStorage _tokenStorage;
    private readonly ILogger<TokenAuthenticationHandler> _logger;

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
        return await base.SendAsync(request, cancellationToken);
    }
}