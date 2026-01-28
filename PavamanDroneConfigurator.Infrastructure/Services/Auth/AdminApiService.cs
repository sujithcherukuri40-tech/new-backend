using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Core.Models.Auth;

namespace PavamanDroneConfigurator.Infrastructure.Services.Auth;

/// <summary>
/// Admin service that makes HTTP calls to the admin API endpoints.
/// </summary>
public sealed class AdminApiService : IAdminService
{
    private readonly HttpClient _httpClient;
    private readonly ITokenStorage _tokenStorage;
    private readonly ILogger<AdminApiService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AdminApiService(
        HttpClient httpClient,
        ITokenStorage _tokenStorage,
        ILogger<AdminApiService> logger)
    {
        _httpClient = httpClient;
        this._tokenStorage = _tokenStorage;
        _logger = logger;
    }

    public async Task<AdminUsersResponse> GetAllUsersAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await _tokenStorage.GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new InvalidOperationException("No access token available");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/users");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var apiResponse = await response.Content.ReadFromJsonAsync<UsersListApiResponse>(JsonOptions, cancellationToken);

            if (apiResponse == null)
            {
                throw new InvalidOperationException("Invalid response from server");
            }

            return new AdminUsersResponse
            {
                Users = apiResponse.Users.Select(u => new AdminUserListItem
                {
                    Id = u.Id,
                    FullName = u.FullName,
                    Email = u.Email,
                    IsApproved = u.IsApproved,
                    Role = u.Role,
                    CreatedAt = u.CreatedAt,
                    LastLoginAt = u.LastLoginAt
                }).ToList(),
                TotalCount = apiResponse.TotalCount
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get users list");
            throw;
        }
    }

    public async Task<bool> ApproveUserAsync(string userId, bool approve, CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await _tokenStorage.GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new InvalidOperationException("No access token available");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/admin/users/{userId}/approve");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent.Create(new { approve }, options: JsonOptions);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("User {UserId} approval set to {Approve}", userId, approve);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> ChangeUserRoleAsync(string userId, string newRole, CancellationToken cancellationToken = default)
    {
        try
        {
            var accessToken = await _tokenStorage.GetAccessTokenAsync(cancellationToken);
            if (string.IsNullOrEmpty(accessToken))
            {
                throw new InvalidOperationException("No access token available");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, $"/admin/users/{userId}/role");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent.Create(new { role = newRole }, options: JsonOptions);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("User {UserId} role changed to {Role}", userId, newRole);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change role for user {UserId}", userId);
            return false;
        }
    }

    #region API Response Models

    private sealed class UsersListApiResponse
    {
        public List<UserListItemApiResponse> Users { get; set; } = new();
        public int TotalCount { get; set; }
    }

    private sealed class UserListItemApiResponse
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsApproved { get; set; }
        public string Role { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? LastLoginAt { get; set; }
    }

    #endregion
}
