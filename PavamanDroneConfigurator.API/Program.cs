using System.Text;
using System.Threading.RateLimiting;
using Amazon;
using Amazon.SimpleEmail;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PavamanDroneConfigurator.API.Data;
using PavamanDroneConfigurator.API.Middleware;
using PavamanDroneConfigurator.API.Services;
using PavamanDroneConfigurator.Core.Interfaces;
using PavamanDroneConfigurator.Infrastructure.Services.Aws;
using DotNetEnv;

// Load .env file if exists (for local development)
var envPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(envPath))
{
    Env.Load(envPath);
    Console.WriteLine("[OK] Loaded .env file");
}

var builder = WebApplication.CreateBuilder(args);

// Configuration sources
builder.Configuration
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables();

// Register services
builder.Services.AddSingleton<AwsSecretsManagerService>();

// Get configuration values
var connectionString = GetConnectionString(builder);
var secretKey = GetJwtSecret(builder);

// Validate required configuration
if (string.IsNullOrEmpty(connectionString))
{
    Console.WriteLine("================================================================");
    Console.WriteLine("[ERROR] DATABASE NOT CONFIGURED");
    Console.WriteLine("================================================================");
    Console.WriteLine("Set environment variables:");
    Console.WriteLine("  DB_HOST=your-rds-endpoint");
    Console.WriteLine("  DB_NAME=drone_configurator");
    Console.WriteLine("  DB_USER=postgres");
    Console.WriteLine("  DB_PASSWORD=your-password");
    Console.WriteLine("Or: ConnectionStrings__PostgresDb=Host=...;Database=...;...");
    Console.WriteLine("================================================================");
    throw new InvalidOperationException("Database connection not configured");
}

if (string.IsNullOrEmpty(secretKey) || secretKey.Length < 32)
{
    Console.WriteLine("================================================================");
    Console.WriteLine("[ERROR] JWT SECRET NOT CONFIGURED");
    Console.WriteLine("================================================================");
    Console.WriteLine("Set environment variable:");
    Console.WriteLine("  JWT_SECRET_KEY=<random-string-at-least-32-characters>");
    Console.WriteLine("Generate with: openssl rand -base64 48");
    Console.WriteLine("================================================================");
    throw new InvalidOperationException("JWT secret not configured (minimum 32 characters)");
}

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString)
        .EnableSensitiveDataLogging(builder.Environment.IsDevelopment() &&
            Environment.GetEnvironmentVariable("ENABLE_SENSITIVE_LOGGING") == "true"));

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var issuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? jwtSettings["Issuer"] ?? "DroneConfigurator";
var audience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? jwtSettings["Audience"] ?? "DroneConfiguratorClient";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
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
                    context.Response.Headers.Append("Token-Expired", "true");
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddFixedWindowLimiter("fixed", limiter =>
    {
        limiter.PermitLimit = 100;
        limiter.Window = TimeSpan.FromSeconds(60);
        limiter.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiter.QueueLimit = 10;
    });

    options.AddFixedWindowLimiter("auth", limiter =>
    {
        limiter.PermitLimit = 10;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 2;
    });

    options.AddFixedWindowLimiter("admin", limiter =>
    {
        limiter.PermitLimit = 30;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 5;
    });
});

// Application Services
builder.Services.AddScoped<PavamanDroneConfigurator.API.Services.ITokenService, TokenService>();
builder.Services.AddScoped<PavamanDroneConfigurator.API.Services.IAuthService, AuthService>();
builder.Services.AddScoped<PavamanDroneConfigurator.API.Services.IAdminService, AdminService>();
builder.Services.AddScoped<PavamanDroneConfigurator.Core.Interfaces.IParamLockService, ParamLockService>();
builder.Services.AddSingleton<IAmazonSimpleEmailService>(_ =>
{
    var region = builder.Configuration["AWS:Region"]
        ?? Environment.GetEnvironmentVariable("AWS_REGION")
        ?? "ap-south-1";
    return new AmazonSimpleEmailServiceClient(RegionEndpoint.GetBySystemName(region));
});
builder.Services.AddScoped<PavamanDroneConfigurator.Core.Interfaces.IEmailService, SesEmailService>();
builder.Services.AddSingleton<PavamanDroneConfigurator.Infrastructure.Services.AwsS3Service>();

// Controllers & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Drone Configurator API",
        Version = "v1",
        Description = "Authentication API for Pavaman Drone Configurator"
    });
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Enter 'Bearer' [space] and your token.",
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
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowDesktopApp", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
        }
        else
        {
            var origins = Environment.GetEnvironmentVariable("ALLOWED_ORIGINS")?.Split(',')
                ?? new[] { "http://localhost:5000", "https://localhost:5001" };
            policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader().AllowCredentials();
        }
    });
});

// Logging
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(builder.Environment.IsDevelopment() ? LogLevel.Debug : LogLevel.Information);

var app = builder.Build();

// Middleware pipeline
app.UseExceptionMiddleware();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "API v1"));
}

// Security headers
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    if (!app.Environment.IsDevelopment())
        context.Response.Headers.Append("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
    await next();
});

app.UseCors("AllowDesktopApp");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// Health check endpoint
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck").WithTags("Health");

// Database migration and seeding
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await dbContext.Database.MigrateAsync();
        app.Logger.LogInformation("[OK] Database migrations applied");
        await DatabaseSeeder.SeedAsync(dbContext, app.Logger);
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "[ERROR] Database migration failed");
        throw;
    }
}

// Startup info
app.Logger.LogInformation("================================================================");
app.Logger.LogInformation("[OK] Drone Configurator API Starting");
app.Logger.LogInformation("     Database: {Host}", GetDatabaseHost(connectionString));
app.Logger.LogInformation("     JWT Issuer: {Issuer}", issuer);
app.Logger.LogInformation("     Environment: {Env}", app.Environment.EnvironmentName);
app.Logger.LogInformation("================================================================");

app.Run();

// ============================================================================
// Helper Methods
// ============================================================================

static string? GetConnectionString(WebApplicationBuilder builder)
{
    // Priority 1: Full connection string from environment
    var connStr = Environment.GetEnvironmentVariable("ConnectionStrings__PostgresDb");
    if (!string.IsNullOrEmpty(connStr))
    {
        Console.WriteLine("[OK] Using ConnectionStrings__PostgresDb");
        return connStr;
    }

    // Priority 2: Individual environment variables
    var host = Environment.GetEnvironmentVariable("DB_HOST");
    var name = Environment.GetEnvironmentVariable("DB_NAME");
    var user = Environment.GetEnvironmentVariable("DB_USER");
    var pass = Environment.GetEnvironmentVariable("DB_PASSWORD");
    var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
    var ssl = Environment.GetEnvironmentVariable("DB_SSL_MODE") ?? "Prefer";

    if (!string.IsNullOrEmpty(host) && !string.IsNullOrEmpty(name) &&
        !string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(pass))
    {
        Console.WriteLine("[OK] Using DB_HOST/DB_NAME/DB_USER/DB_PASSWORD");
        return $"Host={host};Port={port};Database={name};Username={user};Password={pass};Ssl Mode={ssl}";
    }

    // Priority 3: appsettings.json
    var appSettings = builder.Configuration.GetConnectionString("PostgresDb");
    if (!string.IsNullOrEmpty(appSettings))
    {
        Console.WriteLine("[OK] Using appsettings.json connection string");
        return appSettings;
    }

    return null;
}

static string? GetJwtSecret(WebApplicationBuilder builder)
{
    // Priority 1: Environment variable
    var key = Environment.GetEnvironmentVariable("JWT_SECRET_KEY");
    if (!string.IsNullOrEmpty(key) && key.Length >= 32 && !key.Contains("REPLACE"))
    {
        Console.WriteLine("[OK] Using JWT_SECRET_KEY from environment");
        return key;
    }

    // Priority 2: appsettings.json
    var appKey = builder.Configuration.GetSection("Jwt")["SecretKey"];
    if (!string.IsNullOrEmpty(appKey) && appKey.Length >= 32 && !appKey.Contains("REPLACE"))
    {
        Console.WriteLine("[OK] Using JWT from appsettings.json");
        return appKey;
    }

    return null;
}

static string GetDatabaseHost(string connectionString)
{
    try
    {
        var hostPart = connectionString.Split(';')
            .FirstOrDefault(x => x.Trim().StartsWith("Host=", StringComparison.OrdinalIgnoreCase));
        return hostPart?.Split('=')[1].Trim() ?? "unknown";
    }
    catch
    {
        return "unknown";
    }
}
