using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace PavamanDroneConfigurator.Infrastructure.Services.Auth;

/// <summary>
/// API service for managing parameter locks.
/// </summary>
public class ParamLockApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ParamLockApiService> _logger;

    public ParamLockApiService(HttpClient httpClient, ILogger<ParamLockApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Create a new parameter lock.
    /// </summary>
    public async Task<ParamLockResponse?> CreateLockAsync(CreateParamLockRequest request, string token)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.PostAsJsonAsync("/admin/parameter-locks", request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ParamLockResponse>();
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to create parameter lock: {StatusCode} - {Error}", response.StatusCode, error);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception creating parameter lock");
            return null;
        }
    }

    /// <summary>
    /// Update an existing parameter lock.
    /// </summary>
    public async Task<ParamLockResponse?> UpdateLockAsync(UpdateParamLockRequest request, string token)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.PutAsJsonAsync("/admin/parameter-locks", request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<ParamLockResponse>();
            }

            var error = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to update parameter lock: {StatusCode} - {Error}", response.StatusCode, error);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception updating parameter lock");
            return null;
        }
    }

    /// <summary>
    /// Delete a parameter lock.
    /// </summary>
    public async Task<bool> DeleteLockAsync(int lockId, string token)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.DeleteAsync($"/admin/parameter-locks/{lockId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception deleting parameter lock {LockId}", lockId);
            return false;
        }
    }

    /// <summary>
    /// Get all parameter locks (admin view).
    /// </summary>
    public async Task<List<ParamLockInfo>> GetAllLocksAsync(string token)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.GetAsync("/admin/parameter-locks");

            if (response.IsSuccessStatusCode)
            {
                var locks = await response.Content.ReadFromJsonAsync<List<ParamLockInfo>>();
                return locks ?? new List<ParamLockInfo>();
            }

            _logger.LogError("Failed to get all locks: {StatusCode}", response.StatusCode);
            return new List<ParamLockInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting all parameter locks");
            return new List<ParamLockInfo>();
        }
    }

    /// <summary>
    /// Get parameter locks for a specific user.
    /// </summary>
    public async Task<List<ParamLockInfo>> GetUserLocksAsync(Guid userId, string token)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var response = await _httpClient.GetAsync($"/admin/parameter-locks/user/{userId}");

            if (response.IsSuccessStatusCode)
            {
                var locks = await response.Content.ReadFromJsonAsync<List<ParamLockInfo>>();
                return locks ?? new List<ParamLockInfo>();
            }

            _logger.LogError("Failed to get user locks: {StatusCode}", response.StatusCode);
            return new List<ParamLockInfo>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception getting user locks for {UserId}", userId);
            return new List<ParamLockInfo>();
        }
    }

    /// <summary>
    /// Check which parameters are locked for a user/device.
    /// </summary>
    public async Task<LockedParamsResponse?> CheckLockedParamsAsync(Guid userId, string? deviceId, string token)
    {
        try
        {
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");

            var request = new CheckLockedParamsRequest
            {
                UserId = userId,
                DeviceId = deviceId
            };

            var response = await _httpClient.PostAsJsonAsync("/admin/parameter-locks/check", request);

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<LockedParamsResponse>();
            }

            _logger.LogError("Failed to check locked params: {StatusCode}", response.StatusCode);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception checking locked params for user {UserId}", userId);
            return null;
        }
    }
}

#region DTOs

public class CreateParamLockRequest
{
    public Guid UserId { get; set; }
    public string? DeviceId { get; set; }
    public List<string> Params { get; set; } = new();
}

public class UpdateParamLockRequest
{
    public int LockId { get; set; }
    public List<string> Params { get; set; } = new();
}

public class ParamLockResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? S3Key { get; set; }
    public int? LockId { get; set; }
    public int ParamCount { get; set; }
}

public class CheckLockedParamsRequest
{
    public Guid UserId { get; set; }
    public string? DeviceId { get; set; }
}

public class LockedParamsResponse
{
    public Guid UserId { get; set; }
    public string? DeviceId { get; set; }
    public List<string> LockedParams { get; set; } = new();
    public int Count { get; set; }
}

public class ParamLockInfo
{
    public int Id { get; set; }
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public string? UserEmail { get; set; }
    public string? DeviceId { get; set; }
    public int ParamCount { get; set; }
    public List<string> LockedParams { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; }
    public Guid CreatedBy { get; set; }
    public string? CreatedByName { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public bool IsActive { get; set; }
}

#endregion
