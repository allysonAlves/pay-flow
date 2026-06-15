using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using PayFlow.API.Middleware;

namespace PayFlow.API.Tests.Middleware;

public class ApiKeyMiddlewareTests
{
    private static ApiKeyMiddleware CreateMiddleware(RequestDelegate next, string configuredKey = "valid-key")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:ApiKey"] = configuredKey })
            .Build();
        return new ApiKeyMiddleware(next, config);
    }

    [Fact]
    public async Task InvokeAsync_MissingHeader_Returns401()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_WrongKey_Returns401()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.Request.Headers["X-Api-Key"] = "wrong-key";
        var middleware = CreateMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_ValidKey_CallsNext()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Api-Key"] = "valid-key";
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WebhookPath_SkipsAuthAndCallsNext()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/payments/webhook";
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }
}
