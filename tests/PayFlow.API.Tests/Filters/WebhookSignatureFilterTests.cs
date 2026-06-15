using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using PayFlow.API.Filters;

namespace PayFlow.API.Tests.Filters;

public class WebhookSignatureFilterTests
{
    private const string Secret = "test-secret";
    private const string Body = """{"paymentId":"abc123","success":true}""";

    private static WebhookSignatureFilter CreateFilter(string secret = Secret)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Webhook:Secret"] = secret })
            .Build();
        return new WebhookSignatureFilter(config);
    }

    private static string ComputeSignature(string secret, string body)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        using var hmac = new HMACSHA256(secretBytes);
        return Convert.ToHexString(hmac.ComputeHash(bodyBytes)).ToLowerInvariant();
    }

    private static (ActionExecutingContext executingContext, bool[] nextCalled) BuildContext(
        string body, string? signatureHeader)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Body = new MemoryStream(bodyBytes);
        httpContext.Request.ContentLength = bodyBytes.Length;
        httpContext.Response.Body = new MemoryStream();

        if (signatureHeader is not null)
            httpContext.Request.Headers["X-Webhook-Signature"] = signatureHeader;

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executingContext = new ActionExecutingContext(
            actionContext,
            filters: new List<IFilterMetadata>(),
            actionArguments: new Dictionary<string, object?>(),
            controller: new object());

        var nextCalled = new bool[1];
        return (executingContext, nextCalled);
    }

    private static ActionExecutionDelegate BuildNext(bool[] nextCalled, ActionContext actionContext) =>
        () =>
        {
            nextCalled[0] = true;
            return Task.FromResult(new ActionExecutedContext(
                actionContext, new List<IFilterMetadata>(), controller: new object()));
        };

    [Fact]
    public async Task OnActionExecutionAsync_MissingHeader_Returns401()
    {
        var filter = CreateFilter();
        var (context, _) = BuildContext(Body, signatureHeader: null);

        await filter.OnActionExecutionAsync(context, BuildNext(new bool[1], context));

        context.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task OnActionExecutionAsync_InvalidSignature_Returns401()
    {
        var filter = CreateFilter();
        var (context, _) = BuildContext(Body, signatureHeader: "deadbeef");

        await filter.OnActionExecutionAsync(context, BuildNext(new bool[1], context));

        context.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task OnActionExecutionAsync_ValidSignature_CallsNext()
    {
        var filter = CreateFilter();
        var signature = ComputeSignature(Secret, Body);
        var (context, nextCalled) = BuildContext(Body, signatureHeader: signature);

        await filter.OnActionExecutionAsync(context, BuildNext(nextCalled, context));

        nextCalled[0].Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OnActionExecutionAsync_WrongSecret_Returns401()
    {
        var filter = CreateFilter(secret: "correct-secret");
        var signature = ComputeSignature("wrong-secret", Body);
        var (context, _) = BuildContext(Body, signatureHeader: signature);

        await filter.OnActionExecutionAsync(context, BuildNext(new bool[1], context));

        context.Result.Should().BeOfType<UnauthorizedObjectResult>();
    }
}
