using Amazon;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using System.Text.Json;

namespace PavamanDroneConfigurator.API.Services;

/// <summary>
/// Service for retrieving secrets from AWS Secrets Manager.
/// </summary>
public class AwsSecretsManagerService
{
    private readonly ILogger<AwsSecretsManagerService> _logger;
    private readonly string _region;

    public AwsSecretsManagerService(ILogger<AwsSecretsManagerService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _region = Environment.GetEnvironmentVariable("AWS_REGION") ?? configuration["AWS:Region"] ?? "ap-south-1";
    }

    /// <summary>
    /// Retrieves a secret from AWS Secrets Manager.
    /// </summary>
    /// <param name="secretName">Name or ARN of the secret.</param>
    /// <returns>Secret value as string, or null if not found.</returns>
    public async Task<string?> GetSecretAsync(string secretName)
    {
        try
        {
            var client = new AmazonSecretsManagerClient(RegionEndpoint.GetBySystemName(_region));

            var request = new GetSecretValueRequest
            {
                SecretId = secretName
            };

            var response = await client.GetSecretValueAsync(request);

            if (response.SecretString != null)
            {
                _logger.LogInformation("Successfully retrieved secret: {SecretName}", secretName);
                return response.SecretString;
            }

            _logger.LogWarning("Secret {SecretName} has no string value", secretName);
            return null;
        }
        catch (ResourceNotFoundException)
        {
            _logger.LogWarning("Secret {SecretName} not found in AWS Secrets Manager", secretName);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving secret {SecretName} from AWS Secrets Manager", secretName);
            return null;
        }
    }

    /// <summary>
    /// Retrieves a secret and deserializes it as JSON.
    /// </summary>
    public async Task<T?> GetSecretAsJsonAsync<T>(string secretName) where T : class
    {
        var secretValue = await GetSecretAsync(secretName);
        if (string.IsNullOrEmpty(secretValue))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(secretValue);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialize secret {SecretName} as JSON", secretName);
            return null;
        }
    }

    /// <summary>
    /// Retrieves database connection details from AWS Secrets Manager.
    /// Expected JSON format in secret:
    /// {
    ///   "host": "your-db-host",
    ///   "port": "5432",
    ///   "database": "your-db-name",
    ///   "username": "your-username",
    ///   "password": "your-password"
    /// }
    /// </summary>
    public async Task<string?> GetDatabaseConnectionStringAsync(string secretName)
    {
        var secret = await GetSecretAsJsonAsync<DatabaseSecret>(secretName);
        if (secret == null)
        {
            return null;
        }

        return $"Host={secret.Host};Port={secret.Port ?? "5432"};Database={secret.Database};Username={secret.Username};Password={secret.Password};Ssl Mode=Require";
    }

    private class DatabaseSecret
    {
        public string Host { get; set; } = string.Empty;
        public string? Port { get; set; }
        public string Database { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
