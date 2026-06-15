using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace PayFlow.API.Middleware;

public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IConfiguration _configuration;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/payments/webhook"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var receivedKey))
        {
            await WriteUnauthorized(context, "Missing X-Api-Key header.");
            return;
        }

        var configuredKey = _configuration["Auth:ApiKey"] ?? string.Empty;
        var receivedBytes = Encoding.UTF8.GetBytes(receivedKey.ToString());
        var configuredBytes = Encoding.UTF8.GetBytes(configuredKey);

        if (!CryptographicOperations.FixedTimeEquals(receivedBytes, configuredBytes))
        {
            await WriteUnauthorized(context, "Invalid API key.");
            return;
        }

        await _next(context);
    }

    private static async Task WriteUnauthorized(HttpContext context, string detail)
    {
        context.Response.StatusCode = 401;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = 401,
            Title = "Unauthorized",
            Detail = detail
        });
    }
}
