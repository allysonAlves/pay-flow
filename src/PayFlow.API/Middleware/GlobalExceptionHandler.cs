using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PayFlow.Domain.Exceptions;

namespace PayFlow.API.Middleware;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            _logger.LogInformation("Request cancelled by client");
            httpContext.Response.StatusCode = 499;
            return true;
        }

        httpContext.Response.ContentType = "application/problem+json";

        if (exception is ValidationException validationEx)
        {
            var errors = validationEx.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());

            httpContext.Response.StatusCode = 400;
            await httpContext.Response.WriteAsJsonAsync(
                new ValidationProblemDetails(errors)
                {
                    Status = 400,
                    Title = "Validation Error",
                    Instance = httpContext.Request.Path
                },
                cancellationToken);
            return true;
        }

        var (statusCode, title, detail) = exception switch
        {
            DomainException ex      => (422, "Domain Rule Violation", ex.Message),
            KeyNotFoundException ex => (404, "Not Found", ex.Message),
            _                       => (500, "Internal Server Error", "An unexpected error occurred.")
        };

        if (statusCode == 500)
            _logger.LogError(exception, "Unhandled exception");

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = statusCode,
                Title = title,
                Detail = detail,
                Instance = httpContext.Request.Path
            },
            cancellationToken);

        return true;
    }
}
