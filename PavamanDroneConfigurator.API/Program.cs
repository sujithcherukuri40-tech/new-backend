using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PavamanDroneConfigurator.API.Data;
using PavamanDroneConfigurator.API.Middleware;
using PavamanDroneConfigurator.API.Services;
using DotNetEnv;

var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    Env.Load(envPath);
    Console.WriteLine("? Loaded environment variables from .env file");
}
else
{
    Console.WriteLine("??  No .env file found, using system environment variables or AWS Secrets Manager");
}

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

// Register AWS Secrets Manager Service
builder.Services.AddSingleton<AwsSecretsManagerService>();

// Build connection string with AWS Secrets Manager fallback
var connectionString = await BuildConnectionStringAsync(builder.Configuration, builder.Services.BuildServiceProvider());

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException(
        "Database connection string not configured. " +
        "Set DB_HOST, DB_NAME, DB_USER, and DB_PASSWORD environment variables, " +
        "configure ConnectionStrings__PostgresDb, or use AWS Secrets Manager secret: drone-configurator/postgres");
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
        .EnableSensitiveDataLogging(builder.Environment.IsDevelopment())
);

var jwtSettings = builder.Configuration.GetSection("Jwt");

// Get JWT secret with AWS Secrets Manager fallback
var secretKey = await GetJwtSecretKeyAsync(builder.Configuration, builder.Services.BuildServiceProvider());

if (string.IsNullOrEmpty(secretKey) || secretKey.Contains("DEVELOPMENT_ONLY") || secretKey.Contains("REPLACE") || secretKey.Length < 32)
{
    throw new InvalidOperationException(
        "JWT secret key not configured or is insecure. " +
        "Set JWT_SECRET_KEY environment variable with a secure random key (minimum 32 characters), " +
        "or use AWS Secrets Manager secret: drone-configurator/jwt-secret");
}

var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") 
    ?? jwtSettings["Issuer"] 
    ?? "DroneConfigurator";

var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") 
    ?? jwtSettings["Audience"] 
    ?? "DroneConfiguratorClient";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = issuer,
        ValidAudience = audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey)),
        ClockSkew = TimeSpan.Zero
    };

    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            if (context.Exception is SecurityTokenExpiredException)
            {
                context.Response.Headers.Append("Token-Expired", "true");
            }
            return Task.CompletedTask;
        }
    };
});

builder.Services.AddAuthorization();

builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAdminService, AdminService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Drone Configurator Auth API",
        Version = "v1",
        Description = "Authentication API for Pavaman Drone Configurator"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowDesktopApp", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(
    builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);

var app = builder.Build();

app.UseExceptionMiddleware();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Auth API v1");
        options.RoutePrefix = "swagger";
    });
}

app.UseCors("AllowDesktopApp");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
   .WithName("HealthCheck")
   .WithTags("Health");

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    
    try
    {
        await dbContext.Database.MigrateAsync();
        app.Logger.LogInformation("Database migrations applied successfully");
        
        // Seed default admin user
        await DatabaseSeeder.SeedAsync(dbContext, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error applying database migrations");
        throw;
    }
}

app.Logger.LogInformation("Drone Configurator Auth API starting...");
app.Logger.LogInformation("Database: {Host}", GetDatabaseHost(connectionString));
app.Logger.LogInformation("JWT Issuer: {Issuer}", issuer);
app.Logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

app.Run();

static async Task<string?> BuildConnectionStringAsync(IConfiguration configuration, IServiceProvider services)
{
    // Priority 1: Full connection string from environment variable
    var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__PostgresDb");
    if (!string.IsNullOrEmpty(connStr))
    {
        Console.WriteLine("? Using database connection string from environment variable");
        return connStr;
    }
    
    // Priority 2: Individual environment variables
    var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
    var dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
    var dbName = Environment.GetEnvironmentVariable("DB_NAME");
    var dbUser = Environment.GetEnvironmentVariable("DB_USER");
    var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");
    var dbSslMode = Environment.GetEnvironmentVariable("DB_SSL_MODE") ?? "Prefer";
    
    if (!string.IsNullOrEmpty(dbHost) && !string.IsNullOrEmpty(dbName) 
        && !string.IsNullOrEmpty(dbUser) && !string.IsNullOrEmpty(dbPassword))
    {
        Console.WriteLine("? Using database connection from individual environment variables");
        return $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword};Ssl Mode={dbSslMode}";
    }
    
    // Priority 3: AWS Secrets Manager
    var awsSecretName = Environment.GetEnvironmentVariable("AWS_SECRETS_MANAGER_DB_SECRET") 
        ?? configuration["AWS:Secrets:DatabaseSecret"]
        ?? "drone-configurator/postgres";
    
    try
    {
        var secretsManager = services.GetService<AwsSecretsManagerService>();
        if (secretsManager != null)
        {
            var awsConnectionString = await secretsManager.GetDatabaseConnectionStringAsync(awsSecretName);
            if (!string.IsNullOrEmpty(awsConnectionString))
            {
                Console.WriteLine($"? Using database connection from AWS Secrets Manager: {awsSecretName}");
                return awsConnectionString;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"??  Failed to retrieve database secret from AWS Secrets Manager: {ex.Message}");
    }
    
    // Priority 4: appsettings.json (fallback)
    var appSettingsConnStr = configuration.GetConnectionString("PostgresDb");
    if (!string.IsNullOrEmpty(appSettingsConnStr))
    {
        Console.WriteLine("??  Using database connection from appsettings.json (not recommended for production)");
        return appSettingsConnStr;
    }
    
    return null;
}

static async Task<string?> GetJwtSecretKeyAsync(IConfiguration configuration, IServiceProvider services)
{
    // Priority 1: Environment variable
    var secretKey = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
    if (!string.IsNullOrEmpty(secretKey) && !secretKey.Contains("REPLACE"))
    {
        Console.WriteLine("? Using JWT secret from environment variable");
        return secretKey;
    }
    
    // Priority 2: AWS Secrets Manager
    var awsSecretName = Environment.GetEnvironmentVariable("AWS_SECRETS_MANAGER_JWT_SECRET") 
        ?? configuration["AWS:Secrets:JwtSecret"]
        ?? "drone-configurator/jwt-secret";
    
    try
    {
        var secretsManager = services.GetService<AwsSecretsManagerService>();
        if (secretsManager != null)
        {
            var awsSecret = await secretsManager.GetSecretAsync(awsSecretName);
            if (!string.IsNullOrEmpty(awsSecret))
            {
                Console.WriteLine($"? Using JWT secret from AWS Secrets Manager: {awsSecretName}");
                return awsSecret;
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"??  Failed to retrieve JWT secret from AWS Secrets Manager: {ex.Message}");
    }
    
    // Priority 3: appsettings.json (fallback)
    var jwtSettings = configuration.GetSection("Jwt");
    var appSettingsSecret = jwtSettings["SecretKey"];
    if (!string.IsNullOrEmpty(appSettingsSecret) && !appSettingsSecret.Contains("REPLACE"))
    {
        Console.WriteLine("??  Using JWT secret from appsettings.json (not recommended for production)");
        return appSettingsSecret;
    }
    
    return null;
}

static string GetDatabaseHost(string connectionString)
{
    try
    {
        var hostPart = connectionString.Split(';').FirstOrDefault(x => x.Trim().StartsWith("Host=", StringComparison.OrdinalIgnoreCase));
        return hostPart?.Split('=')[1].Trim() ?? "unknown";
    }
    catch
    {
        return "unknown";
    }
}
