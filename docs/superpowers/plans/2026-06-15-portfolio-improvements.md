# Portfolio Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Adicionar autenticação por API Key, validação HMAC no webhook, separar DTO do command, corrigir UpdateAsync, adicionar health checks e README para deixar o projeto pronto para portfolio.

**Architecture:** `ApiKeyMiddleware` protege todos os endpoints exceto `/payments/webhook`, que é protegido pelo `WebhookSignatureFilter` via HMAC-SHA256. `CreatePaymentRequest` desacopla o contrato HTTP do command de Application. Health checks expõem `/health` com status de Postgres e Redis.

**Tech Stack:** ASP.NET Core 8, EF Core 8, StackExchange.Redis, xUnit, FluentAssertions 8.10.0, NSubstitute 5.3.0, `AspNetCore.HealthChecks.Redis`

---

## File Map

**Criar:**
- `src/PayFlow.API/Middleware/ApiKeyMiddleware.cs`
- `src/PayFlow.API/Filters/WebhookSignatureFilter.cs`
- `src/PayFlow.API/Requests/CreatePaymentRequest.cs`
- `tests/PayFlow.API.Tests/PayFlow.API.Tests.csproj`
- `tests/PayFlow.API.Tests/Middleware/ApiKeyMiddlewareTests.cs`
- `tests/PayFlow.API.Tests/Filters/WebhookSignatureFilterTests.cs`
- `docker-compose.yml`
- `README.md`

**Modificar:**
- `src/PayFlow.Infrastructure/Persistence/Repositories/PaymentRepository.cs` — remover `Update()` explícito
- `src/PayFlow.API/Controllers/PaymentsController.cs` — usar `CreatePaymentRequest`, aplicar `WebhookSignatureFilter`
- `src/PayFlow.API/Program.cs` — registrar middleware, health checks, `WebhookSignatureFilter`
- `src/PayFlow.Infrastructure/DependencyInjection.cs` — adicionar health checks
- `src/PayFlow.Infrastructure/PayFlow.Infrastructure.csproj` — pacote `AspNetCore.HealthChecks.Redis`
- `src/PayFlow.API/appsettings.json` — seções `Auth` e `Webhook`

---

## Task 1: Fix PaymentRepository.UpdateAsync

**Files:**
- Modify: `src/PayFlow.Infrastructure/Persistence/Repositories/PaymentRepository.cs`

O `_context.Payments.Update(payment)` marca todas as colunas como `Modified`, gerando UPDATE completo. O EF Core já rastreia a entidade ao buscá-la via `FindAsync` no mesmo `DbContext`.

- [ ] **Step 1: Remover chamada explícita de Update**

Substituir o corpo de `UpdateAsync`:

```csharp
public Task UpdateAsync(Payment payment, CancellationToken ct = default)
    => Task.CompletedTask;
```

- [ ] **Step 2: Build para confirmar que compila**

```bash
dotnet build PayFlow.sln -v q
```
Esperado: `Compilação com êxito. 0 Aviso(s) 0 Erro(s)`

- [ ] **Step 3: Commit**

```bash
git add src/PayFlow.Infrastructure/Persistence/Repositories/PaymentRepository.cs
git commit -m "fix: remove redundant EF Update() call — change tracker handles dirty state"
```

---

## Task 2: CreatePaymentRequest DTO

**Files:**
- Create: `src/PayFlow.API/Requests/CreatePaymentRequest.cs`
- Modify: `src/PayFlow.API/Controllers/PaymentsController.cs`

- [ ] **Step 1: Criar o record de request**

`src/PayFlow.API/Requests/CreatePaymentRequest.cs`:
```csharp
namespace PayFlow.API.Requests;

public record CreatePaymentRequest(Guid CustomerId, Guid MerchantId, decimal Amount, string Currency);
```

- [ ] **Step 2: Atualizar o controller para usar o DTO**

Em `src/PayFlow.API/Controllers/PaymentsController.cs`, substituir o action `Create`:

```csharp
using PayFlow.API.Requests;
using PayFlow.Application.Features.Payments.Commands.CreatePayment;
// (demais usings permanecem)

[HttpPost]
[ProducesResponseType(StatusCodes.Status201Created)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
public async Task<IActionResult> Create([FromBody] CreatePaymentRequest request, CancellationToken ct)
{
    var command = new CreatePaymentCommand(request.CustomerId, request.MerchantId, request.Amount, request.Currency);
    var paymentId = await _mediator.Send(command, ct);
    return CreatedAtAction(nameof(GetById), new { id = paymentId }, new { paymentId });
}
```

- [ ] **Step 3: Build + testes**

```bash
dotnet build PayFlow.sln -v q
dotnet test PayFlow.sln -v q
```
Esperado: build e todos os testes passando.

- [ ] **Step 4: Commit**

```bash
git add src/PayFlow.API/Requests/CreatePaymentRequest.cs src/PayFlow.API/Controllers/PaymentsController.cs
git commit -m "refactor: decouple API contract from CreatePaymentCommand via CreatePaymentRequest DTO"
```

---

## Task 3: Adicionar seções Auth e Webhook ao appsettings.json

**Files:**
- Modify: `src/PayFlow.API/appsettings.json`

- [ ] **Step 1: Adicionar as seções**

```json
{
  "ConnectionStrings": {
    "Postgres": "Host=localhost;Port=5432;Database=payflow;Username=postgres;Password=postgres",
    "Redis": "localhost:6379"
  },
  "Auth": {
    "ApiKey": "dev-api-key-change-in-production"
  },
  "Webhook": {
    "Secret": "dev-webhook-secret-change-in-production"
  },
  "Outbox": {
    "IntervalSeconds": 10,
    "BatchSize": 50
  },
  "FakeGateway": {
    "FailureThreshold": 10000,
    "DelayMs": 500
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
```

- [ ] **Step 2: Commit**

```bash
git add src/PayFlow.API/appsettings.json
git commit -m "config: add Auth:ApiKey and Webhook:Secret sections to appsettings"
```

---

## Task 4: Criar projeto de testes PayFlow.API.Tests

**Files:**
- Create: `tests/PayFlow.API.Tests/PayFlow.API.Tests.csproj`

- [ ] **Step 1: Criar o projeto**

```bash
dotnet new xunit -n PayFlow.API.Tests -o tests/PayFlow.API.Tests --framework net8.0
```

- [ ] **Step 2: Substituir o conteúdo do csproj**

`tests/PayFlow.API.Tests/PayFlow.API.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="FluentAssertions" Version="8.10.0" />
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="8.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\PayFlow.API\PayFlow.API.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Remover o arquivo de teste gerado automaticamente**

```bash
rm tests/PayFlow.API.Tests/UnitTest1.cs
```

- [ ] **Step 4: Adicionar à solution**

```bash
dotnet sln PayFlow.sln add tests/PayFlow.API.Tests/PayFlow.API.Tests.csproj
```

- [ ] **Step 5: Build para confirmar que o projeto compila**

```bash
dotnet build PayFlow.sln -v q
```
Esperado: `Compilação com êxito. 0 Aviso(s) 0 Erro(s)`

- [ ] **Step 6: Commit**

```bash
git add tests/PayFlow.API.Tests/ PayFlow.sln
git commit -m "test: add PayFlow.API.Tests project"
```

---

## Task 5: ApiKeyMiddleware (TDD)

**Files:**
- Create: `tests/PayFlow.API.Tests/Middleware/ApiKeyMiddlewareTests.cs`
- Create: `src/PayFlow.API/Middleware/ApiKeyMiddleware.cs`

- [ ] **Step 1: Criar os testes**

`tests/PayFlow.API.Tests/Middleware/ApiKeyMiddlewareTests.cs`:
```csharp
using System.Text.Json;
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
        // sem header X-Api-Key
        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });

        await middleware.InvokeAsync(context);

        nextCalled.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Rodar para confirmar que falha**

```bash
dotnet test tests/PayFlow.API.Tests/ -v q
```
Esperado: erro de compilação — `ApiKeyMiddleware` não existe ainda.

- [ ] **Step 3: Implementar o middleware**

`src/PayFlow.API/Middleware/ApiKeyMiddleware.cs`:
```csharp
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
```

- [ ] **Step 4: Rodar testes para confirmar que passam**

```bash
dotnet test tests/PayFlow.API.Tests/ -v q
```
Esperado: 4 testes passando.

- [ ] **Step 5: Commit**

```bash
git add src/PayFlow.API/Middleware/ApiKeyMiddleware.cs tests/PayFlow.API.Tests/Middleware/ApiKeyMiddlewareTests.cs
git commit -m "feat: add ApiKeyMiddleware with X-Api-Key header validation"
```

---

## Task 6: WebhookSignatureFilter (TDD)

**Files:**
- Create: `tests/PayFlow.API.Tests/Filters/WebhookSignatureFilterTests.cs`
- Create: `src/PayFlow.API/Filters/WebhookSignatureFilter.cs`

- [ ] **Step 1: Criar os testes**

`tests/PayFlow.API.Tests/Filters/WebhookSignatureFilterTests.cs`:
```csharp
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
```

- [ ] **Step 2: Rodar para confirmar que falha**

```bash
dotnet test tests/PayFlow.API.Tests/ -v q
```
Esperado: erro de compilação — `WebhookSignatureFilter` não existe ainda.

- [ ] **Step 3: Implementar o filter**

`src/PayFlow.API/Filters/WebhookSignatureFilter.cs`:
```csharp
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
```

- [ ] **Step 4: Rodar todos os testes**

```bash
dotnet test PayFlow.sln -v q
```
Esperado: todos os testes passando (incluindo os 4 novos do filter).

- [ ] **Step 5: Commit**

```bash
git add src/PayFlow.API/Filters/WebhookSignatureFilter.cs tests/PayFlow.API.Tests/Filters/WebhookSignatureFilterTests.cs
git commit -m "feat: add WebhookSignatureFilter with HMAC-SHA256 validation"
```

---

## Task 7: Registrar segurança no DI e Program.cs

**Files:**
- Modify: `src/PayFlow.API/Program.cs`
- Modify: `src/PayFlow.API/Controllers/PaymentsController.cs`

- [ ] **Step 1: Registrar o filter e o middleware em Program.cs**

Substituir o conteúdo de `src/PayFlow.API/Program.cs`:
```csharp
using Microsoft.OpenApi.Models;
using PayFlow.API.Filters;
using PayFlow.API.Middleware;
using PayFlow.Application;
using PayFlow.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PayFlow API",
        Version = "v1",
        Description = "Payment processing API — DDD, Clean Architecture, CQRS, Outbox Pattern"
    });
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Name = "X-Api-Key",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "ApiKey" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddExceptionHandler<PayFlow.API.Middleware.GlobalExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddScoped<WebhookSignatureFilter>();

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();

app.Run();
```

- [ ] **Step 2: Aplicar o filter no endpoint de webhook**

Em `src/PayFlow.API/Controllers/PaymentsController.cs`, adicionar o using e o atributo no action `Webhook`:

```csharp
using PayFlow.API.Filters;
// (demais usings permanecem)

[HttpPost("webhook")]
[ServiceFilter(typeof(WebhookSignatureFilter))]
[ProducesResponseType(StatusCodes.Status200OK)]
[ProducesResponseType(StatusCodes.Status400BadRequest)]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
[ProducesResponseType(StatusCodes.Status404NotFound)]
public async Task<IActionResult> Webhook([FromBody] WebhookPayload payload, CancellationToken ct)
{
    await _mediator.Send(new WebhookCommand(payload.PaymentId, payload.Success, payload.ErrorMessage), ct);
    return Ok();
}
```

- [ ] **Step 3: Build + testes**

```bash
dotnet build PayFlow.sln -v q
dotnet test PayFlow.sln -v q
```
Esperado: build e todos os testes passando.

- [ ] **Step 4: Commit**

```bash
git add src/PayFlow.API/Program.cs src/PayFlow.API/Controllers/PaymentsController.cs
git commit -m "feat: register ApiKeyMiddleware and WebhookSignatureFilter in the request pipeline"
```

---

## Task 8: Health Checks

**Files:**
- Modify: `src/PayFlow.Infrastructure/PayFlow.Infrastructure.csproj`
- Modify: `src/PayFlow.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Adicionar pacote de health check do Redis**

```bash
dotnet add src/PayFlow.Infrastructure/PayFlow.Infrastructure.csproj package AspNetCore.HealthChecks.Redis --version 8.0.1
```

- [ ] **Step 2: Registrar health checks no DependencyInjection da Infrastructure**

Em `src/PayFlow.Infrastructure/DependencyInjection.cs`, adicionar ao final do método `AddInfrastructure`, antes do `return services`:

```csharp
services.AddHealthChecks()
    .AddDbContextCheck<PaymentDbContext>()
    .AddRedis(configuration.GetConnectionString("Redis")!);
```

O `using` necessário já está disponível via `Microsoft.Extensions.DependencyInjection`.

- [ ] **Step 3: Mapear o endpoint /health em Program.cs**

Em `src/PayFlow.API/Program.cs`, adicionar após `app.MapControllers()`:

```csharp
app.MapHealthChecks("/health");
```

- [ ] **Step 5: Build + testes**

```bash
dotnet build PayFlow.sln -v q
dotnet test PayFlow.sln -v q
```
Esperado: build e todos os testes passando.

- [ ] **Step 6: Commit**

```bash
git add src/PayFlow.Infrastructure/PayFlow.Infrastructure.csproj src/PayFlow.Infrastructure/DependencyInjection.cs src/PayFlow.API/Program.cs
git commit -m "feat: add health checks for Postgres (EF) and Redis at /health"
```

---

## Task 9: docker-compose.yml e README

**Files:**
- Create: `docker-compose.yml`
- Create: `README.md`

- [ ] **Step 1: Criar docker-compose.yml**

`docker-compose.yml` na raiz:
```yaml
services:
  postgres:
    image: postgres:16-alpine
    environment:
      POSTGRES_DB: payflow
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"

  redis:
    image: redis:7-alpine
    ports:
      - "6379:6379"
```

- [ ] **Step 2: Criar README.md**

`README.md` na raiz:
```markdown
# PayFlow

API de processamento de pagamentos construída para demonstrar boas práticas de arquitetura em .NET.

## Padrões e conceitos demonstrados

| Padrão | Onde encontrar |
|---|---|
| Clean Architecture | Separação em `Domain`, `Application`, `Infrastructure`, `API` |
| DDD | `src/PayFlow.Domain/Aggregates/Payments/Payment.cs` |
| CQRS | `src/PayFlow.Application/Features/Payments/Commands` e `Queries` |
| Outbox Pattern | `src/PayFlow.Infrastructure/Outbox/` |
| Idempotência (Redis + DB) | `src/PayFlow.Infrastructure/Outbox/OutboxProcessor.cs` |
| Pipeline Behavior | `src/PayFlow.Application/Common/ValidationBehavior.cs` |
| API Key Auth | `src/PayFlow.API/Middleware/ApiKeyMiddleware.cs` |
| Webhook HMAC-SHA256 | `src/PayFlow.API/Filters/WebhookSignatureFilter.cs` |

## Fluxo principal

```
POST /payments
  └── CreatePaymentCommand
        └── Payment.Create() → outbox[PaymentCreated]
              └── OutboxProcessor → PaymentCreatedIntegrationEvent
                    └── ProcessPaymentCommand → Gateway
                          ├── Approve() → outbox[PaymentApproved] → NotifyCustomer + NotifyMerchant
                          └── Fail()    → outbox[PaymentFailed]   → NotifyCustomer
```

## Como rodar localmente

**Pré-requisitos:** .NET 8 SDK e Docker.

```bash
# 1. Subir Postgres e Redis
docker compose up -d

# 2. Aplicar migrations
dotnet ef database update --project src/PayFlow.Infrastructure --startup-project src/PayFlow.API

# 3. Rodar a API
dotnet run --project src/PayFlow.API
```

Swagger disponível em `https://localhost:{porta}/swagger`.

Health check em `GET /health`.

## Variáveis de ambiente

| Variável | Descrição | Padrão (desenvolvimento) |
|---|---|---|
| `ConnectionStrings__Postgres` | Connection string do Postgres | `Host=localhost;Port=5432;Database=payflow;Username=postgres;Password=postgres` |
| `ConnectionStrings__Redis` | Connection string do Redis | `localhost:6379` |
| `Auth__ApiKey` | Chave para autenticação da API | `dev-api-key-change-in-production` |
| `Webhook__Secret` | Secret para validação HMAC do webhook | `dev-webhook-secret-change-in-production` |

## Autenticação

Todos os endpoints requerem o header `X-Api-Key`. O endpoint `POST /payments/webhook` usa validação de assinatura HMAC-SHA256 via header `X-Webhook-Signature` em vez de API Key — esse é o padrão de gateways de pagamento reais (Stripe, Adyen).

Em produção, `Auth__ApiKey` e `Webhook__Secret` viriam de um secret manager (Azure Key Vault, AWS Secrets Manager).

## Implementações fake

`FakePaymentGateway`, `FakeCustomerNotifier` e `FakeMerchantNotifier` são stubs para fins de demonstração. Em produção seriam substituídos por clientes HTTP para o gateway de pagamento real e por serviços de notificação (e-mail, SMS, push).

## Testes

```bash
dotnet test PayFlow.sln
```

Cobertura: testes unitários de Domain (agregado e value objects), Application (handlers), Infrastructure (OutboxProcessor com cenários de retry, idempotência e dead-letter) e API (middleware e filter de segurança).
```

- [ ] **Step 3: Build + testes finais**

```bash
dotnet build PayFlow.sln -v q
dotnet test PayFlow.sln -v q
```
Esperado: build e todos os testes passando.

- [ ] **Step 4: Commit final**

```bash
git add docker-compose.yml README.md
git commit -m "docs: add README and docker-compose for local development"
```
