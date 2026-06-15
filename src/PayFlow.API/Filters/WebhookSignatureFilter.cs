using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace PayFlow.API.Filters;

public class WebhookSignatureFilter : IAsyncActionFilter
{
    private readonly IConfiguration _configuration;

    public WebhookSignatureFilter(IConfiguration configuration)
        => _configuration = configuration;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var request = context.HttpContext.Request;
        request.EnableBuffering();

        using var reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync();
        request.Body.Position = 0;

        if (!request.Headers.TryGetValue("X-Webhook-Signature", out var receivedSignature))
        {
            context.Result = new UnauthorizedObjectResult(new ProblemDetails
            {
                Status = 401,
                Title = "Unauthorized",
                Detail = "Missing X-Webhook-Signature header."
            });
            return;
        }

        var secret = _configuration["Webhook:Secret"] ?? string.Empty;
        var expectedSignature = ComputeHmacSha256(secret, body);

        byte[] receivedBytes;
        try
        {
            receivedBytes = Convert.FromHexString(receivedSignature.ToString());
        }
        catch (FormatException)
        {
            context.Result = new UnauthorizedObjectResult(new ProblemDetails
            {
                Status = 401,
                Title = "Unauthorized",
                Detail = "Invalid webhook signature format."
            });
            return;
        }

        var expectedBytes = Convert.FromHexString(expectedSignature);

        if (!CryptographicOperations.FixedTimeEquals(receivedBytes, expectedBytes))
        {
            context.Result = new UnauthorizedObjectResult(new ProblemDetails
            {
                Status = 401,
                Title = "Unauthorized",
                Detail = "Invalid webhook signature."
            });
            return;
        }

        await next();
    }

    private static string ComputeHmacSha256(string secret, string body)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        using var hmac = new HMACSHA256(secretBytes);
        return Convert.ToHexString(hmac.ComputeHash(bodyBytes)).ToLowerInvariant();
    }
}
