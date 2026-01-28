using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models.Auth;

namespace PavamanDroneConfigurator.Infrastructure.Services.Auth;

/// <summary>
/// Authentication service that makes real HTTP calls to the backend API.
/// No mock logic, no fake users, no hardcoded responses.
/// Backend is the single source of truth for all authentication decisions.
/// </summary>
public sealed class AuthApiService : IAuthService
{
    private readonly HttpClient _httpClient;
    private readonly ITokenStorage _tokenStorage;
    private readonly ILogger<AuthApiService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AuthApiService(
        HttpClient httpClient,
        ITokenStorage tokenStorage,
        ILogger<AuthApiService> logger)
    {
        _httpClient = httpClient;
        _tokenStorage = tokenStorage;
        _logger = logger;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            _logger.LogInformation("Attempting login for user: {Email}", request.Email);

            var response = await _httpClient.PostAsJsonAsync(
                "/auth/login",
                new { email = request.Email, password = request.Password },
                JsonOptions,
                cancellationToken
            );

            return await HandleAuthResponseAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error during login");
            return AuthResult.NetworkError();
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "Timeout during login");
            return AuthResult.Timeout();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during login");
            return AuthResult.Failed("An unexpected error occurred. Please try again.", AuthErrorCode.Unknown);
        }
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            _logger.LogInformation("Attempting registration for user: {Email}", request.Email);

            var response = await _httpClient.PostAsJsonAsync(
                "/auth/register",
                new
                {
                    fullName = request.FullName,
                    email = request.Email,
                    password = request.Password,
                    confirmPassword = request.ConfirmPassword
                },
                JsonOptions,
                cancellationToken
            );

            return await HandleAuthResponseAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error during registration");
            return AuthResult.NetworkError();
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "Timeout during registration");
            return AuthResult.Timeout();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during registration");
            return AuthResult.Failed("An unexpected error occurred. Please try again.", AuthErrorCode.Unknown);
        }
    }

    public async Task<AuthResult> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await _tokenStorage.GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogDebug("No access token available");
                return AuthResult.Succeeded(AuthState.CreateUnauthenticated());
            }

            // Check if token is expiring soon, try to refresh first
            if (await _tokenStorage.IsTokenExpiringSoonAsync(30, cancellationToken))
            {
                var refreshResult = await RefreshTokenAsync(cancellationToken);
                if (!refreshResult.Success)
                {
                    return refreshResult;
                }
                accessToken = await _tokenStorage.GetAccessTokenAsync(cancellationToken);
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Token invalid, clear and return unauthenticated
                await _tokenStorage.ClearTokensAsync(cancellationToken);
                return AuthResult.Succeeded(AuthState.CreateUnauthenticated());
            }

            return await HandleAuthResponseAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error during get current user");
            return AuthResult.NetworkError();
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "Timeout during get current user");
            return AuthResult.Timeout();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during get current user");
            return AuthResult.Failed("An unexpected error occurred.", AuthErrorCode.Unknown);
        }
    }

    public async Task<bool> LogoutAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await _tokenStorage.GetAccessTokenAsync(cancellationToken);
            if (!string.IsNullOrEmpty(accessToken))
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/logout");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                // Best-effort logout on server
                try
                {
                    await _httpClient.SendAsync(request, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Server logout failed, continuing with local logout");
                }
            }

            // Always clear local tokens
            await _tokenStorage.ClearTokensAsync(cancellationToken);
            _logger.LogInformation("User logged out successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during logout");
            // Clear tokens anyway on error
            await _tokenStorage.ClearTokensAsync(cancellationToken);
            return false;
        }
    }

    public async Task<AuthResult> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tokens = await _tokenStorage.GetTokensAsync(cancellationToken);
            if (tokens == null)
            {
                return AuthResult.Failed("No refresh token available", AuthErrorCode.SessionExpired);
            }

            var response = await _httpClient.PostAsJsonAsync(
                "/auth/refresh",
                new { refreshToken = tokens.RefreshToken },
                JsonOptions,
                cancellationToken
            );

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Refresh token invalid, clear tokens
                await _tokenStorage.ClearTokensAsync(cancellationToken);
                return AuthResult.Failed("Session expired. Please log in again.", AuthErrorCode.SessionExpired);
            }

            return await HandleAuthResponseAsync(response, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error during token refresh");
            return AuthResult.NetworkError();
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogWarning(ex, "Timeout during token refresh");
            return AuthResult.Timeout();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during token refresh");
            return AuthResult.Failed("An unexpected error occurred.", AuthErrorCode.Unknown);
        }
    }

    private async Task<AuthResult> HandleAuthResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            try
            {
                var authResponse = JsonSerializer.Deserialize<AuthApiResponse>(content, JsonOptions);
                if (authResponse == null)
                {
                    return AuthResult.Failed("Invalid response from server", AuthErrorCode.ServerError);
                }

                // Store tokens if provided
                if (authResponse.Tokens != null)
                {
                    var tokenData = new TokenData
                    {
                        AccessToken = authResponse.Tokens.AccessToken,
                        RefreshToken = authResponse.Tokens.RefreshToken,
                        ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(authResponse.Tokens.ExpiresIn)
                    };
                    await _tokenStorage.StoreTokensAsync(tokenData, cancellationToken);
                }

                // Build user info
                if (authResponse.User == null)
                {
                    return AuthResult.Failed("Invalid user data from server", AuthErrorCode.ServerError);
                }

                var userInfo = new UserInfo
                {
                    Id = authResponse.User.Id,
                    Email = authResponse.User.Email,
                    FullName = authResponse.User.FullName,
                    IsApproved = authResponse.User.IsApproved,
                    CreatedAt = authResponse.User.CreatedAt
                };

                // Determine auth state based on approval status
                var state = userInfo.IsApproved
                    ? AuthState.CreateAuthenticated(userInfo)
                    : AuthState.CreatePendingApproval(userInfo);

                _logger.LogInformation(
                    "Auth successful for {Email}, approved: {IsApproved}",
                    userInfo.Email,
                    userInfo.IsApproved
                );

                return AuthResult.Succeeded(state);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "Failed to parse auth response");
                return AuthResult.Failed("Invalid response from server", AuthErrorCode.ServerError);
            }
        }

        // Handle error responses
        return await HandleErrorResponseAsync(response.StatusCode, content);
    }

    private Task<AuthResult> HandleErrorResponseAsync(HttpStatusCode statusCode, string content)
    {
        try
        {
            var errorResponse = JsonSerializer.Deserialize<AuthErrorResponse>(content, JsonOptions);
            var errorMessage = errorResponse?.Message ?? "An error occurred";
            var errorCode = MapErrorCode(errorResponse?.Code, statusCode);

            _logger.LogWarning("Auth error: {StatusCode} - {Message}", statusCode, errorMessage);

            return Task.FromResult(AuthResult.Failed(errorMessage, errorCode));
        }
        catch (JsonException)
        {
            // Fallback for non-JSON error responses
            var (message, code) = statusCode switch
            {
                HttpStatusCode.Unauthorized => ("Invalid credentials", AuthErrorCode.InvalidCredentials),
                HttpStatusCode.Forbidden => ("Access denied", AuthErrorCode.AccountDisabled),
                HttpStatusCode.BadRequest => ("Invalid request", AuthErrorCode.ValidationError),
                HttpStatusCode.Conflict => ("Email already registered", AuthErrorCode.EmailAlreadyExists),
                HttpStatusCode.InternalServerError => ("Server error. Please try again later.", AuthErrorCode.ServerError),
                HttpStatusCode.ServiceUnavailable => ("Service unavailable. Please try again later.", AuthErrorCode.ServerError),
                _ => ("An error occurred", AuthErrorCode.Unknown)
            };

            return Task.FromResult(AuthResult.Failed(message, code));
        }
    }

    private static AuthErrorCode MapErrorCode(string? code, HttpStatusCode statusCode)
    {
        return code?.ToUpperInvariant() switch
        {
            "INVALID_CREDENTIALS" => AuthErrorCode.InvalidCredentials,
            "ACCOUNT_PENDING_APPROVAL" => AuthErrorCode.AccountPendingApproval,
            "ACCOUNT_DISABLED" => AuthErrorCode.AccountDisabled,
            "EMAIL_EXISTS" => AuthErrorCode.EmailAlreadyExists,
            "WEAK_PASSWORD" => AuthErrorCode.WeakPassword,
            "SESSION_EXPIRED" => AuthErrorCode.SessionExpired,
            "VALIDATION_ERROR" => AuthErrorCode.ValidationError,
            _ => statusCode switch
            {
                HttpStatusCode.Unauthorized => AuthErrorCode.InvalidCredentials,
                HttpStatusCode.Forbidden => AuthErrorCode.AccountDisabled,
                HttpStatusCode.Conflict => AuthErrorCode.EmailAlreadyExists,
                _ => AuthErrorCode.Unknown
            }
        };
    }

    #region API Response Models

    private sealed class AuthApiResponse
    {
        public UserApiResponse? User { get; set; }
        public TokensApiResponse? Tokens { get; set; }
    }

    private sealed class UserApiResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class TokensApiResponse
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
    }

    private sealed class AuthErrorResponse
    {
        public string? Message { get; set; }
        public string? Code { get; set; }
    }

    #endregion
}
