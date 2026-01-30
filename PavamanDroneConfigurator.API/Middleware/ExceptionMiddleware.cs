using System.Net;
using System.Text.Json;
using PavamanDroneConfigurator.API.DTOs;
using PavamanDroneConfigurator.API.Exceptions;

namespace PavamanDroneConfigurator.API.Middleware;

/// <summary>
/// Global exception handling middleware.
/// Catches unhandled exceptions and returns consistent error responses.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;
    private readonly IHostEnvironment _env;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var response = exception switch
        {
            AuthException authEx => new ExceptionResponse(
                authEx.StatusCode,
                authEx.Message,
                authEx.Code),

            UnauthorizedAccessException => new ExceptionResponse(
                (int)HttpStatusCode.Unauthorized,
                "Unauthorized access",
                "UNAUTHORIZED"),

            KeyNotFoundException => new ExceptionResponse(
                (int)HttpStatusCode.NotFound,
                "Resource not found",
                "NOT_FOUND"),

            ArgumentException argEx => new ExceptionResponse(
                (int)HttpStatusCode.BadRequest,
                argEx.Message,
                "VALIDATION_ERROR"),

            _ => new ExceptionResponse(
                (int)HttpStatusCode.InternalServerError,
                _env.IsDevelopment() ? exception.Message : "An internal error occurred",
                "SERVER_ERROR")
        };

        // Log the exception
        if (response.StatusCode >= 500)
        {
            _logger.LogError(exception, "Unhandled exception: {Message}", exception.Message);
        }
        else
        {
            _logger.LogWarning("Handled exception: {Code} - {Message}", response.Code, response.Message);
        }

        context.Response.StatusCode = response.StatusCode;

        var errorResponse = new ErrorResponse
        {
            Message = response.Message,
            Code = response.Code
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(errorResponse, options));
    }

    private record ExceptionResponse(int StatusCode, string Message, string Code);
}

/// <summary>
/// Extension methods for exception middleware.
/// </summary>
public static class ExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseExceptionMiddleware(this IApplicationBuilder app)
    {
        return app.UseMiddleware<ExceptionMiddleware>();
    }
}
